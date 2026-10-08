#if BOSSRUSH_DEV
// ============================================================================
// CampaignProgressDebugControls.cs - F3「NPC/剧情」页的鸭王征程进度区（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-10-08：F3 里加一个「清空鸭王征程进度」的按键，方便从第一章重新走一遍。
// - 宿主 ModBehaviour 的 partial 预算已满（tests/ModBehaviourPartialBudgetGuard.py），这里自建面板
//   （版式同 SkyIslandProgressDebugControls），由同页的 CampaignPetNestDebugControls.Build 末尾挂上，宿主不加一行。
// - 不过「专用测试档」门：这是 owner 在自己档上手动按的按钮，先弹 BossRushConfirmDialog 确认，只许基地执行。
//   写入走 CampaignProgressDevReset.DevResetCurrentSlot（共享 store + 立即落盘 + 读回核对）；
//   杰夫任务页与官方笔记图鉴由投影核心 / NoteBridge 按清空后的事实下一拍自动撤回。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class CampaignProgressDebugControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action<string, bool> report)
        {
            GameObject panel = new GameObject("CampaignProgressDebugControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10, BossRushUISkinPart.Card);
            BossRushUI.ApplyPanelStroke(panel.GetComponent<Image>(), 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("鸭王征程进度", "Duck King campaign progress"), 24, 38);
            Label(panel.transform,
                L10n.T("把当前存档的鸭王征程任务和剧情清回新档，从基地杰夫的任务页重新接第一章。后山设施会重新锁定。背包和仓库里的物品不动。请在基地按。",
                    "Reset this save's campaign quests and story, then accept chapter one from Jeff's quest page at base. Back Mountain facilities lock again. Items in your backpack and storage are kept. Use it at base."),
                18, 62);

            GameObject child = new GameObject("ResetCampaignProgress", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            child.transform.SetParent(panel.transform, false);
            Image image = child.GetComponent<Image>();
            BossRushUI.ApplyPanelSkin(image, 8, BossRushUISkinPart.Button);
            child.GetComponent<LayoutElement>().preferredHeight = 44;
            Button button = child.GetComponent<Button>();
            button.targetGraphic = image;
            BossRushUIKit.StyleSecondaryButton(button);
            BossRushUI.ApplyPanelStroke(image, 8, BossRushUISkinPart.Button, BossRushUIColors.DangerText);
            button.onClick.AddListener(() => Confirm(host, report, panel));
            TextMeshProUGUI text = Label(child.transform, L10n.T("清空鸭王征程进度", "Reset campaign progress"), 18, 44);
            text.color = BossRushUIColors.DangerText;
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
            int slot = Saves.SavesSystem.CurrentSlot;
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("清空鸭王征程的进度？", "Reset the Duck King campaign progress?"),
                Target = L10n.T("存档槽 ", "Save slot ") + slot,
                Body = L10n.T("六章契约、交付解锁、线索、一次性引导与杰夫任务页都回到新档；后山设施重新锁定。背包和仓库里的物品不动。",
                    "All six chapters, delivered unlocks, clues, one-time guides and Jeff's quest page go back to a fresh save; Back Mountain facilities lock again. Items in your backpack and storage are kept."),
                Warning = L10n.T("会立即写进存档，不能撤销。", "This is saved immediately and can't be undone."),
                ConfirmLabel = L10n.T("清空征程进度", "Reset campaign progress"),
                Danger = true,
                OnConfirm = () => Reset(host, slot, report),
                Anchor = anchor
            });
        }

        private static void Reset(ModBehaviour host, int slot, Action<string, bool> report)
        {
            string detail;
            bool ok = CampaignProgressDevReset.DevResetCurrentSlot(host, slot, out detail);
            Debug.Log("[Campaign] F3_PROGRESS_RESET ok=" + ok + " detail=" + detail);
            if (ok)
            {
                report(L10n.T("鸭王征程进度已清空，去基地杰夫的任务页重新接第一章。", "Campaign progress reset. Accept chapter one again from Jeff's quest page at base."), false);
                return;
            }
            report(detail == "not_in_base"
                ? L10n.T("请先回基地再清空。", "Go back to base first.")
                : detail == "slot_changed"
                ? L10n.T("存档槽已切换，请重新确认。", "The save slot changed. Please confirm again.")
                : detail == "save_in_progress"
                ? L10n.T("存档正在保存，请稍后重试。", "The game is saving. Please try again shortly.")
                : L10n.T("清空失败：", "Reset failed: ") + detail, true);
        }
    }
}
#endif
