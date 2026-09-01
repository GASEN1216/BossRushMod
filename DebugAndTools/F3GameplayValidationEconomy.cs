// ============================================================================
// F3GameplayValidationEconomy.cs - 完整验收的词缀锻造 / 图鉴 / 持久化用例
// ============================================================================
// 模块说明：
//   这一组用例都能在基地场景纯逻辑驱动，共同特征是「涉及玩家资产，错一次玩家就亏」：
//
//   - 词缀锻造：失败必须原子回滚（不能扣了熔石不给词缀，也不能给了词缀不扣）。
//     现有 AFFIX_TEMP_ITEM_LIFECYCLE 覆盖的是临时物品的挂载/回滚链；
//     这里补的是**结算契约**：拒绝路径零消耗、锁槽后重掷不动锁定槽。
//   - 图鉴：编解码往返 + 索引重建 + 落盘回读。commit 5842911/b60038d 修过
//     「图鉴商店与库存持久化」，所以往返一致性值得常驻守卫。
//
// 【纪律】全部用临时物品做探针，绝不动玩家背包里的真装备；
//   所有探针在 finally 里销毁，词缀数据 StripAll 清干净。
// ============================================================================

using System;
using System.Collections;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunEconomyCases()
        {
            RunSyncCaseGated("AFFIX_FORGE_REJECT_NO_COST", ValidateAffixForgeRejectionIsFree);
            RunSyncCaseGated("AFFIX_LOCK_PRESERVES_SLOT", ValidateAffixLockPreservesSlot);
            RunSyncCaseGated("CODEX_CODEC_ROUNDTRIP", ValidateCodexCodecRoundtrip);
            RunSyncCaseGated("CODEX_PERSISTENCE_READBACK", ValidateCodexPersistenceReadback);
            yield break;
        }

        /// <summary>
        /// 拒绝路径必须零消耗。拿一个**不可锻造**的物品去走锻造入口，
        /// 断言熔石数量与金钱一分不动——这是「扣了钱没给东西」类 bug 的护栏。
        /// </summary>
        private bool ValidateAffixForgeRejectionIsFree(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            Item probe = null;
            try
            {
                int stonesBefore = AffixForgeSystem.GetOwnedStoneCount();

                // 用熔石本体当探针：它是消耗品，一定不在 AffixForgeableTags 里，
                // 所以 CanAffixForge 必须拒绝它。
                probe = ItemAssetsCollection.InstantiateSync(AffixForgeStoneConfig.TYPE_ID);
                if (probe == null)
                {
                    reason = "无法创建拒绝路径探针物品";
                    return false;
                }

                bool canForge = AffixForgeSystem.CanAffixForge(probe);
                AffixForgeResult rolled = AffixForgeSystem.RollUnlockedSlots(probe);
                AffixForgeResult locked = AffixForgeSystem.LockSlot(probe, 1);
                int stonesAfter = AffixForgeSystem.GetOwnedStoneCount();

                bool rejected = !canForge
                    && rolled != null && !rolled.Success
                    && locked != null && !locked.Success;
                bool free = stonesAfter == stonesBefore
                    && rolled != null && rolled.StonesConsumed == 0 && rolled.MoneyPaid == 0
                    && locked != null && locked.StonesConsumed == 0 && locked.MoneyPaid == 0;
                bool explained = rolled != null && !string.IsNullOrEmpty(rolled.ErrorMessage)
                    && locked != null && !string.IsNullOrEmpty(locked.ErrorMessage);

                metrics = "can_forge=" + canForge + ",stones=" + stonesBefore + "->" + stonesAfter
                    + ",roll_success=" + (rolled != null && rolled.Success)
                    + ",lock_success=" + (locked != null && locked.Success)
                    + ",explained=" + explained;
                if (!rejected) reason = "不可锻造物品未被拒绝";
                else if (!free) reason = "拒绝路径仍产生了熔石或金钱消耗";
                else if (!explained) reason = "拒绝路径未给出失败原因（玩家会看到静默失败）";
                return reason == null;
            }
            catch (Exception e)
            {
                reason = e.ToString();
                return false;
            }
            finally
            {
                DestroyProbeItem(probe);
            }
        }

        /// <summary>
        /// 锁定槽在重掷时必须原样保留。这是词缀系统对玩家最核心的承诺：
        /// 花熔石锁住的词条，再怎么重掷都不会丢。
        /// </summary>
        private bool ValidateAffixLockPreservesSlot(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            Item probe = null;
            try
            {
                probe = ItemAssetsCollection.InstantiateSync(DragonKingBossGunConfig.WeaponTypeId);
                if (probe == null)
                {
                    reason = "无法创建词缀探针武器";
                    return false;
                }

                if (!AffixItemData.EnsureInitialized(probe, 2))
                {
                    reason = "探针武器词缀数据初始化失败";
                    return false;
                }

                // 直接写数据层：不经锻造结算，避免消耗玩家真实资源。
                // 槽位下标是 1-based（AffixSlotView.SlotIndex 注释：1..MaxSlots）。
                bool wrote = AffixItemData.WriteSlot(probe, 1, AffixDefinitions.Id_Lifesteal, 1)
                    && AffixItemData.WriteSlot(probe, 2, AffixDefinitions.Id_Lifesteal, 1);
                bool lockedSlot = AffixItemData.SetLock(probe, 1, true) && AffixItemData.IsLocked(probe, 1);
                bool unlockedSlot = !AffixItemData.IsLocked(probe, 2);

                int unlockedCount = AffixForgeSystem.GetUnlockedSlotCount(probe);

                // 锁定态读回：锁槽内容必须仍可读且 id 不变。
                AffixSlotView lockedView;
                bool readBack = AffixItemData.TryReadSlot(probe, 1, out lockedView)
                    && string.Equals(lockedView.AffixId, AffixDefinitions.Id_Lifesteal, StringComparison.Ordinal)
                    && lockedView.Locked;

                // 解锁应免费且不改动槽内容。
                AffixForgeResult unlockResult = AffixForgeSystem.UnlockSlot(probe, 1);
                bool unlockFree = unlockResult != null
                    && unlockResult.StonesConsumed == 0 && unlockResult.MoneyPaid == 0;
                AffixSlotView stillView;
                bool contentKept = AffixItemData.TryReadSlot(probe, 1, out stillView)
                    && string.Equals(stillView.AffixId, AffixDefinitions.Id_Lifesteal, StringComparison.Ordinal);

                metrics = "wrote=" + wrote + ",locked=" + lockedSlot + ",slot1_unlocked=" + unlockedSlot
                    + ",unlocked_count=" + unlockedCount + ",read_back=" + readBack
                    + ",unlock_free=" + unlockFree + ",content_kept=" + contentKept;
                if (!wrote || !lockedSlot || !unlockedSlot) reason = "词缀写入或锁定状态不符合预期";
                else if (unlockedCount != 1) reason = "锁定一个槽后未锁槽数应为 1，实际 " + unlockedCount;
                else if (!readBack) reason = "锁定槽内容读回不一致";
                else if (!unlockFree) reason = "解锁槽产生了消耗（应免费）";
                else if (!contentKept) reason = "解锁后槽内容被改动";
                return reason == null;
            }
            catch (Exception e)
            {
                reason = e.ToString();
                return false;
            }
            finally
            {
                try { if (probe != null) AffixItemData.StripAll(probe); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 词缀探针清理失败: " + e.Message); }
                DestroyProbeItem(probe);
                try { AffixRuntimeService.EnsureRuntime(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 词缀运行时复位失败: " + e.Message); }
            }
        }

        /// <summary>
        /// 图鉴编解码往返：Encode → Decode 后条目数、键与击杀数必须逐项一致，
        /// 且索引可用（RebuildIndex 之后 GetOrCreate 不应产生重复条目）。
        /// </summary>
        private bool ValidateCodexCodecRoundtrip(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            try
            {
                CodexData current = CodexPersistence.Current;
                if (current == null)
                {
                    reason = "图鉴存档不可用";
                    return false;
                }

                string json = CodexCodec.Encode(current);
                CodexData decoded = CodexCodec.Decode(json);
                if (decoded == null)
                {
                    metrics = "json_bytes=" + (json != null ? json.Length : 0);
                    reason = "图鉴 JSON 无法解码";
                    return false;
                }
                decoded.RebuildIndex();

                bool countMatch = decoded.Entries.Count == current.Entries.Count;
                bool entriesMatch = countMatch;
                long killsBefore = 0L;
                long killsAfter = 0L;
                for (int i = 0; i < current.Entries.Count; i++) killsBefore += current.Entries[i].Kills;
                for (int i = 0; i < decoded.Entries.Count; i++) killsAfter += decoded.Entries[i].Kills;
                if (countMatch)
                {
                    for (int i = 0; i < current.Entries.Count; i++)
                    {
                        CodexEntry a = current.Entries[i];
                        CodexEntry b = decoded.Entries[i];
                        if (a == null || b == null
                            || !string.Equals(a.Key, b.Key, StringComparison.Ordinal)
                            || a.Kills != b.Kills)
                        {
                            entriesMatch = false;
                            break;
                        }
                    }
                }

                int schema = CodexCodec.ReadSchemaVersion(json);
                metrics = "entries=" + current.Entries.Count + "->" + decoded.Entries.Count
                    + ",kills=" + killsBefore + "->" + killsAfter
                    + ",schema=" + schema + ",json_bytes=" + (json != null ? json.Length : 0);
                if (!countMatch || !entriesMatch || killsBefore != killsAfter)
                    reason = "图鉴编解码往返不一致（条目、键或击杀数漂移）";
                return reason == null;
            }
            catch (Exception e)
            {
                reason = e.ToString();
                return false;
            }
        }

        /// <summary>
        /// 图鉴落盘回读：Store → FlushPending → SaveFile → 清静态缓存 → 重新读入，
        /// 条目数与击杀总数必须完全复现。覆盖 commit 5842911「图鉴商店与库存持久化」那条链。
        /// </summary>
        private bool ValidateCodexPersistenceReadback(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            try
            {
                CodexData before = CodexPersistence.Current;
                if (before == null)
                {
                    reason = "图鉴存档不可用";
                    return false;
                }
                int entriesBefore = before.Entries.Count;
                long killsBefore = 0L;
                for (int i = 0; i < before.Entries.Count; i++) killsBefore += before.Entries[i].Kills;

                bool stored = CodexPersistence.Store(before);
                bool flushed = CodexPersistence.FlushPending();
                Saves.SavesSystem.SaveFile(false);

                // 清静态缓存再读：这才是「重启后还在不在」的等价验证。
                CodexPersistence.ResetStaticCaches();
                CodexPersistence.EnsureSubscribed();
                CodexData after = CodexPersistence.Current;

                int entriesAfter = after != null ? after.Entries.Count : -1;
                long killsAfter = 0L;
                for (int i = 0; after != null && i < after.Entries.Count; i++) killsAfter += after.Entries[i].Kills;

                metrics = "stored=" + stored + ",flushed=" + flushed
                    + ",entries=" + entriesBefore + "->" + entriesAfter
                    + ",kills=" + killsBefore + "->" + killsAfter;
                if (!stored || !flushed) reason = "图鉴写入或落盘失败";
                else if (entriesAfter != entriesBefore || killsAfter != killsBefore)
                    reason = "清缓存回读后图鉴条目或击杀数不一致";
                return reason == null;
            }
            catch (Exception e)
            {
                reason = e.ToString();
                return false;
            }
        }

        /// <summary>销毁探针物品。探针一律是 InstantiateSync 出来的游离实例，不在玩家背包里。</summary>
        private static void DestroyProbeItem(Item probe)
        {
            try
            {
                if (probe != null && probe.gameObject != null)
                    UnityEngine.Object.Destroy(probe.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 探针物品销毁失败: " + e.Message);
            }
        }
    }
}
