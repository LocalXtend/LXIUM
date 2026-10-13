using System;
using System.IO;
using UnityEngine;

namespace LXIUM_sephiria
{
    // 모드 폴더의 config.json. 없으면 기본값으로 만든다 (docs/ai/addon.md 5장: 설정 파일은 실행 중 생성).
    [Serializable]
    sealed class Config
    {
        public bool diagnostics = true;
        public bool sendMoneyScanFix = true;

        public static Config Load()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(Config).Assembly.Location), "config.json");
            try
            {
                if (File.Exists(path)) return JsonUtility.FromJson<Config>(File.ReadAllText(path)) ?? new Config();
                var config = new Config();
                File.WriteAllText(path, JsonUtility.ToJson(config, true));
                return config;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LXIUM] config.json 읽기 실패, 기본값 사용: " + e.Message);
                return new Config();
            }
        }
    }
}
