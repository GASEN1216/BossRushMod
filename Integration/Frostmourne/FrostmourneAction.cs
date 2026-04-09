// ============================================================================
// FrostmourneAction.cs - 霜之哀伤右键技能动作
// ============================================================================
// 模块说明：
//   右键技能「亡灵召唤」：在玩家周围 5 个方位召唤 5 只 Cname_Zombie
//   设为玩家同阵营，血量 100，冷却 10 秒
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤右键技能动作 — 亡灵召唤
    /// </summary>
    public class FrostmourneAction : EquipmentAbilityAction
    {
        private static FrostmourneConfig _config;
        private bool spawningStarted;
        private bool spawningComplete;

        // 缓存的僵尸预设
        private static CharacterRandomPreset cachedZombiePreset;
        private static bool presetSearchAttempted;

        // 已召唤的僵尸列表（用于清理）
        private static readonly List<CharacterMainControl> summonedZombies = new List<CharacterMainControl>();

        public static void SetConfig(FrostmourneConfig config)
        {
            _config = config;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (_config == null) _config = new FrostmourneConfig();
            return _config;
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false; // 不持续消耗体力
        }

        protected override bool IsReadyInternal()
        {
            int activeZombieCount = GetActiveSummonedZombieCount();
            if (activeZombieCount >= FrostmourneConfig.SummonCount)
            {
                LogIfVerbose("当前亡灵已达到上限，跳过召唤");
                return false;
            }

            return true;
        }

        protected override bool OnAbilityStart()
        {
            spawningStarted = false;
            spawningComplete = false;
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            if (!spawningStarted)
            {
                spawningStarted = true;
                SpawnZombiesAsync().Forget();
            }

            if (spawningComplete || actionElapsedTime >= FrostmourneConfig.TotalActionDuration)
            {
                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            spawningStarted = false;
            spawningComplete = false;
        }

        /// <summary>
        /// 异步生成 5 只僵尸
        /// </summary>
        private async UniTaskVoid SpawnZombiesAsync()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    LogIfVerbose("玩家角色为空，取消召唤");
                    spawningComplete = true;
                    return;
                }

                // 查找僵尸预设
                CharacterRandomPreset zombiePreset = FindZombiePreset();
                if (zombiePreset == null)
                {
                    LogIfVerbose("未找到 Cname_Zombie 预设，取消召唤");
                    spawningComplete = true;
                    return;
                }

                Teams playerTeam = player.Team;
                Vector3 playerPos = player.transform.position;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                // 清理之前已死亡的僵尸引用
                CleanupDeadZombies();
                int availableSlots = Mathf.Max(0, FrostmourneConfig.SummonCount - summonedZombies.Count);
                if (availableSlots <= 0)
                {
                    LogIfVerbose("当前亡灵数量已满，本次不再追加召唤");
                    spawningComplete = true;
                    return;
                }

                // 仅补齐缺额，保证场上同时最多 5 只亡灵
                for (int i = 0; i < availableSlots; i++)
                {
                    float angle = i * 72f; // 0°, 72°, 144°, 216°, 288°
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * FrostmourneConfig.SummonRadius;
                    Vector3 spawnPos = playerPos + offset;

                    // 尝试将生成点对齐到地面
                    spawnPos = SnapToGround(spawnPos, playerPos.y);

                    // 验证生成点是否可用（不在墙内）
                    if (!IsSpawnPointValid(spawnPos))
                    {
                        // 尝试缩短距离
                        spawnPos = playerPos + offset * 0.6f;
                        spawnPos = SnapToGround(spawnPos, playerPos.y);
                        if (!IsSpawnPointValid(spawnPos))
                        {
                            LogIfVerbose("方位 " + i + " 无可用生成点，跳过");
                            continue;
                        }
                    }

                    try
                    {
                        CharacterMainControl zombie = await zombiePreset.CreateCharacterAsync(
                            spawnPos, Vector3.forward, relatedScene, null, false);

                        if (zombie == null)
                        {
                            LogIfVerbose("方位 " + i + " 僵尸生成失败");
                            continue;
                        }

                        // 设置血量为 100
                        SetZombieHealth(zombie, FrostmourneConfig.ZombieHealth);

                        // 设置为玩家同阵营
                        zombie.SetTeam(playerTeam);

                        // 禁止掉落
                        zombie.dropBoxOnDead = false;

                        // 设置名称
                        zombie.gameObject.name = "Frostmourne_Zombie_" + (summonedZombies.Count + 1);

                        // 激活
                        zombie.gameObject.SetActive(true);

                        // 设置 AI 追踪最近敌人
                        SetupZombieAI(zombie, player);

                        // 记录到列表
                        summonedZombies.Add(zombie);

                        LogIfVerbose("方位 " + i + " 僵尸召唤成功");
                    }
                    catch (Exception e)
                    {
                        LogIfVerbose("方位 " + i + " 僵尸生成异常: " + e.Message);
                    }

                    // 每只之间让出一帧，避免卡顿
                    await UniTask.Yield();
                }

                LogIfVerbose("亡灵召唤完成");
            }
            catch (Exception e)
            {
                LogIfVerbose("SpawnZombiesAsync 异常: " + e.Message);
            }
            finally
            {
                spawningComplete = true;
            }
        }

        /// <summary>
        /// 查找 Cname_Zombie 预设
        /// </summary>
        private static CharacterRandomPreset FindZombiePreset()
        {
            if (cachedZombiePreset != null) return cachedZombiePreset;
            if (presetSearchAttempted) return null;

            presetSearchAttempted = true;

            try
            {
                CharacterRandomPreset[] allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null) continue;
                    if (!string.IsNullOrEmpty(preset.nameKey) &&
                        preset.nameKey == FrostmourneConfig.ZombiePresetName)
                    {
                        cachedZombiePreset = preset;
                        ModBehaviour.DevLog("[Frostmourne] 找到 Cname_Zombie 预设: " + preset.name);
                        return cachedZombiePreset;
                    }
                }

                ModBehaviour.DevLog("[Frostmourne] [WARNING] 未找到 Cname_Zombie 预设");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 查找僵尸预设异常: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 设置僵尸血量
        /// </summary>
        private void SetZombieHealth(CharacterMainControl zombie, float targetHealth)
        {
            try
            {
                if (zombie.Health == null) return;

                Item characterItem = zombie.CharacterItem;
                if (characterItem == null) return;

                Stat hpStat = characterItem.GetStat("MaxHealth".GetHashCode());
                if (hpStat != null)
                {
                    float currentMax = zombie.Health.MaxHealth;
                    if (currentMax > 0.01f)
                    {
                        float scale = targetHealth / currentMax;
                        hpStat.BaseValue *= scale;
                    }
                    else
                    {
                        hpStat.BaseValue = targetHealth;
                    }
                }

                // 同步血量到满
                zombie.Health.SetHealth(zombie.Health.MaxHealth);
            }
            catch (Exception e)
            {
                LogIfVerbose("设置僵尸血量失败: " + e.Message);
            }
        }

        /// <summary>
        /// 设置僵尸 AI（跟随玩家附近，攻击敌人）
        /// </summary>
        private void SetupZombieAI(CharacterMainControl zombie, CharacterMainControl player)
        {
            try
            {
                AICharacterController ai = zombie.GetComponentInChildren<AICharacterController>();
                if (ai == null) return;

                // 设置 AI 战斗因子为 1（积极战斗）
                ai.forceTracePlayerDistance = 0f; // 不强制追踪玩家（它是友军）

                // 让 AI 自然寻敌（同阵营设置后，AI 会自动攻击敌对阵营）
            }
            catch (Exception e)
            {
                LogIfVerbose("设置僵尸 AI 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 将位置对齐到地面
        /// </summary>
        private static Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 samplePoint = position + Vector3.up * 5f;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask;

            if (Physics.Raycast(samplePoint, Vector3.down, out hit, 15f, groundMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }

        /// <summary>
        /// 检查生成点是否可用（不在墙壁内）
        /// </summary>
        private static bool IsSpawnPointValid(Vector3 position)
        {
            // 检测墙壁等不可通过的障碍物
            int wallMask = LayerMask.GetMask("Default", "Wall");
            Collider[] hits = Physics.OverlapCapsule(
                position,
                position + Vector3.up * 1.5f,
                0.4f,
                wallMask
            );

            foreach (Collider col in hits)
            {
                if (col != null && !col.isTrigger)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取当前仍然存活的召唤亡灵数量
        /// </summary>
        internal static int GetActiveSummonedZombieCount()
        {
            CleanupDeadZombies();
            return summonedZombies.Count;
        }

        /// <summary>
        /// 清理已死亡的僵尸引用
        /// </summary>
        private static void CleanupDeadZombies()
        {
            for (int i = summonedZombies.Count - 1; i >= 0; i--)
            {
                CharacterMainControl zombie = summonedZombies[i];
                if (zombie == null || zombie.gameObject == null ||
                    (zombie.Health != null && zombie.Health.IsDead))
                {
                    summonedZombies.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 重置预设缓存（场景切换时调用）
        /// </summary>
        internal static void ResetPresetCache()
        {
            cachedZombiePreset = null;
            presetSearchAttempted = false;
        }

        /// <summary>
        /// 清理所有召唤的僵尸
        /// </summary>
        internal static void CleanupAllSummonedZombies()
        {
            foreach (CharacterMainControl zombie in summonedZombies)
            {
                try
                {
                    if (zombie != null && zombie.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(zombie.gameObject);
                    }
                }
                catch { }
            }
            summonedZombies.Clear();
        }
    }
}
