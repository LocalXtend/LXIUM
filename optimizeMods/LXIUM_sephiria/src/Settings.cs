using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace LXIUM_sephiria
{
    /// <summary>렌더 설정 한 벌. 0 이하·빈 값은 게임 원래 값.</summary>
    public class RenderPreset
    {
        public string Name = "game";
        public float LightScale;    // Renderer2DData 2D 조명 텍스처 배율 (게임 1)
        public float RenderScale;   // URP 렌더 배율. 화면 공간 오버레이 UI는 영향 없음
        public string Upscale = ""; // URP 업스케일 필터: Auto, Linear, Point, FSR, STP, 또는 Texel (LXIUM 픽셀 아트 확대)
        public float Sharpness = -1; // FSR 뒤 RCAS 적응형 선명화 0~1. 0 = 끔, 음수 = 게임 값. Texel은 늘 0
        public float TexelMultiple;  // 0보다 크면 RenderScale 대신 "아트 텍셀 줄 수 × k"로 자동 (Upscale 비면 Texel)

        public override string ToString() =>
            $"{Name}(light={(LightScale > 0 ? LightScale.ToString("0.###") : "game")} " +
            $"scale={(TexelMultiple > 0 ? "texel x" + TexelMultiple.ToString("0.##") : RenderScale > 0 ? RenderScale.ToString("0.###") : "game")} " +
            $"upscale={(Upscale.Length > 0 ? Upscale : TexelMultiple > 0 ? "Texel" : "game")} sharp={(Sharpness >= 0 ? Sharpness.ToString("0.##") : "game")})";
    }

    /// <summary>
    /// settings.json. 없으면 기본값으로 만들고, 있으면 읽은 뒤 새 항목을 채워 다시 쓴다.
    /// Unity JsonUtility는 실행 중 불러온 모드 어셈블리의 중첩 클래스(Render, LabPresets)를 건너뛰어서 게임에 든 Newtonsoft.Json을 쓴다.
    /// </summary>
    public class Settings
    {
        public string ProbeKey = "F9";        // 렌더 상태 덤프 (InputSystem Key 이름)
        public string LabKey = "F10";         // 렌더 설정 비교 실행 / 중단
        public bool AutoProbe = true;         // 장면이 바뀌면 3초 뒤 덤프
        // 자동 측정 (MelonLoader로 타이틀부터 돌 때). 0 = 끔
        public float AutoStartSec;            // 타이틀 로드 n초 뒤 "게임 시작"을 누름
        public float AutoLabSec;              // AutoLabScene 로드 n초 뒤 비교 실행 (한 번)
        public string AutoLabScene = "MainWorld";
        public bool QuitAfterLab;             // 자동 비교(와 AutoProfile)가 끝나면 게임 종료
        // CPU 내역 측정 (MelonLoader 실행 때만, Profile.cs)
        public string ProfileKey = "F11";
        public float ProfileSec = 8;          // 단계마다 측정 시간
        public bool AutoProfile;              // 자동 비교 뒤 이어서 측정
        public string[] ProfileMethods =      // "형식 전체 이름:메서드" (같은 이름 오버로드 모두, 포함 시간)
        {
            "UnityEngine.Rendering.Universal.UniversalRenderPipeline:Render",
            "UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderSingleCamera",
            "UnityEngine.Rendering.Universal.Renderer2D:OnRecordRenderGraph",
            "UnityEngine.Rendering.Universal.LayerUtility:CalculateBatches",
            "UnityEngine.Rendering.RenderGraphModule.RenderGraph:BeginRecording",
            "UnityEngine.Rendering.RenderGraphModule.RenderGraph:EndRecordingAndExecute",
            "UnityEngine.Rendering.RenderGraphModule.RenderGraph:CompileRenderGraph",
            "UnityEngine.Rendering.RenderGraphModule.RenderGraph:ExecuteRenderGraph",
            "UnityEngine.Rendering.VolumeManager:Update",
            "UnityEngine.UI.CanvasUpdateRegistry:PerformUpdate",
            "TMPro.TMP_UpdateManager:DoRebuilds",
        };
        public float StatsIntervalSec = 10;   // 프레임 통계를 로그에 남기는 간격. 0 = 끔
        public float ArtPPU = 16;             // 텍셀 격자 자동 배율의 아트 PPU
        // 기본: 월드 절반 해상도 + LXIUM 픽셀 아트 확대 (3차 실측 +25%, 화면 거의 같음). 원래대로는 Name만 남기고 나머지 0·빈 값
        public RenderPreset Render = new RenderPreset { Name = "texel0.5", RenderScale = 0.5f, Upscale = "Texel" };
        public float LabWarmupSec = 2;
        public float LabMeasureSec = 8;
        public RenderPreset[] LabPresets =
        {
            new RenderPreset { Name = "game" },
            new RenderPreset { Name = "texel0.5", RenderScale = 0.5f, Upscale = "Texel" },
            new RenderPreset { Name = "texelx2", TexelMultiple = 2 },
            new RenderPreset { Name = "texelx3", TexelMultiple = 3 },
            new RenderPreset { Name = "texelx2light0.5", TexelMultiple = 2, LightScale = 0.5f },
            new RenderPreset { Name = "game2" },
        };

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // 파일의 목록이 기본 목록을 대신함
            Formatting = Formatting.Indented,
        };

        public static Settings Load(string dir)
        {
            var s = new Settings();
            string path = Path.Combine(dir, "settings.json");
            try
            {
                if (File.Exists(path)) JsonConvert.PopulateObject(File.ReadAllText(path), s, Json);
            }
            catch (Exception e)
            {
                // 사용자가 고친 파일을 덮어쓰지 않도록 읽기에 실패하면 쓰지 않는다
                Debug.LogWarning("[LXIUM] settings.json 읽기 실패, 기본값 사용: " + e.Message);
                return new Settings();
            }
            try { File.WriteAllText(path, JsonConvert.SerializeObject(s, Json)); }
            catch (Exception e) { Debug.LogWarning("[LXIUM] settings.json 쓰기 실패: " + e.Message); }
            return s;
        }
    }
}
