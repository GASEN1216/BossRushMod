// ============================================================================
// FenHuangHalberdBootstrap.cs - 焚皇断界戟系统启动集成
// ============================================================================
// 模块说明：
//   将焚皇断界戟系统集成到 ModBehaviour 中
//   负责初始化、场景切换处理和清理
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化焚皇断界戟系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializeFenHuangHalberdSystem()
        {
            try
            {
                // 1. 创建连招管理器（MonoBehaviour 单例）
                if (FenHuangComboManager.Instance == null)
                {
                    GameObject comboObj = new GameObject("FenHuangComboManager");
                    DontDestroyOnLoad(comboObj);
                    comboObj.AddComponent<FenHuangComboManager>();
                    DevLog("[FenHuangHalberd] 连招管理器已创建");
                }

                // 2. 创建右键能力管理器（使用具体子类，不走抽象基类的 Instance 属性）
                if (FenHuangHalberdAbilityManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("FenHuangHalberdAbilityManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<FenHuangHalberdAbilityManager>();
                    DevLog("[FenHuangHalberd] 右键能力管理器已创建");
                }

                // 注意：不在这里注册能力到玩家！
                // 初始化时通常在主菜单场景，还没有玩家角色
                // 能力注册由 SetupFenHuangHalberdForScene 在游戏场景加载时执行

                DevLog("[FenHuangHalberd] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[FenHuangHalberd] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置焚皇断界戟系统
        /// </summary>
        private void SetupFenHuangHalberdForScene(Scene scene)
        {
            try
            {
                // 通知连招管理器场景已切换
                if (FenHuangComboManager.Instance != null)
                {
                    FenHuangComboManager.Instance.OnSceneChanged();
                }

                // 通知右键能力管理器场景已切换
                var abilityMgr = FenHuangHalberdAbilityManager.Instance;
                if (abilityMgr != null)
                {
                    abilityMgr.OnSceneChanged();
                }

                // 延迟注册/重新绑定能力到玩家角色
                StartCoroutine(DelayedSetupHalberdAbility());
            }
            catch (Exception e)
            {
                DevLog("[FenHuangHalberd] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟设置焚皇断界戟能力（统一处理首次注册和场景切换重绑定）
        /// </summary>
        private IEnumerator DelayedSetupHalberdAbility()
        {
            // 等待玩家角色可用（游戏场景加载需要时间）
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                // 可能是主菜单等非游戏场景，跳过即可
                DevLog("[FenHuangHalberd] 当前场景无玩家角色，跳过能力注册");
                yield break;
            }

            var mgr = FenHuangHalberdAbilityManager.Instance;
            if (mgr == null)
            {
                DevLog("[FenHuangHalberd] [WARNING] 右键能力管理器不存在");
                yield break;
            }

            // 根据当前状态决定：首次注册 或 重新绑定
            if (!mgr.IsAbilityEnabled)
            {
                // 首次注册（之前从未成功注册过，或者注册被注销了）
                mgr.RegisterAbility(player);
                DevLog("[FenHuangHalberd] 右键能力已注册到玩家");
            }
            else
            {
                // 已注册过，OnSceneChanged 已经重建了动作，
                // 这里只需更新角色引用（如果角色实例变了）
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[FenHuangHalberd] 右键能力已重新绑定到新玩家实例");
                }
                else
                {
                    DevLog("[FenHuangHalberd] 右键能力已由 OnSceneChanged 重建，跳过重复绑定");
                }
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理焚皇断界戟系统
        /// </summary>
        private void CleanupFenHuangHalberdSystem()
        {
            try
            {
                // 清理右键能力管理器
                FenHuangHalberdAbilityManager.CleanupStatic();

                // 清理连招管理器
                if (FenHuangComboManager.Instance != null)
                {
                    Destroy(FenHuangComboManager.Instance.gameObject);
                }

                // 清理龙焰印记
                DragonFlameMarkTracker.ClearAll();

                DevLog("[FenHuangHalberd] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[FenHuangHalberd] 系统清理失败: " + e.Message);
            }
        }
    }
}
