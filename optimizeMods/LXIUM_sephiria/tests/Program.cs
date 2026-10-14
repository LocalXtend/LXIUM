using System;
using LXIUM_sephiria;

// 1..100 ms 프레임 100개: 평균 fps = 100 / 5.05 s, 가장 느린 1% = 100 ms 한 프레임
var ms = new double[100];
for (int i = 0; i < ms.Length; i++) ms[ms.Length - 1 - i] = i + 1;   // 역순으로 넣어 정렬도 확인
var s = Stats.Summarize(ms, ms.Length);
Check(s.Frames == 100, "frames");
Check(Math.Abs(s.Fps - 100 / 5.05) < 1e-9, "fps " + s.Fps);
Check(s.P50 == 50 && s.P95 == 95 && s.P99 == 99 && s.Max == 100, "percentiles " + s);
Check(Math.Abs(s.Low1Fps - 10) < 1e-9, "1% low " + s.Low1Fps);

// 앞 n개만 본다, 빈 입력은 0
var part = new double[] { 4, 2, 9, 9 };
Check(Stats.Summarize(part, 2).Max == 4, "prefix only");
Check(Stats.Summarize(Array.Empty<double>(), 0).Fps == 0, "empty");
Console.WriteLine("ok " + s);

static void Check(bool ok, string what)
{
    if (!ok) throw new Exception("FAIL " + what);
}
