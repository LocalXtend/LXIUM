using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LXIUM_sephiria
{
    /// <summary>
    /// 지금 장면의 렌더 상태(파이프라인, 카메라, 2D 조명, 볼륨, 렌더러, UI, 스크립트 수)를 텍스트로 덤프한다.
    /// 전체 오브젝트 검색이라 수십 ms 걸릴 수 있어 단축키·장면 전환 때만 부른다.
    /// </summary>
    public static class Probe
    {
        const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Environment() =>
            $"game={Application.version} unity={Application.unityVersion} gfx={SystemInfo.graphicsDeviceType} " +
            $"({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize}MB, SM{SystemInfo.graphicsShaderLevel / 10.0:F1}) " +
            $"cpu={SystemInfo.processorType} x{SystemInfo.processorCount} screen={Screen.width}x{Screen.height} {Screen.fullScreenMode} " +
            $"vsync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
            $"frameTiming={FrameTimingManager.IsFeatureEnabled()}";

        public static string SceneName()
        {
            var names = new string[SceneManager.sceneCount];
            for (int i = 0; i < names.Length; i++) names[i] = SceneManager.GetSceneAt(i).name;
            return string.Join("+", names);
        }

        /// <summary>덤프를 logs/probe-장면-시각.txt로 쓰고 경로를 돌려준다.</summary>
        public static string Dump(string logDir)
        {
            var sb = new StringBuilder();
            string scene = SceneName();
            sb.AppendLine($"# LXIUM probe {DateTime.Now:yyyy-MM-dd HH:mm:ss} scene={scene} frame={Time.frameCount}");
            sb.AppendLine(Environment());
            Section(sb, "pipeline", Pipeline);
            Section(sb, "sorting layers", SortingLayers);
            Section(sb, "cameras", Cameras);
            Section(sb, "2D lights", Lights);
            Section(sb, "volumes", Volumes);
            Section(sb, "renderers", Renderers);
            Section(sb, "ui", Ui);
            Section(sb, "scripts", Scripts);
            Directory.CreateDirectory(logDir);
            string path = Path.Combine(logDir, $"probe-{scene.Replace('+', '_')}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        static void Section(StringBuilder sb, string title, Action<StringBuilder> body)
        {
            sb.AppendLine().AppendLine("## " + title);
            try { body(sb); }
            catch (Exception e) { sb.AppendLine("  (실패: " + e.GetType().Name + " " + e.Message + ")"); }
        }

        static void Pipeline(StringBuilder sb)
        {
            var asset = Render.Asset;
            if (asset == null) { sb.AppendLine("  URP 에셋 아님: " + GraphicsSettings.currentRenderPipeline); return; }
            sb.Append("  asset '").Append(asset.name).Append("' ");
            Fields(sb, asset);
            sb.AppendLine();
            foreach (var d in Render.Datas)
            {
                if (d == null) { sb.AppendLine("  renderer: 2D 아님"); continue; }
                sb.Append("  renderer2D '").Append(d.name).Append("' ");
                Fields(sb, d);
                sb.AppendLine();
                sb.Append("    features: ").AppendLine(string.Join(", ", d.rendererFeatures.Select(f => f == null ? "null" : f.name + (f.isActive ? "" : "(off)"))));
                if (d.GetType().GetField("m_LightBlendStyles", AllInstance)?.GetValue(d) is Array styles)
                    for (int i = 0; i < styles.Length; i++)
                    {
                        sb.Append("    blendStyle[").Append(i).Append("] ");
                        Fields(sb, styles.GetValue(i));
                        sb.AppendLine();
                    }
            }
        }

        static void SortingLayers(StringBuilder sb)
        {
            foreach (var l in SortingLayer.layers) sb.AppendLine($"  {l.value,3} {l.name} (id {l.id})");
        }

        static void Cameras(StringBuilder sb)
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(c => c.depth))
            {
                var rt = c.targetTexture;
                sb.Append($"  '{c.name}' on={c.isActiveAndEnabled} depth={c.depth} ortho={c.orthographic}/{c.orthographicSize:F3} px={c.pixelWidth}x{c.pixelHeight} ");
                sb.Append($"target={(rt ? $"{rt.width}x{rt.height} {rt.graphicsFormat} aa{rt.antiAliasing}" : "screen")} clear={c.clearFlags} hdr={c.allowHDR} msaa={c.allowMSAA} ");
                sb.Append($"mask=[{LayerNames(c.cullingMask)}]");
                if (c.TryGetComponent(out UniversalAdditionalCameraData u))
                    sb.Append($" urp(type={u.renderType} post={u.renderPostProcessing} aa={u.antialiasing} stack={u.cameraStack?.Count ?? 0} volMask=[{LayerNames(u.volumeLayerMask)}])");
                sb.Append(" scripts=").AppendLine(string.Join(",", c.GetComponents<MonoBehaviour>().Where(m => m).Select(m => m.GetType().Name)));
            }
        }

        static void Lights(StringBuilder sb)
        {
            var lights = Object.FindObjectsByType<Light2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var on = lights.Where(l => l.isActiveAndEnabled).ToArray();
            sb.AppendLine($"  total={lights.Length} enabled={on.Length}");
            Histogram(sb, "type", on.Select(l => l.lightType.ToString()));
            Histogram(sb, "blendStyle", on.Select(l => l.blendStyleIndex.ToString()));
            Histogram(sb, "targets", on.Select(l => string.Join("+", l.targetSortingLayers.Select(SortingLayer.IDToName))));
            sb.AppendLine($"  shadows={on.Count(l => l.shadowsEnabled)} volumetric={on.Count(l => l.volumetricEnabled)} cookie={on.Count(l => l.lightCookieSprite)} normalMap={on.Count(l => l.normalMapQuality != Light2D.NormalMapQuality.Disabled)}");
            foreach (var l in on.Take(200))
            {
                sb.Append($"  - '{Path2(l.transform)}' {l.lightType} style={l.blendStyleIndex} i={l.intensity:F2} col={l.color} order={l.lightOrder} ");
                if (l.lightType == Light2D.LightType.Point) sb.Append($"r={l.pointLightInnerRadius:F2}/{l.pointLightOuterRadius:F2} ");
                sb.Append($"layers={l.targetSortingLayers.Length} shadow={l.shadowsEnabled} vol={l.volumetricEnabled} cookie={(l.lightCookieSprite ? l.lightCookieSprite.name : "-")} ");
                sb.Append("scripts=").AppendLine(string.Join(",", l.GetComponents<MonoBehaviour>().Where(m => m && !(m is Light2D)).Select(m => m.GetType().Name)));
            }
        }

        static void Volumes(StringBuilder sb)
        {
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var profile = v.sharedProfile;
                sb.AppendLine($"  '{Path2(v.transform)}' on={v.isActiveAndEnabled} global={v.isGlobal} weight={v.weight:F2} priority={v.priority} profile={(profile ? profile.name : "-")}");
                if (!profile) continue;
                foreach (var c in profile.components)
                {
                    sb.Append("    ").Append(c.GetType().Name).Append(c.active ? "" : "(off)").Append(':');
                    for (var t = c.GetType(); t != null && t != typeof(VolumeComponent); t = t.BaseType)
                        foreach (var f in t.GetFields(AllInstance | BindingFlags.DeclaredOnly))
                            if (f.GetValue(c) is VolumeParameter p && p.overrideState)
                                sb.Append(' ').Append(f.Name).Append('=').Append(ParamValue(p));
                    sb.AppendLine();
                }
            }
        }

        static void Renderers(StringBuilder sb)
        {
            var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
            var vis = all.Where(r => r.isVisible).ToArray();
            sb.AppendLine($"  enabled={all.Length} visible={vis.Length} visibleWithPropertyBlock={vis.Count(r => r.HasPropertyBlock())} " +
                          $"materials={vis.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count()} instancedMaterials={vis.SelectMany(r => r.sharedMaterials).Where(m => m && m.name.EndsWith("(Instance)")).Distinct().Count()}");
            sb.AppendLine("  [보이는 것] 종류 | 셰이더 : 수");
            Histogram(sb, "", vis.Select(r => r.GetType().Name + " | " + (r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "-")), 60);
            Histogram(sb, "sortingLayer(visible)", vis.Select(r => r.sortingLayerName));
            var sprites = vis.OfType<SpriteRenderer>().Where(s => s.sprite).ToArray();
            Histogram(sb, "spritePPU(visible)", sprites.Select(s => s.sprite.pixelsPerUnit.ToString("0.##")));
            Histogram(sb, "spriteDrawMode(visible)", sprites.Select(s => s.drawMode + "/" + s.maskInteraction));
            foreach (var t in all.OfType<TilemapRenderer>())
                sb.AppendLine($"  tilemap '{Path2(t.transform)}' mode={t.mode} visible={t.isVisible} layer={t.sortingLayerName}/{t.sortingOrder} tiles={t.GetComponent<Tilemap>()?.GetUsedTilesCount()}");
            var ps = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            sb.AppendLine($"  particleSystems={ps.Length} playing={ps.Count(p => p.isPlaying)} particles={ps.Sum(p => p.particleCount)}");
        }

        static void Ui(StringBuilder sb)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.isRootCanvas).OrderBy(c => c.sortingOrder))
            {
                var graphics = c.GetComponentsInChildren<Graphic>(false);
                sb.AppendLine($"  '{c.name}' on={c.isActiveAndEnabled} mode={c.renderMode} order={c.sortingOrder} scale={c.scaleFactor:F2} pixelPerfect={c.pixelPerfect} camera={(c.worldCamera ? c.worldCamera.name : "-")} " +
                              $"graphics={graphics.Length} active={graphics.Count(g => g.isActiveAndEnabled)} tmp={graphics.OfType<TMP_Text>().Count(t => t.isActiveAndEnabled)} subCanvases={c.GetComponentsInChildren<Canvas>(false).Length - 1}");
            }
            var texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            sb.AppendLine($"  TMP_Text={texts.Length} enabled={texts.Count(t => t.isActiveAndEnabled)} world(TextMeshPro)={texts.Count(t => t is TextMeshPro)}");
        }

        static readonly Dictionary<Type, string> Callbacks = new Dictionary<Type, string>();

        static void Scripts(StringBuilder sb)
        {
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(m => m && m.isActiveAndEnabled).ToArray();
            var groups = all.GroupBy(m => m.GetType()).Select(g => (type: g.Key, count: g.Count(), cb: CallbacksOf(g.Key))).OrderByDescending(g => g.count).ToArray();
            sb.AppendLine($"  enabled={all.Length} types={groups.Length} withUpdate={groups.Where(g => g.cb.Contains('U')).Sum(g => g.count)} withLateUpdate={groups.Where(g => g.cb.Contains('L')).Sum(g => g.count)}");
            foreach (var g in groups.Take(60)) sb.AppendLine($"  {g.count,5} {g.type.FullName} [{g.cb}]");
        }

        /// <summary>U = Update, L = LateUpdate, F = FixedUpdate (상속 포함)</summary>
        static string CallbacksOf(Type t)
        {
            if (Callbacks.TryGetValue(t, out var s)) return s;
            bool Has(string name) => t.GetMethod(name, AllInstance, null, Type.EmptyTypes, null) != null;
            s = (Has("Update") ? "U" : "") + (Has("LateUpdate") ? "L" : "") + (Has("FixedUpdate") ? "F" : "");
            Callbacks[t] = s;
            return s;
        }

        static void Histogram(StringBuilder sb, string title, IEnumerable<string> keys, int top = 30)
        {
            if (title.Length > 0) sb.Append("  ").Append(title).Append(": ");
            var items = keys.GroupBy(k => k).OrderByDescending(g => g.Count()).Take(top).Select(g => $"{g.Key}={g.Count()}");
            if (title.Length > 0) sb.AppendLine(string.Join(", ", items));
            else foreach (var i in items) sb.Append("    ").AppendLine(i);
        }

        /// <summary>값 형식(숫자·열거·문자열·벡터·색) 필드는 값을, 배열은 길이를, Unity 오브젝트는 이름을 한 줄로 쓴다.</summary>
        static void Fields(StringBuilder sb, object o)
        {
            for (var t = o.GetType(); t != null && t != typeof(Object) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(AllInstance | BindingFlags.DeclaredOnly))
                {
                    object v;
                    try { v = f.GetValue(o); } catch { continue; }
                    if (v == null) continue;
                    if (v is Array a) sb.Append(f.Name).Append("[]=").Append(a.Length).Append(' ');
                    else if (v is Object uo) sb.Append(f.Name).Append('=').Append(uo ? uo.name : "null").Append(' ');
                    else if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || v is string || v is Vector2 || v is Vector3 || v is Vector4 || v is Color || v is LayerMask)
                        sb.Append(f.Name).Append('=').Append(Value(v)).Append(' ');
                }
        }

        static string Value(object v) => v switch
        {
            null => "null",
            Object uo => uo ? uo.name : "null",
            LayerMask m => "[" + LayerNames(m) + "]",
            float f => f.ToString("0.####"),
            _ => v.ToString(),
        };

        static string ParamValue(VolumeParameter p)
        {
            try { return Value(p.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(p)); }
            catch (Exception e) { return "?" + e.GetType().Name; }
        }

        static string LayerNames(int mask)
        {
            if (mask == -1) return "all";
            var names = new List<string>();
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) names.Add(LayerMask.LayerToName(i) is { Length: > 0 } n ? n : i.ToString());
            return string.Join(",", names);
        }

        static string Path2(Transform t) => t.parent ? t.parent.name + "/" + t.name : t.name;
    }
}
