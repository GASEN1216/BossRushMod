using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
}

public static class CameraMode { public static bool Active; }

namespace Duckov.UI
{
    public sealed class PauseMenu
    {
        public static PauseMenu Instance = new PauseMenu();
        public bool Shown;
    }
}

namespace BossRush
{
    public static class ZombieModeUIHelper { public static bool IsModalInputPaused; }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private void TickZombieModeDropsAndPerformance(float deltaTime) { owner.TickZombieModeDropsAndPerformanceForRuntimeModule(deltaTime); }
        private void TickZombieModeTemporaryNpcProtection() { owner.TickZombieModeTemporaryNpcProtectionForRuntimeModule(); }
    }

    public partial class ModBehaviour
    {
        internal readonly List<string> TickOrder = new List<string>();
        internal readonly List<float> TickDeltas = new List<float>();

        internal void AttachTestModule(ZombieModeRuntimeModule module)
        {
            zombieModeRuntimeModule = module;
        }

        internal void RunTick(float deltaTime) { TickZombieMode(deltaTime); }
        internal void TickClock(float time)
        {
            UnityEngine.Time.unscaledTime = time;
            RefreshZombieModeRuntimePauseClock();
        }

        internal void TickZombieModeWaveControllerForRuntimeModule(float deltaTime) { RecordTick("wave", deltaTime); }
        internal void TickZombieModeDropsAndPerformanceForRuntimeModule(float deltaTime) { RecordTick("drops", deltaTime); }
        internal void TickZombieModeBossControllerForRuntimeModule(float deltaTime) { RecordTick("boss", deltaTime); }
        internal void TickZombieModeTemporaryNpcProtectionForRuntimeModule() { TickOrder.Add("npc"); }
        internal void UpdateModeFFortificationHighlightsForRuntimeModule() { TickOrder.Add("fort"); }
        internal void UpdateFortPlacementMode() { TickOrder.Add("placement"); }
        internal void UpdateModeFRepairSelection() { TickOrder.Add("repair"); }

        private void RecordTick(string name, float deltaTime)
        {
            TickOrder.Add(name);
            TickDeltas.Add(deltaTime);
        }
    }

    public static class Program
    {
        private static int checks;

        private static void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception("FAIL " + message);
            Console.WriteLine("PASS " + message);
        }

        private static string Order(ModBehaviour owner) { return string.Join(",", owner.TickOrder); }

        public static void Main()
        {
            var owner = new ModBehaviour();
            var run = new ZombieModeRunState();
            var module = new ZombieModeRuntimeModule(owner, run);
            owner.AttachTestModule(module);

            owner.TickClock(10f);
            Check(owner.GetZombieModeRuntimeNow() == 10f, "normal runtime clock advances on unscaled time");
            owner.RunTick(0.25f);
            Check(Order(owner) == "wave,drops,boss,npc,fort,placement,repair", "active tick retains controller and fortification order");
            Check(owner.TickDeltas.Count == 3 && owner.TickDeltas[0] == 0.25f && owner.TickDeltas[1] == 0.25f && owner.TickDeltas[2] == 0.25f,
                "active tick forwards the original deltaTime to time-based controllers");

            owner.TickOrder.Clear(); owner.TickDeltas.Clear();
            CameraMode.Active = true;
            owner.TickClock(20f); owner.RunTick(0.5f);
            owner.TickClock(30f); owner.RunTick(0.5f);
            Check(owner.IsZombieModeRuntimePaused() && owner.GetZombieModeRuntimeNow() == 20f,
                "photo mode freezes the runtime clock and tick body");
            Check(owner.TickOrder.Count == 0, "photo pause returns before controller updates");
            CameraMode.Active = false;
            owner.TickClock(30f); owner.RunTick(0.5f);
            Check(!owner.IsZombieModeRuntimePaused() && owner.GetZombieModeRuntimeNow() == 20f,
                "leaving photo mode resumes without charging paused duration");
            Check(Order(owner) == "wave,drops,boss,npc,fort,placement,repair", "active tick resumes in its original order");

            owner.TickOrder.Clear(); owner.TickDeltas.Clear();
            ZombieModeUIHelper.IsModalInputPaused = true;
            owner.TickClock(31f); owner.RunTick(0.5f);
            owner.TickClock(35f); owner.RunTick(0.5f);
            Check(owner.IsZombieModeRuntimePaused() && owner.TickOrder.Count == 0,
                "reward-modal pause blocks the tick body");
            ZombieModeUIHelper.IsModalInputPaused = false;
            owner.TickClock(35f); owner.RunTick(0.5f);
            Check(owner.GetZombieModeRuntimeNow() == 21f && owner.TickOrder.Count == 7,
                "reward-modal pause is excluded from runtime deadlines and resumes ticking");

            owner.TickOrder.Clear();
            Duckov.UI.PauseMenu.Instance.Shown = true;
            owner.TickClock(40f); owner.RunTick(0.5f);
            Check(owner.IsZombieModeGamePaused() && owner.TickOrder.Count == 0,
                "official pause-menu state blocks tick updates");
            Duckov.UI.PauseMenu.Instance.Shown = false;
            owner.TickClock(45f); owner.RunTick(0.5f);
            Check(owner.GetZombieModeRuntimeNow() == 26f && owner.TickOrder.Count == 7,
                "leaving the pause menu excludes its duration from runtime deadlines");

            owner.TickOrder.Clear();
            run.LifecyclePhase = ZombieModeLifecyclePhase.None;
            owner.TickClock(50f); owner.RunTick(0.5f);
            Check(owner.TickOrder.Count == 0 && owner.GetZombieModeRuntimeNow() == 50f,
                "inactive tick resets pause-clock state and skips runtime controllers");
            run.LifecyclePhase = ZombieModeLifecyclePhase.Active;
            owner.TickClock(55f); owner.RunTick(0.5f);
            Check(owner.GetZombieModeRuntimeNow() == 55f && owner.TickOrder.Count == 7,
                "new active run starts a clean unscaled runtime clock");

            Console.WriteLine("AuditModeLifecycle pause clock/tick: " + checks + " PASS / 0 FAIL");
        }
    }
}
