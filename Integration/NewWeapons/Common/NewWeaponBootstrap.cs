// ============================================================================
// NewWeaponBootstrap.cs - P0新武器扩展启动集成
// ============================================================================
// 模块说明：
//   将五把新武器系统集成到 ModBehaviour 中
//   负责初始化、场景切换处理和清理
//   使用 partial class 扩展 ModBehaviour，最小化对现有代码的修改
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// P0 新武器扩展启动模块
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化新武器系统（在 InitializeLateEquipmentAbilitySystems 中调用）
        /// </summary>
        private void InitializeNewWeaponSystems()
        {
            try
            {
                // 1. 订阅毒蛇匕首运行时事件
                ViperDaggerRuntime.Subscribe();

                // 2. 创建召唤法杖能力管理器
                if (SummonStaffManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("SummonStaffManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<SummonStaffManager>();
                    DevLog("[NewWeapons] 召唤法杖能力管理器已创建");
                }

                // 3. 订阅能量盾运行时事件
                EnergyShieldRuntime.Subscribe();

                // 4. 订阅雷电戒指运行时事件
                ThunderRingRuntime.Subscribe();

                // 冰霜长矛不需要运行时订阅（纯被动属性，由 ItemSetting_MeleeWeapon 处理）

                DevLog("[NewWeapons] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[NewWeapons] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置新武器系统
        /// </summary>
        private void SetupNewWeaponsForScene(Scene scene)
        {
            try
            {
                // 通知召唤法杖管理器场景已切换
                var staffMgr = SummonStaffManager.Instance;
                if (staffMgr != null)
                {
                    staffMgr.OnSceneChanged();
                }

                // 延迟注册召唤法杖能力到玩家角色
                if (IsGameplaySceneName(scene.name))
                {
                    StartCoroutine(DelayedSetupSummonStaffAbility());
                }

                // 重置毒蛇匕首的叠毒状态（场景切换时敌人已不存在）
                ViperDaggerRuntime.ResetStaticCaches();

                // 重置能量盾状态
                EnergyShieldRuntime.ResetStaticCaches();

                // 重置雷电戒指状态
                ThunderRingRuntime.ResetStaticCaches();
            }
            catch (Exception e)
            {
                DevLog("[NewWeapons] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟设置召唤法杖能力
        /// </summary>
        private IEnumerator DelayedSetupSummonStaffAbility()
        {
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return sharedWait05s;
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                DevLog("[NewWeapons] 当前场景无玩家角色，跳过召唤法杖能力注册");
                yield break;
            }

            var mgr = SummonStaffManager.Instance;
            if (mgr == null)
            {
                DevLog("[NewWeapons] 召唤法杖管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                DevLog("[NewWeapons] 召唤法杖能力已注册到玩家");
            }
            else
            {
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[NewWeapons] 召唤法杖能力已重新绑定到新玩家实例");
                }
            }
        }

        // ========== 装备加载后配置 ==========

        /// <summary>
        /// 在 LoadEquipmentContent 中调用，配置新武器
        /// </summary>
        private void ConfigureNewWeaponsAfterLoad()
        {
            try
            {
                // 毒蛇匕首
                Item viperDagger = ItemFactory.GetLoadedItem(NewWeaponIds.ViperDaggerTypeId);
                if (viperDagger != null)
                {
                    ViperDaggerWeaponConfig.TryConfigure(viperDagger, NewWeaponIds.ViperDaggerBaseName);
                }

                // 召唤法杖
                Item summonStaff = ItemFactory.GetLoadedItem(NewWeaponIds.SummonStaffTypeId);
                if (summonStaff != null)
                {
                    SummonStaffWeaponConfig.TryConfigure(summonStaff, NewWeaponIds.SummonStaffBaseName);
                }

                // 能量盾
                Item energyShield = ItemFactory.GetLoadedItem(NewWeaponIds.EnergyShieldTypeId);
                if (energyShield != null)
                {
                    EnergyShieldWeaponConfig.TryConfigure(energyShield, NewWeaponIds.EnergyShieldBaseName);
                }

                // 冰霜长矛
                Item frostSpear = ItemFactory.GetLoadedItem(NewWeaponIds.FrostSpearTypeId);
                if (frostSpear != null)
                {
                    FrostSpearWeaponConfig.TryConfigure(frostSpear, NewWeaponIds.FrostSpearBaseName);
                }

                // 雷电戒指
                Item thunderRing = ItemFactory.GetLoadedItem(NewWeaponIds.ThunderRingTypeId);
                if (thunderRing != null)
                {
                    ThunderRingWeaponConfig.TryConfigure(thunderRing, NewWeaponIds.ThunderRingBaseName);
                }
            }
            catch (Exception e)
            {
                DevLog("[NewWeapons] 配置新武器失败: " + e.Message);
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理新武器系统
        /// </summary>
        private void CleanupNewWeaponSystemsOnDestroy()
        {
            try
            {
                // 取消订阅运行时事件
                ViperDaggerRuntime.Unsubscribe();
                EnergyShieldRuntime.Unsubscribe();
                ThunderRingRuntime.Unsubscribe();

                // 清理召唤法杖
                SummonStaffAction.CleanupAllSummonedAllies();
                SummonStaffManager.CleanupStatic();

                // 重置运行时与配置静态缓存
                ViperDaggerRuntime.ResetStaticCaches();
                EnergyShieldRuntime.ResetStaticCaches();
                ThunderRingRuntime.ResetStaticCaches();
                ViperDaggerWeaponConfig.ResetStaticCaches();
                SummonStaffWeaponConfig.ResetStaticCaches();
                EnergyShieldWeaponConfig.ResetStaticCaches();
                FrostSpearWeaponConfig.ResetStaticCaches();
                ThunderRingWeaponConfig.ResetStaticCaches();

                DevLog("[NewWeapons] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[NewWeapons] 系统清理失败: " + e.Message);
            }
        }
    }
}
