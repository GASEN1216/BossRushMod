using System;
using System.Collections.Generic;
using BossRush;

/// <summary>
/// 云蚋躲子弹的离线模拟属性测试。生产 <see cref="SkyIslandGnatMotor"/> 原样执行；弹按官方 `Projectile` 的逐帧扫掠推进
/// （首帧从枪手身体中线起扫 v·dt + 0.3 m，之后每帧从当前位置往回 0.1 m 起扫 v·dt + 0.3 m，单帧时长封顶 0.04 s，
/// 命中 = 扫掠线段离蚊子中心不到弹半径 + 蚊子半径）。**同一帧里弹先扫、蚊子后动**——对蚊子最不利的执行顺序。
///
/// 射手两种：
/// - 玩家：准星带 0.12–0.20 s 的反应延迟追着「看见的位置」走（14 m/s），压到目标 0.06–0.14 s 才开火；
///   开火时按官方辅助瞄准，准星线（水平）离蚊子不到吸附半径就瞄它的中心，否则瞄准星在 y+0.5 平面上的点。这是「正常用枪」。
/// - 神枪：准星每帧都在蚊子身上、到点就开火，用来证明不是免疫（预算用完、贴脸、晃了眼时打得死）。
/// 场景：单发（0.5 s）/ 每秒 10 发全自动 / 8 丸 6° 散布的霰弹（0.8 s）；距离 3 / 5 / 10 / 20 m；玩家静止与横移（峰值约 3.5 m/s）；
/// 弹速 60 / 90 / 140 m/s（官方弹速按枪不同、源码里没有常量，取三档覆盖）；60 fps，另跑一组 30 fps。
///
/// 阈值是设计目标，写在断言里，不是跑出来之后回填的：
/// ≥ 4 m 的玩家单发首枪命中 ≤ 10%、前三枪 ≤ 20%、全自动第一秒 ≤ 30%、霰弹第一扳机 ≤ 20%；
/// 神枪全自动 5 m 三秒内至少打中一次 ≥ 95%（预算用完）、1.5 m 首枪 ≥ 90%（贴脸）、风灯下晃了眼时玩家 5 m 首枪 ≥ 50%。
/// </summary>
internal static class SkyIslandGnatDodgeSimulation
{
    private const float MuzzleHeight = 1.2f;
    private const float MuzzleForward = 0.6f;
    private const float BulletRadius = 0.08f;
    private const float BulletRange = 60f;
    private const float AimAssistRadius = 0.22f;
    private const float OnTargetRadius = 0.25f;
    private const float HumanAimSpeed = 14f;
    private const int HistorySize = 64;

    internal enum Weapon { Single, Auto, Shotgun }
    internal enum Shooter { Human, Oracle }

    private sealed class Bullet
    {
        internal SkyIslandGnatVec Origin, Position, Direction;
        internal float Speed, Traveled, FiredAt;
        internal int Index;
        internal bool First = true, Alive = true;
    }

    private sealed class OpenSpace : ISkyIslandGnatSpace
    {
        public bool DashClear(SkyIslandGnatVec from, SkyIslandGnatVec to) { return true; }
    }

    internal sealed class Outcome
    {
        internal int Trials, Bullets, Hits, FirstShotHits, FirstThreeShots, FirstThreeHits, EarlyBullets, EarlyHits, Pulls, PullKills;
        internal int TrialsHitWithin3s, Dashes, SpeedViolations, LengthViolations, VisibilityViolations, CheckViolations, MaxChecks;
        internal double TimeToHitSum;
        internal int TimeToHitCount;

        internal void Add(Outcome other)
        {
            Trials += other.Trials; Bullets += other.Bullets; Hits += other.Hits; FirstShotHits += other.FirstShotHits;
            FirstThreeShots += other.FirstThreeShots; FirstThreeHits += other.FirstThreeHits; EarlyBullets += other.EarlyBullets;
            EarlyHits += other.EarlyHits; Pulls += other.Pulls; PullKills += other.PullKills; TrialsHitWithin3s += other.TrialsHitWithin3s;
            Dashes += other.Dashes; SpeedViolations += other.SpeedViolations; LengthViolations += other.LengthViolations;
            VisibilityViolations += other.VisibilityViolations; CheckViolations += other.CheckViolations;
            MaxChecks = Math.Max(MaxChecks, other.MaxChecks); TimeToHitSum += other.TimeToHitSum; TimeToHitCount += other.TimeToHitCount;
        }

        internal double Rate(int numerator, int denominator) { return denominator == 0 ? 0.0 : (double)numerator / denominator; }
    }

