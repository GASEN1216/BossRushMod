using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 内容批次四「云蚋」纯规则的执行回归：判夜唯一口径与无时钟、刷新权重（烟 / 光 / 风 / 静水 / 青蛙 / 岛心 / 敌人）、叮咬速率封顶与叮不死、
/// 痒的叠加与滞回、蒲扇与灭蚊灯的判定、青蛙手记、冲刺运动学（单步位移上限、冲刺长度上限、前摇与可见帧数、预算与冷却）、英文无中文。
/// 生产文件原样链接执行；躲子弹的整体手感另见 <see cref="SkyIslandGnatDodgeSimulation"/>。
/// </summary>
internal static class SkyIslandMosquitoRegression
{
    internal static void Run(Action<bool, string> check)
    {
        Night(check);
        Spawn(check);
        Bites(check);
        Itch(check);
        FanAndZapper(check);
        Frogs(check);
        Kinematics(check);
        English(check);
    }

    private static SkyIslandGnatVec V(float x, float y, float z) { return new SkyIslandGnatVec(x, y, z); }

    private static void Night(Action<bool, string> check)
    {
        check(SkyIslandNight.IsNight(21) && SkyIslandNight.IsNight(23.5) && SkyIslandNight.IsNight(4.99) && SkyIslandNight.IsNight(-1),
            "gnat night covers 21:00 to 05:00 and wraps across midnight");
        check(!SkyIslandNight.IsNight(5) && !SkyIslandNight.IsNight(12) && !SkyIslandNight.IsNight(20.99), "daytime hours are not night");
        check(!SkyIslandNight.IsNight(double.NaN) && !SkyIslandNight.IsNight(double.PositiveInfinity), "non-finite hours never count as night");
        // 官方 GameClock 没有实例时 TimeOfDay 恒为 00:00：照读会把整趟判成夜里。
        check(double.IsNaN(SkyIslandNight.EffectiveHours(false, 0)) && !SkyIslandNight.IsNight(SkyIslandNight.EffectiveHours(false, 0)),
            "a missing GameClock instance reads as not night");
        check(SkyIslandNight.EffectiveHours(true, 22.5) == 22.5, "a live clock passes straight through");
        SkyIslandNight.DevForceNight = true;
        check(SkyIslandNight.IsNight(SkyIslandNight.EffectiveHours(false, double.NaN)) && SkyIslandNight.IsNight(SkyIslandNight.EffectiveHours(true, 12)),
            "the dev force-night switch overrides both a missing and a daytime clock");
        SkyIslandNight.ResetStaticCaches();
        check(!SkyIslandNight.DevForceNight && !SkyIslandNight.IsNight(SkyIslandNight.EffectiveHours(true, 12)), "module teardown clears the dev switch");
    }

    private static SkyIslandGnatSite Calm()
    {
        return new SkyIslandGnatSite { Night = true, WindLevel = 0, WaterEdgeDistance = 100f };
    }

