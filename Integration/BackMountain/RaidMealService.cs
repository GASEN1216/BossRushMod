// ============================================================================
// RaidMealService.cs - 出击餐：吃在基地，效果在下一局
// ============================================================================
// 【为什么不能用官方 Buff】
//   官方 Buff 不跨场景：CharacterBuffManager 没有存档，角色对象每个场景都重建。
//   在基地吃下的 Buff，进竞技场那一刻就没了。所以「出击前吃、下一局生效」
//   必须自己落存档：食用时登记一条待生效记录，下一局开局时再挂 Modifier。
//
// 【为什么落存档而不是内存变量】
//   玩家完全可能吃完饭就退游戏，第二天再进。内存变量会让那顿饭白吃。
//
// 【生效与清理】
//   生效点选 LevelManager.OnLevelInitialized——这正是官方 BuildingEffect 给
//   建筑加成用的时机，说明那时角色 Stat 已就绪。
//   清理走 RuntimeStatModifierTracker.RemoveAll（PercentageAdd + 反向迭代），
//   并在「回到基地」时一并消费掉登记：一顿饭只管一局。
// ============================================================================

using System;
using System.Collections.Generic;
using Saves;

namespace BossRush
{
    /// <summary>出击餐的登记、生效与清理。</summary>
    internal static class RaidMealService
    {
        #region 数值（草案，待 owner 审定）

        /// <summary>龙息果：枪械与近战伤害倍率 +10%。</summary>
        private const float DragonFruitDamageBonus = 0.10f;

        /// <summary>焚心椒：移速 +8%。</summary>
        private const float EmberChiliSpeedBonus = 0.08f;

        /// <summary>焚心椒：换弹速度 +10%。</summary>
        private const float EmberChiliReloadBonus = 0.10f;

        /// <summary>
        /// 幽影蘑菇：受到的物理伤害 -10%。
        ///
        /// 走 ElementFactor_Physics 这个受击侧的伤害倍率（&lt;1 减伤，见
        /// Integration/Config/DragonSetConfig.cs 的说明；Health 在结算时读它）。
        /// 这不是"碰伤害管线"——它和其他加成一样只是个 Stat Modifier，
        /// 丧尸模式的守护护盾用的就是同一条路（-25% PercentageAdd）。
        /// 数值取负，PercentageAdd。
        /// </summary>
        private const float PhantomMushroomPhysicsDamageReduction = -0.10f;

        #endregion

        #region 状态

        /// <summary>本局已挂上的 Modifier 记录，退局时按 source 一次清干净。</summary>
        private static readonly List<ZombieModeAttributeModifierRecord> _records =
            new List<ZombieModeAttributeModifierRecord>();

        /// <summary>Modifier 的 source 标记。同一个对象贯穿加与摘。</summary>
        private static readonly object _modifierSource = new object();

        /// <summary>本局是否已经应用过（防止同一局重复挂）。</summary>
        private static bool _appliedThisRun;

        #endregion

        #region 登记（食用时）

