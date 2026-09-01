// ============================================================================
// PetNestProgressionService.cs - 遗种巢崽的等级与经验
// ============================================================================
// 设计稿只写了两句机制（「等级/天赋以 Modifier 形式加 PetCapcity」「满级后成年体不再成长」），
// 数值由 owner 2026-08-29 拍板：Lv10 封顶、每级 100 exp 线性，
// 经验来源＝进局存活归巢 +10 / 随从击杀 +2（单局封顶 +30）/ 远征存活 +25，
// 效果＝每 3 级给玩家 +1 格捡漏背包。存档字段 level/exp 早已就绪，无 schema 变更。
//
// 纪律：
//   - AddExp **只改内存，不落档**：调用方按自己的事务边界提交
//     （远征侧必须并进 CommitBoth，否则会破坏那条链的原子性）；
//   - 击杀订阅只在随从在场窗口内挂，幂等成对，热路径零分配；
//   - 只走 PetNestService / PetNestMuseumStats，不直接碰 PetNestPersistence（分层约束）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>崽的养成推进（经验、升级、生涯场次）。</summary>
    internal static class PetNestProgressionService
    {
        #region 状态

        private static bool _killSubscribed;

        /// <summary>本局已发放的击杀经验，用于单局封顶。回基地结算时清零。</summary>
        private static int _runKillExpGranted;

        /// <summary>本图已计过的受害者，防同一次死亡被多次派发（经验不可回滚）。</summary>
        private static readonly HashSet<Health> _countedVictims = new HashSet<Health>();

        #endregion

        #region 经验与升级

        /// <summary>
        /// 加经验并处理升级。**不落档**：调用方负责提交。
        /// 已满级时直接返回，成年体不再成长。
        /// </summary>
        internal static void AddExp(PetNestPetRecord pet, int amount)
        {
            if (pet == null || amount <= 0) return;
            try
            {
                if (pet.level >= PetNestTuning.PetMaxLevel)
                {
                    pet.exp = 0;
                    return;
                }

                pet.exp += amount;
                bool leveled = false;
                while (pet.exp >= PetNestTuning.PetExpPerLevel
                       && pet.level < PetNestTuning.PetMaxLevel)
                {
                    pet.exp -= PetNestTuning.PetExpPerLevel;
                    pet.level++;
                    leveled = true;
                }

                if (pet.level >= PetNestTuning.PetMaxLevel)
                {
                    pet.level = PetNestTuning.PetMaxLevel;
                    pet.exp = 0;
                }
                if (pet.exp < 0) pet.exp = 0;

                if (leveled)
                {
                    // 图鉴「最高养成等级」跟随；RecordLevel 自身只在更高时才写
                    PetNestMuseumStats.RecordLevel(pet);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 加经验失败: " + e.Message);
            }
        }

        /// <summary>当前是否已是成年体（纯派生，不占存档字段）。</summary>
        internal static bool IsAdult(PetNestPetRecord pet)
        {
            return pet != null && pet.level >= PetNestTuning.PetMaxLevel;
        }

        #endregion

        #region 归巢结算

        /// <summary>
        /// 回基地时结算这一局带崽的收益：存活归巢 +经验、生涯场次 +1。
        ///
        /// 受益者两类，天然不相交：
        ///   a) 仍在场上的随从（activeCompanionPetId）——必须在 CleanupOnce 清掉它之前读；
        ///   b) 重伤退场（Downed）的崽——必须在 RestoreDownedPetsOnReturnToBase 复位之前读。
        /// 因此本方法要挂在基地分支的最前面。
        /// </summary>
        internal static void SettleRunHomecoming(string activeCompanionPetId)
        {
            try
            {
                string transactionError;
                if (!PetNestPersistenceAccess.BeginTransaction(out transactionError)) return;
                List<PetNestPetRecord> settled = new List<PetNestPetRecord>();

                PetNestPetRecord active = PetNestService.TryGetPet(activeCompanionPetId);
                if (active != null) settled.Add(active);

                List<PetNestPetRecord> pets = PetNestService.Pets;
                if (pets != null)
                {
                    for (int i = 0; i < pets.Count; i++)
                    {
                        PetNestPetRecord p = pets[i];
                        if (p == null) continue;
                        if (p.state != (int)PetNestPetState.Downed) continue;
                        if (settled.Contains(p)) continue;
                        settled.Add(p);
                    }
                }

                if (settled.Count == 0)
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    ResetRunKillBudget();
                    return;
                }

                for (int i = 0; i < settled.Count; i++)
                {
                    PetNestPetRecord pet = settled[i];
                    AddExp(pet, PetNestTuning.PetExpHomecoming);
                    // 生涯场次的契约是「进局 + 远征」，此前只有远征侧在数
                    long next = (long)pet.careerCount + 1L;
                    pet.careerCount = next > int.MaxValue ? int.MaxValue : (int)next;
                }

                ResetRunKillBudget();

                string ignored;
                PetNestService.Commit(out ignored);
            }
            catch (Exception e)
            {
                PetNestPersistenceAccess.AbortTransaction();
                ModBehaviour.DevLog("[PetNest] 归巢结算失败: " + e.Message);
            }
        }

        #endregion

        #region 击杀经验

        /// <summary>幂等订阅死亡事件。只在随从入场后调用。</summary>
        internal static void EnsureKillTrackingSubscribed()
        {
            if (_killSubscribed) return;
            try
            {
                Health.OnDead += HandleAnyDead;
                _killSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 订阅 Health.OnDead 失败，随从击杀不计经验: "
                    + e.Message);
            }
        }

        /// <summary>幂等退订。与随从清理成对。</summary>
        internal static void ShutdownKillTracking()
        {
            if (!_killSubscribed) return;
            try
            {
                Health.OnDead -= HandleAnyDead;
            }
            catch (Exception)
            {
                // 退订失败也要清标志，避免二次退订
            }
            _killSubscribed = false;
        }

        /// <summary>切图时清受害者去重集（跨图的 Health 引用已死）。本局封顶计数不清。</summary>
        internal static void ClearSceneKillDedup()
        {
            _countedVictims.Clear();
        }

        private static void ResetRunKillBudget()
        {
            _runKillExpGranted = 0;
            _countedVictims.Clear();
        }

        /// <summary>
        /// 全局死亡回调。零分配早返链：没有随从、已达单局封顶、凶手不是随从、
        /// 死的就是随从自己，四种情况直接返回。
        /// </summary>
        private static void HandleAnyDead(Health health, DamageInfo info)
        {
            if (!PetNestCompanionAgent.IsCompanionArmed) return;
            if (_runKillExpGranted >= PetNestTuning.PetExpCompanionKillRunCap) return;
            if (health == null) return;

            try
            {
                CharacterMainControl companion = PetNestCompanionRuntime.CompanionCharacter;
                if (companion == null) return;
                if (info.fromCharacter == null || info.fromCharacter != companion) return;
                // 随从自己倒下不算战功
                if (PetNestCompanionAgent.IsCompanionHealth(health)) return;
                string transactionError;
                if (!PetNestPersistenceAccess.BeginTransaction(out transactionError)) return;
                if (!_countedVictims.Add(health))
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    return;
                }
                PetNestPetRecord pet = PetNestService.TryGetPet(
                    PetNestCompanionRuntime.ActiveCompanionPetId);
                if (pet == null)
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    return;
                }

                AddExp(pet, PetNestTuning.PetExpPerCompanionKill);
                // 战斗中只入队，物理落盘由协调器在基地统一触发
                if (PetNestService.StageCommit())
                {
                    _runKillExpGranted += PetNestTuning.PetExpPerCompanionKill;
                }
                else _countedVictims.Remove(health);
            }
            catch (Exception e)
            {
                PetNestPersistenceAccess.AbortTransaction();
                _countedVictims.Remove(health);
                ModBehaviour.DevLog("[PetNest] 随从击杀经验结算失败: " + e.Message);
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownKillTracking();
            _runKillExpGranted = 0;
            _countedVictims.Clear();
        }

        #endregion
    }
}
