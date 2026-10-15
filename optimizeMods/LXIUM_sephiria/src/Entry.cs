using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LXIUM_sephiria
{
    /// <summary>
    /// 게임 내장 AddOnLoader가 metadata.json의 entryClass로 만드는 진입점.
    /// GameDataLoader.Awake 안, 게임 데이터베이스 초기화 전에 불린다.
    /// </summary>
    public class Entry : HorayModBase
    {
        GameObject host;

        protected override void OnModLoaded()
        {
            string dir = Path.GetDirectoryName(typeof(Entry).Assembly.Location);
            var settings = Settings.Load(dir);
            Frame.Install();
            Debug.Log($"[LXIUM] {typeof(Entry).Assembly.GetName().Version.ToString(3)} loaded; {Probe.Environment()}");
            Debug.Log("[LXIUM] loop phases: " + string.Join(",", Frame.PhaseNames));
            Shaders.Load();
            Debug.Log("[LXIUM] render " + Render.Apply(settings.Render));
            host = new GameObject("LXIUM_sephiria") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Driver>().Init(settings, Path.Combine(dir, "logs"));
        }

        protected override void OnModUnloaded()
        {
            if (host) Object.Destroy(host);
            Frame.Uninstall();
        }
    }

    /// <summary>단축키, 장면 전환 덤프, 비교 실행, 주기 통계를 매 프레임 처리한다.</summary>
    public sealed class Driver : MonoBehaviour
    {
        Settings settings;
        string logDir;
        Lab lab;
        Key probeKey, labKey;
        float nextStats, probeAt = -1;
        long statsFrom;

        public void Init(Settings s, string dir)
        {
            settings = s;
            logDir = dir;
            lab = new Lab(s, dir);
            probeKey = ParseKey(s.ProbeKey);
            labKey = ParseKey(s.LabKey);
            nextStats = Time.realtimeSinceStartup + s.StatsIntervalSec;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static Key ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return Key.None;
            if (Enum.TryParse(name, true, out Key k)) return k;
            Debug.LogWarning($"[LXIUM] 키 '{name}' 모름 (InputSystem Key 이름, 예: F9). 끔");
            return Key.None;
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (settings.AutoProbe) probeAt = Time.realtimeSinceStartup + 3;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (probeKey != Key.None && kb[probeKey].wasPressedThisFrame) RunProbe();
                if (labKey != Key.None && kb[labKey].wasPressedThisFrame) lab.Toggle();
            }
            if (probeAt > 0 && now >= probeAt)
            {
                probeAt = -1;
                RunProbe();
            }
            lab.Tick();
            if (lab.Running || settings.StatsIntervalSec <= 0)
            {
                statsFrom = Frame.Count;
                nextStats = now + settings.StatsIntervalSec;
            }
            else if (now >= nextStats)
            {
                Debug.Log($"[LXIUM] frame scene={Probe.SceneName()} {Frame.Report(statsFrom)}");
                statsFrom = Frame.Count;
                nextStats = now + settings.StatsIntervalSec;
            }
        }

        void RunProbe()
        {
            try { Debug.Log("[LXIUM] probe → logs/" + Path.GetFileName(Probe.Dump(logDir))); }
            catch (Exception e) { Debug.LogWarning("[LXIUM] probe 실패: " + e); }
        }
    }
}
