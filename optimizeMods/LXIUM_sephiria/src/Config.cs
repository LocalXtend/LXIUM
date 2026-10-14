using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LXIUM.Sephiria
{
    public sealed class RenderOptions
    {
        public bool LightResolution = true, IdlePasses = true, StaticLights = true, SpriteBatch = true;
        public bool WorldUpscale = true, DedicatedShader = true;
        public float LightScale = .5f, WorldScale = .75f, Sharpness = .35f;
        public RenderOptions Copy() => (RenderOptions)MemberwiseClone();
        public void Validate()
        {
            LightScale = Clamp(LightScale, .25f, 1f, .5f);
            WorldScale = Clamp(WorldScale, .5f, 1f, .75f);
            Sharpness = Clamp(Sharpness, 0f, 1f, .35f);
        }
        static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    }

    public sealed class Config
    {
        public RenderOptions Options = new RenderOptions();
        public bool F10Comparison = true, Screenshots = true;
        public int SectionSeconds = 12;
        public ComparisonResult LastComparison;
        [JsonExtensionData] public IDictionary<string, JToken> Extra;

        public void Validate()
        {
            Options ??= new RenderOptions();
            Options.Validate();
            SectionSeconds = Math.Max(10, Math.Min(15, SectionSeconds));
        }

        public static Config Load(string path)
        {
            if (!File.Exists(path)) return new Config();
            var result = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty config");
            result.Validate();
            return result;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }

    public sealed class ComparisonResult
    {
        public string StartedUtc, FinishedUtc, Outcome, GameAssembly, UnityVersion, GraphicsApi, Gpu;
        public string[] LoadedMods;
        public int Width, Height, Quality, OriginalVSync, OriginalFrameLimit, SectionSeconds;
        public string Directory;
        public RenderOptions SavedOptions;
        public List<SectionResult> Sections = new List<SectionResult>();
    }

    public sealed class SectionResult
    {
        public int Number;
        public string Name, Status, EffectiveFeatures;
        public RenderOptions Requested;
        public int Frames;
        public double Seconds, AverageFps, Low1Fps, LowPoint1Fps, High1Fps;
        public double MeanMs, MinMs, MaxMs, P50Ms, P95Ms, P99Ms;
        public double? CpuMainMs, CpuRenderMs, GpuMs, DrawCallsPerFrame, BatchesPerFrame, TrianglesPerFrame, GcBytesPerFrame;
        public int CpuTimingSamples, GpuTimingSamples, GcCollections;
        public long LightHits, LightMisses, LightFallbacks, WorldScaled, WorldNative, PassSkipped;
        public double? DeltaFpsPercent, BaselineDriftPercent;
        public string Screenshot;
        public int StartSprites, EndSprites, StartLights, EndLights;
        public float StartCameraSize, EndCameraSize;
    }
}
