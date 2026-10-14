using System;
using System.IO;
using System.Linq;
using LXIUM.Sephiria;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .00001, $"{actual} != {expected}");
    static void Main()
    {
        var stats = new FrameStats();
        foreach (double value in new[] { 10d, 20, 30, 0, -1, double.NaN, double.PositiveInfinity }) stats.Add(value);
        var section = new SectionResult(); stats.Apply(section);
        Check(section.Frames == 3, "invalid frame samples admitted"); Near(section.AverageFps, 50);
        Near(section.MeanMs, 20); Near(section.P50Ms, 20); Near(section.P99Ms, 30);
        Near(section.Low1Fps, 1000d / 30); Near(section.High1Fps, 100);
        stats = new FrameStats(); for (int i = 0; i < 1000; i++) stats.Add(i == 999 ? 1000 : 10);
        section = new SectionResult(); stats.Apply(section);
        Near(section.Low1Fps, 10000d / 1090); Near(section.LowPoint1Fps, 1); Near(section.P99Ms, 10);
        Check(section.AverageFps < 100 && section.AverageFps > 90, "FPS must use elapsed time, not mean instantaneous FPS");
        var empty = new SectionResult(); new FrameStats().Apply(empty); Check(empty.Status == "no samples", "empty section");

        var options = new RenderOptions();
        for (int i = 0; i < 10; i++)
        {
            var preset = RenderLab.Preset(i, options);
            int count = new[] { preset.LightResolution, preset.IdlePasses, preset.StaticLights, preset.SpriteBatch, preset.WorldUpscale, preset.DedicatedShader }.Count(x => x);
            Check(count == (i == 0 || i == 9 ? 0 : i == 6 ? 2 : i == 7 || i == 8 ? 6 : 1), "preset isolation " + i);
        }
        Check(options.StaticLights && options.WorldUpscale, "presets mutate user's saved options");
        string dir = Path.Combine(Path.GetTempPath(), "lxium-config-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{\"SectionSeconds\":99,\"extraPreference\":42,\"Options\":{\"WorldScale\":0.1,\"LightScale\":4},\"LastComparison\":null}");
            var config = Config.Load(path);
            Check(config.SectionSeconds == 15 && config.Options.WorldScale == .5f && config.Options.LightScale == 1, "config range validation");
            config.LastComparison = new ComparisonResult { Outcome = "aborted: test" };
            config.LastComparison.Sections.Add(new SectionResult { Name = "01_original", GpuMs = null, AverageFps = 80 });
            config.Save(path); Check(File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "atomic save and backup");
            var reread = Config.Load(path); Check((int)reread.Extra["extraPreference"] == 42, "unknown config fields lost");
            Check(reread.LastComparison.Sections[0].GpuMs == null && reread.LastComparison.Outcome.StartsWith("aborted"), "unsupported counters or aborted outcome lost");
            string malformed = "{broken config"; File.WriteAllText(path, malformed);
            try { Config.Load(path); throw new Exception("malformed config accepted"); } catch (Newtonsoft.Json.JsonException) { }
            Check(File.ReadAllText(path) == malformed, "invalid config must not be overwritten on read");
            options.WorldScale = float.NaN; options.LightScale = float.PositiveInfinity; options.Validate();
            Check(options.WorldScale == .75f && options.LightScale == .5f, "nonfinite config values");
        }
        finally { foreach (string file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
        LightCacheChecks.Run(typeof(Config).Assembly);
        foreach (var item in new[] { (typeof(Pipeline), "idleInstalled"), (typeof(WorldRender), "installed"), (typeof(LightCache), "installed") })
        {
            var field = item.Item1.GetField(item.Item2, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field.SetValue(null, true);
            item.Item1.GetMethod("ResetHooks").Invoke(null, null);
            Check(!(bool)field.GetValue(null), "unload/reload hook state " + item.Item1.Name);
        }
        Console.WriteLine("PASS: frame distributions/tails, isolated presets, config validation/atomic roundtrip, cache invalidation");
    }
}
