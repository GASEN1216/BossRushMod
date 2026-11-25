// ============================================================================
// ArenaInkTitleCard.cs - 出征令的水墨标题幕（压轴登场 / 噩梦每只 Boss 登场）
// ============================================================================
// 一次性演出，不是常驻 HUD：上下两道墨色横幅压进画面，Boss 名以笔势从左到右扫出，
// 压轴时右侧落一枚「压轴」朱印并伴一声战鼓；噩梦档普通波是缩短版（无印、无鼓，一笔扫出）。
// 每帧仍跟随官方 HUD 显隐（背包、地图打开时不压在上面），暂停时由 BossRushInkMotion 冻结。
// 同一时刻只留一张；切图时画布随场景销毁。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ArenaInkTitleCard : MonoBehaviour
    {
        private const string RootName = "BossRush_ArenaInkTitleCard";

        private static ArenaInkTitleCard instance;

        private Canvas canvas;

        /// <param name="finale">压轴版（长、落印、战鼓）；否则噩梦缩短版。</param>
        internal static void Play(string bossName, bool finale)
        {
            if (string.IsNullOrEmpty(bossName)) return;
            Stop();
            try
            {
                Canvas root = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.HudOverlay, false);
                instance = root.gameObject.AddComponent<ArenaInkTitleCard>();
                instance.canvas = root;
                instance.Build(bossName, finale);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令标题幕创建失败（不影响战斗）: " + e.Message);
                Stop();
            }
        }

        internal static void Stop()
        {
            ArenaInkTitleCard card = instance;
            instance = null;
            if (card != null && card.gameObject != null) Object.Destroy(card.gameObject);
        }

        private void Build(string bossName, bool finale)
        {
            Transform root = canvas.transform;
            BossRushInkMotion motion = BossRushInkMotion.For(gameObject);
            float hold = finale ? 1.2f : 0.55f;
            float bandHeight = finale ? 128f : 84f;

            CanvasGroup group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // 上下两道墨幅：从屏幕外压进来，像画轴两端合拢。
            RectTransform top = CreateBand(root, "BandTop", 1f, bandHeight);
            RectTransform bottom = CreateBand(root, "BandBottom", 0f, bandHeight);
            motion.Add(0f, 0.28f, true, t =>
            {
                if (top != null) top.anchoredPosition = new Vector2(0f, Mathf.Lerp(bandHeight, 0f, t));
                if (bottom != null) bottom.anchoredPosition = new Vector2(0f, Mathf.Lerp(-bandHeight, 0f, t));
            });

            // 中央：墨笔底衬 + Boss 名（RectMask2D 从左到右扫开）+ 金色一笔。
            float fontSize = finale ? 64f : 46f;
            GameObject center = ZombieModeUIHelper.CreateRect("Center", root, new Vector2(0.5f, 0.56f), new Vector2(1400f, fontSize * 2.6f));
            RectTransform centerRect = center.GetComponent<RectTransform>();

            TextMeshProUGUI probe = ZombieModeUIHelper.CreateText("Probe", center.transform, bossName, fontSize,
                Vector2.zero, new Vector2(1400f, fontSize * 1.45f + 4f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            BossRushUI.ApplyGameFont(probe);
            probe.fontStyle = FontStyles.Bold;
            probe.characterSpacing = 18f;
            float textWidth = Mathf.Clamp(probe.GetPreferredValues(bossName).x + 24f, fontSize * 2f, 1300f);
            Object.Destroy(probe.gameObject);

            RectTransform underlay = BossRushInkSkin.CreateBrushStroke(center.transform, "Underlay",
                new Vector2(textWidth + fontSize * 2.4f, fontSize * 1.7f), new Vector2(-fontSize * 0.1f, fontSize * 0.05f),
                new Color(BossRushInkColors.InkWash.r, BossRushInkColors.InkWash.g, BossRushInkColors.InkWash.b, 0.86f));
            BossRushInkSkin.PlayBrushWrite(underlay, 0.12f, finale ? 0.42f : 0.3f, motion);

            GameObject nameMask = ZombieModeUIHelper.CreateRect("NameMask", center.transform, new Vector2(0.5f, 0.5f),
                new Vector2(textWidth, fontSize * 1.45f + 4f));
            RectTransform nameMaskRect = nameMask.GetComponent<RectTransform>();
            nameMask.AddComponent<RectMask2D>();
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText("Name", nameMask.transform, bossName, fontSize,
                Vector2.zero, new Vector2(textWidth, fontSize * 1.45f + 4f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            BossRushUI.ApplyGameFont(title);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 18f;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Overflow;
            BossRushUIKit.ApplyWorldTextOutline(title);
            // 名字随遮罩扫出：文字固定在中心，遮罩从左缘往右长。
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(textWidth * 0.5f, 0f);
            BossRushInkSkin.PlayBrushWrite(nameMaskRect, 0.2f, finale ? 0.5f : 0.34f, motion);

            RectTransform gold = BossRushInkSkin.CreateBrushStroke(center.transform, "GoldStroke",
                new Vector2(textWidth * 0.9f, 10f), new Vector2(0f, -fontSize * 0.78f), BossRushInkColors.Gold);
            BossRushInkSkin.PlayBrushWrite(gold, 0.46f, 0.36f, motion);

            if (finale)
            {
                TextMeshProUGUI sub = ZombieModeUIHelper.CreateText("Sub", center.transform, "HEADLINER", 18f,
                    new Vector2(0f, -fontSize * 1.18f), new Vector2(600f, 30f), TextAlignmentOptions.Center, BossRushInkColors.GoldSoft);
                BossRushUI.ApplyGameFont(sub);
                sub.characterSpacing = 40f;
                CanvasGroup subGroup = sub.gameObject.AddComponent<CanvasGroup>();
                subGroup.alpha = 0f;
                motion.Add(0.6f, 0.4f, false, t => { if (subGroup != null) subGroup.alpha = t; });

                RectTransform seal = BossRushInkSkin.CreateSeal(center.transform, "压轴", fontSize * 1.15f);
                seal.anchoredPosition = new Vector2(textWidth * 0.5f + fontSize * 0.9f, fontSize * 0.1f);
                seal.localRotation = Quaternion.Euler(0f, 0f, -6f);
                BossRushInkSkin.StampSeal(seal, centerRect, 0.72f);
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);
            }
            else
            {
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxBrushSweep);
            }

            float fadeAt = 0.7f + hold;
            motion.Add(fadeAt, 0.4f, false, t => { if (group != null) group.alpha = 1f - t; }, () =>
            {
                // 只收自己：这期间若已换上新的一张，Stop() 会误杀它。
                if (this == null) return;
                if (instance == this) instance = null;
                Object.Destroy(gameObject);
            });
        }

        private static RectTransform CreateBand(Transform root, string name, float edge, float height)
        {
            GameObject band = ZombieModeUIHelper.CreateRect(name, root, new Vector2(0f, edge), new Vector2(1f, edge),
                Vector2.zero, new Vector2(0f, height), new Vector2(0.5f, edge));
            Image image = band.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetSurfaceSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.6f;
            image.color = new Color(BossRushInkColors.InkWash.r, BossRushInkColors.InkWash.g, BossRushInkColors.InkWash.b, 0.82f);
            image.raycastTarget = false;
            return band.GetComponent<RectTransform>();
        }

        private void Update()
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
