using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LXIUM_sephiria
{
    /// <summary>
    /// 엔진에 이미 있는 기능만으로 렌더 비용을 바꾼다: URP 렌더 배율·업스케일 필터, 2D 조명 텍스처 배율.
    /// 게임 원래 값은 처음 찾을 때 한 번 기억하고, 프리셋의 0 이하·빈 값은 그 값으로 되돌린다.
    /// </summary>
    public static class Render
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo RendererList = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Private);
        static readonly FieldInfo LightScale = typeof(Renderer2DData).GetField("m_LightRenderTextureScale", Private);

        static UniversalRenderPipelineAsset asset;
        static Renderer2DData[] datas = Array.Empty<Renderer2DData>();
        static float[] gameLight = Array.Empty<float>();
        static float gameScale, gameSharpness;
        static bool gameOverride;
        static UpscalingFilterSelection gameFilter;

        public static UniversalRenderPipelineAsset Asset => Find() ? asset : null;
        public static Renderer2DData[] Datas => Find() ? datas : Array.Empty<Renderer2DData>();

        static bool Find()
        {
            if (asset != null) return true;
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return false;
            var list = RendererList?.GetValue(asset) as ScriptableRendererData[] ?? Array.Empty<ScriptableRendererData>();
            datas = Array.ConvertAll(list, d => d as Renderer2DData);
            gameLight = Array.ConvertAll(datas, d => d != null && LightScale != null ? (float)LightScale.GetValue(d) : 1f);
            gameScale = asset.renderScale;
            gameFilter = asset.upscalingFilter;
            gameOverride = asset.fsrOverrideSharpness;
            gameSharpness = asset.fsrSharpness;
            return true;
        }

        /// <summary>아트의 1유닛당 픽셀 수. 세피리아 스프라이트는 모두 16 [관찰: probe]</summary>
        public static float ArtPpu = 16;
        static RenderPreset current = new RenderPreset();

        public static string Apply(RenderPreset p)
        {
            if (!Find()) return "URP 에셋 없음, 적용 안 함";
            current = p;
            asset.renderScale = p.TexelMultiple > 0 ? TexelScale(p.TexelMultiple, gameScale) : p.RenderScale > 0 ? p.RenderScale : gameScale;
            var filter = gameFilter;
            string upscale = p.Upscale.Length > 0 ? p.Upscale : p.TexelMultiple > 0 ? "Texel" : "";
            bool texel = string.Equals(upscale, "Texel", StringComparison.OrdinalIgnoreCase) && Shaders.TexelUpscale;
            if (texel) filter = UpscalingFilterSelection.FSR;
            else if (upscale.Length > 0 && !Enum.TryParse(upscale, true, out filter))
            {
                Debug.LogWarning($"[LXIUM] 업스케일 필터 '{upscale}' 모름 (Auto, Linear, Point, FSR, STP, Texel) 또는 셰이더 없음. 게임 값 사용");
                filter = gameFilter;
            }
            asset.upscalingFilter = filter;
            SwapEasu(texel);
            // Texel은 RCAS를 끈 경로 전용 (TexelUpscale.hlsl). 0이면 URP가 _RCAS 키워드를 안 켬
            float sharp = texel ? 0 : p.Sharpness;
            asset.fsrOverrideSharpness = sharp >= 0 || gameOverride;
            asset.fsrSharpness = sharp >= 0 ? sharp : gameSharpness;
            for (int i = 0; i < datas.Length; i++)
                if (datas[i] != null && LightScale != null) LightScale.SetValue(datas[i], p.LightScale > 0 ? p.LightScale : gameLight[i]);
            return p + $" → scale {asset.renderScale:0.##}";
        }

        /// <summary>
        /// 텍셀 격자 자동 배율: 월드 세로 해상도 = k × 화면에 보이는 아트 텍셀 줄 수 (2 × 직교 크기 × PPU).
        /// 카메라 줌·창 크기가 바뀌면 따라가도록 매 프레임 부른다. 0.01 단위로 반올림해 바뀔 때만 설정 (URP가 대상 텍스처를 다시 만듦).
        /// </summary>
        public static void Tick()
        {
            if (current.TexelMultiple <= 0 || asset == null) return;
            float s = TexelScale(current.TexelMultiple, asset.renderScale);
            if (Mathf.Abs(s - asset.renderScale) < 0.005f) return;
            asset.renderScale = s;
            Debug.Log($"[LXIUM] texel x{current.TexelMultiple:0.##} → scale {s:0.##} (ortho {Camera.main.orthographicSize:0.###}, {Camera.main.pixelHeight}px)");
        }

        /// <summary>카메라가 없거나 원근이면 fallback 그대로. 1 이상이면 원래 해상도</summary>
        static float TexelScale(float k, float fallback)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic || cam.pixelHeight <= 0) return fallback;
            return Mathf.Clamp(Mathf.Round(k * 2 * cam.orthographicSize * ArtPpu / cam.pixelHeight * 100) / 100, 0.1f, 1f);
        }

        const string EasuName = "Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling";
        static Shader easu;

        /// <summary>
        /// URP FSR 경로의 확대 재질(PostProcessMaterialLibrary.easu)의 셰이더를 LXIUM/TexelUpscale로 바꾸거나 되돌린다.
        /// 바인딩이 같아 URP의 나머지(입력 준비, RCAS 선명화)는 그대로 돈다. 재질은 렌더러가 다시 만들어지면 새로 생기므로 적용할 때마다 찾는다.
        /// </summary>
        static void SwapEasu(bool texel)
        {
            var target = texel ? Shaders.TexelUpscale : easu;
            if (target == null) return;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null) continue;
                if (m.shader.name == EasuName) easu = m.shader;
                if (m.shader.name == EasuName || m.shader == Shaders.TexelUpscale) m.shader = target;
            }
        }
    }

    /// <summary>
    /// 렌더 설정 비교: 프리셋마다 적용 → 예열 → 측정 → 스크린샷 순으로 돌고, 끝나면 사용자 설정(Settings.Render)으로 되돌린다.
    /// 결과는 logs/lab-시각/ 에 results.txt와 프리셋별 png로 남긴다.
    /// </summary>
    public sealed class Lab
    {
        enum Stage { Warmup, Measure, Shot }

        readonly Settings settings;
        readonly string logDir;
        int index = -1;
        Stage stage;
        float stageEnd;
        long from;
        string runDir;
        StringBuilder results;

        public Lab(Settings settings, string logDir) { this.settings = settings; this.logDir = logDir; }

        public bool Running => index >= 0;

        public void Toggle()
        {
            if (Running) { Stop("중단"); return; }
            if (settings.LabPresets == null || settings.LabPresets.Length == 0) { Debug.Log("[LXIUM] lab: 프리셋 없음"); return; }
            runDir = Path.Combine(logDir, "lab-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDir);
            results = new StringBuilder();
            results.AppendLine("# " + Probe.Environment());
            index = 0;
            Begin();
        }

        void Begin()
        {
            var p = settings.LabPresets[index];
            Debug.Log("[LXIUM] lab " + (index + 1) + "/" + settings.LabPresets.Length + " " + Render.Apply(p));
            stage = Stage.Warmup;
            stageEnd = Time.realtimeSinceStartup + settings.LabWarmupSec;
        }

        public void Tick()
        {
            if (!Running || Time.realtimeSinceStartup < stageEnd) return;
            var p = settings.LabPresets[index];
            switch (stage)
            {
                case Stage.Warmup:
                    stage = Stage.Measure;
                    from = Frame.Count;
                    stageEnd = Time.realtimeSinceStartup + settings.LabMeasureSec;
                    break;
                case Stage.Measure:
                    string line = $"{index + 1:00}_{p}: {Frame.Report(from)}";
                    Debug.Log("[LXIUM] lab " + line);
                    results.AppendLine(line);
                    // 스크린샷은 이 프레임 끝에 찍히므로 다음 프리셋은 몇 프레임 뒤에 적용한다
                    ScreenCapture.CaptureScreenshot(Path.Combine(runDir, $"{index + 1:00}_{FileName(p.Name)}.png"));
                    stage = Stage.Shot;
                    stageEnd = Time.realtimeSinceStartup + 0.3f;
                    break;
                case Stage.Shot:
                    if (++index >= settings.LabPresets.Length) Stop("끝");
                    else Begin();
                    break;
            }
        }

        void Stop(string why)
        {
            index = -1;
            Debug.Log("[LXIUM] lab " + why + ", 사용자 설정으로 복귀: " + Render.Apply(settings.Render));
            try { File.WriteAllText(Path.Combine(runDir, "results.txt"), results.ToString()); }
            catch (Exception e) { Debug.LogWarning("[LXIUM] lab 결과 저장 실패: " + e.Message); }
        }

        static string FileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
