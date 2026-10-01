#if BOSSRUSH_DEV
// ============================================================================
// SkyIslandProgressDebugControls.cs - F3「NPC/剧情」页的天空岛进度区（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-10-01：F3 里加一个「清空天空岛剧情 / 任务进度」的按键，方便从 Jeff 的序章重新走一遍。
// - 宿主 ModBehaviour 的 partial 预算已满（tests/ModBehaviourPartialBudgetGuard.py），这里自建面板（版式同 CampaignPetNestDebugControls），
//   由同页的 CampaignPetNestDebugControls.Build 末尾挂上，宿主不加一行。
// - 不过「专用测试档」门：这是 owner 在自己档上手动按的按钮，先弹 BossRushConfirmDialog 确认，只许岛外执行。
//   写入走 SkyIslandStoryService.DevResetCurrentSlot（共享 store + 立即落盘 + 读回核对）；
//   官方任务日志里的 590001、590011–590013 由投影核心按清空后的事实下一拍自动撤回。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class SkyIslandProgressDebugControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action<string, bool> report)
        {
            GameObject panel = new GameObject("SkyIslandProgressDebugControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("天空岛进度", "Sky Islands progress"), 24, 38);
            Label(panel.transform,
                L10n.T("把当前存档的天空岛剧情和任务清回新档，从 Jeff 的「云上的坐标」重新开始。背包和仓库里的物品不动。请在基地按。",
                    "Reset this save's Sky Islands story and quests to a fresh start, beginning again with Jeff's Coordinates Above the Clouds. Items in your backpack and storage are kept. Use it at base."),
                18, 62);

            GameObject child = new GameObject("ResetSkyIslandProgress", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            child.transform.SetParent(panel.transform, false);
            Image image = child.GetComponent<Image>();
            image.color = BossRushUIColors.Danger;
            BossRushUI.ApplyPanelSkin(image, 8);
            child.GetComponent<LayoutElement>().preferredHeight = 44;
            child.GetComponent<Button>().targetGraphic = image;
            child.GetComponent<Button>().onClick.AddListener(() => Confirm(host, report, panel));
            TextMeshProUGUI text = Label(child.transform, L10n.T("清空天空岛剧情/任务", "Reset Sky Islands story/quests"), 18, 44);
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

        private static void Confirm(ModBehaviour host, Action<string, bool> report, GameObject anchor)
        {
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("清空天空岛的剧情和任务？", "Reset the Sky Islands story and quests?"),
                Target = L10n.T("存档槽 ", "Save slot ") + Saves.SavesSystem.CurrentSlot,
                Body = L10n.T("序章、航线、三条主线任务、航标、折翎与钟守的结果、手记、来信、点亮的灯和蛙卵都回到新档。背包和仓库里的物品不动。",
                    "The prelude, route, the three main quests, the beacons, the outcomes with Zheling and the Bell Keeper, the journal, letters, lit lamps and frogspawn all go back to a fresh save. Items in your backpack and storage are kept."),
                Warning = L10n.T("会立即写进存档，不能撤销。", "This is saved immediately and can't be undone."),
                ConfirmLabel = L10n.T("清空天空岛进度", "Reset Sky Islands progress"),
                Danger = true,
                OnConfirm = () => Reset(host, report),
                Anchor = anchor
            });
        }

        private static void Reset(ModBehaviour host, Action<string, bool> report)
        {
            string detail;
            bool ok = SkyIslandStoryService.DevResetCurrentSlot(host, out detail);
            Debug.Log("[SkyIsland] F3_STORY_RESET ok=" + ok + " detail=" + detail);
            if (ok)
            {
                report(L10n.T("天空岛剧情和任务已清空，去找 Jeff 重新开始。", "Sky Islands story and quests reset. Talk to Jeff to start over."), false);
                return;
            }
            report(detail == "on_island"
                ? L10n.T("请先回基地再清空。", "Go back to base first.")
                : L10n.T("清空失败：", "Reset failed: ") + detail, true);
        }
    }
}
#endif
