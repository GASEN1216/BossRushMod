// ============================================================================
// PetNestService.cs - 遗种巢领域服务（实施计划 步骤 3）
// ============================================================================
// 职责：巢 CRUD / 出战席位 / 遗魂账本。是所有玩法层写巢状态的唯一入口。
//
// 纪律：
//   - 所有写操作走「改内存 -> Store 入队 -> 协调器落盘」三段，禁止玩法层直接碰
//     PetNestPersistence；
//   - 单席契约：出战席位至多一个 petId，设置新席位自动清旧席位；
//   - 远征锁定：state==OnExpedition 的崽不可出战、不可移除（结算才解锁）；
//   - 容量：超容不入巢，返回 false 由调用方给提示（不静默丢弃玩家的蛋）；
//   - 全程 no-throw；异常时返回失败而不是抛给玩法层。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>遗种巢领域服务。无实例，状态在 PetNestPersistence 的 store 里。</summary>
    internal static class PetNestService
    {
        #region 巢访问

        /// <summary>当前巢数据（首次访问触发加载）。永不为 null。</summary>
        internal static PetNestNestData Nest
        {
            get
            {
                try
                {
                    PetNestNestData data = PetNestPersistence.Nest.Current;
                    return data ?? PetNestCodec.CreateDefaultNest();
                }
                catch (Exception)
                {
                    return PetNestCodec.CreateDefaultNest();
                }
            }
        }

        /// <summary>巢里的崽（只读遍历；调用方不得修改返回的列表）。</summary>
        internal static List<PetNestPetRecord> Pets
        {
            get { return Nest.pets; }
        }

        /// <summary>巢容量。</summary>
        internal static int Capacity { get { return Nest.capacity; } }

        /// <summary>当前崽数量。</summary>
        internal static int PetCount
        {
            get
            {
                List<PetNestPetRecord> pets = Nest.pets;
                return pets != null ? pets.Count : 0;
            }
        }

        /// <summary>巢是否已满。</summary>
        internal static bool IsFull { get { return PetCount >= Capacity; } }

        /// <summary>按 id 查崽。查不到返回 null。</summary>
        internal static PetNestPetRecord TryGetPet(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            List<PetNestPetRecord> pets = Nest.pets;
            if (pets == null) return null;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord p = pets[i];
                if (p != null && string.Equals(p.id, petId, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        #endregion

        #region 巢 CRUD

        /// <summary>
        /// 入巢。超容返回 false（不静默丢弃玩家的蛋，由调用方给提示）。
        /// 成功后立即落档。
        /// </summary>
        internal static bool TryAddPet(PetNestPetRecord pet, out string failureReasonId)
        {
            failureReasonId = null;
            if (pet == null || string.IsNullOrEmpty(pet.id))
            {
                failureReasonId = "pet_invalid";
                return false;
            }

            try
            {
                PetNestNestData nest = Nest;
                if (nest.pets == null) nest.pets = new List<PetNestPetRecord>();
                if (nest.pets.Count >= nest.capacity)
                {
                    failureReasonId = "nest_full";
                    return false;
                }
                if (TryGetPet(pet.id) != null)
                {
                    failureReasonId = "pet_duplicate";
                    return false;
                }

                pet.Normalize();
                nest.pets.Add(pet);
                return Commit(out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "add_pet_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 入巢失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 移除崽（远征真死 / 玩家放生）。远征锁定期间拒绝移除。
        /// </summary>
        internal static bool TryRemovePet(string petId, out string failureReasonId)
        {
            failureReasonId = null;
            PetNestPetRecord pet = TryGetPet(petId);
            if (pet == null)
            {
                failureReasonId = "pet_not_found";
                return false;
            }
            if (pet.state == (int)PetNestPetState.OnExpedition)
            {
                failureReasonId = "pet_locked_by_expedition";
                return false;
            }

            try
            {
                PetNestNestData nest = Nest;
                nest.pets.Remove(pet);
                if (string.Equals(nest.deployedPetId, petId, StringComparison.Ordinal))
                {
                    nest.deployedPetId = null;
                }
                return Commit(out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "remove_pet_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>改名。空名表示恢复血脉默认名。</summary>
        internal static bool TryRenamePet(string petId, string displayName, out string failureReasonId)
        {
            failureReasonId = null;
            PetNestPetRecord pet = TryGetPet(petId);
            if (pet == null)
            {
                failureReasonId = "pet_not_found";
                return false;
            }
            pet.displayName = string.IsNullOrEmpty(displayName) ? null : displayName.Trim();
            return Commit(out failureReasonId);
        }

        /// <summary>崽的显示名：玩家起的名字优先，否则回落血脉名。</summary>
        internal static string GetPetDisplayName(PetNestPetRecord pet)
        {
            if (pet == null) return string.Empty;
            if (!string.IsNullOrEmpty(pet.displayName)) return pet.displayName;
            PetNestLineageInfo info;
            if (PetNestLineageCatalog.TryGet(pet.lineageKey, out info) && info != null
                && !string.IsNullOrEmpty(info.DisplayName))
            {
                return info.DisplayName;
            }
            return string.IsNullOrEmpty(pet.lineageKey) ? pet.id : pet.lineageKey;
        }

        /// <summary>生成一个新的崽 id（巢内唯一，随 nameSerial 递增）。</summary>
        internal static string AllocatePetId()
        {
            PetNestNestData nest = Nest;
            nest.nameSerial++;
            return "pet_" + nest.nameSerial.ToString() + "_" + DateTime.UtcNow.Ticks.ToString();
        }

        #endregion

        #region 出战席位（单席）

        /// <summary>当前出战席位的崽（无则 null）。</summary>
        internal static PetNestPetRecord DeployedPet
        {
            get { return TryGetPet(Nest.deployedPetId); }
        }

        /// <summary>是否有崽在出战席位上。</summary>
        internal static bool HasDeployedPet
        {
            get { return DeployedPet != null; }
        }

        /// <summary>
        /// 设置出战席位。单席契约：设置新席位自动把旧席位复位为 InNest。
        /// 远征锁定 / 本局已重伤退场的崽不可上席。
        /// </summary>
        internal static bool TrySetDeployedPet(string petId, out string failureReasonId)
        {
            failureReasonId = null;
            PetNestPetRecord pet = TryGetPet(petId);
            if (pet == null)
            {
                failureReasonId = "pet_not_found";
                return false;
            }
            if (pet.state == (int)PetNestPetState.OnExpedition)
            {
                failureReasonId = "pet_locked_by_expedition";
                return false;
            }
            if (pet.state == (int)PetNestPetState.Downed)
            {
                failureReasonId = "pet_downed";
                return false;
            }

            try
            {
                PetNestNestData nest = Nest;
                PetNestPetRecord previous = TryGetPet(nest.deployedPetId);
                if (previous != null && previous != pet
                    && previous.state == (int)PetNestPetState.Deployed)
                {
                    previous.state = (int)PetNestPetState.InNest;
                }
                nest.deployedPetId = pet.id;
                pet.state = (int)PetNestPetState.Deployed;
                return Commit(out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "set_deployed_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>清空出战席位（不带崽进局）。</summary>
        internal static bool ClearDeployedPet(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                PetNestNestData nest = Nest;
                PetNestPetRecord previous = TryGetPet(nest.deployedPetId);
                if (previous != null && previous.state == (int)PetNestPetState.Deployed)
                {
                    previous.state = (int)PetNestPetState.InNest;
                }
                nest.deployedPetId = null;
                return Commit(out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "clear_deployed_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 回基地时把「本局重伤退场」复位为在巢待命。幂等。
        /// </summary>
        internal static void RestoreDownedPetsOnReturnToBase()
        {
            try
            {
                PetNestNestData nest = Nest;
                List<PetNestPetRecord> pets = nest.pets;
                if (pets == null) return;
                bool changed = false;
                for (int i = 0; i < pets.Count; i++)
                {
                    PetNestPetRecord p = pets[i];
                    if (p != null && p.state == (int)PetNestPetState.Downed)
                    {
                        p.state = string.Equals(p.id, nest.deployedPetId, StringComparison.Ordinal)
                            ? (int)PetNestPetState.Deployed
                            : (int)PetNestPetState.InNest;
                        changed = true;
                    }
                }
                if (changed)
                {
                    string ignored;
                    Commit(out ignored);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 重伤状态复位失败: " + e.Message);
            }
        }

        #endregion

        #region 遗魂账本

        /// <summary>查该血脉已攒的遗魂数。</summary>
        internal static int GetSouls(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return 0;
            List<PetNestSoulLedgerEntry> ledger = Nest.soulLedger;
            if (ledger == null) return 0;
            for (int i = 0; i < ledger.Count; i++)
            {
                PetNestSoulLedgerEntry e = ledger[i];
                if (e != null && string.Equals(e.lineageKey, lineageKey, StringComparison.Ordinal))
                {
                    return e.souls;
                }
            }
            return 0;
        }

        /// <summary>
        /// 记账遗魂。count &lt;= 0 直接返回。commit=false 时只改内存，
        /// 由调用方在批量记账后统一落档（避免每次击杀都写盘）。
        /// </summary>
        internal static void AddSouls(string lineageKey, int count, bool commit)
        {
            if (string.IsNullOrEmpty(lineageKey) || count <= 0) return;
            try
            {
                PetNestNestData nest = Nest;
                if (nest.soulLedger == null) nest.soulLedger = new List<PetNestSoulLedgerEntry>();

                PetNestSoulLedgerEntry entry = null;
                for (int i = 0; i < nest.soulLedger.Count; i++)
                {
                    PetNestSoulLedgerEntry e = nest.soulLedger[i];
                    if (e != null && string.Equals(e.lineageKey, lineageKey, StringComparison.Ordinal))
                    {
                        entry = e;
                        break;
                    }
                }
                if (entry == null)
                {
                    entry = new PetNestSoulLedgerEntry();
                    entry.lineageKey = lineageKey;
                    nest.soulLedger.Add(entry);
                }

                long next = (long)entry.souls + count;
                entry.souls = next > int.MaxValue ? int.MaxValue : (int)next;

                if (commit)
                {
                    string ignored;
                    Commit(out ignored);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗魂记账失败: " + e.Message);
            }
        }

        /// <summary>扣减遗魂（凝蛋）。余额不足返回 false 且不扣。</summary>
        internal static bool TrySpendSouls(string lineageKey, int count, out string failureReasonId)
        {
            failureReasonId = null;
            if (string.IsNullOrEmpty(lineageKey) || count <= 0)
            {
                failureReasonId = "invalid_request";
                return false;
            }
            if (GetSouls(lineageKey) < count)
            {
                failureReasonId = "souls_insufficient";
                return false;
            }

            try
            {
                PetNestNestData nest = Nest;
                for (int i = 0; i < nest.soulLedger.Count; i++)
                {
                    PetNestSoulLedgerEntry e = nest.soulLedger[i];
                    if (e != null && string.Equals(e.lineageKey, lineageKey, StringComparison.Ordinal))
                    {
                        e.souls -= count;
                        if (e.souls < 0) e.souls = 0;
                        break;
                    }
                }
                return Commit(out failureReasonId);
            }
            catch (Exception e)
            {
                failureReasonId = "spend_souls_failed:" + e.GetType().Name;
                return false;
            }
        }

        #endregion

        #region 落档

        /// <summary>
        /// 把当前巢状态入队并请求落盘。写屏障 / StoreFaulted 时返回 false，
        /// 调用方据此给玩家「本次改动未能保存」的提示，而不是假装成功。
        /// </summary>
        internal static bool Commit(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                PetNestNestData nest = Nest;
                nest.Normalize();
                if (!PetNestPersistence.Nest.Store(nest))
                {
                    failureReasonId = PetNestPersistence.Nest.HasWriteBarrier
                        ? "save_write_barrier"
                        : "save_store_faulted";
                    return false;
                }
                string flushError;
                if (!PetNestSaveCoordinator.RequestFlush(out flushError))
                {
                    // deferred 不算失败：pending 保留，协调器 tick 会重试
                    if (!string.Equals(flushError, "flush_deferred_is_saving", StringComparison.Ordinal))
                    {
                        failureReasonId = flushError;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "commit_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 巢状态落档失败: " + e.Message);
                return false;
            }
        }

        #endregion
    }
}
