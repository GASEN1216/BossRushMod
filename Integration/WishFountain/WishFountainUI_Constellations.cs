// ============================================================================
// WishFountainUI_Constellations.cs - 许愿面板的星宿穹顶、流星与「星灯」开关
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 3 项，水墨东方）：
//   - 星宿穹顶：标题下方一道弧形的十二宿，中间高两端低，像星空罩在星泉上。每宿是一枚
//     程序化的连星小图（柔光星点 + 发丝细线）配一行类别名。待选时宣纸淡色、星点慢慢明灭；
//     悬停微亮；点亮时星与线转鎏金、背后浮起一团金晕、落一圈墨环，响一声磬。再点一次熄灭。
//     穹顶下方是「星泉」，只有光、不写字（2026-11-25 owner 去掉了说明行）。窄屏（一宿不足 58 像素宽）时折成两行六宿。
//   - 流星：许愿送出的那一拍，一颗带尾的星从点亮的那一宿划进星泉，落一圈金环；没点亮就只在星泉落环。
//   - 「星灯」开关：面板右上角，记在 BossRushLocalPrefs（只在本机）。熄了以后不拉取、不预热、不显示星灯。
//   亮度变化全部按帧缓动（不弹跳），驱动挂在穹顶自己的物体上，面板关上即停；官方暂停菜单开着时冻结。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    public partial class WishFountainView
    {
        private const float CONSTELLATION_CHIP_MIN_WIDTH = 58f;
        private const float CONSTELLATION_ROW_HEIGHT = 70f;
        private const float CONSTELLATION_DOME_SAG = 14f;
        private const float CONSTELLATION_SPRING_HEIGHT = 30f;
        private const float CONSTELLATION_GLYPH_WIDTH = 46f;
        private const float CONSTELLATION_GLYPH_HEIGHT = 30f;
        private const float CONSTELLATION_PANEL_SPACING = 16f;   // 与面板 VerticalLayoutGroup.spacing 一致
        private const string StarRiverPrefKey = "wish.starRiver";

        private sealed class ConstellationChip
        {
            internal WishConstellation Data;
            internal RectTransform Rect;
            internal Button Button;
            internal RectTransform Glyph;
            internal Image Halo;
            internal readonly List<Image> Stars = new List<Image>();
            internal readonly List<float> StarSizes = new List<float>();
            internal readonly List<float> StarPhases = new List<float>();
            internal readonly List<Image> Lines = new List<Image>();
            internal TextMeshProUGUI Label;
            internal float Glow;     // 当前亮度：0 待选，约 0.45 悬停，1 点亮；每帧向目标缓动
            internal bool Hover;
        }

        private readonly List<ConstellationChip> constellationChips = new List<ConstellationChip>();
        private RectTransform constellationBand;
        private LayoutElement constellationBandLayout;
        // 星泉：流星落点与泉光所在。原先这里有一行说明字（点亮一宿……/望向某宿），owner 2026-11-25 要求去掉，只留光。
        private Vector2 springPoint;
        private Image springGlow;
        private GameObject meteorHost;
        private string chosenConstellationId;
        private bool constellationsInteractable = true;
        private float constellationBandHeight;
        private float springFlash;
        private TextMeshProUGUI starRiverLabel;
        private Image starRiverDot;

        // ====================================================================
        // 构建
        // ====================================================================

        private void BuildConstellationBand(RectTransform panelRect, TMP_FontAsset font)
        {
            GameObject band = CreateUIObject("ConstellationBand", panelRect, typeof(LayoutElement));
            constellationBand = band.GetComponent<RectTransform>();
            constellationBandLayout = band.GetComponent<LayoutElement>();

            springGlow = CreateGlowImage("SpringGlow", constellationBand, new Vector2(300f, 54f), BossRushInkColors.Gold, 0f);

            constellationChips.Clear();
            for (int i = 0; i < WishConstellations.All.Length; i++)
            {
                constellationChips.Add(BuildConstellationChip(WishConstellations.All[i], font, i));
            }

            band.AddComponent<WishConstellationTicker>().Tick = TickConstellations;
            LayoutConstellationBand(BASE_PANEL_WIDTH - 64f);
            RefreshConstellationTexts();
        }

        private ConstellationChip BuildConstellationChip(WishConstellation data, TMP_FontAsset font, int index)
        {
            ConstellationChip chip = new ConstellationChip { Data = data };
            GameObject chipObject = CreateUIObject("Star_" + data.Id, constellationBand, typeof(Image), typeof(Button));
            chip.Rect = chipObject.GetComponent<RectTransform>();
            Image hit = chipObject.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);   // 透明命中区：整格可点，不画底
            hit.raycastTarget = true;
            chip.Button = chipObject.GetComponent<Button>();
            chip.Button.transition = Selectable.Transition.None;
            chip.Button.targetGraphic = hit;
            Navigation navigation = chip.Button.navigation;
            navigation.mode = Navigation.Mode.None;
            chip.Button.navigation = navigation;
            chip.Button.onClick.AddListener(() => OnConstellationClicked(chip));
            WishConstellationHover hover = chipObject.AddComponent<WishConstellationHover>();
            hover.Changed = inside => chip.Hover = inside;

            chip.Halo = CreateGlowImage("Halo", chip.Rect, new Vector2(84f, 62f), BossRushInkColors.Gold, 0f);
            chip.Halo.rectTransform.anchoredPosition = new Vector2(0f, 10f);

            GameObject glyphObject = ZombieModeUIHelper.CreateRect("Glyph", chip.Rect, new Vector2(0.5f, 0.5f),
                new Vector2(CONSTELLATION_GLYPH_WIDTH, CONSTELLATION_GLYPH_HEIGHT));
            chip.Glyph = glyphObject.GetComponent<RectTransform>();
            chip.Glyph.anchoredPosition = new Vector2(0f, 10f);

            Vector2[] points = new Vector2[data.Stars.Length];
            for (int i = 0; i < data.Stars.Length; i++)
            {
                points[i] = new Vector2((data.Stars[i].x - 0.5f) * CONSTELLATION_GLYPH_WIDTH,
                    (data.Stars[i].y - 0.5f) * CONSTELLATION_GLYPH_HEIGHT);
            }

            for (int e = 0; e + 1 < data.Edges.Length; e += 2)
            {
                Vector2 a = points[data.Edges[e]];
                Vector2 b = points[data.Edges[e + 1]];
                Vector2 delta = b - a;
                float length = Mathf.Max(0f, delta.magnitude - 7f);   // 线头离星点留一点空，不糊在一起
                if (length < 2f) continue;
                GameObject lineObject = ZombieModeUIHelper.CreateRect("Line", chip.Glyph, new Vector2(0.5f, 0.5f), new Vector2(length, 1.4f));
                RectTransform lineRect = lineObject.GetComponent<RectTransform>();
                lineRect.anchoredPosition = (a + b) * 0.5f;
                lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                Image line = lineObject.AddComponent<Image>();
                line.sprite = BossRushUI.GetSolidSprite();
                line.raycastTarget = false;
                chip.Lines.Add(line);
            }

            for (int i = 0; i < points.Length; i++)
            {
                float size = i == 0 ? 15f : 10f;   // 第一个是主星
                Image star = CreateGlowImage("Star", chip.Glyph, new Vector2(size, size), BossRushInkColors.Paper, 0f);
                star.rectTransform.anchoredPosition = points[i];
                chip.Stars.Add(star);
                chip.StarSizes.Add(size);
                chip.StarPhases.Add(index * 1.7f + i * 2.3f);
            }

            chip.Label = CreateText("Label", chip.Rect, font, NOTE_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Center);
            chip.Label.rectTransform.sizeDelta = new Vector2(80f, NOTE_LINE_HEIGHT);
            chip.Label.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            chip.Label.enableWordWrapping = false;
            chip.Label.overflowMode = TextOverflowModes.Overflow;
            ApplyChipVisual(chip, 0f);
            return chip;
        }

        private void BuildStarRiverToggle(RectTransform panelRect, TMP_FontAsset font)
        {
            GameObject toggle = CreateUIObject("StarRiverToggle", panelRect, typeof(Image), typeof(Button), typeof(LayoutElement));
            toggle.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rect = toggle.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(150f, NOTE_LINE_HEIGHT + 6f);
            rect.anchoredPosition = new Vector2(-22f, -18f);

            Image hit = toggle.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            Button button = toggle.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(ToggleStarRiver);

            starRiverLabel = CreateText("Label", rect, font, NOTE_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Right);
            starRiverLabel.rectTransform.anchorMin = Vector2.zero;
            starRiverLabel.rectTransform.anchorMax = Vector2.one;
            starRiverLabel.rectTransform.offsetMin = Vector2.zero;
            starRiverLabel.rectTransform.offsetMax = Vector2.zero;
            starRiverLabel.enableWordWrapping = false;

            starRiverDot = CreateGlowImage("Lamp", rect, new Vector2(22f, 22f), BossRushInkColors.Ember, 1f);
            starRiverDot.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            starRiverDot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            starRiverDot.rectTransform.anchoredPosition = new Vector2(14f, 0f);
            RefreshStarRiverVisual();
        }

        private static Image CreateGlowImage(string name, RectTransform parent, Vector2 size, Color color, float alpha)
        {
            GameObject glowObject = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            Image image = glowObject.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetGlowSprite();
            image.raycastTarget = false;
            image.color = new Color(color.r, color.g, color.b, alpha);
            return image;
        }

        // ====================================================================
        // 布局（AdjustPanelForResolution 调用）
        // ====================================================================

        /// <summary>按面板内容宽度排十二宿；返回穹顶连同面板间距要占的高度。</summary>
        private float LayoutConstellationBand(float contentWidth)
        {
            if (constellationBand == null || constellationChips.Count == 0) return 0f;
            int count = constellationChips.Count;
            int rows = contentWidth / count < CONSTELLATION_CHIP_MIN_WIDTH ? 2 : 1;
            int perRow = Mathf.CeilToInt(count / (float)rows);
            float cell = Mathf.Max(1f, contentWidth) / perRow;
            float height = rows * CONSTELLATION_ROW_HEIGHT + CONSTELLATION_DOME_SAG + CONSTELLATION_SPRING_HEIGHT;

            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                float d = perRow > 1 ? col / (perRow - 1f) * 2f - 1f : 0f;
                float x = (col - (perRow - 1) * 0.5f) * cell;
                // 穹顶：中间高、两端低
                float y = height * 0.5f - row * CONSTELLATION_ROW_HEIGHT - CONSTELLATION_ROW_HEIGHT * 0.5f
                    - CONSTELLATION_DOME_SAG * d * d;
                ConstellationChip chip = constellationChips[i];
                chip.Rect.anchorMin = new Vector2(0.5f, 0.5f);
                chip.Rect.anchorMax = new Vector2(0.5f, 0.5f);
                chip.Rect.sizeDelta = new Vector2(cell, CONSTELLATION_ROW_HEIGHT);
                chip.Rect.anchoredPosition = new Vector2(x, y);
            }

            Vector2 spring = new Vector2(0f, -height * 0.5f + CONSTELLATION_SPRING_HEIGHT * 0.5f);
            springPoint = spring;
            if (springGlow != null) springGlow.rectTransform.anchoredPosition = spring;

            constellationBandHeight = height;
            if (constellationBandLayout != null)
            {
                constellationBandLayout.minHeight = height;
                constellationBandLayout.preferredHeight = height;
            }
            return height + CONSTELLATION_PANEL_SPACING;
        }

        // ====================================================================
        // 交互
        // ====================================================================

        private void OnConstellationClicked(ConstellationChip chip)
        {
            if (chip == null || !constellationsInteractable || sending || successDisplayed) return;
            bool turningOff = string.Equals(chosenConstellationId, chip.Data.Id, StringComparison.OrdinalIgnoreCase);
            chosenConstellationId = turningOff ? null : chip.Data.Id;
            if (turningOff)
            {
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
            }
            else
            {
                BossRushInkSkin.SpawnRing(constellationBand, ChipGlyphPosition(chip), 40f, BossRushInkColors.Gold, 0.75f, 2.4f, 0.65f);
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxChimeStone);
                springFlash = 1f;
            }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == chip.Button.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);   // 不让芯片吃掉回车
            }
        }

        private void SetConstellationsInteractable(bool interactable)
        {
            constellationsInteractable = interactable;
            for (int i = 0; i < constellationChips.Count; i++)
            {
                if (constellationChips[i].Button != null) constellationChips[i].Button.interactable = interactable;
            }
        }

        /// <summary>重新打开面板：熄掉上次点亮的一宿，收掉没飞完的流星。</summary>
        private void ResetConstellations()
        {
            chosenConstellationId = null;
            springFlash = 0f;
            if (meteorHost != null)
            {
                Destroy(meteorHost);
                meteorHost = null;
            }
            for (int i = 0; i < constellationChips.Count; i++)
            {
                constellationChips[i].Glow = 0f;
                constellationChips[i].Hover = false;
                ApplyChipVisual(constellationChips[i], Time.unscaledTime);
            }
        }

        private void RefreshConstellationTexts()
        {
            for (int i = 0; i < constellationChips.Count; i++)
            {
                ConstellationChip chip = constellationChips[i];
                if (chip.Label != null) chip.Label.text = chip.Data.Label;
            }
            RefreshStarRiverVisual();
        }

        private Vector2 ChipGlyphPosition(ConstellationChip chip)
        {
            return chip.Rect.anchoredPosition + chip.Glyph.anchoredPosition;
        }

        // ====================================================================
        // 每帧：亮度缓动、星点明灭、泉光呼吸
        // ====================================================================

        private void TickConstellations(float deltaTime)
        {
            float time = Time.unscaledTime;
            float follow = 1f - Mathf.Exp(-deltaTime * 9f);
            for (int i = 0; i < constellationChips.Count; i++)
            {
                ConstellationChip chip = constellationChips[i];
                bool lit = string.Equals(chosenConstellationId, chip.Data.Id, StringComparison.OrdinalIgnoreCase);
                float target = lit ? 1f : (chip.Hover && constellationsInteractable ? 0.45f : 0f);
                chip.Glow += (target - chip.Glow) * follow;
                ApplyChipVisual(chip, time);
            }

            springFlash = Mathf.Max(0f, springFlash - deltaTime * 1.6f);
            if (springGlow != null)
            {
                bool any = WishConstellations.Find(chosenConstellationId) != null;
                float breathe = any ? 0.16f + 0.06f * Mathf.Sin(time * 1.5f) : 0f;
                float alpha = Mathf.Max(breathe, springFlash * 0.55f);
                Color current = springGlow.color;
                current.a += (alpha - current.a) * follow;
                springGlow.color = current;
            }
        }

        private void ApplyChipVisual(ConstellationChip chip, float time)
        {
            float g = Mathf.Clamp01(chip.Glow);
            Color tone = Color.Lerp(BossRushInkColors.PaperDim, BossRushInkColors.Gold, g);
            for (int i = 0; i < chip.Stars.Count; i++)
            {
                Image star = chip.Stars[i];
                if (star == null) continue;
                float twinkle = 0.78f + 0.22f * Mathf.Sin(time * 1.3f + chip.StarPhases[i]);
                float alpha = Mathf.Lerp(0.55f, 1f, g) * Mathf.Lerp(twinkle, 1f, g);
                star.color = new Color(tone.r, tone.g, tone.b, alpha);
                float size = chip.StarSizes[i] * (1f + 0.3f * g);
                star.rectTransform.sizeDelta = new Vector2(size, size);
            }
            for (int i = 0; i < chip.Lines.Count; i++)
            {
                Image line = chip.Lines[i];
                if (line != null) line.color = new Color(tone.r, tone.g, tone.b, Mathf.Lerp(0.22f, 0.8f, g));
            }
            if (chip.Halo != null) chip.Halo.color = new Color(tone.r, tone.g, tone.b, 0.3f * g);
            if (chip.Label != null)
            {
                Color label = Color.Lerp(BossRushInkColors.PaperDim, BossRushInkColors.Gold, g);
                label.a = Mathf.Lerp(0.85f, 1f, g);
                chip.Label.color = label;
            }
        }

        // ====================================================================
        // 流星（BeginSuccessBeat 调用）
        // ====================================================================

        private void PlayConstellationMeteor()
        {
            if (constellationBand == null) return;
            Vector2 spring = springPoint;
            ConstellationChip chosen = null;
            for (int i = 0; i < constellationChips.Count; i++)
            {
                if (string.Equals(chosenConstellationId, constellationChips[i].Data.Id, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = constellationChips[i];
                    break;
                }
            }
            if (chosen == null)
            {
                BossRushInkSkin.SpawnRing(constellationBand, spring, 56f, BossRushInkColors.Gold, 0.55f, 2.8f, 0.7f);
                springFlash = 1f;
                return;
            }

            if (meteorHost != null) Destroy(meteorHost);
            meteorHost = ZombieModeUIHelper.CreateRect("Meteor", constellationBand, new Vector2(0.5f, 0.5f), Vector2.zero);
            RectTransform host = meteorHost.GetComponent<RectTransform>();
            const int TrailCount = 7;
            Image[] parts = new Image[TrailCount];
            for (int k = TrailCount - 1; k >= 0; k--)
            {
                float size = 24f * (1f - k * 0.1f);
                Color tint = k == 0 ? new Color(1f, 0.9f, 0.62f, 1f) : BossRushInkColors.Gold;
                parts[k] = CreateGlowImage("Trail" + k, host, new Vector2(size, size), tint, 0f);
            }

            Vector2 from = ChipGlyphPosition(chosen);
            Vector2 control = new Vector2((from.x + spring.x) * 0.5f, Mathf.Max(from.y, spring.y) + 48f);
            GameObject owner = meteorHost;
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxSoulRise);
            BossRushInkMotion.For(owner).Add(0f, 0.5f, false, t =>
            {
                for (int k = 0; k < TrailCount; k++)
                {
                    Image part = parts[k];
                    if (part == null) continue;
                    float tk = Mathf.Clamp01(t - k * 0.045f);
                    float u = 1f - tk;
                    part.rectTransform.anchoredPosition = u * u * from + 2f * u * tk * control + tk * tk * spring;
                    float fade = t < 0.9f ? 1f : (1f - t) / 0.1f;
                    float alpha = (t - k * 0.045f) <= 0f ? 0f : (1f - k / (float)TrailCount) * (k == 0 ? 1f : 0.6f) * fade;
                    Color c = part.color;
                    c.a = alpha;
                    part.color = c;
                }
            }, () =>
            {
                if (constellationBand != null)
                {
                    BossRushInkSkin.SpawnRing(constellationBand, spring, 48f, BossRushInkColors.Gold, 0.8f, 3f, 0.75f);
                    BossRushInkSkin.SpawnRing(constellationBand, spring, 30f, BossRushInkColors.Ember, 0.5f, 2f, 0.5f);
                }
                springFlash = 1f;
                if (owner != null) Destroy(owner);
                if (meteorHost == owner) meteorHost = null;
            });
        }

        // ====================================================================
        // 「星灯」开关
        // ====================================================================

        private static bool IsStarRiverEnabled()
        {
            return BossRushLocalPrefs.GetBool(StarRiverPrefKey, true);
        }

        private void ToggleStarRiver()
        {
            bool enable = !IsStarRiverEnabled();
            BossRushLocalPrefs.SetBool(StarRiverPrefKey, enable);
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn);
            RefreshStarRiverVisual();
            if (enable)
            {
                RefreshDanmakuDisplay();
            }
            else
            {
                CancelDanmakuFetch();
                HideDanmaku();
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void RefreshStarRiverVisual()
        {
            bool enable = IsStarRiverEnabled();
            if (starRiverLabel != null)
            {
                starRiverLabel.text = enable ? L10n.T("星灯 亮", "Lanterns on") : L10n.T("星灯 熄", "Lanterns off");
                starRiverLabel.color = enable ? BossRushInkColors.Gold : BossRushInkColors.PaperDim;
            }
            if (starRiverDot != null)
            {
                starRiverDot.color = enable
                    ? BossRushInkColors.Ember
                    : new Color(BossRushInkColors.PaperDim.r, BossRushInkColors.PaperDim.g, BossRushInkColors.PaperDim.b, 0.35f);
            }
        }
    }

    /// <summary>星宿穹顶的每帧驱动：只在面板开着（物体激活）时跑，官方暂停菜单开着时冻结。</summary>
    internal sealed class WishConstellationTicker : MonoBehaviour
    {
        internal Action<float> Tick;

        private void Update()
        {
            if (Tick == null || BossRushUI.IsGamePaused()) return;
            try { Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f)); }
            catch (Exception e) { ModBehaviour.DevLog("[WishFountain] 星宿穹顶刷新失败: " + e.Message); }
        }
    }

    /// <summary>星宿芯片的悬停：只报进出，亮度缓动在穹顶驱动里做。</summary>
    internal sealed class WishConstellationHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action<bool> Changed;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Changed != null) Changed(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Changed != null) Changed(false);
        }

        private void OnDisable()
        {
            if (Changed != null) Changed(false);
        }
    }
}
