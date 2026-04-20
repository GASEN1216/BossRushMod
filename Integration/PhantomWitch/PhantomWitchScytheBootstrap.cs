// ============================================================================
// PhantomWitchScytheBootstrap.cs - 幽灵女巫大镰右键技能系统启动集成
// ============================================================================
// 模块说明：
//   将幽灵女巫大镰「诅咒领域」系统集成到 ModBehaviour 中。
//   负责右键能力管理器的初始化、场景切换后的重绑定、清理。
//
//   与 FrostmourneBootstrap 保持一致的生命周期范式。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫大镰系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化幽灵女巫大镰系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializePhantomWitchScytheSystem()
        {
            try
            {
                if (PhantomWitchScytheAbilityManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("PhantomWitchScytheAbilityManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<PhantomWitchScytheAbilityManager>();
                    DevLog("[PhantomWitchScythe] 右键能力管理器已创建");
                }

                PhantomWitchCurseSweatVfx.RegisterGlobalHook();
                DevLog("[PhantomWitchScythe] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置幽灵女巫大镰系统
        /// </summary>
        private void SetupPhantomWitchScytheForScene(Scene scene)
        {
            try
            {
                var abilityMgr = PhantomWitchScytheAbilityManager.Instance;
                if (abilityMgr != null)
                {
                    abilityMgr.OnSceneChanged();
                }

                StartCoroutine(DelayedSetupPhantomWitchScytheAbility());
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟把右键能力重新绑定到玩家角色
        /// </summary>
        private IEnumerator DelayedSetupPhantomWitchScytheAbility()
        {
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                DevLog("[PhantomWitchScythe] 当前场景无玩家角色，跳过能力注册");
                yield break;
            }

            var mgr = PhantomWitchScytheAbilityManager.Instance;
            if (mgr == null)
            {
                DevLog("[PhantomWitchScythe] [WARNING] 右键能力管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                DevLog("[PhantomWitchScythe] 右键能力已注册到玩家");
            }
            else
            {
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[PhantomWitchScythe] 右键能力已重新绑定到新玩家实例");
                }
                else
                {
                    DevLog("[PhantomWitchScythe] 右键能力已由 OnSceneChanged 重建，跳过重复绑定");
                }
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理幽灵女巫大镰系统
        /// </summary>
        private void CleanupPhantomWitchScytheSystem()
        {
            try
            {
                PhantomWitchCurseSweatVfx.UnregisterGlobalHook();
                PhantomWitchScytheAbilityManager.CleanupStatic();
                PhantomWitchCurseRealmVisual.ClearCache();
                DevLog("[PhantomWitchScythe] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 系统清理失败: " + e.Message);
            }
        }
    }
}
