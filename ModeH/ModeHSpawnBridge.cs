using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一个 Mode H 临时角色的 runtime handle（设计提案 §19.3）。
    /// 角色、Health、clone preset 与登记状态同寿命：原版伤害与死亡路径仍会读取
    /// characterPreset，因此 clone 只能在角色回收、引用登记解除之后销毁。
    /// </summary>
    internal sealed class ModeHSpawnHandle
    {
        /// <summary>官方预设稳定 key。</summary>
        public string StableKey;
        /// <summary>本次使用的 runtime clone preset。</summary>
        public CharacterRandomPreset ClonePreset;
        /// <summary>生成出的角色。</summary>
        public CharacterMainControl Character;
        /// <summary>角色的 Health 组件。</summary>
        public Health Health;
        /// <summary>目标阵营。</summary>
        public Teams Team;
        /// <summary>是否已登记到额外死亡掉落抑制表。</summary>
        public bool SuppressionRegistered;
        /// <summary>是否已激活进入擂台。</summary>
        public bool Activated;
        /// <summary>计划槽位序号（敌军使用）。</summary>
        public int PlanSlotIndex;
        /// <summary>关联的 profile ID（我方选手使用）。</summary>
        public string ProfileId;
    }

    /// <summary>
    /// Mode H 生成桥（设计提案 §19.3、§25.1）。
    ///
    /// 冻结契约：
    /// - **不调用也不修改 Utilities/EnemySpawnCore.cs**：这里重写一份与其
    ///   HoldForExternalCommit 分支形状相同的 hold 逻辑（SetInvincible(true) + SetActive(false)），
    ///   是为“不把未证明的 early-hold 语义扩散到 Mode D/E/G”付出的已知重复成本；
    /// - 每次先用 Instantiate 克隆已审计 preset，在克隆上固定 team、aiCombatFactor=1f、
    ///   dropBoxOnDead=false，保持 specialAttachmentBases 为空并禁止 managed Boss；
    /// - 调用创建**之前**先把 clone preset 注册到 Mode H 额外死亡掉落抑制表；
    /// - 在 modeHStagingPos 创建，返回后的第一个同步步骤立即登记 Health/CharacterMainControl、
    ///   SetInvincible(true) 与 SetActive(false)；
    /// - 一律传 group=null、isLeader=false：AICharacterController.Update 会在 leader 与成员之间
    ///   双向同步 searchedEnemy，一旦成组，finish 类点火会被同组目标互相污染。
    /// </summary>
    internal static class ModeHSpawnBridge
    {
        #region 创建

        /// <summary>
        /// 在 staging 点创建一个隔离角色。返回的 handle 里角色已 inactive + invincible。
        /// 失败返回 null 并给出 failureReasonId。
        /// </summary>
        internal static async UniTask<ModeHSpawnHandle> CreateIsolatedAsync(
            CharacterRandomPreset auditedPreset,
            string stableKey,
            Teams team,
            Vector3 stagingPos,
            ModeHSpawnDiagnostics diagnostics)
        {
            if (auditedPreset == null || string.IsNullOrEmpty(stableKey))
            {
                if (diagnostics != null) diagnostics.RecordFailure(stableKey, "spawn_preset_null");
                return null;
            }

            ModeHSpawnHandle handle = new ModeHSpawnHandle();
            handle.StableKey = stableKey;
            handle.Team = team;

            CharacterRandomPreset clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(auditedPreset);
                clone.aiCombatFactor = 1f;
                clone.dropBoxOnDead = false;
                clone.team = team;
                // 必须设为 true：官方 Boss preset 默认 canDieIfNotRaidMap=false（为防止基地意外触发死亡），
                // 但 Mode H 认证需要在非 raid 图（如竞技场）可控击杀 Boss 并观测受伤/死亡事件。
                clone.canDieIfNotRaidMap = true;
                handle.ClonePreset = clone;
            }
            catch (Exception e)
            {
                if (diagnostics != null)
                {
                    diagnostics.RecordFailure(stableKey, "spawn_clone_failed:" + e.GetType().Name);
                }
                DestroyClone(clone);
                return null;
            }

            // 创建调用之前登记抑制表：死亡帧的额外掉落 handler 必须已经知道这个 preset
            try
            {
                ModeHDeathSuppressionRegistry.RegisterPreset(clone);
                handle.SuppressionRegistered = true;
            }
            catch (Exception e)
            {
                if (diagnostics != null)
                {
                    diagnostics.RecordFailure(stableKey, "spawn_suppression_failed:" + e.GetType().Name);
                }
                DestroyClone(clone);
                return null;
            }

            if (diagnostics != null) diagnostics.BeginCreateWindow(stableKey);

            CharacterMainControl character = null;
            try
            {
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await clone.CreateCharacterAsync(
                    stagingPos, Vector3.forward, relatedScene, null, false);
            }
            catch (Exception e)
            {
                if (diagnostics != null)
                {
                    diagnostics.EndCreateWindow(stableKey);
                    diagnostics.RecordFailure(stableKey, "spawn_create_exception:" + e.GetType().Name);
                }
                ModeHDeathSuppressionRegistry.UnregisterPreset(clone);
                DestroyClone(clone);
                return null;
            }

            // 创建返回后的第一个同步步骤：登记引用并立即隔离
            if (character == null)
            {
                if (diagnostics != null)
                {
                    diagnostics.EndCreateWindow(stableKey);
                    diagnostics.RecordFailure(stableKey, "spawn_create_null");
                }
                ModeHDeathSuppressionRegistry.UnregisterPreset(clone);
                DestroyClone(clone);
                return null;
            }

            handle.Character = character;
            try
            {
                handle.Health = character.Health;
            }
            catch (Exception)
            {
                handle.Health = null;
            }

            try
            {
                ModeHDeathSuppressionRegistry.RegisterCharacter(handle.Health, character);
                if (handle.Health != null)
                {
                    handle.Health.SetInvincible(true);
                }
                character.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                if (diagnostics != null)
                {
                    diagnostics.EndCreateWindow(stableKey);
                    diagnostics.RecordFailure(stableKey, "spawn_isolate_failed:" + e.GetType().Name);
                }
                Recycle(handle);
                return null;
            }

            if (diagnostics != null) diagnostics.EndCreateWindow(stableKey);
            return handle;
        }

        #endregion

        #region 提交与回收

        /// <summary>
        /// 隔离结束后的提交步骤：应用阵营、清理强制追踪玩家、放到擂台点位。
        /// 不在此激活角色；激活由事务在同一帧统一执行。
        /// </summary>
        internal static bool TryPrepareForArena(ModeHSpawnHandle handle, Vector3 arenaPos, out string failureReasonId)
        {
            failureReasonId = null;
            if (handle == null || handle.Character == null)
            {
                failureReasonId = "spawn_handle_invalid";
                return false;
            }

            try
            {
                handle.Character.SetTeam(handle.Team);
                handle.Character.SetPosition(arenaPos);

                // 缓存字段是 Inspector 序列化的，Mod 刷出的选手上可能为空，所以回退不可省。
                // 回退必须传 true：本方法在隔离期调用，此时角色已被 SetActive(false)，
                // 不含未激活对象的重载会返回 null，强制追踪玩家就清不掉。
                AICharacterController ai = handle.Character.aiCharacterController;
                if (ai == null)
                {
                    ai = handle.Character.GetComponentInChildren<AICharacterController>(true);
                }
                if (ai != null)
                {
                    // 安全项：forceTracePlayerDistance 生效阈值是 > 0.5f，且每帧在同队目标清理之前
                    // 覆写 searchedEnemy；原版 spawner 路径会写成 9999f，清零必须发生在创建返回之后。
                    ai.forceTracePlayerDistance = 0f;
                    ai.searchedEnemy = null;
                    ai.noticed = false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "spawn_prepare_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>在同一帧解除无敌并激活。</summary>
        internal static bool TryActivate(ModeHSpawnHandle handle, out string failureReasonId)
        {
            failureReasonId = null;
            if (handle == null || handle.Character == null)
            {
                failureReasonId = "spawn_handle_invalid";
                return false;
            }
            try
            {
                handle.Character.gameObject.SetActive(true);
                if (handle.Health != null)
                {
                    handle.Health.SetInvincible(false);
                }

                // 选手是 Mod 刷出来的，必须摘掉官方距离休眠：观众席离擂台远超 100m，
                // SetActiveByPlayerDistance.FixedUpdate 会把整场选手静默关掉，比赛就冻在原地。
                // 放在激活之后：此时角色已落到擂台点位，helper 的强制激活与本方法意图一致。
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(handle.Character);

                handle.Activated = true;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "spawn_activate_failed:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 回收一个 handle：先解除角色引用登记，再销毁角色，最后销毁 clone preset。
        /// 顺序固定为“角色引用 -> preset 引用 -> 销毁 clone”。
        /// </summary>
        internal static void Recycle(ModeHSpawnHandle handle)
        {
            if (handle == null) return;

            try
            {
                ModeHDeathSuppressionRegistry.UnregisterCharacter(handle.Health);
            }
            catch (Exception)
            {
                // 解除登记失败不阻断回收
            }

            try
            {
                if (handle.Character != null && handle.Character.gameObject != null)
                {
                    UnityEngine.Object.Destroy(handle.Character.gameObject);
                }
            }
            catch (Exception)
            {
                // 角色销毁失败不阻断 preset 清理
            }
            handle.Character = null;
            handle.Health = null;

            try
            {
                if (handle.ClonePreset != null)
                {
                    ModeHDeathSuppressionRegistry.UnregisterPreset(handle.ClonePreset);
                }
            }
            catch (Exception)
            {
                // 同上
            }

            DestroyClone(handle.ClonePreset);
            handle.ClonePreset = null;
            handle.SuppressionRegistered = false;
            handle.Activated = false;
        }

        private static void DestroyClone(CharacterRandomPreset clone)
        {
            if (clone == null) return;
            try
            {
                UnityEngine.Object.Destroy(clone);
            }
            catch (Exception)
            {
                // clone 销毁失败只丢弃引用
            }
        }

        #endregion
    }

    /// <summary>
    /// 生成期副作用采集接口（由生产认证实现）。
    /// 只比较创建前后两个计数不能替代持续时间线，因此窗口是显式的。
    /// </summary>
    internal sealed class ModeHSpawnDiagnostics
    {
        private readonly Dictionary<string, List<string>> _failures =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>当前处于创建 await 窗口的 stable key（空表示不在窗口内）。</summary>
        internal string ActiveWindowKey { get; private set; }

        /// <summary>窗口内观察到的伤害事件数。</summary>
        internal int DamageEventCount;

        /// <summary>窗口内观察到的死亡事件数。</summary>
        internal int DeathEventCount;

        /// <summary>窗口内观察到的额外掉落事件数。</summary>
        internal int ExtraDropEventCount;

        /// <summary>窗口内观察到的旧模式 tracking 事件数。</summary>
        internal int LegacyTrackingEventCount;

        /// <summary>窗口内峰值非预期活动实例数。</summary>
        internal int PeakUnexpectedActiveCount;

        /// <summary>进入创建 await 窗口。</summary>
        internal void BeginCreateWindow(string stableKey)
        {
            ActiveWindowKey = stableKey;
            DamageEventCount = 0;
            DeathEventCount = 0;
            ExtraDropEventCount = 0;
            LegacyTrackingEventCount = 0;
            PeakUnexpectedActiveCount = 0;
        }

        /// <summary>离开创建 await 窗口。</summary>
        internal void EndCreateWindow(string stableKey)
        {
            ActiveWindowKey = null;
        }

        /// <summary>记录一条失败原因。</summary>
        internal void RecordFailure(string stableKey, string reasonId)
        {
            if (string.IsNullOrEmpty(stableKey) || string.IsNullOrEmpty(reasonId)) return;
            List<string> list;
            if (!_failures.TryGetValue(stableKey, out list))
            {
                list = new List<string>();
                _failures[stableKey] = list;
            }
            if (!list.Contains(reasonId)) list.Add(reasonId);
        }

        /// <summary>取某个 key 的失败原因（稳定排序）。</summary>
        internal List<string> GetFailures(string stableKey)
        {
            List<string> list;
            if (string.IsNullOrEmpty(stableKey) || !_failures.TryGetValue(stableKey, out list))
            {
                return new List<string>();
            }
            List<string> copy = new List<string>(list);
            copy.Sort(StringComparer.Ordinal);
            return copy;
        }

        /// <summary>窗口内是否观察到任何副作用。</summary>
        internal bool HasWindowSideEffects()
        {
            return DamageEventCount > 0
                || DeathEventCount > 0
                || ExtraDropEventCount > 0
                || LegacyTrackingEventCount > 0
                || PeakUnexpectedActiveCount > 0;
        }
    }
}
