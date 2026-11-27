// ============================================================================
// ArenaExpeditionHud.cs - 出征令局内角标：右侧一枚小朱印写着战帖，下面一行小字写征程
// ============================================================================
// 常驻 HUD（AGENTS §4.17）：每帧经官方 HUD 显隐与暂停两道门（PersistentHudVisibilityGuard 登记）。
// 只在竞技场 Legacy 波次进行中显示；开波时落印一次，通关 / 离场 / 切图由 ArenaExpeditionState.End 收掉。
// 不接收点击（画布 raycaster 关闭）。
//
// 2026-11-25 战规机制化：角标下面再加两行——天象名、诅咒名（噩梦禁药时多一行读秒），变天时上方一道墨色横扫加一声低钟；
// 另有一层压在官方 HUD 之下的屏幕色调（ScreenAmbience）：天色 / 天象的画面（雨雾的冷灰、黄昏的暖光、夜色、血月暗角、
// 永夜的黑边、雷暴的闪光）都画在这里，同一个 Update 里跟着两道门一起收起。数值效果不在这里，见 WavesArena/Trials。
// 2026-11-27 加码：战帖印右上角一枚小级数印（Ⅰ–Ⅷ），开波时随档印之后落下。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>屏幕色调层的几种氛围（天色 4 种 + 天象 5 种）。</summary>
    internal enum ArenaExpeditionAmbience
    {
        None = 0,
        Rain,
        Snow,
        Dusk,
        Night,
        Blizzard,
        Thunder,
        BloodMoon,
        EternalNight,
        Downpour,
    }

    internal sealed class ArenaExpeditionHud : MonoBehaviour
    {
        private const string RootName = "BossRush_ArenaExpeditionHud";
        private const string AmbienceName = "BossRush_ArenaExpeditionAmbience";
        private const float SealSize = 58f;

        private static ArenaExpeditionHud instance;
        // 战规写进来的那几行：HUD 在时立刻画，Build 时也从这里读（ApplyLines）。
        // Show 先 Hide，Hide 会把它们清空——新一局开 HUD 不继承上一局的天象 / 诅咒 / 禁药读秒。
        private static string omenText = string.Empty;
        private static string curseText = string.Empty;
        private static string sealedText = string.Empty;
        private static ArenaExpeditionAmbience ambience = ArenaExpeditionAmbience.None;
        private static float heatGoal;

        private Canvas canvas;
        private Canvas ambienceCanvas;
        private TextMeshProUGUI omenLabel;
        private TextMeshProUGUI curseLabel;
        private TextMeshProUGUI sealedLabel;
        private RectTransform anchorRect;
        private Image tint;
        private Image vignette;
        private Image flash;
        private Image heat;
        private float heatLevel;
        private Color tintFrom, tintTo, vignetteFrom, vignetteTo;
        private float blend = 1f;
        private float flashStrength;
        private float flashAge = 99f;
        private float clock;

        internal static void Show()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return;
            Hide();
            try
            {
                Canvas root = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.HudOverlay, false);
                instance = root.gameObject.AddComponent<ArenaExpeditionHud>();
                instance.canvas = root;
                instance.Build(run);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令角标创建失败（不影响战斗）: " + e.Message);
                Hide();
            }
        }

        internal static void Hide()
        {
            ArenaExpeditionHud hud = instance;
            instance = null;
            omenText = curseText = sealedText = string.Empty;
            ambience = ArenaExpeditionAmbience.None;
            heatGoal = 0f;
            if (hud != null && hud.gameObject != null) Object.Destroy(hud.gameObject);
        }

        // ------------------------------------------------------------------
        // 战规（WavesArena/Trials）写进来的东西
        // ------------------------------------------------------------------

        /// <summary>天象名 / 诅咒名（空串隐藏）；animateOmen 时天象那行落一下墨。</summary>
        internal static void SetTrialLines(string omen, string curse, bool animateOmen)
        {
            omenText = omen ?? string.Empty;
            curseText = curse ?? string.Empty;
            ArenaExpeditionHud hud = instance;
            if (hud == null) return;
            hud.ApplyLines();
            if (animateOmen && hud.omenLabel != null && omenText.Length > 0)
            {
                BossRushInkMotion motion = BossRushInkMotion.For(hud.gameObject);
                TextMeshProUGUI label = hud.omenLabel;
                // 动效 0.35 秒后才起：先藏起来，免得新天象名先满色亮一下、再跳回透明淡入。
                label.alpha = 0f;
                motion.Add(0.35f, 0.5f, true, t =>
                {
                    if (label == null) return;
                    label.alpha = t;
                    label.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, t);
                });
            }
        }

        /// <summary>噩梦断补的禁药读秒（空串隐藏）。</summary>
        internal static void SetSealedLine(string text)
        {
            text = text ?? string.Empty;
            if (text == sealedText) return;
            sealedText = text;
            ArenaExpeditionHud hud = instance;
            if (hud != null) hud.ApplyLines();
        }

        /// <summary>换屏幕色调（淡入淡出，时长见 ArenaTrialsTuning.SkyBlendSeconds）。</summary>
        internal static void SetAmbience(ArenaExpeditionAmbience value)
        {
            if (value == ambience) return;
            ambience = value;
            ArenaExpeditionHud hud = instance;
            if (hud != null) hud.BeginBlend(true);
        }

        /// <summary>
        /// 雷暴的闪光（0–1）：天边一团冷银色的辉光（不是整屏平涂的白），像真闪电一样亮—暗—再亮两下，之后缓缓褪去。
        /// 每道闪电落在天边不同的位置。
        /// </summary>
        internal static void FlashAmbience(float strength)
        {
            ArenaExpeditionHud hud = instance;
            if (hud == null || hud.flash == null) return;
            hud.flashStrength = Mathf.Clamp01(strength);
            hud.flashAge = 0f;
            RectTransform rect = hud.flash.rectTransform;
            float x = UnityEngine.Random.Range(0.18f, 0.82f);
            rect.anchorMin = rect.anchorMax = new Vector2(x, 1.02f);
        }

        /// <summary>闪光包络：0.05 秒冲到顶、0.09 秒回落一截、0.15 秒再亮一次、之后 0.9 秒缓出褪尽。</summary>
        private static float FlashEnvelope(float age)
        {
            if (age < 0.05f) return age / 0.05f;
            if (age < 0.09f) return Mathf.Lerp(1f, 0.3f, (age - 0.05f) / 0.04f);
            if (age < 0.15f) return Mathf.Lerp(0.3f, 0.82f, (age - 0.09f) / 0.06f);
            if (age < 1.05f)
            {
                float t = (age - 0.15f) / 0.9f;
                return 0.82f * (1f - t) * (1f - t);
            }
            return 0f;
        }

        /// <summary>
        /// 焚城的热浪（0–1）：屏幕四周一圈余烬色的暗角，按噪声微微明灭；站到圈外时烧到最亮，贴着火墙时微微发烫。
        /// 升得快、退得慢（热是慢慢散的）。
        /// </summary>
        internal static void SetHeat(float level)
        {
            heatGoal = Mathf.Clamp01(level);
        }

        /// <summary>
        /// 变天：屏幕上方一道墨色横扫，天象名以大字墨书写出、旁落一方朱印，配一声低钟；停一会儿整体淡去。
        /// 只写天象名，不出说明字幕（具体效果在悬赏令与 Wiki 上）。焚城收紧也走这里：印文换成「战场」、钟换成战鼓。
        /// 返回是否真的演出来了（HUD 不在、构建失败时为 false，声音也没响）：调用方据此自己补一声。
        /// </summary>
        internal static bool PlayOmenSweep(string omenName, string sealText = "天象", string sfx = null)
        {
            ArenaExpeditionHud hud = instance;
            if (hud == null) return false;
            try
            {
                hud.BuildSweep(omenName ?? string.Empty, string.IsNullOrEmpty(sealText) ? "天象" : sealText,
                    string.IsNullOrEmpty(sfx) ? BossRushInkSkin.SfxTempleBell : sfx);
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 变天横扫失败（不影响战斗）: " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        private void Build(ArenaExpeditionRun run)
        {
            GameObject anchor = ZombieModeUIHelper.CreateRect("Anchor", canvas.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-30f, 170f), new Vector2(120f, 110f), new Vector2(1f, 0.5f));
            anchorRect = anchor.GetComponent<RectTransform>();

            RectTransform seal = BossRushInkSkin.CreateSeal(anchor.transform, ArenaExpeditionTuning.TierSeal(run.Order.Tier), SealSize);
            seal.anchoredPosition = new Vector2(0f, 14f);
            if (run.Order.Tier == ArenaExpeditionTier.Nightmare)
            {
                Image sealImage = seal.GetComponent<Image>();
                if (sealImage != null) sealImage.color = new Color(0.52f, 0.10f, 0.08f, 1f);
            }

            string line = run.Order.IsHell
                ? ArenaExpeditionTuning.LineupName(run.Order.Lineup)
                : (run.HasQueue ? ArenaExpeditionTuning.LengthName(run.Order.Length) : ArenaExpeditionTuning.LengthName(ArenaExpeditionLength.Full));
            if (L10n.IsChinese) line += "\n" + ArenaExpeditionTuning.TierNameEn(run.Order.Tier).ToUpperInvariant();
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Line", anchor.transform, line, 14f,
                new Vector2(0f, -36f), new Vector2(120f, 44f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            BossRushUI.ApplyGameFont(label);
            label.characterSpacing = 4f;
            label.lineSpacing = -8f;
            BossRushUIKit.ApplyWorldTextOutline(label);

            // 战规两行 + 禁药读秒：单行框高 ≥ 字号 × 1.45 + 4（TMP 省略号会把整串清空）
            omenLabel = CreateTrialLabel(anchor.transform, "Omen", -70f, 15f, BossRushInkColors.Paper);
            curseLabel = CreateTrialLabel(anchor.transform, "Curse", -94f, 14f, BossRushInkColors.CinnabarText);
            sealedLabel = CreateTrialLabel(anchor.transform, "Sealed", -117f, 13f, BossRushInkColors.Ember);
            ApplyLines();

            BossRushInkSkin.StampSeal(seal, anchorRect, 0.25f);
            // 加码（2026-11-27）：战帖印右上角再压一枚小级数印，档印落定后才落、不出第二声
            if (run.Order.Raise > 0)
            {
                RectTransform raiseSeal = BossRushInkSkin.CreateSeal(anchor.transform, ArenaExpeditionRaise.Numeral(run.Order.Raise),
                    L10n.IsChinese ? 30f : 34f);
                raiseSeal.anchoredPosition = new Vector2(32f, 40f);
                raiseSeal.localRotation = Quaternion.Euler(0f, 0f, -8f);
                BossRushInkSkin.StampSeal(raiseSeal, null, 0.55f, false);
            }
            BuildAmbience();
        }

        private static TextMeshProUGUI CreateTrialLabel(Transform parent, string name, float y, float size, Color color)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(name, parent, string.Empty, size,
                new Vector2(0f, y), new Vector2(200f, size * 1.45f + 6f), TextAlignmentOptions.Right, color);
            BossRushUI.ApplyGameFont(label);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.characterSpacing = 3f;
            label.rectTransform.anchoredPosition = new Vector2(-40f, y);
            BossRushUIKit.ApplyWorldTextOutline(label);
            return label;
        }

        private void ApplyLines()
        {
            SetLabel(omenLabel, omenText.Length > 0 ? L10n.T("天象 · ", "Omen · ") + omenText : string.Empty);
            SetLabel(curseLabel, curseText);
            SetLabel(sealedLabel, sealedText);
        }

        private static void SetLabel(TextMeshProUGUI label, string text)
        {
            if (label == null) return;
            label.text = text;
            label.gameObject.SetActive(text.Length > 0);
        }

        private void BuildAmbience()
        {
            ambienceCanvas = BossRushUI.CreateCanvasRoot(AmbienceName, BossRushUILayers.ScreenAmbience, false);
            Transform root = ambienceCanvas.transform;
            tint = CreateFullScreen(root, "Tint", null);
            Sprite vignetteSprite = BossRushFxKit.GetVignetteSprite();
            vignette = vignetteSprite != null ? CreateFullScreen(root, "Vignette", vignetteSprite) : null;
            // 焚城热浪：另一层同样的暗角，余烬色，只由 SetHeat 驱动（不跟天色混在一起）
            heat = vignetteSprite != null ? CreateFullScreen(root, "Heat", vignetteSprite) : null;
            // 闪电：一团很大的辉光挂在天边（锚点每道闪电重选），比整屏平涂的白更像远处的云里打闪
            flash = CreateFullScreen(root, "Flash", BossRushInkSkin.GetGlowSprite());
            RectTransform flashRect = flash.rectTransform;
            flashRect.anchorMin = flashRect.anchorMax = new Vector2(0.5f, 1.02f);
            flashRect.sizeDelta = new Vector2(2600f, 1700f);
            flash.color = new Color(0.80f, 0.86f, 0.98f, 0f);
            BeginBlend(false);
        }

        private static Image CreateFullScreen(Transform parent, string name, Sprite sprite)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            image.color = new Color(0f, 0f, 0f, 0f);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        private void BuildSweep(string omenName, string sealText, string sfx)
        {
            BossRushInkMotion motion = BossRushInkMotion.For(gameObject);
            GameObject holder = ZombieModeUIHelper.CreateRect("OmenSweep", canvas.transform, new Vector2(0.5f, 0.8f), new Vector2(1700f, 200f));
            RectTransform holderRect = holder.GetComponent<RectTransform>();
            CanvasGroup group = holder.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // 墨晕：横扫背后一团淡墨慢慢洇开
            GameObject bloomObject = ZombieModeUIHelper.CreateRect("Bloom", holder.transform, new Vector2(0.5f, 0.5f), new Vector2(1500f, 420f));
            RectTransform bloom = bloomObject.GetComponent<RectTransform>();
            Image blot = bloomObject.AddComponent<Image>();
            blot.sprite = BossRushInkSkin.GetBlotSprite();
            blot.raycastTarget = false;
            Color wash = BossRushInkColors.InkWash;
            blot.color = new Color(wash.r, wash.g, wash.b, 0f);
            motion.Add(0f, 0.7f, true, t =>
            {
                if (bloom == null) return;
                bloom.localScale = new Vector3(Mathf.Lerp(0.55f, 1f, t), Mathf.Lerp(0.4f, 1f, t), 1f);
                blot.color = new Color(wash.r, wash.g, wash.b, 0.5f * Mathf.Min(1f, t * 1.4f));
            });

            RectTransform stroke = BossRushInkSkin.CreateBrushStroke(holder.transform, "Stroke", new Vector2(1640f, 96f), Vector2.zero,
                new Color(wash.r, wash.g, wash.b, 0.8f));
            BossRushInkSkin.PlayBrushWrite(stroke, 0f, 0.55f, motion);
            RectTransform gold = BossRushInkSkin.CreateBrushStroke(holder.transform, "Gold", new Vector2(1100f, 6f), new Vector2(60f, -46f),
                new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.72f));
            BossRushInkSkin.PlayBrushWrite(gold, 0.24f, 0.5f, motion);

            if (omenName.Length > 0)
            {
                // 天象名：大字墨书，字距从散到聚（像墨在纸上收住），不缩放、不回弹
                TextMeshProUGUI title = ZombieModeUIHelper.CreateText("OmenName", holder.transform, omenName, 58f,
                    new Vector2(0f, 4f), new Vector2(1200f, 58f * 1.45f + 4f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
                BossRushUI.ApplyGameFont(title);
                title.fontStyle = FontStyles.Bold;
                title.enableWordWrapping = false;
                title.alpha = 0f;
                float spreadFrom = L10n.IsChinese ? 70f : 22f;
                float spreadTo = L10n.IsChinese ? 34f : 8f;
                title.characterSpacing = spreadFrom;
                motion.Add(0.16f, 0.6f, true, t =>
                {
                    if (title == null) return;
                    title.alpha = t;
                    title.characterSpacing = Mathf.Lerp(spreadFrom, spreadTo, t);
                });

                float half = Mathf.Min(560f, title.GetPreferredValues(omenName).x * 0.5f + 40f);
                RectTransform seal = BossRushInkSkin.CreateSeal(holder.transform, sealText, 46f);
                seal.anchoredPosition = new Vector2(half + 44f, -6f);
                seal.localRotation = Quaternion.Euler(0f, 0f, -6f);
                BossRushInkSkin.StampSeal(seal, holderRect, 0.62f);
            }

            motion.Add(1.9f, 0.9f, false, t => { if (group != null) group.alpha = 1f - t; }, () =>
            {
                if (holder != null) Object.Destroy(holder);
            });
            BossRushInkSkin.PlaySfx(sfx);
        }

        // ------------------------------------------------------------------
        // 屏幕色调
        // ------------------------------------------------------------------

        private void BeginBlend(bool animate)
        {
            if (tint == null) return;
            tintFrom = tint.color;
            vignetteFrom = vignette != null ? vignette.color : Color.clear;
            AmbienceColors(ambience, out tintTo, out vignetteTo);
            blend = animate ? 0f : 1f;
            if (!animate)
            {
                tint.color = tintTo;
                if (vignette != null) vignette.color = vignetteTo;
            }
        }

        /// <summary>各氛围的色调（全屏薄色）与暗角色。alpha 都很低：只染画面，不挡视线（永夜、血月例外，挡一点是故意的）。</summary>
        private static void AmbienceColors(ArenaExpeditionAmbience value, out Color tintColor, out Color vignetteColor)
        {
            switch (value)
            {
                case ArenaExpeditionAmbience.Rain:
                    tintColor = new Color(0.22f, 0.30f, 0.38f, 0.12f);
                    vignetteColor = new Color(0.10f, 0.14f, 0.20f, 0.30f);
                    return;
                case ArenaExpeditionAmbience.Snow:
                    tintColor = new Color(0.78f, 0.86f, 0.95f, 0.08f);
                    vignetteColor = new Color(0.86f, 0.92f, 1f, 0.28f);
                    return;
                case ArenaExpeditionAmbience.Dusk:
                    tintColor = new Color(0.95f, 0.52f, 0.22f, 0.10f);
                    vignetteColor = new Color(0.45f, 0.16f, 0.10f, 0.38f);
                    return;
                case ArenaExpeditionAmbience.Night:
                    tintColor = new Color(0.04f, 0.07f, 0.16f, 0.30f);
                    vignetteColor = new Color(0.01f, 0.02f, 0.06f, 0.62f);
                    return;
                case ArenaExpeditionAmbience.Blizzard:
                    tintColor = new Color(0.80f, 0.86f, 0.93f, 0.12f);
                    vignetteColor = new Color(0.86f, 0.90f, 0.95f, 0.42f);
                    return;
                case ArenaExpeditionAmbience.Thunder:
                    tintColor = new Color(0.10f, 0.12f, 0.20f, 0.22f);
                    vignetteColor = new Color(0.03f, 0.04f, 0.08f, 0.50f);
                    return;
                case ArenaExpeditionAmbience.BloodMoon:
                    tintColor = new Color(0.55f, 0.06f, 0.05f, 0.10f);
                    vignetteColor = new Color(RandomEventFx.BloodMoonTint.r, RandomEventFx.BloodMoonTint.g, RandomEventFx.BloodMoonTint.b, 0.55f);
                    return;
                case ArenaExpeditionAmbience.EternalNight:
                    tintColor = new Color(0.01f, 0.015f, 0.04f, 0.38f);
                    vignetteColor = new Color(0f, 0f, 0.01f, 0.85f);
                    return;
                case ArenaExpeditionAmbience.Downpour:
                    tintColor = new Color(0.16f, 0.22f, 0.28f, 0.18f);
                    vignetteColor = new Color(0.06f, 0.09f, 0.13f, 0.45f);
                    return;
                default:
                    tintColor = new Color(0f, 0f, 0f, 0f);
                    vignetteColor = new Color(0f, 0f, 0f, 0f);
                    return;
            }
        }

        private void TickAmbience(float dt)
        {
            if (tint == null) return;
            clock += dt;
            if (blend < 1f)
            {
                blend = Mathf.MoveTowards(blend, 1f, dt / Mathf.Max(0.1f, ArenaTrialsTuning.SkyBlendSeconds));
                float t = BossRushUI.SmoothStep(blend);
                tint.color = Color.Lerp(tintFrom, tintTo, t);
                if (vignette != null) vignette.color = Color.Lerp(vignetteFrom, vignetteTo, t);
            }
            else if (vignette != null && ambience == ArenaExpeditionAmbience.BloodMoon)
            {
                // 血月暗角呼吸（与鸭生无常的血月同色、同节奏量级）
                Color c = vignetteTo;
                c.a *= 0.82f + 0.18f * Mathf.Sin(clock * 1.6f);
                vignette.color = c;
            }
            if (heat != null && (heatLevel > 0f || heatGoal > 0f))
            {
                heatLevel = Mathf.MoveTowards(heatLevel, heatGoal, dt * (heatGoal > heatLevel ? 2.2f : 0.9f));
                float shimmer = 0.86f + 0.14f * Mathf.PerlinNoise(clock * 2.3f, 0.37f);
                heat.color = new Color(0.86f, 0.30f, 0.10f, heatLevel * 0.5f * shimmer);
            }
            if (flash != null && flashAge < 2f)
            {
                flashAge += dt;
                Color f = flash.color;
                f.a = FlashEnvelope(flashAge) * flashStrength * 0.34f;
                flash.color = f;
            }
        }

        private void Update()
        {
            bool visible = ArenaExpeditionState.Current != null && IsRunLive()
                && !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
            if (ambienceCanvas != null) ambienceCanvas.enabled = visible;
            if (visible) TickAmbience(Time.deltaTime);
        }

        private static bool IsRunLive()
        {
            ModBehaviour host = ModBehaviour.Instance;
            return host != null && host.IsActive;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (ambienceCanvas != null) Object.Destroy(ambienceCanvas.gameObject);
            ambienceCanvas = null;
        }
    }
}
