// ============================================================================
// SpawnedEnemyActivationHelper.cs - Mod 刷出敌人的激活保护
// ============================================================================
// 模块说明：
//   官方 CharacterRandomPreset.setActiveByPlayerDistance 默认 true，
//   CreateCharacterAsync 传入 relatedScene != -1 时，角色会被注册进
//   Duckov.Utilities.SetActiveByPlayerDistance。该组件 FixedUpdate 每帧无条件执行
//   gameObject.SetActive(距玩家 < 100m)。
//
//   原版靠 spawner 把怪放在玩家会走到的地方，所以这套休眠是优化。但 Mod 的刷怪点
//   来自 Assets/SpawnPoints/*.json，玩家可能离得很远（换图、大地图、跑图），
//   此时已刷出的怪会被静默关掉：Health.IsDead 仍为 false、仍留在模式的存活列表里，
//   于是波次永远不结算，玩家卡波次。
//
//   Mode E 早就单独处理过这件事（ModeERespawnItems.TryForceActivateModeEEnemy），
//   本 helper 把那段已验证逻辑提到共享层，供共享生成核心的激活点统一调用。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mod 刷出敌人的激活保护：解除官方「距玩家过远即休眠」注册。
    /// </summary>
    internal static class SpawnedEnemyActivationHelper
    {
        /// <summary>
        /// 把角色从官方距离休眠系统里摘出来，并确保它处于激活、非休眠状态。
        /// 逐项 try/catch：任一步失败都不应阻断刷怪流程。
        /// </summary>
        /// <remarks>
        /// 只对「Mod 自己刷出来的怪」调用。原版 spawner 生成的角色仍应保留官方休眠优化，
        /// 否则远处整图的 AI 都会常驻运行。
        /// </remarks>
        internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl character)
        {
            if (character == null)
            {
                return;
            }

            GameObject characterObject = character.gameObject;
            if (characterObject == null)
            {
                return;
            }

            try
            {
                Duckov.Utilities.SetActiveByPlayerDistance.Unregister(
                    characterObject, characterObject.scene.buildIndex);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SpawnActivation] [WARNING] 取消距离休眠注册失败: " + e.Message);
            }

            try
            {
                character.SetSleeping(false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SpawnActivation] [WARNING] 解除休眠失败: " + e.Message);
            }

            try
            {
                if (!characterObject.activeSelf)
                {
                    characterObject.SetActive(true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SpawnActivation] [WARNING] 重新激活失败: " + e.Message);
            }
        }
    }
}
