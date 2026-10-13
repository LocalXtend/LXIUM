using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LXIUM_sephiria
{
    // 렌더 설정과 프레임 통계를 Player.log에 남기는 진단 기능.
    // - 시작 시: 실행 환경, URP 에셋과 렌더러 데이터의 직렬화 값
    // - 씬 로드 3초 후: 카메라 구성, 2D 라이트·파티클·볼륨 개수
    // - 10초마다: FPS, 프레임 시간 분포, 끊김 프레임 수와 그중 GC가 돈 프레임 수, 드로우 콜, 관리 힙 크기
    // - F9: URP renderScale을 원래 값 ↔ 0.5로 전환. GPU 병목이면 0.5에서 프레임이 크게 오른다.
    //   에셋을 런타임에만 바꾸므로 게임을 다시 켜면 원래 값으로 돌아간다.
    // 이 게임 빌드는 FrameTimingManager(CPU/GPU 시간)와 GC 할당 카운터를 제공하지 않아 쓰지 않는다.
    sealed class Diagnostics : MonoBehaviour
    {
        const string Tag = "[LXIUM] ";
        const float ReportSeconds = 10f;
        const float HitchSeconds = 0.025f;
        const float TestScale = 0.5f;

        readonly float[] _frameTimes = new float[16384];
        readonly float[] _sorted = new float[16384];
        int _frames;
        float _elapsed;
        int _hitches, _hitchesWithGc, _gcRuns, _lastGcCount;

        ProfilerRecorder _setPass, _drawCalls, _batches;
        long _setPassSum, _drawCallsSum, _batchesSum;

        bool _f9WasDown;
        float _originalScale = -1f;

        void Start()
        {
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _lastGcCount = GC.CollectionCount(0);

            LogEnvironment();
            LogPipeline();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _setPass.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
            if (_originalScale >= 0f && Asset() is UniversalRenderPipelineAsset urp) urp.renderScale = _originalScale;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_frames < _frameTimes.Length) _frameTimes[_frames++] = dt;
            _elapsed += dt;

            // 직전 프레임 사이에 GC가 끝났는지와 그 프레임이 끊겼는지를 함께 센다
            int gc = GC.CollectionCount(0);
            bool gcRan = gc != _lastGcCount;
            _lastGcCount = gc;
            if (gcRan) _gcRuns++;
            if (dt > HitchSeconds)
            {
                _hitches++;
                if (gcRan) _hitchesWithGc++;
            }

            // 직전 프레임 값이 들어 있으므로 매 프레임 누적해 평균을 낸다
            _setPassSum += _setPass.LastValue;
            _drawCallsSum += _drawCalls.LastValue;
            _batchesSum += _batches.LastValue;

            // 게임의 Input System 갱신 방식과 무관하도록 눌림 상태 변화를 직접 비교한다
            var kb = Keyboard.current;
            bool f9Down = kb != null && kb.f9Key.isPressed;
            bool f9Pressed = f9Down && !_f9WasDown;
            _f9WasDown = f9Down;

            if (f9Pressed) ToggleRenderScale();
            else if (_elapsed >= ReportSeconds) Report();
        }

        void Report()
        {
            if (_frames == 0) return;
            Array.Copy(_frameTimes, _sorted, _frames);
            Array.Sort(_sorted, 0, _frames);
            float Pct(float p) => _sorted[Math.Min(_frames - 1, (int)(_frames * p))] * 1000f;

            var sb = new StringBuilder(Tag).Append("frame");
            sb.Append(" scene=").Append(SceneManager.GetActiveScene().name);
            sb.Append(" scale=").Append(Asset() is UniversalRenderPipelineAsset urp ? urp.renderScale.ToString("0.##") : "?");
            sb.Append(" fps=").Append((_frames / _elapsed).ToString("0.0"));
            sb.Append(" ms(p50/p95/p99/max)=").Append(Pct(0.5f).ToString("0.0")).Append('/').Append(Pct(0.95f).ToString("0.0"))
              .Append('/').Append(Pct(0.99f).ToString("0.0")).Append('/').Append((_sorted[_frames - 1] * 1000f).ToString("0.0"));
            sb.Append(" hitches>25ms=").Append(_hitches).Append(" withGC=").Append(_hitchesWithGc).Append(" gcRuns=").Append(_gcRuns)
              .Append(" heapMB=").Append((GC.GetTotalMemory(false) / 1048576f).ToString("0"));
            sb.Append(" setpass=").Append(_setPassSum / _frames)
              .Append(" draws=").Append(_drawCallsSum / _frames)
              .Append(" batches=").Append(_batchesSum / _frames);
            Debug.Log(sb.ToString());

            _frames = 0;
            _elapsed = 0f;
            _hitches = _hitchesWithGc = _gcRuns = 0;
            _setPassSum = _drawCallsSum = _batchesSum = 0;
        }

        void ToggleRenderScale()
        {
            Report();
            if (!(Asset() is UniversalRenderPipelineAsset urp))
            {
                Debug.Log(Tag + "renderScale: 활성 파이프라인이 URP가 아님");
                return;
            }
            if (_originalScale < 0f) _originalScale = urp.renderScale;
            urp.renderScale = Mathf.Approximately(urp.renderScale, _originalScale) ? TestScale : _originalScale;
            Debug.Log(Tag + "renderScale -> " + urp.renderScale.ToString("0.##"));
        }

        static RenderPipelineAsset Asset() => GraphicsSettings.currentRenderPipeline;

        static void LogEnvironment()
        {
            var r = Screen.currentResolution;
            Debug.Log(Tag + "env"
                + " game=" + Application.version
                + " unity=" + Application.unityVersion
                + " gfx=" + SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsMemorySize + "MB)"
                + " threading=" + SystemInfo.renderingThreadingMode
                + " cpu=" + SystemInfo.processorCount + "threads"
                + " screen=" + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode + " @" + r.refreshRateRatio.value.ToString("0.##") + "Hz"
                + " vsync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate
                + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "(" + QualitySettings.GetQualityLevel() + "/" + QualitySettings.names.Length + ")"
                + " frameTiming=" + FrameTimingManager.IsFeatureEnabled()
                + " input=" + (Keyboard.current != null ? InputSystem.settings.updateMode.ToString() : "noKeyboard"));
        }

        static void LogPipeline()
        {
            var asset = Asset();
            if (asset == null)
            {
                Debug.Log(Tag + "pipeline: 없음 (Built-in)");
                return;
            }
            Debug.Log(Tag + "pipeline " + asset.GetType().Name + " '" + asset.name + "' " + Fields(asset));

            // 렌더러 데이터 목록은 공개 API가 없어 직렬화 필드를 읽는다
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(asset) as ScriptableRendererData[];
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null)
                    Debug.Log(Tag + "renderer[" + i + "] " + list[i].GetType().Name + " '" + list[i].name + "' " + Fields(list[i]));
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(LogSceneLater(scene.name));

        IEnumerator LogSceneLater(string sceneName)
        {
            // 씬 초기화(층 생성 등)가 끝난 뒤 개수를 센다
            yield return new WaitForSecondsRealtime(3f);
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                var comps = new StringBuilder();
                foreach (var c in cam.GetComponents<Component>()) comps.Append(c.GetType().Name).Append(',');
                Debug.Log(Tag + "camera scene=" + sceneName + " '" + cam.name + "'"
                    + " px=" + cam.pixelWidth + "x" + cam.pixelHeight
                    + " ortho=" + cam.orthographic + "/" + cam.orthographicSize.ToString("0.##")
                    + " hdr=" + cam.allowHDR + " msaa=" + cam.allowMSAA
                    + " type=" + data.renderType + " post=" + data.renderPostProcessing + " aa=" + data.antialiasing
                    + " target=" + (cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "screen")
                    + " components=" + comps);
            }
            var lights = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            int global = 0, point = 0, sprite = 0, freeform = 0;
            foreach (var l in lights)
            {
                if (l.lightType == Light2D.LightType.Global) global++;
                else if (l.lightType == Light2D.LightType.Point) point++;
                else if (l.lightType == Light2D.LightType.Sprite) sprite++;
                else freeform++;
            }
            Debug.Log(Tag + "scene=" + sceneName
                + " light2d=" + lights.Length + "(global " + global + ", point " + point + ", sprite " + sprite + ", other " + freeform + ")"
                + " particles=" + FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length
                + " volumes=" + FindObjectsByType<Volume>(FindObjectsSortMode.None).Length
                + " spriteRenderers=" + FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length);
        }

        // 값 타입·문자열 직렬화 필드만 name=value로 나열한다
        static string Fields(object o)
        {
            var sb = new StringBuilder();
            for (var t = o.GetType(); t != null && t != typeof(ScriptableObject); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string))
                        sb.Append(f.Name).Append('=').Append(f.GetValue(o)).Append(' ');
                    else if (ft.IsArray && f.GetValue(o) is Array a)
                        sb.Append(f.Name).Append("[]=").Append(a.Length).Append(' ');
                }
            return sb.ToString();
        }
    }
}