        /// <summary>
        /// 登记一份待生效的出击餐。同一时间只保留一条——
        /// 连吃两个不叠加，后吃的覆盖先吃的（避免叠满一桌菜进局）。
        /// </summary>
        internal static bool RegisterMeal(int mealTypeId)
        {
            int previous = 0;
            bool writeAttempted = false;
            try
            {
                if (BackMountainItems.GetDefinition(mealTypeId) == null) return false;
                if (SavesSystem.IsSaving) return false;
                if (SavesSystem.KeyExisits(BackMountainConfig.RaidMealSaveKey))
                    previous = SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey);
                writeAttempted = true;
                SavesSystem.Save<int>(BackMountainConfig.RaidMealSaveKey, mealTypeId);
                if (SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey) != mealTypeId)
                {
                    throw new InvalidOperationException("meal_readback_mismatch");
                }
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "出击餐已登记: " + mealTypeId);
                return true;
            }
            catch (Exception e)
            {
                if (writeAttempted)
                {
                    try { SavesSystem.Save<int>(BackMountainConfig.RaidMealSaveKey, previous); }
                    catch (Exception rollbackError)
                    { ModBehaviour.CriticalLog(BackMountainConfig.LogPrefix + "出击餐登记回滚失败: " + rollbackError.Message); }
                }
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐登记失败: " + e.Message);
                return false;
            }
        }

        /// <summary>读取当前登记的出击餐；没有返回 0。</summary>
        internal static int ReadRegisteredMeal()
        {
            try
            {
                if (!SavesSystem.KeyExisits(BackMountainConfig.RaidMealSaveKey)) return 0;
                return SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 清掉存档里的出击餐登记（写回 0）。
        ///
        /// 开放为 internal 的理由：`ClearForRun()` 按设计只摘本局 Modifier、**不碰存档键**，
        /// 于是「撤销一次登记」在全仓没有任何可调用的入口。F3 验收用例因此只能用
        /// ClearForRun 假装还原，把测试写进去的焚心椒永久留在玩家存档里——
        /// 玩家下一次出局会被 ApplyForRun 消费掉，白吃一份自己没做过的餐。
        /// 这不是为了让测试变绿而开的后门：它是一个本就缺失的正当动作入口。
        /// </summary>
        internal static bool ClearRegisteredMeal()
        {
            try
            {
                if (SavesSystem.IsSaving) return false;
                SavesSystem.Save<int>(BackMountainConfig.RaidMealSaveKey, 0);
                return SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey) == 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 清理出击餐登记失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 生效与清理

        /// <summary>
        /// 进入非基地场景时应用登记的出击餐。幂等：同一局只挂一次。
        /// 应用后立刻消费掉登记——一顿饭只管一局，中途重开也不该再拿到。
        /// </summary>
        internal static void ApplyForRun()
        {
            try
            {
                if (_appliedThisRun) return;

                int mealTypeId = ReadRegisteredMeal();
                if (mealTypeId <= 0) return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                // 先确认这是认识的餐品再消费登记。原先的写法是先清登记再 switch，
                // 遇到旧存档里的陌生 ID 会走 default 直接 return——饭被吃掉、
                // 加成没给、玩家也看不到任何提示。
                if (BackMountainItems.GetDefinition(mealTypeId) == null)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                        + "[WARNING] 登记的出击餐 ID 无法识别，已丢弃: " + mealTypeId);
                    bool cleared = ClearRegisteredMeal();
                    Duckov.UI.NotificationText.Push(cleared
                        ? L10n.T("旧版出击餐记录无法识别，已清除。",
                            "An unrecognized legacy meal record was cleared.")
                        : L10n.T("出击餐记录无法识别且暂时不能清除，请稍后重试。",
                            "The meal record is unrecognized and could not be cleared; try again later."));
                    return;
                }

                // 先持久消费登记，再挂本局 modifier；消费失败时绝不让同一份餐跨局重复生效。
                if (!ClearRegisteredMeal())
                {
                    Duckov.UI.NotificationText.Push(
                        L10n.T("出击餐登记暂时无法结算，本局未消耗也未生效。",
                            "The meal record could not be settled; it was neither consumed nor applied."));
                    return;
                }
                _appliedThisRun = true;

                switch (mealTypeId)
                {
                    case BossRushItemIds.DragonFruit:
                        AddModifier(main, ZombieModeStatNames.GunDamageMultiplier, DragonFruitDamageBonus);
                        AddModifier(main, ZombieModeStatNames.MeleeDamageMultiplier, DragonFruitDamageBonus);
                        break;
                    case BossRushItemIds.EmberChili:
                        // 官方角色只有 WalkSpeed / RunSpeed / Moveability 三个移动 stat，
                        // "MoveSpeed" 是 Animator 参数名（AGENTS §14），挂上去会被
                        // RuntimeStatModifierTracker 当缺失 stat 静默丢弃，故不再挂。
                        AddModifier(main, ZombieModeStatNames.RunSpeed, EmberChiliSpeedBonus);
                        AddModifier(main, ZombieModeStatNames.WalkSpeed, EmberChiliSpeedBonus);
                        // 换弹的官方 stat 是 ReloadSpeedGain（CharacterMainControl 的
                        // reloadSpeedGainHash）；"ReloadSpeedMultiplier" 在官方源码里不存在。
                        AddModifier(main, ZombieModeStatNames.ReloadSpeedGain, EmberChiliReloadBonus);
                        break;
                    case BossRushItemIds.PhantomMushroom:
                        AddModifier(main, ZombieModeStatNames.ElementFactorPhysics,
                            PhantomMushroomPhysicsDamageReduction);
                        break;
                }

                BackMountainItems.Definition def = BackMountainItems.GetDefinition(mealTypeId);
                if (def != null)
                {
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("出击餐生效：", "Meal in effect: ") + L10n.T(def.NameCN, def.NameEN));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐应用失败: " + e.Message);
            }
        }

        private static void AddModifier(CharacterMainControl character, string statName, float percent)
        {
            RuntimeStatModifierTracker.TryAdd(
                character, statName, percent, _modifierSource, _records, "RaidMeal");
        }

        /// <summary>
        /// 局结束 / 回基地时清掉本局加成。幂等；无记录时 O(1) 早返。
        /// </summary>
        internal static void ClearForRun()
        {
            try
            {
                if (_records.Count > 0)
                {
                    RuntimeStatModifierTracker.RemoveAll(_records, "RaidMeal");
                }
                _appliedThisRun = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐清理失败: " + e.Message);
                _appliedThisRun = false;
            }
        }

        #endregion

        #region 清理

        internal static void ResetStaticCaches()
        {
            ClearForRun();
            _records.Clear();
        }

        #endregion
    }
}
