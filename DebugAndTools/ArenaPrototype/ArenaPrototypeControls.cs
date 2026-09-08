using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class ArenaPrototypeControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action closeMenu, Action<string, bool> report)
        {
            GameObject panel = new GameObject("ArenaPrototypeControls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(panel.GetComponent<Image>(), 10);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label(panel.transform, L10n.T("自建场景实验 · 石砌遗迹", "Custom Arena · Stone Ruins"), 24, 34);
            Label(panel.transform, L10n.T("30 米场地；仅空闲时进入。金环出生，红环刷敌，走入蓝环返回。测试敌人无掉落。",
                "30 m arena; enter while idle. Gold: spawn; red: enemy; blue: return. Test enemies have no loot."), 17, 52);
            Button(panel.transform, L10n.T("从基地出发 · 100 米石堡前哨", "Depart base · 100 m Stone Outpost"), BossRushUIColors.Success, delegate
            {
                string reason;
                if (!ArenaPrototypeSession.CanEnter(host, out reason)) { report(reason, true); return; }
                closeMenu();
                ArenaPrototypeSession.Enter(host, report, true);
            });
            GameObject row = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 10;
            row.AddComponent<LayoutElement>().preferredHeight = 44;
            Button(row.transform, L10n.T("进入自建场地", "Enter arena"), BossRushUIColors.Success, delegate
            {
                string reason;
                if (!ArenaPrototypeSession.CanEnter(host, out reason)) { report(reason, true); return; }
                closeMenu();
                ArenaPrototypeSession.Enter(host, report);
            });
            Button(row.transform, L10n.T("生成测试敌人", "Spawn test enemy"), BossRushUIColors.Warning, delegate
            {
                ArenaPrototypeSession session = host.GetComponent<ArenaPrototypeSession>();
                if (session == null) { report(L10n.T("请先进入自建场地", "Enter the arena first"), true); return; }
                closeMenu();
                session.SpawnEnemy();
            });
            Button(row.transform, L10n.T("退出并回收场地", "Return and clean up"), BossRushUIColors.Danger, delegate
            {
                ArenaPrototypeSession session = host.GetComponent<ArenaPrototypeSession>();
                if (session == null) { report(L10n.T("当前没有试验场", "No active arena"), false); return; }
                closeMenu();
                session.Close(true, "manual_exit");
            });
        }

        internal static TextMeshProUGUI CreateHud(out GameObject root)
        {
            root = new GameObject("ArenaPrototypeHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.Hud;
            ZombieModeUIHelper.ConfigureCanvasScaler(root.GetComponent<CanvasScaler>());
            TextMeshProUGUI text = Label(root.transform, string.Empty, 20, 90);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(920, 90);
            rect.anchoredPosition = new Vector2(0, -72);
            text.alignment = TextAlignmentOptions.Top;
            return text;
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

        private static void Button(Transform parent, string label, Color color, Action action)
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
        }
    }
}
