using System;
using System.IO;
using UnityEngine;

namespace LXIUM_sephiria
{
    /// <summary>렌더 설정 한 벌. 0 이하·빈 값은 게임 원래 값.</summary>
    [Serializable]
    public class RenderPreset
    {
        public string Name = "game";
        public float LightScale;    // Renderer2DData 2D 조명 텍스처 배율 (게임 1)
        public float RenderScale;   // URP 렌더 배율. 화면 공간 오버레이 UI는 영향 없음
        public string Upscale = ""; // URP 업스케일 필터: Auto, Linear, Point, FSR, STP, 또는 Texel (LXIUM 픽셀 아트 확대)
        public float Sharpness = -1; // FSR·Texel 뒤 RCAS 적응형 선명화 0~1. 0 = 끔, 음수 = 게임 값

        public override string ToString() =>
            $"{Name}(light={(LightScale > 0 ? LightScale.ToString("0.###") : "game")} scale={(RenderScale > 0 ? RenderScale.ToString("0.###") : "game")} " +
            $"upscale={(Upscale.Length > 0 ? Upscale : "game")} sharp={(Sharpness >= 0 ? Sharpness.ToString("0.##") : "game")})";
    }

    /// <summary>모드 폴더의 settings.json. 없으면 기본값으로 만들고, 있으면 읽은 뒤 새 항목을 채워 다시 쓴다.</summary>
    [Serializable]
    public class Settings
    {
        public string ProbeKey = "F9";        // 렌더 상태 덤프 (InputSystem Key 이름)
        public string LabKey = "F10";         // 렌더 설정 비교 실행 / 중단
        public bool AutoProbe = true;         // 장면이 바뀌면 3초 뒤 덤프
        public float StatsIntervalSec = 10;   // 프레임 통계를 로그에 남기는 간격. 0 = 끔
        public RenderPreset Render = new RenderPreset();
        public float LabWarmupSec = 2;
        public float LabMeasureSec = 6;
        public RenderPreset[] LabPresets =
        {
            new RenderPreset { Name = "game" },
            new RenderPreset { Name = "light0.5", LightScale = 0.5f },
            new RenderPreset { Name = "light0.25", LightScale = 0.25f },
            new RenderPreset { Name = "point0.5", RenderScale = 0.5f, Upscale = "Point" },
            new RenderPreset { Name = "fsr0.5", RenderScale = 0.5f, Upscale = "FSR" },
            new RenderPreset { Name = "texel0.5", RenderScale = 0.5f, Upscale = "Texel", Sharpness = 0 },
            new RenderPreset { Name = "texel0.5rcas", RenderScale = 0.5f, Upscale = "Texel" },
            new RenderPreset { Name = "texel0.75", RenderScale = 0.75f, Upscale = "Texel", Sharpness = 0 },
            new RenderPreset { Name = "game2" },
        };

        public static Settings Load(string dir)
        {
            var s = new Settings();
            string path = Path.Combine(dir, "settings.json");
            try
            {
                if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), s);
            }
            catch (Exception e)
            {
                // 사용자가 고친 파일을 덮어쓰지 않도록 읽기에 실패하면 쓰지 않는다
                Debug.LogWarning("[LXIUM] settings.json 읽기 실패, 기본값 사용: " + e.Message);
                return new Settings();
            }
            try { File.WriteAllText(path, JsonUtility.ToJson(s, true)); }
            catch (Exception e) { Debug.LogWarning("[LXIUM] settings.json 쓰기 실패: " + e.Message); }
            return s;
        }
    }
}