    private static SkyIslandGnatVec V(float x, float y, float z) { return new SkyIslandGnatVec(x, y, z); }

    internal static void Run(Action<bool, string> check)
    {
        float[] speeds = { 60f, 90f, 140f };
        float[] distances = { 3f, 5f, 10f, 20f };
        var totals = new Outcome();
        var table = new Dictionary<string, Outcome>();
        int seed = 1;
        foreach (Weapon weapon in new[] { Weapon.Single, Weapon.Auto, Weapon.Shotgun })
            foreach (float distance in distances)
            {
                var row = new Outcome();
                foreach (float speed in speeds)
                    foreach (bool strafe in new[] { false, true })
                        row.Add(Simulate(weapon, Shooter.Human, distance, speed, strafe, false, 1f / 60f, 4f, 24, seed++));
                table[weapon + "@" + distance] = row;
                totals.Add(row);
            }

        Console.WriteLine("gnat dodge simulation (human shooter, 60 fps, 3 bullet speeds x 2 movements x 24 trials):");
        foreach (KeyValuePair<string, Outcome> entry in table)
        {
            Outcome o = entry.Value;
            Console.WriteLine(string.Format("  {0,-12} first {1,5:P0}  first3 {2,5:P0}  firstSecond {3,5:P0}  pullKill {4,5:P0}  all {5,5:P0}  dashes {6}",
                entry.Key, o.Rate(o.FirstShotHits, o.Trials), o.Rate(o.FirstThreeHits, o.FirstThreeShots), o.Rate(o.EarlyHits, o.EarlyBullets),
                o.Rate(o.PullKills, o.Pulls == 0 ? 0 : o.Trials), o.Rate(o.Hits, o.Bullets), o.Dashes));
        }

        foreach (float distance in new[] { 5f, 10f, 20f })
        {
            Outcome single = table[Weapon.Single + "@" + distance];
            Outcome auto = table[Weapon.Auto + "@" + distance];
            Outcome shotgun = table[Weapon.Shotgun + "@" + distance];
            check(single.Rate(single.FirstShotHits, single.Trials) <= 0.10,
                "normal single shots at " + distance + " m almost never hit a fresh gnat with the first shot (" + single.Rate(single.FirstShotHits, single.Trials) + ")");
            check(single.Rate(single.FirstThreeHits, single.FirstThreeShots) <= 0.20,
                "and rarely within the first three shots at " + distance + " m (" + single.Rate(single.FirstThreeHits, single.FirstThreeShots) + ")");
            check(auto.Rate(auto.EarlyHits, auto.EarlyBullets) <= 0.30,
                "full auto misses most of its first second at " + distance + " m (" + auto.Rate(auto.EarlyHits, auto.EarlyBullets) + ")");
            check(shotgun.Rate(shotgun.FirstShotHits, shotgun.Trials) <= 0.20,
                "a shotgun's first pull rarely kills at " + distance + " m (" + shotgun.Rate(shotgun.FirstShotHits, shotgun.Trials) + ")");
        }
        check(table[Weapon.Single + "@3"].Rate(table[Weapon.Single + "@3"].FirstShotHits, table[Weapon.Single + "@3"].Trials) >=
              table[Weapon.Single + "@10"].Rate(table[Weapon.Single + "@10"].FirstShotHits, table[Weapon.Single + "@10"].Trials),
            "closer is easier: 3 m first shots hit at least as often as 10 m");

        Outcome exhausted = Simulate(Weapon.Auto, Shooter.Oracle, 5f, 90f, false, false, 1f / 60f, 3f, 40, 9001);
        check(exhausted.Rate(exhausted.TrialsHitWithin3s, exhausted.Trials) >= 0.95,
            "not immune: sustained perfect fire at 5 m drains the dodge budget and hits within 3 s (" + exhausted.Rate(exhausted.TrialsHitWithin3s, exhausted.Trials) + ")");
        Outcome pointBlank = Simulate(Weapon.Single, Shooter.Oracle, 1.5f, 90f, false, false, 1f / 60f, 0.6f, 40, 9002);
        check(pointBlank.Rate(pointBlank.FirstShotHits, pointBlank.Trials) >= 0.90,
            "not immune: at 1.5 m there is no time to dash (" + pointBlank.Rate(pointBlank.FirstShotHits, pointBlank.Trials) + ")");
        Outcome dazzled = Simulate(Weapon.Single, Shooter.Human, 5f, 90f, false, true, 1f / 60f, 2f, 40, 9003);
        check(dazzled.Rate(dazzled.FirstShotHits, dazzled.Trials) >= 0.50,
            "not immune: under lantern light a normal first shot at 5 m usually hits (" + dazzled.Rate(dazzled.FirstShotHits, dazzled.Trials) + ")");
        check(!SkyIslandMosquitoRules.ShouldTrackProjectile(true, 9.8f, 0f) && !SkyIslandMosquitoRules.ShouldTrackProjectile(true, 0f, 4f),
            "not immune: grenades and rockets are never predicted, so their blasts land");
        totals.Add(exhausted);
        totals.Add(pointBlank);
        totals.Add(dazzled);

        var slow = new Outcome();
        slow.Add(Simulate(Weapon.Single, Shooter.Human, 10f, 90f, true, false, 1f / 30f, 4f, 16, 9100));
        slow.Add(Simulate(Weapon.Auto, Shooter.Oracle, 5f, 140f, false, false, 1f / 30f, 3f, 16, 9101));
        slow.Add(Simulate(Weapon.Shotgun, Shooter.Human, 5f, 60f, true, false, 1f / 30f, 4f, 16, 9102));
        totals.Add(slow);

        check(totals.Dashes > 0, "the simulation actually made the gnats dash (" + totals.Dashes + ")");
        check(totals.SpeedViolations == 0, "no frame ever moved a gnat faster than dash speed x frame time");
        check(totals.LengthViolations == 0, "no dash ever exceeded 3 m");
        check(totals.VisibilityViolations == 0, "every dash showed its windup and enough moving frames");
        check(totals.CheckViolations == 0 && totals.MaxChecks <= SkyIslandMosquitoRules.MaxChecksPerFrame,
            "per-frame checks stay under the bound (" + totals.MaxChecks + " <= " + SkyIslandMosquitoRules.MaxChecksPerFrame + ")");
        Console.WriteLine(string.Format("  not immune: exhausted {0:P0} within 3 s, point blank {1:P0}, dazzled {2:P0}; dashes {3}, max checks/frame {4}",
            exhausted.Rate(exhausted.TrialsHitWithin3s, exhausted.Trials), pointBlank.Rate(pointBlank.FirstShotHits, pointBlank.Trials),
            dazzled.Rate(dazzled.FirstShotHits, dazzled.Trials), totals.Dashes, totals.MaxChecks));
    }

