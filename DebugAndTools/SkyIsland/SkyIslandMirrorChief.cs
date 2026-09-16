using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：F 镜水寺的头目「镜中客」（R4，只在夜里出来；等夜与补刷由遭遇 owner 判，这里不判夜）。
    ///
    /// 核心招式「倒影换位」：玩家在镜池一带时，它在玩家背后、与自己关于玩家对称的位置亮一圈，
    /// 预警结束翻过去落地震一下（官方爆炸，圈半径就是判定半径），原地留一个会吸准星的半透明倒影。
    /// 翻身不暂停官方 AI：落地立刻认人，接着打。
    ///
    /// 克制：
    /// - 看见身后亮圈就转身、挪出圈；
    /// - 先打碎倒影，它失衡几秒（官方 AI 暂停），失衡期间不翻身；倒影到时自散，不会一直吸准星；
    /// - 把它拉离镜池（玩家走出池心 <see cref="SkyIslandBossRules.PoolRange"/> 米）它就不再翻到你背后。
    ///
    /// 装备联动：镜纹甲耐久打空（官方在出击图上自己磨身甲，不需要受击回调）之后照样翻身，但再也留不下倒影。
    /// 只有开战时真穿着才算：配装失败的那一趟从头就不留倒影（口径同观星手的镜盔）。
    ///
    /// 倒影是 <see cref="SkyIslandBossProps.CreateDecoy"/> 烤出来的静态网格接收体，不克隆角色；
    /// 打碎看 activeSelf（官方 DamageReceiver.OnDead 停用根物体）。
    /// 事件订阅：只订自己身上的 `Health.OnDeadEvent`，OnDestroy 成对退订；圈、倒影与烤出来的网格在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandMirrorChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        /// <summary>开战后这么久才第一次翻身：先让玩家看清它是谁。</summary>
        private const float FirstSwapDelay = 5f;
        /// <summary>找不到落点、预警没画出来或被失衡打断时，隔这么久再试。</summary>
        private const float SwapRetryDelay = 2f;
        private const float LandingClearance = 0.45f;
        private const float LandingJitter = 1.5f;
        private static readonly Color SwapTint = new Color(0.70f, 0.86f, 1f, 1f);
        private static readonly Color DecoyTint = new Color(0.78f, 0.9f, 1f, 0.55f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private readonly List<LineRenderer> swapRings = new List<LineRenderer>();
        private readonly List<Mesh> bakedMeshes = new List<Mesh>();
        private GameObject decoy;
        /// <summary>给 ChargeRings 的中止判据：Bind 时建一次，不在每次换位里新建委托。</summary>
        private Func<bool> abortedCheck;
        private float nextTick, nextSwapAt, decoyExpiresAt, staggerUntil;
        private bool subscribed, swapping, staggered, decoyOut, finished;
        private bool plateEquipped, plateBrokenAnnounced, firstSwapAnnounced, firstShatterAnnounced;

        /// <summary>只读，给 F3 与演练：是否正在换位预警、倒影在不在、是否失衡、镜纹甲是否还起作用。</summary>
        internal bool Swapping { get { return swapping; } }
        internal bool DecoyAlive { get { return decoyOut && SkyIslandBossProps.Alive(decoy); } }
        internal bool Staggered { get { return staggered; } }
        internal bool PlateWorking { get { return plateEquipped && !PlateBroken(); } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("镜中客缺少生命组件");
            // 配装在控制器之前做完：只有开战时真穿着镜纹甲，才会留倒影、才会播「被打穿」那句字幕。
            plateEquipped = !SkyIslandBossForge.PieceBroken(character.GetArmorItem(), BossRushItemIds.SkyIslandMirrorgrainPlate);
            abortedCheck = Aborted;
            try { aiControl = new BossAIController(character, "SkyIslandMirror"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 镜中客 AI 暂停控制不可用，倒影碎了也不失衡：" + e.Message);
            }
            nextSwapAt = Time.time + FirstSwapDelay;
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
            TickPlate();
            TickDecoy();
            TickStagger();
            if (swapping || staggered || Time.time < nextSwapAt) return;
            TryStartSwap();
        }

        // ====================================================================
        // 倒影换位
        // ====================================================================

        private void TryStartSwap()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 at = player.transform.position;
            // 玩家离镜池远了就不翻：把它拉离镜池是这一招的解法之一。
            if (!SkyIslandBossRules.NearMirrorPool(at.x, at.z)) return;
            Vector3 from = boss.transform.position;
            float x, z;
            if (!SkyIslandBossRules.MirrorAcross(at.x, at.z, from.x, from.z, out x, out z))
            {
                nextSwapAt = Time.time + SwapRetryDelay;
                return;
            }
            Vector3 ground;
            if (!SkyIslandBossProps.SnapNear(new Vector3(x, at.y, z), context, LandingClearance, LandingJitter, out ground))
            {
                nextSwapAt = Time.time + SwapRetryDelay;
                return;
            }
            StartCoroutine(SwapRoutine(ground));
        }

        private IEnumerator SwapRoutine(Vector3 ground)
        {
            swapping = true;
            bool drawn = false;
            try
            {
                LineRenderer ring = SkyIslandBossForge.CreateGroundRing(context.Root, ground);
                ring.gameObject.name = "SkyIslandMirrorRing";
                swapRings.Add(ring);
                SkyIslandBossForge.SetRing(ring, SkyIslandBossRules.SwapRadius, 0f, SwapTint);
                drawn = true;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 镜中客换位预警失败：" + e.Message); }
            if (drawn && !firstSwapAnnounced)
            {
                firstSwapAnnounced = true;
                Announce("镜中客在你背后亮了一圈：它要翻过去了，转身、挪出圈。",
                    "A ring lights up behind you: the Mirror Guest is about to flip over. Turn around and step out of it.", true);
            }

            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：预警只会更长，逃圈判据仍按不戴的算。
            // catch 子句与带 catch 的 try 里都不能 yield（CS1631）：画圈在上面的 try 里做完，蓄力放在外面。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.SwapTelegraph, SkyIslandBossGearWorn.Earmuffs);
            if (drawn)
                yield return StartCoroutine(SkyIslandBossProps.ChargeRings(swapRings, SkyIslandBossRules.SwapRadius, telegraph, SwapTint, abortedCheck));

            // 没画出预警就不翻（不许无预警落地震人）；蓄力期间倒影被打碎、正在失衡也不翻。
            if (!drawn || staggered || Aborted())
            {
                SkyIslandBossProps.DestroyRings(swapRings);
                swapping = false;
                if (!Aborted()) nextSwapAt = Time.time + SwapRetryDelay;
                yield break;
            }
            try
            {
                // 倒影先落在它此刻站的地方，再翻身。镜纹甲还起作用、场上没有倒影时才留。
                if (PlateWorking && !decoyOut) SpawnDecoy();
                if (SkyIslandBossProps.Teleport(boss, ground + Vector3.up * 0.1f, null))
                {
                    SkyIslandBossProps.NoticePlayer(boss);
                    SkyIslandBossForge.Detonate(boss, ground, SkyIslandBossRules.SwapRadius, SkyIslandBossRules.SwapDamage);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 镜中客换位落地失败：" + e.Message); }
            SkyIslandBossProps.DestroyRings(swapRings);
            swapping = false;
            nextSwapAt = Time.time + SkyIslandBossRules.SwapInterval;
        }

        // ====================================================================
        // 倒影与失衡
        // ====================================================================

        private void SpawnDecoy()
        {
            decoy = SkyIslandBossProps.CreateDecoy(context.Root, boss, SkyIslandBossRules.DecoyHealth, DecoyTint, bakedMeshes);
            if (decoy == null)
            {
                // 建失败时接收体已由 Props 收掉，但烤到一半的网格还留在列表里。
                SkyIslandBossProps.DestroyDecoy(null, bakedMeshes);
                return;
            }
            decoyOut = true;
            decoyExpiresAt = Time.time + SkyIslandBossRules.DecoySeconds;
        }

        private void TickDecoy()
        {
            if (!decoyOut) return;
            if (decoy == null)
            {
                // 被外部连根销毁（会话收尾）：只收烤出来的网格，不算打碎。
                ClearDecoy();
                return;
            }
            if (!SkyIslandBossProps.Alive(decoy))
            {
                ClearDecoy();
                EnterStagger();
                return;
            }
            if (Time.time >= decoyExpiresAt) ClearDecoy();
        }

        private void ClearDecoy()
        {
            SkyIslandBossProps.DestroyDecoy(decoy, bakedMeshes);
            decoy = null;
            decoyOut = false;
        }

        private void EnterStagger()
        {
            try { if (aiControl != null) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 镜中客失衡停手失败：" + e.Message); }
            staggered = true;
            staggerUntil = Time.time + SkyIslandBossRules.DecoyStagger;
            if (firstShatterAnnounced) return;
            firstShatterAnnounced = true;
            Announce("倒影碎了，镜中客晃了一下：趁现在打它。", "The reflection shatters and the Mirror Guest reels: hit it now.", true);
        }

        private void TickStagger()
        {
            if (!staggered || Time.time < staggerUntil) return;
            staggered = false;
            ResumeAi();
        }

        private void ResumeAi()
        {
            try { if (aiControl != null && aiControl.IsPaused) aiControl.Resume(CharacterMainControl.Main); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 镜中客恢复失败：" + e.Message); }
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private bool PlateBroken()
        {
            return boss == null || SkyIslandBossForge.PieceBroken(boss.GetArmorItem(), BossRushItemIds.SkyIslandMirrorgrainPlate);
        }

        private void TickPlate()
        {
            if (plateEquipped && !plateBrokenAnnounced && PlateBroken())
            {
                plateBrokenAnnounced = true;
                Announce("镜纹甲被打穿了：镜中客再也留不下倒影。",
                    "The mirrorgrain plate is shot through: the Mirror Guest can no longer leave a reflection behind.", false);
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

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            Cleanup();
            Announce("镜中客倒下了，池面上的倒影慢慢散了。", "The Mirror Guest falls and the reflections on the pool slowly fade.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        /// <summary>收掉圈、倒影与烤出来的网格。倒下与销毁都走这里，不恢复 AI（人已经倒了或正在销毁）。</summary>
        private void Cleanup()
        {
            SkyIslandBossProps.DestroyRings(swapRings);
            ClearDecoy();
            swapping = false;
            staggered = false;
        }

        private void OnDisable()
        {
            // 角色被停用时协程随之停止：收掉换位预警、清掉进行中的标记，免得重新启用后再也不翻身。
            // 失衡由节流按时恢复，这里不碰 AI；销毁路径也先走这里，同样不恢复。
            if (finished || !swapping) return;
            SkyIslandBossProps.DestroyRings(swapRings);
            swapping = false;
            nextSwapAt = Time.time + SwapRetryDelay;
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            Cleanup();
            aiControl = null;
            abortedCheck = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
