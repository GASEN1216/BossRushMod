// ============================================================================
// SummonStaffAction.cs - 召唤法杖右键技能动作
// ============================================================================
// 模块说明：
//   右键技能「灵魂召唤」：在玩家周围召唤 3 只短命友军
//   复用霜之哀伤的召唤路径（CharacterRandomPreset.CreateCharacterAsync）
//   友军存活 15 秒后自动销毁
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 召唤法杖右键技能动作 — 灵魂召唤
    /// </summary>
    public class SummonStaffAction : EquipmentAbilityAction
    {
        private static SummonStaffConfig _config;
        private bool spawningStarted;
        private bool spawningComplete;
        private int activeSpawnRequestId;

        // 缓存的预设
        private static CharacterRandomPreset cachedPreset;
        private static bool presetSearchAttempted;

        // 已召唤的友军列表
        private static readonly List<CharacterMainControl> summonedAllies = new List<CharacterMainControl>();

        public static void SetConfig(SummonStaffConfig config)
        {
            _config = config;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (_config == null) _config = new SummonStaffConfig();
            return _config;
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override bool IsReadyInternal()
        {
            CleanupDeadAllies();
            if (summonedAllies.Count >= SummonStaffConfig.SummonCount)
            {
                return false;
            }
            return true;
        }

        protected override bool OnAbilityStart()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            if (!spawningStarted)
            {
                spawningStarted = true;
                int requestId = activeSpawnRequestId;
                SpawnAlliesAsync(requestId).Forget();
            }

            if (spawningComplete || actionElapsedTime >= SummonStaffConfig.TotalActionDuration)
            {
                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
        }

        private void OnDestroy()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
        }

        /// <summary>
        /// 异步生成友军
        /// </summary>
        private async UniTaskVoid SpawnAlliesAsync(int requestId)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                CharacterRandomPreset preset = FindPreset();
                if (preset == null)
                {
                    LogIfVerbose("未找到召唤预设，取消召唤");
                    return;
                }

                Teams playerTeam = player.Team;
                Vector3 playerPos = player.transform.position;
                int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                CleanupDeadAllies();
                int availableSlots = Mathf.Max(0, SummonStaffConfig.SummonCount - summonedAllies.Count);
                if (availableSlots <= 0) return;

                int successCount = 0;

                for (int i = 0; i < availableSlots; i++)
                {
                    if (!IsRequestValid(requestId, player, sceneIndex)) return;

                    // 计算生成位置
                    float angle = 360f * i / SummonStaffConfig.SummonCount;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * SummonStaffConfig.SummonRadius;
                    Vector3 spawnPos = SnapToGround(playerPos + offset, playerPos.y);

                    try
                    {
                        CharacterMainControl ally = await preset.CreateCharacterAsync(
                            spawnPos, Vector3.forward, sceneIndex, null, false);

                        if (!IsRequestValid(requestId, player, sceneIndex))
                        {
                            if (ally != null && ally.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(ally.gameObject);
                            }
                            return;
                        }

                        if (ally == null) continue;

                        // 配置友军
                        SetAllyHealth(ally, SummonStaffConfig.SummonHealth);
                        ally.SetTeam(playerTeam);
                        ModBehaviour.Instance?.SanitizeBossRushZombieSpawn(ally, "SummonStaff");
                        ally.dropBoxOnDead = false;
                        ally.gameObject.name = "SummonStaff_Ally_" + (summonedAllies.Count + 1);
                        ally.gameObject.SetActive(true);

                        // 设置 AI
                        SetupAllyAI(ally);

                        // 注册定时销毁
                        SummonStaffAllyLifetime lifetime = ally.gameObject.AddComponent<SummonStaffAllyLifetime>();
                        lifetime.Initialize(SummonStaffConfig.SummonLifetime);

                        summonedAllies.Add(ally);
                        successCount++;
                    }
                    catch (Exception e)
                    {
                        LogIfVerbose("召唤友军异常: " + e.Message);
                    }

                    // 每只之间让出一帧
                    await UniTask.Yield();
                }

                if (!IsRequestValid(requestId, player, sceneIndex)) return;

                if (successCount > 0)
                {
                    ShowSummonBubble(player, successCount);
                }
            }
            catch (Exception e)
            {
                LogIfVerbose("SpawnAlliesAsync 异常: " + e.Message);
            }
            finally
            {
                if (requestId == activeSpawnRequestId)
                {
                    spawningComplete = true;
                }
            }
        }

        private bool IsRequestValid(int requestId, CharacterMainControl player, int sceneIndex)
        {
            if (this == null || requestId != activeSpawnRequestId || !spawningStarted)
                return false;
            if (!isActiveAndEnabled || player == null || CharacterMainControl.Main != player)
                return false;
            if (player.Health != null && player.Health.IsDead)
                return false;
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == sceneIndex;
        }

        private static CharacterRandomPreset FindPreset()
        {
            if (cachedPreset != null) return cachedPreset;
            if (presetSearchAttempted) return null;

            presetSearchAttempted = true;

            try
            {
                CharacterRandomPreset[] allPresets = ObjectCache.GetCharacterPresets();
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null) continue;
                    if (!string.IsNullOrEmpty(preset.nameKey) &&
                        preset.nameKey == SummonStaffConfig.SummonPresetName)
                    {
                        cachedPreset = preset;
                        ModBehaviour.DevLog("[SummonStaff] 找到召唤预设: " + preset.name);
                        return cachedPreset;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SummonStaff] 查找预设异常: " + e.Message);
            }

            return null;
        }

        private void SetAllyHealth(CharacterMainControl ally, float targetHealth)
        {
            try
            {
                if (ally.Health == null) return;
                Item characterItem = ally.CharacterItem;
                if (characterItem == null) return;

                Stat hpStat = characterItem.GetStat("MaxHealth".GetHashCode());
                if (hpStat != null)
                {
                    float currentMax = ally.Health.MaxHealth;
                    if (currentMax > 0.01f)
                    {
                        hpStat.BaseValue *= targetHealth / currentMax;
                    }
                    else
                    {
                        hpStat.BaseValue = targetHealth;
                    }
                }

                ally.Health.SetHealth(ally.Health.MaxHealth);
            }
            catch (Exception e)
            {
                LogIfVerbose("设置友军血量失败: " + e.Message);
            }
        }

        private void SetupAllyAI(CharacterMainControl ally)
        {
            try
            {
                AICharacterController ai = ally.GetComponentInChildren<AICharacterController>();
                if (ai == null) return;

                ai.forceTracePlayerDistance = 0f;
                ai.searchedEnemy = null;
                ai.noticed = false;
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

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

        private static void ShowSummonBubble(CharacterMainControl player, int count)
        {
            if (player == null || player.transform == null) return;

            try
            {
                string text = L10n.T(
                    "<color=#BA68C8>灵魂战士已召唤！</color>",
                    "<color=#BA68C8>Soul warriors summoned!</color>");

                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    text,
                    player.transform,
                    2f,
                    false,
                    false,
                    -1f,
                    1.5f);
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

        private static void CleanupDeadAllies()
        {
            for (int i = summonedAllies.Count - 1; i >= 0; i--)
            {
                CharacterMainControl ally = summonedAllies[i];
                if (ally == null || ally.gameObject == null ||
                    (ally.Health != null && ally.Health.IsDead))
                {
                    summonedAllies.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 重置预设缓存
        /// </summary>
        internal static void ResetPresetCache()
        {
            cachedPreset = null;
            presetSearchAttempted = false;
        }

        /// <summary>
        /// 清理所有召唤的友军
        /// </summary>
        internal static void CleanupAllSummonedAllies()
        {
            foreach (CharacterMainControl ally in summonedAllies)
            {
                try
                {
                    if (ally != null && ally.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(ally.gameObject);
                    }
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }
            summonedAllies.Clear();
        }
    }

    /// <summary>
    /// 召唤物生命周期组件 - 到期自动销毁
    /// </summary>
    internal sealed class SummonStaffAllyLifetime : MonoBehaviour
    {
        private float lifetime;
        private float elapsed;

        public void Initialize(float duration)
        {
            lifetime = duration;
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
