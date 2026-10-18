using System;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static partial class Program
{
    private static bool skipAreaUpdates;
    private static readonly MethodInfo AreaUpdate = typeof(ZombieModeTimedRunScopedRuntime)
        .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void UpdateArea(ZombieModeAreaTickRuntime area)
    {
        if (area != null && area.isActiveAndEnabled) AreaUpdate.Invoke(area, null);
    }

    private static ZombieModeAreaTickRuntime BeginTimedArea(float uplink, float downlink,
        float duration = 5f, float startup = 0f, CharacterMainControl source = null)
    {
        Setup(); skipAreaUpdates = false;
        host.ClockOffset = host.Now = 1000f;
        guest.ClockOffset = guest.Now = 20000f;
        BossRushCoop.TimedWire = true;
        BossRushCoop.Uplink = uplink; BossRushCoop.Downlink = downlink;
        var area = new GameObject().AddComponent<ZombieModeAreaTickRuntime>();
        area.Initialize(11, source, 4f, duration, 0f, 0.25f, 0.3f, startup, source != null);
        for (int frame = 1; frame <= 240; frame++)
        {
            float at = frame * 0.025f;
            BossRushCoop.Schedule(at, () =>
            {
                BossRushCoop.Current = host;
                if (!skipAreaUpdates) UpdateArea(area); h.Tick(0.025f);
                BossRushCoop.Current = guest; g.Tick(0.025f);
                BossRushCoop.Current = host;
            });
        }
        return area;
    }

    private static void PersistentZones()
    {
        // All clocks below have unrelated origins; only monotonic elapsed intervals cross the wire.
        foreach (float[] latency in new[] { new[] { 0.125f, 0.125f }, new[] { 0.45f, 0.01f },
            new[] { 0.01f, 0.45f }, new[] { 0.4f, 0.4f } })
        {
            BeginTimedArea(latency[0], latency[1]);
            for (int sample = 0; sample < 35; sample++)
            {
                BossRushCoop.Advance(1.75f + sample * 0.05f);
                Check(Slow(guest) != null && Slow(guest).Ends + 0.002f >= guest.Now,
                    "persistent live area continuously slows guest with independent clocks and directional latency " + latency[0] + "/" + latency[1]);
            }
            Check(Slow(guest).Duration <= 0.501f && Slow(guest).Percent == 0.3f,
                "latency compensation never increases the original tick duration or slow strength");
            BossRushCoop.Advance(6f);
            Check(Slow(guest).MaxEnds <= guest.ClockOffset + 5.002f,
                "full-RTT validity budget never extends a persistent effect beyond the real source lifetime");
        }

        BeginTimedArea(0.125f, 0.125f, 0.6f);
        BossRushCoop.Advance(2f);
        Check(Slow(guest) == null, "a source that expires during confirmation cannot revive its last tick");

        BeginTimedArea(0.125f, 0.125f);
        BossRushCoop.Schedule(0.55f, () => guest.Body.transform.position = new Vector3(8, 0, 0));
        BossRushCoop.Advance(2f);
        Check(Slow(guest) == null, "guest leaving after host confirmation still rejects every in-flight slow at its real position");

        foreach (bool destroy in new[] { false, true })
        {
            ZombieModeAreaTickRuntime area = BeginTimedArea(0.125f, 0.125f);
            BossRushCoop.Schedule(0.4f, () => { if (destroy) UnityEngine.Object.Destroy(area.gameObject); else area.enabled = false; });
            BossRushCoop.Advance(2f);
            Check(Slow(guest) == null && h.Facts == 0,
                "disabled or destroyed source refuses previously offered ticks before nonce confirmation");
        }

        var movingSource = new CharacterMainControl();
        BeginTimedArea(0.125f, 0.125f, 5f, 0f, movingSource);
        BossRushCoop.Schedule(0.4f, () =>
        {
            movingSource.transform.position = new Vector3(10, 0, 0);
            guest.Body.transform.position = new Vector3(11, 0, 0);
        });
        BossRushCoop.Advance(0.7f);
        Check(Slow(guest) != null && Slow(guest).Calls == 1,
            "confirmation refreshes a following source's actual center instead of replaying the old origin");

        ZombieModeAreaTickRuntime starting = BeginTimedArea(0.125f, 0.125f, 4f, 1f);
        Check(starting.CoopSlowRemainingSeconds == 0f, "unarmed telegraph cannot offer an active slow lifetime");
        BossRushCoop.Advance(0.9f);
        Check(Slow(guest) == null && h.Facts == 0, "startup frames execute real Update without creating slow facts");
        BossRushCoop.Advance(1.7f);
        Check(Slow(guest) != null, "real arming and tick Update begins the ordinary persistent slow");

        foreach (string droppedPacket in new[] { "offer", "probe", "receipt" })
        {
            ZombieModeAreaTickRuntime area = BeginTimedArea(0.125f, 0.125f);
            BossRushCoop.DropNextOffer = droppedPacket == "offer";
            BossRushCoop.DropNextProbe = droppedPacket == "probe";
            BossRushCoop.DropNextReceipt = droppedPacket == "receipt";
            // Stop new tick generation after the first fact while leaving the source genuinely alive.
            BossRushCoop.Schedule(0.3f, () => skipAreaUpdates = true);
            BossRushCoop.Advance(2f);
            Check(Slow(guest) != null && Slow(guest).Calls == 1 && g.Pending == 0,
                "lost offer, probe or final receipt retries the same nonce/fact exactly once without resetting its elapsed budget");
        }

        BeginTimedArea(0.125f, 0.125f, 1f);
        BossRushCoop.Schedule(0.55f, () => g.Paused = true);
        BossRushCoop.Schedule(2f, () => g.Paused = false);
        BossRushCoop.Advance(3f);
        Check(Slow(guest) == null, "paused guest cannot resume an already expired source from a reliable retry");

        BeginTimedArea(0.125f, 0.125f);
        BossRushCoop.Schedule(0.55f, () => guest.Token = "successor-scene");
        BossRushCoop.Advance(2f);
        Check(Slow(guest) == null, "new scene token rejects source-bound in-flight and future old-scene offers");

        BeginTimedArea(0.125f, 0.125f);
        BossRushCoop.Schedule(0.55f, () => guest.Body.Health.CurrentHealth = 0f);
        BossRushCoop.Advance(2f);
        Check(Slow(guest) == null, "dead guest cannot receive a persistent slow even while host source is alive");

        PauseBeforeBaseResume();
    }

    private static void PauseBeforeBaseResume()
    {
        Setup(); host.Now = 1000f; guest.Now = 30000f;
        var area = new GameObject().AddComponent<ZombieModeAreaTickRuntime>();
        area.Initialize(11, null, 4f, 2f, 0f, 0.25f, 0.3f);
        host.Now = 1000.25f; UpdateArea(area);
        host.Now = 1000.3f; h.Paused = true; UpdateArea(area);
        BossRushCoop.Wire.Dequeue()(); // Guest's offer creates its nonce before the long pause ends.
        host.Now = 1020f; h.Paused = false;
        Check(Math.Abs(area.CoopSlowRemainingSeconds - 1.7f) < 0.002f,
            "remaining lifetime uses the frozen pause clock before base Update has extended the end time");
        BossRushCoop.Wire.Dequeue()(); // Host confirms before its area Update resumes.
        guest.Now += 0.25f; BossRushCoop.Flush();
        Check(Slow(guest) != null && Slow(guest).Duration == 0.5f,
            "confirmation in the resume-before-base-Update frame retains the original slow duration");
        UpdateArea(area);
        Check(area != null && area.isActiveAndEnabled && Math.Abs(area.CoopSlowRemainingSeconds - 1.7f) < 0.002f,
            "subsequent real base Update extends the paused lifetime exactly once and keeps the source alive");
    }
}
