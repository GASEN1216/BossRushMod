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
    /// 收获交付完成后提示去向，包含官方和后山作物。只包装 Harvest 内已有的任务，
    /// 不重发物品、不改清格流程；原 Forget 仍是任务与发货异常的唯一消费者。
    /// </summary>
    [HarmonyPatch(typeof(Crop), nameof(Crop.Harvest), new Type[0])]
    internal static class GardenHarvestNoticePatch
    {
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
                owner.ShowBigBanner(L10n.T(
                    "已收获 " + name + " ×" + amount + "\n请到基地仓库查看；满仓部分在马蜂自提点领取。",
                    "Harvested " + name + " ×" + amount + "\nCheck base storage; collect overflow at Package Pickup."));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BackMountain] 收获横幅显示失败: " + e.Message);
            }
        }
    }
}
