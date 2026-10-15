using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LXIUM_sephiria
{
    /// <summary>
    /// 로더와 무관한 시작·정리. 진입점은 게임 내장 AddOnLoader(<see cref="Entry"/>)와 MelonLoader(<see cref="MelonEntry"/>) 둘.
    /// 두 로더가 다 있으면 먼저 부른 쪽만 돈다 (어셈블리가 따로 로드돼도 AppDomain 표식은 하나).
    /// </summary>
    public static class Core
    {
        const string Running = "LXIUM_sephiria.running";
        static GameObject host;
        public static string Loader { get; private set; }

        /// <param name="dir">settings.json·logs를 둘 폴더</param>
        public static void Start(string dir, string loader)
        {
            if (AppDomain.CurrentDomain.GetData(Running) is string other)
            {
                Debug.Log($"[LXIUM] {loader}: 이미 {other}로 실행 중, 건너뜀");
                return;
            }
            AppDomain.CurrentDomain.SetData(Running, loader);
            Loader = loader;
            Directory.CreateDirectory(dir);
            var settings = Settings.Load(dir);
            Frame.Install();
            Debug.Log($"[LXIUM] {typeof(Core).Assembly.GetName().Version.ToString(3)} loaded by {loader}; {Probe.Environment()}");
            Debug.Log("[LXIUM] loop phases: " + string.Join(",", Frame.PhaseNames));
            Shaders.Load();
            Render.ArtPpu = settings.ArtPPU;
            Debug.Log("[LXIUM] render " + Render.Apply(settings.Render));
            host = new GameObject("LXIUM_sephiria") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Driver>().Init(settings, Path.Combine(dir, "logs"));
        }

        public static void Stop()
        {
            if (host) Object.Destroy(host);
            Frame.Uninstall();
        }
    }

    /// <summary>
    /// 게임 내장 AddOnLoader가 metadata.json의 entryClass로 만드는 진입점.
    /// 타이틀이 아니라 게임에 들어갈 때 GameDataLoader.Awake 안에서 불린다. 설정·로그는 AddOns/LXIUM_sephiria/.
    /// </summary>
    public class Entry : HorayModBase
    {
        protected override void OnModLoaded() => Core.Start(Path.GetDirectoryName(typeof(Entry).Assembly.Location), "AddOns");
        protected override void OnModUnloaded() => Core.Stop();
    }

    /// <summary>단축키, 장면 전환 덤프, 비교 실행, 주기 통계를 매 프레임 처리한다.</summary>
    public sealed class Driver : MonoBehaviour
    {
        Settings settings;
        string logDir;
        Lab lab;
        Profile profile;   // Harmony가 없으면 null
        Key probeKey, labKey, profileKey;
        float nextStats, probeAt = -1, autoStartAt = -1, autoLabAt = -1;
        int autoStartTries;
        bool autoLabDone, autoLabRunning, autoProfileRunning;
        long statsFrom;

        public void Init(Settings s, string dir)
        {
            settings = s;
            logDir = dir;
            lab = new Lab(s, dir);
            // 측정용 Harmony는 MelonLoader가 싣는 것만 씀 (다른 모드가 싣는 0Harmony는 버전이 다를 수 있음)
            if (Core.Loader == "MelonLoader" && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony"))
                profile = new Profile(s, dir);
            probeKey = ParseKey(s.ProbeKey);
            labKey = ParseKey(s.LabKey);
            profileKey = ParseKey(s.ProfileKey);
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
            float now = Time.realtimeSinceStartup;
            if (settings.AutoProbe) probeAt = now + 3;
            if (settings.AutoStartSec > 0 && scene.name == "Title")
            {
                autoStartAt = now + settings.AutoStartSec;
                autoStartTries = 0;
            }
            if (settings.AutoLabSec > 0 && !autoLabDone && scene.name == settings.AutoLabScene) autoLabAt = now + settings.AutoLabSec;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (probeKey != Key.None && kb[probeKey].wasPressedThisFrame) RunProbe();
                if (labKey != Key.None && kb[labKey].wasPressedThisFrame) lab.Toggle();
                if (profile != null && profileKey != Key.None && kb[profileKey].wasPressedThisFrame) profile.Toggle();
            }
            if (probeAt > 0 && now >= probeAt)
            {
                probeAt = -1;
                RunProbe();
            }
            if (autoStartAt > 0 && now >= autoStartAt) AutoStart(now);
            if (autoLabAt > 0 && now >= autoLabAt)
            {
                autoLabAt = -1;
                autoLabDone = true;
                if (!lab.Running) { lab.Toggle(); autoLabRunning = true; }
            }
            lab.Tick();
            profile?.Tick();
            Render.Tick();
            if (autoLabRunning && !lab.Running)
            {
                autoLabRunning = false;
                if (settings.AutoProfile && profile != null) { profile.Toggle(); autoProfileRunning = true; }
                else if (settings.QuitAfterLab) Quit();
            }
            if (autoProfileRunning && !profile.Running)
            {
                autoProfileRunning = false;
                if (settings.QuitAfterLab) Quit();
            }
            if (lab.Running || (profile?.Running ?? false) || settings.StatsIntervalSec <= 0)
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

        static void Quit()
        {
            Debug.Log("[LXIUM] auto: 측정 끝, 게임 종료");
            Application.Quit();
        }

        /// <summary>타이틀의 "게임 시작" 버튼과 같은 UI_TitleLobby.NetworkHost(): 선택된 세이브로 호스트 시작. 장면이 안 바뀌면 10초마다 최대 3번</summary>
        void AutoStart(float now)
        {
            var lobby = Object.FindAnyObjectByType<UI_TitleLobby>();
            if (lobby == null || ++autoStartTries > 3) { autoStartAt = -1; return; }
            Debug.Log($"[LXIUM] auto: 타이틀 게임 시작 ({autoStartTries})");
            lobby.NetworkHost();
            autoStartAt = now + 10;
        }

        void RunProbe()
        {
            // 덤프와 같은 이름의 스크린샷도 남김 (화면 확인용, 이 프레임 끝에 찍힘)
            try
            {
                string path = Probe.Dump(logDir);
                ScreenCapture.CaptureScreenshot(Path.ChangeExtension(path, ".png"));
                Debug.Log("[LXIUM] probe → logs/" + Path.GetFileName(path));
            }
            catch (Exception e) { Debug.LogWarning("[LXIUM] probe 실패: " + e); }
        }
    }
}
