// ============================================================================
// PetNestHatchService.cs - 遗种巢孵化、凝蛋与命名（实施计划 步骤 5）
// ============================================================================
// 硬约束（tests/PetNestHatchCommitGuard.py 守卫）：
//   - **commit-before-reveal**：roll 出来的结果必须先落档，再交给演出层回放。
//     本文件因此**不得** import 任何 View 符号——孵化演出只读已 commit 的结果，
//     一次孵化在任何时点断电/崩溃都不会出现"演出播了但崽没进巢"。
//   - **fail-closed 不消耗蛋**：血脉 KV 读不到、或血脉不在目录里时，提示并
//     原样保留蛋（官方 preset 改名会让老蛋漂移，绝不能把玩家的蛋吞掉）。
//   - 孵化 roll 三层（出身天赋 ×2 / 性格 ×1 / 异色）**孵化即锁定**，不可洗。
//   - 凝蛋从玩家背包与仓库扫 500059 与遗魂账本扣减，两侧都走事务式检查。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>一次孵化的结果快照。演出层只读它，不参与 roll，也不写档。</summary>
    internal sealed class PetNestHatchResult
    {
        /// <summary>孵出的崽（已在巢内、已落档）。</summary>
        internal PetNestPetRecord Pet;
        /// <summary>血脉显示名。</summary>
        internal string LineageDisplayName;
        /// <summary>是否异色。</summary>
        internal bool Shiny;
        /// <summary>本次是否由遗魂凝蛋而来。</summary>
        internal bool FromCondense;
    }

    /// <summary>遗种巢孵化服务。数据层，无任何演出与 UI 依赖。</summary>
    internal static class PetNestHatchService
    {
        #region 出身天赋表（数值草案，待 owner 审定）

        /// <summary>
        /// 出身天赋候选。数据形态镜像官方 EndowmentEntry 的 ModifierDescription：
        /// statKey + 数值 + 是否百分比。
        /// 注意 PetCapcity 是**格子数**（官方 Mathf.RoundToInt），必须用常量加而不是百分比。
        /// </summary>
        private static readonly PetNestTalentEntry[] TalentPool =
        {
            MakeTalent("swift", "WalkSpeed", 8f, true),
            MakeTalent("sprinter", "RunSpeed", 8f, true),
            MakeTalent("tough", "MaxHealth", 12f, true),
            MakeTalent("fierce", "Damage", 5f, true),
            MakeTalent("packmule", "PetCapcity", 2f, false),
            MakeTalent("keeneye", "SightDistance", 10f, true),
            MakeTalent("thickhide", "BodyArmor", 6f, true),
            MakeTalent("nimble", "DodgeChance", 4f, true),
        };

        private static PetNestTalentEntry MakeTalent(string id, string statKey, float value, bool percentage)
        {
            PetNestTalentEntry entry = new PetNestTalentEntry();
            entry.id = id;
            entry.statKey = statKey;
            entry.value = value;
            entry.percentage = percentage;
            return entry;
        }

        #endregion

        #region 孵化

        /// <summary>
        /// 孵化一枚遗种蛋。成功时蛋被消耗、崽已入巢并落档，result 非 null。
        ///
        /// fail-closed：血脉读不到或不在目录里时返回 false 且**不消耗蛋**。
        /// </summary>
        internal static bool TryHatchEgg(Item egg, out PetNestHatchResult result, out string failureReasonId)
        {
            result = null;
            failureReasonId = null;

            if (egg == null)
            {
                failureReasonId = "egg_missing";
                return false;
            }

            string lineageKey = RelicEggConfig.ReadLineage(egg);
            if (string.IsNullOrEmpty(lineageKey))
            {
                // 蛋上没有血脉：可能是老档或异常物品，原样保留，不吞
                failureReasonId = "lineage_unknown";
                return false;
            }

            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(lineageKey, out lineage) || lineage == null)
            {
                // 官方 preset 改名导致血脉漂移：提示并保留蛋
                failureReasonId = "lineage_unknown";
                return false;
            }

            if (PetNestService.IsFull)
            {
                failureReasonId = "nest_full";
                return false;
            }

            PetNestPetRecord pet = RollNewPet(lineageKey);
            if (pet == null)
            {
                failureReasonId = "roll_failed";
                return false;
            }

            // 先入巢并落档（commit-before-reveal），成功之后才消耗蛋
            if (!PetNestService.TryAddPet(pet, out failureReasonId))
            {
                return false;
            }

            if (!TryConsumeEgg(egg))
            {
                // 蛋没消耗掉是严重的重复孵化风险：回滚刚入巢的崽
                string rollbackReason;
                PetNestService.TryRemovePet(pet.id, out rollbackReason);
                failureReasonId = "egg_consume_failed";
                return false;
            }

            result = new PetNestHatchResult();
            result.Pet = pet;
            result.LineageDisplayName = lineage.DisplayName;
            result.Shiny = pet.shiny;
            result.FromCondense = false;
            return true;
        }

        /// <summary>
        /// 三层 roll：出身天赋 ×2（不重复）/ 性格 ×1 / 异色。孵化即锁定，不可洗。
        /// </summary>
        internal static PetNestPetRecord RollNewPet(string lineageKey)
        {
            try
            {
                PetNestPetRecord pet = new PetNestPetRecord();
                pet.id = PetNestService.AllocatePetId();
                pet.lineageKey = lineageKey;
                pet.displayName = null;
                pet.birthTicks = DateTime.UtcNow.Ticks;
                pet.level = 1;
                pet.exp = 0;
                pet.state = (int)PetNestPetState.InNest;
                pet.talents = new List<PetNestTalentEntry>();
                pet.scars = new List<PetNestScarRecord>();

                // 异色
                pet.shiny = UnityEngine.Random.value < PetNestTuning.ShinyChance;

                // 性格
                string[] personalities = PetNestTuning.AllPersonalityIds;
                if (personalities != null && personalities.Length > 0)
                {
                    pet.personalityId = personalities[UnityEngine.Random.Range(0, personalities.Length)];
                }

                // 出身天赋：不重复抽 TalentRollCount 条
                RollTalents(pet);

                pet.Normalize();
                return pet;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 孵化 roll 失败: " + e.Message);
                return null;
            }
        }

        private static void RollTalents(PetNestPetRecord pet)
        {
            if (TalentPool.Length == 0) return;
            int want = Mathf.Min(PetNestTuning.TalentRollCount, TalentPool.Length);

            List<int> indices = new List<int>(TalentPool.Length);
            for (int i = 0; i < TalentPool.Length; i++) indices.Add(i);

            for (int picked = 0; picked < want && indices.Count > 0; picked++)
            {
                int slot = UnityEngine.Random.Range(0, indices.Count);
                PetNestTalentEntry template = TalentPool[indices[slot]];
                indices.RemoveAt(slot);

                PetNestTalentEntry copy = new PetNestTalentEntry();
                copy.id = template.id;
                copy.statKey = template.statKey;
                copy.value = template.value;
                copy.percentage = template.percentage;
                pet.talents.Add(copy);
            }
        }

        private static bool TryConsumeEgg(Item egg)
        {
            try
            {
                egg.DestroyTree();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 消耗遗种蛋失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 凝蛋（遗魂定向兑换）

        /// <summary>该血脉当前是否够凝一枚蛋。</summary>
        internal static bool CanCondense(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return false;
            if (!PetNestLineageCatalog.IsKnownLineage(lineageKey)) return false;
            return PetNestService.GetSouls(lineageKey) >= PetNestTuning.SoulsPerCondensedEgg;
        }

        /// <summary>
        /// 用遗魂定向凝成一枚该血脉的遗种蛋，直接孵化入巢（不产出实体蛋，
        /// 避免"凝出来的蛋放不进背包"这种半成品状态）。
        ///
        /// 事务式：先扣遗魂再入巢；入巢失败把遗魂退回去。
        /// </summary>
        internal static bool TryCondenseAndHatch(
            string lineageKey, out PetNestHatchResult result, out string failureReasonId)
        {
            result = null;
            failureReasonId = null;

            if (string.IsNullOrEmpty(lineageKey))
            {
                failureReasonId = "lineage_unknown";
                return false;
            }

            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(lineageKey, out lineage) || lineage == null)
            {
                failureReasonId = "lineage_unknown";
                return false;
            }
            if (PetNestService.IsFull)
            {
                failureReasonId = "nest_full";
                return false;
            }
            if (!CanCondense(lineageKey))
            {
                failureReasonId = "souls_insufficient";
                return false;
            }

            if (!PetNestService.TrySpendSouls(lineageKey, PetNestTuning.SoulsPerCondensedEgg, out failureReasonId))
            {
                return false;
            }

            PetNestPetRecord pet = RollNewPet(lineageKey);
            if (pet == null || !PetNestService.TryAddPet(pet, out failureReasonId))
            {
                // 入巢失败：把遗魂退回去，玩家不该为系统故障买单
                PetNestService.AddSouls(lineageKey, PetNestTuning.SoulsPerCondensedEgg, true);
                if (string.IsNullOrEmpty(failureReasonId)) failureReasonId = "roll_failed";
                return false;
            }

            result = new PetNestHatchResult();
            result.Pet = pet;
            result.LineageDisplayName = lineage.DisplayName;
            result.Shiny = pet.shiny;
            result.FromCondense = true;
            return true;
        }

        #endregion

        #region 蛋的收集（玩家背包 + 仓库）

        /// <summary>
        /// 扫玩家背包与仓库里的遗种蛋。结果按血脉分组返回，供孵化面板列表使用。
        /// 惰性调用：只在打开面板时扫一次，不常驻、不每帧扫。
        /// </summary>
        internal static List<Item> CollectAvailableEggs()
        {
            List<Item> found = new List<Item>();
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null)
                {
                    ScanInventoryForEggs(player.CharacterItem.Inventory, found);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 扫描玩家背包遗种蛋失败: " + e.Message);
            }

            try
            {
                ScanInventoryForEggs(PlayerStorage.Inventory, found);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 扫描仓库遗种蛋失败: " + e.Message);
            }

            return found;
        }

        private static void ScanInventoryForEggs(Inventory inventory, List<Item> found)
        {
            if (inventory == null || found == null) return;
            foreach (Item item in inventory)
            {
                if (item == null) continue;
                try
                {
                    if (item.TypeID == RelicEggConfig.TYPE_ID)
                    {
                        found.Add(item);
                    }
                }
                catch (Exception)
                {
                    // 单个物品读取失败不影响其余扫描
                }
            }
        }

        #endregion

        #region 命名（数据层）

        /// <summary>
        /// 给崽起名。空名恢复血脉默认名。名字只存在巢档里，不动本地化表。
        /// </summary>
        internal static bool TryRename(string petId, string displayName, out string failureReasonId)
        {
            return PetNestService.TryRenamePet(petId, SanitizeName(displayName), out failureReasonId);
        }

        /// <summary>
        /// 名字清洗：去首尾空白、限长。空串按"恢复默认名"处理。
        /// </summary>
        internal static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return null;
            const int MaxNameLength = 16;
            if (trimmed.Length > MaxNameLength) trimmed = trimmed.Substring(0, MaxNameLength);
            return trimmed;
        }

        #endregion
    }
}
