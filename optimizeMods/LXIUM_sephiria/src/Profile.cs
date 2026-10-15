using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LXIUM_sephiria
{
    /// <summary>
    /// CPU 내역 측정 (MelonLoader의 Harmony가 있을 때만, 개발용).
    /// 1단계: 패치 없이 플레이어 루프 하위 시스템별 시간. 2단계: 모든 MonoBehaviour의 Update·LateUpdate와
    /// 설정의 엔진 메서드(렌더·UI)를 Harmony로 감싸 형식별 시간. 2단계가 끝나면 패치를 모두 되돌린다.
    /// 감싸는 비용이 2단계 숫자에 들어가므로 1단계 숫자와 비율로 읽는다.
    /// Harmony 형식은 메서드 안에서만 써서, Harmony가 없는 내장 로더 실행에서도 이 형식을 들고 있을 수 있다.
    /// </summary>
    public sealed class Profile
    {
        enum Stage { Idle, Plain, Warm, Patched }

        static readonly string[] Callbacks = { "Update", "LateUpdate" };

        readonly Settings settings;
        readonly string logDir;
        Stage stage;
        float stageEnd;
        long from;
        object harmony;
        int patched, failed;
        StringBuilder report;

        // 집계 칸. 키 = (실제 형식, 원래 메서드). 칸 수 한도를 넘으면 버림
        static readonly Dictionary<(Type, MethodBase), int> Slots = new Dictionary<(Type, MethodBase), int>();
        static readonly List<string> Names = new List<string>();
        static readonly List<bool> IsScript = new List<bool>();
        static readonly long[] Ticks = new long[16384];
        static readonly long[] Calls = new long[16384];

        public Profile(Settings settings, string logDir) { this.settings = settings; this.logDir = logDir; }

        public bool Running => stage != Stage.Idle;

        public void Toggle()
        {
            if (Running) { Finish("중단"); return; }
            report = new StringBuilder("# " + Probe.Environment() + "\n# scene=" + Probe.SceneName() + "\n");
            Frame.ResetDetail();
            from = Frame.Count;
            stage = Stage.Plain;
            stageEnd = Time.realtimeSinceStartup + settings.ProfileSec;
            Debug.Log($"[LXIUM] profile 1/2: 패치 없이 하위 시스템 {settings.ProfileSec}초");
        }

        public void Tick()
        {
            if (!Running || Time.realtimeSinceStartup < stageEnd) return;
            switch (stage)
            {
                case Stage.Plain:
                    report.AppendLine("## 1단계 (패치 없음) " + Frame.Report(from)).AppendLine(Frame.DetailReport(60));
                    Shot("1-plain");
                    Patch();
                    stage = Stage.Warm;
                    stageEnd = Time.realtimeSinceStartup + 1;
                    break;
                case Stage.Warm:
                    Array.Clear(Ticks, 0, Ticks.Length);
                    Array.Clear(Calls, 0, Calls.Length);
                    Frame.ResetDetail();
                    from = Frame.Count;
                    stage = Stage.Patched;
                    stageEnd = Time.realtimeSinceStartup + settings.ProfileSec;
                    break;
                case Stage.Patched:
                    Shot("2-patched");
                    Finish("끝");
                    break;
            }
        }

        void Patch()
        {
            var h = new HarmonyLib.Harmony("lxium.sephiria.profile");
            harmony = h;
            var pre = new HarmonyMethod(AccessTools.Method(typeof(Profile), nameof(Pre)));
            var postScript = new HarmonyMethod(AccessTools.Method(typeof(Profile), nameof(PostScript)));
            var postMethod = new HarmonyMethod(AccessTools.Method(typeof(Profile), nameof(PostMethod)));
            var sw = Stopwatch.StartNew();
            patched = failed = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == typeof(Profile).Assembly || asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                    foreach (var name in Callbacks)
                    {
                        var m = t.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (m == null || m.IsAbstract) continue;
                        try { h.Patch(m, pre, postScript); patched++; }
                        catch { failed++; }
                    }
                }
            }
            foreach (var spec in settings.ProfileMethods)
            {
                int colon = spec.LastIndexOf(':');
                var type = colon > 0 ? AccessTools.TypeByName(spec.Substring(0, colon)) : null;
                var methods = type?.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == spec.Substring(colon + 1) && !m.IsAbstract && !m.ContainsGenericParameters).ToArray();
                if (methods == null || methods.Length == 0) { report.AppendLine("# 못 찾음: " + spec); continue; }
                foreach (var m in methods)
                {
                    try { h.Patch(m, pre, postMethod); patched++; }
                    catch (Exception e) { failed++; report.AppendLine($"# 패치 실패: {spec} {e.GetType().Name}"); }
                }
            }
            Debug.Log($"[LXIUM] profile 2/2: {patched}개 패치 ({failed} 실패, {sw.ElapsedMilliseconds} ms), {settings.ProfileSec}초");
        }

        /// <summary>단계마다 화면도 남겨 패치가 그림을 바꾸는지 확인 (이 프레임 끝에 찍힘)</summary>
        void Shot(string name)
        {
            Directory.CreateDirectory(logDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(logDir, $"profile-{DateTime.Now:yyyyMMdd-HHmmss}-{name}.png"));
        }

        static void Pre(out long __state) => __state = Stopwatch.GetTimestamp();

        static void PostScript(long __state, object __instance, MethodBase __originalMethod) =>
            Add(__instance != null ? __instance.GetType() : __originalMethod.DeclaringType, __originalMethod, __state, true);

        static void PostMethod(long __state, MethodBase __originalMethod) => Add(__originalMethod.DeclaringType, __originalMethod, __state, false);

        static void Add(Type t, MethodBase m, long start, bool script)
        {
            long d = Stopwatch.GetTimestamp() - start;
            if (!Slots.TryGetValue((t, m), out int i))
            {
                i = Names.Count;
                if (i >= Ticks.Length) return;
                Slots[(t, m)] = i;
                Names.Add(t.FullName + "." + m.Name);
                IsScript.Add(script);
            }
            Ticks[i] += d;
            Calls[i]++;
        }

        void Finish(string why)
        {
            var was = stage;
            stage = Stage.Idle;
            if (harmony is HarmonyLib.Harmony h) h.UnpatchSelf();
            harmony = null;
            if (was == Stage.Patched)
            {
                long n = Math.Max(1, Frame.Count - from);
                double toMs = 1000.0 / Stopwatch.Frequency / n;
                double scriptMs = 0;
                for (int i = 0; i < Names.Count; i++) if (IsScript[i]) scriptMs += Ticks[i] * toMs;
                report.AppendLine($"## 2단계 (패치 {patched}개) " + Frame.Report(from))
                      .AppendLine($"스크립트 Update·LateUpdate 합 {scriptMs:F3} ms/frame (감싸는 비용 포함)")
                      .AppendLine(Frame.DetailReport(25))
                      .AppendLine("ms/frame  calls/frame  이름 (스크립트=형식별, 엔진 메서드=포함 시간)");
                foreach (int i in Enumerable.Range(0, Names.Count).OrderByDescending(i => Ticks[i]).Take(80))
                    report.AppendLine($"{Ticks[i] * toMs,8:F3} {Calls[i] / (double)n,8:F1}  {(IsScript[i] ? "" : "[engine] ")}{Names[i]}");
            }
            Directory.CreateDirectory(logDir);
            string path = Path.Combine(logDir, $"profile-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            try { File.WriteAllText(path, report.ToString()); }
            catch (Exception e) { Debug.LogWarning("[LXIUM] profile 저장 실패: " + e.Message); }
            Debug.Log($"[LXIUM] profile {why} → logs/{Path.GetFileName(path)}");
            Slots.Clear();
            Names.Clear();
            IsScript.Clear();
        }
    }
}