    private static Outcome Simulate(Weapon weapon, Shooter shooter, float distance, float bulletSpeed, bool strafe, bool dazzled,
        float dt, float seconds, int trials, int seed)
    {
        var outcome = new Outcome();
        var space = new OpenSpace();
        var history = new SkyIslandGnatVec[HistorySize];
        var bullets = new List<Bullet>(64);
        bool sixty = Math.Abs(dt - 1f / 60f) < 1e-6f;
        for (int trial = 0; trial < trials; trial++)
        {
            var random = new Random(seed * 7919 + trial * 104729);
            var motor = new SkyIslandGnatMotor((trial & 1) == 1) { Dazzled = dazzled };
            float bearing = (float)(random.NextDouble() * 0.6 - 0.3);
            SkyIslandGnatVec player = V(0f, 0f, 0f);
            SkyIslandGnatVec home = V((float)Math.Sin(bearing) * distance, 0f, (float)Math.Cos(bearing) * distance);
            SkyIslandGnatVec gnat = V(home.X, SkyIslandMosquitoRules.HoverHeight, home.Z);
            SkyIslandGnatVec jitter = V(0f, 0f, 0f);
            float jitterTimer = 0f;
            SkyIslandGnatVec aim = shooter == Shooter.Oracle
                ? V(gnat.X, 0.5f, gnat.Z)
                : V(gnat.X + (float)(random.NextDouble() * 3.0 - 1.5), 0.5f, gnat.Z + (float)(random.NextDouble() * 3.0 - 1.5));
            float reaction = 0.12f + (float)random.NextDouble() * 0.08f;
            float clickDelay = 0.06f + (float)random.NextDouble() * 0.08f;
            float onTarget = 0f, offTarget = 0f, nextFire = (float)random.NextDouble() * 0.2f, firstShotAt = -1f, firstHitAt = -1f;
            bool held = false;
            int shotIndex = 0, lastDodges = 0, stillFrames = 0, movingFrames = 0;
            bool trackingDash = false;
            var shotHit = new List<bool>(64);
            bullets.Clear();
            int frames = (int)Math.Round(seconds / dt);
            for (int frame = 0; frame < frames; frame++)
            {
                float t = frame * dt;
                player = strafe ? V(2.5f * (float)Math.Sin(t * 1.4f), 0f, 0f) : V(0f, 0f, 0f);
                history[frame % HistorySize] = gnat;

                // ---- 射手 ----
                SkyIslandGnatVec seen = shooter == Shooter.Oracle ? gnat
                    : history[Math.Max(0, frame - (int)Math.Round(reaction / dt)) % HistorySize];
                if (shooter == Shooter.Oracle) aim = V(gnat.X, 0.5f, gnat.Z);
                else
                {
                    SkyIslandGnatVec toward = V(seen.X - aim.X, 0f, seen.Z - aim.Z);
                    float gap = toward.Length, reach = HumanAimSpeed * dt;
                    aim = gap <= reach ? V(seen.X, 0.5f, seen.Z) : aim + toward * (reach / gap);
                }
                SkyIslandGnatVec facing = (aim - player).Flat.Normalized;
                if (facing.SqrLength < 0.5f) facing = V(0f, 0f, 1f);
                SkyIslandGnatVec muzzle = V(player.X, MuzzleHeight, player.Z) + facing * MuzzleForward;
                bool onTargetNow = shooter == Shooter.Oracle || V(aim.X - seen.X, 0f, aim.Z - seen.Z).Length <= OnTargetRadius;
                onTarget = onTargetNow ? onTarget + dt : 0f;
                offTarget = onTargetNow ? 0f : offTarget + dt;
                float interval = weapon == Weapon.Auto ? 0.1f : weapon == Weapon.Shotgun ? 0.8f : 0.5f;
                bool fire;
                if (shooter == Shooter.Oracle) fire = t >= nextFire;
                else if (weapon == Weapon.Auto)
                {
                    if (onTarget >= clickDelay) held = true;
                    if (offTarget > 0.25f) held = false;
                    fire = held && t >= nextFire;
                }
                else fire = onTarget >= clickDelay && t >= nextFire;
                if (fire)
                {
                    nextFire = t + interval;
                    if (firstShotAt < 0f) firstShotAt = t;
                    SkyIslandGnatVec aimFlat = (aim - muzzle).Flat;
                    float aimLength = aimFlat.Length;
                    SkyIslandGnatVec point = aim;
                    if (aimLength > 0.1f)
                    {
                        aimFlat = aimFlat * (1f / aimLength);
                        SkyIslandGnatVec relative = (gnat - muzzle).Flat;
                        float along = SkyIslandGnatVec.Dot(relative, aimFlat);
                        if (along > 0.1f && (relative - aimFlat * along).Length <= AimAssistRadius) point = gnat;
                    }
                    SkyIslandGnatVec baseDirection = (point - muzzle).Normalized;
                    int pellets = weapon == Weapon.Shotgun ? 8 : 1;
                    shotHit.Add(false);
                    if (weapon == Weapon.Shotgun) outcome.Pulls++;
                    for (int p = 0; p < pellets; p++)
                    {
                        float yaw = (pellets > 1 ? -3f + p * (6f / 7f) : 0f) + (float)(random.NextDouble() - 0.5);
                        SkyIslandGnatVec direction = RotateY(baseDirection, yaw);
                        var bullet = new Bullet
                        {
                            Direction = direction, Speed = bulletSpeed, Position = muzzle, FiredAt = t, Index = shotIndex,
                            Origin = muzzle - direction.Flat.Normalized * MuzzleForward
                        };
                        bullets.Add(bullet);
                        outcome.Bullets++;
                        if (firstShotAt >= 0f && t - firstShotAt < 1f) outcome.EarlyBullets++;
                        motor.OnShot(gnat, new SkyIslandGnatShot(bullet.Origin, direction, bulletSpeed, BulletRange, BulletRadius), frame, space);
                    }
                    shotIndex++;
                }

                // ---- 弹先扫 ----
                float bulletStep = Math.Min(dt, 0.04f);
                for (int b = 0; b < bullets.Count; b++)
                {
                    Bullet bullet = bullets[b];
                    if (!bullet.Alive) continue;
                    float step = bullet.Speed * bulletStep;
                    SkyIslandGnatVec start = bullet.First ? bullet.Origin : bullet.Position - bullet.Direction * 0.1f;
                    float length = step + 0.3f;
                    SkyIslandGnatVec relative = gnat - start;
                    float s = SkyIslandGnatVec.Dot(relative, bullet.Direction);
                    s = s < 0f ? 0f : s > length ? length : s;
                    if ((relative - bullet.Direction * s).Length <= BulletRadius + SkyIslandMosquitoRules.GnatRadius)
                    {
                        bullet.Alive = false;
                        outcome.Hits++;
                        if (firstShotAt >= 0f && bullet.FiredAt - firstShotAt < 1f) outcome.EarlyHits++;
                        if (bullet.Index < shotHit.Count) shotHit[bullet.Index] = true;
                        if (firstHitAt < 0f) firstHitAt = t;
                        continue;
                    }
                    if (bullet.First) bullet.First = false;
                    bullet.Position = bullet.Position + bullet.Direction * step;
                    bullet.Traveled += step;
                    if (bullet.Traveled > BulletRange) bullet.Alive = false;
                }

                // ---- 蚊子后动 ----
                motor.Tick(dt);
                motor.OnAim(gnat, muzzle, aim, frame, space);
                outcome.MaxChecks = Math.Max(outcome.MaxChecks, motor.ChecksThisFrame);
                if (motor.ChecksThisFrame > SkyIslandMosquitoRules.MaxChecksPerFrame) outcome.CheckViolations++;
                if (motor.DodgesStarted != lastDodges)
                {
                    if (trackingDash && movingFrames == 0) outcome.VisibilityViolations++;
                    lastDodges = motor.DodgesStarted;
                    outcome.Dashes++;
                    trackingDash = true;
                    stillFrames = 0;
                    movingFrames = 0;
                }
                SkyIslandGnatVec move = motor.Step(dt);
                float moved = move.Length;
                if (moved > SkyIslandMosquitoRules.DashSpeed * dt + 1e-4f) outcome.SpeedViolations++;
                if (motor.LastDashLength > SkyIslandMosquitoRules.MaxDash + 1e-4f) outcome.LengthViolations++;
                if (trackingDash)
                {
                    if (moved > 0f) movingFrames++;
                    else if (movingFrames == 0) stillFrames++;
                    if (motor.Phase == SkyIslandGnatPhase.Recover || motor.Phase == SkyIslandGnatPhase.Idle)
                    {
                        trackingDash = false;
                        bool visible = sixty ? stillFrames >= 2 && movingFrames >= 3 : stillFrames >= 1 && movingFrames >= 2;
                        if (!visible) outcome.VisibilityViolations++;
                    }
                }
                if (moved > 0f) home = (gnat + move - player).Flat;
                else if (motor.Phase == SkyIslandGnatPhase.Idle)
                {
                    jitterTimer -= dt;
                    if (jitterTimer <= 0f)
                    {
                        jitter = V((float)(random.NextDouble() * 2.0 - 1.0), 0f, (float)(random.NextDouble() * 2.0 - 1.0)) * 1.5f;
                        jitterTimer = 0.25f + (float)random.NextDouble() * 0.15f;
                    }
                    SkyIslandGnatVec desired = home.Normalized * distance;
                    SkyIslandGnatVec want = (player + desired - gnat).Flat;
                    if (want.Length > 2.5f) want = want.Normalized * 2.5f;
                    want = want + jitter;
                    if (want.Length > SkyIslandMosquitoRules.CruiseSpeed) want = want.Normalized * SkyIslandMosquitoRules.CruiseSpeed;
                    move = want * dt;
                    if (move.Length > SkyIslandMosquitoRules.DashSpeed * dt + 1e-4f) outcome.SpeedViolations++;
                }
                gnat = gnat + move;
                gnat.Y = SkyIslandMosquitoRules.HoverHeight + SkyIslandMosquitoRules.HoverBob * (float)Math.Sin(t * 6f + trial);
            }
            outcome.Trials++;
            if (shotHit.Count > 0 && shotHit[0]) outcome.FirstShotHits++;
            if (weapon == Weapon.Shotgun && shotHit.Count > 0 && shotHit[0]) outcome.PullKills++;
            for (int i = 0; i < Math.Min(3, shotHit.Count); i++)
            {
                outcome.FirstThreeShots++;
                if (shotHit[i]) outcome.FirstThreeHits++;
            }
            if (firstHitAt >= 0f && firstHitAt <= 3f) outcome.TrialsHitWithin3s++;
            if (firstHitAt >= 0f)
            {
                outcome.TimeToHitSum += firstHitAt;
                outcome.TimeToHitCount++;
            }
        }
        return outcome;
    }

    private static SkyIslandGnatVec RotateY(SkyIslandGnatVec direction, float degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        return new SkyIslandGnatVec(direction.X * cos + direction.Z * sin, direction.Y, -direction.X * sin + direction.Z * cos).Normalized;
    }
}
