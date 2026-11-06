#if BOSSRUSH_DEV
// ============================================================================
// TidegateProgressDebugControls.cs - F3「NPC/剧情」页的潮闸镇区（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-11-06：「你还可以做清空状态按钮啥的，尽可能减少人工测试」。三个按钮：
// - 清空潮闸镇进度：剧情存档回到新档、收走剧情链物品（信、怀表、手轮、防水袋），之后在基地等阿稳重新送信，整条线从头再走。
//   会写存档：只许专用测试档、只许在基地（F3GameplayValidationRunner.CanRunManualProgression），先弹 BossRushConfirmDialog 确认。
// - 潮相跳到下一相 / 潮相回到真实时钟：人工测试时不用干等涨落（一轮 270 秒）。只改这一趟的潮汐偏移（TidegateTide.DevOffsetHours），
//   不写官方时钟、不碰存档；出图时自动清零。
// 宿主 ModBehaviour 的 partial 预算紧（tests/ModBehaviourPartialBudgetGuard.py），这里自建面板，由同页的
// CampaignPetNestDebugControls.Build 末尾挂上（版式同 SkyIslandProgressDebugControls）。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class TidegateProgressDebugControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action<string, bool> report)
        {
            GameObject panel = new GameObject("TidegateProgressDebugControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("潮闸镇", "Tidegate"), 24, 38);
            Label(panel.transform,
                L10n.T("清空进度：剧情回到新档，收走信、潮汐怀表、阀门手轮和防水袋，在基地等阿稳重新送信（只限专用测试档）。跳潮相：在潮闸镇里用，不用干等涨落。",
                    "Reset progress: the story returns to a fresh save and the letter, tide pocket watch, valve handwheel and dry bag are removed; wait at base for Awen to deliver the letter again (test save only). Jump tide: use it inside Tidegate instead of waiting for the tide."),
                18, 84);
            Button(panel.transform, L10n.T("清空潮闸镇进度", "Reset Tidegate progress"), BossRushUIColors.Danger, () => Confirm(host, report, panel));
            Button(panel.transform, L10n.T("潮相跳到下一相", "Jump to the next tide phase"), BossRushUIColors.SurfaceRaised, () => JumpPhase(host, report));
            Button(panel.transform, L10n.T("潮相回到真实时钟", "Return the tide to the real clock"), BossRushUIColors.SurfaceRaised, () => ResetPhase(host, report));
        }

        private static void Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject child = new GameObject("TidegateDebugButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
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

        private static void Confirm(ModBehaviour host, Action<string, bool> report, GameObject anchor)
        {
            string reason;
            if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("清空潮闸镇进度？", "Reset Tidegate progress?"),
                Target = L10n.T("存档槽 ", "Save slot ") + Saves.SavesSystem.CurrentSlot,
                Body = L10n.T("信没送、没读，四页日志、交怀表都回到新档，竹筏和闸伯会从下水道消失。身上和仓库里的闸伯的信、潮汐怀表、阀门手轮、防水袋一并收走；鸭蹼脚掌和别的物品不动。",
                    "The letter is undelivered and unread, the four log pages and the watch hand-over return to a fresh save, and the raft and Old Zha leave the sewer. Old Zha's letter, the tide pocket watch, the valve handwheel and dry bags on you and in storage are removed; flippers and everything else stay."),
                Warning = L10n.T("会立即写进存档，不能撤销。", "This is saved immediately and can't be undone."),
                ConfirmLabel = L10n.T("清空潮闸镇进度", "Reset Tidegate progress"),
                Danger = true,
                OnConfirm = () => Reset(host, report),
                Anchor = anchor
            });
        }

        private static void Reset(ModBehaviour host, Action<string, bool> report)
        {
            string reason;
            if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }
            string detail;
            bool ok = TidegateStory.DevResetCurrentSlot(out detail);
            Debug.Log(TidegateRaidScene.LogTag + " F3_STORY_RESET ok=" + ok + " detail=" + detail);
            report(ok
                ? L10n.T("潮闸镇进度已清空。留在基地，阿稳半分钟内会把湿包裹寄到快递站。", "Tidegate progress reset. Stay at base; Awen will send the soaked parcel to the delivery point within half a minute.")
                : L10n.T("清空失败：", "Reset failed: ") + detail, !ok);
        }

        private static TidegateSession LiveSession(ModBehaviour host, Action<string, bool> report)
        {
            TidegateSession session = host == null ? null : host.GetComponent<TidegateSession>();
            if (session == null || session.IsClosed || !session.IsReady || session.Tide == null)
            {
                report(L10n.T("先乘竹筏进潮闸镇再拨潮相", "Enter Tidegate by raft first"), true);
                return null;
            }
            if (F3GameplayValidationRunner.IsRunning)
            {
                report(L10n.T("验收正在跑，别手动拨潮相", "A validation run is in progress; don't move the tide by hand"), true);
                return null;
            }
            return session;
        }

        private static void JumpPhase(ModBehaviour host, Action<string, bool> report)
        {
            TidegateSession session = LiveSession(host, report);
            if (session == null) return;
            TidegateTidePhase next = TidegateTide.DevNextPhase(session.Tide.Phase);
            TidegateTide.DevJumpInto(next, 2.0);
            report(L10n.T("潮相已拨到：", "Tide moved to: ") + TidegateText.PhaseName(next), false);
        }

        private static void ResetPhase(ModBehaviour host, Action<string, bool> report)
        {
            if (LiveSession(host, report) == null) return;
            TidegateTide.DevOffsetHours = 0.0;
            report(L10n.T("潮汐已回到真实时钟", "The tide follows the real clock again"), false);
        }
    }
}
#endif
