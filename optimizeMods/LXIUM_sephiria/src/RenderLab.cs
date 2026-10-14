using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LXIUM.Sephiria
{
    internal static class RenderLab
    {
        const double SettleSeconds = 2;
        public static bool Running { get; private set; }
        static Coroutine coroutine;
        static ComparisonResult result;
        static RenderOptions saved;
        static int vsync, limit;
        static string scene;
        static Camera camera;
        static ProfilerRecorder draws, batches, triangles, gc;
        static readonly FrameTiming[] timings = new FrameTiming[1];
        static readonly string[] Names = { "01_original", "02_light_only", "03_idle_passes_only", "04_static_lights_only",
            "05_compatible_batch_only", "06_world_upscale_only", "07_world_upscale_shader", "08_all", "09_configured", "10_original_repeat" };

        public static void Start(Host host)
        {
            if (Running) return;
            if (GameCamera.Instance == null || GameCamera.Instance.Camera == null || !GameCamera.Instance.Camera.isActiveAndEnabled)
            { host.Status = "F10: 게임 장면과 메인 카메라가 준비된 후 시작하세요."; return; }
            saved = host.Config.Options.Copy(); saved.Validate();
            camera = GameCamera.Instance.Camera;
            scene = camera.gameObject.scene.name;
            vsync = QualitySettings.vSyncCount; limit = Application.targetFrameRate;
            string directory = Path.Combine(Host.ModDirectory, "comparisons", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            result = new ComparisonResult
            {
                StartedUtc = DateTime.UtcNow.ToString("o"), Outcome = "running", Directory = directory,
                UnityVersion = Application.unityVersion, GraphicsApi = SystemInfo.graphicsDeviceType.ToString(), Gpu = SystemInfo.graphicsDeviceName,
                GameAssembly = typeof(HorayModBase).Module.ModuleVersionId.ToString(), Width = Screen.width, Height = Screen.height,
                Quality = QualitySettings.GetQualityLevel(), OriginalVSync = vsync, OriginalFrameLimit = limit,
                SectionSeconds = host.Config.SectionSeconds, SavedOptions = saved.Copy(),
                LoadedMods = AppDomain.CurrentDomain.GetAssemblies().Where(a => a != typeof(HorayModBase).Assembly &&
                    SafeTypes(a).Any(t => !t.IsAbstract && typeof(HorayModBase).IsAssignableFrom(t)))
                    .Select(a => a.FullName).ToArray()
            };
            try
            {
                Directory.CreateDirectory(directory);
                Running = true;
                draws = Recorder(ProfilerCategory.Render, "Draw Calls Count");
                batches = Recorder(ProfilerCategory.Render, "Batches Count");
                triangles = Recorder(ProfilerCategory.Render, "Triangles Count");
                gc = Recorder(ProfilerCategory.Memory, "GC Allocated In Frame");
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                host.Config.LastComparison = result; host.SaveConfig();
                coroutine = host.StartCoroutine(Guard(host));
            }
            catch (Exception e) { Cancel(host, "start failed: " + e.Message); }
        }
        static Type[] SafeTypes(System.Reflection.Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }
        static IEnumerator Guard(Host host)
        {
            var run = Run(host);
            while (Running)
            {
                object current;
                try { if (!run.MoveNext()) yield break; current = run.Current; }
                catch (Exception e) { Finish(host, "failed: " + e.GetBaseException().Message); yield break; }
                yield return current;
            }
        }
        static ProfilerRecorder Recorder(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch { return default; }
        }
        internal static RenderOptions Preset(int index, RenderOptions configured)
        {
            var p = configured.Copy();
            p.LightResolution = p.IdlePasses = p.StaticLights = p.SpriteBatch = p.WorldUpscale = p.DedicatedShader = false;
            switch (index)
            {
                case 1: p.LightResolution = true; break;
                case 2: p.IdlePasses = true; break;
                case 3: p.StaticLights = true; break;
                case 4: p.SpriteBatch = true; break;
                case 5: p.WorldUpscale = true; break;
                case 6: p.WorldUpscale = p.DedicatedShader = true; break;
                case 7: p.LightResolution = p.IdlePasses = p.StaticLights = p.SpriteBatch = p.WorldUpscale = p.DedicatedShader = true; break;
                case 8: return configured.Copy();
            }
            return p;
        }
        static IEnumerator Run(Host host)
        {
            for (int index = 0; index < Names.Length; index++)
            {
                var requested = Preset(index, saved);
                Pipeline.Set(requested); Pipeline.Tick();
                host.Status = $"LXIUM F10 {index + 1}/{Names.Length}: {Names[index]}\n안정화 2초 → 측정 {result.SectionSeconds - 2}초 · F10 취소\n같은 장면·행동을 유지하세요.";
                double until = Time.realtimeSinceStartupAsDouble + SettleSeconds;
                while (Time.realtimeSinceStartupAsDouble < until) { if (!Check(host)) yield break; yield return null; }
                var stats = new FrameStats();
                var section = new SectionResult { Number = index + 1, Name = Names[index], Status = "measured", Requested = requested.Copy() };
                section.StartSprites = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;
                section.StartLights = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled);
                section.StartCameraSize = camera.orthographicSize;
                long hit = LightCache.Hits, miss = LightCache.Misses, fallback = LightCache.Fallbacks, scaled = WorldRender.Scaled,
                    native = WorldRender.Native, skipped = Pipeline.PassSkipped;
                int collections = Collections();
                double cpu = 0, render = 0, gpu = 0, drawSum = 0, batchSum = 0, triangleSum = 0, gcSum = 0;
                int cpuCount = 0, gpuCount = 0, drawCount = 0, batchCount = 0, triangleCount = 0, gcCount = 0;
                ulong timestamp = 0;
                double last = Time.realtimeSinceStartupAsDouble;
                until = last + result.SectionSeconds - SettleSeconds;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    yield return null;
                    if (!Check(host)) yield break;
                    double now = Time.realtimeSinceStartupAsDouble;
                    stats.Add((now - last) * 1000); last = now;
                    Read(draws, ref drawSum, ref drawCount); Read(batches, ref batchSum, ref batchCount);
                    Read(triangles, ref triangleSum, ref triangleCount); Read(gc, ref gcSum, ref gcCount);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != timestamp)
                    {
                        var timing = timings[0]; timestamp = timing.frameStartTimestamp;
                        if (timing.cpuMainThreadFrameTime > 0) { cpu += timing.cpuMainThreadFrameTime; render += timing.cpuRenderThreadFrameTime; cpuCount++; }
                        if (timing.gpuFrameTime > 0) { gpu += timing.gpuFrameTime; gpuCount++; }
                    }
                }
                stats.Apply(section);
                section.CpuTimingSamples = cpuCount; section.GpuTimingSamples = gpuCount;
                section.CpuMainMs = Mean(cpu, cpuCount); section.CpuRenderMs = Mean(render, cpuCount); section.GpuMs = Mean(gpu, gpuCount);
                section.DrawCallsPerFrame = Mean(drawSum, drawCount); section.BatchesPerFrame = Mean(batchSum, batchCount);
                section.TrianglesPerFrame = Mean(triangleSum, triangleCount); section.GcBytesPerFrame = Mean(gcSum, gcCount);
                section.GcCollections = Collections() - collections;
                section.EndSprites = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;
                section.EndLights = UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled);
                section.EndCameraSize = camera.orthographicSize;
                section.LightHits = LightCache.Hits - hit; section.LightMisses = LightCache.Misses - miss; section.LightFallbacks = LightCache.Fallbacks - fallback;
                section.WorldScaled = WorldRender.Scaled - scaled; section.WorldNative = WorldRender.Native - native; section.PassSkipped = Pipeline.PassSkipped - skipped;
                section.EffectiveFeatures = Pipeline.Effective;
                if (result.Sections.Count > 0 && result.Sections[0].AverageFps > 0)
                    section.DeltaFpsPercent = (section.AverageFps / result.Sections[0].AverageFps - 1) * 100;
                if (index == Names.Length - 1) section.BaselineDriftPercent = section.DeltaFpsPercent;
                result.Sections.Add(section);
                host.Config.LastComparison = result; host.SaveConfig();
                Log.Info($"F10 {section.Name}: {section.AverageFps:F1} FPS, 1% {section.Low1Fps:F1}, p99 {section.P99Ms:F2}ms; {section.EffectiveFeatures}");
                host.Status = null;
                if (host.Config.Screenshots)
                {
                    yield return null; yield return new WaitForEndOfFrame();
                    if (!Check(host)) yield break;
                    Texture2D screenshot = null;
                    try
                    {
                        screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                        section.Screenshot = section.Name + ".png";
                        File.WriteAllBytes(Path.Combine(result.Directory, section.Screenshot), ImageConversion.EncodeToPNG(screenshot));
                    }
                    catch (Exception e) { Log.Warn("screenshot: " + e.Message); }
                    finally { if (screenshot != null) UnityEngine.Object.Destroy(screenshot); }
                }
            }
            Finish(host, "completed");
        }
        static bool Check(Host host)
        {
            if (camera == null || GameCamera.Instance == null || GameCamera.Instance.Camera != camera || camera.gameObject.scene.name != scene ||
                Screen.width != result.Width || Screen.height != result.Height || QualitySettings.GetQualityLevel() != result.Quality)
            { Finish(host, "aborted: camera/scene/display/quality changed"); return false; }
            return Running;
        }
        static void Read(ProfilerRecorder recorder, ref double sum, ref int count)
        { if (recorder.Valid && recorder.Count > 0) { sum += recorder.LastValue; count++; } }
        static double? Mean(double sum, int count) => count == 0 ? (double?)null : sum / count;
        static int Collections() => GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        public static void Cancel(Host host, string reason)
        {
            if (!Running) return;
            var stop = coroutine; coroutine = null;
            if (stop != null) host.StopCoroutine(stop);
            Finish(host, "aborted: " + reason);
        }
        static void Finish(Host host, string outcome)
        {
            if (!Running) return;
            Running = false; coroutine = null;
            try
            {
                QualitySettings.vSyncCount = vsync; Application.targetFrameRate = limit;
                Pipeline.Set(saved); Pipeline.Tick();
            }
            finally
            {
                draws.Dispose(); batches.Dispose(); triangles.Dispose(); gc.Dispose();
                result.Outcome = outcome; result.FinishedUtc = DateTime.UtcNow.ToString("o");
                host.Config.LastComparison = result; host.SaveConfig();
                try { File.WriteAllText(Path.Combine(result.Directory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented)); }
                catch (Exception e) { Log.Warn("comparison save: " + e.Message); }
                host.Status = "LXIUM F10 " + outcome + "\n설정·FPS 제한·VSync 복원 완료. config.json → LastComparison\n" + result.Directory;
            }
        }
    }
}
