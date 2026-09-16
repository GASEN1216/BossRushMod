using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：D 悬根林的岛主「悬根猎首」（头目 R2）。
    ///
    /// 核心招式两件事，都绑在它身上那套悬根猎装上（装备即招式）：
    /// 1. **根索**：玩家进到 20 米内时，在它与玩家连线的中点横着立两根可以打的根桩（各离中点 4 米），桩间拉一道绊索。
    ///    立好先松着（暗线），过一小会儿才绷紧（亮线），12 秒自灭。绷紧后玩家这一步跨过或贴近绊索就被绊住：
    ///    脚下一小圈伤害、减速 2.5 秒，绊索随即断掉。根桩照供能桩 / 云蚋做成轻量伤害接收体，死亡看 activeSelf。
    /// 2. **换位伏击**：血线 75% / 45% 时，从开战时挑好的三处根洞里选离玩家最近、又不是它脚下那处的一个亮圈；
    ///    预警结束瞬移过去、原地炸一圈，然后愣 1.5 秒（官方 AI 暂停），愣完立刻可以再拉绊索。
    ///
    /// 克制：先打掉任意一根根桩（30 血）绊索就断；看见根洞亮圈就离开那处；它钻出来愣神那几秒趁机输出。
    ///
    /// 破甲断招（每 0.2 秒节流读一次，各播一次字幕）：
    /// - 根须面罩被暴击打穿 → 钻出来之前根洞亮得更久（1 秒变 2 秒）。官方 `Health.Hurt` 只磨头盔与护甲，
    ///   面罩由本控制器在自己的受击回调里照同一口径扣（SkyIslandBossProps.WearSoftPiece）；
    /// - 藤编甲耐久打空 → 绊索的减速只剩一半（SkyIslandBossRules.SnareSlowFor）。
    ///
    /// 伤害走官方爆炸（口径同噬风），预警与绷紧时长都过静听耳罩（SkyIslandBossRules.TelegraphSeconds）。
    /// 事件订阅只有自己身上的 `Health.OnDeadEvent` 与 `OnHurtEvent`，OnDestroy 成对退订；
    /// 根洞圈、根桩、绊索与伏击圈都挂在地图根上，在 OnDead / OnDestroy 里收，玩家身上的减速一并摘掉。
    /// </summary>
    internal sealed class SkyIslandRootHunterBoss : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        /// <summary>根洞常亮的暗圈半径：只是「这里有个洞」的标记，伏击的判定圈另画 AmbushRadius。</summary>
        private const float HollowRingRadius = 1.2f;
        /// <summary>绊住时脚下那一小圈的半径：只炸被绊的人，够不着绊索两头的根桩。</summary>
        private const float TripBlastRadius = 0.9f;
        private const float StakeCoreHeight = 0.6f;
        private const float StakeTopHeight = 1.3f;
        private const float StakeWidth = 0.22f;
        private const float SnareLineHeight = 0.35f;
        private const float SnareSlackWidth = 0.05f;
        private const float SnareTautWidth = 0.1f;
        /// <summary>立桩落点找不到地面时隔这么久再试。</summary>
        private const float SnareRetrySeconds = 1f;
        private static readonly Color HollowTint = new Color(0.45f, 0.62f, 0.30f, 1f);
        private static readonly Color AmbushTint = new Color(0.95f, 0.55f, 0.25f, 1f);
        private static readonly Color StakeTint = new Color(0.52f, 0.36f, 0.20f, 1f);
        private static readonly Color SnareSlackTint = new Color(0.60f, 0.48f, 0.30f, 0.35f);
        private static readonly Color SnareTautTint = new Color(0.96f, 0.80f, 0.45f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private SkyIslandPlayerSlow slow;
        private Func<bool> abortedCheck;
        private readonly List<Vector3> hollows = new List<Vector3>();
        private readonly List<GameObject> hollowMarks = new List<GameObject>();
        private readonly List<LineRenderer> ambushRings = new List<LineRenderer>();
        private GameObject stakeA, stakeB;
        private LineRenderer snareLine;
        private Vector3 snareFrom, snareTo;
        private int phase;
        private float nextTick, nextSnareAt, snareArmedAt, snareExpiresAt, lastPlayerX, lastPlayerZ;
        private bool subscribed, finished, ambushing, snareTaut, hasPlayerSample;
        private bool maskEquipped, cuirassEquipped, maskBrokenAnnounced, cuirassBrokenAnnounced;
        private bool snapAnnounced, tripAnnounced, ambushAnnounced;

        /// <summary>只读，给 F3 与演练：伏击相位、绊索是否已绷紧、场上活着的根桩数、是否正在换位伏击。</summary>
        internal int Phase { get { return phase; } }
        internal bool SnareArmed { get { return snareLine != null && Time.time >= snareArmedAt; } }
        internal int LiveStakes { get { return (SkyIslandBossProps.Alive(stakeA) ? 1 : 0) + (SkyIslandBossProps.Alive(stakeB) ? 1 : 0); } }
        internal bool Ambushing { get { return ambushing; } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("悬根猎首缺少生命组件");
            // 配装在控制器之前做完：只有真的穿上了，「被打穿」才有意义，也才会播那句字幕。
            maskEquipped = !SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(character, "FaceMask"), BossRushItemIds.SkyIslandRootweaveMask);
            cuirassEquipped = !SkyIslandBossForge.PieceBroken(character.GetArmorItem(), BossRushItemIds.SkyIslandVinewovenCuirass);
            slow = new SkyIslandPlayerSlow("SkyIslandRootHunterSnare");
            abortedCheck = Aborted;
            try { aiControl = new BossAIController(character, "SkyIslandRootHunter"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 悬根猎首 AI 暂停控制不可用，钻出根洞后不愣神：" + e.Message);
            }
            PlaceHollows();
            nextSnareAt = Time.time + SkyIslandBossRules.SnareInterval;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            health.OnHurtEvent.AddListener(OnHurt);
            Announce("悬根猎首从树根间直起身，林子里的根须跟着动了一下。",
                "The Hanging-Root Huntmaster rises from between the roots, and the roots across the wood stir with it.", false);
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            float now = Time.time;
            nextTick = now + TickInterval;
            // 减速按时摘放在会话判定之前：会话收尾那几拍也不会把人一直拖慢。
            slow.Tick(now);
            if (context.Valid != null && !context.Valid()) return;
            if (health.IsDead) return;
            TickBrokenGear();
            TickPhase();
            CharacterMainControl player = CharacterMainControl.Main;
            bool playerAlive = player != null && player.Health != null && !player.Health.IsDead;
            Vector3 at = playerAlive ? player.transform.position : Vector3.zero;
            TickSnare(now, player, playerAlive, at);
            // 记下这一拍玩家的位置：下一拍拿「上一拍 → 这一拍」的位移线段判定有没有跨过绊索。
            hasPlayerSample = playerAlive;
            lastPlayerX = at.x;
            lastPlayerZ = at.z;
        }

        // ====================================================================
        // 根洞与换位伏击
        // ====================================================================

        private void PlaceHollows()
        {
            Vector3 center = boss.transform.position;
            float baseAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            int count = SkyIslandBossRules.RootHollowCount;
            for (int i = 0; i < count; i++)
            {
                float angle = baseAngle + i * (Mathf.PI * 2f / count);
                float distance = UnityEngine.Random.Range(SkyIslandBossRules.RootHollowMinDistance, SkyIslandBossRules.RootHollowMaxDistance);
                Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                Vector3 ground;
                if (!SkyIslandBossProps.SnapNear(probe, context, 0.45f, 1.5f, out ground)) continue;
                hollows.Add(ground);
                try
                {
                    LineRenderer mark = SkyIslandBossForge.CreateGroundRing(context.Root, ground);
                    mark.gameObject.name = "SkyIslandRootHollow";
                    hollowMarks.Add(mark.gameObject);
                    SkyIslandBossForge.SetRing(mark, HollowRingRadius, 0.2f, HollowTint);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 根洞标记失败：" + e.Message); }
            }
            if (hollows.Count == 0) Debug.LogWarning("[SkyIslandBoss] 悬根猎首身边找不到能落脚的根洞，这一场不换位伏击");
        }

        private void TickPhase()
        {
            // 伏击途中跨过下一档先不记：等这一次钻完再轮到它。
            if (ambushing) return;
            float max = health.MaxHealth;
            if (max <= 0f) return;
            int target = SkyIslandBossRules.PhaseFor(Mathf.Clamp01(health.CurrentHealth / max), SkyIslandBossRules.RootHunterAmbushThresholds);
            if (target <= phase) return;
            phase = target;
            int index = PickHollow();
            if (index >= 0) StartCoroutine(AmbushRoutine(hollows[index]));
        }

        /// <summary>离玩家最近、又不是它脚下那处（伏击圈以内）的根洞；只剩脚下那处时就用它。</summary>
        private int PickHollow()
        {
            if (hollows.Count == 0) return -1;
            Vector3 self = boss.transform.position;
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 reference = player != null ? player.transform.position : self;
            float standSqr = SkyIslandBossRules.AmbushRadius * SkyIslandBossRules.AmbushRadius;
            int best = -1, fallback = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hollows.Count; i++)
            {
                if (FlatSqr(hollows[i], self) <= standSqr) { fallback = i; continue; }
                float sqr = FlatSqr(hollows[i], reference);
                if (sqr < bestSqr) { bestSqr = sqr; best = i; }
            }
            return best >= 0 ? best : fallback;
        }

        private IEnumerator AmbushRoutine(Vector3 hollow)
        {
            ambushing = true;
            try
            {
                LineRenderer ring = SkyIslandBossForge.CreateGroundRing(context.Root, hollow);
                ambushRings.Add(ring);
                SkyIslandBossForge.SetRing(ring, SkyIslandBossRules.AmbushRadius, 0f, AmbushTint);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 根洞伏击圈失败：" + e.Message); }
            // 圈画不出来就不钻：没有预警的伏击不公平。此时还没暂停官方 AI，不需要恢复。
            if (ambushRings.Count == 0)
            {
                ambushing = false;
                yield break;
            }
            if (!ambushAnnounced)
            {
                ambushAnnounced = true;
                Announce("悬根猎首钻进了树根里：哪个根洞亮起来，就离那里远点。",
                    "The Huntmaster dives into the roots. Whichever root hollow lights up, get away from it.", true);
            }
            // 面罩被打穿之后根洞亮得更久；戴着静听耳罩的玩家早一点听见（只会更长，逃圈判据仍按不戴的算）。
            float baseSeconds = maskEquipped && MaskBroken() ? SkyIslandBossRules.AmbushTelegraphMaskBroken : SkyIslandBossRules.AmbushTelegraph;
            float telegraph = SkyIslandBossRules.TelegraphSeconds(baseSeconds, SkyIslandBossGearWorn.Earmuffs);
            yield return StartCoroutine(SkyIslandBossProps.ChargeRings(ambushRings, SkyIslandBossRules.AmbushRadius, telegraph, AmbushTint, abortedCheck));
            if (!Aborted())
            {
                // catch 子句体内不能 yield return（CS1631）：换位与落点各自只记账，互不连坐。
                try { SkyIslandBossProps.Teleport(boss, hollow + Vector3.up * 0.1f, aiControl); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 悬根猎首钻出根洞失败：" + e.Message); }
                try { SkyIslandBossForge.Detonate(boss, hollow, SkyIslandBossRules.AmbushRadius, SkyIslandBossRules.AmbushDamage); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 根洞伏击落点失败：" + e.Message); }
            }
            SkyIslandBossProps.DestroyRings(ambushRings);
            // 钻出来愣一下：官方 AI 已由 Teleport 暂停，这几秒不走不打。
            float staggerUntil = Time.time + SkyIslandBossRules.AmbushStagger;
            while (Time.time < staggerUntil && !Aborted()) yield return null;
            ambushing = false;
            // 倒下不恢复；活着的出口（愣完、会话失效中途退出）一律恢复并重新认人。
            if (finished || boss == null || health == null || health.IsDead) yield break;
            ResumeAi();
            nextSnareAt = Time.time;
        }

        private void ResumeAi()
        {
            try { if (aiControl != null && aiControl.IsPaused) aiControl.Resume(CharacterMainControl.Main); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 悬根猎首恢复失败：" + e.Message); }
        }

        private static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // ====================================================================
        // 根索
        // ====================================================================

        private void TickSnare(float now, CharacterMainControl player, bool playerAlive, Vector3 at)
        {
            if (snareLine != null)
            {
                if (!SkyIslandBossProps.Alive(stakeA) || !SkyIslandBossProps.Alive(stakeB))
                {
                    DestroySnare();
                    if (snapAnnounced) return;
                    snapAnnounced = true;
                    Announce("根桩倒了，悬根猎首的绊索断了。", "A root stake is down and the Huntmaster's snare snaps.", false);
                    return;
                }
                if (now >= snareExpiresAt) { DestroySnare(); return; }
                if (now < snareArmedAt) return;
                if (!snareTaut)
                {
                    snareTaut = true;
                    snareLine.widthMultiplier = SnareTautWidth;
                    snareLine.startColor = SnareTautTint;
                    snareLine.endColor = SnareTautTint;
                }
                if (playerAlive && hasPlayerSample && SkyIslandBossRules.SnareTripped(snareFrom.x, snareFrom.z, snareTo.x, snareTo.z,
                        lastPlayerX, lastPlayerZ, at.x, at.z))
                    Trip(player, at);
                return;
            }
            if (ambushing || !playerAlive || now < nextSnareAt) return;
            float range = SkyIslandBossRules.SnareRange;
            if ((at - boss.transform.position).sqrMagnitude > range * range)
            {
                nextSnareAt = now + SnareRetrySeconds;
                return;
            }
            StringSnare(now, at);
        }

        /// <summary>在它与玩家连线的中点横着立两根桩、拉一道绊索：绊索横在两人之间，不会立在玩家脚下。</summary>
        private void StringSnare(float now, Vector3 playerAt)
        {
            Vector3 self = boss.transform.position;
            Vector3 flat = new Vector3(playerAt.x - self.x, 0f, playerAt.z - self.z);
            if (flat.sqrMagnitude < 0.01f) { nextSnareAt = now + SnareRetrySeconds; return; }
            Vector3 mid = (self + playerAt) * 0.5f;
            Vector3 side = Vector3.Cross(Vector3.up, flat.normalized) * SkyIslandBossRules.SnareHalfLength;
            Vector3 groundA, groundB;
            if (!SkyIslandBossProps.SnapNear(mid + side, context, 0.3f, 1f, out groundA) ||
                !SkyIslandBossProps.SnapNear(mid - side, context, 0.3f, 1f, out groundB))
            {
                nextSnareAt = now + SnareRetrySeconds;
                return;
            }
            nextSnareAt = now + SkyIslandBossRules.SnareInterval;
            stakeA = SpawnStake(groundA);
            stakeB = SpawnStake(groundB);
            if (stakeA == null || stakeB == null)
            {
                DestroySnare();
                return;
            }
            try
            {
                snareLine = SkyIslandBossForge.StraightLine(context.Root, "SkyIslandRootSnare", SnareSlackWidth, SnareSlackTint);
                snareLine.SetPosition(0, groundA + Vector3.up * SnareLineHeight);
                snareLine.SetPosition(1, groundB + Vector3.up * SnareLineHeight);
            }
            catch (Exception e)
            {
                // 看不见的绊索不许留：线画不出来就连桩一起收。
                Debug.LogWarning("[SkyIslandBoss] 绊索创建失败：" + e.Message);
                DestroySnare();
                return;
            }
            snareFrom = groundA;
            snareTo = groundB;
            snareTaut = false;
            // 立好先松着，不许当场绊人；戴着静听耳罩的玩家早一点听见，绷紧只会更晚。
            snareArmedAt = now + SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.SnareArmSeconds, SkyIslandBossGearWorn.Earmuffs);
            snareExpiresAt = now + SkyIslandBossRules.SnareLifetime;
        }

        private GameObject SpawnStake(Vector3 ground)
        {
            GameObject go = SkyIslandBossProps.CreateReceiver(context.Root, "SkyIslandRootStake", ground + Vector3.up * StakeCoreHeight, 0.45f,
                SkyIslandBossRules.SnareStakeHealth, boss);
            if (go == null) return null;
            try
            {
                // 桩身挂在桩自己身上：官方 HealthSimpleBase 打空后停用根物体，桩身跟着一起消失。
                LineRenderer column = SkyIslandBossForge.StraightLine(go.transform, "Column", StakeWidth, StakeTint);
                column.SetPosition(0, ground + Vector3.up * 0.05f);
                column.SetPosition(1, ground + Vector3.up * StakeTopHeight);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 根桩桩身失败：" + e.Message);
                Destroy(go);
                return null;
            }
            SkyIslandBossProps.Activate(go, boss);
            return go;
        }

        private void Trip(CharacterMainControl player, Vector3 at)
        {
            try { SkyIslandBossForge.Detonate(boss, at, TripBlastRadius, SkyIslandBossRules.SnareDamage); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 绊索伤害失败：" + e.Message); }
            // 藤编甲被打空之后减速只剩一半（纯规则 SnareSlowFor）；同幅度再绊只顺延，不叠。
            slow.Apply(player, SkyIslandBossRules.SnareSlowFor(cuirassEquipped && CuirassBroken()), SkyIslandBossRules.SnareSlowSeconds);
            DestroySnare();
            if (tripAnnounced) return;
            tripAnnounced = true;
            Announce("被悬根猎首的绊索绊住了：先砍倒一根根桩，绊索就断。",
                "The Huntmaster's snare trips you. Cut down either root stake and the snare snaps.", true);
        }

        private void DestroySnare()
        {
            if (stakeA != null) Destroy(stakeA);
            if (stakeB != null) Destroy(stakeB);
            if (snareLine != null) Destroy(snareLine.gameObject);
            stakeA = null;
            stakeB = null;
            snareLine = null;
            snareTaut = false;
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private bool MaskBroken()
        {
            return SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(boss, "FaceMask"), BossRushItemIds.SkyIslandRootweaveMask);
        }

        private bool CuirassBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetArmorItem(), BossRushItemIds.SkyIslandVinewovenCuirass);
        }

        private void TickBrokenGear()
        {
            if (maskEquipped && !maskBrokenAnnounced && MaskBroken())
            {
                maskBrokenAnnounced = true;
                Announce("根须面罩被打穿了：悬根猎首钻出来之前，根洞会亮得更久。",
                    "The rootweave mask is shot through. The hollows now glow longer before the Huntmaster bursts out.", false);
            }
            if (cuirassEquipped && !cuirassBrokenAnnounced && CuirassBroken())
            {
                cuirassBrokenAnnounced = true;
                Announce("藤编甲被打烂了：悬根猎首的绊索只能让你慢一半。",
                    "The vinewoven cuirass is torn apart. The Huntmaster's snares now slow you only half as much.", false);
            }
        }

        private void OnHurt(DamageInfo damage)
        {
            if (finished) return;
            // 官方 Health.Hurt 只磨头盔与护甲：面罩照同一口径（暴击、非真实伤害、不无视护甲）在这里扣。
            SkyIslandBossProps.WearSoftPiece(boss, damage, "FaceMask", BossRushItemIds.SkyIslandRootweaveMask);
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
            Cleanup();
            if (slow != null) slow.Release();
            Announce("悬根猎首倒下了，林子里的根须慢慢垂了下去。",
                "The Hanging-Root Huntmaster falls, and the roots across the wood slowly go limp.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        /// <summary>收掉本控制器建的全部场上物件（根桩、绊索、伏击圈、根洞圈）。不恢复 AI：倒下与销毁都不该让它再动。</summary>
        private void Cleanup()
        {
            DestroySnare();
            SkyIslandBossProps.DestroyRings(ambushRings);
            for (int i = 0; i < hollowMarks.Count; i++) if (hollowMarks[i] != null) Destroy(hollowMarks[i]);
            hollowMarks.Clear();
            hollows.Clear();
        }

        private void OnDestroy()
        {
            if (subscribed && health != null)
            {
                health.OnDeadEvent.RemoveListener(OnDead);
                health.OnHurtEvent.RemoveListener(OnHurt);
            }
            subscribed = false;
            finished = true;
            Cleanup();
            if (slow != null) slow.Release();
            slow = null;
            aiControl = null;
            abortedCheck = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
