#if BOSSRUSH_DEV
// ============================================================================
// CollectiblesDebugControls.cs - F3「NPC/剧情」页的收集品区（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-11-19：收集品 100 件、收藏册与杰夫引导 590217 的人工测试捷径，尽量少等运气：
// - 下一只箱子必出收集品 / 出现倍率 ×10 与恢复：只改本次运行的内存开关，不写存档。
// - 给一件收集品（1–6 档随机 / 7 档 / 8 档）：直接进背包，放不下进仓库。
// - 收藏册全登记 / 清空：会写存档，只许专用测试档、只许在基地，先弹确认。
// 宿主 ModBehaviour 的 partial 预算已满，这里自建面板，由 CampaignPetNestDebugControls.Build 末尾挂上。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class CollectiblesDebugControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action<string, bool> report)
        {
            GameObject panel = new GameObject("CollectiblesDebugControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("收集品与收藏册", "Collectibles and album"), 24, 38);
            Label(panel.transform,
                L10n.T("必出与倍率只影响这次运行；给物品直接进背包。全登记 / 清空会写存档，只限专用测试档、在基地用。",
                    "Force and multiplier only affect this session; given items go straight to your backpack. Register-all / clear write the save: test save only, at base."),
                18, 64);
            Button(panel.transform, L10n.T("下一只箱子必出收集品", "Next crate always has a collectible"), BossRushUIColors.SurfaceRaised, () =>
            {
                CollectibleLootRuntime.DevForceNext = true;
                report(L10n.T("下一只第一次打开的箱子必出一件", "The next first-opened crate will hold one"), false);
            });
            Button(panel.transform, L10n.T("出现倍率 ×10 / 恢复 ×1", "Chance ×10 / back to ×1"), BossRushUIColors.SurfaceRaised, () =>
            {
                CollectibleLootRuntime.DevChanceMultiplier = CollectibleLootRuntime.DevChanceMultiplier > 1f ? 1f : 10f;
                report(L10n.T("出现倍率：×", "Chance multiplier: ×") + CollectibleLootRuntime.DevChanceMultiplier, false);
            });
            Button(panel.transform, L10n.T("给一件 1–6 档收集品", "Give a tier 1–6 collectible"), BossRushUIColors.SurfaceRaised, () => Give(report, 1, 6));
            Button(panel.transform, L10n.T("给一件 7 档珍品", "Give a tier 7 treasure"), BossRushUIColors.SurfaceRaised, () => Give(report, 7, 7));
            Button(panel.transform, L10n.T("给初代鸭王金蛋（8 档）", "Give the First Duck King's Golden Egg (tier 8)"), BossRushUIColors.SurfaceRaised, () => Give(report, 8, 8));
            Button(panel.transform, L10n.T("收藏册全部登记", "Register the whole album"), BossRushUIColors.SurfaceRaised, () => Confirm(host, report, panel, true));
            Button(panel.transform, L10n.T("清空收藏册与杰夫引导", "Clear the album and Jeff's quest"), BossRushUIColors.Danger, () => Confirm(host, report, panel, false));
        }

        private static void Give(Action<string, bool> report, int minQuality, int maxQuality)
        {
            CollectiblesRuntimeModule module = CollectiblesRuntimeModule.Current;
            if (module == null) { report(L10n.T("收集品模块未加载", "Collectibles module not loaded"), true); return; }
            var pool = new System.Collections.Generic.List<CollectibleDefinition>();
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def != null && def.Quality >= minQuality && def.Quality <= maxQuality) pool.Add(def);
            }
            if (pool.Count == 0) { report(L10n.T("这一档没有收集品", "No collectible in that tier"), true); return; }
            CollectibleDefinition pick = pool[UnityEngine.Random.Range(0, pool.Count)];
            bool ok = module.DevGive(pick.TypeId);
            report(ok ? L10n.T("已给：", "Given: ") + CollectiblesLocalization.Name(pick) : L10n.T("物品实例化失败", "Failed to create the item"), !ok);
        }

        private static void Confirm(ModBehaviour host, Action<string, bool> report, GameObject anchor, bool registerAll)
        {
            string reason;
            if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = registerAll ? L10n.T("收藏册全部登记？", "Register the whole album?") : L10n.T("清空收藏册？", "Clear the album?"),
                Target = L10n.T("存档槽 ", "Save slot ") + Saves.SavesSystem.CurrentSlot,
                Body = registerAll
                    ? L10n.T("100 件全部记为已登记，全部系列记为已领奖（不发奖金，不收物品）；系列加成立即生效。",
                        "All 100 are marked as logged and every set as claimed (no cash, no items taken); set bonuses apply immediately.")
                    : L10n.T("已登记的收集品、集齐奖励和杰夫引导 590217 全部回到新档；售货机下次进基地不再上架收藏册。",
                        "Logged collectibles, set rewards and Jeff's quest 590217 all return to a fresh save; the vending machine stops stocking the album from the next base visit."),
                Warning = L10n.T("会立即写进存档，不能撤销。", "This is saved immediately and can't be undone."),
                ConfirmLabel = registerAll ? L10n.T("全部登记", "Register all") : L10n.T("清空", "Clear"),
                Danger = !registerAll,
                OnConfirm = () => Apply(host, report, registerAll),
                Anchor = anchor
            });
        }

        private static void Apply(ModBehaviour host, Action<string, bool> report, bool registerAll)
        {
            string reason;
            if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }
            CollectiblesRuntimeModule module = CollectiblesRuntimeModule.Current;
            string detail = null;
            bool ok = module != null && module.DevSetAll(registerAll, out detail);
            Debug.Log(CollectibleTuning.LogPrefix + "F3_ALBUM_" + (registerAll ? "REGISTER_ALL" : "CLEAR") + " ok=" + ok + " detail=" + detail);
            report(ok ? L10n.T("已完成", "Done") : L10n.T("失败：", "Failed: ") + (detail ?? L10n.T("模块未加载", "module not loaded")), !ok);
        }

        private static void Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject child = new GameObject("CollectiblesDebugButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            child.transform.SetParent(parent, false);
            Image image = child.GetComponent<Image>();
            image.color = color;
            BossRushUI.ApplyPanelSkin(image, 8);
            child.GetComponent<LayoutElement>().preferredHeight = 44;
            child.GetComponent<Button>().targetGraphic = image;
            child.GetComponent<Button>().onClick.AddListener(onClick);
            TextMeshProUGUI text = Label(child.transform, label, 18, 44);
            text.color = BossRushUI.GetButtonTextColor(image.color);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8, 0);
            text.rectTransform.offsetMax = new Vector2(-8, 0);
        }

        private static TextMeshProUGUI Label(Transform parent, string value, int size, float height)
        {
            GameObject child = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            child.transform.SetParent(parent, false);
            TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.text = value;
            text.fontSize = size;
            text.color = BossRushUIColors.TextPrimary;
            text.raycastTarget = false;
            child.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }
    }
}
#endif
