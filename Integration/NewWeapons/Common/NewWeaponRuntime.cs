// ============================================================================
// NewWeaponRuntime.cs - P0 五把新武器的运行时生命周期 owner
// ============================================================================
// 模块说明：
//   初始化、场景切换、装备加载后配置、宿主销毁清理，四段全在本文件。
//   宿主 ModBehaviour 只保留四个一行转发（NewWeaponBootstrap.cs）——AGENTS §4.15：
//   「新子系统的状态、异步任务和专属算法放在自己的 RuntimeModule、服务或对象里，
//     不新增承载这些职责的 partial class ModBehaviour；宿主只保留必要的生命周期分发」。
//   这样做的直接好处是 ModBehaviourPartialBudgetGuard 的行数预算回落，
//   以后新武器再扩，加的是本文件的行，不是宿主的行。
//
// 清理顺序是硬约束：
//   先退订全部静态事件（Health 伤害/死亡、手持变化、槽位变化），再清表现层与状态缓存。
//   顺序颠倒会出现「事件还挂着但缓存已空」的窗口。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>P0 五把新武器的运行时 owner。全部方法幂等、自吞异常，不拖崩宿主。</summary>
    internal static class NewWeaponRuntime
    {
        /// <summary>等待主角就绪的轮询间隔。与宿主的 sharedWait05s 同步长，但不占宿主字段。</summary>
        private static readonly WaitForSeconds WaitHalfSecond = new WaitForSeconds(0.5f);

        /// <summary>等主角出现的最长时间（秒）。超时就放弃本次注册，下次进图再试。</summary>
        private const float PlayerWaitTimeoutSeconds = 15f;

        // 读档/快递恢复与锻造资格共用这份登记；物品工厂的 prefab 配置不能替代实例补配。
        internal static void RegisterRuntimeConfigs()
        {
            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                NewWeaponIds.ViperDaggerTypeId,
                item => ViperDaggerWeaponConfig.TryConfigure(item, NewWeaponIds.ViperDaggerBaseName),
                "毒蛇匕首");
            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                NewWeaponIds.FrostSpearTypeId,
                item => FrostSpearWeaponConfig.TryConfigure(item, NewWeaponIds.FrostSpearBaseName),
                "冰霜长矛");
            CustomItemRuntimeStateHelper.RegisterMeleeRuntimeConfiguredItem(
                NewWeaponIds.SummonStaffTypeId,
                item => SummonStaffWeaponConfig.TryConfigure(item, NewWeaponIds.SummonStaffBaseName),
                "召唤法杖");
        }

        // ====================================================================
        // 初始化
        // ====================================================================

        /// <summary>
        /// 初始化新武器系统（由 ModBehaviour.InitializeNewWeaponSystems 转发）。
        /// </summary>
        internal static void Initialize()
        {
            try
            {
                // 0. 先挂手持/佩戴状态缓存：四个运行时的门控判据都读它，必须最先就绪
                NewWeaponEquipState.Subscribe();

                // 1. 订阅毒蛇匕首运行时事件
                ViperDaggerRuntime.Subscribe();

                // 2. 创建召唤法杖能力管理器
                if (SummonStaffManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("SummonStaffManager");
                    UnityEngine.Object.DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<SummonStaffManager>();
                    ModBehaviour.DevLog("[NewWeapons] 召唤法杖能力管理器已创建");
                }

                // 3. 订阅能量盾运行时事件
                EnergyShieldRuntime.Subscribe();

                // 4. 订阅雷电戒指运行时事件
                ThunderRingRuntime.Subscribe();

                // 5. 订阅冰霜长矛命中表现（减速仍由 ItemSetting_MeleeWeapon 的官方 Cold buff 提供，
                //    这里只负责在命中处点一圈霜环）
                FrostSpearRuntime.Subscribe();

                ModBehaviour.DevLog("[NewWeapons] 系统初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeapons] 系统初始化失败: " + e.Message);
            }
        }

        // ====================================================================
        // 场景管理
        // ====================================================================

        /// <summary>
        /// 场景加载后设置新武器系统（由 ModBehaviour.SetupNewWeaponsForScene 转发）。
        /// </summary>
        internal static void SetupForScene(ModBehaviour owner, Scene scene)
        {
            try
            {
                // 装备状态缓存置脏：过图后官方会重建主角与 CharacterItem
                NewWeaponEquipState.MarkDirty();

                // 通知召唤法杖管理器场景已切换
                SummonStaffManager staffMgr = SummonStaffManager.Instance;
                if (staffMgr != null)
                {
                    staffMgr.OnSceneChanged();
                }

                // 延迟注册召唤法杖能力到玩家角色
                if (owner != null && ModBehaviour.IsGameplaySceneName(scene.name))
                {
                    owner.StartCoroutine(DelayedSetupSummonStaffAbility());
                }

                // 重置毒蛇匕首的叠毒状态（场景切换时敌人已不存在）
                ViperDaggerRuntime.ResetStaticCaches();

                // 重置能量盾状态
                EnergyShieldRuntime.ResetStaticCaches();

                // 重置雷电戒指状态
                ThunderRingRuntime.ResetStaticCaches();

                // 重置冰霜长矛的命中特效去重表（键是上一张图敌人的 InstanceID，留着没意义）
                FrostSpearRuntime.ResetStaticCaches();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeapons] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟设置召唤法杖能力：等主角出现再绑定，最长等 15 秒。
        /// </summary>
        private static IEnumerator DelayedSetupSummonStaffAbility()
        {
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < PlayerWaitTimeoutSeconds)
            {
                yield return WaitHalfSecond;
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                ModBehaviour.DevLog("[NewWeapons] 当前场景无玩家角色，跳过召唤法杖能力注册");
                yield break;
            }

            SummonStaffManager mgr = SummonStaffManager.Instance;
            if (mgr == null)
            {
                ModBehaviour.DevLog("[NewWeapons] 召唤法杖管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                ModBehaviour.DevLog("[NewWeapons] 召唤法杖能力已注册到玩家");
            }
            else if (mgr.TargetCharacter != player)
            {
                mgr.RebindToCharacter(player);
                ModBehaviour.DevLog("[NewWeapons] 召唤法杖能力已重新绑定到新玩家实例");
            }
        }

        // ====================================================================
        // 装备加载后配置
        // ====================================================================

        /// <summary>
        /// 在 LoadEquipmentContent 之后补一次配置（由 ModBehaviour.ConfigureNewWeaponsAfterLoad 转发）。
        /// 正常路径已由 ItemFactory 配置器覆盖（NewWeaponItemConfigurators），这里是兜底。
        /// </summary>
        internal static void ConfigureAfterLoad()
        {
            TryConfigureLoaded(NewWeaponIds.ViperDaggerTypeId, NewWeaponIds.ViperDaggerBaseName,
                ViperDaggerWeaponConfig.TryConfigure);
            TryConfigureLoaded(NewWeaponIds.SummonStaffTypeId, NewWeaponIds.SummonStaffBaseName,
                SummonStaffWeaponConfig.TryConfigure);
            TryConfigureLoaded(NewWeaponIds.EnergyShieldTypeId, NewWeaponIds.EnergyShieldBaseName,
                EnergyShieldWeaponConfig.TryConfigure);
            TryConfigureLoaded(NewWeaponIds.FrostSpearTypeId, NewWeaponIds.FrostSpearBaseName,
                FrostSpearWeaponConfig.TryConfigure);
            TryConfigureLoaded(NewWeaponIds.ThunderRingTypeId, NewWeaponIds.ThunderRingBaseName,
                ThunderRingWeaponConfig.TryConfigure);
        }

        private static void TryConfigureLoaded(int typeId, string baseName, Func<Item, string, bool> configure)
        {
            try
            {
                Item item = ItemFactory.GetLoadedItem(typeId);
                if (item == null) return;
                configure(item, baseName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeapons] 配置新武器失败 (TypeID=" + typeId + "): " + e.Message);
            }
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 宿主销毁清理（由 ModBehaviour.CleanupNewWeaponSystemsOnDestroy 转发）。
        /// 方法名里的 OnDestroy 是 StaticCacheLifecycleGuard 识别「清理路径」的判据，不要改名。
        /// </summary>
        internal static void CleanupOnDestroy()
        {
            try
            {
                // 1) 先退订全部静态事件
                NewWeaponEquipState.Unsubscribe();
                ViperDaggerRuntime.Unsubscribe();
                EnergyShieldRuntime.Unsubscribe();
                ThunderRingRuntime.Unsubscribe();
                FrostSpearRuntime.Unsubscribe();

                // 2) 清理召唤法杖（先收场上的灵魂战士，再销毁管理器）
                SummonStaffAction.CleanupAllSummonedAllies();
                SummonStaffManager.CleanupStatic();

                // 3) 重置运行时与配置静态缓存
                NewWeaponEquipState.ResetStaticCaches();
                ViperDaggerRuntime.ResetStaticCaches();
                EnergyShieldRuntime.ResetStaticCaches();
                ThunderRingRuntime.ResetStaticCaches();
                FrostSpearRuntime.ResetStaticCaches();
                // 表现层：销毁电弧池、清空拖尾对象池、释放程序化精灵
                NewWeaponFx.ResetStaticCaches();
                NewWeaponMeleeFx.ResetStaticCaches();
                // Boss 额外掉落的 pending 表
                NewWeaponBossDropHandler.ResetStaticCaches();
                ViperDaggerWeaponConfig.ResetStaticCaches();
                SummonStaffWeaponConfig.ResetStaticCaches();
                EnergyShieldWeaponConfig.ResetStaticCaches();
                FrostSpearWeaponConfig.ResetStaticCaches();
                ThunderRingWeaponConfig.ResetStaticCaches();

                ModBehaviour.DevLog("[NewWeapons] 系统清理完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeapons] 系统清理失败: " + e.Message);
            }
        }
    }
}
