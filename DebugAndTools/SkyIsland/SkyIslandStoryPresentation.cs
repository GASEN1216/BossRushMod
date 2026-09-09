using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class SkyIslandStoryPresentation : IDisposable
    {
        internal sealed class Choice
        {
            internal string Label;
            internal Func<string> Select;
            internal Choice(string label, Func<string> select) { Label = label; Select = select; }
        }
        private Canvas canvas;
        private TextMeshProUGUI body;
        private ZombieModeUIHelper.ModalInputLease input;
        internal bool Visible { get { return canvas != null; } }
        internal void Show(string title, string text, IList<Choice> choices)
        {
            Dispose();
            canvas = BossRushUI.CreateCanvasRoot("SkyIslandStory", BossRushUILayers.Modal, true);
            BossRushUI.CreateBackdrop(canvas.transform);
            RectTransform panel = MakeRect(canvas.transform, "StoryPanel", Vector2.zero, new Vector2(820, 660));
            Image surface = panel.gameObject.AddComponent<Image>();
            surface.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surface, 18);
            Label(panel, title, 30, new Vector2(0, 275), new Vector2(750, 55));
            body = Label(panel, text, 22, new Vector2(0, 139), new Vector2(744, 204));
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableAutoSizing = true; body.fontSizeMin = 17; body.fontSizeMax = 22;
            for (int i = 0; i < choices.Count; i++)
            {
                Choice choice = choices[i];
                Button(panel, choice.Label, new Vector2(0, 5 - i * 52), delegate
                {
                    string result = choice.Select();
                    if (body != null) body.text = result;
                });
            }
            Button(panel, "继续旅程 · ESC", new Vector2(0, -282), Dispose);
            input = ZombieModeUIHelper.ClaimModalInput(canvas.gameObject, "SkyIslandStory");
        }
        internal void Tick()
        {
            if (!Visible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Dispose(); return; }
            ZombieModeUIHelper.EnforceModalInputPause();
        }
        public void Dispose()
        {
            if (input != null) input.Release();
            input = null;
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null; body = null;
        }
        private static RectTransform MakeRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        private static TextMeshProUGUI Label(Transform parent, string value, int size, Vector2 position, Vector2 bounds)
        {
            TextMeshProUGUI text = MakeRect(parent, "Text", position, bounds).gameObject.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text); text.fontSize = size; text.text = value;
            text.color = BossRushUIColors.TextPrimary; text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
        private static void Button(Transform parent, string title, Vector2 position, Action action)
        {
            RectTransform rect = MakeRect(parent, "Choice", position, new Vector2(740, 44));
            Image image = rect.gameObject.AddComponent<Image>(); image.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(image, 8);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            Label(rect, title, 21, Vector2.zero, new Vector2(720, 40));
        }
    }

    public sealed class SkyIslandStoryInteractable : BossRushBuildingInteractableBase
    {
        private string label;
        private Action interact;
        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Story_" + name;
                LocalizationHelper.InjectLocalization(key, label ?? "群岛记事");
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIsland]"; } }
        protected override bool IsBuildingInteractable() { return interact != null; }
        internal void Bind(string title, Action action) { label = title; interact = action; }
        protected override void OnInteractCompleted() { if (interact != null) interact(); }
        internal static GameObject Create(Transform parent, Vector3 position, string name, string title, Action action)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = position;
            go.layer = LayerMask.NameToLayer("Interactable");
            BoxCollider trigger = go.AddComponent<BoxCollider>(); trigger.isTrigger = true;
            trigger.center = Vector3.up; trigger.size = new Vector3(3, 2, 3);
            go.AddComponent<SkyIslandStoryInteractable>().Bind(title, action);
            GameObject sign = new GameObject("Label", typeof(TextMeshPro));
            sign.transform.SetParent(go.transform, false); sign.transform.localPosition = Vector3.up * 2.5f;
            sign.transform.rotation = Quaternion.Euler(60, 0, 0);
            TextMeshPro text = sign.GetComponent<TextMeshPro>(); text.font = ZombieModeUIHelper.GetGameFont();
            text.text = title; text.fontSize = 3; text.alignment = TextAlignmentOptions.Center;
            text.color = BossRushUIColors.WarningText;
            text.rectTransform.sizeDelta = new Vector2(18, 5);
            return go;
        }
    }
}
