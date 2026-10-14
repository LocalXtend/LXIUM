using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace LXIUM.Sephiria
{
    internal static class WorldRender
    {
        const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static bool enabled, installed;
        static float scale = .75f;
        static Material plain, sharpen;
        static Shader shader;
        static Renderer[] renderers = Array.Empty<Renderer>();
        static Canvas[] canvases = Array.Empty<Canvas>();
        static SpriteMask[] masks = Array.Empty<SpriteMask>();
        static readonly Plane[] frustum = new Plane[6];
        static int refreshFrame = -1;
        public static string Status = "off";
        public static long Scaled, Native;
        sealed class State
        {
            public RenderGraph Graph;
            public UniversalResourceData Resources;
            public TextureHandle Color, Depth, Scratch;
            public Material Material;
            public bool Restored;
        }
        sealed class CompositeData { public TextureHandle Source; public Material Material; }
        public static void Initialize()
        {
            if (installed) return;
            try
            {
                var type = typeof(Renderer2DData).Assembly.GetType("UnityEngine.Rendering.Universal.DrawRenderer2DPass", true);
                var target = type.GetMethod("Render", All);
                if (target == null || target.GetParameters().Length != 6) throw new NotSupportedException("DrawRenderer2DPass changed");
                installed = PatchBridge.Finalizer(target, typeof(WorldRender).GetMethod(nameof(OnError), All)) &&
                    PatchBridge.Postfix(target, typeof(WorldRender).GetMethod(nameof(After), All)) &&
                    PatchBridge.Prefix(target, typeof(WorldRender).GetMethod(nameof(Before), All));
                Refresh();
            }
            catch (Exception e) { Status = "hook unavailable: " + e.GetBaseException().Message; Log.Warn(Status); }
        }
        public static void Set(RenderOptions options)
        {
            enabled = options.WorldUpscale; scale = options.WorldScale;
            if (!enabled) { Status = "off"; return; }
            Initialize();
            if (plain == null) LoadShader();
            if (sharpen != null) sharpen.SetFloat("_Intensity", options.Sharpness);
            useSharpen = options.DedicatedShader;
            if (plain != null) Status = "eligible layer batches only; native UI/unknown materials";
        }
        static bool useSharpen;
        static void LoadShader()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11) { Status = "dedicated shader requires Direct3D11; native fallback"; return; }
            try
            {
                using var stream = typeof(WorldRender).Assembly.GetManifestResourceStream("lxium_sephiria.bundle");
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                var bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                if (bundle == null) throw new InvalidDataException("shader bundle not loaded");
                try { shader = bundle.LoadAsset<Shader>("lxium/worldupscale"); }
                finally { bundle.Unload(false); }
                if (shader == null || !shader.isSupported) throw new NotSupportedException("shader unsupported");
                shader.hideFlags = HideFlags.DontUnloadUnusedAsset;
                plain = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                sharpen = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                plain.SetFloat("_Intensity", 0f);
            }
            catch (Exception e) { Status = "native fallback: " + e.GetBaseException().Message; Log.Warn(Status); }
        }
        public static void Refresh()
        {
            if (refreshFrame == Time.frameCount) return;
            refreshFrame = Time.frameCount;
            renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            masks = UnityEngine.Object.FindObjectsByType<SpriteMask>(FindObjectsSortMode.None);
        }
        static bool Eligible(SortingLayerRange range, Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            int visibleSprites = 0;
            foreach (var canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.ScreenSpaceOverlay) continue;
                foreach (var renderer in canvas.GetComponentsInChildren<CanvasRenderer>())
                    for (int i = 0; i < renderer.materialCount; i++)
                        if (!ShaderCompatibility.NoDepth(renderer.GetMaterial(i), false)) return false;
                if (canvas != null && canvas.isActiveAndEnabled && canvas.renderMode != RenderMode.ScreenSpaceOverlay &&
                    InRange(canvas.sortingLayerID, range)) return false;
            }
            foreach (var mask in masks) if (mask != null && mask.enabled && mask.gameObject.activeInHierarchy) return false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 || !GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)) continue;
                // Depth written by a different native batch would not exist in the cleared scratch depth.
                foreach (var depthMaterial in renderer.sharedMaterials)
                    if (!ShaderCompatibility.NoDepth(depthMaterial, renderer.HasPropertyBlock())) return false;
                SortingGroup group = null;
                for (var node = renderer.transform; node != null; node = node.parent)
                {
                    var candidate = node.GetComponent<SortingGroup>();
                    if (candidate == null || !candidate.enabled) continue;
                    group = candidate;
                    if (candidate.sortAtRoot) break;
                }
                int layer = group != null && group.enabled ? group.sortingLayerID : renderer.sortingLayerID;
                if (!InRange(layer, range)) continue;
                if (!(renderer is SpriteRenderer sprite) || sprite.maskInteraction != SpriteMaskInteraction.None ||
                    sprite.drawMode != SpriteDrawMode.Simple || renderer.HasPropertyBlock()) return false;
                var material = renderer.sharedMaterial;
                if (material == null || material.shader == null || material.renderQueue != 3000 ||
                    material.shader.name != "Universal Render Pipeline/2D/Sprite-Lit-Default" &&
                    material.shader.name != "Universal Render Pipeline/2D/Sprite-Unlit-Default") return false;
                if (material.HasProperty("_ZWrite") && material.GetFloat("_ZWrite") != 0f) return false;
                visibleSprites++;
            }
            return visibleSprites > 0;
        }
        static bool InRange(int layer, SortingLayerRange range)
        {
            int value = SortingLayer.GetLayerValueFromID(layer);
            return value >= range.lowerBound && value <= range.upperBound;
        }
        static void Before(object[] __args, ref State __state)
        {
            __state = null;
            if (!enabled || !installed || plain == null || Pipeline.Current == null) return;
            try
            {
                var frame = (ContextContainer)__args[1];
                if (!ShaderCompatibility.SupportedVersion || !Pipeline.WorldDepthSafe) { Native++; Status = "native fallback: unverified version/renderer depth"; return; }
                var cameraData = frame.Get<UniversalCameraData>();
                var camera = cameraData.camera;
                var resources = frame.Get<UniversalResourceData>();
                if (GameCamera.Instance == null || camera != GameCamera.Instance.Camera || camera.stereoEnabled ||
                    cameraData.renderType != CameraRenderType.Base || camera.targetTexture != null || resources.isActiveTargetBackBuffer)
                { Native++; return; }
                var range = ((FilteringSettings)__args[5]).sortingLayerRange;
                // New mod-created renderers must be checked before this frame's eligibility decision.
                Refresh();
                var batches = (Array)__args[3]; var batch = batches.GetValue((int)__args[4]);
                object stats = batch.GetType().GetField("lightStats", All).GetValue(batch);
                if ((bool)stats.GetType().GetProperty("useVolumetricLights", All).GetValue(stats) || !Eligible(range, camera))
                { Native++; return; }
                var graph = (RenderGraph)__args[0];
                var color = resources.cameraColor; var depth = resources.cameraDepth;
                var desc = graph.GetTextureDesc(color);
                if (desc.sizeMode != TextureSizeMode.Explicit || desc.msaaSamples != MSAASamples.None || desc.dimension != TextureDimension.Tex2D)
                { Native++; return; }
                desc.width = Math.Max(1, (int)(desc.width * scale)); desc.height = Math.Max(1, (int)(desc.height * scale));
                desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                desc.clearBuffer = true; desc.clearColor = Color.clear; desc.name = "LXIUM world layer";
                var scratch = graph.CreateTexture(desc);
                TextureHandle scratchDepth = TextureHandle.nullHandle;
                if (depth.IsValid())
                {
                    var depthDesc = graph.GetTextureDesc(depth);
                    depthDesc.width = desc.width; depthDesc.height = desc.height;
                    depthDesc.clearBuffer = true; depthDesc.name = "LXIUM layer depth";
                    scratchDepth = graph.CreateTexture(depthDesc);
                }
                __state = new State { Graph = graph, Resources = resources, Color = color, Depth = depth, Scratch = scratch,
                    Material = useSharpen ? sharpen : plain };
                resources.cameraColor = scratch; resources.cameraDepth = scratchDepth;
            }
            catch (Exception e) { Restore(__state); __state = null; enabled = false; Status = "native fallback: " + e.GetBaseException().Message; Log.Warn(Status); }
        }
        static void Restore(State state)
        {
            if (state == null || state.Restored) return;
            state.Resources.cameraColor = state.Color; state.Resources.cameraDepth = state.Depth; state.Restored = true;
        }
        static void After(State __state)
        {
            if (__state == null) return;
            Restore(__state);
            try
            {
                using var builder = __state.Graph.AddRasterRenderPass<CompositeData>("LXIUM upscale world layer", out var data);
                data.Source = __state.Scratch; data.Material = __state.Material;
                builder.UseTexture(data.Source);
                // Blending needs the previously composed native layers; a write-only attachment discards them.
                builder.SetRenderAttachment(__state.Color, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc((CompositeData pass, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, pass.Source, new Vector4(1f, 1f, 0f, 0f), pass.Material, 0));
                Scaled++;
            }
            catch (Exception e) { enabled = false; Status = "composite failed: " + e.GetBaseException().Message; Log.Warn(Status); throw; }
        }
        static Exception OnError(Exception __exception, State __state) { Restore(__state); return __exception; }
        public static void Dispose()
        {
            enabled = false;
            if (plain != null) UnityEngine.Object.Destroy(plain);
            if (sharpen != null) UnityEngine.Object.Destroy(sharpen);
            if (shader != null) UnityEngine.Object.Destroy(shader);
            plain = sharpen = null; shader = null;
        }
        public static void ResetHooks() => installed = false;
    }
}
