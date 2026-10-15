using System;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine.LowLevel;

namespace LXIUM_sephiria
{
    /// <summary>
    /// 플레이어 루프 최상위 단계(TimeUpdate, Initialization, ..., PostLateUpdate)마다 시작 시각을 찍어
    /// 단계별 메인 스레드 시간을 링 버퍼에 쌓는다. TimeUpdate에는 지난 프레임 표시 대기(GPU·VSync·프레임 제한)가,
    /// PostLateUpdate에는 렌더 기록·제출이 들어간다.
    /// </summary>
    public static class Frame
    {
        public const int Capacity = 8192;

        public static string[] PhaseNames = Array.Empty<string>();
        public static readonly double[] FrameMs = new double[Capacity];
        public static double[][] PhaseMs = Array.Empty<double[]>();   // [단계][링 칸]
        public static readonly int[] Draws = new int[Capacity];
        public static readonly int[] SetPass = new int[Capacity];
        public static readonly int[] Batches = new int[Capacity];
        public static readonly long[] GcBytes = new long[Capacity];
        public static long Count;   // 기록한 프레임 수. 링 칸 = Count % Capacity

        // 표식은 모든 하위 시스템 앞에 하나씩(단계 첫 표식 = 단계 시작) + 루프 끝에 하나. 번호는 실행 순서
        static long[] marks = Array.Empty<long>();   // 이번 프레임 표식 틱. 0 = 안 지남
        static int[] phaseFirst = Array.Empty<int>(); // 단계마다 첫 표식 번호
        static int endMark;
        public static string[] MarkNames = Array.Empty<string>();   // 표식 m부터 다음 표식까지 = "단계.하위 시스템"
        static long[] detailTicks = Array.Empty<long>();            // ResetDetail 뒤 표식별 누적 틱
        static long detailFrames;
        static long frameStart;
        static ProfilerRecorder draws, setPass, batches, gcAlloc;
        static PlayerLoopSystem original;
        static bool installed;

        /// <summary>표식 시스템을 끼운 플레이어 루프를 등록한다. 다른 모드의 루프 변경을 지우지 않도록 현재 루프에 끼운다.</summary>
        public static void Install()
        {
            if (installed) return;
            original = PlayerLoop.GetCurrentPlayerLoop();
            var loop = original;
            var top = loop.subSystemList;
            int n = top.Length;
            PhaseNames = new string[n];
            PhaseMs = new double[n][];
            phaseFirst = new int[n];
            var names = new System.Collections.Generic.List<string>();
            var copy = new PlayerLoopSystem[n];
            for (int i = 0; i < n; i++)
            {
                PhaseNames[i] = top[i].type?.Name ?? ("phase" + i);
                PhaseMs[i] = new double[Capacity];
                var sys = top[i];
                var subs = sys.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var list = new System.Collections.Generic.List<PlayerLoopSystem>(subs.Length * 2 + 2);
                phaseFirst[i] = names.Count;
                if (subs.Length == 0) { list.Add(Marker(names.Count)); names.Add(PhaseNames[i]); }
                foreach (var s in subs)
                {
                    list.Add(Marker(names.Count));
                    names.Add(PhaseNames[i] + "." + (s.type?.Name ?? "?"));
                    list.Add(s);
                }
                if (i == n - 1) { endMark = names.Count; list.Add(Marker(endMark)); names.Add("(end)"); }
                sys.subSystemList = list.ToArray();
                copy[i] = sys;
            }
            MarkNames = names.ToArray();
            marks = new long[MarkNames.Length];
            detailTicks = new long[MarkNames.Length];
            loop.subSystemList = copy;
            PlayerLoop.SetPlayerLoop(loop);

            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            installed = true;
        }

        public static void Uninstall()
        {
            if (!installed) return;
            PlayerLoop.SetPlayerLoop(original);
            draws.Dispose(); setPass.Dispose(); batches.Dispose(); gcAlloc.Dispose();
            installed = false;
        }

        struct Mark { }

        static PlayerLoopSystem Marker(int k) => new PlayerLoopSystem
        {
            type = typeof(Mark),
            updateDelegate = () => OnMark(k),
        };

