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

        /// <summary>巢容量（里程碑派生，见 <see cref="GetEffectiveNestCapacity"/>）。</summary>
        internal static int Capacity { get { return GetEffectiveNestCapacity(); } }

        /// <summary>
        /// 有效巢容量 = 基础 12 + 图鉴解锁血脉数每达一个里程碑 +4，封顶 24。
        ///
        /// 纯派生值，不写档：容量完全由图鉴解锁数决定，存档里的 capacity 字段只作
        /// 老档下限兼容（曾经手动扩过的档不会因此缩容）。
        /// 只在孵化/入巢/开面板这类低频时刻被读，UnlockedLineageCount 是一次线性扫描，
        /// 血脉量级只有几十条，无每帧成本。
        /// </summary>
        internal static int GetEffectiveNestCapacity()
        {
            int capacity = PetNestTuning.DefaultNestCapacity;
            try
            {
                int unlocked = PetNestMuseumStats.UnlockedLineageCount;
                int[] milestones = PetNestTuning.NestCapacityMilestoneLineageCounts;
                for (int i = 0; i < milestones.Length; i++)
                {
                    if (unlocked >= milestones[i]) capacity += PetNestTuning.NestCapacityMilestoneStep;
                }

                int stored = Nest.capacity;
                if (stored > capacity) capacity = stored;
            }
            catch (Exception)
            {
                // 图鉴不可读：退回基础容量，不因统计故障锁死玩家的巢
            }
            return capacity > PetNestTuning.MaxNestCapacity ? PetNestTuning.MaxNestCapacity : capacity;
        }

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
                // 用同一个派生容量，避免与 Capacity/IsFull 出现两套口径
                if (nest.pets.Count >= GetEffectiveNestCapacity())
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
                if (!Commit(out failureReasonId))
                {
                    // Store 失败时什么都没入队，内存必须一并回滚：
                    // 否则调用方（孵化）以为失败而不消耗蛋，内存里却多了一只崽，
                    // 反复点击就能一枚蛋孵出满巢。
                    nest.pets.Remove(pet);
                    return false;
                }
                return true;
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
                int index = nest.pets.IndexOf(pet);
                string previousDeployed = nest.deployedPetId;
                nest.pets.Remove(pet);
                if (string.Equals(nest.deployedPetId, petId, StringComparison.Ordinal))
                {
                    nest.deployedPetId = null;
                }
                if (!Commit(out failureReasonId))
                {
                    if (index >= 0 && index <= nest.pets.Count) nest.pets.Insert(index, pet);
                    else nest.pets.Add(pet);
                    nest.deployedPetId = previousDeployed;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "remove_pet_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 放生一只崽：移出巢并返还一部分同血脉遗魂，不刻碑（放生不是阵亡）。
        ///
        /// 单事务实现而非「TryRemovePet + AddSouls」两段提交：那样在第一段成功、
        /// 第二段失败时会把崽和遗魂一起弄丢。这里内存改动全部做完再一次 Commit，
        /// 失败则逐项回滚（崽插回原位、还席、遗魂减回）。
        /// 远征锁定期间拒绝放生，与 TryRemovePet 同一原因码。
        /// </summary>
        internal static bool TryReleasePet(string petId, out string failureReasonId)
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
                string lineageKey = pet.lineageKey;
                int index = nest.pets.IndexOf(pet);
                string previousDeployed = nest.deployedPetId;
                int previousSouls = GetSouls(lineageKey);

                nest.pets.Remove(pet);
                if (string.Equals(nest.deployedPetId, petId, StringComparison.Ordinal))
                {
                    nest.deployedPetId = null;
                }
                AddSouls(lineageKey, PetNestTuning.ReleaseSoulRefund, false);

                if (!Commit(out failureReasonId))
                {
                    // release 回滚标记：三项内存改动必须整体还原，否则崽没了遗魂也没进账
                    if (index >= 0 && index <= nest.pets.Count) nest.pets.Insert(index, pet);
                    else nest.pets.Add(pet);
                    nest.deployedPetId = previousDeployed;
                    SetSouls(lineageKey, previousSouls);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "release_pet_failed:" + e.GetType().Name;
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
            string previousName = pet.displayName;
            pet.displayName = string.IsNullOrEmpty(displayName) ? null : displayName.Trim();
            if (!Commit(out failureReasonId))
            {
                pet.displayName = previousName;
                return false;
            }
            return true;
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
                string previousDeployedId = nest.deployedPetId;
                int previousState = previous != null ? previous.state : 0;
                int petPreviousState = pet.state;

                if (previous != null && previous != pet
                    && previous.state == (int)PetNestPetState.Deployed)
                {
                    previous.state = (int)PetNestPetState.InNest;
                }
                nest.deployedPetId = pet.id;
                pet.state = (int)PetNestPetState.Deployed;

                if (!Commit(out failureReasonId))
                {
                    if (previous != null) previous.state = previousState;
                    pet.state = petPreviousState;
                    nest.deployedPetId = previousDeployedId;
                    return false;
                }
                return true;
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
                string previousDeployedId = nest.deployedPetId;
                int previousState = previous != null ? previous.state : 0;

                if (previous != null && previous.state == (int)PetNestPetState.Deployed)
                {
                    previous.state = (int)PetNestPetState.InNest;
                }
                nest.deployedPetId = null;

                if (!Commit(out failureReasonId))
                {
                    if (previous != null) previous.state = previousState;
                    nest.deployedPetId = previousDeployedId;
                    return false;
                }
                return true;
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
        /// <summary>
        /// 直接把某血脉的遗魂数写成给定值。**仅供事务回滚使用**：
        /// 正常增减一律走 AddSouls / TrySpendSouls，避免绕过它们的溢出与下限处理。
        /// </summary>
        private static void SetSouls(string lineageKey, int souls)
        {
            if (string.IsNullOrEmpty(lineageKey)) return;
            try
            {
                PetNestNestData nest = Nest;
                if (nest.soulLedger == null) return;
                for (int i = 0; i < nest.soulLedger.Count; i++)
                {
                    PetNestSoulLedgerEntry e = nest.soulLedger[i];
                    if (e != null && string.Equals(e.lineageKey, lineageKey, StringComparison.Ordinal))
                    {
                        e.souls = souls < 0 ? 0 : souls;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗魂回滚失败: " + e.Message);
            }
        }

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
                PetNestSoulLedgerEntry target = null;
                int previousSouls = 0;
                for (int i = 0; i < nest.soulLedger.Count; i++)
                {
                    PetNestSoulLedgerEntry e = nest.soulLedger[i];
                    if (e != null && string.Equals(e.lineageKey, lineageKey, StringComparison.Ordinal))
                    {
                        target = e;
                        previousSouls = e.souls;
                        e.souls -= count;
                        if (e.souls < 0) e.souls = 0;
                        break;
                    }
                }

                if (!Commit(out failureReasonId))
                {
                    // 扣了遗魂却没入队 = 玩家凭空少 240 遗魂，必须原样加回来
                    if (target != null) target.souls = previousSouls;
                    return false;
                }
                return true;
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
        /// 只入队不落盘。高频写入（每次击杀记遗魂）用它：逐次 SaveFile 会拖帧，
        /// pending 会被官方 OnCollectSaveData 与切图/回基地的 flush 写下去。
        /// </summary>
        internal static bool StageCommit()
        {
            try
            {
                PetNestNestData nest = Nest;
                nest.Normalize();
                return PetNestPersistence.Nest.Store(nest);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 巢状态入队失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把当前巢状态入队并请求落盘。写屏障 / StoreFaulted 时返回 false，
        /// 调用方据此给玩家「本次改动未能保存」的提示，而不是假装成功。
        /// </summary>
        /// <remarks>
        /// **成败以 Store（入队）为准，不以 flush 为准。** pending 一旦入队，
        /// 官方 OnCollectSaveData 与后续任意一次 flush 都会把它写下去；此时若因为
        /// flush 报错就返回 false，调用方会回滚内存，而磁盘上仍会落到"已提交"的那份，
        /// 两边永久分叉（例如遗魂已扣但玩家看到"凝蛋失败"）。
        /// 因此 flush 一律 best-effort，不影响返回值。
        /// </remarks>
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

                // best-effort：入队即视为已提交，落盘失败由协调器重试
                PetNestSaveCoordinator.RequestFlush();
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

    /// <summary>
    /// 远征与博物馆两个 store 的服务层访问口。
    ///
    /// 与 PetNestService 同处服务层（同一文件，共享"只有服务层能碰 PetNestPersistence"
    /// 的分层规则，见 tests/PetNestRuntimeModuleGuard.py）：玩法层一律经这里读写，
    /// 不直接触碰持久化。
    /// </summary>
    internal static class PetNestPersistenceAccess
    {
        /// <summary>远征数据（首次访问触发加载）。永不为 null。</summary>
        internal static PetNestExpeditionData Expedition
        {
            get
            {
                try
                {
                    PetNestExpeditionData data = PetNestPersistence.Expedition.Current;
                    return data ?? PetNestCodec.CreateDefaultExpedition();
                }
                catch (Exception)
                {
                    return PetNestCodec.CreateDefaultExpedition();
                }
            }
        }

        /// <summary>博物馆数据（首次访问触发加载）。永不为 null。</summary>
        internal static PetNestMuseumData Museum
        {
            get
            {
                try
                {
                    PetNestMuseumData data = PetNestPersistence.Museum.Current;
                    return data ?? PetNestCodec.CreateDefaultMuseum();
                }
                catch (Exception)
                {
                    return PetNestCodec.CreateDefaultMuseum();
                }
            }
        }

        /// <summary>把远征数据入队（不落盘）。落盘由协调器统一触发。</summary>
        internal static bool StageExpedition()
        {
            try
            {
                PetNestExpeditionData data = Expedition;
                data.Normalize();
                return PetNestPersistence.Expedition.Store(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征数据入队失败: " + e.Message);
                return false;
            }
        }

        /// <summary>三个 key 现在是否都能写。多 key 事务的前置检查。</summary>
        internal static bool CanStoreAll { get { return PetNestPersistence.CanStoreAll; } }

        /// <summary>任一 key 处于写屏障（用于区分屏障与故障两种失败原因）。</summary>
        internal static bool HasAnyWriteBarrier { get { return PetNestPersistence.HasAnyWriteBarrier; } }

        /// <summary>丢弃三个 key 的 pending。多 key 事务中途失败时的回滚。</summary>
        internal static void DiscardAllPending() { PetNestPersistence.DiscardAllPending(); }

        /// <summary>丢弃三个 key 的内存缓存，下次访问从当前存档槽重新加载。</summary>
        internal static void ResetCachesForSlotReload() { PetNestPersistence.ResetCachesForSlotReload(); }

        /// <summary>把博物馆数据入队（不落盘）。</summary>
        internal static bool StageMuseum()
        {
            try
            {
                PetNestMuseumData data = Museum;
                data.Normalize();
                return PetNestPersistence.Museum.Store(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 博物馆数据入队失败: " + e.Message);
                return false;
            }
        }
    }
}
