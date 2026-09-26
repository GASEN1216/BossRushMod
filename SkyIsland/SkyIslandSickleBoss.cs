using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：C 青穗梯田的岛主「穗镰」（头目 R3）。
    ///
    /// 核心招式两件事，都在水车渠与谷仓之间的田埂上打：
    /// 1. **开闸放水**（血线跨过 <see cref="SkyIslandBossRules.SicklePhaseThresholds"/> 每档一次）：在玩家脚下与左右两侧各冲出一块烂泥。
    ///    泥从圈里漫开之前踩进去不减速，漫开之后留一段时间；站在泥里走跑都变慢（<see cref="SkyIslandPlayerSlow"/>，
    ///    每拍续一次短减速，出了泥很快摘掉）。第一次开闸还朝谷仓吆喝一声，把 <see cref="SkyIslandBossRules.SickleHelpers"/>
    ///    组还活着的帮手喊过来（遭遇 owner 的 CallGroup，不另开生成路径）。
    /// 2. **镰扫**：玩家贴到身边时脚下亮圈，圈跟着它走，预警结束在它脚下扫一圈。
    ///
    /// 克制：别站在泥里打；先清掉谷仓那一组，它就喊不来人；保持距离，镰扫亮圈就退。
    ///
    /// 装备联动（每 0.2 秒节流读一次耐久，不进每帧热路径；两件都在官方会磨耐久的头盔 / 护甲槽，不用订受击）：
    /// - 青穗斗笠耐久打空（被爆头打穿）→ 开闸只冲出一块泥；
    /// - 蓑衣甲耐久打空 → 镰扫的预警慢下来。
    ///
    /// 伤害走官方爆炸（口径同噬风）。事件订阅只有自己身上的 `Health.OnDeadEvent`，OnDestroy 成对退订；
    /// 泥圈、镰扫圈与玩家身上的减速在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandSickleBoss : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        /// <summary>每拍重新挂一次的减速时长：比节拍略长，站在泥里一直续上；出了泥最多再慢这么久。</summary>
        private const float MudSlowRefresh = 0.35f;
        /// <summary>梯田一层层有落差：和泥块中心的高差超过这么多（米）就不算踩进这块泥（站在上一层田埂上不该被下一层的泥拖慢）。</summary>
        private const float MudHeightTolerance = 1.5f;
        private const string MudPatchName = "SkyIslandMudPatch";
        private const string SweepRingName = "SkyIslandSickleSweep";
        private static readonly Color MudTint = new Color(0.46f, 0.34f, 0.20f, 1f);
        /// <summary>漫开之后的泥面：暗、半透明，压在暖琥珀的田埂上读作「湿泥」而不是一块色贴纸。</summary>
        private static readonly Color MudSurface = new Color(0.22f, 0.16f, 0.10f, 0.45f);
        /// <summary>踩进泥里溅起的泥点颜色。</summary>
        private static readonly Color MudSplashColor = new Color(0.30f, 0.22f, 0.13f, 0.9f);
        /// <summary>站在泥里时每隔几拍再溅一次（节拍 0.2 s，3 拍 ≈ 0.6 s），刚踩进去那一拍必溅。</summary>
        private const int MudSplashEveryTicks = 3;
        private static readonly Color SweepTint = new Color(0.90f, 0.78f, 0.35f, 1f);

        /// <summary>一块泥：圈中心、开始漫开与漫开完成的时刻、消失时刻。值类型，节流推进与逐帧蓄力里不产生垃圾。</summary>
        private struct MudPatch
        {
            internal Vector3 Center;
            internal float StartAt, ActiveAt, ExpiresAt;
            internal LineRenderer Ring;
            internal bool Steady;
        }

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private SkyIslandPlayerSlow slow;
        private readonly List<MudPatch> mudPatches = new List<MudPatch>();
        private LineRenderer sweepRing;
        private int phase, helpersCalled, mudTicks;
        private float nextTick, sweepReadyAt;
        private bool subscribed, finished, sweeping, mudCharging, sluiceAnnounced, sweepAnnounced;
        private bool hatEquipped, raincoatEquipped, hatBrokenAnnounced, raincoatBrokenAnnounced;

        /// <summary>只读，给 F3 与演练：当前相位、场上的泥块（含还在漫开的）、是否正在镰扫、谷仓喊来几个帮手。</summary>
        internal int Phase { get { return phase; } }
        internal int LiveMudPatches { get { return mudPatches.Count; } }
        internal bool Sweeping { get { return sweeping; } }
        internal int HelpersCalled { get { return helpersCalled; } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("穗镰缺少生命组件");
            // 配装在控制器之前做完：只有真的穿上了，「被打穿」才有意义，也才会播那句字幕。
            hatEquipped = !SkyIslandBossForge.PieceBroken(character.GetHelmatItem(), BossRushItemIds.SkyIslandGreenearStrawHat);
            raincoatEquipped = !SkyIslandBossForge.PieceBroken(character.GetArmorItem(), BossRushItemIds.SkyIslandStrawRaincoat);
            slow = new SkyIslandPlayerSlow("SkyIslandSickleMud");
            sweepReadyAt = Time.time + SkyIslandBossRules.SweepCooldown * 0.5f;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void Update()
        {
            if (finished || boss == null || health == null) return;
            if (mudCharging) AnimateMud();
            if (Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (Aborted())
            {
                // 会话收尾时节流推进不再往下跑：减速别挂在主角身上等 OnDestroy。
                if (slow != null) slow.Release();
                return;
            }
            TickBrokenGear();
            CharacterMainControl player = CharacterMainControl.Main;
            bool playerAlive = player != null && player.Health != null && !player.Health.IsDead;
            TickMud(playerAlive ? player : null);
            float max = health.MaxHealth;
            if (max > 0f)
            {
                int target = SkyIslandBossRules.PhaseFor(Mathf.Clamp01(health.CurrentHealth / max), SkyIslandBossRules.SicklePhaseThresholds);
                if (target > phase)
                {
                    // 一拍里跨过几档也只开一次闸（口径同残星匠首的供能桩）。
                    phase = target;
                    if (playerAlive) OpenSluice(player);
                }
            }
            if (sweeping || !playerAlive || Time.time < sweepReadyAt) return;
            float range = SkyIslandBossRules.SweepRange;
            if ((player.transform.position - boss.transform.position).sqrMagnitude > range * range) return;
            StartCoroutine(SweepRoutine());
        }

        // ====================================================================
        // 开闸放水
        // ====================================================================

        private void OpenSluice(CharacterMainControl player)
        {
            // 青穗斗笠被打穿之后没了准头：只冲出中间那一块。
            int count = hatEquipped && HatBroken() ? SkyIslandBossRules.MudPatchesHatBroken : SkyIslandBossRules.MudPatches;
            Vector3 target = player.transform.position;
            Vector3 flat = new Vector3(target.x - boss.transform.position.x, 0f, target.z - boss.transform.position.z);
            Vector3 side = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：漫开只会更慢，逃圈判据仍按不戴的算。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.MudTelegraph, SkyIslandBossGearWorn.Earmuffs);
            float now = Time.time;
            // 中间那块压在脚下，两侧的沿着和它连线的垂直方向铺开：往前或往后走出泥。
            AddMudPatch(target, now, telegraph);
            if (count > 1) AddMudPatch(target + side * SkyIslandBossRules.MudSpread, now, telegraph);
            if (count > 2) AddMudPatch(target - side * SkyIslandBossRules.MudSpread, now, telegraph);
            if (sluiceAnnounced) return;
            sluiceAnnounced = true;
            Announce("穗镰开了水渠的闸：泥地里跑不快，别站在泥里打。",
                "Grain Sickle opens the sluice. You can't run in the mud, so don't fight standing in it.", true);
            CallHelpers();
        }

        private void AddMudPatch(Vector3 at, float now, float telegraph)
        {
            Vector3 ground;
            Vector3 center = SkyIslandBossForge.SnapToGround(at, context, 0f, out ground) ? ground : at;
            LineRenderer ring = null;
            try
            {
                ring = SkyIslandBossForge.CreateGroundRing(context.Root, center);
                ring.gameObject.name = MudPatchName;
                SkyIslandBossForge.SetRing(ring, SkyIslandBossRules.MudRadius, 0f, MudTint);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 穗镰泥圈失败：" + e.Message);
                if (ring != null) Destroy(ring.gameObject);
                ring = null;
            }
            // 没画出圈就不铺泥：看不见的减速比没有减速更糟。
            if (ring == null) return;
            MudPatch patch = new MudPatch();
            patch.Center = center;
            patch.StartAt = now;
            patch.ActiveAt = now + telegraph;
            patch.ExpiresAt = patch.ActiveAt + SkyIslandBossRules.MudSeconds;
            patch.Ring = ring;
            mudPatches.Add(patch);
            mudCharging = true;
        }

        /// <summary>逐帧：还在漫开的泥圈越来越亮，漫开那一刻落到常亮（只在有圈在漫开时跑）。</summary>
        private void AnimateMud()
        {
            float now = Time.time;
            bool stillCharging = false;
            for (int i = 0; i < mudPatches.Count; i++)
            {
                MudPatch patch = mudPatches[i];
                if (patch.Steady) continue;
                if (now < patch.ActiveAt)
                {
                    float span = Mathf.Max(0.01f, patch.ActiveAt - patch.StartAt);
                    SkyIslandBossForge.SetRing(patch.Ring, SkyIslandBossRules.MudRadius, Mathf.Clamp01((now - patch.StartAt) / span), MudTint);
                    stillCharging = true;
                    continue;
                }
                // 漫开之后是一块看得见的泥：圈内铺满暗泥色，圈压到 0.6（VB-22；此前只剩一圈棕色细线，泥本身看不见）。
                SkyIslandBossForge.SetPatch(patch.Ring, SkyIslandBossRules.MudRadius, MudTint, MudSurface);
                patch.Steady = true;
                mudPatches[i] = patch;
            }
            mudCharging = stillCharging;
        }

        /// <summary>节流：收掉到时的泥块；主角站进任一块已经漫开的泥就续一拍减速，出了泥由 Tick 按时摘。</summary>
        private void TickMud(CharacterMainControl player)
        {
            float now = Time.time;
            float reach = SkyIslandBossRules.MudRadius * SkyIslandBossRules.MudRadius;
            Vector3 feet = player != null ? player.transform.position : Vector3.zero;
            bool inMud = false;
            for (int i = mudPatches.Count - 1; i >= 0; i--)
            {
                MudPatch patch = mudPatches[i];
                if (now >= patch.ExpiresAt || patch.Ring == null)
                {
                    // 泥干了：淡出，不一帧消失。
                    SkyIslandBossForge.ReleaseRing(patch.Ring);
                    mudPatches.RemoveAt(i);
                    continue;
                }
                if (player == null || inMud || now < patch.ActiveAt) continue;
                float dx = feet.x - patch.Center.x, dz = feet.z - patch.Center.z;
                if (dx * dx + dz * dz <= reach && Mathf.Abs(feet.y - patch.Center.y) <= MudHeightTolerance) inMud = true;
            }
            if (inMud) slow.Apply(player, SkyIslandBossRules.MudSlow, MudSlowRefresh);
            slow.Tick(now);
            // 踩进泥里脚下溅几粒泥点（VB-22）：刚踩进去那一拍一定溅，之后每 3 拍再溅一次；按节拍走，不进每帧路径。
            if (inMud)
            {
                if (mudTicks % MudSplashEveryTicks == 0) SkyIslandImpactFx.Splash(context.Root, feet, MudSplashColor, 4);
                mudTicks++;
            }
            else mudTicks = 0;
        }

        /// <summary>谷仓叫人（只在第一次开闸时）：遭遇 owner 把那一组还活着的人拉到谷仓门口并盯上主角。</summary>
        private void CallHelpers()
        {
            if (context.CallGroup == null) return;
            try
            {
                int called = context.CallGroup(SkyIslandBossRules.SickleHelpers,
                    new Vector3(SkyIslandBossRules.BarnDoorX, SkyIslandBossRules.BarnDoorY, SkyIslandBossRules.BarnDoorZ));
                helpersCalled = Mathf.Max(0, called);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 穗镰谷仓叫人失败：" + e.Message);
                return;
            }
            if (helpersCalled > 0)
                Announce("穗镰朝谷仓吆喝了一声，那边的帮手跑了过来。",
                    "Grain Sickle hollers toward the barn, and the helpers over there come running.", false);
            else
                Announce("穗镰朝谷仓吆喝了一声，没人应，谷仓那边已经清干净了。",
                    "Grain Sickle hollers toward the barn. Nobody answers; the barn has already been cleared.", false);
        }

        // ====================================================================
        // 镰扫
        // ====================================================================

        private IEnumerator SweepRoutine()
        {
            sweeping = true;
            try
            {
                sweepRing = SkyIslandBossForge.CreateGroundRing(context.Root, boss.transform.position);
                sweepRing.gameObject.name = SweepRingName;
                SkyIslandBossForge.SetRing(sweepRing, SkyIslandBossRules.SweepRadius, 0f, SweepTint);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 穗镰镰扫预警失败：" + e.Message);
                DestroySweepRing();
            }
            if (sweepRing == null)
            {
                // 没有预警就不出手（口径同瞭台观星手的标记圈）。
                sweeping = false;
                sweepReadyAt = Time.time + SkyIslandBossRules.SweepCooldown;
                yield break;
            }
            if (!sweepAnnounced)
            {
                sweepAnnounced = true;
                Announce("穗镰抡圆了镰刀：脚下亮圈就退开三步。",
                    "Grain Sickle winds up a full swing. When the ring lights up at its feet, back off three steps.", true);
            }

            // 蓑衣甲被打空之后镰扫慢下来；戴着静听耳罩的玩家再早一点听见（只会更长，逃圈判据仍按不戴的算）。
            float baseSeconds = raincoatEquipped && RaincoatBroken() ? SkyIslandBossRules.SweepTelegraphBroken : SkyIslandBossRules.SweepTelegraph;
            float telegraph = SkyIslandBossRules.TelegraphSeconds(baseSeconds, SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted())
            {
                // 圈跟着它走：它还在追人，只有退开才躲得掉。
                SkyIslandBossForge.PlaceRing(sweepRing, context.Root, boss.transform.position);
                SkyIslandBossForge.SetRing(sweepRing, SkyIslandBossRules.SweepRadius, Mathf.Clamp01((Time.time - started) / telegraph), SweepTint);
                yield return null;
            }
            if (!Aborted())
            {
                // catch 子句体内不能 yield return（CS1631）：这里只记账。
                // 镰扫不是爆炸：不冒官方火球，只留余波圈与扬尘（VB-21）。
                try
                {
                    SkyIslandBossForge.Detonate(boss, boss.transform.position, SkyIslandBossRules.SweepRadius, SkyIslandBossRules.SweepDamage,
                        false, SkyIslandImpactFx.BossShake, SweepTint);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 穗镰镰扫失败：" + e.Message); }
            }
            DestroySweepRing();
            sweeping = false;
            sweepReadyAt = Time.time + SkyIslandBossRules.SweepCooldown;
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private bool HatBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetHelmatItem(), BossRushItemIds.SkyIslandGreenearStrawHat);
        }

        private bool RaincoatBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetArmorItem(), BossRushItemIds.SkyIslandStrawRaincoat);
        }

        private void TickBrokenGear()
        {
            if (hatEquipped && !hatBrokenAnnounced && HatBroken())
            {
                hatBrokenAnnounced = true;
                Announce("青穗斗笠被打穿了：穗镰再开闸只冲得出一块泥。",
                    "The greenear straw hat is shot through. Grain Sickle's sluice now floods only one patch of mud.", false);
            }
            if (raincoatEquipped && !raincoatBrokenAnnounced && RaincoatBroken())
            {
                raincoatBrokenAnnounced = true;
                Announce("蓑衣甲被打烂了：穗镰的镰扫慢了下来，亮圈之后有更多时间退开。",
                    "The straw raincoat is torn apart. Grain Sickle's sweep winds up slower, giving you more time to back off.", false);
            }
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        private void DestroySweepRing()
        {
            SkyIslandBossForge.ReleaseRing(sweepRing);
            sweepRing = null;
        }

        private void Cleanup()
        {
            if (slow != null) slow.Release();
            for (int i = 0; i < mudPatches.Count; i++)
                if (mudPatches[i].Ring != null) Destroy(mudPatches[i].Ring.gameObject);
            mudPatches.Clear();
            mudCharging = false;
            DestroySweepRing();
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            Cleanup();
            Announce("穗镰倒下了，水渠的闸慢慢落了回去。", "Grain Sickle falls, and the sluice gate slowly settles shut.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            Cleanup();
            slow = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