        static void OnMark(int k)
        {
            long now = Stopwatch.GetTimestamp();
            if (k == 0)
            {
                if (frameStart != 0) Commit(now);
                Array.Clear(marks, 0, marks.Length);
                frameStart = now;
            }
            // FixedUpdate처럼 한 프레임에 여러 번 도는 단계는 첫 시작만 쓴다
            if (marks[k] == 0) marks[k] = now;
        }

        static void Commit(long now)
        {
            int slot = (int)(Count % Capacity);
            double toMs = 1000.0 / Stopwatch.Frequency;
            FrameMs[slot] = (now - frameStart) * toMs;
            int n = PhaseMs.Length;
            for (int i = 0; i < n; i++)
            {
                long start = marks[phaseFirst[i]];
                if (start == 0) { PhaseMs[i][slot] = 0; continue; }   // 이번 프레임에 안 돈 단계
                long next = marks[endMark] != 0 ? marks[endMark] : now;
                for (int j = i + 1; j < n; j++) if (marks[phaseFirst[j]] != 0) { next = marks[phaseFirst[j]]; break; }
                PhaseMs[i][slot] = (next - start) * toMs;
            }
            // 하위 시스템: 표식부터 다음으로 지난 표식까지 (안 돈 하위 시스템 몫은 앞 것에 붙음)
            for (int m = 0; m < endMark; m++)
            {
                if (marks[m] == 0) continue;
                long next = now;
                for (int k = m + 1; k <= endMark; k++) if (marks[k] != 0) { next = marks[k]; break; }
                detailTicks[m] += next - marks[m];
            }
            detailFrames++;
            Draws[slot] = draws.Valid ? (int)draws.LastValue : -1;
            SetPass[slot] = setPass.Valid ? (int)setPass.LastValue : -1;
            Batches[slot] = batches.Valid ? (int)batches.LastValue : -1;
            GcBytes[slot] = gcAlloc.Valid ? gcAlloc.LastValue : -1;
            Count++;
        }

        public static void ResetDetail()
        {
            Array.Clear(detailTicks, 0, detailTicks.Length);
            detailFrames = 0;
        }

        /// <summary>ResetDetail 뒤 하위 시스템별 프레임당 평균 ms, 큰 것부터 top개</summary>
        public static string DetailReport(int top)
        {
            if (detailFrames == 0) return "(프레임 없음)";
            double toMs = 1000.0 / Stopwatch.Frequency / detailFrames;
            var idx = new int[endMark];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => detailTicks[b].CompareTo(detailTicks[a]));
            var sb = new StringBuilder($"subsystems n={detailFrames}");
            for (int i = 0; i < Math.Min(top, idx.Length); i++)
                sb.Append("\n  ").Append((detailTicks[idx[i]] * toMs).ToString("F3")).Append(' ').Append(MarkNames[idx[i]]);
            return sb.ToString();
        }

        /// <summary>기록 [from, Count)를 요약한다. 링에 남은 최근 Capacity 프레임까지만 본다.</summary>
        public static string Report(long from)
        {
            long to = Count;
            from = Math.Max(from, to - Capacity);
            int n = (int)Math.Max(0, to - from);
            var ms = new double[n];
            var phase = new double[PhaseMs.Length];
            double d = 0, sp = 0, b = 0, gc = 0;
            for (int k = 0; k < n; k++)
            {
                int slot = (int)((from + k) % Capacity);
                ms[k] = FrameMs[slot];
                for (int i = 0; i < phase.Length; i++) phase[i] += PhaseMs[i][slot];
                d += Draws[slot]; sp += SetPass[slot]; b += Batches[slot]; gc += GcBytes[slot];
            }
            var sb = new StringBuilder(Stats.Summarize(ms, n).ToString());
            if (n == 0) return sb.ToString();
            sb.Append(" phase-ms");
            for (int i = 0; i < phase.Length; i++) sb.Append(' ').Append(PhaseNames[i]).Append('=').Append((phase[i] / n).ToString("F2"));
            sb.Append($" draws={d / n:F0} setpass={sp / n:F0} batches={b / n:F0} gcKB/frame={gc / n / 1024:F1}");
            return sb.ToString();
        }
    }
}
