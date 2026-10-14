using System;

namespace LXIUM_sephiria
{
    /// <summary>프레임 시간(ms) 묶음의 요약. Unity에 의존하지 않아 tests에서 그대로 검사한다.</summary>
    public struct Summary
    {
        public int Frames;
        public double Fps;      // 총 프레임 / 총 시간
        public double P50, P95, P99, Max;
        public double Low1Fps;  // 가장 느린 1% 프레임의 평균 시간 기준 fps

        public override string ToString() =>
            $"fps={Fps:F1} 1%low={Low1Fps:F1} ms(p50/p95/p99/max)={P50:F2}/{P95:F2}/{P99:F2}/{Max:F2} n={Frames}";
    }

    public static class Stats
    {
        /// <summary>오름차순 정렬된 앞 n개에서 가까운 순위 방식 백분위. p는 0~100.</summary>
        public static double Percentile(double[] sorted, int n, double p)
        {
            if (n <= 0) return 0;
            int i = (int)Math.Ceiling(p / 100.0 * n) - 1;
            return sorted[Math.Min(Math.Max(i, 0), n - 1)];
        }

        /// <summary>ms의 앞 n개를 요약한다. ms는 정렬되므로 사본을 넘긴다.</summary>
        public static Summary Summarize(double[] ms, int n)
        {
            var s = new Summary { Frames = n };
            if (n <= 0) return s;
            Array.Sort(ms, 0, n);
            double total = 0;
            for (int i = 0; i < n; i++) total += ms[i];
            int slow = Math.Max(1, n / 100);
            double slowTotal = 0;
            for (int i = n - slow; i < n; i++) slowTotal += ms[i];
            s.Fps = total > 0 ? n * 1000.0 / total : 0;
            s.Low1Fps = slowTotal > 0 ? slow * 1000.0 / slowTotal : 0;
            s.P50 = Percentile(ms, n, 50);
            s.P95 = Percentile(ms, n, 95);
            s.P99 = Percentile(ms, n, 99);
            s.Max = ms[n - 1];
            return s;
        }
    }
}
