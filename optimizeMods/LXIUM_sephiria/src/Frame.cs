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

        static long[] marks = Array.Empty<long>();   // 이번 프레임 단계 시작 틱, 마지막 칸 = 루프 끝. 0 = 안 지남
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
            marks = new long[n + 1];
            var copy = new PlayerLoopSystem[n];
            for (int i = 0; i < n; i++)
            {
                PhaseNames[i] = top[i].type?.Name ?? ("phase" + i);
                PhaseMs[i] = new double[Capacity];
                var sys = top[i];
                var subs = sys.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                bool last = i == n - 1;
                var list = new PlayerLoopSystem[subs.Length + (last ? 2 : 1)];
                list[0] = Marker(i);
                Array.Copy(subs, 0, list, 1, subs.Length);
                if (last) list[list.Length - 1] = Marker(n);
                sys.subSystemList = list;
                copy[i] = sys;
            }
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
                long start = marks[i];
                if (start == 0) { PhaseMs[i][slot] = 0; continue; }   // 이번 프레임에 안 돈 단계
                long next = now;
                for (int j = i + 1; j <= n; j++) if (marks[j] != 0) { next = marks[j]; break; }
                PhaseMs[i][slot] = (next - start) * toMs;
            }
            Draws[slot] = draws.Valid ? (int)draws.LastValue : -1;
            SetPass[slot] = setPass.Valid ? (int)setPass.LastValue : -1;
            Batches[slot] = batches.Valid ? (int)batches.LastValue : -1;
            GcBytes[slot] = gcAlloc.Valid ? gcAlloc.LastValue : -1;
            Count++;
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
