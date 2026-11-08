using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    // 固定目标与时间归同一个出招 owner；换潮、警告消失和撤离一律取消待结算的一击。
    internal sealed class TidegateSandAttack
    {
        internal bool Flats { get; private set; }
        internal bool Pending { get; private set; }
        internal float Start { get; private set; }
        internal Vector3 Mark { get; private set; }
        internal float Radius { get { return Flats ? 3.6f : 2.15f; } }
        internal float Windup { get { return Flats ? 2.0f : 2.6f; } }
        private float next;
        private bool placed;

        internal bool Change(bool flats, float now)
        {
            if (placed && Flats == flats) return false;
            placed = true; Flats = flats; Cancel(now);
            return true;
        }
        internal bool TryStart(float now, float range, bool inPosition, bool seen, Vector3 ownerFeet, Vector3 targetFeet)
        {
            if (!placed || Pending || now < next || !seen || !inPosition) return false;
            if (Flats ? range > 4.5f : range < 5f || range > 20f) return false;
            Mark = Flats ? ownerFeet : targetFeet;
            Pending = true; Start = now; return true;
        }
        internal bool Due(float now, bool visible, bool stillSafe)
        {
            if (!Pending) return false;
            if (!visible || !stillSafe) { Cancel(now); return false; }
            if (now < Start + Windup) return false;
            Pending = false; next = now + (Flats ? 10f : 14f); return true;
        }
        internal void Cancel(float now) { Pending = false; next = now + 3f; }
    }

    internal sealed class TidegateSandBoss : MonoBehaviour
    {
        internal const string SourceName = "EnemyPreset_Scav";
        private static readonly Color FlatsTint = new Color(.98f, .57f, .17f), DeckTint = new Color(.99f, .28f, .16f);
        private readonly TidegateSandAttack attack = new TidegateSandAttack();
        private CharacterMainControl boss;
        private AICharacterController ai;
        private TidegateTide tide;
        private TidegateWorld world;
        private Func<bool> flats, valid;
        private Func<int> spare;
        private Vector3 deck, dig;
        private LineRenderer ring;
        private float nextThink, nextMove;
        private bool subscribed, settled;
        internal string Reference { get; private set; }
        internal bool OnFlats { get { return attack.Flats; } }
        internal bool OwnsDrop { get { return subscribed; } }

        /// <summary>
        /// 原为天空岛头目 / 岛主的保底 1000（SkyIslandCombatBalance.BossHealthFloor）；owner 2026-11-08 发布前要求整体加强，
        /// 随小兵的 ×1.5（<see cref="TidegateCombatTuning"/>）提到 1500，仍高于保底。
        /// 与镇上人形敌人同用官方 Scav，再设前摇 0.3、伤害倍率 1.25；压力主要来自两招有预警的潮相攻击（预警时长不变）。
        /// 离线拿不到官方武器伤害数据，击杀时间只能实机量（实机清单 M_TIDEGATE_12）。
        /// </summary>
        internal const float BossHealth = 1500f;
        internal const float BossDamage = 1.25f;
        /// <summary>两招潮相攻击的爆炸伤害：滩上扫钩 / 船上落锤（原 38 / 46）。</summary>
        internal const float FlatsBlast = 44f, DeckBlast = 52f;
        /// <summary>只放大模型（碰撞体与导航半径不变），和天空岛同底模的风猎看守（1.0 倍 + 断风兜帽）一眼分得开。</summary>
        internal const float ModelScale = 1.2f;

        internal static void Tune(CharacterRandomPreset clone)
        {
            clone.nameKey = TidegateText.MapBossKey; clone.showName = clone.showHealthBar = clone.isBoss = true;
            clone.health = BossHealth; clone.forceTracePlayerDistance = 0f;
            clone.patrolRange = 2.5f; clone.damageMultiplier = BossDamage; clone.shootDelay = .3f;
            clone.setMeleeDamageMultiplier = true; clone.meleeDamageMultiplier = BossDamage;
            clone.specialAttachmentBases = new List<AISpecialAttachmentBase>(); // 不带参照 Boss 的独有招式。
        }
        /// <summary>同天空岛 SkyIslandBossForge.ApplyScale：只放大模型节点，挂在 socket 上的装备跟着放大。可失败的装饰。</summary>
        internal static void ApplyLook(CharacterMainControl created)
        {
            try
            {
                if (created != null && created.characterModel != null)
                    created.characterModel.transform.localScale = Vector3.one * ModelScale;
            }
            catch (Exception e) { Debug.LogWarning("[Tidegate] 沙老大体型缩放失败：" + e.Message); }
        }

        /// <summary>带着响铊再来打的人拿另一件货；没接上时始终给响铊。</summary>
        internal void OfferSpare(Func<int> pick) { spare = pick; }

        internal void Bind(CharacterMainControl character, AICharacterController controller, TidegateTide localTide,
            TidegateWorld site, Vector3 boat, Vector3 flatsPost, Func<bool> whichPost, Func<bool> sessionValid, string reference)
        {
            if (subscribed) return;
            boss = character; ai = controller; tide = localTide; world = site; deck = boat; dig = flatsPost;
            flats = whichPost; valid = sessionValid; Reference = reference;
            boss.BeforeCharacterSpawnLootOnDead += BeforeLoot;
            subscribed = true;
        }
        private void Update()
        {
            float now = Time.time;
            if (now < nextThink) return;
            nextThink = now + (attack.Pending ? .08f : .30f);
            try
            {
                var player = CharacterMainControl.Main;
                if (boss == null || boss.Health == null || boss.Health.IsDead || player == null || player.Health == null ||
                    player.Health.IsDead || ai == null || valid == null || !valid()) { DropRing(); return; }
                bool onFlats = flats();
                Vector3 post = onFlats ? dig : deck;
                if (attack.Change(onFlats, now))
                {
                    DropRing(); nextMove = now;
                    ai.traceTargetChance = onFlats ? .8f : 0f; ai.combatMoveRange = onFlats ? 5f : 1.5f;
                    if ((boss.transform.position - player.transform.position).sqrMagnitude < 32f * 32f)
                        Say(onFlats ? L10n.T("潮落，下滩起钩！", "Ebb tide. Hooks to the flats!") : L10n.T("收钩！回船架绞盘！", "Hooks in! Back to the winch!"));
                }
                float offPost = FlatDistance(transform.position, post);
                bool inPosition = offPost <= (onFlats ? 9f : 6f);
                if (!inPosition && now >= nextMove)
                {
                    nextMove = now + 2f;
                    if (world.TryGroundAt(post, out post.y) && !tide.IsDeepAt(post)) { ai.patrolPosition = post; ai.MoveToPos(post); }
                }
                if (attack.Pending)
                {
                    bool visible = ring != null && ring.enabled && ring.gameObject.activeInHierarchy;
                    if (visible) SkyIslandBossForge.SetRing(ring, attack.Radius, Mathf.Clamp01((now - attack.Start) / attack.Windup), Tint);
                    // 跨墙、涨水、Boss 离阵或死亡都不补发。爆炸的遮蔽与敌我过滤由官方 ExplosionManager 结算。
                    if (attack.Due(now, visible, inPosition && !tide.IsDeepAt(attack.Mark) && ClearSight(attack.Mark)))
                        SkyIslandBossForge.Detonate(boss, attack.Mark, attack.Radius, onFlats ? FlatsBlast : DeckBlast, false, .15f, Tint);
                    if (!attack.Pending) DropRing();
                    return;
                }
                if (!inPosition || ai.searchedEnemy == null || ai.searchedEnemy != player.mainDamageReceiver || !ClearSight(player.transform.position)) return;
                Vector3 feet = onFlats ? transform.position : player.transform.position;
                if (!world.TryGroundAt(feet, out feet.y) || tide.IsDeepAt(feet)) return;
                if (!attack.TryStart(now, FlatDistance(transform.position, player.transform.position), inPosition, true, feet, feet)) return;
                ring = SkyIslandBossForge.CreateGroundRing(transform.parent, feet);
                SkyIslandBossForge.SetRing(ring, attack.Radius, 0f, Tint);
                Say(onFlats ? L10n.T("绞钩扫滩！离我远点！", "Hook sweep! Stand clear of me!") : L10n.T("松闸放锤！看落点！", "Winch away! Watch the landing mark!"));
            }
            catch (Exception e) { attack.Cancel(now); DropRing(); Debug.LogWarning("[Tidegate] 沙老大招式取消：" + e.Message); }
        }
        private Color Tint { get { return attack.Flats ? FlatsTint : DeckTint; } }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return (a - b).magnitude; }
        private bool ClearSight(Vector3 feet) { return !Physics.Linecast(transform.position + Vector3.up, feet + Vector3.up * .7f, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore); }
        private void Say(string text) { try { DialogueBubblesManager.Show(text, transform, 2.2f, false, false, -1f, 1.2f).Forget(); } catch { } }
        private void BeforeLoot(DamageInfo damage)
        {
            if (settled || !subscribed || valid == null || !valid()) return;
            settled = true; // 返回基地和销毁不会经过死亡事件，重入也不会再发。
            Item item = null;
            try
            {
                var inventory = boss.CharacterItem != null ? boss.CharacterItem.Inventory : null;
                // 响铊能反复用：玩家身上已经带着一把（它只在潮闸镇有用，常用的人会随身带），第二把只剩卖钱的用处，
                // 换成一件挖沙船私货；第一次打倒他、或者私货目录不可用时照旧给响铊。
                int typeId = BossRushItemIds.TidegateSoundingLead;
                if (spare != null && TidegateInventory.Carries(CharacterMainControl.Main, BossRushItemIds.TidegateSoundingLead))
                {
                    int alternative = spare();
                    if (alternative > 0 && ItemAssetsCollection.GetPrefab(alternative) != null) typeId = alternative;
                }
                if (inventory == null || ItemAssetsCollection.GetPrefab(typeId) == null)
                    throw new InvalidOperationException("尸体库存或掉落预制件不可用");
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId ||
                    !InteractableLootboxInventoryHelper.TryAddExtraItem(inventory, item)) throw new InvalidOperationException("专属掉落未进入尸体库存");
                item = null;
            }
            catch (Exception e) { Debug.LogWarning("[Tidegate] 沙老大专属掉落：" + e.Message); }
            finally { SkyIslandInventoryTransaction.DestroyUnowned(item); }
        }
        private void DropRing() { if (ring != null) SkyIslandBossForge.ReleaseRing(ring); ring = null; }
        private void OnDisable() { attack.Cancel(Time.time); DropRing(); }
        private void OnDestroy()
        {
            if (subscribed && boss != null) boss.BeforeCharacterSpawnLootOnDead -= BeforeLoot;
            subscribed = false; DropRing();
        }
    }
}
