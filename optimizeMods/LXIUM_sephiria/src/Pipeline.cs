using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LXIUM.Sephiria
{
    internal static class Pipeline
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly FieldInfo DataList = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", All);
        static readonly FieldInfo Renderers = typeof(UniversalRenderPipelineAsset).GetField("m_Renderers", All);
        static readonly FieldInfo LightScale = typeof(Renderer2DData).GetField("m_LightRenderTextureScale", All);
        static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        static readonly Dictionary<Renderer2DData, float> originalScale = new Dictionary<Renderer2DData, float>();
        static RenderPipelineAsset previousQuality;
        static bool idleInstalled;
        public static UniversalRenderPipelineAsset Current { get; private set; }
        public static RenderOptions Options = new RenderOptions();
        public static string Status = "waiting for URP";
        public static long PassSkipped;
        public static bool WorldDepthSafe { get; private set; }

        public static void Set(RenderOptions value)
        {
            Options = value.Copy(); Options.Validate();
            Ensure();
            LightCache.Set(Options.StaticLights);
            SpriteBatch.Set(Options.SpriteBatch);
            WorldRender.Set(Options);
        }
        static bool Any => Options.LightResolution || Options.IdlePasses || Options.StaticLights || Options.SpriteBatch || Options.WorldUpscale;
        public static void Ensure()
        {
            if (!Any) { Dispose(); Status = "original game pipeline"; return; }
            if (Current != null && QualitySettings.renderPipeline == Current) return;
            Dispose();
            var source = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (source == null || DataList == null || Renderers == null || LightScale == null) { Status = "unsupported URP layout"; return; }
            previousQuality = QualitySettings.renderPipeline;
            try
            {
                Current = UnityEngine.Object.Instantiate(source);
                Current.name = "LXIUM " + source.name;
                owned.Add(Current);
                var originals = (ScriptableRendererData[])DataList.GetValue(source);
                var copies = new ScriptableRendererData[originals.Length];
                WorldDepthSafe = true;
                for (int i = 0; i < copies.Length; i++)
                {
                    if (originals[i] == null) continue;
                    if (!(originals[i] is Renderer2DData)) WorldDepthSafe = false;
                    copies[i] = UnityEngine.Object.Instantiate(originals[i]); owned.Add(copies[i]);
                    copies[i].rendererFeatures.Clear();
                    foreach (var feature in originals[i].rendererFeatures)
                    {
                        if (feature == null) continue;
                        if (!(feature is FullScreenPassRendererFeature full) || full.bindDepthStencilAttachment ||
                            full.injectionPoint != FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing)
                            WorldDepthSafe = false;
                        var copy = UnityEngine.Object.Instantiate(feature);
                        owned.Add(copy); copies[i].rendererFeatures.Add(copy);
                    }
                    if (copies[i] is Renderer2DData twoD) originalScale[twoD] = (float)LightScale.GetValue(twoD);
                }
                DataList.SetValue(Current, copies);
                Renderers.SetValue(Current, new ScriptableRenderer[copies.Length]);
                QualitySettings.renderPipeline = Current;
                Status = "owned URP/Renderer2D clones";
                if (!idleInstalled)
                    idleInstalled = PatchBridge.Prefix(typeof(FullScreenPassRendererFeature).GetMethod("AddRenderPasses", All),
                        typeof(Pipeline).GetMethod(nameof(BeforeFeature), All));
            }
            catch (Exception e) { Dispose(); Status = "pipeline unsupported: " + e.GetBaseException().Message; Log.Warn(Status); }
        }
        public static void Tick()
        {
            if (Current == null) return;
            foreach (var pair in originalScale)
                LightScale.SetValue(pair.Key, Options.LightResolution ? Options.LightScale : pair.Value);
            SpriteBatch.Tick(null);
        }
        static bool BeforeFeature(FullScreenPassRendererFeature __instance, ref RenderingData renderingData)
        {
            if (!Options.IdlePasses || Current == null || GameCamera.Instance == null ||
                renderingData.cameraData.camera != GameCamera.Instance.Camera || renderingData.cameraData.camera.targetTexture != null ||
                renderingData.cameraData.camera.stereoEnabled || renderingData.cameraData.renderScale != 1f ||
                !renderingData.cameraData.isDefaultViewport || __instance.injectionPoint != FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing)
                return true;
            var material = __instance.passMaterial;
            if (material == null || material.shader == null || __instance.passIndex != 0) return true;
            // Both shipped effects force alpha=1. Only alpha-less targets permit an identity bypass.
            if (GraphicsFormatUtility.HasAlphaChannel(renderingData.cameraData.cameraTargetDescriptor.graphicsFormat)) return true;
            bool idle = material.shader.name == "Shader Graphs/FS-Distortion" && Zero(material, "_Intensity") ||
                material.shader.name == "Shader Graphs/FS-Glitch" && Zero(material, "_GlitchIntensity") &&
                Zero(material, "_ColorIntensity");
            if (!idle) return true;
            PassSkipped++;
            return false;
        }
        static bool Zero(Material material, string name) => material.HasProperty(name) && material.GetFloat(name) == 0f;
        public static string Effective => Status + "; light=" + (Options.LightResolution && Current != null ? Options.LightScale.ToString("F2") : "original") +
            "; idleHook=" + idleInstalled + "; cache=" + LightCache.Status + "; batching=" + SpriteBatch.Status + "; world=" + WorldRender.Status;
        public static void Dispose()
        {
            if (Current != null && QualitySettings.renderPipeline == Current) QualitySettings.renderPipeline = previousQuality;
            Current = null;
            originalScale.Clear();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.Destroy(owned[i]);
            owned.Clear();
        }
        public static void ResetHooks() => idleInstalled = false;
    }
}
