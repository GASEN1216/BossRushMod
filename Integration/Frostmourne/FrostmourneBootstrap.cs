// ============================================================================
// FrostmourneBootstrap.cs - 霜之哀伤系统启动集成
// ============================================================================
// 模块说明：
//   将霜之哀伤系统集成到 ModBehaviour 中
//   负责初始化、场景切换处理和清理
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化霜之哀伤系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializeFrostmourneSystem()
        {
            try
            {
                // 创建右键能力管理器（MonoBehaviour 单例）
                if (FrostmourneAbilityManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("FrostmourneAbilityManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<FrostmourneAbilityManager>();
                    DevLog("[Frostmourne] 右键能力管理器已创建");
                }

                DevLog("[Frostmourne] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置霜之哀伤系统
        /// </summary>
        private void SetupFrostmourneForScene(Scene scene)
        {
            try
            {
                // 通知右键能力管理器场景已切换
                var abilityMgr = FrostmourneAbilityManager.Instance;
                if (abilityMgr != null)
                {
                    abilityMgr.OnSceneChanged();
                }

                // 延迟注册/重新绑定能力到玩家角色
                StartCoroutine(DelayedSetupFrostmourneAbility());
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟设置霜之哀伤能力
        /// </summary>
        private IEnumerator DelayedSetupFrostmourneAbility()
        {
            // 等待玩家角色可用
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                DevLog("[Frostmourne] 当前场景无玩家角色，跳过能力注册");
                yield break;
            }

            var mgr = FrostmourneAbilityManager.Instance;
            if (mgr == null)
            {
                DevLog("[Frostmourne] [WARNING] 右键能力管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                DevLog("[Frostmourne] 右键能力已注册到玩家");
            }
            else
            {
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[Frostmourne] 右键能力已重新绑定到新玩家实例");
                }
                else
                {
                    DevLog("[Frostmourne] 右键能力已由 OnSceneChanged 重建，跳过重复绑定");
                }
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理霜之哀伤系统
        /// </summary>
        private void CleanupFrostmourneSystem()
        {
            try
            {
                // 清理右键能力管理器
                FrostmourneAbilityManager.CleanupStatic();

                // 清理所有召唤的僵尸
                FrostmourneAction.CleanupAllSummonedZombies();

                DevLog("[Frostmourne] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 系统清理失败: " + e.Message);
            }
        }
    }
}
