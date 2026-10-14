using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LXIUM_sephiria
{
    // 렌더 실험실 (docs/ai/sephiria-render.md 3장).
    // 실행마다 게임 월드(MainWorld)에 처음 들어가고 20초 뒤 한 번 동작한다.
    // 1) 화면 구성(캔버스 렌더 모드, 스프라이트 PPU, 화면을 크게 덮는 렌더러, 2D 조명)을 기록하고
    // 2) 렌더 설정 후보를 하나씩 적용해 프레임 통계·스크린샷·에러 로그를 남긴 뒤 원래 설정으로 되돌린다.
    sealed class RenderLab : MonoBehaviour
    {
        const string Tag = "[LXIUM] lab ";
        const string WorldScene = "MainWorld";
        const float StartDelay = 20f, Settle = 2f, Sample = 6f;
        // 기본 orthographicSize 5.625 = 360/64 → 32 PPU 기준 세로 360px 픽셀아트로 추정
        const float ArtHeight = 360f;

        static readonly FieldInfo RendererList = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo LightScale = typeof(Renderer2DData).GetField("m_LightRenderTextureScale", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly List<float> _frameTimes = new List<float>(4096);
        string _status;
        bool _started;
        int _errors;
        string _firstError;

        void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == WorldScene) Begin();
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == WorldScene) Begin();
        }

        void Begin()
        {
            if (_started) return;
            _started = true;
            StartCoroutine(Run());
        }

        void OnGUI()
        {
            if (_status != null) GUI.Box(new Rect(10, 10, 720, 26), _status);
        }

        IEnumerator Run()
        {
            for (float left = StartDelay; left > 0; left -= Time.unscaledDeltaTime)
            {
                _status = "LXIUM 렌더 실험 " + Mathf.CeilToInt(left) + "초 뒤 시작 — 실험 동안 한 자리에 가만히 서 있어 주세요";
                yield return null;
            }

            var gameCamera = GameCamera.Instance;
            var cam = gameCamera != null ? gameCamera.Camera : Camera.main;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var r2d = (RendererList?.GetValue(urp) as ScriptableRendererData[])?.FirstOrDefault() as Renderer2DData;
            if (cam == null || urp == null)
            {
                Log.Write(Tag + "카메라 또는 URP 에셋이 없어 중단");
                _status = null;
                yield break;
            }

            Log.Write(Tag + "start screen=" + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode
                + " gfx=" + SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + ")");
            Inventory(cam);

            var camData = cam.GetUniversalAdditionalCameraData();
            var original = new Settings(urp, r2d, cam, camData);
            var presets = Presets(urp, r2d, cam, camData);
            var dir = Path.Combine(Log.ModDir, "bench", DateTime.Now.ToString("MMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLogMessage;

            for (int i = 0; i < presets.Count && cam != null; i++)
            {
                original.Apply();
                presets[i].Value();
                _status = "LXIUM 렌더 실험 " + (i + 1) + "/" + presets.Count + " (" + presets[i].Key + ") — 그대로 서 있어 주세요";
                yield return new WaitForSecondsRealtime(Settle);

                _frameTimes.Clear();
                _errors = 0;
                _firstError = null;
                for (float t = 0; t < Sample; t += Time.unscaledDeltaTime)
                {
                    _frameTimes.Add(Time.unscaledDeltaTime);
                    yield return null;
                }
                Log.Write(Tag + Stats(presets[i].Key, urp, r2d) + (_errors > 0 ? " errors=" + _errors + " first=" + _firstError : ""));

                // 안내 문구가 없는 프레임을 찍는다
                _status = null;
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, i.ToString("00") + "_" + presets[i].Key + ".png"));
                yield return null;
                yield return null;
            }

            Application.logMessageReceived -= OnLogMessage;
            original.Apply();
            Log.Write(Tag + "done screenshots=" + dir);
            _status = "LXIUM 렌더 실험 완료 — 원래 설정으로 돌아왔습니다";
            yield return new WaitForSecondsRealtime(4f);
            _status = null;
        }

        static List<KeyValuePair<string, Action>> Presets(UniversalRenderPipelineAsset urp, Renderer2DData r2d, Camera cam, UniversalAdditionalCameraData camData)
        {
            void Scale(float scale, UpscalingFilterSelection filter)
            {
                urp.upscalingFilter = filter;
                urp.renderScale = Mathf.Clamp(scale, 0.1f, 1f);
            }
            float pixel1 = ArtHeight / Screen.height, pixel2 = 2f * ArtHeight / Screen.height;

            var list = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("base", () => { }),
                new KeyValuePair<string, Action>("fsr75", () => Scale(0.75f, UpscalingFilterSelection.FSR)),
                new KeyValuePair<string, Action>("fsr50", () => Scale(0.5f, UpscalingFilterSelection.FSR)),
                new KeyValuePair<string, Action>("point50", () => Scale(0.5f, UpscalingFilterSelection.Point)),
                new KeyValuePair<string, Action>("pixel1x", () => Scale(pixel1, UpscalingFilterSelection.Point)),
                new KeyValuePair<string, Action>("pixel2x", () => Scale(pixel2, UpscalingFilterSelection.Point)),
                new KeyValuePair<string, Action>("hdrOff", () => cam.allowHDR = false),
                new KeyValuePair<string, Action>("postOff", () => camData.renderPostProcessing = false),
            };
            if (r2d != null && LightScale != null)
            {
                list.Insert(1, new KeyValuePair<string, Action>("light50", () => SetLightScale(r2d, 0.5f)));
                list.Insert(2, new KeyValuePair<string, Action>("light25", () => SetLightScale(r2d, 0.25f)));
                list.Add(new KeyValuePair<string, Action>("combo", () =>
                {
                    SetLightScale(r2d, 0.25f);
                    Scale(pixel2, UpscalingFilterSelection.Point);
                    cam.allowHDR = false;
                }));
            }
            return list;
        }

        static void SetLightScale(Renderer2DData r2d, float scale)
        {
            LightScale.SetValue(r2d, scale);
            r2d.SetDirty();
        }

        // 실험 전 설정. Apply로 되돌린다
        sealed class Settings
        {
            readonly UniversalRenderPipelineAsset _urp;
            readonly Renderer2DData _r2d;
            readonly Camera _cam;
            readonly UniversalAdditionalCameraData _camData;
            readonly float _scale, _lightScale;
            readonly UpscalingFilterSelection _filter;
            readonly bool _hdr, _post;

            public Settings(UniversalRenderPipelineAsset urp, Renderer2DData r2d, Camera cam, UniversalAdditionalCameraData camData)
            {
                _urp = urp; _r2d = r2d; _cam = cam; _camData = camData;
                _scale = urp.renderScale;
                _filter = urp.upscalingFilter;
                _lightScale = r2d != null && LightScale != null ? (float)LightScale.GetValue(r2d) : -1f;
                _hdr = cam.allowHDR;
                _post = camData.renderPostProcessing;
            }

            public void Apply()
            {
                _urp.renderScale = _scale;
                _urp.upscalingFilter = _filter;
                if (_lightScale >= 0f && !Mathf.Approximately((float)LightScale.GetValue(_r2d), _lightScale)) SetLightScale(_r2d, _lightScale);
                if (_cam != null) _cam.allowHDR = _hdr;
                if (_camData != null) _camData.renderPostProcessing = _post;
            }
        }

        string Stats(string name, UniversalRenderPipelineAsset urp, Renderer2DData r2d)
        {
            var sorted = _frameTimes.OrderBy(x => x).ToArray();
            float Pct(float p) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))] * 1000f;
            return name
                + " fps=" + (sorted.Length / sorted.Sum()).ToString("0.0")
                + " ms(p50/p95/p99)=" + Pct(0.5f).ToString("0.0") + "/" + Pct(0.95f).ToString("0.0") + "/" + Pct(0.99f).ToString("0.0")
                + " scale=" + urp.renderScale.ToString("0.###") + " filter=" + urp.upscalingFilter
                + " light=" + (r2d != null && LightScale != null ? ((float)LightScale.GetValue(r2d)).ToString("0.##") : "?");
        }

        void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Warning) return;
            _errors++;
            if (_firstError == null) _firstError = message.Length > 160 ? message.Substring(0, 160) : message;
        }

        static void Inventory(Camera cam)
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas).ToArray();
            Log.Write(Tag + "canvas " + string.Join(", ", canvases.GroupBy(c => c.renderMode).Select(g => g.Key + "=" + g.Count())));
            foreach (var c in canvases.Where(c => c.renderMode != RenderMode.ScreenSpaceOverlay).Take(20))
                Log.Write(Tag + "canvas " + c.renderMode + " '" + c.name + "' cam=" + (c.worldCamera != null ? c.worldCamera.name : "-")
                    + " order=" + c.sortingOrder + " layer=" + c.sortingLayerName + " plane=" + c.planeDistance.ToString("0.##"));

            var sprites = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Where(s => s.sprite != null).ToArray();
            Log.Write(Tag + "sprites=" + sprites.Length
                + " ppu=" + string.Join(",", sprites.GroupBy(s => Mathf.RoundToInt(s.sprite.pixelsPerUnit)).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key + ":" + g.Count()))
                + " filter=" + string.Join(",", sprites.Where(s => s.sprite.texture != null).GroupBy(s => s.sprite.texture.filterMode).Select(g => g.Key + ":" + g.Count())));

            // 화면의 25% 이상을 덮는 렌더러 = 큰 반투명 레이어 후보
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var p = cam.transform.position;
            var view = new Rect(p.x - w / 2f, p.y - h / 2f, w, h);
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.isVisible).ToArray();
            Log.Write(Tag + "visible " + string.Join(", ", renderers.GroupBy(r => r.GetType().Name).OrderByDescending(g => g.Count()).Select(g => g.Key + "=" + g.Count())));
            var covers = renderers
                .Select(r => new { r, cover = Overlap(view, r.bounds) / (w * h) })
                .Where(x => x.cover >= 0.25f)
                .OrderByDescending(x => x.cover)
                .Take(25);
            foreach (var x in covers)
            {
                var r = x.r;
                var alpha = r is SpriteRenderer sr ? sr.color.a.ToString("0.##") : "-";
                var parent = r.transform.parent != null ? r.transform.parent.name + "/" : "";
                Log.Write(Tag + "cover " + (x.cover * 100f).ToString("0") + "% " + r.GetType().Name + " '" + parent + r.name + "'"
                    + " layer=" + r.sortingLayerName + "/" + r.sortingOrder + " alpha=" + alpha
                    + " shader=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "-"));
            }

            var lights = FindObjectsByType<Light2D>(FindObjectsSortMode.None);
            Log.Write(Tag + "light2d=" + lights.Length
                + " shadows=" + lights.Count(l => l.shadowsEnabled)
                + " types=" + string.Join(",", lights.GroupBy(l => l.lightType).Select(g => g.Key + ":" + g.Count()))
                + " blendStyles=" + string.Join(",", lights.GroupBy(l => l.blendStyleIndex).Select(g => g.Key + ":" + g.Count())));
        }

        static float Overlap(Rect view, Bounds b)
        {
            float xMin = Mathf.Max(view.xMin, b.min.x), xMax = Mathf.Min(view.xMax, b.max.x);
            float yMin = Mathf.Max(view.yMin, b.min.y), yMax = Mathf.Min(view.yMax, b.max.y);
            return xMax > xMin && yMax > yMin ? (xMax - xMin) * (yMax - yMin) : 0f;
        }
    }
}
