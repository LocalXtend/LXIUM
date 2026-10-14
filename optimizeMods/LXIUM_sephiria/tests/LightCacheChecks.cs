using System;
using System.Reflection;

internal static class LightCacheChecks
{
    public static void Run(Assembly modAssembly)
    {
        var same = modAssembly.GetType("LXIUM.Sephiria.LightCache", true)!
            .GetMethod("Same", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Equal(object[] a, object[] b) => (bool)same.Invoke(null, new object[] { a, b })!;
        var original = new object[] { "camera", 1920, 1080, 1f, "light", 12, 4, 9, 0.5f };
        Check(Equal(original, (object[])original.Clone()), "stationary snapshots with new allocations must match");
        var resized = (object[])original.Clone(); resized[1] = 1280;
        Check(!Equal(original, resized), "resolution changes must invalidate before texture reuse");
        var moved = (object[])original.Clone(); moved[3] = 1.0000001f;
        Check(!Equal(original, moved), "small light or camera changes cannot use approximate equality");
        var reordered = (object[])original.Clone(); reordered[6] = 9; reordered[7] = 4;
        Check(!Equal(original, reordered), "sorting target order changes must invalidate");
        var recolored = (object[])original.Clone(); recolored[8] = 0.75f;
        Check(!Equal(original, recolored), "animated color changes must invalidate");
        Check(!Equal(original, new object[] { "camera", 1920, 1080, 1f }), "removed lights must invalidate");
        Check(Equal(Array.Empty<object>(), Array.Empty<object>()), "empty snapshots compare safely");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Light cache check failed: " + message);
    }
}
