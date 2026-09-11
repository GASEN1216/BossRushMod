using System;

namespace BossRush
{
    /// <summary>纯规则用的三维向量：不依赖 UnityEngine，运行时与 Vector3 逐分量互转。结构体、无分配。</summary>
    internal struct SkyIslandGnatVec
    {
        internal float X, Y, Z;

        internal SkyIslandGnatVec(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static SkyIslandGnatVec operator +(SkyIslandGnatVec a, SkyIslandGnatVec b) { return new SkyIslandGnatVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static SkyIslandGnatVec operator -(SkyIslandGnatVec a, SkyIslandGnatVec b) { return new SkyIslandGnatVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static SkyIslandGnatVec operator *(SkyIslandGnatVec a, float s) { return new SkyIslandGnatVec(a.X * s, a.Y * s, a.Z * s); }

        internal static float Dot(SkyIslandGnatVec a, SkyIslandGnatVec b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        internal float SqrLength { get { return X * X + Y * Y + Z * Z; } }
        internal float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        /// <summary>压到水平面（y = 0）。</summary>
        internal SkyIslandGnatVec Flat { get { return new SkyIslandGnatVec(X, 0f, Z); } }
        internal SkyIslandGnatVec Normalized
        {
            get
            {
                float length = Length;
                return length > 1e-6f ? new SkyIslandGnatVec(X / length, Y / length, Z / length) : new SkyIslandGnatVec(0f, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// 一发子弹（霰弹的一丸）的预测输入，取自官方 `ProjectileContext` 与 `Projectile.radius`：
    /// <see cref="Origin"/> 是首帧扫掠的起点（`firstFrameCheckStartPoint`，枪手的身体中线），<see cref="Direction"/> 为单位向量。
    /// </summary>
    internal struct SkyIslandGnatShot
    {
        internal SkyIslandGnatVec Origin;
        internal SkyIslandGnatVec Direction;
        internal float Speed;
        internal float Range;
        internal float Radius;

        internal SkyIslandGnatShot(SkyIslandGnatVec origin, SkyIslandGnatVec direction, float speed, float range, float radius)
        {
            Origin = origin;
            Direction = direction.Normalized;
            Speed = speed;
            Range = range;
            Radius = radius;
        }
    }

    /// <summary>刷新权重要看的现场。局内 owner 每 0.5 游戏秒按玩家脚下采样一次（与夜风同一次地面射线）。</summary>
    internal struct SkyIslandGnatSite
    {
        internal bool Night;
        /// <summary>环境风力 0 无风 / 1 微风 / 2 大风：夜风规则算出来的那一档，**不经噬风之核减风**（核只改你身上的寒意，不改空气）。</summary>
        internal int WindLevel;
        /// <summary>站在灶火的烟里。</summary>
        internal bool InSmoke;
        /// <summary>驱风香燃着。</summary>
        internal bool Incense;
        /// <summary>附近还有活着的敌人（与剧情面板同一道 35 m 战斗门）。</summary>
        internal bool EnemiesNear;
        /// <summary>离最近一处静水岸边的水平距离（米）。</summary>
        internal float WaterEdgeDistance;
        /// <summary>本存档往蛙鸣池放生过几团蛙卵。</summary>
        internal int FrogsReleased;
        /// <summary>站在岛心（背风）。</summary>
        internal bool IslandCore;
        /// <summary>风灯燃着（光招云蚋）。</summary>
        internal bool Lantern;
        /// <summary>点起来的风晶灯附近（光招云蚋）。灶火不算：灶火有烟。</summary>
        internal bool NearLamp;
        /// <summary>燃着的灭蚊灯附近（嗡声引蚋）。</summary>
        internal bool NearZapper;
    }

    /// <summary>冲刺路径上有没有墙：运行时用 `Physics.Linecast`，离线模拟用自带的障碍（或恒通）。</summary>
    internal interface ISkyIslandGnatSpace
    {
        bool DashClear(SkyIslandGnatVec from, SkyIslandGnatVec to);
    }

    internal enum SkyIslandGnatPhase
    {
        Idle = 0,
        /// <summary>冲刺前摇：原地振翅发亮，不移动。</summary>
        Windup = 1,
        Dash = 2,
        /// <summary>冲刺落地后的一小段喘息：不再躲、不主动飞。</summary>
        Recover = 3
    }

    /// <summary>
    /// 一只云蚋的躲闪状态机：躲闪预算、冷却、前摇、冲刺的逐帧位移。**纯逻辑、无分配**：运行时与离线模拟跑的是同一份代码。
    ///
    /// 两种触发：
    /// - <see cref="OnAim"/>：准星（水平方向）扫过它时提前闪。官方辅助瞄准会把瞄准点吸到准星线上的伤害接收体上，
    ///   准星一旦压到它，子弹就是冲着它的中心去的——所以「准星扫过」本身就是威胁。
    /// - <see cref="OnShot"/>：玩家的弹生成时（`Projectile.Init` 后置补丁）预测弹道上离它最近的点；会被打中且来得及就侧向冲刺。
    ///   同一帧的多丸（霰弹）只在新弹仍穿过冲刺终点时重新规划，重新规划不再扣预算。
    ///
    /// 不是免疫：预算用完、冷却中、贴脸（首帧扫掠约 2 m 内来不及）、风灯下晃了眼、被扇晕、爆炸与范围伤害都打得死它。
    /// </summary>
    internal sealed class SkyIslandGnatMotor
    {
        private readonly SkyIslandGnatShot[] frameShots = new SkyIslandGnatShot[SkyIslandMosquitoRules.MaxShotsPerFrame];
        private readonly float sideBias;
        private int frameShotCount;
        private int shotFrame = int.MinValue, planFrame = int.MinValue, countedFrame = int.MinValue;
        private SkyIslandGnatVec dashFrom, dashTo, dashDirection;
        private float dashLength, traveled, phaseTime, windup, dashElapsed;

        internal SkyIslandGnatMotor(bool leftHanded)
        {
            sideBias = leftHanded ? -1f : 1f;
            Budget = SkyIslandMosquitoRules.DodgeBudgetMax;
        }

        internal SkyIslandGnatPhase Phase { get; private set; }
        /// <summary>可用的躲闪次数（小数部分是正在恢复的那一次）。</summary>
        internal float Budget { get; private set; }
        internal float Cooldown { get; private set; }
        internal float StunnedFor { get; private set; }
        /// <summary>风灯光下晃了眼：这一刻不躲。由运行时每帧写。</summary>
        internal bool Dazzled;
        /// <summary>这一帧做了多少次弹道 / 准星 / 路径检测（离线模拟断言它有上界）。</summary>
        internal int ChecksThisFrame { get; private set; }
        internal int DodgesStarted { get; private set; }
        internal int Replans { get; private set; }
        internal float LastDashLength { get; private set; }
        internal float LastDashSeconds { get; private set; }
        internal SkyIslandGnatVec DashDirection { get { return dashDirection; } }
        internal bool Busy { get { return Phase == SkyIslandGnatPhase.Windup || Phase == SkyIslandGnatPhase.Dash; } }
        private bool Ready { get { return !Busy && StunnedFor <= 0f && !Dazzled && Budget >= 1f && Cooldown <= 0f; } }

        /// <summary>按游戏时间恢复预算、走冷却与眩晕。</summary>
        internal void Tick(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return;
            Budget = Math.Min(SkyIslandMosquitoRules.DodgeBudgetMax, Budget + seconds / SkyIslandMosquitoRules.DodgeRegenSeconds);
            if (Cooldown > 0f) Cooldown = Math.Max(0f, Cooldown - seconds);
            if (StunnedFor > 0f) StunnedFor = Math.Max(0f, StunnedFor - seconds);
        }

        /// <summary>准星扫过：<paramref name="muzzle"/> 到 <paramref name="aimPoint"/> 的水平射线离它不到 <see cref="SkyIslandMosquitoRules.AimPreDodgeRadius"/> 就提前闪。</summary>
        internal bool OnAim(SkyIslandGnatVec position, SkyIslandGnatVec muzzle, SkyIslandGnatVec aimPoint, int frame, ISkyIslandGnatSpace space)
        {
            Count(frame, 1);
            if (!Ready) return false;
            SkyIslandGnatVec direction = (aimPoint - muzzle).Flat;
            float length = direction.Length;
            if (length < 0.1f) return false;
            direction = direction * (1f / length);
            SkyIslandGnatVec relative = (position - muzzle).Flat;
            float along = SkyIslandGnatVec.Dot(relative, direction);
            if (along < SkyIslandMosquitoRules.AimMinAlong || along > SkyIslandMosquitoRules.AimMaxAlong) return false;
            SkyIslandGnatVec offset = relative - direction * along;
            float miss = offset.Length;
            if (miss >= SkyIslandMosquitoRules.AimPreDodgeRadius) return false;
            SkyIslandGnatVec side = SkyIslandMosquitoRules.SideOf(direction, offset, sideBias);
            float keep = SkyIslandMosquitoRules.AimPreDodgeRadius + SkyIslandMosquitoRules.AimExitMargin;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // 往准星偏着的那一侧躲只需再挪 keep − miss；那一侧有墙就换另一侧，要先穿过准星线，得挪 keep + miss。
                SkyIslandGnatVec way = attempt == 0 ? side : side * -1f;
                float dash = SkyIslandMosquitoRules.DashLengthFor(attempt == 0 ? keep - miss : keep + miss);
                Count(frame, 1);
                if (space != null && !space.DashClear(position, position + way * dash)) continue;
                Begin(position, way, dash, SkyIslandMosquitoRules.WindupAimSeconds, frame, true);
                return true;
            }
            return false;
        }

        /// <summary>登记一发玩家的弹（霰弹每丸各调一次）。会被打中、来得及、预算与冷却都允许时开始躲；返回是否（重新）规划了冲刺。</summary>
        internal bool OnShot(SkyIslandGnatVec position, SkyIslandGnatShot shot, int frame, ISkyIslandGnatSpace space)
        {
            if (!(shot.Speed > 0f) || shot.Direction.SqrLength < 0.5f) return false;
            if (frame != shotFrame) { shotFrame = frame; frameShotCount = 0; }
            // 一帧最多记 MaxShotsPerFrame 丸：超出的不参与规划，检测次数因此有上界。
            if (frameShotCount >= frameShots.Length) return false;
            frameShots[frameShotCount++] = shot;
            Count(frame, 1);
            if (Busy)
            {
                // 已经在躲：只在同一帧、还在前摇里、新弹仍穿过冲刺终点时重新规划；之后几帧的弹不改主意（冷却里本来就躲不了）。
                if (frame != planFrame || Phase != SkyIslandGnatPhase.Windup || !SkyIslandMosquitoRules.Threatens(shot, dashTo)) return false;
                Replans++;
                return Plan(dashFrom, frame, space, windup, false);
            }
            if (!SkyIslandMosquitoRules.Threatens(shot, position) || !Ready) return false;
            return Plan(position, frame, space, SkyIslandMosquitoRules.WindupShotSeconds, true);
        }

        /// <summary>
        /// 被蒲扇扇退：不走前摇、不扣预算，按冲刺的速度上限退开（至多 <see cref="SkyIslandMosquitoRules.MaxDash"/>），并晕一小会儿。
        /// </summary>
        internal void Knockback(SkyIslandGnatVec direction, float distance, float stunSeconds)
        {
            SkyIslandGnatVec way = direction.Flat.Normalized;
            if (way.SqrLength < 0.5f) way = new SkyIslandGnatVec(1f, 0f, 0f);
            dashFrom = new SkyIslandGnatVec(0f, 0f, 0f);
            dashDirection = way;
            dashLength = Math.Max(0f, Math.Min(distance, SkyIslandMosquitoRules.MaxDash));
            dashTo = way * dashLength;
            traveled = 0f;
            dashElapsed = SkyIslandMosquitoRules.DashAccelSeconds;
            Phase = SkyIslandGnatPhase.Dash;
            LastDashLength = dashLength;
            StunnedFor = Math.Max(StunnedFor, stunSeconds);
        }

        /// <summary>收掉进行中的冲刺（散去、被吹走时）。</summary>
        internal void Cancel()
        {
            Phase = SkyIslandGnatPhase.Idle;
            phaseTime = 0f;
        }

        /// <summary>
        /// 推进一段游戏时间，返回这段时间里的位移：前摇期间为零；冲刺期间按「振翅加速 → 全速」的速度曲线走，
        /// **单步位移恒不超过 <see cref="SkyIslandMosquitoRules.DashSpeed"/> × 时长**，走满冲刺长度即进入喘息。
        /// </summary>
        internal SkyIslandGnatVec Step(float seconds)
        {
            SkyIslandGnatVec none = new SkyIslandGnatVec(0f, 0f, 0f);
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return none;
            if (Phase == SkyIslandGnatPhase.Windup)
            {
                phaseTime += seconds;
                if (phaseTime < windup) return none;
                seconds = phaseTime - windup;
                Phase = SkyIslandGnatPhase.Dash;
                dashElapsed = 0f;
            }
            if (Phase == SkyIslandGnatPhase.Dash)
            {
                float before = SkyIslandMosquitoRules.DashDistanceAt(dashElapsed);
                dashElapsed += seconds;
                float step = SkyIslandMosquitoRules.DashDistanceAt(dashElapsed) - before;
                float left = dashLength - traveled;
                if (step >= left)
                {
                    step = Math.Max(0f, left);
                    Phase = SkyIslandGnatPhase.Recover;
                    phaseTime = 0f;
                    LastDashSeconds = SkyIslandMosquitoRules.TravelTime(dashLength);
                }
                traveled += step;
                return dashDirection * step;
            }
            if (Phase == SkyIslandGnatPhase.Recover)
            {
                phaseTime += seconds;
                if (phaseTime >= SkyIslandMosquitoRules.RecoverSeconds) Phase = SkyIslandGnatPhase.Idle;
            }
            return none;
        }

        private void Count(int frame, int amount)
        {
            if (frame != countedFrame) { countedFrame = frame; ChecksThisFrame = 0; }
            ChecksThisFrame += amount;
        }

        /// <summary>
        /// 对这一帧登记过的全部弹规划一次冲刺：以第一发威胁弹决定先往哪边躲（往已经偏着的一侧），
        /// 从最短的冲刺开始逐档加长，要求终点离开每一发弹的「管子」、而且在每一发弹到达之前穿出（或等它过去才穿进）。
        /// </summary>
        private bool Plan(SkyIslandGnatVec from, int frame, ISkyIslandGnatSpace space, float windupSeconds, bool fresh)
        {
            int lead = -1;
            for (int i = 0; i < frameShotCount && lead < 0; i++)
            {
                Count(frame, 1);
                if (SkyIslandMosquitoRules.Threatens(frameShots[i], from) || (!fresh && SkyIslandMosquitoRules.Threatens(frameShots[i], dashTo)))
                    lead = i;
            }
            if (lead < 0) return false;
            SkyIslandGnatVec relative = from - frameShots[lead].Origin;
            SkyIslandGnatVec offset = relative - frameShots[lead].Direction * SkyIslandGnatVec.Dot(relative, frameShots[lead].Direction);
            SkyIslandGnatVec side = SkyIslandMosquitoRules.SideOf(frameShots[lead].Direction, offset, sideBias);
            float stride = (SkyIslandMosquitoRules.MaxDash - SkyIslandMosquitoRules.MinDash) / (SkyIslandMosquitoRules.MaxPlanCandidates - 1);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                SkyIslandGnatVec way = attempt == 0 ? side : side * -1f;
                for (int k = 0; k < SkyIslandMosquitoRules.MaxPlanCandidates; k++)
                {
                    float dash = SkyIslandMosquitoRules.MinDash + stride * k;
                    if (!Clears(from, way, dash, windupSeconds, frame)) continue;
                    Count(frame, 1);
                    // 这一侧撞墙就换另一侧：同一侧更长的冲刺只会撞得更深。
                    if (space != null && !space.DashClear(from, from + way * dash)) break;
                    Begin(from, way, dash, windupSeconds, frame, fresh);
                    return true;
                }
            }
            return false;
        }

        private bool Clears(SkyIslandGnatVec from, SkyIslandGnatVec way, float dash, float windupSeconds, int frame)
        {
            float lag = SkyIslandMosquitoRules.PlanFrameSeconds + windupSeconds;
            SkyIslandGnatVec end = from + way * dash;
            for (int i = 0; i < frameShotCount; i++)
            {
                SkyIslandGnatShot shot = frameShots[i];
                Count(frame, 1);
                if (SkyIslandMosquitoRules.Threatens(shot, end)) return false;
                float enter, exit;
                if (!SkyIslandMosquitoRules.TubeSpan(shot, from, way, dash, out enter, out exit)) continue;
                float impact = SkyIslandMosquitoRules.TimeToImpact(shot, SkyIslandGnatVec.Dot(from - shot.Origin, shot.Direction));
                if (lag + SkyIslandMosquitoRules.TravelTime(exit) <= impact) continue;
                // 穿进管子的时刻晚于弹扫过这里（再留两帧与一截扫掠厚度），也躲得开。
                if (enter > 0f && lag + SkyIslandMosquitoRules.TravelTime(enter) >=
                    impact + 2f * SkyIslandMosquitoRules.PlanFrameSeconds + SkyIslandMosquitoRules.FirstSweepExtra / shot.Speed) continue;
                return false;
            }
            return true;
        }

        private void Begin(SkyIslandGnatVec from, SkyIslandGnatVec way, float dash, float windupSeconds, int frame, bool fresh)
        {
            dashFrom = from;
            dashDirection = way;
            dashLength = dash;
            dashTo = from + way * dash;
            traveled = 0f;
            planFrame = frame;
            LastDashLength = dash;
            if (!fresh) return;
            Phase = SkyIslandGnatPhase.Windup;
            phaseTime = 0f;
            windup = windupSeconds;
            Budget -= 1f;
            Cooldown = SkyIslandMosquitoRules.DodgeCooldown;
            DodgesStarted++;
        }
    }

    /// <summary>
    /// COMPAT：天空岛内容批次四「云蚋」——夜里的蚊群的纯规则：刷在哪、叮多狠、怎么躲子弹、烟 / 光 / 风 / 蛙怎么改它，以及玩家看到的话。
    ///
    /// 来历全部取自场景已有内容：蛙鸣池（`S1_FrogPond`）、镜水寺的池（`F_MirrorPool`，见闻 F_02）、青穗梯田的水车与雨水桶是静水；
    /// 风灾之后蛙鸣池没了青蛙（来信《写给池子里的青蛙》）；栈道栏杆刻着「灯亮之后……有东西会循着光过来」；大风里蚊子飞不稳。
    ///
    /// 冻结口径：
    /// - **只在夜里**（<see cref="SkyIslandNight"/>，与光照、夜风同一个判断）；灶火的烟里、驱风香燃着、附近有敌人、大风里都不刷。
    /// - **叮人不叮死**：每口 1 点真实伤害，全部蚊子合计的叮咬速率封顶，生命低于上限的 <see cref="BiteHealthFloor"/> 只绕不叮；
    ///   叮久了叠「痒」只动耐力恢复，不做数值墙。
    /// - **躲子弹但不免疫**：见 <see cref="SkyIslandGnatMotor"/>。每次冲刺至多 <see cref="MaxDash"/> 米、速度上限 <see cref="DashSpeed"/>，有前摇与拖尾，不是瞬移。
    /// - **死了不掉东西**：不给刷材料的理由（待拍板）。
    /// - **不加存档字段**：放生的蛙卵写进本槽手记（`discoveredNotes`，id 前缀 <see cref="FrogNotePrefix"/>）。
    /// 纯逻辑、无 Unity 依赖，隔离回归（`tests/fixtures/SkyIslandStory`）直接执行规则与躲闪离线模拟。
    /// </summary>
    internal static class SkyIslandMosquitoRules
    {
        #region 刷新

        internal const int MaxAlive = 6;
        internal const int GroupMin = 2;
        internal const int GroupMax = 4;
        /// <summary>在玩家周围、屏幕外缘刷：鸭科夫一屏约 28×20 m 地面，8–14 m 正好在边上。</summary>
        internal const float SpawnMinDistance = 8f;
        internal const float SpawnMaxDistance = 14f;
        /// <summary>多久掷一次「这一片要不要来一群」（游戏秒）。</summary>
        internal const float SpawnCheckSeconds = 4f;
        internal const float SpawnCooldownMin = 10f;
        internal const float SpawnCooldownMax = 18f;
        internal const double SpawnChancePerWeight = 0.5;
        internal const double SpawnChanceMax = 0.85;
        /// <summary>离玩家这么远就散了（跑远了甩得掉）。</summary>
        internal const float DespawnDistance = 30f;
        internal const float BaseWeight = 0.55f;
        /// <summary>离静水岸边这么近算「近水」。</summary>
        internal const float WaterReach = 20f;
        /// <summary>一只青蛙都没有时近水的倍率；每放生一团蛙卵降 <see cref="WaterBoostStep"/>，放满 <see cref="FrogTarget"/> 团回到 1（不再加成）。</summary>
        internal const float WaterBoostMax = 2.2f;
        internal const float WaterBoostStep = 0.4f;
        internal const float BreezeFactor = 0.5f;
        internal const float IslandCoreFactor = 1.35f;
        /// <summary>岛心 = 离岛中心不到「岛框短边一半」的这个比例。</summary>
        internal const float IslandCoreFraction = 0.35f;
        internal const float LanternFactor = 1.5f;
        internal const float LampFactor = 1.4f;
        internal const float LampRadius = 12f;
        internal const float ZapperFactor = 1.5f;
        /// <summary>灶火的烟：比取暖半径（7 m）大一圈，蚊子在烟边上就掉头。</summary>
        internal const float SmokeRadius = 9f;
        /// <summary>驱风香燃着时，身边这么近的云蚋散开。</summary>
        internal const float IncenseRepelRadius = 6f;

        #endregion

        #region 飞行、叮咬与痒

        internal const float CruiseSpeed = 4.2f;
        internal const float HoverHeight = 1.25f;
        internal const float HoverBob = 0.08f;
        internal const float OrbitRadius = 0.45f;
        /// <summary>带着云苔纱笠：只能在这么远处打转，难贴身。</summary>
        internal const float VeilOrbitRadius = 1.0f;
        internal const float BiteReach = 0.6f;
        internal const float BiteIntervalMin = 1.5f;
        internal const float BiteIntervalMax = 2.5f;
        /// <summary>全部蚊子合计：两口之间至少隔这么久（游戏秒），即每秒至多约 1.1 口。</summary>
        internal const float GlobalBiteGap = 0.9f;
        internal const float VeilBiteFactor = 3f;
        internal const float VeilGapFactor = 2f;
        internal const float BiteDamage = 1f;
        /// <summary>生命低于上限的这个比例时只绕不叮：云蚋叮不死人。</summary>
        internal const float BiteHealthFloor = 0.35f;
        internal const float GnatHealth = 3f;
        internal const float ItchPerBite = 1f;
        internal const float ItchDecayPerSecond = 0.2f;
        internal const float ItchOn = 6f;
        internal const float ItchOff = 2f;
        internal const float ItchMax = 10f;
        /// <summary>痒：耐力恢复 −12%（与风寒可以同时在身上，合计也只是耐力恢复慢一些）。</summary>
        internal const float ItchStaminaRecover = -0.12f;
        /// <summary>抹了星苔药膏之后这么久里，叮上也不痒（游戏秒）。</summary>
        internal const float SalveSootheSeconds = 90f;
        /// <summary>风灯光里的云蚋晃了眼、躲不开：离玩家这么近。</summary>
        internal const float LanternDazzleRadius = 6f;

        #endregion

        #region 躲闪

        internal const float GnatRadius = 0.2f;
        internal const float ShotMargin = 0.12f;
        internal const float DashSpeed = 22f;
        internal const float DashAccelSeconds = 0.04f;
        internal const float MinDash = 0.9f;
        /// <summary>每次冲刺的硬上限（米）。</summary>
        internal const float MaxDash = 3f;
        /// <summary>弹触发的前摇：约两帧（60 fps）的发亮振翅。</summary>
        internal const float WindupShotSeconds = 0.034f;
        /// <summary>准星触发的前摇：约三帧，玩家看得见它「一抖」。</summary>
        internal const float WindupAimSeconds = 0.05f;
        internal const float RecoverSeconds = 0.12f;
        internal const float DodgeCooldown = 0.22f;
        internal const float DodgeBudgetMax = 5f;
        /// <summary>恢复一次躲闪要的游戏秒数。</summary>
        internal const float DodgeRegenSeconds = 1.0f;
        internal const float AimPreDodgeRadius = 0.55f;
        internal const float AimExitMargin = 0.35f;
        internal const float AimMinAlong = 1.2f;
        internal const float AimMaxAlong = 45f;
        /// <summary>规划时假设的一帧（60 fps）：弹的首帧扫掠长度与蚊子的反应延迟都按它算。帧率更低时首帧扫得更远，离线模拟另跑 30 fps。</summary>
        internal const float PlanFrameSeconds = 1f / 60f;
        /// <summary>官方弹每帧多扫的一截（`_distanceThisFrame + 0.3`，起点再往回 0.1 m）。</summary>
        internal const float FirstSweepExtra = 0.35f;
        internal const int MaxShotsPerFrame = 16;
        internal const int MaxPlanCandidates = 8;
        /// <summary>最短的冲刺也要持续这么久才看得见（60 fps 下约三帧）。</summary>
        internal const float MinVisibleDashSeconds = 0.05f;

        /// <summary>
        /// 一只蚊子一帧里检测次数的上界：准星 1 次 + 准星两侧各 1 次路径检测，加上每一丸：登记 1 次 + 找威胁弹至多一帧的丸数 +
        /// 两侧 × 候选档数 ×（逐丸检测 + 1 次路径检测）。
        /// </summary>
        internal static int MaxChecksPerFrame
        {
            get { return 3 + MaxShotsPerFrame * (1 + MaxShotsPerFrame + 2 * MaxPlanCandidates * (MaxShotsPerFrame + 1)); }
        }

        #endregion

        #region 蒲扇与灭蚊灯

        internal const float FanRange = 3.4f;
        internal const float FanKnockRange = 6f;
        internal const float FanHalfAngleDegrees = 55f;
        internal const float FanDamage = 3f;
        internal const float FanKnockDistance = 2.5f;
        internal const float FanStunSeconds = 1.2f;
        internal const float FanCooldownSeconds = 1f;
        internal const float ZapperBurnSeconds = 300f;
        internal const float ZapperRadius = 3.2f;
        internal const float ZapperLureRadius = 12f;
        internal const float ZapperPulseSeconds = 0.7f;
        internal const float ZapperDamage = 3f;
        internal const int ZapperMaxActive = 2;

        #endregion

        #region 静水、岛心与青蛙

        /// <summary>静水（作者布局 `obstacles` 里的池、水车与雨水桶，根节点本地坐标）：中心与半径 = 障碍框水平对角线的一半。守卫从 layout.json 推导核对。</summary>
        internal sealed class Water
        {
            internal string Id;
            internal float X, Z, Radius;
        }

        private static readonly Water[] waters =
        {
            MakeWater("S1_FrogPond", -292f, -106f, 10.82f),
            MakeWater("F_MirrorPool", 175f, -62.333f, 28.26f),
            MakeWater("C_WaterMill", -199.211f, -66.471f, 10f),
            MakeWater("C_Life11_rain_barrel", -201.947f, -48.206f, 2.99f)
        };

        internal static Water[] Waters { get { return waters; } }

        /// <summary>岛框（作者布局 `islands`，根节点本地坐标）：中心与短边的一半。守卫从 layout.json 推导核对。</summary>
        internal sealed class Island
        {
            internal string Region;
            internal float X, Z, HalfMin;
        }

        private static readonly Island[] islands =
        {
            MakeIsland("A", 0f, -220f, 42.5f), MakeIsland("B", 0f, -95f, 52.5f), MakeIsland("C", -165f, -80f, 57.5f),
            MakeIsland("D", -160f, 75f, 60f), MakeIsland("E", 15f, 80f, 52.5f), MakeIsland("F", 175f, -65f, 60f),
            MakeIsland("G", 175f, 75f, 52.5f), MakeIsland("H", 5f, 225f, 52.5f), MakeIsland("S1", -290f, -100f, 25f),
            MakeIsland("S2", -282.5f, 100f, 27.5f), MakeIsland("S3", 302.5f, -65f, 32.5f), MakeIsland("S4", 295f, 75f, 32.5f)
        };

        internal static Island[] Islands { get { return islands; } }

        internal const string FrogNotePrefix = "Frog_";
        internal const int FrogTarget = 3;

        #endregion

        #region 刷新规则

        /// <summary>这一片现在刷云蚋的相对权重；0 表示不刷。各项是乘法，互不抵消。</summary>
        internal static float SpawnWeight(SkyIslandGnatSite site)
        {
            if (!site.Night || site.InSmoke || site.Incense || site.EnemiesNear || site.WindLevel >= 2) return 0f;
            float weight = BaseWeight;
            if (site.WaterEdgeDistance <= WaterReach) weight *= WaterBoost(site.FrogsReleased);
            if (site.WindLevel == 1) weight *= BreezeFactor;
            if (site.IslandCore) weight *= IslandCoreFactor;
            if (site.Lantern) weight *= LanternFactor;
            if (site.NearLamp) weight *= LampFactor;
            if (site.NearZapper) weight *= ZapperFactor;
            return weight;
        }

        /// <summary>近水倍率：放生的蛙卵越多越低，放满回到 1。</summary>
        internal static float WaterBoost(int frogsReleased)
        {
            int frogs = frogsReleased < 0 ? 0 : frogsReleased > FrogTarget ? FrogTarget : frogsReleased;
            return WaterBoostMax - WaterBoostStep * frogs;
        }

        /// <summary>这一次掷骰来一群的概率。</summary>
        internal static double SpawnChance(float weight)
        {
            if (!(weight > 0f)) return 0.0;
            return Math.Min(SpawnChanceMax, weight * SpawnChancePerWeight);
        }

        /// <summary>一群几只：权重越高越可能多，恒在 <see cref="GroupMin"/>–<see cref="GroupMax"/>。</summary>
        internal static int GroupSize(float weight, double roll)
        {
            double spread = 1.0 + Math.Min(2.0, Math.Max(0.0, weight));
            int size = GroupMin + (int)Math.Floor(Clamp01(roll) * spread);
            return size < GroupMin ? GroupMin : size > GroupMax ? GroupMax : size;
        }

        /// <summary>还能再刷几只（同时至多 <see cref="MaxAlive"/> 只）。</summary>
        internal static int RoomFor(int alive, int group)
        {
            int room = MaxAlive - Math.Max(0, alive);
            return Math.Max(0, Math.Min(group, room));
        }

        internal static float SpawnCooldown(double roll) { return SpawnCooldownMin + (float)(Clamp01(roll) * (SpawnCooldownMax - SpawnCooldownMin)); }

        internal static float SpawnDistance(double roll) { return SpawnMinDistance + (float)(Clamp01(roll) * (SpawnMaxDistance - SpawnMinDistance)); }

        /// <summary>离最近一处静水岸边的水平距离（本地坐标）；在水面上为 0。</summary>
        internal static float WaterEdgeDistance(float x, float z)
        {
            double best = double.MaxValue;
            for (int i = 0; i < waters.Length; i++)
            {
                double dx = x - waters[i].X, dz = z - waters[i].Z;
                double edge = Math.Sqrt(dx * dx + dz * dz) - waters[i].Radius;
                if (edge < best) best = edge;
            }
            return best < 0.0 ? 0f : (float)best;
        }

        /// <summary>站在这个区域的岛心（背风）：离岛中心不到短边一半的 <see cref="IslandCoreFraction"/>。桥与未知区域不算。</summary>
        internal static bool IsIslandCore(string region, float x, float z)
        {
            if (region == null) return false;
            for (int i = 0; i < islands.Length; i++)
            {
                if (!string.Equals(islands[i].Region, region, StringComparison.Ordinal)) continue;
                double dx = x - islands[i].X, dz = z - islands[i].Z;
                double radius = islands[i].HalfMin * IslandCoreFraction;
                return dx * dx + dz * dz <= radius * radius;
            }
            return false;
        }

        /// <summary>
        /// 要不要为这发弹做躲闪预测：只看主角自己的弹；有重力或会爆炸的弹（手雷、火箭）不躲——爆炸与范围伤害本来就该打得死它。
        /// </summary>
        internal static bool ShouldTrackProjectile(bool fromMainCharacter, float gravity, float explosionRange)
        {
            return fromMainCharacter && !(gravity > 0f) && !(explosionRange > 0f);
        }

        #endregion

        #region 叮咬与痒

        internal static float BiteDelay(double roll, bool veil)
        {
            float delay = BiteIntervalMin + (float)(Clamp01(roll) * (BiteIntervalMax - BiteIntervalMin));
            return veil ? delay * VeilBiteFactor : delay;
        }

        internal static float GlobalBiteGapFor(bool veil) { return veil ? GlobalBiteGap * VeilGapFactor : GlobalBiteGap; }

        internal static float OrbitRadiusFor(bool veil) { return veil ? VeilOrbitRadius : OrbitRadius; }

        /// <summary>这一只到点了、离上一口（任何一只）也够久了。</summary>
        internal static bool BiteReady(float now, float gnatReadyAt, float lastBiteAt, bool veil)
        {
            return now >= gnatReadyAt && now - lastBiteAt >= GlobalBiteGapFor(veil);
        }

        /// <summary>生命在下限之上才叮：云蚋叮不死人。</summary>
        internal static bool HealthAllowsBite(float currentHealth, float maxHealth)
        {
            return maxHealth > 0f && currentHealth > maxHealth * BiteHealthFloor && currentHealth - BiteDamage > 0f;
        }

        /// <summary>推进痒：每口 +<see cref="ItchPerBite"/>（抹了药膏的那一阵不加），随时间消退，封顶 <see cref="ItchMax"/>。</summary>
        internal static float StepItch(float itch, int bites, float seconds, bool soothed)
        {
            if (float.IsNaN(itch) || float.IsInfinity(itch)) itch = 0f;
            if (!soothed && bites > 0) itch += bites * ItchPerBite;
            if (seconds > 0f && !float.IsInfinity(seconds)) itch -= ItchDecayPerSecond * seconds;
            return itch < 0f ? 0f : itch > ItchMax ? ItchMax : itch;
        }

        /// <summary>痒的滞回：攒到 <see cref="ItchOn"/> 才上身，退到 <see cref="ItchOff"/> 以下才解除。</summary>
        internal static bool NextItchy(bool itchy, float itch)
        {
            return itchy ? itch > ItchOff : itch >= ItchOn;
        }

        #endregion

        #region 躲闪几何

        internal static float Clearance(SkyIslandGnatShot shot) { return Math.Max(0f, shot.Radius) + GnatRadius + ShotMargin; }

        /// <summary>这发弹会不会打中这个位置的蚊子（弹道线段离它不到弹半径 + 蚊子半径 + 余量）。</summary>
        internal static bool Threatens(SkyIslandGnatShot shot, SkyIslandGnatVec position)
        {
            SkyIslandGnatVec relative = position - shot.Origin;
            float along = SkyIslandGnatVec.Dot(relative, shot.Direction);
            if (along < -GnatRadius || along > shot.Range + GnatRadius) return false;
            float clearance = Clearance(shot);
            return (relative - shot.Direction * along).SqrLength < clearance * clearance;
        }

        /// <summary>这发弹最早可能打到沿弹道 <paramref name="along"/> 米处的时刻（秒）：首帧已经扫过 v·帧 + 一截。</summary>
        internal static float TimeToImpact(SkyIslandGnatShot shot, float along)
        {
            if (!(shot.Speed > 0f)) return 0f;
            float firstSweep = shot.Speed * PlanFrameSeconds + FirstSweepExtra;
            return along <= firstSweep ? 0f : (along - firstSweep) / shot.Speed;
        }

        /// <summary>冲刺开始 <paramref name="seconds"/> 秒后走了多远：先匀加速 <see cref="DashAccelSeconds"/>，再以 <see cref="DashSpeed"/> 匀速。</summary>
        internal static float DashDistanceAt(float seconds)
        {
            if (!(seconds > 0f)) return 0f;
            if (seconds <= DashAccelSeconds) return DashSpeed * seconds * seconds / (2f * DashAccelSeconds);
            return DashSpeed * DashAccelSeconds * 0.5f + DashSpeed * (seconds - DashAccelSeconds);
        }

        /// <summary><see cref="DashDistanceAt"/> 的反函数：走 <paramref name="distance"/> 米要多久。</summary>
        internal static float TravelTime(float distance)
        {
            if (!(distance > 0f)) return 0f;
            float accelDistance = DashSpeed * DashAccelSeconds * 0.5f;
            if (distance <= accelDistance) return (float)Math.Sqrt(2.0 * DashAccelSeconds * distance / DashSpeed);
            return DashAccelSeconds + (distance - accelDistance) / DashSpeed;
        }

        internal static float DashLengthFor(float need)
        {
            return need < MinDash ? MinDash : need > MaxDash ? MaxDash : need;
        }

        /// <summary>
        /// 从 <paramref name="from"/> 沿 <paramref name="way"/> 走 <paramref name="maxDistance"/> 米，哪一段在这发弹的「管子」里：
        /// 解 |w₀ + u·s|² = R²（w₀ 是起点到弹道的垂直分量，u 是走向去掉沿弹道的分量）。整段都不在管子里返回 false。
        /// </summary>
        internal static bool TubeSpan(SkyIslandGnatShot shot, SkyIslandGnatVec from, SkyIslandGnatVec way, float maxDistance,
            out float enter, out float exit)
        {
            enter = exit = 0f;
            SkyIslandGnatVec relative = from - shot.Origin;
            SkyIslandGnatVec w0 = relative - shot.Direction * SkyIslandGnatVec.Dot(relative, shot.Direction);
            SkyIslandGnatVec u = way - shot.Direction * SkyIslandGnatVec.Dot(way, shot.Direction);
            float radius = Clearance(shot);
            double a = u.SqrLength, b = 2.0 * SkyIslandGnatVec.Dot(w0, u), c = w0.SqrLength - radius * radius;
            if (a < 1e-6)
            {
                if (c >= 0.0) return false;
                exit = float.MaxValue;
                return true;
            }
            double discriminant = b * b - 4.0 * a * c;
            if (discriminant <= 0.0) return false;
            double root = Math.Sqrt(discriminant);
            double s0 = (-b - root) / (2.0 * a), s1 = (-b + root) / (2.0 * a);
            if (s1 <= 0.0 || s0 >= maxDistance) return false;
            enter = (float)Math.Max(0.0, s0);
            exit = (float)s1;
            return true;
        }

        /// <summary>往哪一侧躲：水平面上垂直于弹道（或准星线）的方向，朝自己已经偏着的那一侧；正好在线上时按个体偏好。</summary>
        internal static SkyIslandGnatVec SideOf(SkyIslandGnatVec direction, SkyIslandGnatVec offset, float bias)
        {
            SkyIslandGnatVec flat = direction.Flat;
            if (flat.SqrLength < 0.04f) flat = new SkyIslandGnatVec(1f, 0f, 0f);
            flat = flat.Normalized;
            SkyIslandGnatVec normal = new SkyIslandGnatVec(-flat.Z, 0f, flat.X);
            float lean = SkyIslandGnatVec.Dot(normal, offset);
            if (Math.Abs(lean) < 0.02f) lean = bias;
            return lean >= 0f ? normal : normal * -1f;
        }

        #endregion

        #region 蒲扇与灭蚊灯规则

        /// <summary>蒲扇这一扇对这只蚊子：0 没扇到 / 1 近处扑落 / 2 远处扇退。扇面朝 <paramref name="facing"/>（水平）。</summary>
        internal static int FanEffect(SkyIslandGnatVec player, SkyIslandGnatVec facing, SkyIslandGnatVec gnat)
        {
            SkyIslandGnatVec to = (gnat - player).Flat;
            float distance = to.Length;
            if (distance > FanKnockRange) return 0;
            SkyIslandGnatVec ahead = facing.Flat.Normalized;
            if (ahead.SqrLength < 0.5f) return 0;
            if (distance > 0.05f && SkyIslandGnatVec.Dot(to * (1f / distance), ahead) < (float)Math.Cos(FanHalfAngleDegrees * Math.PI / 180.0))
                return 0;
            return distance <= FanRange ? 1 : 2;
        }

        internal static bool ZapperReaches(SkyIslandGnatVec zapper, SkyIslandGnatVec gnat)
        {
            SkyIslandGnatVec to = gnat - zapper;
            return to.Flat.SqrLength <= ZapperRadius * ZapperRadius && Math.Abs(to.Y) <= 3f;
        }

        internal static bool ZapperLures(SkyIslandGnatVec zapper, SkyIslandGnatVec gnat)
        {
            return (gnat - zapper).Flat.SqrLength <= ZapperLureRadius * ZapperLureRadius;
        }

        #endregion

        #region 青蛙

        internal static string FrogNoteId(int index) { return FrogNotePrefix + (index + 1); }

        internal static bool IsFrogNote(string id)
        {
            for (int i = 0; i < FrogTarget; i++)
                if (string.Equals(FrogNoteId(i), id, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static int FrogsReleased(SkyIslandStoryData data)
        {
            if (data == null || data.discoveredNotes == null) return 0;
            int count = 0;
            for (int i = 0; i < FrogTarget; i++)
                if (Array.IndexOf(data.discoveredNotes, FrogNoteId(i)) >= 0) count++;
            return count;
        }

        internal static bool FrogsComplete(SkyIslandStoryData data) { return FrogsReleased(data) >= FrogTarget; }

        /// <summary>下一团蛙卵放生时写进手记的 id；放满返回 null。</summary>
        internal static string NextFrogNote(SkyIslandStoryData data)
        {
            if (data == null) return null;
            for (int i = 0; i < FrogTarget; i++)
                if (data.discoveredNotes == null || Array.IndexOf(data.discoveredNotes, FrogNoteId(i)) < 0) return FrogNoteId(i);
            return null;
        }

        #endregion

        #region 玩家看到的话

        internal static string SwarmArrives
        {
            get
            {
                return L10n.T("云蚋来了——夜里静水边最多。灶火的烟和驱风香能把它们赶开，风灯的光会招来更多；它们躲得开远处的子弹，贴近了才打得中。",
                    "Cloud gnats are out — thickest near still water at night. Hearth smoke and windward incense drive them off, and a wind lantern's light draws more in. They dodge bullets from afar; up close they are easy to hit.");
            }
        }

        internal static string LanternDraws
        {
            get
            {
                return L10n.T("风灯把云蚋招了过来——可灯下它们晃了眼，躲不开枪口。",
                    "The lantern draws the gnats in — but dazzled by its light, they cannot dodge your aim.");
            }
        }

        internal static string GaleScatters
        { get { return L10n.T("一阵大风，云蚋全被吹散了。", "A gust of wind scatters the cloud gnats."); } }

        internal static string SmokeScatters
        { get { return L10n.T("烟一飘过来，云蚋就散了。", "The smoke drifts over and the gnats scatter."); } }

        internal static string ItchStarted
        {
            get
            {
                return L10n.T("被云蚋叮得发痒：耐力恢复变慢。星苔药膏或眠苔的苔药能止痒。",
                    "The gnat bites itch: stamina recovers slower. Starmoss salve or Miantai's moss remedy stops it.");
            }
        }

        internal static string ItchEnded
        { get { return L10n.T("不痒了。", "The itching has faded."); } }

        internal static string Soothed
        {
            get
            {
                return L10n.T("星苔药膏凉丝丝的，痒退了；这一阵再被叮也不会痒。",
                    "The starmoss salve is cool: the itch is gone, and new bites will not itch for a while.");
            }
        }

        internal static string FanSwept(int downed, int pushed)
        {
            if (downed <= 0 && pushed <= 0) return L10n.T("扇了个空。", "The fan catches nothing but air.");
            return L10n.T("一扇子下去：扑落 ", "One sweep of the fan: ") + downed + L10n.T(" 只，扇退 ", " down, ") + pushed +
                L10n.T(" 只。", " driven back.");
        }

        internal static string FanResting
        { get { return L10n.T("扇子刚扇过，缓一口气。", "The fan needs a breath before the next sweep."); } }

        internal static string ZapperLit
        {
            get
            {
                return L10n.T("风晶灭蚊灯嗡嗡响起来：附近的云蚋会被引过去电落，约 5 分钟。",
                    "The windcrystal zapper starts to hum: nearby gnats are drawn in and zapped, for about 5 minutes.");
            }
        }

        internal static string ZapperOut
        { get { return L10n.T("灭蚊灯的风晶芯烧尽了。", "The zapper's windcrystal wick has burned out."); } }

        internal static string ZapperLimit
        { get { return L10n.T("已经有两盏灭蚊灯在响了。", "Two zappers are already humming."); } }

        internal static string ZapperNoGround
        { get { return L10n.T("这里放不稳灭蚊灯，换块平地。", "The zapper will not stand here — find flat ground."); } }

        /// <summary>镜水寺池边「捧一团蛙卵」的按钮：夜里才有，写着要用的云苔纤维与蛙鸣池的进度。</summary>
        internal static string SpawnChoice(bool night, int fiberInPack, int released)
        {
            if (!night)
                return L10n.T("池边的蛙卵只在夜里浮上水面（蛙鸣池 ", "Frogspawn only rises in the pool at night (Frogsong Pool ") +
                    released + "/" + FrogTarget + L10n.T("）", ")");
            return L10n.T("捧一团蛙卵，用云苔纤维包好（云苔纤维 ", "Scoop up frogspawn wrapped in cloudmoss (Cloudmoss Fiber ") +
                Math.Min(Math.Max(0, fiberInPack), 1) + L10n.T("/1 · 蛙鸣池 ", "/1 · Frogsong Pool ") + released + "/" + FrogTarget +
                L10n.T("）", ")");
        }

        internal static string SpawnNeedsNight
        { get { return L10n.T("白天池面太亮，青蛙都沉在底下。夜里再来。", "The pool is too bright by day; the frogs stay deep. Come back at night."); } }

        internal static string SpawnNeedsFiber
        { get { return L10n.T("得有一把云苔纤维才包得住蛙卵。", "You need a strand of cloudmoss fibre to wrap the spawn."); } }

        internal static string SpawnAlreadyCarried
        { get { return L10n.T("你已经捧着一团蛙卵了，先把它送回蛙鸣池。", "You are already carrying frogspawn — take it to Frogsong Pool first."); } }

        internal static string SpawnTaken
        {
            get
            {
                return L10n.T("你用云苔纤维包起一团蛙卵。这一趟里把它送回蛙鸣池——人倒下了，它也就没了。",
                    "You wrap a clutch of frogspawn in cloudmoss. Bring it to Frogsong Pool this trip — if you fall, it is lost.");
            }
        }

        internal static string ReleaseChoice(int released)
        {
            return L10n.T("把蛙卵放回蛙鸣池（第 ", "Release the frogspawn into Frogsong Pool (clutch ") + (released + 1) + "/" + FrogTarget +
                L10n.T(" 团）", ")");
        }

        internal static string Released(int released)
        {
            if (released >= FrogTarget)
                return L10n.T("三团蛙卵都放回去了。夜里蛙鸣池又有了蛙叫——那封写给池子里青蛙的信，总算有谁在替那个孩子数灯了。",
                    "All three clutches are back in the pool. Frogsong Pool croaks again at night — someone is counting the lights for that child at last.");
            return L10n.T("蛙卵沉进了蛙鸣池。等青蛙长起来，近水的云蚋会少一些。（蛙鸣池 ", "The frogspawn sinks into Frogsong Pool. Once the frogs grow, there will be fewer gnats by the water. (Frogsong Pool ") +
                released + "/" + FrogTarget + L10n.T("）", ")");
        }

        internal static string FrogsAlreadyHome
        { get { return L10n.T("蛙鸣池已经很热闹了，不用再放。", "Frogsong Pool is lively enough already."); } }

        /// <summary>手记总览里的一行：「蛙鸣池的蛙 2/3」。</summary>
        internal static string FrogProgress(SkyIslandStoryData data)
        {
            return L10n.T(" · 蛙鸣池的蛙 ", " · frogs in Frogsong Pool ") + FrogsReleased(data) + "/" + FrogTarget;
        }

        #endregion

        private static Water MakeWater(string id, float x, float z, float radius)
        {
            return new Water { Id = id, X = x, Z = z, Radius = radius };
        }

        private static Island MakeIsland(string region, float x, float z, float halfMin)
        {
            return new Island { Region = region, X = x, Z = z, HalfMin = halfMin };
        }

        private static double Clamp01(double value)
        {
            return double.IsNaN(value) ? 0.0 : value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }
    }
}
