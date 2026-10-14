#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LXIUM.Sephiria;

internal static class LightCache
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Budget = 64L * 1024 * 1024;
    private static readonly Dictionary<(int camera, int renderer, int batch), Entry> entries = new();
    private static bool enabled, installed, attempted;
    private static MethodInfo? getResources;
    private static PropertyInfo? lightTextures;
    private static long allocated;
    public static long Hits, Misses, Fallbacks;
    public static string Status { get; private set; } = "off";

    private sealed class Entry
    {
        public object[] Snapshot = Array.Empty<object>();
        public RTHandle[] Targets = Array.Empty<RTHandle>();
        public int LastFrame, Generation;
        public long Bytes;
        public bool Ready;
    }

    private sealed class Pending
    {
        public Entry Entry = null!;
        public RenderGraph Graph = null!;
        public TextureHandle[] Sources = null!;
        public TextureDesc[] Descriptors = null!;
    }

    private sealed class CopyData
    {
        public Entry Entry = null!;
        public int Generation;
        public TextureHandle[] Sources = null!, Targets = null!;
    }

    public static void Set(bool value)
    {
        enabled = value;
        if (!value) { Dispose(); return; }
        if (installed) { Status = "waiting for stationary supported light batches"; return; }
        if (attempted) { enabled = false; return; }
        attempted = true;
        try
        {
            var assembly = typeof(Light2D).Assembly;
            var pass = assembly.GetType("UnityEngine.Rendering.Universal.DrawLight2DPass", true)!;
            var resources = typeof(UniversalCameraData).Assembly.GetType("UnityEngine.Rendering.Universal.Universal2DResourceData", true)!;
            getResources = typeof(ContextContainer).GetMethod("Get")!.MakeGenericMethod(resources);
            lightTextures = resources.GetProperty("lightTextures", All) ?? throw new MissingMemberException("lightTextures");
            var render = pass.GetMethod("Render", All) ?? throw new MissingMethodException("DrawLight2DPass.Render");
            if (render.GetParameters().Length != 6) throw new NotSupportedException("DrawLight2DPass layout changed");
            // Postfix is registered first; it is inert unless this prefix supplies a pending copy.
            if (!PatchBridge.Postfix(render, typeof(LightCache).GetMethod(nameof(After), All)!) ||
                !PatchBridge.Prefix(render, typeof(LightCache).GetMethod(nameof(Before), All)!))
                throw new NotSupportedException("the render hook could not be installed");
            installed = true;
            Status = "waiting for stationary supported light batches";
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static bool Before(object[] __args, ref Pending? __state)
    {
        __state = null;
        if (!enabled) return true;
        try
        {
            if ((bool)__args[5] || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return Fallback("volumetric or unsupported texture copying");
            var graph = (RenderGraph)__args[0];
            var frame = (ContextContainer)__args[1];
            var renderer = (Renderer2DData)__args[2];
            var batch = __args[3];
            int index = (int)__args[4];
            var rendererType = typeof(Light2D).Assembly.GetType("UnityEngine.Rendering.Universal.Renderer2D", true)!;
            if (!(bool)(rendererType.GetField("s_IsLightingActive", All)?.GetValue(null) ?? false)) return Fallback("lighting disabled");
            var stats = Field(batch, "lightStats");
            if (!(bool)Property(stats, "useLights") || (bool)Property(stats, "useShadows") ||
                (bool)Property(stats, "useNormalMap") || (bool)Property(stats, "useVolumetricLights") ||
                (bool)Field(batch, "useNormals")) return Fallback("shadows, normals, volumetrics or no lights");
            var cameraData = frame.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            if (!camera || Pipeline.Current == null || GameCamera.Instance == null || camera != GameCamera.Instance.Camera ||
                camera.cameraType != CameraType.Game || camera.stereoEnabled || cameraData.renderType != CameraRenderType.Base ||
                camera.targetTexture != null) return Fallback("unsupported camera");
            var resources = getResources!.Invoke(frame, null)!;
            var textures = (TextureHandle[][])lightTextures!.GetValue(resources)!;
            var sources = textures[index];
            if (sources.Length == 0) return Fallback("no light textures");
            var descriptors = new TextureDesc[sources.Length];
            var snapshot = new List<object> { cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix(),
                cameraData.cameraTargetDescriptor, camera.pixelRect,
                camera.cullingMask, camera.orthographic, renderer.hdrEmulationScale, Field(batch, "startLayerID"),
                Field(batch, "endLayerValue"), Field(batch, "layerRange"), stats };
            foreach (var style in renderer.lightBlendStyles) snapshot.Add(style);
            foreach (int blend in (int[])Field(batch, "activeBlendStylesIndices")) snapshot.Add(blend);
            for (int i = 0; i < sources.Length; i++)
            {
                if (!sources[i].IsValid()) return Fallback("invalid light target");
                var desc = graph.GetTextureDesc(sources[i]);
                if (desc.dimension != TextureDimension.Tex2D || desc.slices != 1 || desc.msaaSamples != MSAASamples.None ||
                    desc.useDynamicScale || desc.useDynamicScaleExplicit || desc.useMipMap || desc.depthBufferBits != DepthBits.None ||
                    desc.format == GraphicsFormat.None) return Fallback("unsupported light target layout");
                var size = desc.CalculateFinalDimensions();
                desc.width = size.x; desc.height = size.y; desc.sizeMode = TextureSizeMode.Explicit;
                descriptors[i] = desc;
                snapshot.Add(desc);
            }
            foreach (Light2D light in (IEnumerable)Field(batch, "lights"))
            {
                if (!light || light.lightCookieSprite || light.volumetricEnabled ||
                    light.normalMapQuality != Light2D.NormalMapQuality.Disabled) return Fallback("unsupported light features");
                snapshot.Add(light.GetInstanceID()); snapshot.Add(light.isActiveAndEnabled);
                snapshot.Add(light.transform.localToWorldMatrix); snapshot.Add(light.lightType);
                snapshot.Add(light.blendStyleIndex); snapshot.Add(light.color); snapshot.Add(light.intensity);
                snapshot.Add(light.falloffIntensity); snapshot.Add(light.overlapOperation); snapshot.Add(light.lightOrder);
                snapshot.Add(light.pointLightInnerAngle); snapshot.Add(light.pointLightOuterAngle);
                snapshot.Add(light.pointLightInnerRadius); snapshot.Add(light.pointLightOuterRadius);
                snapshot.Add(light.shapeLightFalloffSize); snapshot.Add(light.shapeLightParametricRadius);
                snapshot.Add(light.shapeLightParametricSides); snapshot.Add(light.shapeLightParametricAngleOffset);
                snapshot.Add(Field(light, "m_ShapeLightFalloffOffset")); snapshot.Add(light.normalMapDistance);
                snapshot.Add(light.targetSortingLayers.Length);
                foreach (int layer in light.targetSortingLayers) snapshot.Add(layer);
                var path = light.shapePath;
                snapshot.Add(path?.Length ?? 0);
                if (path != null) foreach (var vertex in path) snapshot.Add(vertex);
            }
            var materials = (IDictionary)Property(renderer, "lightMaterials");
            foreach (DictionaryEntry material in materials)
            {
                snapshot.Add(material.Key);
                var value = (Material)material.Value;
                if (!value || !value.shader || value.shader.name != "Hidden/Light2D") return Fallback("custom light shader");
                snapshot.Add(value ? value.ComputeCRC() : 0);
            }
            var key = (camera.GetInstanceID(), renderer.GetInstanceID(), index);
            if (!entries.TryGetValue(key, out var entry))
            {
                // shortcut: at most 16 layer batches and 64 MiB; raise only after measuring cache coverage.
                if (entries.Count >= 16) return Fallback("light cache entry limit");
                entry = new Entry(); entries.Add(key, entry);
            }
            bool unchanged = Same(entry.Snapshot, snapshot);
            int previousFrame = entry.LastFrame;
            entry.LastFrame = Time.frameCount;
            if (!unchanged)
            {
                entry.Snapshot = snapshot.ToArray();
                entry.Ready = false; entry.Generation++;
                Misses++;
                Status = "light state changed; native redraw";
                return true;
            }
            if (entry.Ready && entry.Targets.Length == sources.Length)
            {
                var imported = new TextureHandle[sources.Length];
                for (int i = 0; i < imported.Length; i++)
                {
                    if (entry.Targets[i].rt == null || !entry.Targets[i].rt.IsCreated()) { entry.Ready = false; return true; }
                    imported[i] = graph.ImportTexture(entry.Targets[i]);
                }
                textures[index] = imported;
                Hits++;
                Status = "reusing stationary GPU light textures";
                return false;
            }
            // A continuously moving batch never pays for a persistent texture copy.
            if (previousFrame != Time.frameCount - 1) return true;
            __state = new Pending { Entry = entry, Graph = graph, Sources = (TextureHandle[])sources.Clone(), Descriptors = descriptors };
            return true;
        }
        catch (Exception ex) { Fail(ex); return true; }
    }

    private static void After(Pending? __state)
    {
        if (!enabled || __state == null) return;
        try
        {
            var pending = __state;
            var entry = pending.Entry;
            Release(entry);
            long bytes = 0;
            foreach (var desc in pending.Descriptors)
                bytes += (long)desc.width * desc.height * GraphicsFormatUtility.GetBlockSize(desc.format);
            if (allocated + bytes > Budget) { Status = "light cache memory limit; native redraw"; return; }
            entry.Targets = new RTHandle[pending.Descriptors.Length];
            entry.Bytes = bytes; allocated += bytes;
            var targets = new TextureHandle[entry.Targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                var desc = pending.Descriptors[i];
                var texture = new RenderTexture(new RenderTextureDescriptor(desc.width, desc.height)
                    { graphicsFormat = desc.format, depthStencilFormat = GraphicsFormat.None, msaaSamples = 1,
                      dimension = TextureDimension.Tex2D, volumeDepth = 1, useMipMap = false })
                    { name = "LXIUM static light", filterMode = desc.filterMode, wrapMode = desc.wrapMode,
                      hideFlags = HideFlags.HideAndDontSave };
                if (!texture.Create()) { UnityEngine.Object.Destroy(texture); throw new InvalidOperationException("light cache allocation failed"); }
                entry.Targets[i] = RTHandles.Alloc(texture, transferOwnership: true);
                targets[i] = pending.Graph.ImportTexture(entry.Targets[i]);
            }
            using var builder = pending.Graph.AddUnsafePass<CopyData>("LXIUM Cache Light2D", out var data);
            data.Entry = entry; data.Generation = entry.Generation; data.Sources = pending.Sources; data.Targets = targets;
            for (int i = 0; i < targets.Length; i++)
            {
                builder.UseTexture(data.Sources[i], AccessFlags.Read);
                builder.UseTexture(targets[i], AccessFlags.Write);
            }
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((CopyData copy, UnsafeGraphContext context) =>
            {
                try
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    for (int i = 0; i < copy.Targets.Length; i++) cmd.CopyTexture((RTHandle)copy.Sources[i], (RTHandle)copy.Targets[i]);
                    if (copy.Generation == copy.Entry.Generation) copy.Entry.Ready = true;
                }
                catch (Exception ex) { copy.Entry.Ready = false; Fail(ex); }
            });
            Status = "filling stationary GPU light cache";
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static bool Same(IReadOnlyList<object> first, IReadOnlyList<object> second)
    {
        if (first.Count != second.Count) return false;
        for (int i = 0; i < first.Count; i++) if (!Equals(first[i], second[i])) return false;
        return true;
    }

    private static object Field(object value, string name) => value.GetType().GetField(name, All)?.GetValue(value)
        ?? throw new MissingFieldException(value.GetType().FullName, name);
    private static object Property(object value, string name) => value.GetType().GetProperty(name, All)?.GetValue(value)
        ?? throw new MissingMemberException(value.GetType().FullName, name);
    private static bool Fallback(string reason) { Misses++; Fallbacks++; Status = "native redraw: " + reason; return true; }
    private static void Fail(Exception ex)
    {
        enabled = false;
        Status = "unavailable: " + ex.GetBaseException().Message;
        Log.Warn("Static light cache disabled: " + Status);
    }
    private static void Release(Entry entry)
    {
        foreach (var handle in entry.Targets)
        {
            if (handle == null) continue;
            handle.Release();
        }
        entry.Targets = Array.Empty<RTHandle>(); allocated -= entry.Bytes; entry.Bytes = 0; entry.Ready = false;
    }
    public static void Dispose()
    {
        enabled = false;
        foreach (var entry in entries.Values) Release(entry);
        entries.Clear();
        Status = "off";
    }
    public static void ResetHooks() { installed = attempted = false; }
}
