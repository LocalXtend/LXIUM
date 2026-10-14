#nullable enable
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LXIUM.Sephiria;

internal static class SpriteBatch
{
    private static bool enabled;
    private static UniversalRenderPipelineAsset? asset;
    private static bool original;
    public static string Status { get; private set; } = "off";

    public static void Set(bool value)
    {
        enabled = value;
        if (!value) Dispose();
        else Tick(null);
    }

    public static void Tick(Camera? camera)
    {
        if (!enabled) return;
        var current = Pipeline.Current;
        if (current != asset)
        {
            Restore();
            asset = current;
            if (asset) original = asset.supportsDynamicBatching;
        }
        if (!asset)
        {
            Status = "waiting for the cloned URP asset";
            return;
        }
        asset.supportsDynamicBatching = true;
        // Unity retains transparent order and rejects incompatible material, mask and shader draws.
        Status = "native dynamic batching enabled; actual batches require GPU measurement";
    }

    private static void Restore()
    {
        if (asset) asset.supportsDynamicBatching = original;
        asset = null;
    }

    public static void Dispose()
    {
        Restore();
        enabled = false;
        Status = "off";
    }
}
