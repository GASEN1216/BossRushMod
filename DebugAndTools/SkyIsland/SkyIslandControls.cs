using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class SkyIslandControls
    {
        /// <summary>三个按钮共用同一句「先进岛」提示，中英各写一遍容易漂。</summary>
        private static string EnterFirst
        { get { return L10n.T("请先进入天空岛", "Enter the Sky Islands first"); } }

        internal static void Build(Transform parent, ModBehaviour host, Action closeMenu, Action<string, bool> report)
        {
            GameObject panel = new GameObject("SkyIslandControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image background = panel.GetComponent<Image>();
            background.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(background, 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan Archipelago"), 24, 34);
            // 「蓝环 / 绿环」现在是真的：撤离点由 SkyIslandExtractionRings 画出贴地圆环，
            // 半径等于 SkyIslandSession.ExtractionRadius，钟庭那个在敲钟结局后才出现。
            Label(panel.transform, L10n.T("基地船点可正式出发。岛上按地图键查阅全岛；站进码头的蓝环停留 3 秒返航，敲响归航钟后钟庭的绿环同样可用。",
                "Depart from the base boat. Use the map key on the island; stand in the blue ring at the dock for 3 seconds to return. After the Homecoming Bell rings, the green ring at the Bell Court works the same way."), 17, 66);
            Button(panel.transform, L10n.T("从基地前往天空岛", "Depart base for Sky Islands"), BossRushUIColors.Success, delegate
            {
                string reason;
                if (!SkyIslandSession.CanEnter(host, out reason)) { report(reason, true); return; }
                closeMenu();
                SkyIslandSession.Enter(host, report);
            });
            Button(panel.transform, L10n.T("打开天空岛地图", "Open the Sky Island map"), BossRushUIColors.Accent, delegate
            {
                SkyIslandSession session = host.GetComponent<SkyIslandSession>();
                if (session == null) { report(EnterFirst, true); return; }
                closeMenu(); session.OpenMap();
            });
            SkyIslandSession currentSession = host.GetComponent<SkyIslandSession>();
            string lightName = currentSession == null ? L10n.T("晴昼", "Daylight") : currentSession.LightingPresetName;
            Button lightButton = Button(panel.transform, L10n.T("光色：", "Lighting: ") + lightName +
                L10n.T(" · 点击切换", " · Cycle preset"), BossRushUIColors.Accent, delegate
            {
                SkyIslandSession session = host.GetComponent<SkyIslandSession>();
                if (session == null || !session.CanChangeLighting)
                { report(L10n.T("请等待天空岛就绪", "Wait for the Sky Islands to finish loading"), true); return; }
                closeMenu(); session.CycleLighting();
            });
            lightButton.interactable = currentSession != null && currentSession.CanChangeLighting;
            GameObject row = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 8;
            row.AddComponent<LayoutElement>().preferredHeight = 44;
            Button(row.transform, L10n.T("巡览下一地标", "Visit next landmark"), BossRushUIColors.Accent, delegate
            {
                SkyIslandSession session = host.GetComponent<SkyIslandSession>();
                if (session == null) { report(EnterFirst, true); return; }
                closeMenu(); session.VisitNextLandmark();
            });
            Button(row.transform, L10n.T("本区测试敌人", "Local test enemy"), BossRushUIColors.Warning, delegate
            {
                SkyIslandSession session = host.GetComponent<SkyIslandSession>();
                if (session == null) { report(EnterFirst, true); return; }
                closeMenu(); session.SpawnEnemy();
            });
            Button(row.transform, L10n.T("立即返回基地", "Return to base"), BossRushUIColors.Danger, delegate
            {
                SkyIslandSession session = host.GetComponent<SkyIslandSession>();
                if (session == null)
                { report(L10n.T("当前没有天空岛会话", "There is no Sky Island session right now"), false); return; }
                closeMenu(); session.Close(true, "manual_return");
            });
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
            // 只给下限、不钉死高度：LayoutElement 的布局优先级高于 TMP，钉死 preferredHeight 会让
            // 纵向布局永远取这个数，长说明（尤其英文）折行后多出来的行直接画到下面的按钮上。
            child.GetComponent<LayoutElement>().minHeight = height;
            return text;
        }

        private static Button Button(Transform parent, string label, Color color, Action action)
        {
            GameObject child = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            child.transform.SetParent(parent, false);
            Image image = child.GetComponent<Image>();
            image.color = color;
            BossRushUI.ApplyPanelSkin(image, 8);
            child.GetComponent<LayoutElement>().flexibleWidth = 1;
            child.GetComponent<LayoutElement>().preferredHeight = 44;
            child.GetComponent<Button>().targetGraphic = image;
            child.GetComponent<Button>().onClick.AddListener(() => action());
            TextMeshProUGUI text = Label(child.transform, label, 18, 44);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(6, 0);
            text.rectTransform.offsetMax = new Vector2(-6, 0);
            return child.GetComponent<Button>();
        }
    }
}
