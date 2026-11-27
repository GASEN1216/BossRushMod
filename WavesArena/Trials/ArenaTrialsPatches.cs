// ============================================================================
// ArenaTrialsPatches.cs - 出征令噩梦常驻诅咒（失明 / 断补 / 血债）要拦的官方入口
// ============================================================================
// 每个补丁第一句都是 ArenaTrials.Active == null 早返：不是出征令的局、或不是噩梦档时 O(1) 放行，原版行为逐字不变。
//   - CA_UseItem.OnStart：断补禁药期间主玩家用不了道具（读条根本不开始，手持物不切换）；
//   - CA_UseItem.OnUpdateAction：断补下主玩家用道具的读条走一半速度（UpdateAction 先加了 deltaTime，这里扣回一部分）；
//   - HealthBar.Refresh / ShowDamageBar：失明下本局敌方身体的血条只剩几格、不弹单次掉血条；
//   - HealthBar.RefreshCharacterIcon（后缀）：首领词缀的名缀写在官方名字前（「分裂·狂暴 光头哥」），
//     词缀身体写「分身 / 双生 / 亲卫 / 影从」。官方只在血条挂上目标时写一次名字，所以用后缀接在它后面；
//     词缀是首领登记后才抽的，抽完由 ArenaTrialsHealthBarName.Refresh 主动让已经挂着的血条再写一次；
//   - Health.AddHealth：血债下主玩家的一切回血被挡下（药、食物、技能、别的 Mod 走官方入口的都算），
//     只有击杀回血（ArenaTrialsCurses 自己开闸）能过；
//   - Drug.CanBeUsed：血债下回血药对主玩家判成不可用。官方 CA_UseItem.OnFinish 读完条照扣数量 / 耐久，
//     不拦在这里就是「药扣了、血没回」（CR-2026-11-27-002）。同一件药还有别的用处（止血等）时照常能用，只是回血那一段跳过；
//   - CharacterMainControl.UseItem：只剩回血一种用处、因此用不了的药，换成血债的提示（不弹官方笼统的「无法使用」）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.ItemUsage;
using Duckov.UI;
using ItemStatsSystem;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    [HarmonyPatch(typeof(CA_UseItem), "OnStart")]
    internal static class ArenaTrialsUseItemSealPatch
    {
        private static float lastNoticeAt = -10f;

        [HarmonyPrefix]
        internal static bool Prefix(CA_UseItem __instance, ref bool __result)
        {
            if (ArenaTrials.Active == null || __instance == null) return true;
            if (!ArenaTrialsCurses.UseItemSealed(__instance.characterController)) return true;
            __result = false;
            if (Time.unscaledTime - lastNoticeAt > 1.2f)
            {
                lastNoticeAt = Time.unscaledTime;
                try { NotificationText.Push(L10n.T("药囊被封住了", "Your kit is sealed")); }
                catch (Exception) { }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CA_UseItem), "OnUpdateAction")]
    internal static class ArenaTrialsUseItemSlowPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(CA_UseItem __instance, float deltaTime, ref float ___actionTimer)
        {
            if (ArenaTrials.Active == null || __instance == null || deltaTime <= 0f) return;
            float scale = ArenaTrialsCurses.UseTimeScale(__instance.characterController);
            if (scale <= 1.0001f) return;
            ___actionTimer = Mathf.Max(0f, ___actionTimer - deltaTime * (1f - 1f / scale));
        }
    }

    [HarmonyPatch(typeof(HealthBar), "Refresh")]
    internal static class ArenaTrialsBlindHealthBarPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(HealthBar __instance, Image ___fill, Image ___followFill, Gradient ___colorOverAmount)
        {
            if (ArenaTrials.Active == null || __instance == null || ___fill == null) return true;
            Health target = __instance.target;
            int steps = ArenaTrialsCurses.BlindStepsFor(target);
            if (steps <= 0) return true;
            float max = target.MaxHealth;
            float ratio = max > 0f ? Mathf.Clamp01(target.CurrentHealth / max) : 0f;
            // 向上取整：还活着就至少剩一格；满血那格只有真满血才亮满
            float shown = ratio <= 0f ? 0f : Mathf.Ceil(ratio * steps - 0.0001f) / steps;
            ___fill.fillAmount = shown;
            if (___colorOverAmount != null) ___fill.color = ___colorOverAmount.Evaluate(shown);
            if (___followFill != null) ___followFill.fillAmount = shown;
            return false;
        }
    }

    [HarmonyPatch(typeof(HealthBar), "ShowDamageBar")]
    internal static class ArenaTrialsBlindDamageBarPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(HealthBar __instance)
        {
            if (ArenaTrials.Active == null || __instance == null) return true;
            return ArenaTrialsCurses.BlindStepsFor(__instance.target) <= 0;
        }
    }

    [HarmonyPatch(typeof(HealthBar), "RefreshCharacterIcon")]
    internal static class ArenaTrialsAffixNamePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(HealthBar __instance, TextMeshProUGUI ___nameText)
        {
            if (ArenaTrials.Active == null || __instance == null || ___nameText == null) return;
            if (!___nameText.gameObject.activeSelf) return;
            string prefix = ArenaTrialsAffixes.NamePrefixFor(__instance.target);
            if (string.IsNullOrEmpty(prefix)) return;
            ___nameText.richText = true;
            ___nameText.text = prefix + ___nameText.text;
        }
    }

    /// <summary>首领词缀抽完之后，让已经挂在它身上的官方血条重写一次名字（冷路径：每只首领 / 身体一次）。</summary>
    internal static class ArenaTrialsHealthBarName
    {
        internal static void Refresh(Health health)
        {
            if (health == null) return;
            try
            {
                HealthBarManager manager = HealthBarManager.Instance;
                if (manager == null) return;
                HealthBar bar = Traverse.Create(manager).Method("GetActiveHealthBar", new object[] { health }).GetValue<HealthBar>();
                if (bar != null) Traverse.Create(bar).Method("RefreshCharacterIcon").GetValue();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 血条名缀刷新失败: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Health), "AddHealth")]
    internal static class ArenaTrialsBloodDebtHealPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(Health __instance, float healthValue)
        {
            if (ArenaTrials.Active == null || healthValue <= 0f) return true;
            return !ArenaTrialsCurses.HealBlocked(__instance);
        }
    }

    [HarmonyPatch(typeof(Drug), "CanBeUsed")]
    internal static class ArenaTrialsBloodDebtDrugPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(Drug __instance, object user, ref bool __result)
        {
            if (ArenaTrials.Active == null || __instance == null || __instance.healValue <= 0) return true;
            CharacterMainControl character = user as CharacterMainControl;
            if (character == null || !character.IsMainCharacter || !ArenaTrialsCurses.HealBlocked(character.Health)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterMainControl), "UseItem")]
    internal static class ArenaTrialsBloodDebtUseNoticePatch
    {
        private static float lastNoticeAt = -10f;

        [HarmonyPrefix]
        internal static bool Prefix(CharacterMainControl __instance, Item item)
        {
            if (ArenaTrials.Active == null || __instance == null || item == null) return true;
            if (!OnlyHealsUnderBloodDebt(__instance, item)) return true;
            if (Time.unscaledTime - lastNoticeAt > 1.2f)
            {
                lastNoticeAt = Time.unscaledTime;
                try { NotificationText.Push(L10n.T("血债未清，伤药不灵", "Blood Debt: medicine won't heal you")); }
                catch (Exception) { }
            }
            return false;
        }

        /// <summary>血债下这件东西用不了、而且它带回血药（用不了正是因为回血被封）：冷路径，玩家每按一次用一次。</summary>
        private static bool OnlyHealsUnderBloodDebt(CharacterMainControl character, Item item)
        {
            try
            {
                if (!character.IsMainCharacter || !ArenaTrialsCurses.HealBlocked(character.Health)) return false;
                UsageUtilities usage = item.UsageUtilities;
                if (usage == null || usage.IsUsable(item, character)) return false;
                List<UsageBehavior> behaviors = usage.behaviors;
                for (int i = 0; behaviors != null && i < behaviors.Count; i++)
                {
                    Drug drug = behaviors[i] as Drug;
                    if (drug != null && drug.healValue > 0) return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
