// ============================================================================
// ModeEHarmonyPatch.cs - Mode E Harmony Patches
// ============================================================================
// 包含：
//   1. SetTeam 阵营保护 Patch：阻止原版防作弊逻辑篡改玩家阵营
//   2. HealthBar 友方血条绿色 Patch：同阵营单位血条显示为绿色
//   3. InteractableBase.OnTimeOut Patch：拦截商人主交互，执行召唤煤球
// 注意：HealthBar 名字后缀 patch 已合并至 ModeFUI.cs 的 BossRushHealthBarNamePatch
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Duckov.UI;
using Duckov.Economy;
using Duckov.Economy.UI;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    [HarmonyPatch(typeof(Health), "Hurt", new Type[] { typeof(DamageInfo) })]
    public static class ModeEHiredBossKillAttributionPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> source)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(source);
            FieldInfo isDeadField = AccessTools.Field(typeof(Health), "isDead");
            MethodInfo attributeMethod = AccessTools.Method(
                typeof(ModeEHiredBossKillAttributionPatch),
                nameof(AttributeKillToOwner));
            if (isDeadField == null || attributeMethod == null)
            {
                Debug.LogError("[ModeE/Hire] Health.Hurt kill attribution binding failed");
                return instructions;
            }

            int insertIndex = -1;
            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction instruction = instructions[i];
                if (instruction.opcode != OpCodes.Stfld ||
                    !object.Equals(instruction.operand, isDeadField))
                {
                    continue;
                }

                if (insertIndex >= 0)
                {
                    Debug.LogError("[ModeE/Hire] Health.Hurt has multiple isDead writes");
                    return instructions;
                }
                insertIndex = i + 1;
            }

            if (insertIndex < 0)
            {
                Debug.LogError("[ModeE/Hire] Health.Hurt death point was not found");
                return instructions;
            }

            instructions.InsertRange(insertIndex, new[]
            {
                new CodeInstruction(OpCodes.Ldarga_S, (byte)1),
                new CodeInstruction(OpCodes.Call, attributeMethod)
            });
            return instructions;
        }

        public static void AttributeKillToOwner(ref DamageInfo damageInfo)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive ||
                damageInfo.fromCharacter == null)
            {
                return;
            }

            inst.AttributeModeEHiredBossKillToOwner(ref damageInfo);
        }
    }

    [HarmonyPatch(typeof(StockShop), "Buy", new Type[] { typeof(int), typeof(int) })]
    public static class ModeEShellBuyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            StockShop __instance,
            int itemTypeID,
            int amount,
            ref UniTask<bool> __result)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null) return true;

            ModeEShellShopPatchDisposition disposition =
                inst.GetModeEShellShopPatchDisposition(__instance);
            if (disposition == ModeEShellShopPatchDisposition.PassOriginal) return true;
            if (disposition == ModeEShellShopPatchDisposition.Block)
            {
                __result = UniTask.FromResult(false);
                return false;
            }

            __result = inst.BuyModeEShellItemAsync(__instance, itemTypeID, amount);
            return false;
        }
    }

    [HarmonyPatch(typeof(StockShop), "Sell", new Type[] { typeof(Item) })]
    public static class ModeEShellSellPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(StockShop __instance, Item target, ref UniTask __result)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ShouldBypassModeEShellSellPatch(__instance)) return true;

            ModeEShellShopPatchDisposition disposition =
                inst.GetModeEShellShopPatchDisposition(__instance);
            if (disposition == ModeEShellShopPatchDisposition.PassOriginal) return true;
            if (disposition == ModeEShellShopPatchDisposition.Block)
            {
                __result = UniTask.CompletedTask;
                return false;
            }

            __result = inst.WrapModeEShellSellAsync(__instance, target);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(StockShopItemEntry),
        "Setup",
        new Type[] { typeof(StockShopView), typeof(StockShop.Entry) })]
    public static class ModeEShellShopItemEntryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            StockShopItemEntry __instance,
            StockShopView master,
            StockShop.Entry entry)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null)
            {
                inst.ApplyModeEShellItemEntryUi(__instance, master, entry);
            }
        }
    }

    [HarmonyPatch(typeof(StockShopView), "Setup", new Type[] { typeof(StockShop) })]
    public static class ModeEMerchantShopViewSetupReusePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            StockShopView __instance,
            StockShop target,
            ref ModeEMerchantSellAllUI.ProgressiveShopViewSetupState __state)
        {
            __state = null;
            if (ModeEMerchantSellAllUI.CanReuseShopViewSetup(__instance, target))
            {
                return false;
            }

            ModeEMerchantSellAllUI.BeginShopViewSetup(target);
            __state = ModeEMerchantSellAllUI.PrepareProgressiveShopViewSetup(
                __instance,
                target);
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(
            StockShopView __instance,
            StockShop target,
            ModeEMerchantSellAllUI.ProgressiveShopViewSetupState __state)
        {
            ModeEMerchantSellAllUI.CompleteProgressiveShopViewSetup(
                __instance,
                target,
                __state,
                true);
            ModeEMerchantSellAllUI.EndShopViewSetup();
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            StockShopView __instance,
            StockShop target,
            ModeEMerchantSellAllUI.ProgressiveShopViewSetupState __state)
        {
            ModeEMerchantSellAllUI.CompleteProgressiveShopViewSetup(
                __instance,
                target,
                __state,
                __exception == null);
            ModeEMerchantSellAllUI.EndShopViewSetup();
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(Duckov.UI.InventoryDisplay),
        "Setup",
        new Type[]
        {
            typeof(Inventory),
            typeof(Func<Item, bool>),
            typeof(Func<Item, bool>),
            typeof(bool),
            typeof(Func<Item, bool>)
        })]
    public static class ModeEMerchantInventoryDisplaySetupReusePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Duckov.UI.InventoryDisplay __instance, Inventory target)
        {
            return !ModeEMerchantSellAllUI.CanReuseInventoryDisplaySetup(__instance, target);
        }
    }

    [HarmonyPatch(typeof(StockShopView), "RefreshInteractionButton")]
    public static class ModeEShellInteractionButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(StockShopView __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null)
            {
                inst.ApplyModeEShellInteractionButtonUi(__instance);
            }
        }
    }

    /// <summary>
    /// Patch CharacterMainControl.SetTeam：
    /// Mode E 中阻止主角阵营被篡改为 Teams.all
    /// </summary>
    [HarmonyPatch(typeof(CharacterMainControl), "SetTeam")]
    public static class ModeESetTeamPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CharacterMainControl __instance, Teams _team)
        {
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive)
                return true;

            // 已雇佣 Boss 的阵营由雇佣事务唯一拥有，阻止托管技能改回敌对/中立。
            if (inst.ShouldBlockModeEHiredBossTeamChange(__instance, _team))
                return false;

            // 主角仅需要阻止原版控制逻辑改为 Teams.all。
            if (_team != Teams.all)
                return true;

            // 只保护主角
            if (!__instance.IsMainCharacter)
                return true;

            // 阻止 SetTeam(Teams.all)，保持玩家正确阵营
            return false;
        }
    }

    /// <summary>
    /// Patch InteractableBase.OnTimeOut：
    /// Mode E 中拦截神秘商人主交互，执行召唤煤球逻辑
    /// </summary>
    [HarmonyPatch(typeof(InteractableBase), "OnTimeOut")]
    public static class ModeEMerchantInteractPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InteractableBase __instance)
        {
            // 非 Mode E 时放行
            var inst = ModBehaviour.Instance;
            if (inst == null || (!inst.IsModeEActive && !inst.IsModeFActive))
                return true;

            // 检查是否是商人主交互
            if (inst.ModeEMerchantMainInteract == null || __instance != inst.ModeEMerchantMainInteract)
                return true;

            // 拦截并执行召唤煤球
            ModeEPetSpawner.SpawnPet();

            // 返回 false 阻止原版 OnTimeOut 执行
            return false;
        }
    }

    /// <summary>
    /// Patch HealthBar.Refresh：
    /// Mode E 中将同阵营友方单位的血条颜色覆盖为绿色
    /// [性能优化] 缓存 ModBehaviour.Instance 引用，减少每帧属性访问
    /// </summary>
    [HarmonyPatch(typeof(HealthBar), "Refresh")]
    public static class ModeEHealthBarColorPatch
    {
        /// <summary>友方血条绿色（鲜明的绿色，易于辨识）</summary>
        private static readonly Color AllyHealthBarColor = new Color(0.2f, 0.9f, 0.2f, 1f);

        /// <summary>缓存的 ModBehaviour 实例引用</summary>
        private static ModBehaviour cachedInstance;
        /// <summary>上次刷新缓存的帧号（用帧号代替计数器，避免多 HealthBar 实例竞态导致刷新频率不稳定）</summary>
        private static int lastRefreshFrame = -1;

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, Image ___fill)
        {
            // 每 60 帧刷新一次缓存（约每秒一次，不受多实例并发影响）
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            // 非 Mode E 时跳过（快速路径）
            if (cachedInstance == null || !cachedInstance.IsModeEActive)
                return;

            // 获取血条绑定的 Health 目标
            Health target = __instance.target;
            if (target == null) return;

            // 获取角色
            CharacterMainControl character = target.TryGetCharacter();
            if (character == null || character.IsMainCharacter) return;

            // 判断是否与玩家同阵营
            if (character.Team == cachedInstance.ModeEPlayerFaction)
            {
                // 同阵营友方：覆盖血条颜色为绿色
                if (___fill != null)
                {
                    ___fill.color = AllyHealthBarColor;
                }
            }
        }
    }

    /// <summary>
    /// Patch StockShop.GetItemInstanceDirect：
    /// Mode E 子弹商店：修改用于 UI 显示的示例对象的 StackCount，让界面上算对整组金额、并显示数量
    /// </summary>
    [HarmonyPatch(typeof(Duckov.Economy.StockShop), "GetItemInstanceDirect")]
    public static class ModeEBulletShopUIDisplayPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Duckov.Economy.StockShop __instance, ref ItemStatsSystem.Item __result)
        {
            var inst = ModBehaviour.Instance;
            if (inst == null)
            {
                return;
            }

            if (inst.IsCurrentModeEShellCapability(__instance))
            {
                inst.NormalizeModeEShellStackForShop(__instance, __result);
                return;
            }

            // Mode F 继续沿用原子弹满堆显示，不继承贝壳经济。
            if (inst.IsModeFActive && __instance.MerchantID == "ModeE_Bullet" &&
                __result != null && __result.Stackable)
            {
                __result.StackCount = __result.MaxStackCount;
            }
        }
    }

    /// <summary>
    /// Patch ItemUtilities.SendToPlayerCharacterInventory：
    /// Mode E 子弹商店：在原版把交易物送进背包前，先把真正买到的弹药实例补成满组。
    /// 原版 StockShop.BuyTask 总是只实例化一件物品，Buy(amount) 只影响库存，不影响到手堆叠。
    /// </summary>
    [HarmonyPatch(typeof(global::ItemUtilities), "SendToPlayerCharacterInventory")]
    public static class ModeEBulletShopPurchaseStackPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemStatsSystem.Item item)
        {
            var inst = ModBehaviour.Instance;
            // Mode E 专用交易使用捕获商店和统一规范化 helper；该旧 Prefix 仅保留 Mode F 兼容。
            if (inst == null || inst.IsModeEActive || !inst.IsModeFActive ||
                item == null || !item.Stackable)
            {
                return;
            }

            var shopView = Duckov.Economy.UI.StockShopView.Instance;
            var targetShop = shopView != null ? shopView.Target : null;
            if (targetShop == null || targetShop.MerchantID != "ModeE_Bullet")
            {
                return;
            }

            if (!string.Equals(item.FromInfoKey, "UI_Trade", StringComparison.Ordinal))
            {
                return;
            }

            if (item.StackCount < item.MaxStackCount)
            {
                item.StackCount = item.MaxStackCount;
            }
        }
    }

}
