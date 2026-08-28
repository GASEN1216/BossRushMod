// ============================================================================
// PetNestDownedHandler.cs - 随从重伤退场与战痕（实施计划 步骤 7）
// ============================================================================
// 「你在场，它不死」的落地：
//   致死钳制链第四消费者把随从的血钳到 1，然后**登记一次待处理退场**；
//   真正的退场发生在下一个宿主 tick，而不是 Hurt 内部——在 Health.Hurt 的调用栈里
//   销毁角色、写存档、改场景状态是宿主崩溃的经典配方。
//
// 退场四步：
//   1) 立刻上短无敌（避免钳血后同帧被连击真的打死）；
//   2) 下一 tick 落 ScarRecord（时间 / 地图 / 凶手）+ 一条永久小 Modifier；
//   3) 把崽标记为 Downed（本局不再出场，回基地自动复位）；
//   4) 走 PetNestCompanionRuntime.CleanupOnce 统一清理（还席 / 摘容量 / 回收角色）。
//
// 战痕纪律（存档体积防线）：
//   每崽战痕上限 PetNestTuning.MaxScarsPerPet，溢出时最旧的一条并进
//   mergedOldScarCount 计数，不无限增长。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>随从重伤退场处理器。钳制命中登记，宿主 tick 执行。</summary>
    internal static class PetNestDownedHandler
    {
        #region 待处理登记

        private static readonly object _lock = new object();
        private static bool _downedPending;
        private static string _pendingKillerName;
        private static string _pendingPlace;
        private static int _downedCount;

        /// <summary>是否有待处理的重伤退场。</summary>
        internal static bool HasPendingDowned { get { return _downedPending; } }

        /// <summary>本次会话累计重伤退场次数（诊断用）。</summary>
        internal static int DownedCount { get { return _downedCount; } }

        /// <summary>
        /// 致死钳制命中回调。**只登记，不动场景状态**：这里仍在 Health.Hurt 的调用栈里。
        /// 唯一的同步副作用是上短无敌，因为它必须在同一帧生效才能挡住连击。
        ///
        /// 凶手取自 OnHurt 订阅记下的最后一次攻击者：CurrentHealth setter 拿不到
        /// DamageInfo，而给 Health.Hurt 的 Prefix 加参数会动到既有 guard 断言的签名。
        /// </summary>
        internal static void NotifyLethalClamped(Health health)
        {
            try
            {
                lock (_lock)
                {
                    if (_downedPending) return;
                    _downedPending = true;
                    _pendingKillerName = _lastAttackerName;
                    _pendingPlace = SafeSceneName();
                }

                if (health != null)
                {
                    // 短无敌：钳到 1 血之后如果同帧还挨打，还是会死
                    health.SetInvincible(true);
                }

                ModBehaviour.DevLog("[PetNest] 随从被打倒，登记重伤退场");
            }
            catch (Exception)
            {
                // 绝不打断宿主受伤流程
            }
        }

        #endregion

        #region 凶手记录（官方 Health.OnHurt 静态事件）

        private static bool _hurtSubscribed;
        private static string _lastAttackerName;

        /// <summary>当前是否订阅着 OnHurt。</summary>
        internal static bool IsHurtSubscribed { get { return _hurtSubscribed; } }

        /// <summary>
        /// 幂等订阅官方 Health.OnHurt 静态事件（AGENTS.md 4.6：私有 bool 防重复订阅）。
        ///
        /// 只在**随从在场期间**订阅：OnHurt 是全场热路径，常驻订阅等于给每一次伤害
        /// 加一个委托调用。随从离场立刻退订，不带崽时零开销。
        /// </summary>
        internal static void EnsureHurtSubscribed()
        {
            if (_hurtSubscribed) return;
            try
            {
                Health.OnHurt += HandleAnyHurt;
                _hurtSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 订阅 Health.OnHurt 失败，战痕凶手将记 unknown: "
                    + e.Message);
            }
        }

        /// <summary>幂等退订。随从离场、切图、宿主销毁都要调。</summary>
        internal static void ShutdownHurtSubscription()
        {
            if (!_hurtSubscribed) return;
            _hurtSubscribed = false;
            try
            {
                Health.OnHurt -= HandleAnyHurt;
            }
            catch (Exception)
            {
                // 退订失败也要把标记清掉，避免重复订阅越滚越多
            }
            _lastAttackerName = null;
        }

        /// <summary>
        /// 全场受伤回调。只对随从的 Health 记一笔攻击者名字，其余一律零分配早返。
        /// </summary>
        private static void HandleAnyHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                if (health == null) return;
                if (!PetNestCompanionAgent.IsCompanionHealth(health)) return;
                _lastAttackerName = ResolveKillerName(damageInfo.fromCharacter);
            }
            catch (Exception)
            {
                // 凶手记录失败只让战痕刻 unknown，绝不打断宿主受伤流程
            }
        }

        #endregion

        #region 宿主 tick 执行

        /// <summary>
        /// 宿主 tick：处理待办的重伤退场。无待办时 O(1) 早返。
        /// </summary>
        internal static void Tick()
        {
            if (!_downedPending) return;

            string killerName;
            string place;
            lock (_lock)
            {
                if (!_downedPending) return;
                _downedPending = false;
                killerName = _pendingKillerName;
                place = _pendingPlace;
                _pendingKillerName = null;
                _pendingPlace = null;
            }

            try
            {
                _downedCount++;
                string petId = PetNestCompanionRuntime.ActiveCompanionPetId;
                if (!string.IsNullOrEmpty(petId))
                {
                    PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                    if (pet != null)
                    {
                        AppendScar(pet, place, killerName);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 战痕落档失败: " + e.Message);
            }

            try
            {
                // 标记 Downed + 统一清理（还席 / 摘容量 / 回收角色）
                PetNestCompanionRuntime.NotifyDowned();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 重伤退场清理失败: " + e.Message);
            }
        }

        #endregion

        #region 战痕

        /// <summary>
        /// 追加一条战痕：时间 / 地图 / 凶手 + 一条永久小 Modifier。
        /// 超过上限时把最旧的一条并进 mergedOldScarCount，防存档无限增长。
        /// </summary>
        internal static void AppendScar(PetNestPetRecord pet, string place, string killerName)
        {
            if (pet == null) return;
            pet.Normalize();

            PetNestScarRecord scar = new PetNestScarRecord();
            scar.ticks = DateTime.UtcNow.Ticks;
            scar.place = string.IsNullOrEmpty(place) ? SafeSceneName() : place;
            scar.killer = string.IsNullOrEmpty(killerName) ? "unknown" : killerName;
            scar.statKey = PickScarStatKey(pet);
            scar.percent = PetNestTuning.ScarModifierPercent;

            pet.scars.Add(scar);

            while (pet.scars.Count > PetNestTuning.MaxScarsPerPet)
            {
                // 最旧的一条合并为"旧伤"计数：履历还在，存档不涨
                pet.scars.RemoveAt(0);
                pet.mergedOldScarCount++;
            }

            PetNestService.StageCommit();
        }

        /// <summary>
        /// 挑一条还没到叠加封顶的 stat 作为这道疤的落点。
        /// 全都封顶时回落到移速（履历优先，数值不再恶化）。
        /// </summary>
        private static string PickScarStatKey(PetNestPetRecord pet)
        {
            string[] candidates = { "WalkSpeed", "RunSpeed", "MaxHealth" };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (SumScarPercent(pet, candidates[i]) > PetNestTuning.ScarModifierCapPercent)
                {
                    return candidates[i];
                }
            }
            return candidates[0];
        }

        /// <summary>某个 stat 上已累计的战痕减益（负数）。</summary>
        internal static float SumScarPercent(PetNestPetRecord pet, string statKey)
        {
            if (pet == null || pet.scars == null || string.IsNullOrEmpty(statKey)) return 0f;
            float sum = 0f;
            for (int i = 0; i < pet.scars.Count; i++)
            {
                PetNestScarRecord s = pet.scars[i];
                if (s != null && string.Equals(s.statKey, statKey, StringComparison.Ordinal))
                {
                    sum += s.percent;
                }
            }
            return sum;
        }

        /// <summary>
        /// 某个 stat 上生效的战痕减益（已按叠加封顶钳制）。
        /// 随从入场时按它给幼体挂 Modifier。
        /// </summary>
        internal static float GetEffectiveScarPercent(PetNestPetRecord pet, string statKey)
        {
            float sum = SumScarPercent(pet, statKey);
            return Mathf.Max(sum, PetNestTuning.ScarModifierCapPercent);
        }

        #endregion

        #region 辅助

        private static string SafeSceneName()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>从 DamageInfo 解析凶手显示名。解析不出返回 null。</summary>
        internal static string ResolveKillerName(CharacterMainControl attacker)
        {
            if (attacker == null) return null;
            try
            {
                CharacterRandomPreset preset = attacker.characterPreset;
                if (preset != null && !string.IsNullOrEmpty(preset.DisplayName))
                {
                    return preset.DisplayName;
                }
                if (preset != null && !string.IsNullOrEmpty(preset.nameKey))
                {
                    return preset.nameKey;
                }
                return attacker.gameObject != null ? attacker.gameObject.name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownHurtSubscription();
            lock (_lock)
            {
                _downedPending = false;
                _pendingKillerName = null;
                _pendingPlace = null;
            }
            _downedCount = 0;
        }

        #endregion
    }
}
