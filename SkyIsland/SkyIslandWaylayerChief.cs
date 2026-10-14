using System;
using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：S2 倒挂邮亭的头目「截信人」（头目 R2）。
    ///
    /// 核心招式「劫包」：玩家贴到 2.5 米内、背包里有岛上耗材时，它脚下亮圈伸手 0.8 秒；亮圈结束玩家还在 2.9 米内，
    /// 就抢走一件最值钱的岛上耗材（整堆只拿一个），随即闪到邮亭离玩家远的那一头，跑速 +30% 持续 6 秒。一场最多抢两次。
    /// 只偷 SkyIslandItemRules.StealableTypeIds：纪念品、材料与官方物品一律不碰；拿在手上的那件也不偷。
    ///
    /// 克制：别让它贴身，伸手亮圈时后撤（退出够手距离就抓空，冷却减半）；被抢了就追到逃点打——
    /// 东西在它自己的背包里，倒下时照官方掉落进尸体箱（本控制器在死亡时不碰它们）。
    ///
    /// 装备联动「旧邮包是它的胆」：没背着旧邮包（配装失败）就没处藏，整场不劫包，只剩官方 AI；
    /// 血线打到四成以下，它把抢来的、还在身上的东西丢在脚下（官方 Drop 成拾取物），此后不再劫包。
    ///
    /// 物品搬运照 SkyIslandInventoryTransaction 的口径：每一步按实际归属确认结果，失败按 AddAt → AddItem → SendToPlayer → 落地退回，
    /// 绝不留下无主物品；整堆里拿一个时先把复制出的那一个在它背包里放稳，再给玩家那堆减一，减不下去就把复制品收回销毁。
    /// 事件订阅只有自己身上的 `Health.OnDeadEvent`，OnDestroy 退订；逃跑加速在到时、倒下与销毁时摘掉。
    /// </summary>
    internal sealed class SkyIslandWaylayerChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        private static readonly Color SnatchTint = new Color(0.95f, 0.85f, 0.45f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private LineRenderer snatchRing;
        /// <summary>抢来的物品实例（丢下时与 F3 计数用）；只清引用，从不在这里销毁。</summary>
        private readonly List<Item> stolen = new List<Item>();
        private readonly List<BossRushStatModifierRecord> fleeRecords = new List<BossRushStatModifierRecord>();
        private readonly object fleeSource = new object();
        private int snatches;
        private float nextTick, snatchReadyAt, fleeUntil;
        private bool subscribed, finished, snatching, fleeing, dropped, mailbagEquipped, snatchAnnounced, missAnnounced;
        private bool coopSnatchPending;

        /// <summary>只读，给 F3 与演练：是否正在伸手、这一场抢了几次、身上还揣着几件赃物、是否在逃跑加速里。</summary>
        internal bool Snatching { get { return snatching; } }
        internal int Snatches { get { return snatches; } }
        internal int StolenHeld { get { return CountHeld(); } }
        internal bool Fleeing { get { return fleeing; } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("截信人缺少生命组件");
            // 旧邮包没有耐久：这里只判「背包槽里是不是它」。配装失败就没处藏赃物，整场不劫包。
            mailbagEquipped = !SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(character, "Backpack"), BossRushItemIds.SkyIslandOldMailbag);
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            if (mailbagEquipped)
                Announce("截信人拍了拍鼓囊囊的旧邮包，眼睛盯上了你的背包。",
                    "The Waylayer pats its bulging old mailbag and eyes the pack on your back.", false);
            else
                Announce("截信人从倒挂的邮亭里探出头，上下打量着你。",
                    "The Waylayer leans out of the upturned post hut and sizes you up.", false);
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            float now = Time.time;
            nextTick = now + TickInterval;
            // 加速按时摘放在会话判定之前：会话收尾那几拍也不会让它一直跑得飞快。
            if (fleeing && now >= fleeUntil) EndFlee();
            if (context.Valid != null && !context.Valid()) return;
            if (health.IsDead) return;
            TickDrop();
            if (!mailbagEquipped || dropped || snatching || coopSnatchPending || snatches >= SkyIslandBossRules.SnatchMax || now < snatchReadyAt) return;
            CharacterMainControl player = BossRushCoop.GetCombatTarget(boss.transform.position);
            if (player == null || player.Health == null || player.Health.IsDead) return;
            float range = SkyIslandBossRules.SnatchRange;
            if ((player.transform.position - boss.transform.position).sqrMagnitude > range * range) return;
            int slotIndex;
            if ((!BossRushCoop.IsConnected || player == CharacterMainControl.Main) && BestTarget(player, out slotIndex) == null) return;
            StartCoroutine(SnatchRoutine(player));
        }

        // ====================================================================
        // 劫包
        // ====================================================================

        private IEnumerator SnatchRoutine(CharacterMainControl player)
        {
            bool coop = BossRushCoop.IsConnected;
            string asset = coop ? PlayerAsset(player) : null;
            snatching = true;
            try
            {
                snatchRing = SkyIslandBossForge.CreateGroundRing(context.Root, boss.transform.position);
                snatchRing.gameObject.name = "SkyIslandSnatchRing";
                SkyIslandBossForge.SetRing(snatchRing, SkyIslandBossRules.SnatchReach, 0f, SnatchTint);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 截信人伸手圈失败：" + e.Message);
                DestroyRing();
            }
            // 圈画不出来就不伸手：没有预警的劫包不公平。
            if (snatchRing == null)
            {
                snatching = false;
                snatchReadyAt = Time.time + SkyIslandBossRules.SnatchCooldown;
                yield break;
            }
            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：伸手只会更慢。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.SnatchTelegraph, SkyIslandBossGearWorn.WorldEarmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted() && !dropped)
            {
                // 圈跟着它的脚走：伸手够得着的就是它此刻身边这一圈。半径画的是结算判定 SnatchReach，不是起手距离（发版审查 B-02）。
                SkyIslandBossForge.PlaceRing(snatchRing, context.Root, boss.transform.position);
                SkyIslandBossForge.SetRing(snatchRing, SkyIslandBossRules.SnatchReach, Mathf.Clamp01((Time.time - started) / telegraph), SnatchTint);
                yield return null;
            }
            DestroyRing();
            if (!Aborted() && !dropped && (!coop || (BossRushCoop.IsHost && asset == PlayerAsset(player)))) ResolveSnatch(player);
            snatching = false;
        }

        /// <summary>伸手结束的结算：够不着就抓空；够得着就抢一件、闪身、加速。协程外的普通方法，异常就地吞掉不打断协程。</summary>
        private void ResolveSnatch(CharacterMainControl player)
        {
            float now = Time.time;
            float reach = SkyIslandBossRules.SnatchReach;
            bool inReach = player != null && player.Health != null && !player.Health.IsDead &&
                (player.transform.position - boss.transform.position).sqrMagnitude <= reach * reach;
            if (!inReach)
            {
                snatchReadyAt = now + SkyIslandBossRules.SnatchCooldown * 0.5f;
                if (missAnnounced) return;
                missAnnounced = true;
                Announce("截信人伸手抓了个空。", "The Waylayer grabs at empty air.", false);
                return;
            }
            if (BossRushCoop.IsHost)
            {
                coopSnatchPending = CoopWaylayerEscrow.Current != null && CoopWaylayerEscrow.Current.Begin(this, player, boss);
                if (!coopSnatchPending) snatchReadyAt = now + SkyIslandBossRules.SnatchCooldown * 0.5f;
                return;
            }
            int typeId;
            if (!TrySteal(player, out typeId))
            {
                // 伸手这几拍里能偷的东西没了，或者它自己背包满了：当作没抓到，不算次数。
                snatchReadyAt = now + SkyIslandBossRules.SnatchCooldown * 0.5f;
                return;
            }
            FinishSnatch(player, typeId);
        }

        private void FinishSnatch(CharacterMainControl player, int typeId)
        {
            snatches++;
            snatchReadyAt = Time.time + SkyIslandBossRules.SnatchCooldown;
            try { Flee(player); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 截信人闪身失败：" + e.Message); }
            Say(string.Format(L10n.T("截信人抢走了你的{0}，闪到了邮亭另一头：追上去打倒它，东西就在它身上。",
                "The Waylayer snatched your {0} and flicked to the far end of the hut: run it down, it's got your stuff."),
                SkyIslandItemRules.Name(typeId)), !snatchAnnounced);
            snatchAnnounced = true;
        }

        private static string PlayerAsset(CharacterMainControl player)
        {
            BossRushCoopPlayer member = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(player));
            return member != null && member.IsConnected && member.Character == player ? member.AssetId : null;
        }

        internal bool CanAcceptCoopSnatch
        {
            get { return !Aborted() && !dropped && mailbagEquipped && snatches < SkyIslandBossRules.SnatchMax; }
        }

        internal bool AcceptCoopSnatch(Item item, CharacterMainControl player)
        {
            if (!CanAcceptCoopSnatch || item == null) return false;
            Inventory bag = BossInventory();
            if (bag == null) return false;
            try { bag.AddItem(item); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 联机赃物入包通知失败：" + e.Message); }
            if (item.InInventory != bag) return false;
            stolen.Add(item);
            // 实物已入包即为提交；表现/通知失败不能把已经提交的扣物错误地返还一份。
            try { FinishSnatch(player, item.TypeID); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 联机劫包提交后表现失败：" + e.Message); }
            return true;
        }

        internal void CompleteCoopSnatch(bool success)
        {
            coopSnatchPending = false;
            if (!success) snatchReadyAt = Time.time + SkyIslandBossRules.SnatchCooldown * 0.5f;
        }

        private bool TrySteal(CharacterMainControl player, out int typeId)
        {
            typeId = 0;
            Inventory bag = BossInventory();
            Inventory pack = player.CharacterItem != null ? player.CharacterItem.Inventory : null;
            if (bag == null || pack == null) return false;
            int slotIndex;
            // 伸手这 0.8 秒里背包可能变过：重新挑一次。
            Item item = BestTarget(player, out slotIndex);
            if (item == null) return false;
            typeId = item.TypeID;
            try
            {
                return item.Stackable && item.StackCount > 1 ? TakeOneFromStack(item, bag) : TakeWhole(item, slotIndex, pack, bag, player);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 截信人劫包失败：" + e.Message);
                return false;
            }
        }

        /// <summary>整堆只拿一个：先问 prefab、复制一个放进它背包，放稳之后再给玩家那堆减一；减不下去就把复制品收回销毁。</summary>
        private bool TakeOneFromStack(Item stack, Inventory bag)
        {
            int typeId = stack.TypeID;
            Item prefab = null;
            try { prefab = ItemAssetsCollection.GetPrefab(typeId); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物 prefab 查询失败：" + e.Message); }
            // 缺资源时官方 InstantiateSync 回一个带同样 TypeID 的空壳，回读分辨不出来：必须先问 prefab（contracts §7.1）。
            if (prefab == null) return false;
            Item copy = null;
            try { copy = ItemAssetsCollection.InstantiateSync(typeId); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物复制失败：" + e.Message); }
            if (copy == null || copy.TypeID != typeId)
            {
                if (copy != null) copy.DestroyTree();
                return false;
            }
            bool added = false;
            try
            {
                copy.StackCount = 1;
                added = bag.AddItem(copy);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物入包通知失败：" + e.Message); }
            // AddAt 可能已经放进去、只是末尾通知抛异常：按实际归属判定。
            if (!added) added = copy.InInventory == bag;
            if (!added)
            {
                if (!SkyIslandInventoryTransaction.HasOwner(copy)) copy.DestroyTree();
                return false;
            }
            int before = stack.StackCount;
            try { stack.StackCount = before - 1; }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 玩家那堆扣减通知失败：" + e.Message); }
            if (stack.StackCount == before)
            {
                // 没扣下来：不许凭空多出一个，把复制品收回销毁，玩家那堆原样不动。
                try { copy.Detach(); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 复制品收回失败：" + e.Message); }
                SkyIslandInventoryTransaction.DestroyUnowned(copy);
                return false;
            }
            stolen.Add(copy);
            return true;
        }

        /// <summary>单件整件拿走；放不进它背包就原样退回玩家（<see cref="GiveBack"/>）。</summary>
        private bool TakeWhole(Item item, int slotIndex, Inventory pack, Inventory bag, CharacterMainControl player)
        {
            try { item.Detach(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 从玩家背包取出失败：" + e.Message); }
            // 没取出来就原样留在玩家那里；被官方回调接到了别处，也不再碰它。
            if (SkyIslandInventoryTransaction.HasOwner(item)) return false;
            bool added = false;
            try { added = bag.AddItem(item); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物入包通知失败：" + e.Message); }
            if (!added) added = item.InInventory == bag;
            if (added)
            {
                stolen.Add(item);
                return true;
            }
            GiveBack(item, slotIndex, pack, player);
            return false;
        }

        /// <summary>退回的阶梯：原槽空着 AddAt、否则 AddItem、再不行官方投递（不合堆、不寄仓库），最后落在玩家脚下。每一步先看有没有主，绝不投递两次。</summary>
        internal static void GiveBack(Item item, int slotIndex, Inventory pack, CharacterMainControl player)
        {
            if (item == null || item.IsBeingDestroyed || SkyIslandInventoryTransaction.HasOwner(item)) return;
            try
            {
                if (slotIndex >= 0 && slotIndex < pack.Capacity && pack.GetItemAt(slotIndex) == null) pack.AddAt(item, slotIndex);
                if (!SkyIslandInventoryTransaction.HasOwner(item)) pack.AddItem(item);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物退回玩家背包失败：" + e.Message); }
            if (SkyIslandInventoryTransaction.HasOwner(item)) return;
            try { ItemUtilities.SendToPlayer(item, true, false); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物投递失败：" + e.Message); }
            if (SkyIslandInventoryTransaction.HasOwner(item)) return;
            try { item.Drop(player, true); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 赃物落地失败：" + e.Message); }
        }

        /// <summary>玩家背包里最值钱的那件可偷物（倒着走、不分配）；拿在手上的那件不偷。没有返回 null。</summary>
        internal static Item BestTarget(CharacterMainControl player, out int slotIndex)
        {
            slotIndex = -1;
            Inventory pack = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
            List<Item> content = pack != null ? pack.Content : null;
            if (content == null) return null;
            Item best = null;
            int bestScore = -1;
            for (int i = content.Count - 1; i >= 0; i--)
            {
                Item item = content[i];
                if (item == null || item.IsBeingDestroyed) continue;
                int score = SkyIslandBossRules.StealScore(item.TypeID);
                if (score <= bestScore) continue;
                if (item.AgentUtilities != null && item.AgentUtilities.ActiveAgent != null) continue;
                best = item;
                bestScore = score;
                slotIndex = i;
            }
            return best;
        }

        /// <summary>
        /// 闪到两处逃点里离玩家远的那个（落不到地面就不闪、只加速），加速 FleeSeconds 秒。
        /// 不喊它重新认人：它本来就在打你，再强行盯上会让它顶着加速直线折回来，「追到逃点打」就没了。
        /// </summary>
        private void Flee(CharacterMainControl player)
        {
            Vector3 a, b;
            bool hasA = SkyIslandBossProps.TryMarker(context.Root, SkyIslandBossRules.WaylayerFleeA, out a);
            bool hasB = SkyIslandBossProps.TryMarker(context.Root, SkyIslandBossRules.WaylayerFleeB, out b);
            if (hasA || hasB)
            {
                Vector3 from = player != null ? player.transform.position : boss.transform.position;
                Vector3 target = hasA && hasB
                    ? (SkyIslandBossRules.FartherPoint(from.x, from.z, a.x, a.z, b.x, b.z) == 0 ? a : b)
                    : (hasA ? a : b);
                Vector3 ground;
                // 宁可少闪一次，也不把它塞进墙里或悬在半空。
                if (SkyIslandBossProps.SnapNear(target, context, 0.45f, 2f, out ground))
                {
                    Vector3 blinkFrom = boss.transform.position;
                    // 闪身留一道风痕和两团扬尘：看得出它带着东西跑去了哪头（VB-25，纯表现）。
                    if (SkyIslandBossProps.Teleport(boss, ground + Vector3.up * 0.1f, null))
                    {
                        SkyIslandImpactFx.Puff(context.Root, blinkFrom, 0.5f, 6);
                        SkyIslandImpactFx.Streak(context.Root, blinkFrom, ground, SnatchTint);
                        SkyIslandImpactFx.Puff(context.Root, ground, 0.5f, 6);
                    }
                }
            }
            else Debug.LogWarning("[SkyIslandBoss] 倒挂邮亭的逃点标记都不在，截信人只加速不闪身");
            RuntimeStatModifierTracker.RemoveAll(fleeRecords, "SkyIslandWaylayerFlee");
            RuntimeStatModifierTracker.TryAdd(boss, ZombieModeStatNames.WalkSpeed, SkyIslandBossRules.FleeSpeedBonus, fleeSource, fleeRecords, "SkyIslandWaylayerFlee");
            RuntimeStatModifierTracker.TryAdd(boss, ZombieModeStatNames.RunSpeed, SkyIslandBossRules.FleeSpeedBonus, fleeSource, fleeRecords, "SkyIslandWaylayerFlee");
            fleeing = true;
            fleeUntil = Time.time + SkyIslandBossRules.FleeSeconds;
        }

        private void EndFlee()
        {
            RuntimeStatModifierTracker.RemoveAll(fleeRecords, "SkyIslandWaylayerFlee");
            fleeing = false;
        }

        // ====================================================================
        // 旧邮包是它的胆
        // ====================================================================

        private void TickDrop()
        {
            if (dropped || !mailbagEquipped) return;
            float max = health.MaxHealth;
            if (max <= 0f || health.CurrentHealth / max >= SkyIslandBossRules.WaylayerDropBelow) return;
            dropped = true;
            if (DropStolen() > 0)
                Announce("截信人慌了，把抢来的东西丢在了脚下。", "The Waylayer panics and drops what it stole at its feet.", false);
            else
                Announce("截信人慌了神，捂紧邮包，再也不敢伸手。", "The Waylayer loses its nerve, clutches its mailbag and stops grabbing.", false);
        }

        /// <summary>把还在它背包里的赃物逐件丢成拾取物；返回丢下几件。丢到一半出错、东西悬空时塞回它背包（倒下照样进尸体箱）。</summary>
        private int DropStolen()
        {
            Inventory bag = BossInventory();
            int count = 0;
            for (int i = stolen.Count - 1; i >= 0; i--)
            {
                Item item = stolen[i];
                if (item == null || item.IsBeingDestroyed || bag == null || item.InInventory != bag) continue;
                try
                {
                    item.Detach();
                    item.Drop(boss, true);
                    count++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SkyIslandBoss] 截信人丢下赃物失败：" + e.Message);
                    if (item != null && !item.IsBeingDestroyed && !SkyIslandInventoryTransaction.HasOwner(item))
                    {
                        try { if (!bag.AddItem(item)) item.Drop(boss, true); }
                        catch (Exception again) { Debug.LogWarning("[SkyIslandBoss] 赃物塞回失败：" + again.Message); }
                    }
                }
            }
            stolen.Clear();
            return count;
        }

        private int CountHeld()
        {
            Inventory bag = BossInventory();
            if (bag == null) return 0;
            int count = 0;
            for (int i = 0; i < stolen.Count; i++)
            {
                Item item = stolen[i];
                if (item != null && !item.IsBeingDestroyed && item.InInventory == bag) count++;
            }
            return count;
        }

        private Inventory BossInventory()
        {
            return boss != null && boss.CharacterItem != null ? boss.CharacterItem.Inventory : null;
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void DestroyRing()
        {
            SkyIslandBossForge.ReleaseRing(snatchRing);
            snatchRing = null;
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        /// <summary>已经按语言拼好的字幕（带物品名的那句）。</summary>
        private void Say(string text, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(text, urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            // 抢来的东西还在它背包里：官方建尸体箱时照常收走，这里不碰。
            DestroyRing();
            EndFlee();
            snatching = false;
            Announce("截信人倒下了，邮包里的信撒了一地。", "The Waylayer falls, and the letters in its mailbag scatter across the ground.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position, damage);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            RuntimeStatModifierTracker.RemoveAll(fleeRecords, "SkyIslandWaylayerFlee");
            fleeing = false;
            DestroyRing();
            // 只清引用、不销毁物品：赃物此刻要么在它背包里（随尸体箱走），要么已经丢在地上归玩家。
            stolen.Clear();
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }

    /// <summary>个人运行时持有的劫包事务。首领销毁后仍重试确认/返还；只有资产本人操作实际背包。</summary>
    internal sealed class CoopWaylayerEscrow : IDisposable
    {
        [Serializable] internal sealed class Packet
        {
            public string Run, Id, Player, Asset, Boss;
            public int Generation;
            public ItemTreeData Tree;
        }
        [Serializable] internal sealed class Decision { public string Id, Asset; public bool Success; }
        private sealed class HostEntry
        {
            internal Packet Request;
            internal SkyIslandWaylayerChief Chief;
            internal bool Processing, Complete, Success;
        }
        private sealed class LocalEntry
        {
            internal Packet Response;
            internal Inventory Pack;
            internal Item Whole, Stack;
            internal int Slot;
            internal bool Reserved, Settled, Refunding;
        }
        private const string RequestTopic = "sky.waylayer.reserve", ResponseTopic = "sky.waylayer.reserved";
        private readonly Dictionary<string, HostEntry> host = new Dictionary<string, HostEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, LocalEntry> local = new Dictionary<string, LocalEntry>(StringComparer.Ordinal);
        private readonly IDisposable requestSubscription, responseSubscription;
        private readonly CoopPersonalDelivery<Decision> decisions;
        private float timer;
        private bool disposed;
        internal static CoopWaylayerEscrow Current { get; private set; }

        internal CoopWaylayerEscrow()
        {
            Current = this;
            requestSubscription = BossRushCoop.Register(RequestTopic, OnReserve);
            responseSubscription = BossRushCoop.Register(ResponseTopic, OnResponse);
            decisions = new CoopPersonalDelivery<Decision>("waylayer", ApplyDecision);
        }
        internal bool Begin(SkyIslandWaylayerChief chief, CharacterMainControl target, CharacterMainControl boss)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(target));
            string entity = BossRushCoop.GetEntityId(boss);
            if (!BossRushCoop.IsHost || chief == null || !chief.CanAcceptCoopSnatch || player == null || !player.IsAlive
                || string.IsNullOrEmpty(player.AssetId) || string.IsNullOrEmpty(entity)) return false;
            var packet = new Packet { Run = BossRushCoop.RunId, Id = Guid.NewGuid().ToString("N"), Player = player.Id,
                Asset = player.AssetId, Boss = entity, Generation = BossRushCoop.SceneGeneration };
            host.Add(packet.Id, new HostEntry { Request = packet, Chief = chief });
            timer = 0f;
            return true;
        }
        private static bool Same(Packet a, Packet b)
        {
            return a != null && b != null && a.Id == b.Id && a.Run == b.Run && a.Player == b.Player
                && a.Asset == b.Asset && a.Boss == b.Boss && a.Generation == b.Generation;
        }
        private static bool CurrentAsset(Packet packet)
        {
            return OwnAsset(packet) && packet.Player == BossRushCoop.LocalPlayerId;
        }
        private static bool OwnAsset(Packet packet)
        { return packet != null && packet.Asset == BossRushCoop.LocalAssetId; }
        private static bool CurrentWorld(HostEntry entry)
        {
            Packet packet = entry.Request;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(packet.Player);
            return BossRushCoop.IsHost && packet.Run == BossRushCoop.RunId && packet.Generation == BossRushCoop.SceneGeneration
                && entry.Chief != null && entry.Chief.CanAcceptCoopSnatch && player != null && player.IsAlive && player.AssetId == packet.Asset;
        }
        private void OnReserve(BossRushCoopMessage message)
        {
            if (!disposed && BossRushCoop.IsClient && message.IsFromHost) Reserve(message.Read<Packet>());
        }
        private void Reserve(Packet request)
        {
            if (request == null || string.IsNullOrEmpty(request.Id) || request.Id.Length > 64 || !CurrentAsset(request)
                || request.Run != BossRushCoop.RunId) return;
            LocalEntry entry;
            if (local.TryGetValue(request.Id, out entry))
            {
                if (Same(request, entry.Response) && !entry.Settled) SendResponse(entry.Response);
                return;
            }
            var response = new Packet { Run = request.Run, Id = request.Id, Player = request.Player, Asset = request.Asset,
                Boss = request.Boss, Generation = request.Generation };
            entry = new LocalEntry { Response = response };
            // 先登记失败结果，重复请求绝不改挑另一件，也不会二次扣物。
            local.Add(request.Id, entry);
            CharacterMainControl player = CharacterMainControl.Main;
            CharacterMainControl chief = BossRushCoop.FindEntity(request.Boss);
            float reach = SkyIslandBossRules.SnatchReach;
            if (request.Generation != BossRushCoop.SceneGeneration || player == null || player.Health == null || player.Health.IsDead
                || chief == null || chief.Health == null || chief.Health.IsDead
                || (player.transform.position - chief.transform.position).sqrMagnitude > reach * reach)
            { SendResponse(response); return; }
            int slot;
            Item item = SkyIslandWaylayerChief.BestTarget(player, out slot);
            if (item == null) { SendResponse(response); return; }
            try
            {
                ItemTreeData tree = ItemTreeData.FromItem(item);
                if (tree == null || tree.RootData == null) { SendResponse(response); return; }
                // 快照保留全部原变量与子物品；整堆只扣一个，Count 在冻结副本里改为 1。
                tree.RootData.variables.RemoveAll(value => value.Key == "Count");
                tree.RootData.variables.Add(new CustomData("Count", 1));
                entry.Pack = item.InInventory; entry.Slot = slot;
                if (item.Stackable && item.StackCount > 1)
                {
                    int before = item.StackCount;
                    try { item.StackCount = before - 1; } catch (Exception) { }
                    if (item.StackCount != before - 1) { SendResponse(response); return; }
                    entry.Stack = item;
                }
                else
                {
                    try { item.Detach(); } catch (Exception) { }
                    if (SkyIslandInventoryTransaction.HasOwner(item)) { SendResponse(response); return; }
                    entry.Whole = item;
                }
                entry.Reserved = true; response.Tree = tree;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 联机劫包预留失败：" + e.Message); }
            SendResponse(response);
        }
        private void SendResponse(Packet packet)
        {
            if (BossRushCoop.IsHost) ProcessResponse(packet.Player, packet);
            else BossRushCoop.SendToHost(ResponseTopic, packet);
        }
        private void OnResponse(BossRushCoopMessage message)
        {
            if (!disposed && BossRushCoop.IsHost) ProcessResponse(message.SenderId, message.Read<Packet>());
        }
        private async void ProcessResponse(string sender, Packet packet)
        {
            HostEntry entry;
            if (packet == null || sender != packet.Player || !host.TryGetValue(packet.Id, out entry) || !Same(packet, entry.Request)
                || entry.Processing || entry.Complete) return;
            entry.Processing = true;
            Item created = null;
            bool success = false;
            try
            {
                ItemTreeData tree = packet.Tree;
                if (CurrentWorld(entry) && tree != null && tree.RootData != null && tree.RootData.StackCount == 1
                    && tree.entries.Count <= 128 && SkyIslandBossRules.StealScore(tree.RootTypeID) >= 0
                    && ItemAssetsCollection.GetPrefab(tree.RootTypeID) != null)
                {
                    created = await ItemTreeData.InstantiateAsync(tree);
                    // 工厂续作必须再次核对同一房间、场景和本人资产，不能在离岛/切档后完成旧劫包。
                    if (!disposed && CurrentWorld(entry) && created != null && created.TypeID == tree.RootTypeID && created.StackCount == 1)
                    {
                        BossRushCoopPlayer player = BossRushCoop.FindPlayer(packet.Player);
                        success = entry.Chief.AcceptCoopSnatch(created, player.Character);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 联机赃物创建失败：" + e.Message); }
            finally
            {
                if (!success) SkyIslandInventoryTransaction.DestroyUnowned(created);
                entry.Processing = false; entry.Complete = true; entry.Success = success;
                if (entry.Chief != null) entry.Chief.CompleteCoopSnatch(success);
                QueueDecision(entry);
            }
        }
        private void QueueDecision(HostEntry entry)
        {
            if (disposed || entry.Request.Run != BossRushCoop.RunId) return;
            decisions.Deliver(entry.Request.Player, "snatch:" + entry.Request.Id,
                new Decision { Id = entry.Request.Id, Asset = entry.Request.Asset, Success = entry.Success }, entry.Request.Asset);
        }
        private bool ApplyDecision(Decision decision)
        {
            LocalEntry entry;
            if (decision == null || !local.TryGetValue(decision.Id, out entry) || decision.Asset != entry.Response.Asset
                || !CurrentAsset(entry.Response)) return false;
            if (entry.Settled) return true;
            if (!entry.Reserved) { entry.Settled = true; return true; }
            if (decision.Success)
            {
                SkyIslandInventoryTransaction.DestroyUnowned(entry.Whole);
                entry.Whole = null; entry.Settled = true;
                return true;
            }
            if (!entry.Refunding) Refund(entry);
            return entry.Settled;
        }
        private async void Refund(LocalEntry entry)
        {
            entry.Refunding = true;
            try
            {
                if (!OwnAsset(entry.Response)) return;
                Item stack = entry.Stack;
                if (stack != null && !stack.IsBeingDestroyed && stack.InInventory == entry.Pack)
                {
                    int before = stack.StackCount;
                    try { stack.StackCount = before + 1; } catch (Exception) { }
                    if (stack.StackCount == before + 1) { entry.Settled = true; return; }
                    return;
                }
                if (entry.Whole == null || entry.Whole.IsBeingDestroyed)
                    entry.Whole = await ItemTreeData.InstantiateAsync(entry.Response.Tree);
                // 切档期间晚到的原件仍由 escrow 持有；回到原资产后再交付，绝不发给新档主角。
                if (!OwnAsset(entry.Response) || entry.Whole == null) return;
                CharacterMainControl player = CharacterMainControl.Main;
                Inventory pack = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
                if (pack == null) return;
                SkyIslandWaylayerChief.GiveBack(entry.Whole, entry.Slot, pack, player);
                entry.Settled = SkyIslandInventoryTransaction.HasOwner(entry.Whole);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 联机赃物返还待重试：" + e.Message); }
            finally { entry.Refunding = false; }
        }
        internal void Tick(float delta)
        {
            if (disposed) return;
            decisions.Tick(delta);
            timer -= Mathf.Max(0f, delta);
            if (timer > 0f) return;
            timer = 0.5f;
            foreach (LocalEntry entry in new List<LocalEntry>(local.Values))
            {
                if (entry.Settled || entry.Refunding || !OwnAsset(entry.Response)) continue;
                if (!string.IsNullOrEmpty(BossRushCoop.RunId) && entry.Response.Run != BossRushCoop.RunId)
                { if (entry.Reserved) Refund(entry); else entry.Settled = true; }
            }
            if (!BossRushCoop.IsHost) return;
            foreach (HostEntry entry in new List<HostEntry>(host.Values))
            {
                if (entry.Processing) continue;
                if (entry.Complete) { QueueDecision(entry); continue; }
                if (!CurrentWorld(entry))
                { entry.Complete = true; if (entry.Chief != null) entry.Chief.CompleteCoopSnatch(false); QueueDecision(entry); continue; }
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(entry.Request.Player);
                if (player.IsLocal) Reserve(entry.Request);
                else BossRushCoop.SendToPlayer(player.Id, RequestTopic, entry.Request);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            // 主动卸载在原资产内归还尚未确认的预留，不删除或迁移任何存档。
            foreach (LocalEntry entry in local.Values)
                if (!entry.Settled && entry.Reserved && !entry.Refunding && OwnAsset(entry.Response)) Refund(entry);
            disposed = true;
            requestSubscription.Dispose(); responseSubscription.Dispose(); decisions.Dispose();
            host.Clear();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
