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

                // 2. 确保右键能力管理器实例存在
                FenHuangHalberdAbilityManager.EnsureInstance();
                DevLog("[FenHuangHalberd] 右键能力管理器已创建");

                // 3. 延迟注册能力到玩家（等待玩家角色可用）
                StartCoroutine(DelayedRegisterHalberdAbility());

                DevLog("[FenHuangHalberd] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[FenHuangHalberd] 系统初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟注册焚皇断界戟能力到玩家
        /// </summary>
        private IEnumerator DelayedRegisterHalberdAbility()
        {
            // 等待玩家角色可用
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 10f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
            {
                var mgr = FenHuangHalberdAbilityManager.Instance;
                if (mgr != null)
                {
                    mgr.RegisterAbility(player);
                    DevLog("[FenHuangHalberd] 右键能力已注册到玩家");
                }
            }
            else
            {
                DevLog("[FenHuangHalberd] [WARNING] 等待玩家角色超时，右键能力未注册");
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
                if (FenHuangHalberdAbilityManager.Instance != null)
                {
                    FenHuangHalberdAbilityManager.Instance.OnSceneChanged();
                }

                // 延迟重新绑定到新场景的玩家角色
                StartCoroutine(DelayedRebindHalberdAbility());
            }
            catch (Exception e)
            {
                DevLog("[FenHuangHalberd] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟重新绑定焚皇断界戟能力到新场景的玩家角色
        /// </summary>
        private IEnumerator DelayedRebindHalberdAbility()
        {
            yield return new WaitForSeconds(0.5f);

            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
            {
                var mgr = FenHuangHalberdAbilityManager.Instance;
                if (mgr != null)
                {
                    mgr.RebindToCharacter(player);
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
