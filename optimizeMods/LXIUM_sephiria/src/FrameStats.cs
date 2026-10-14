using System;
using System.Collections.Generic;

namespace LXIUM.Sephiria
{
    internal sealed class FrameStats
    {
        readonly List<double> samples = new List<double>(8192);
        public int Count => samples.Count;
        public void Add(double milliseconds)
        {
            if (milliseconds > 0 && !double.IsNaN(milliseconds) && !double.IsInfinity(milliseconds)) samples.Add(milliseconds);
        }
        public void Apply(SectionResult result)
        {
            if (samples.Count == 0) { result.Status = "no samples"; return; }
            samples.Sort();
            double sum = 0;
            foreach (double sample in samples) sum += sample;
            result.Frames = samples.Count;
            result.Seconds = sum / 1000;
            result.AverageFps = samples.Count * 1000 / sum;
            result.MeanMs = sum / samples.Count;
            result.MinMs = samples[0]; result.MaxMs = samples[samples.Count - 1];
            result.P50Ms = Percentile(.5); result.P95Ms = Percentile(.95); result.P99Ms = Percentile(.99);
            result.Low1Fps = TailFps(.01, true); result.LowPoint1Fps = TailFps(.001, true);
            result.High1Fps = TailFps(.01, false);
        }
        double Percentile(double q) => samples[Math.Max(0, (int)Math.Ceiling(q * samples.Count) - 1)];
        double TailFps(double fraction, bool slow)
        {
            int n = Math.Max(1, (int)Math.Ceiling(samples.Count * fraction));
            double sum = 0;
            for (int i = 0; i < n; i++) sum += samples[slow ? samples.Count - 1 - i : i];
            return n * 1000 / sum;
        }
    }
}
