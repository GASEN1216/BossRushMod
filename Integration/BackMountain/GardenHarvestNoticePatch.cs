using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Cysharp.Threading.Tasks;
using Duckov.Crops;
using Duckov.Economy;
using HarmonyLib;
using ItemStatsSystem;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 后山产物先验资源并优先进背包，官方作物保留仓库路线；交付完成后提示真实去向。
    /// 仍只执行 Harvest 原有的一次发货，原 Forget 是任务与发货异常的唯一消费者。
    /// </summary>
    [HarmonyPatch(typeof(Crop), nameof(Crop.Harvest), new Type[0])]
    internal static class GardenHarvestNoticePatch
    {
        [HarmonyPrefix]
        internal static bool EnsureHarvestProduct(Crop __instance, ref bool __result)
        {
            try
            {
                if (__instance == null) return true;
                int productId = __instance.Info.resultNormal;
                BackMountainItems.Definition definition = BackMountainItems.GetDefinition(productId);
                if (definition == null || definition.IsSeed) return true;
                // Harvest 会在异步发货完成前销毁作物。先验证产物资源，避免缺资源时清掉地里的果实。
                if (BackMountainItems.EnsureRuntimeRegistration(productId)
                    && ItemAssetsCollection.GetPrefab(productId) != null) return true;
                Duckov.UI.NotificationText.Push(L10n.T("果实资源尚未就绪，请稍后再收获。", "Fruit resources are not ready. Please harvest again shortly."));
            }
            catch (Exception e) { ModBehaviour.DevLog("[BackMountain] 收获前验证失败: " + e.Message); }
            __result = false;
            return false;
        }

        internal static bool PreferPlayerInventory(Crop crop)
        {
            if (crop == null) return false;
            BackMountainItems.Definition definition = BackMountainItems.GetDefinition(crop.Info.resultNormal);
            return definition != null && !definition.IsSeed;
        }

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo delivery = AccessTools.Method(typeof(Cost), "Return",
                new[] { typeof(bool), typeof(bool), typeof(int), typeof(List<Item>) });
            MethodInfo forget = AccessTools.Method(typeof(UniTaskExtensions), "Forget", new[] { typeof(UniTask) });
            MethodInfo observe = AccessTools.Method(typeof(GardenHarvestNoticePatch), nameof(Observe));
            int found = -1;
            int matches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (delivery == null || codes[i].opcode != OpCodes.Call || !Equals(codes[i].operand, delivery)) continue;
                matches++;
                found = i;
            }
            int next = found + 1;
            while (next < codes.Count && codes[next].opcode == OpCodes.Nop) next++;
            if (matches != 1 || forget == null || observe == null || next >= codes.Count
                || codes[next].opcode != OpCodes.Call || !Equals(codes[next].operand, forget))
            {
                Debug.LogWarning("[BackMountain] 收获横幅绑定失败：官方 Harvest 的交付调用已变化，保留原收获流程。");
                return codes;
            }
            // 官方实参固定为 Return(false, false, 1, null)。只替换第二个 bool，
            // 后山果实直接进背包，放不下仍走官方仓库/快递；官方作物继续原路线。
            if (found < 4 || codes[found - 4].opcode != OpCodes.Ldc_I4_0
                || codes[found - 3].opcode != OpCodes.Ldc_I4_0
                || codes[found - 2].opcode != OpCodes.Ldc_I4_1 || codes[found - 1].opcode != OpCodes.Ldnull)
            {
                Debug.LogWarning("[BackMountain] 收获去向绑定失败：官方 Return 参数布局已变化，保留原收获流程。");
                return codes;
            }
            CodeInstruction route = codes[found - 3];
            route.opcode = OpCodes.Ldarg_0;
            route.operand = null;
            codes.Insert(found - 2, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(GardenHarvestNoticePatch), nameof(PreferPlayerInventory))));
            found++;
            codes.InsertRange(found + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, observe),
            });
            return codes;
        }

        internal static UniTask Observe(UniTask delivery, Crop crop)
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                CharacterMainControl player = CharacterMainControl.Main;
                if (owner == null || !owner.IsBackMountainConfiguredEnabled() || player == null || crop == null) return delivery;
                CropInfo info = crop.Info;
                if (info.resultNormal <= 0 || info.resultAmount <= 0) return delivery;
                return CompleteNotice(delivery, info.resultNormal, info.resultAmount, owner, player,
                    SavesSystem.CurrentSlot, SceneManager.GetActiveScene().handle);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BackMountain] 收获提示快照失败: " + e.Message);
                // 原 Forget 继续消费原任务；提示层失败不能吞掉或重做发货。
                return delivery;
            }
        }

        private static async UniTask CompleteNotice(UniTask delivery, int productId, int amount,
            ModBehaviour owner, CharacterMainControl player, int slot, int sceneHandle)
        {
            // 发货异常继续交给原 Forget，不发成功横幅。不要等待/读取已被回收的 Crop。
            await delivery;
            try
            {
                if (owner == null || owner != ModBehaviour.Instance || player == null
                    || player != CharacterMainControl.Main || slot != SavesSystem.CurrentSlot
                    || !owner.IsBackMountainConfiguredEnabled() || SceneLoader.IsSceneLoading
                    || sceneHandle != SceneManager.GetActiveScene().handle) return;
                // 取用时解析语言；异步期间切换语言后，物品名与正文仍保持一致。
                string name = ItemAssetsCollection.GetMetaData(productId).DisplayName;
                if (string.IsNullOrEmpty(name)) name = L10n.T("作物", "crop");
                BackMountainItems.Definition definition = BackMountainItems.GetDefinition(productId);
                bool fruit = definition != null && !definition.IsSeed;
                owner.ShowBigBanner(L10n.T(
                    "已收获 " + name + " ×" + amount + (fruit
                        ? "\n优先放进背包；背包满时请查基地仓库，满仓部分在马蜂自提点领取。"
                        : "\n请到基地仓库查看；满仓部分在马蜂自提点领取。"),
                    "Harvested " + name + " ×" + amount + (fruit
                        ? "\nSent to your backpack first; overflow goes to base storage, then Package Pickup."
                        : "\nCheck base storage; collect overflow at Package Pickup.")));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BackMountain] 收获横幅显示失败: " + e.Message);
            }
        }
    }
}
