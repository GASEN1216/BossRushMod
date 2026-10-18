using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>同一枚炸弹复用一个头顶标记；整数变动才改字，逐帧仅投影坐标与缩放。</summary>
    internal sealed class PassTheBombFx : MonoBehaviour
    {
        private static readonly string[] Seconds = { "0", "1", "2", "3", "4", "5" };
        private static Sprite markerSprite;
        private Canvas canvas;
        private RectTransform canvasRect, markerRoot;
        private Camera worldCamera;
        private Image marker, fuseRing;
        private TextMeshProUGUI countdown;
        private Vector3 headPosition;
        private float remaining, transferAge = 1f;
        private int shownSeconds = -1;
        private bool onPlayer, wasOnPlayer;

        internal static PassTheBombFx Create(Transform parent)
        {
            GameObject go = new GameObject("PassTheBomb_Fx");
            try
            {
                go.transform.SetParent(parent, false);
                PassTheBombFx view = go.AddComponent<PassTheBombFx>();
                view.Build();
                return view;
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 头顶倒计时创建失败: " + e.Message);
                return null;
            }
        }

        private void Build()
        {
            canvas = BossRushUI.CreateCanvasRoot("PassTheBomb_HeadMarker", BossRushUILayers.WorldOverlay, false);
            canvas.transform.SetParent(transform, false);
            canvasRect = (RectTransform)canvas.transform;
            worldCamera = Camera.main;
            GameObject root = new GameObject("BombMarker", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            markerRoot = (RectTransform)root.transform;
            markerRoot.anchorMin = markerRoot.anchorMax = markerRoot.pivot = new Vector2(0.5f, 0.5f);
            markerRoot.sizeDelta = new Vector2(62f, 82f);

            if (markerSprite == null) markerSprite = ProductionIconCache.Get("Assets/Icons/pass_the_bomb_marker.png");
            marker = CreateImage(root.transform, "Bomb", markerSprite, new Vector2(50f, 50f), new Vector2(0f, 10f));
            // 图标损坏时仍留醒目的圆形警告和数字，不让资源问题变成隐形伤害。
            if (marker.sprite == null)
            {
                marker.sprite = BossRushFxKit.GetSoftCircleSprite();
                marker.color = BossRushUIColors.WarningText;
            }
            fuseRing = CreateImage(root.transform, "FuseRing", BossRushProceduralSprites.GetRingSprite(),
                new Vector2(59f, 59f), new Vector2(0f, 8f));
            fuseRing.type = Image.Type.Filled;
            fuseRing.fillMethod = Image.FillMethod.Radial360;
            fuseRing.fillOrigin = 2;
            fuseRing.fillClockwise = false;
            fuseRing.color = BossRushUIColors.WarningText;

            GameObject label = new GameObject("Seconds", typeof(RectTransform));
            label.transform.SetParent(root.transform, false);
            countdown = label.AddComponent<TextMeshProUGUI>();
            countdown.rectTransform.sizeDelta = new Vector2(60f, 35f);
            countdown.rectTransform.anchoredPosition = new Vector2(0f, -27f);
            BossRushUI.ApplyGameFont(countdown);
            countdown.fontSize = 28f;
            countdown.fontStyle = FontStyles.Bold;
            countdown.alignment = TextAlignmentOptions.Center;
            countdown.color = BossRushUIColors.TextPrimary;
            countdown.raycastTarget = false;
            BossRushUIKit.ApplyWorldTextOutline(countdown);
            canvas.enabled = false;
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            return image;
        }

        internal void SetState(Vector3 position, float seconds, bool playerIsCarrier)
        {
            headPosition = position;
            remaining = Mathf.Max(0f, seconds);
            onPlayer = playerIsCarrier;
        }

        internal void ShowTransfer() { transferAge = 0f; }

        private void LateUpdate()
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
            if (!visible) return;
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) { canvas.enabled = false; return; }
            Vector3 point = worldCamera.WorldToViewportPoint(headPosition);
            if (point.z <= 0f || point.x < -0.05f || point.x > 1.05f || point.y < -0.05f || point.y > 1.05f)
            {
                canvas.enabled = false;
                return;
            }
            Rect rect = canvasRect.rect;
            Vector2 position = new Vector2((point.x - 0.5f) * rect.width, (point.y - 0.5f) * rect.height);
            if (!markerRoot.anchoredPosition.Equals(position)) markerRoot.anchoredPosition = position;
            int second = Mathf.Clamp(Mathf.CeilToInt(remaining), 0, Seconds.Length - 1);
            if (second != shownSeconds)
            {
                shownSeconds = second;
                countdown.text = Seconds[second];
                countdown.color = second <= 1 ? BossRushUIColors.Danger : BossRushUIColors.TextPrimary;
            }
            if (wasOnPlayer != onPlayer)
            {
                wasOnPlayer = onPlayer;
                fuseRing.color = onPlayer ? BossRushUIColors.Danger : BossRushUIColors.WarningText;
            }
            float fill = Mathf.Clamp01(remaining / PassTheBombConfig.FuseSeconds);
            fuseRing.fillAmount = fill;
            transferAge = Mathf.Min(1f, transferAge + Time.deltaTime);
            float arrival = 1f - BossRushUI.EaseOut(Mathf.Clamp01(transferAge / 0.18f));
            float pulse = remaining <= 1f ? Mathf.Sin(Time.time * 22f) * 0.055f : 0f;
            Vector3 scale = Vector3.one * (1f + 0.18f * arrival + pulse);
            if (!markerRoot.localScale.Equals(scale)) markerRoot.localScale = scale;
        }

        internal static void PlayExplosion(Vector3 position, float radius)
        {
            // 复用已经验证过的爆点、尘圈和短音效，不预热粒子池、不创建新的爆炸伤害管线。
            EmptyMagazineMineFx.PlayExplosion(position, radius);
        }

        internal static void ResetStaticCaches() { markerSprite = null; }
    }
}