    private static void Spawn(Action<bool, string> check)
    {
        float calm = SkyIslandMosquitoRules.SpawnWeight(Calm());
        check(calm > 0f, "a calm night away from water still has some gnats");
        SkyIslandGnatSite site = Calm(); site.Night = false;
        check(SkyIslandMosquitoRules.SpawnWeight(site) == 0f, "no gnats by day");
        site = Calm(); site.InSmoke = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) == 0f, "hearth smoke keeps gnats away");
        site = Calm(); site.Incense = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) == 0f, "burning windward incense keeps gnats away");
        site = Calm(); site.EnemiesNear = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) == 0f, "no new swarm while living enemies are near");
        site = Calm(); site.WindLevel = 2;
        check(SkyIslandMosquitoRules.SpawnWeight(site) == 0f, "a gale (night bridges, the storm's boardwalk) grounds them");
        site = Calm(); site.WindLevel = 1;
        float breeze = SkyIslandMosquitoRules.SpawnWeight(site);
        check(breeze > 0f && breeze < calm, "a breeze thins them without clearing them");
        site = Calm(); site.WaterEdgeDistance = 5f;
        check(SkyIslandMosquitoRules.SpawnWeight(site) > calm, "still water breeds more gnats");
        float previous = float.MaxValue;
        for (int frogs = 0; frogs <= SkyIslandMosquitoRules.FrogTarget + 1; frogs++)
        {
            site.FrogsReleased = frogs;
            float weight = SkyIslandMosquitoRules.SpawnWeight(site);
            check(weight <= previous + 1e-6f && weight >= calm - 1e-6f, "each clutch of frogspawn lowers the near-water weight: " + frogs);
            previous = weight;
        }
        check(SkyIslandMosquitoRules.WaterBoost(0) > SkyIslandMosquitoRules.WaterBoost(1) &&
              SkyIslandMosquitoRules.WaterBoost(1) > SkyIslandMosquitoRules.WaterBoost(2) &&
              SkyIslandMosquitoRules.WaterBoost(2) > SkyIslandMosquitoRules.WaterBoost(3), "one full step per clutch");
        check(Math.Abs(SkyIslandMosquitoRules.WaterBoost(SkyIslandMosquitoRules.FrogTarget) - 1f) < 1e-5f &&
              SkyIslandMosquitoRules.WaterBoost(SkyIslandMosquitoRules.FrogTarget + 5) == SkyIslandMosquitoRules.WaterBoost(SkyIslandMosquitoRules.FrogTarget) &&
              SkyIslandMosquitoRules.WaterBoost(-3) == SkyIslandMosquitoRules.WaterBoost(0),
            "with the pool full of frogs, still water no longer adds gnats");
        site = Calm(); site.Lantern = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) > calm, "a lit wind lantern draws gnats");
        site = Calm(); site.NearLamp = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) > calm, "a lit windcrystal lamp draws gnats");
        site = Calm(); site.NearZapper = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) > calm, "a humming zapper draws gnats");
        site = Calm(); site.IslandCore = true;
        check(SkyIslandMosquitoRules.SpawnWeight(site) > calm, "the sheltered island core has more");
        for (int i = 0; i <= 20; i++)
        {
            double roll = i / 20.0;
            foreach (float weight in new[] { 0f, 0.1f, 0.55f, 2.5f, 50f, float.NaN })
            {
                int size = SkyIslandMosquitoRules.GroupSize(weight, roll);
                check(size >= SkyIslandMosquitoRules.GroupMin && size <= SkyIslandMosquitoRules.GroupMax, "a swarm is 2 to 4 gnats");
                double chance = SkyIslandMosquitoRules.SpawnChance(weight);
                check(chance >= 0.0 && chance <= SkyIslandMosquitoRules.SpawnChanceMax, "spawn chance is capped");
            }
            float cooldown = SkyIslandMosquitoRules.SpawnCooldown(roll);
            check(cooldown >= SkyIslandMosquitoRules.SpawnCooldownMin && cooldown <= SkyIslandMosquitoRules.SpawnCooldownMax, "spawn cooldown in range");
            float distance = SkyIslandMosquitoRules.SpawnDistance(roll);
            check(distance >= 8f && distance <= 14f, "swarms appear 8 to 14 m away, at the edge of the screen");
        }
        check(SkyIslandMosquitoRules.SpawnChance(SkyIslandMosquitoRules.SpawnWeight(Calm())) > 0.0, "a calm night can roll a swarm");
        for (int alive = 0; alive <= 8; alive++)
            for (int group = 0; group <= 4; group++)
                check(alive + SkyIslandMosquitoRules.RoomFor(alive, group) <= Math.Max(alive, SkyIslandMosquitoRules.MaxAlive),
                    "never more than six gnats at once");
        foreach (SkyIslandMosquitoRules.Water water in SkyIslandMosquitoRules.Waters)
            check(SkyIslandMosquitoRules.WaterEdgeDistance(water.X, water.Z) == 0f, "a water point sits on its own water: " + water.Id);
        check(SkyIslandMosquitoRules.WaterEdgeDistance(0f, -220f) > SkyIslandMosquitoRules.WaterReach, "the dock is far from still water");
        check(SkyIslandMosquitoRules.WaterEdgeDistance(-292f, -86f) <= SkyIslandMosquitoRules.WaterReach, "the Frogsong Pool path is near water");
        check(SkyIslandMosquitoRules.Islands.Length == 12, "twelve islands in the core table");
        foreach (SkyIslandMosquitoRules.Island island in SkyIslandMosquitoRules.Islands)
            check(SkyIslandMosquitoRules.IsIslandCore(island.Region, island.X, island.Z), "an island centre is its own core: " + island.Region);
        check(!SkyIslandMosquitoRules.IsIslandCore("A", 40f, -220f) && !SkyIslandMosquitoRules.IsIslandCore(null, 0f, -220f) &&
              !SkyIslandMosquitoRules.IsIslandCore("AB", 0f, -150f), "island edges, bridges and unknown ground are not cores");
        check(SkyIslandMosquitoRules.ShouldTrackProjectile(true, 0f, 0f) && !SkyIslandMosquitoRules.ShouldTrackProjectile(false, 0f, 0f) &&
              !SkyIslandMosquitoRules.ShouldTrackProjectile(true, 9.8f, 0f) && !SkyIslandMosquitoRules.ShouldTrackProjectile(true, 0f, 3f),
            "only the player's own straight bullets are dodged; grenades and rockets are not");
    }

    private static void Bites(Action<bool, string> check)
    {
        for (int i = 0; i <= 10; i++)
        {
            float plain = SkyIslandMosquitoRules.BiteDelay(i / 10.0, false), veiled = SkyIslandMosquitoRules.BiteDelay(i / 10.0, true);
            check(plain >= SkyIslandMosquitoRules.BiteIntervalMin - 1e-4f && plain <= SkyIslandMosquitoRules.BiteIntervalMax + 1e-4f,
                "one gnat bites every 1.5 to 2.5 s");
            check(Math.Abs(veiled - plain * SkyIslandMosquitoRules.VeilBiteFactor) < 1e-4f, "the cloudmoss veil slows every gnat");
        }
        check(!SkyIslandMosquitoRules.HealthAllowsBite(30f, 100f) && SkyIslandMosquitoRules.HealthAllowsBite(80f, 100f) &&
              !SkyIslandMosquitoRules.HealthAllowsBite(1f, 2f) && !SkyIslandMosquitoRules.HealthAllowsBite(10f, 0f),
            "gnats only circle once you are below the floor, and never bite you to death");
        check(SkyIslandMosquitoRules.BiteDamage == 1f, "a bite is exactly one point of damage");
        foreach (bool veil in new[] { false, true })
        {
            var random = new Random(4242);
            var ready = new float[SkyIslandMosquitoRules.MaxAlive];
            for (int g = 0; g < ready.Length; g++) ready[g] = SkyIslandMosquitoRules.BiteDelay(random.NextDouble(), veil);
            float lastBite = -100f;
            int bites = 0;
            const float dt = 1f / 60f, seconds = 120f;
            for (int frame = 0; frame < (int)(seconds / dt); frame++)
            {
                float now = frame * dt;
                for (int g = 0; g < ready.Length; g++)
                {
                    if (!SkyIslandMosquitoRules.BiteReady(now, ready[g], lastBite, veil)) continue;
                    bites++;
                    lastBite = now;
                    ready[g] = now + SkyIslandMosquitoRules.BiteDelay(random.NextDouble(), veil);
                }
            }
            float cap = seconds / SkyIslandMosquitoRules.GlobalBiteGapFor(veil) + 1f;
            check(bites <= cap, "six gnats together stay under the bite-rate cap (" + bites + " <= " + cap + ", veil=" + veil + ")");
            check(bites >= (veil ? 10 : 60), "the cap still lets a swarm bite (" + bites + ", veil=" + veil + ")");
        }
        check(SkyIslandMosquitoRules.OrbitRadiusFor(true) > SkyIslandMosquitoRules.BiteReach &&
              SkyIslandMosquitoRules.OrbitRadiusFor(false) < SkyIslandMosquitoRules.BiteReach,
            "with the veil the swarm circles outside biting reach and has to dive in");
    }

    private static void Itch(Action<bool, string> check)
    {
        float itch = 0f;
        bool itchy = false;
        int bitesToItch = 0;
        while (!itchy && bitesToItch < 20)
        {
            itch = SkyIslandMosquitoRules.StepItch(itch, 1, 1f, false);
            itchy = SkyIslandMosquitoRules.NextItchy(itchy, itch);
            bitesToItch++;
        }
        check(itchy && bitesToItch >= 6 && bitesToItch <= 10, "itch builds over several bites, not the first one (" + bitesToItch + ")");
        float seconds = 0f;
        while (itchy && seconds < 120f)
        {
            itch = SkyIslandMosquitoRules.StepItch(itch, 0, 0.5f, false);
            itchy = SkyIslandMosquitoRules.NextItchy(itchy, itch);
            seconds += 0.5f;
        }
        check(!itchy && seconds >= 10f && seconds <= 40f, "the itch fades on its own within half a minute without bites (" + seconds + " s)");
        check(SkyIslandMosquitoRules.NextItchy(true, SkyIslandMosquitoRules.ItchOff + 0.5f) &&
              !SkyIslandMosquitoRules.NextItchy(false, SkyIslandMosquitoRules.ItchOn - 0.5f), "itch hysteresis does not flicker");
        check(SkyIslandMosquitoRules.StepItch(0f, 10, 0f, true) == 0f, "bites add no itch while the salve soothes");
        check(SkyIslandMosquitoRules.StepItch(float.NaN, 1, 0f, false) == SkyIslandMosquitoRules.ItchPerBite &&
              SkyIslandMosquitoRules.StepItch(100f, 0, 0f, false) == SkyIslandMosquitoRules.ItchMax, "itch stays finite and capped");
        check(SkyIslandMosquitoRules.ItchStaminaRecover < 0f && SkyIslandMosquitoRules.ItchStaminaRecover > -0.25f,
            "itch is a small stamina penalty, milder than wind chill");
    }

    private static void FanAndZapper(Action<bool, string> check)
    {
        SkyIslandGnatVec player = V(0f, 0f, 0f), facing = V(0f, 0f, 1f);
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(0f, 1.2f, 2f)) == 1, "a gnat right in front of the fan is swatted");
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(0f, 1.2f, 5f)) == 2, "one further out is driven back");
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(0f, 1.2f, -2f)) == 0, "the fan does nothing behind you");
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(3f, 1.2f, 0.5f)) == 0, "nor far off to the side");
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(0f, 1.2f, 7f)) == 0, "nor beyond its reach");
        check(SkyIslandMosquitoRules.FanEffect(player, facing, V(0.01f, 1.2f, 0.01f)) == 1, "a gnat sitting on your face is swatted");
        check(SkyIslandMosquitoRules.FanKnockDistance <= SkyIslandMosquitoRules.MaxDash && SkyIslandMosquitoRules.FanDamage >= SkyIslandMosquitoRules.GnatHealth &&
              SkyIslandMosquitoRules.ZapperDamage >= SkyIslandMosquitoRules.GnatHealth, "a swat or a zap kills; knockback obeys the dash cap");
        SkyIslandGnatVec lamp = V(10f, 0f, 10f);
        check(SkyIslandMosquitoRules.ZapperReaches(lamp, V(12f, 1.2f, 10f)) && !SkyIslandMosquitoRules.ZapperReaches(lamp, V(14f, 1.2f, 10f)),
            "the zapper only shocks gnats inside its radius");
        check(SkyIslandMosquitoRules.ZapperLures(lamp, V(20f, 1.2f, 10f)) && !SkyIslandMosquitoRules.ZapperLures(lamp, V(25f, 1.2f, 10f)),
            "the zapper hums gnats in from further out");
        check(SkyIslandMosquitoRules.ZapperLureRadius > SkyIslandMosquitoRules.ZapperRadius && SkyIslandMosquitoRules.FanRange < SkyIslandMosquitoRules.ZapperLureRadius,
            "division of labour: the fan is close and instant, the zapper is an area held over time");
    }

    private static void Frogs(Action<bool, string> check)
    {
        var data = new SkyIslandStoryData { discoveredNotes = new string[0] };
        check(SkyIslandMosquitoRules.FrogsReleased(data) == 0 && SkyIslandMosquitoRules.NextFrogNote(data) == "Frog_1" &&
              !SkyIslandMosquitoRules.FrogsComplete(data), "a fresh save has no frogs back");
        for (int i = 0; i < SkyIslandMosquitoRules.FrogTarget; i++)
        {
            string note = SkyIslandMosquitoRules.NextFrogNote(data);
            check(SkyIslandMosquitoRules.IsFrogNote(note), "frog notes are registered journal ids");
            var notes = new List<string>(data.discoveredNotes) { note };
            data.discoveredNotes = notes.ToArray();
            check(SkyIslandMosquitoRules.FrogsReleased(data) == i + 1, "each release is counted once");
        }
        check(SkyIslandMosquitoRules.FrogsComplete(data) && SkyIslandMosquitoRules.NextFrogNote(data) == null, "three clutches fill the pool");
        check(!SkyIslandMosquitoRules.IsFrogNote("Frog_0") && !SkyIslandMosquitoRules.IsFrogNote("Frog_4") &&
              !SkyIslandMosquitoRules.IsFrogNote("Light_E") && !SkyIslandMosquitoRules.IsFrogNote(null), "only Frog_1 to Frog_3 are frog notes");
        check(SkyIslandMosquitoRules.FrogsReleased(null) == 0 && SkyIslandMosquitoRules.NextFrogNote(null) == null, "no save, no frogs");
    }

    private sealed class Walls : ISkyIslandGnatSpace
    {
        public bool DashClear(SkyIslandGnatVec from, SkyIslandGnatVec to) { return false; }
    }

    private static SkyIslandGnatShot Incoming(SkyIslandGnatVec at)
    {
        return new SkyIslandGnatShot(at + V(0f, 0f, -30f), V(0f, 0f, 1f), 60f, 60f, 0.08f);
    }

    private static void Kinematics(Action<bool, string> check)
    {
        for (float d = 0.05f; d <= SkyIslandMosquitoRules.MaxDash; d += 0.05f)
            check(Math.Abs(SkyIslandMosquitoRules.DashDistanceAt(SkyIslandMosquitoRules.TravelTime(d)) - d) < 1e-3f,
                "travel time inverts dash distance at " + d);
        check(SkyIslandMosquitoRules.TravelTime(SkyIslandMosquitoRules.MinDash) >= SkyIslandMosquitoRules.MinVisibleDashSeconds,
            "even the shortest dash lasts long enough to see");
        check(SkyIslandMosquitoRules.DashLengthFor(0.1f) == SkyIslandMosquitoRules.MinDash && SkyIslandMosquitoRules.DashLengthFor(99f) == SkyIslandMosquitoRules.MaxDash &&
              SkyIslandMosquitoRules.DashLengthFor(1.5f) == 1.5f, "dash length is clamped to [MinDash, MaxDash]");
        check(SkyIslandMosquitoRules.MaxDash <= 3f && SkyIslandMosquitoRules.DashSpeed >= 20f && SkyIslandMosquitoRules.DashSpeed <= 25f,
            "a dash is at most 3 m at 20 to 25 m/s");
        foreach (float dt in new[] { 1f / 144f, 1f / 60f, 1f / 30f, 0.04f, 0.1f })
        {
            var motor = new SkyIslandGnatMotor(false);
            SkyIslandGnatVec at = V(0f, 1.2f, 0f);
            check(motor.OnShot(at, Incoming(at), 1, null), "a far, slow bullet is dodged (dt=" + dt + ")");
            float moved = 0f;
            int movingFrames = 0, stillFrames = 0;
            for (int frame = 0; frame < 400 && motor.Phase != SkyIslandGnatPhase.Recover; frame++)
            {
                SkyIslandGnatVec step = motor.Step(dt);
                float length = step.Length;
                check(length <= SkyIslandMosquitoRules.DashSpeed * dt + 1e-4f, "per-step displacement never exceeds dash speed x dt (dt=" + dt + ")");
                if (length > 0f) movingFrames++;
                else if (movingFrames == 0) stillFrames++;
                moved += length;
            }
            check(Math.Abs(moved - motor.LastDashLength) < 1e-3f && motor.LastDashLength <= SkyIslandMosquitoRules.MaxDash,
                "the dash covers exactly its planned length and never more than 3 m (dt=" + dt + ")");
            if (Math.Abs(dt - 1f / 60f) < 1e-6f)
                check(stillFrames >= 2 && movingFrames >= 3,
                    "at 60 fps a dodge shows a windup (" + stillFrames + " frames) and at least three moving frames (" + movingFrames + ")");
        }
        var tired = new SkyIslandGnatMotor(true);
        int dodged = 0;
        SkyIslandGnatVec position = V(0f, 1.2f, 0f);
        for (int frame = 0; frame < 60; frame++)
        {
            if (tired.OnShot(position, Incoming(position), frame, null)) dodged++;
            position = position + tired.Step(1f / 60f);
            tired.Tick(1f / 60f);
        }
        check(dodged >= 2 && dodged <= (int)SkyIslandMosquitoRules.DodgeBudgetMax,
            "a second of point-blank-aimed bullets buys only the dodge budget (" + dodged + ")");
        var stunned = new SkyIslandGnatMotor(false);
        stunned.Knockback(V(1f, 0f, 0f), 10f, SkyIslandMosquitoRules.FanStunSeconds);
        check(stunned.LastDashLength <= SkyIslandMosquitoRules.MaxDash && stunned.StunnedFor > 0f, "fan knockback obeys the 3 m cap and stuns");
        float knocked = 0f;
        for (int frame = 0; frame < 60; frame++)
        {
            SkyIslandGnatVec step = stunned.Step(1f / 60f);
            check(step.Length <= SkyIslandMosquitoRules.DashSpeed / 60f + 1e-4f, "knockback also obeys the per-frame cap");
            knocked += step.Length;
        }
        check(Math.Abs(knocked - SkyIslandMosquitoRules.MaxDash) < 1e-3f, "knockback travels its capped distance");
        check(!stunned.OnShot(V(0f, 1.2f, 0f), Incoming(V(0f, 1.2f, 0f)), 99, null), "a stunned gnat cannot dodge");
        var dazzled = new SkyIslandGnatMotor(false) { Dazzled = true };
        check(!dazzled.OnShot(V(0f, 1.2f, 0f), Incoming(V(0f, 1.2f, 0f)), 1, null), "a gnat dazzled by lantern light cannot dodge a bullet");
        check(!dazzled.OnAim(V(0f, 1.2f, 5f), V(0f, 1.2f, 0f), V(0f, 0.5f, 10f), 1, null), "nor flinch from your aim");
        var aimed = new SkyIslandGnatMotor(false);
        check(aimed.OnAim(V(0.1f, 1.2f, 5f), V(0f, 1.2f, 0f), V(0f, 0.5f, 10f), 1, null), "a gnat flinches when the aim line sweeps over it");
        var ignored = new SkyIslandGnatMotor(false);
        check(!ignored.OnAim(V(2f, 1.2f, 5f), V(0f, 1.2f, 0f), V(0f, 0.5f, 10f), 1, null), "an aim line well to the side is ignored");
        var cornered = new SkyIslandGnatMotor(false);
        check(!cornered.OnShot(V(0f, 1.2f, 0f), Incoming(V(0f, 1.2f, 0f)), 1, new Walls()), "with walls on both sides there is no dodge");
        var late = new SkyIslandGnatMotor(false);
        var pointBlank = new SkyIslandGnatShot(V(0f, 1.2f, -1.5f), V(0f, 0f, 1f), 90f, 60f, 0.08f);
        check(!late.OnShot(V(0f, 1.2f, 0f), pointBlank, 1, null), "a bullet from 1.5 m arrives before any dash could clear it");
        check(SkyIslandMosquitoRules.MaxChecksPerFrame > SkyIslandMosquitoRules.MaxShotsPerFrame, "the per-frame check bound is meaningful");
    }

    private static void English(Action<bool, string> check)
    {
        L10n.IsChinese = false;
        try
        {
            var texts = new List<string>
            {
                SkyIslandMosquitoRules.SwarmArrives, SkyIslandMosquitoRules.LanternDraws, SkyIslandMosquitoRules.GaleScatters,
                SkyIslandMosquitoRules.SmokeScatters, SkyIslandMosquitoRules.ItchStarted, SkyIslandMosquitoRules.ItchEnded,
                SkyIslandMosquitoRules.Soothed, SkyIslandMosquitoRules.FanSwept(0, 0), SkyIslandMosquitoRules.FanSwept(2, 1),
                SkyIslandMosquitoRules.FanResting, SkyIslandMosquitoRules.ZapperLit, SkyIslandMosquitoRules.ZapperOut,
                SkyIslandMosquitoRules.ZapperLimit, SkyIslandMosquitoRules.ZapperNoGround, SkyIslandMosquitoRules.SpawnChoice(true, 3, 1),
                SkyIslandMosquitoRules.SpawnChoice(false, 0, 2), SkyIslandMosquitoRules.SpawnNeedsNight, SkyIslandMosquitoRules.SpawnNeedsFiber,
                SkyIslandMosquitoRules.SpawnAlreadyCarried, SkyIslandMosquitoRules.SpawnTaken, SkyIslandMosquitoRules.ReleaseChoice(0),
                SkyIslandMosquitoRules.Released(1), SkyIslandMosquitoRules.Released(3), SkyIslandMosquitoRules.FrogsAlreadyHome,
                SkyIslandMosquitoRules.FrogProgress(new SkyIslandStoryData())
            };
            foreach (string text in texts)
                check(!string.IsNullOrEmpty(text) && !Cjk(text), "English gnat text has no Chinese: " + text);
        }
        finally
        {
            L10n.IsChinese = true;
        }
    }

    private static bool Cjk(string text)
    {
        foreach (char c in text ?? string.Empty)
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF)) return true;
        return false;
    }
}
