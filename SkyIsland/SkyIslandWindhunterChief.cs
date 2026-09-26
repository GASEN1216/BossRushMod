using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：K1 / K2 / K3 回程中继平台上的三位头目「断风游猎 · 追 / 伏 / 守」（R4）。
    /// 一个控制器，按档案的 <see cref="SkyIslandBossProfile.Variant"/> 分派；整组换阵营由遭遇 owner 装配时做完，这里不碰阵营。
    ///
    /// 核心招式「断风冲步」：玩家在 [<see cref="SkyIslandBossRules.LungeMinRange"/>, <see cref="SkyIslandBossRules.LungeMaxRange"/>] 米内时，
    /// 地上画一条从它脚下到你面前的冲锋线、落点亮一圈；线亮满就瞬身冲过去落地震一下（官方爆炸，圈半径就是判定半径），然后硬直。
    /// 预警、落地与硬直期间暂停官方 AI（不移动、不开枪），结束时由 BossAIController.Resume 重新认人。
    /// - 追（K1）：落地紧跟第二步，重新瞄你**此刻**的位置、停到你身后再震一下，两步都要横着让；
    /// - 伏（K2）：冲完稍等就闪回起手点（不伤人），躲在平台边缘补枪；
    /// - 守（K3）：只冲一步、线亮得最久，是三位里最好读的一位。
    ///
    /// 被贴身就撤（<see cref="SkyIslandBossRules.ShouldDisengage"/>）：官方 AI 会一路走到脸上，进了冲步下限之后冲步再也起不来，
    /// 三位游猎会退化成普通拾荒者。连续贴身 <see cref="SkyIslandBossRules.CloseQuartersSeconds"/> 秒且冷却到了就退到
    /// <see cref="SkyIslandBossRules.DisengageRange"/> 米外（不伤人、不画预警、走冲步同一套掐 AI → 落位 → 恢复），撤一次进一次冷却。
    /// 冲步没起来的原因分开计数（<see cref="BlockedNear"/> / <see cref="BlockedFar"/> / <see cref="BlockedLanding"/>），F3 metrics 里读得到。
    ///
    /// 装备联动（只有开战时真穿着才算）：
    /// - 追的断风披甲、守的断风兜帽耐久打空（官方在出击图上自己磨头盔与身甲，不需要受击回调）→ 冲锋线亮两倍久；
    /// - 伏的断风行囊没有耐久：血线打到 <see cref="SkyIslandBossRules.StalkerPackBreakBelow"/> 以下行囊散开，不再闪回。
    ///
    /// 伤害走官方爆炸，口径同噬风；只订自己身上的 `Health.OnDeadEvent`，OnDestroy 成对退订；冲锋线与落点圈在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandWindhunterChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        /// <summary>开战后这么久才第一次冲步。</summary>
        private const float FirstLungeDelay = 3f;
        /// <summary>找不到落点、预警没画出来或被打断时，隔这么久再试。</summary>
        private const float LungeRetryDelay = 2f;
        private const float LandingClearance = 0.45f;
        private const float LandingJitter = 1.2f;
        /// <summary>冲锋线蓄满时的线宽（米）：从三成多宽慢慢变粗，和落点圈一起亮满。</summary>
        private const float LineWidth = 0.45f;
        private static readonly Color LungeTint = new Color(0.55f, 0.92f, 0.86f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private LineRenderer windLine, landingRing;
        private float nextTick, nextLungeAt;
        private int variant, lungeCount;
        /// <summary>冲步没起来的次数，按原因分开记（只在 <see cref="TryStartLunge"/> 里自增，给 F3 与演练读）。</summary>
        private int blockedNear, blockedFar, blockedLanding;
        /// <summary>拉开距离的次数与其中吸不到落脚点的次数。</summary>
        private int disengages, blockedDisengage;
        /// <summary>上一次判定时与主角的水平距离（米）：冲步不起来时要分清是贴太近、离太远还是落点找不到。</summary>
        private float lastRange = -1f;
        /// <summary>连续贴身的秒数与「正在拉开距离」：见 <see cref="TickCloseQuarters"/> 与 <see cref="DisengageRoutine"/>。</summary>
        private float closeSeconds;
        private bool disengaging, disengageAnnounced;
        private bool subscribed, lunging, stepLanded, resumePending, finished, firstLungeAnnounced;
        private bool mantleEquipped, packEquipped, hoodEquipped, pieceBrokenAnnounced, packBurstAnnounced;

        /// <summary>只读，给 F3 与演练：是否正在冲步、完整冲完几轮、第几位（1 追 / 2 伏 / 3 守）、它那件断风装备是否还起作用（伏：行囊没散）。</summary>
        internal bool Lunging { get { return lunging; } }
        internal int LungeCount { get { return lungeCount; } }
        internal int Variant { get { return variant; } }
        /// <summary>
        /// 只读诊断，给 F3 与演练：冲步没起来时按原因分开的计数与上一次量到的水平距离。
        /// 冲步是它的招牌动作，实机等不到线时要一眼看出是贴太近（官方 AI 走进 <see cref="SkyIslandBossRules.LungeMinRange"/> 以内）、
        /// 离太远，还是落点吸不到地（2026-09-16 第八轮 F3：守在 7.5 m 处 17 秒一次没冲，日志里没有任何告警）。
        /// </summary>
        internal int BlockedNear { get { return blockedNear; } }
        internal int BlockedFar { get { return blockedFar; } }
        internal int BlockedLanding { get { return blockedLanding; } }
        internal int Disengages { get { return disengages; } }
        internal int BlockedDisengage { get { return blockedDisengage; } }
        internal float LastRange { get { return lastRange; } }
        internal bool PieceWorking
        {
            get
            {
                switch (variant)
                {
                    case SkyIslandBossRules.WindhunterChaser: return mantleEquipped && !MantleBroken();
                    case SkyIslandBossRules.WindhunterStalker: return packEquipped && !PackBurst();
                    case SkyIslandBossRules.WindhunterWarden: return hoodEquipped && !HoodBroken();
                    default: return false;
                }
            }
        }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("断风游猎缺少生命组件");
            variant = value.Variant;
            // 配装在控制器之前做完：只有开战时真穿着，装备联动才算数。三件分属三位，每位只会穿上自己那一件，其余两个自然为 false。
            mantleEquipped = !SkyIslandBossForge.PieceBroken(character.GetArmorItem(), BossRushItemIds.SkyIslandWindbreakMantle);
            packEquipped = !SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(character, "Backpack"), BossRushItemIds.SkyIslandWindbreakPack);
            hoodEquipped = !SkyIslandBossForge.PieceBroken(character.GetHelmatItem(), BossRushItemIds.SkyIslandWindbreakHood);
            try { aiControl = new BossAIController(character, "SkyIsland" + value.Id); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 断风游猎 AI 暂停控制不可用，冲步预警与硬直时不停手：" + e.Message);
            }
            nextLungeAt = Time.time + FirstLungeDelay;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void Update()
        {
            if (finished || boss == null || health == null) return;
            if (Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (context.Valid != null && !context.Valid()) return;
            if (health.IsDead) return;
            TickGear();
            if (resumePending)
            {
                // 冲步中途角色被停用过（见 OnDisable）：重新启用后补一次恢复。
                resumePending = false;
                ResumeAi();
                nextLungeAt = Time.time + LungeRetryDelay;
            }
            if (disengaging) return;
            // 冲步中与冷却里都不计贴身：只数「本可以冲、却因为它贴在你脸上而冲不了」的时间。
            // 不这样的话每一次冲步落地之后都在你面前 2.5 m，冷却一到就必定先撤一次，撤与冲会一人一半。
            if (lunging || Time.time < nextLungeAt) { closeSeconds = 0f; return; }
            TickCloseQuarters();
            if (SkyIslandBossRules.ShouldDisengage(lunging, closeSeconds, true)) { StartCoroutine(DisengageRoutine()); return; }
            TryStartLunge();
        }

        /// <summary>连续贴身的秒数：进了冲步下限就累加，离开就清零。按 <see cref="TickInterval"/> 的节拍加，不用每帧。</summary>
        private void TickCloseQuarters()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) { closeSeconds = 0f; return; }
            Vector3 offset = player.transform.position - boss.transform.position;
            offset.y = 0f;
            float min = SkyIslandBossRules.LungeMinRange;
            closeSeconds = offset.sqrMagnitude < min * min ? closeSeconds + TickInterval : 0f;
        }

        /// <summary>
        /// 拉开距离（不伤人、不画预警）：口径照伏的闪回——掐官方 AI、吸一个背向玩家的落脚点、落位、立刻恢复 AI 补枪。
        /// 吸不到地就原地不动，下一拍再试；撤一次就进冷却，玩家追上来也不会变成每两秒瞬移一次。
        /// </summary>
        private IEnumerator DisengageRoutine()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) { closeSeconds = 0f; yield break; }
            Vector3 away = boss.transform.position - player.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = boss.transform.forward;
            Vector3 wanted = player.transform.position + away.normalized * SkyIslandBossRules.DisengageRange;
            wanted.y = boss.transform.position.y;
            Vector3 spot;
            closeSeconds = 0f;
            disengages++;
            if (!SkyIslandBossProps.SnapNear(wanted, context, LandingClearance, LandingJitter, out spot))
            {
                blockedDisengage++;
                nextLungeAt = Time.time + LungeRetryDelay;
                yield break;
            }
            disengaging = true;
            try { if (aiControl != null) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风游猎拉开距离停手失败：" + e.Message); }
            yield return null;
            bool moved = false;
            Vector3 from = boss.transform.position;
            try { moved = SkyIslandBossProps.Teleport(boss, spot + Vector3.up * 0.1f, aiControl); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风游猎拉开距离落位失败：" + e.Message); }
            // 退开也是一次瞬身：留一道风痕和两团扬尘，看得出它往哪退了（VB-25，不伤人）。
            if (moved) Blink(from, spot);
            disengaging = false;
            ResumeAi();
            // 落位失败（官方寻路没接住）时不进长冷却、也不报字幕：这一次等于没撤。
            nextLungeAt = Time.time + (moved ? SkyIslandBossRules.LungeCooldown : LungeRetryDelay);
            if (!moved) { blockedDisengage++; yield break; }
            if (!disengageAnnounced)
            {
                disengageAnnounced = true;
                Announce(string.Format(L10n.T("{0}不跟你贴身：它退开重新拉线。",
                    "{0} refuses to brawl: it backs off and lines up again."), SkyIslandBossRules.Name(profile)), false);
            }
        }

        // ====================================================================
        // 断风冲步
        // ====================================================================

        private void TryStartLunge()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 offset = player.transform.position - boss.transform.position;
            offset.y = 0f;
            float sqr = offset.sqrMagnitude;
            lastRange = Mathf.Sqrt(sqr);
            float min = SkyIslandBossRules.LungeMinRange, max = SkyIslandBossRules.LungeMaxRange;
            if (sqr < min * min) { blockedNear++; return; }
            if (sqr > max * max) { blockedFar++; return; }
            // 落点吸不到地就不冲（LungeRoutine 会自己退出并进重试冷却）：这里先记一笔，免得三种「没冲」在报告里分不开。
            Vector3 probe;
            if (!TryLanding(player, false, out probe)) { blockedLanding++; nextLungeAt = Time.time + LungeRetryDelay; return; }
            StartCoroutine(LungeRoutine(player));
        }

        private IEnumerator LungeRoutine(CharacterMainControl player)
        {
            lunging = true;
            Vector3 origin = boss.transform.position;
            Vector3 landing;
            if (!TryLanding(player, false, out landing))
            {
                lunging = false;
                nextLungeAt = Time.time + LungeRetryDelay;
                yield break;
            }
            if (!firstLungeAnnounced)
            {
                firstLungeAnnounced = true;
                Announce(string.Format(L10n.T("{0}在地上画了一道冲锋线：线亮起来就横着让开。",
                    "{0} draws a lunge line on the ground: when it lights up, sidestep it."), SkyIslandBossRules.Name(profile)), true);
            }
            yield return StartCoroutine(StepRoutine(landing));
            if (Aborted() || !stepLanded)
            {
                EndLunge(false);
                yield break;
            }

            // 追：落地紧跟第二步，从新位置重新瞄你此刻的位置，停到你身后。
            if (variant == SkyIslandBossRules.WindhunterChaser && player != null && player.Health != null && !player.Health.IsDead &&
                TryLanding(player, true, out landing))
            {
                yield return StartCoroutine(StepRoutine(landing));
                if (Aborted())
                {
                    EndLunge(false);
                    yield break;
                }
            }

            // 伏：行囊还在就稍等一下闪回起手点（不伤人）；落不了地就原地不动。
            if (variant == SkyIslandBossRules.WindhunterStalker && packEquipped && !PackBurst())
            {
                float waitStarted = Time.time;
                while (Time.time - waitStarted < SkyIslandBossRules.StalkerRetreatDelay && !Aborted()) yield return null;
                if (Aborted())
                {
                    EndLunge(false);
                    yield break;
                }
                Vector3 originGround;
                Vector3 blinkFrom = boss.transform.position;
                if (SkyIslandBossProps.SnapNear(origin, context, LandingClearance, LandingJitter, out originGround) &&
                    SkyIslandBossProps.Teleport(boss, originGround + Vector3.up * 0.1f, aiControl))
                {
                    Blink(blinkFrom, originGround);
                    // 闪回边缘就立刻恢复官方 AI 补枪：它的反打窗口是冲到你面前那一下（五栏「等它下一次冲过来」），不是躲回去之后。
                    EndLunge(true);
                    yield break;
                }
                // 落不回起手点：原地照常硬直。
            }

            // 硬直：官方 AI 仍停着，这是反打窗口。
            float staggerStarted = Time.time;
            while (Time.time - staggerStarted < SkyIslandBossRules.LungeStagger && !Aborted()) yield return null;
            EndLunge(!Aborted());
        }

        /// <summary>冲一步：暂停官方 AI → 画冲锋线与落点圈并蓄力 → 瞬身落地震一下 → 收掉线与圈。落没落地写进 stepLanded。</summary>
        private IEnumerator StepRoutine(Vector3 landing)
        {
            stepLanded = false;
            try { if (aiControl != null) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风游猎冲步停手失败：" + e.Message); }
            bool drawn = false;
            try
            {
                windLine = SkyIslandBossForge.StraightLine(context.Root, "SkyIslandWindLine", LineWidth, LungeTint);
                landingRing = SkyIslandBossForge.CreateGroundRing(context.Root, landing);
                DrawTelegraph(landing, 0f);
                drawn = true;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风冲锋线预警失败：" + e.Message); }
            // 没画出预警就不冲（不许无预警落地震人）；由调用方按「没落地」收尾并恢复 AI。
            if (!drawn)
            {
                DestroyVisuals();
                yield break;
            }

            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：预警只会更长，逃圈判据仍按不戴的算。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(
                SkyIslandBossRules.LungeTelegraphFor(variant, TelegraphPieceBroken()), SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted())
            {
                DrawTelegraph(landing, Mathf.Clamp01((Time.time - started) / telegraph));
                yield return null;
            }
            if (!Aborted())
            {
                // catch 子句体内不能 yield return（CS1631）：这里只落地与记账。
                try
                {
                    Vector3 from = boss.transform.position;
                    if (SkyIslandBossProps.Teleport(boss, landing + Vector3.up * 0.1f, aiControl))
                    {
                        // 冲步是一段冲锋，不是爆炸：起点扬尘 + 两点之间一道风痕 + 落点余波，不冒官方火球（VB-21 / VB-25）。
                        SkyIslandImpactFx.Puff(context.Root, from, 0.5f, 8);
                        SkyIslandImpactFx.Streak(context.Root, from, landing, LungeTint);
                        SkyIslandBossForge.Detonate(boss, landing, SkyIslandBossRules.LungeRadius, SkyIslandBossRules.LungeDamage,
                            false, SkyIslandImpactFx.BossShake, LungeTint);
                        stepLanded = true;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风冲步落地失败：" + e.Message); }
            }
            DestroyVisuals();
        }

        /// <summary>冲锋线跟着 Boss 脚下，线宽与不透明度随蓄力变；落点圈只变宽度与不透明度，半径恒为判定半径。</summary>
        private void DrawTelegraph(Vector3 landing, float charge)
        {
            if (windLine != null && boss != null)
            {
                Vector3 lift = Vector3.up * SkyIslandGroundRing.GroundLift;
                windLine.SetPosition(0, boss.transform.position + lift);
                windLine.SetPosition(1, landing + lift);
                windLine.widthMultiplier = Mathf.Lerp(LineWidth * 0.35f, LineWidth, charge);
                Color color = new Color(LungeTint.r, LungeTint.g, LungeTint.b, Mathf.Lerp(0.35f, 1f, charge));
                windLine.startColor = color;
                windLine.endColor = color;
            }
            SkyIslandBossForge.SetRing(landingRing, SkyIslandBossRules.LungeRadius, charge, LungeTint);
        }

        private bool TryLanding(CharacterMainControl player, bool behind, out Vector3 landing)
        {
            landing = Vector3.zero;
            if (player == null || boss == null) return false;
            Vector3 from = boss.transform.position, to = player.transform.position;
            float x, z;
            SkyIslandBossRules.LungeLanding(from.x, from.z, to.x, to.z, behind, out x, out z);
            return SkyIslandBossProps.SnapNear(new Vector3(x, from.y, z), context, LandingClearance, LandingJitter, out landing);
        }

        /// <summary>收尾一轮冲步：收掉线与圈；人还活着就恢复官方 AI（倒下与销毁不恢复）。完整冲完才计数、进冷却。</summary>
        private void EndLunge(bool completed)
        {
            DestroyVisuals();
            lunging = false;
            if (finished || boss == null || health == null || health.IsDead) return;
            ResumeAi();
            if (completed) lungeCount++;
            nextLungeAt = Time.time + (completed ? SkyIslandBossRules.LungeCooldown : LungeRetryDelay);
        }

        private void ResumeAi()
        {
            try { if (aiControl != null && aiControl.IsPaused) aiControl.Resume(CharacterMainControl.Main); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 断风游猎恢复失败：" + e.Message); }
        }

        /// <summary>不伤人的瞬身（拉开距离、伏的闪回）：起落两团扬尘 + 一道风痕。纯表现，失败不影响动作。</summary>
        private void Blink(Vector3 from, Vector3 to)
        {
            if (context == null) return;
            SkyIslandImpactFx.Puff(context.Root, from, 0.5f, 6);
            SkyIslandImpactFx.Streak(context.Root, from, to, LungeTint);
            SkyIslandImpactFx.Puff(context.Root, to, 0.5f, 6);
        }

        private void DestroyVisuals()
        {
            if (windLine != null) Destroy(windLine.gameObject);
            SkyIslandBossForge.ReleaseRing(landingRing);
            windLine = null;
            landingRing = null;
        }

        // ====================================================================
        // 装备联动
        // ====================================================================

        private bool MantleBroken()
        {
            return boss == null || SkyIslandBossForge.PieceBroken(boss.GetArmorItem(), BossRushItemIds.SkyIslandWindbreakMantle);
        }

        private bool HoodBroken()
        {
            return boss == null || SkyIslandBossForge.PieceBroken(boss.GetHelmatItem(), BossRushItemIds.SkyIslandWindbreakHood);
        }

        /// <summary>伏的行囊散没散：背包没有耐久，按血线算（低于 <see cref="SkyIslandBossRules.StalkerPackBreakBelow"/> 就散）。</summary>
        private bool PackBurst()
        {
            if (health == null) return true;
            float max = health.MaxHealth;
            return max > 0f && health.CurrentHealth / max < SkyIslandBossRules.StalkerPackBreakBelow;
        }

        /// <summary>冲锋线亮两倍久的条件：追看披甲、守看兜帽，都要开战时真穿着；伏恒为 false（口径见 LungeTelegraphFor）。</summary>
        private bool TelegraphPieceBroken()
        {
            if (variant == SkyIslandBossRules.WindhunterChaser) return mantleEquipped && MantleBroken();
            if (variant == SkyIslandBossRules.WindhunterWarden) return hoodEquipped && HoodBroken();
            return false;
        }

        private void TickGear()
        {
            if (!pieceBrokenAnnounced && TelegraphPieceBroken())
            {
                pieceBrokenAnnounced = true;
                Announce("断风装备被打穿了：它冲锋前的线要亮得更久。",
                    "Its Galebreaker gear is shot through: the lunge line now stays lit longer before it charges.", false);
            }
            if (variant == SkyIslandBossRules.WindhunterStalker && packEquipped && !packBurstAnnounced && PackBurst())
            {
                packBurstAnnounced = true;
                Announce("断风行囊散开了：伏再也闪不回平台边缘。",
                    "The Galebreaker pack bursts open: the Stalker can no longer blink back to the platform edge.", false);
            }
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || context == null ||
                (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        /// <summary>带名字的字幕：调用方已用 string.Format(L10n.T(...), 名字) 按当前语言拼好。</summary>
        private void Announce(string text, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(text, urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            DestroyVisuals();
            lunging = false;
            disengaging = false;
            Announce(string.Format(L10n.T("{0}倒下了，桥上的风一下子松了。", "{0} falls and the wind over the bridge eases."),
                SkyIslandBossRules.Name(profile)), false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void OnDisable()
        {
            // 角色被停用时协程随之停止：收掉线与圈、清掉进行中的标记（否则再也不冲），重新启用后在节流里补一次恢复 AI。
            // 销毁路径也先走这里，但销毁之后不会再有 Update，所以不会恢复。
            if (finished || (!lunging && !disengaging)) return;
            DestroyVisuals();
            lunging = false;
            disengaging = false;
            resumePending = true;
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            DestroyVisuals();
            lunging = false;
            disengaging = false;
            aiControl = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
