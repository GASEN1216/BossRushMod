using System;
using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal readonly List<string> Calls = new List<string>();
        internal bool ArenaStopsLaterModes;
        internal float LastArenaDelta, LastZombieDelta;
        private void TickModeEFSpawnPostprocessScheduler() { Calls.Add("postprocess"); }
        private bool TickWavesArenaRuntime(float delta) { LastArenaDelta = delta; Calls.Add("arena"); return ArenaStopsLaterModes; }
        private void TickModeERuntime(float delta) { Calls.Add("e"); }
        private void TickModeFRuntime(float delta) { Calls.Add("f"); }
        private void UpdateModeG(float delta) { Calls.Add("g"); }
        private void TickZombieModeRuntime(float delta) { LastZombieDelta = delta; Calls.Add("zombie"); }
        private void TickWavesArenaBossCleanupRuntime(float delta) { Calls.Add("cleanup"); }
    }

    internal static class Program
    {
        private static void Check(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }

        private static int At(List<string> calls, string value)
        {
            int index = calls.IndexOf(value);
            Check(index >= 0 && calls.LastIndexOf(value) == index, value + " must run exactly once");
            return index;
        }

        private static void Main()
        {
            var normal = new ModBehaviour();
            Check(!normal.TickModeRuntimeGroup(2f, 3f), "normal path continues");
            Check(normal.Calls.Count == 7, "normal path calls every runtime phase once");
            Check(At(normal.Calls, "postprocess") < At(normal.Calls, "arena"), "shared scheduler precedes arena");
            Check(At(normal.Calls, "arena") < At(normal.Calls, "e"), "arena precedes Mode E");
            Check(At(normal.Calls, "e") < At(normal.Calls, "f"), "Mode E precedes Mode F");
            Check(At(normal.Calls, "f") < At(normal.Calls, "g"), "Mode F precedes Mode G");
            Check(At(normal.Calls, "g") < At(normal.Calls, "zombie"), "Mode G precedes Zombie");
            Check(At(normal.Calls, "zombie") < At(normal.Calls, "cleanup"), "Zombie precedes cleanup");
            Check(normal.LastArenaDelta == 2f && normal.LastZombieDelta == 3f, "time sources stay split");

            var arena = new ModBehaviour { ArenaStopsLaterModes = true };
            Check(arena.TickModeRuntimeGroup(2f, 3f), "arena early return propagates");
            Check(arena.Calls.Count == 2, "arena early return skips later modes and cleanup");
            Check(At(arena.Calls, "postprocess") < At(arena.Calls, "arena"), "shared scheduler survives arena early return");
            Console.WriteLine("ModeRuntimeDispatch: PASS");
        }
    }
}
