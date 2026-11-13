using System;
using ItemStatsSystem;

namespace BossRush
{
    // TurnValve and its rule helpers are linked from production. The test only supplies the raid owners.
    internal sealed partial class TidegateDevices
    {
        private const int TideGaugeValve = 2;
        private readonly bool[] valves = new[] { true, true, false };
        private readonly TidegateTide tide = new TidegateTide();
        private readonly ValveWorld world = new ValveWorld();
        private readonly ValveSounds sounds = null;
        private readonly Func<bool> valid = () => true;
        private readonly Action<string, bool> announce = (_, __) => { };
        private bool disposed, fittingHandwheel;
        private float reinforceAt;

        internal static void TestHandwheel(Action<bool, string> check)
        {
            var inventory = new Inventory();
            CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item { Inventory = inventory } };
            var owner = new TidegateDevices();
            owner.TurnValve(TideGaugeValve);
            check(owner.OpenValves == 2 && !owner.tide.Open, "missing handwheel opened the gate");

            var wheel = new Item { TypeID = BossRushItemIds.TidegateHandwheel };
            check(inventory.AddAt(wheel, 4), "fixture could not carry the handwheel");
            inventory.BeforeRemove = _ => throw new InvalidOperationException("before detach");
            owner.TurnValve(TideGaugeValve);
            check(owner.OpenValves == 2 && !owner.tide.Open && inventory.GetItemAt(4) == wheel,
                "failed reservation consumed the wheel or opened the gate");
            inventory.BeforeRemove = null;

            inventory.AfterRemove = _ => owner.disposed = true;
            owner.TurnValve(TideGaugeValve);
            check(owner.OpenValves == 2 && !owner.tide.Open && inventory.GetItemAt(4) == wheel,
                "raid close during reservation did not return the original handwheel");
            owner = new TidegateDevices();

            var second = new Item { TypeID = BossRushItemIds.TidegateHandwheel };
            check(inventory.AddAt(second, 6), "fixture could not carry a second handwheel");
            inventory.AfterRemove = _ =>
            {
                owner.TurnValve(TideGaugeValve);
                throw new InvalidOperationException("after detach");
            };
            owner.TurnValve(TideGaugeValve);
            owner.TurnValve(TideGaugeValve);
            check(owner.OpenValves == 3 && owner.tide.Open && owner.reinforceAt > 0f,
                "confirmed detachment did not open the valve and schedule reinforcements");
            check(wheel == null && second != null && inventory.GetItemAt(6) == second,
                "successful valve use did not consume exactly one handwheel");
        }
    }

    internal sealed class TidegateTide
    {
        internal bool Open;
        internal void OpenSluice() { Open = true; }
    }

    internal sealed class ValveWorld { internal readonly ValvePoint[] Valves = new ValvePoint[3]; }
    internal sealed class ValvePoint { internal object position; }
    internal enum TidegateCue { Valve, Gate }
    internal sealed class ValveSounds { internal void Play(TidegateCue cue, object position) { } }
}
