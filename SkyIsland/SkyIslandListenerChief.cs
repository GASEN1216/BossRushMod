using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：S3 听雨洞的头目「听雨人」（头目 R3）。
    ///
    /// 核心招式：**听枪引石**。数玩家在洞口一带（<see cref="SkyIslandBossRules.InCave"/>）开的枪，每数满一轮
    /// （<see cref="SkyIslandBossRules.ShotsPerRockfallFor"/>）就循着枪声让洞顶在玩家身边落石：第一块落在脚下、其余的在身边偏开一点，
    /// 圈一起亮完才砸。数枪订官方静态事件 `ItemAgent_Gun.OnMainCharacterShootEvent`（主角每开一枪发一次，不是每颗弹丸），
    /// 回调里只判一次位置、自增，不分配、不记日志。
    ///
    /// 克制：在洞口少开枪，换近战或蒲扇；把它引出洞区再打，计数就不涨；看见落石圈就挪开。
    /// 玩家自己戴着静听耳罩，枪声压得低，要开两倍的枪才落一次石（所有头目的预警也亮得更久）。
    ///
    /// 装备联动：它戴的静听耳罩被暴击打穿，就再也听不见枪声、引不下石头。耳机槽官方不磨耐久，
    /// 由本控制器在自己的 `Health.OnHurtEvent` 里照官方磨头盔的口径扣（<see cref="SkyIslandBossProps.WearSoftPiece"/>），
    /// 每 0.2 秒节流读一次耐久。
    ///
    /// 伤害走官方爆炸（口径同噬风）。事件订阅各有私有布尔：静态开枪事件在耳罩打穿、倒下与销毁三处退订，
    /// 自己身上的死亡与受击事件在 OnDestroy 成对退订；落石圈在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandListenerChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        private const string RockfallRingName = "SkyIslandRockfallRing";
        private static readonly Color RockTint = new Color(0.62f, 0.64f, 0.70f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        /// <summary>给 <see cref="SkyIslandBossProps.ChargeRings"/> 的中止判断，Bind 时缓存一次，落石时不再分配委托。</summary>
        private Func<bool> aborted;
        private readonly List<Vector3> rockPoints = new List<Vector3>();
        private readonly List<LineRenderer> rockRings = new List<LineRenderer>();
        private int shots;
        private float nextTick, rockReadyAt;
        private bool subscribed, hurtSubscribed, shootSubscribed, dropping, finished;
        private bool earmuffsEquipped, earmuffsBroken, halfAnnounced, firstRockfallAnnounced;

        /// <summary>只读，给 F3 与演练：已数的枪、这一轮要数满几枪、是否正在落石、静听耳罩是否还起作用。</summary>
        internal int Shots { get { return shots; } }
        internal int ShotsNeeded { get { return SkyIslandBossRules.ShotsPerRockfallFor(SkyIslandBossGearWorn.Earmuffs); } }
        internal bool Dropping { get { return dropping; } }
        internal bool EarmuffsWorking { get { return earmuffsEquipped && boss != null && !EarmuffsBroken(); } }

#if BOSSRUSH_DEV
        /// <summary>Dev 自动验收专用：不开枪直接往计数里加（离线造不出主角的枪声）。正式构建里不存在。</summary>
        internal void DevFeedShots(int count) { if (count > 0) shots += count; }
#endif

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("听雨人缺少生命组件");
            // 配装在控制器之前做完：只有真的戴上了，「被打穿」才有意义，也才会数枪。
            earmuffsEquipped = !SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(character, "Headset"), BossRushItemIds.SkyIslandRainhushEarmuffs);
            earmuffsBroken = !earmuffsEquipped;
            aborted = Aborted;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            // 没戴上耳罩（配装失败）就既不数枪、也不用磨耳罩：这一场它只剩官方 AI。
            if (!earmuffsEquipped) return;
            if (!hurtSubscribed)
            {
                health.OnHurtEvent.AddListener(OnHurt);
                hurtSubscribed = true;
            }
            if (!shootSubscribed)
            {
                ItemAgent_Gun.OnMainCharacterShootEvent += OnPlayerShot;
                shootSubscribed = true;
            }
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (Aborted()) return;
            TickBrokenGear();
            if (earmuffsBroken) return;
            int needed = SkyIslandBossRules.ShotsPerRockfallFor(SkyIslandBossGearWorn.Earmuffs);
            if (!halfAnnounced && shots > 0 && shots * 2 >= needed)
            {
                halfAnnounced = true;
                Announce("听雨人侧着头在听你的枪声。", "The Rain Listener tilts its head, listening to your gunfire.", false);
            }
            if (dropping || shots < needed || Time.time < rockReadyAt) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            // 数满了但人已经出了洞区：计数留着，回到洞口再开枪（或者只是站回去）就落。
            Vector3 feet = player.transform.position;
            if (!SkyIslandBossRules.InCave(feet.x, feet.z)) return;
            shots = 0;
            StartCoroutine(RockfallRoutine(feet));
        }

        /// <summary>主角每开一枪调一次（官方静态事件，热路径）：只判位置、自增，不分配、不记日志。</summary>
        private void OnPlayerShot(ItemAgent_Gun gun)
        {
            if (finished || earmuffsBroken) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && SkyIslandBossRules.InCave(player.transform.position.x, player.transform.position.z)) shots++;
        }

        // ====================================================================
        // 听枪引石
        // ====================================================================

        private IEnumerator RockfallRoutine(Vector3 target)
        {
            dropping = true;
            try
            {
                for (int i = 0; i < SkyIslandBossRules.RockfallStones; i++)
                {
                    // 第一块落在脚下，其余的在身边随机偏开一点：站着不动一定挨砸。
                    Vector3 at = target;
                    if (i > 0)
                    {
                        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        at += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SkyIslandBossRules.RockfallSpread;
                    }
                    Vector3 ground;
                    if (SkyIslandBossForge.SnapToGround(at, context, 0f, out ground)) at = ground;
                    LineRenderer line = SkyIslandBossForge.CreateGroundRing(context.Root, at);
                    rockRings.Add(line);
                    line.gameObject.name = RockfallRingName;
                    SkyIslandBossForge.SetRing(line, SkyIslandBossRules.RockfallRadius, 0f, RockTint);
                    // 点在圈画好之后才记：没画出圈的地方不砸。
                    rockPoints.Add(at);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 听雨人落石预警失败：" + e.Message); }
            if (rockPoints.Count == 0)
            {
                ClearRocks();
                dropping = false;
                rockReadyAt = Time.time + SkyIslandBossRules.RockfallCooldown;
                yield break;
            }
            if (!firstRockfallAnnounced)
            {
                firstRockfallAnnounced = true;
                Announce("洞顶的石头被枪声震松了：看见圈就挪开，在洞口少开枪。",
                    "Your gunfire shakes stones loose from the cave roof. Step out of the rings, and hold your fire near the cave mouth.", true);
            }

            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：预警只会更长，逃圈判据仍按不戴的算。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.RockfallTelegraph, SkyIslandBossGearWorn.Earmuffs);
            yield return StartCoroutine(SkyIslandBossProps.ChargeRings(rockRings, SkyIslandBossRules.RockfallRadius, telegraph, RockTint, aborted));
            if (!Aborted())
            {
                for (int i = 0; i < rockPoints.Count; i++)
                {
                    // catch 子句体内不能 yield return（CS1631）：这里只记账。
                    // 落石不是爆炸：不冒官方火球，只留余波圈与扬尘；几块齐落只震第一块（VB-21）。
                    try
                    {
                        SkyIslandBossForge.Detonate(boss, rockPoints[i], SkyIslandBossRules.RockfallRadius, SkyIslandBossRules.RockfallDamage,
                            false, i == 0 ? SkyIslandImpactFx.BossShake : 0f, RockTint);
                    }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 听雨人落石失败：" + e.Message); }
                }
            }
            ClearRocks();
            dropping = false;
            rockReadyAt = Time.time + SkyIslandBossRules.RockfallCooldown;
        }

        private void ClearRocks()
        {
            SkyIslandBossProps.DestroyRings(rockRings);
            rockPoints.Clear();
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private void OnHurt(DamageInfo damage)
        {
            if (finished || earmuffsBroken) return;
            SkyIslandBossProps.WearSoftPiece(boss, damage, "Headset", BossRushItemIds.SkyIslandRainhushEarmuffs);
        }

        private bool EarmuffsBroken()
        {
            return SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(boss, "Headset"), BossRushItemIds.SkyIslandRainhushEarmuffs);
        }

        private void TickBrokenGear()
        {
            if (earmuffsBroken || !EarmuffsBroken()) return;
            earmuffsBroken = true;
            shots = 0;
            // 听不见了就不必再听：静态开枪事件当场退订，省掉之后每一枪的回调。
            if (shootSubscribed)
            {
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnPlayerShot;
                shootSubscribed = false;
            }
            Announce("静听耳罩被打穿了：听雨人再也听不见你的枪声。",
                "The rainhush earmuffs are shot through. The Rain Listener can no longer hear your gunfire.", false);
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

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            if (shootSubscribed)
            {
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnPlayerShot;
                shootSubscribed = false;
            }
            ClearRocks();
            dropping = false;
            Announce("听雨人倒下了，洞里只剩下雨声。", "The Rain Listener falls. Only the sound of rain is left in the cave.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            if (hurtSubscribed && health != null) health.OnHurtEvent.RemoveListener(OnHurt);
            hurtSubscribed = false;
            if (shootSubscribed)
            {
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnPlayerShot;
                shootSubscribed = false;
            }
            finished = true;
            ClearRocks();
            dropping = false;
            aborted = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
