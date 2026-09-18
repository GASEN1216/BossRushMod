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
//   生效点选 LevelManager.OnAfterLevelInitialized——这正是官方 BuildingEffect 给
//   建筑加成用的时机，说明那时角色 Stat 已就绪。
//   清理走 RuntimeStatModifierTracker.RemoveAll（PercentageAdd + 反向迭代），
//   首次装配成功后消费登记；同一次官方出击换区可重挂，结束/死亡/换槽清理。
// ============================================================================

using System;
using System.Collections.Generic;
using Saves;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>出击餐的登记、生效与清理。</summary>
    internal static class RaidMealService
    {
        #region 数值（保留既有平衡）

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
        private static int _activeMeal;
        private static uint _activeRaidId;
        private static int _activeSlot = -1;
        private static CharacterMainControl _appliedCharacter;

        #endregion

        #region 登记（食用时）

        /// <summary>
        /// 登记一份待生效的出击餐。同一时间只保留一条——
        /// 连吃两个不叠加，后吃的覆盖先吃的（避免叠满一桌菜进局）。
        /// </summary>
        internal static bool RegisterMeal(int mealTypeId)
        {
            BackMountainItems.Definition definition = BackMountainItems.GetDefinition(mealTypeId);
            return definition != null && !definition.IsSeed && TryWriteRegisteredMeal(mealTypeId);
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
            return TryWriteRegisteredMeal(0);
        }

        // 登记、覆盖与结算共用同一事务，失败恢复原值。
        private static bool TryWriteRegisteredMeal(int mealTypeId)
        {
            int previous = 0;
            bool writeAttempted = false;
            try
            {
                if (SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0) return false;
                if (SavesSystem.KeyExisits(BackMountainConfig.RaidMealSaveKey))
                    previous = SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey);
                writeAttempted = true;
                SavesSystem.Save<int>(BackMountainConfig.RaidMealSaveKey, mealTypeId);
                if (SavesSystem.Load<int>(BackMountainConfig.RaidMealSaveKey) != mealTypeId)
                    throw new InvalidOperationException("meal_readback_mismatch");
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
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐登记写入失败: " + e.Message);
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
                // 只有真实出击能消费餐食；菜单、基地与非出击关卡都不算一局。
                if (LevelManager.Instance == null || LevelManager.Instance.IsBaseLevel
                    || !LevelManager.Instance.IsRaidMap) return;
                RaidUtilities.RaidInfo raid = RaidUtilities.CurrentRaid;
                int slot = SavesSystem.CurrentSlot;
                if (!raid.valid || raid.ended || raid.dead || slot < 0) return;
                if (_activeMeal != 0 && (_activeRaidId != raid.ID || _activeSlot != slot)) ClearForRun();

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;
                if (_activeMeal != 0 && _appliedCharacter == main && _records.Count > 0) return;

                bool pending = _activeMeal == 0;
                int mealTypeId = pending ? ReadRegisteredMeal() : _activeMeal;
                if (mealTypeId == 0) return;
                BackMountainItems.Definition def = BackMountainItems.GetDefinition(mealTypeId);
                // 陌生旧记录保留原值，不以“修复”为由清除玩家数据。
                if (def == null || def.IsSeed) return;

                RuntimeStatModifierTracker.RemoveAll(_records, "RaidMeal");
                _appliedCharacter = null;
                if (!TryApplyModifiers(main, mealTypeId))
                {
                    RuntimeStatModifierTracker.RemoveAll(_records, "RaidMeal");
                    Duckov.UI.NotificationText.Push(L10n.T(
                        "出击餐属性尚未就绪，餐食记录已保留。",
                        "Meal stats are not ready; your meal record has been kept."));
                    return;
                }
                // 所有属性装配成功后才结算；写入失败也摘掉刚挂的效果，避免半份餐或白吃。
                if (pending && !ClearRegisteredMeal())
                {
                    RuntimeStatModifierTracker.RemoveAll(_records, "RaidMeal");
                    Duckov.UI.NotificationText.Push(L10n.T(
                        "出击餐登记暂时无法结算，本局未消耗也未生效。",
                        "The meal record could not be settled; it was neither consumed nor applied."));
                    return;
                }
                _activeMeal = mealTypeId;
                _activeRaidId = raid.ID;
                _activeSlot = slot;
                _appliedCharacter = main;
                if (pending) ModBehaviour.Instance?.ShowMessage(
                    L10n.T("出击餐生效：", "Meal in effect: ") + L10n.T(def.NameCN, def.NameEN));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐应用失败: " + e.Message);
            }
        }

        private static bool TryApplyModifiers(CharacterMainControl main, int mealTypeId)
        {
            switch (mealTypeId)
            {
                case BossRushItemIds.DragonFruit:
                    return AddModifier(main, ZombieModeStatNames.GunDamageMultiplier, DragonFruitDamageBonus)
                        && AddModifier(main, ZombieModeStatNames.MeleeDamageMultiplier, DragonFruitDamageBonus);
                case BossRushItemIds.EmberChili:
                    return AddModifier(main, ZombieModeStatNames.RunSpeed, EmberChiliSpeedBonus)
                        && AddModifier(main, ZombieModeStatNames.WalkSpeed, EmberChiliSpeedBonus)
                        // 官方换弹时间 = 武器时间 / (1 + ReloadSpeedGain)。Gain 基础值为零，须加值。
                        && RuntimeStatModifierTracker.TryAdd(main, ZombieModeStatNames.ReloadSpeedGain,
                            EmberChiliReloadBonus, _modifierSource, _records, "RaidMeal", ModifierType.Add);
                case BossRushItemIds.PhantomMushroom:
                    return AddModifier(main, ZombieModeStatNames.ElementFactorPhysics,
                        PhantomMushroomPhysicsDamageReduction);
                default:
                    return false;
            }
        }

        private static bool AddModifier(CharacterMainControl character, string statName, float percent)
        {
            return RuntimeStatModifierTracker.TryAdd(
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
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐清理失败: " + e.Message);
            }
            finally
            {
                _activeMeal = 0;
                _activeSlot = -1;
                _activeRaidId = 0;
                _appliedCharacter = null;
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
