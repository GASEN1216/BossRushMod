using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>官方准星下方的机械刻度与命中收束。原生装填结束后保留短暂尾光，硬取消仍立即销毁。</summary>
    internal sealed class ReloadMetronomeHud : MonoBehaviour
    {
        private CanvasGroup rootGroup;
        private RectTransform window, marker, trailRect;
        private Image windowImage, trailImage, glint;
        private TextMeshProUGUI label;
        private AudioSource audioSource;
        private ReloadMetronomeResult result = ReloadMetronomeResult.Waiting;
        private float cachedTotal = -1f, flashAge = 1f, retirementAge;
        private bool retiring;
        private const float TrackWidth = 136f;
        private const float FinishTail = 0.24f;

        internal static ReloadMetronomeHud Create(Transform parent)
        {
            GameObject go = new GameObject("ReloadMetronomeHud", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                go.transform.SetParent(parent, false);
                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(154f, 42f);
                rect.anchoredPosition = new Vector2(0f, -66f);
                ReloadMetronomeHud hud = go.AddComponent<ReloadMetronomeHud>();
                hud.rootGroup = go.GetComponent<CanvasGroup>();
                hud.rootGroup.interactable = hud.rootGroup.blocksRaycasts = false;
                hud.rootGroup.alpha = 0f;
                Image background = go.AddComponent<Image>();
                background.color = BossRushUIColors.Surface;
                background.raycastTarget = false;
                BossRushUI.ApplyFramedPanelSkin(background, 6, BossRushUISkinPart.Card);
                Image track = Bar(go.transform, "Track", TrackWidth, 4f, BossRushUIColors.Stroke);
                track.rectTransform.anchoredPosition = new Vector2(0f, 6f);
                for (int i = 0; i <= 10; i++)
                {
                    Image tick = Bar(track.transform, "Tick", 1f, i % 5 == 0 ? 4f : 2f, BossRushUIColors.TextSecondary);
                    tick.rectTransform.anchoredPosition = new Vector2((i / 10f - 0.5f) * TrackWidth, 7f);
                }
                hud.windowImage = Bar(track.transform, "TimingWindow", 0f, 4f, BossRushUIColors.WarningText);
                hud.window = hud.windowImage.rectTransform;
                hud.trailImage = Art(track.transform, "NeedleTrail", ReloadMetronomeAssets.Trail, 22f, 12f);
                hud.trailRect = hud.trailImage.rectTransform;
                hud.trailImage.color = new Color(1f, 0.84f, 0.57f, 0.38f);
                hud.marker = Bar(track.transform, "Needle", 2f, 11f, BossRushUIColors.TextPrimary).rectTransform;
                Image cap = Bar(hud.marker, "NeedleCap", 4f, 2f, BossRushUIColors.TextPrimary);
                cap.rectTransform.anchoredPosition = new Vector2(0f, 6f);
                hud.glint = Art(track.transform, "BeatGlint", ReloadMetronomeAssets.Glint, 30f, 22f);
                hud.glint.color = Color.clear;
                GameObject text = new GameObject("Prompt", typeof(RectTransform));
                text.transform.SetParent(go.transform, false);
                hud.label = text.AddComponent<TextMeshProUGUI>();
                hud.label.rectTransform.sizeDelta = new Vector2(144f, 20f);
                hud.label.rectTransform.anchoredPosition = new Vector2(0f, -10f);
                BossRushUI.ApplyGameFont(hud.label);
                hud.label.fontSize = 12f;
                hud.label.alignment = TextAlignmentOptions.Center;
                hud.label.color = BossRushUIColors.TextPrimary;
                hud.label.raycastTarget = false;
                BossRushUIKit.ApplyWorldTextOutline(hud.label);
                hud.audioSource = go.AddComponent<AudioSource>();
                hud.audioSource.playOnAwake = false;
                hud.audioSource.spatialBlend = 0f;
                hud.audioSource.volume = 0f;
                hud.audioSource.clip = ReloadMetronomeAssets.Click;
                return hud;
            }
            catch (System.Exception e)
            {
                Destroy(go);
                ModBehaviour.DevLog("[ReloadMetronome] 节拍条创建失败: " + e.Message);
                return null;
            }
        }

        private static Image Art(Transform parent, string name, Sprite sprite, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.rectTransform.sizeDelta = new Vector2(width, height);
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.raycastTarget = false;
            return image;
        }

        private static Image Bar(Transform parent, string name, float width, float height, Color color)
        {
            Image image = Art(parent, name, null, width, height);
            image.enabled = true;
            image.color = color;
            BossRushUI.ApplyPanelSkin(image, 1, BossRushUISkinPart.Hairline);
            return image;
        }

        internal void SetResult(ReloadMetronomeResult next)
        {
            if (next == result || next == ReloadMetronomeResult.Inactive) return;
            result = next;
            windowImage.color = next == ReloadMetronomeResult.Perfect ? BossRushUIColors.WarningText : BossRushUIColors.Stroke;
            if (next != ReloadMetronomeResult.Perfect) return;
            flashAge = 0f;
            glint.rectTransform.anchoredPosition = marker.anchoredPosition;
            if (audioSource != null && audioSource.clip != null)
            {
                // 包内 PCM 由 Unity 播放，显式跟随官方 FMOD 的主音量与音效音量。
                var master = Duckov.AudioManager.GetBus("Master");
                var sfx = Duckov.AudioManager.GetBus("Master/SFX");
                audioSource.volume = master != null && sfx != null
                    ? 0.28f * Mathf.Clamp01(master.Volume) * Mathf.Clamp01(sfx.Volume) : 0f;
                if (audioSource.volume > 0f) audioSource.Play();
            }
        }

        internal void FinishReload() { retiring = true; retirementAge = 0f; }

        private void Update()
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (rootGroup != null && rootGroup.alpha != (visible ? 1f : 0f)) rootGroup.alpha = visible ? 1f : 0f;
            if (!visible)
            {
                if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
                if (!BossRushUI.IsGamePaused() && retiring) AdvanceRetirement();
                return;
            }
            float current, total;
            if (!retiring && ReloadMetronomeRuntime.TryGetProgress(out current, out total))
            {
                if (cachedTotal != total)
                {
                    cachedTotal = total;
                    float start, end;
                    ReloadMetronomeRules.GetWindow(total, out start, out end);
                    window.sizeDelta = new Vector2((end - start) / total * TrackWidth, 4f);
                    window.anchoredPosition = new Vector2(((start + end) * 0.5f / total - 0.5f) * TrackWidth, 0f);
                }
                float x = (Mathf.Clamp01(current / total) - 0.5f) * TrackWidth;
                marker.anchoredPosition = new Vector2(x, 0f);
                float width = Mathf.Min(22f, x + TrackWidth * 0.5f);
                trailRect.sizeDelta = new Vector2(width, 12f);
                trailRect.anchoredPosition = new Vector2(x - width * 0.5f, 0f);
            }
            else if (!retiring) { rootGroup.alpha = 0f; return; }
            if (retiring) AdvanceRetirement();
            if (flashAge < 0.20f)
            {
                flashAge = Mathf.Min(0.20f, flashAge + Time.deltaTime);
                float fade = 1f - BossRushUI.EaseOut(flashAge / 0.20f);
                glint.color = new Color(1f, 0.87f, 0.64f, fade * 0.8f);
                glint.rectTransform.localScale = Vector3.one * (1f + (1f - fade) * 0.5f);
            }
            string prompt = result == ReloadMetronomeResult.Perfect ? L10n.T("合拍装填", "ON BEAT")
                : result == ReloadMetronomeResult.Early ? L10n.T("早了一拍 · 原速装填", "Early - normal reload")
                : result == ReloadMetronomeResult.Late ? L10n.T("晚了一拍 · 原速装填", "Late - normal reload")
                : L10n.T("踩点再按换弹", "Tap reload on beat");
            if (label.text != prompt) label.text = prompt;
        }

        private void AdvanceRetirement()
        {
            retirementAge += Time.deltaTime;
            float t = Mathf.Clamp01(retirementAge / FinishTail);
            rootGroup.alpha *= 1f - BossRushUI.SmoothStep(t);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
