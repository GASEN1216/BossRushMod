// ============================================================================
// WildHornUsage.cs - 荒野号角使用行为
// ============================================================================
// 模块说明：
//   实现荒野号角物品的使用逻辑：
//   - 场景中无坐骑时：通过 testVehicle.CreateCharacterAsync 生成马匹
//   - 场景中有坐骑时：调用 CallHorse() 呼唤坐骑到身边
//   - 包含冷却时间检查（默认3秒）
//   - 场景切换时自动清理缓存引用
// ============================================================================

using System;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// 荒野号角使用行为 - 召唤/呼唤坐骑
    /// </summary>
    public class WildHornUsage : UsageBehavior
    {
        // ============================================================================
        // 静态状态（跨实例共享）
        // ============================================================================

        /// <summary>缓存的坐骑AI引用（场景级别，切换时清空）</summary>
        private static AISpecialAttachment_Horse cachedHorseAI;

        /// <summary>上次使用时间戳（用于冷却计算）</summary>
        private static float lastUseTime = -999f;

        // ============================================================================
        // UsageBehavior 重写
        // ============================================================================

        /// <summary>
        /// 显示设置（物品描述中显示的使用说明）
        /// </summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = WildHornConfig.GetUsageDescription()
                };
            }
        }

        /// <summary>
        /// 检查物品是否可以使用（冷却时间检查）
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // 冷却时间检查：当前时间 - 上次使用时间 < 冷却秒数 → 不可使用
            if (Time.time - lastUseTime < WildHornConfig.COOLDOWN_SECONDS)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 使用物品时调用 - 判断坐骑是否存在，分支处理召唤/呼唤
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 获取玩家角色
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                if (player == null)
                {
                    // 玩家引用为空，安全返回（需求 5.1）
                    ModBehaviour.DevLog("[WildHorn] 玩家角色引用为空，取消使用");
                    return;
                }

                // 记录使用时间，开始冷却（需求 4.3）
                lastUseTime = Time.time;

                // 判断坐骑是否已存在
                if (HasExistingMount(player))
                {
                    // 坐骑已存在：呼唤坐骑到身边（需求 3.1）
                    player.CallHorse();
                    ShowBubbleHint(player, WildHornConfig.GetCallMountHint());
                    ModBehaviour.DevLog("[WildHorn] 呼唤已有坐骑");
                }
                else
                {
                    // 坐骑不存在：异步生成新坐骑（需求 2.1）
                    SpawnMountAsync(player).Forget();
                }
            }
            catch (Exception e)
            {
                // 捕获未预期异常（需求 5.3）
                ModBehaviour.DevLog("[WildHorn] 使用物品出错: " + e.Message);
            }
        }

        // ============================================================================
        // 坐骑生成与检查
        // ============================================================================

        /// <summary>
        /// 异步生成坐骑（通过 testVehicle.CreateCharacterAsync）
        /// </summary>
        private async UniTaskVoid SpawnMountAsync(CharacterMainControl player)
        {
            try
            {
                // 获取马匹预设
                var vehiclePreset = GameplayDataSettings.CharacterRandomPresetData.testVehicle;
                if (vehiclePreset == null)
                {
                    // 预设为空，显示失败提示（需求 5.2）
                    ModBehaviour.DevLog("[WildHorn] testVehicle 预设为空，无法生成坐骑");
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                    return;
                }

                // 在玩家位置附近生成马匹
                Vector3 spawnPos = player.transform.position + player.transform.forward * 2f + Vector3.up * 0.25f;
                int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                var horse = await vehiclePreset.CreateCharacterAsync(
                    spawnPos, Vector3.forward, sceneIndex, null, false);

                if (horse == null)
                {
                    // 生成返回空，显示失败提示（需求 5.2）
                    ModBehaviour.DevLog("[WildHorn] CreateCharacterAsync 返回空，生成坐骑失败");
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                    return;
                }

                // 获取马匹AI组件并设置主人引用（需求 2.2, 2.3）
                var horseAI = horse.GetComponentInChildren<AISpecialAttachment_Horse>();
                if (horseAI != null)
                {
                    horseAI.master = player;
                    player.horseAI = horseAI;
                    cachedHorseAI = horseAI;
                    ModBehaviour.DevLog("[WildHorn] 坐骑生成成功，已设置 horseAI 和 master 引用");
                }
                else
                {
                    // 没有找到马匹AI组件，仍然缓存角色引用
                    ModBehaviour.DevLog("[WildHorn] 警告: 生成的角色缺少 AISpecialAttachment_Horse 组件");
                }

                // 显示召唤成功提示（需求 2.4）
                ShowBubbleHint(player, WildHornConfig.GetSummonSuccessHint());
            }
            catch (Exception e)
            {
                // 捕获异步异常（需求 5.3）
                ModBehaviour.DevLog("[WildHorn] 生成坐骑异常: " + e.Message);
                try
                {
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                }
                catch { }
            }
        }

        /// <summary>
        /// 检查坐骑是否已存在（需求 7.2）
        /// 同时验证缓存引用非空且 GameObject 未被销毁
        /// </summary>
        private bool HasExistingMount(CharacterMainControl player)
        {
            // 优先检查缓存引用（Unity 对象 null 检查会识别已销毁的对象）
            if (cachedHorseAI != null && cachedHorseAI.gameObject != null)
            {
                return true;
            }

            // 兜底检查游戏原生引用
            if (player.horseAI != null)
            {
                // 同步缓存
                cachedHorseAI = player.horseAI;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 清理坐骑缓存引用（场景切换时调用）（需求 7.1）
        /// </summary>
        public static void ClearMountCache()
        {
            cachedHorseAI = null;
            ModBehaviour.DevLog("[WildHorn] 坐骑缓存已清理");
        }

        // ============================================================================
        // 辅助方法
        // ============================================================================

        /// <summary>
        /// 显示气泡提示（参考 BrickStoneUsage 的模式）
        /// </summary>
        private void ShowBubbleHint(CharacterMainControl player, string hint)
        {
            try
            {
                DialogueBubblesManager.Show(hint, player.transform, 2.5f, false, false, -1f, 2f);
            }
            catch { }
        }
    }
}
