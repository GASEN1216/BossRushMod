// ============================================================================
// BossRushInkWidgets.cs - 全 Mod 复用的高阶水墨 UI 构件与粒子演出
// ============================================================================
// 模块说明：
//   配合 BossRushInkSkin / BossRushInkTheme，将「出征令」「冒险家日志」验证过的
//   旧宣纸卷轴、朱砂/鎏金/古铜印章、余烬/星火粒子场、毛笔题头、金笔底划标签与
//   水墨横幅扫入收口成单一共享工具箱，避免各界面复制同类动效与排版代码。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>旧宣纸 / 战报奏折色板（供出征令、冒险家日志、邸报、结算卷轴等共用）。</summary>
    internal static class BossRushInkParchmentColors
    {
        internal static readonly Color Paper = new Color(0.800f, 0.725f, 0.585f, 0.985f);
        internal static readonly Color ScorchedPaper = new Color(0.735f, 0.620f, 0.490f, 0.985f);
        internal static readonly Color Ink = new Color(0.120f, 0.090f, 0.070f, 1f);
        internal static readonly Color InkSoft = new Color(0.300f, 0.240f, 0.185f, 1f);
        internal static readonly Color Cinnabar = new Color(0.560f, 0.130f, 0.100f, 1f);
        internal static readonly Color CinnabarDark = new Color(0.470f, 0.075f, 0.055f, 1f);
        internal static readonly Color Bronze = new Color(0.540f, 0.390f, 0.130f, 1f);
        internal static readonly Color RodWood = new Color(0.095f, 0.065f, 0.048f, 1f);
        internal static readonly Color RodCap = new Color(0.560f, 0.430f, 0.180f, 1f);
        internal static readonly Color Stain = new Color(0.420f, 0.280f, 0.140f, 0.10f);
    }

    /// <summary>可向下展卷的旧宣纸卷轴壳。</summary>
    internal sealed class InkScrollShell
    {
        internal RectTransform Root;
        internal RectTransform UnrollMask;
        internal RectTransform Paper;
        internal Image PaperImage;
        internal RectTransform TopRod;
        internal RectTransform BottomRod;
        internal RectTransform Writ;
        internal float Height;
    }

    /// <summary>水墨高阶 UI 共享小件。</summary>
    internal static class BossRushInkWidgets
    {
        private const string SegmentUnderlineName = "InkSegmentUnderline";
        private const string HeaderStrokeName = "InkHeaderStroke";

        /// <summary>判断文本是否以拉丁字母为主，决定毛笔字距与宽度策略。</summary>
        internal static bool IsMostlyLatin(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int latin = 0;
            int cjk = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z') latin++;
                else if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
            }
            return latin > cjk;
        }

        /// <summary>构建一枚可自由指定印色的白文方印（朱砂 / 鎏金 / 古铜 / 淡墨等），可选延迟落印震动。</summary>
        internal static RectTransform CreateSealBadge(
            Transform parent,
            string labelText,
            float size,
            Color sealColor,
            Vector2 position,
            float angleDegrees = -8f,
            bool stampNow = false,
            RectTransform shakeTarget = null,
            float stampDelay = 0f,
            Color? inkTextColor = null)
        {
            if (parent == null) return null;
            RectTransform seal = BossRushInkSkin.CreateSeal(parent, labelText, size);
            seal.anchoredPosition = position;
            seal.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
            Image bg = seal.GetComponent<Image>();
            if (bg != null) bg.color = sealColor;
            if (inkTextColor.HasValue)
            {
                TextMeshProUGUI label = seal.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.color = inkTextColor.Value;
            }
            if (stampNow)
            {
                BossRushInkSkin.StampSeal(seal, shakeTarget, stampDelay);
            }
            return seal;
        }

        /// <summary>创建左侧对齐或居中的水墨标题 + 从左到右书写的金笔下划（不与已有分隔线冲突的场合使用）。</summary>
        internal static TextMeshProUGUI CreateHeaderTitle(
            Transform parent,
            string name,
            string titleText,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            bool addGoldenStroke = true,
            Vector2? anchorMin = null,
            Vector2? anchorMax = null,
            Vector2? pivot = null)
        {
            RectTransform rect;
            // 单行框高不到字号×1.45+4 时，TMP 的 Ellipsis 会把整串清空。
            size.y = Mathf.Max(size.y, Mathf.Ceil(fontSize * 1.45f) + 4f);
            GameObject go = ZombieModeUIHelper.CreateRect(
                name,
                parent,
                anchorMin ?? new Vector2(0.5f, 0.5f),
                anchorMax ?? new Vector2(0.5f, 0.5f),
                anchoredPosition,
                size,
                pivot ?? new Vector2(0.5f, 0.5f));
            rect = go.GetComponent<RectTransform>();
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(tmp);
            tmp.text = titleText ?? string.Empty;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = BossRushInkColors.Paper;
            tmp.alignment = alignment;
            tmp.characterSpacing = IsMostlyLatin(titleText) ? 2f : 4f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            if (addGoldenStroke)
            {
                AddLeftOrAlignedStroke(tmp, BossRushInkColors.Gold);
            }
            return tmp;
        }

        /// <summary>为已存在的标题 TMP 添加或刷新一条紧贴字底的金笔划（支持重算宽度与自左向右书写动画）。</summary>
        internal static RectTransform AddLeftOrAlignedStroke(TextMeshProUGUI title, Color strokeColor, float strokeHeight = 8f, bool writeReveal = true)
        {
            if (title == null) return null;
            // Destroy 要到帧末才生效：先摘下再销毁，同一帧第二次调用 Find 不会拿到待删的旧笔、漏掉第一次新建的那道
            // （也顺手清掉以前漏下的同名笔）。
            Transform old;
            while ((old = title.transform.Find(HeaderStrokeName)) != null)
            {
                old.SetParent(null, false);
                UnityEngine.Object.Destroy(old.gameObject);
            }
            if (string.IsNullOrEmpty(title.text)) return null;

            title.ForceMeshUpdate(true, false);
            Bounds bounds = title.textBounds;
            RectTransform rect = title.rectTransform;
            float size = title.fontSize;
            float textWidth = bounds.size.x > 0f ? bounds.size.x : title.GetPreferredValues(title.text).x;
            float width = Mathf.Min(Mathf.Max(64f, rect.rect.width > 0f ? rect.rect.width : rect.sizeDelta.x), Mathf.Max(size * 2.4f, textWidth + size * 0.55f));
            float xOffset = 0f;
            Vector2 center = rect.rect.center;
            if (bounds.size.x > 0f)
            {
                switch (title.horizontalAlignment)
                {
                    case HorizontalAlignmentOptions.Left:
                        xOffset = (bounds.min.x + width * 0.5f - size * 0.15f) - center.x;
                        break;
                    case HorizontalAlignmentOptions.Right:
                        xOffset = (bounds.max.x - width * 0.5f + size * 0.15f) - center.x;
                        break;
                    default:
                        xOffset = bounds.center.x - center.x;
                        break;
                }
            }
            else if (title.horizontalAlignment == HorizontalAlignmentOptions.Left)
            {
                xOffset = -rect.rect.width * 0.5f + width * 0.5f;
            }
            float yOffset = bounds.size.y > 0f ? (bounds.min.y - 5f) - center.y : (-size * 0.55f - 4f);

            RectTransform stroke = BossRushInkSkin.CreateBrushStroke(title.transform, HeaderStrokeName, new Vector2(width, strokeHeight),
                new Vector2(xOffset, yOffset), strokeColor);
            LayoutElement le = stroke.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            if (writeReveal)
            {
                stroke.gameObject.AddComponent<BossRushInkRevealStroke>();
            }
            return stroke;
        }

        /// <summary>构建共享的“×”墨风幽灵关闭钮（统一使用 BossRush 共享按钮体系，不再克隆官方 Prefab）。</summary>
        internal static Button CreateInkCloseButton(
            Transform parent,
            Vector2 anchor,
            Vector2 anchoredPosition,
            UnityAction onClick,
            float size = 36f)
        {
            Color ghost = new Color(BossRushUIColors.Danger.r, BossRushUIColors.Danger.g, BossRushUIColors.Danger.b, 0f);
            Button closeButton = ZombieModeUIHelper.CreateButton(
                "CloseButton",
                parent,
                "×",
                anchor,
                anchoredPosition,
                new Vector2(size, size),
                ghost,
                24f,
                new Vector2(size, size),
                onClick,
                true);
            IntegrationUIFeedback.StyleGhostCloseButton(
                closeButton,
                closeButton != null ? closeButton.GetComponentInChildren<TextMeshProUGUI>(true) : null);
            return closeButton;
        }

        /// <summary>设置或清除分段/标签按钮底部的金笔下划（幂等；支持切页时从左向右书写）。</summary>
        internal static void SetSegmentUnderline(Button button, bool selected, bool animate = true)
        {
            if (button == null) return;
            RectTransform rect = button.GetComponent<RectTransform>();
            if (rect == null) return;
            float buttonWidth = rect.rect.width > 8f ? rect.rect.width : rect.sizeDelta.x;
            float fullWidth = Mathf.Clamp(buttonWidth * 0.62f, 36f, Mathf.Max(36f, buttonWidth - 16f));

            Transform existing = button.transform.Find(SegmentUnderlineName);
            RectTransform underline = existing as RectTransform;
            if (!selected)
            {
                if (underline != null) underline.gameObject.SetActive(false);
                return;
            }

            if (underline == null)
            {
                underline = BossRushInkSkin.CreateBrushStroke(
                    button.transform,
                    SegmentUnderlineName,
                    new Vector2(fullWidth, 5f),
                    Vector2.zero,
                    BossRushInkColors.Gold);
                underline.anchorMin = new Vector2(0.5f, 0f);
                underline.anchorMax = new Vector2(0.5f, 0f);
                underline.pivot = new Vector2(0f, 0.5f);
                LayoutElement le = underline.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }
            underline.gameObject.SetActive(true);
            underline.anchoredPosition = new Vector2(-fullWidth * 0.5f, 4f);
            if (!animate || !button.gameObject.activeInHierarchy)
            {
                underline.sizeDelta = new Vector2(fullWidth, 5f);
                return;
            }
            underline.sizeDelta = new Vector2(0f, 5f);
            RectTransform target = underline;
            BossRushInkMotion.For(underline.gameObject).Add(0f, 0.24f, true, t =>
            {
                if (target != null) target.sizeDelta = new Vector2(fullWidth * t, 5f);
            });
        }

        /// <summary>同步修改任意共享按钮的边框色（兼容旧 "Stroke" 与水墨 "InkFrame"）。</summary>
        internal static void SetAnyButtonFrameColor(Button button, Color color)
        {
            if (button == null) return;
            Image target = button.targetGraphic as Image;
            if (target == null) return;
            Transform oldStroke = target.transform.Find("Stroke");
            Image stroke = oldStroke != null ? oldStroke.GetComponent<Image>() : null;
            if (stroke != null) stroke.color = color;
            BossRushInkSkin.SetFrameColor(target, color);
        }

        /// <summary>为面板或按钮添加一圈发散的墨环波纹（带音频选项）。</summary>
        internal static void PlayActionRipple(Transform parent, Vector2 position, float size, Color color, string sfx = null)
        {
            if (parent != null)
            {
                BossRushInkSkin.SpawnRing(parent, position, size, color, 0.45f, 1.85f, 0.42f);
            }
            if (!string.IsNullOrEmpty(sfx))
            {
                BossRushInkSkin.PlaySfx(sfx);
            }
        }

        /// <summary>组合播放水墨面板打开缩放、墨晕扩散与音效。墨晕由 <see cref="BossRushUI.PlayOpenAnimation"/> 放（只放一滴）。</summary>
        internal static void PlayInkSurfaceOpen(GameObject surface, string sfx = BossRushInkSkin.SfxPaperTurn)
        {
            if (surface == null) return;
            BossRushUI.PlayOpenAnimation(surface);
            if (!string.IsNullOrEmpty(sfx)) BossRushInkSkin.PlaySfx(sfx);
        }

        /// <summary>将一对轨道 / 填充条切换为水墨墨槽 + 横笔贴图。</summary>
        internal static void StyleInkBarPair(Image track, Image fill, Color trackColor, Color fillColor)
        {
            if (track != null)
            {
                track.sprite = BossRushInkSkin.GetSurfaceSprite();
                track.type = Image.Type.Sliced;
                track.pixelsPerUnitMultiplier = 2f;
                track.color = trackColor;
                track.raycastTarget = false;
            }
            if (fill != null)
            {
                fill.sprite = BossRushInkSkin.GetBrushSprite();
                fill.type = Image.Type.Simple;
                fill.color = fillColor;
                fill.raycastTarget = false;
            }
        }

        /// <summary>
        /// 构建一张可从上轴向下展开的旧宣纸卷轴壳（用于战报奏折、古宣邸报、揭晓简报等）。
        /// </summary>
        internal static InkScrollShell CreateScrollShell(
            Transform parent,
            string name,
            Vector2 centerPos,
            Vector2 size,
            bool scorched = false,
            bool addStains = true)
        {
            GameObject rootObj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(size.x + 70f, size.y + 56f));
            RectTransform root = rootObj.GetComponent<RectTransform>();
            root.anchoredPosition = centerPos;
            float top = size.y * 0.5f;

            GameObject maskObj = ZombieModeUIHelper.CreateRect("Unroll", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, top), new Vector2(size.x + 32f, size.y), new Vector2(0.5f, 1f));
            RectTransform unrollMask = maskObj.GetComponent<RectTransform>();
            maskObj.AddComponent<RectMask2D>();

            GameObject paperObj = ZombieModeUIHelper.CreateRect("Paper", unrollMask, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, size, new Vector2(0.5f, 1f));
            RectTransform paper = paperObj.GetComponent<RectTransform>();
            Image paperImage = paperObj.AddComponent<Image>();
            paperImage.sprite = BossRushInkSkin.GetSurfaceSprite();
            paperImage.type = Image.Type.Sliced;
            paperImage.pixelsPerUnitMultiplier = 1.3f;
            paperImage.color = scorched ? BossRushInkParchmentColors.ScorchedPaper : BossRushInkParchmentColors.Paper;
            paperImage.raycastTarget = true;

            Image fiber = AddPaperInsetLayer(paper, "Fiber", BossRushInkSkin.GetFiberSprite(), new Color(0.30f, 0.20f, 0.10f, 0.20f), 10f);
            fiber.type = Image.Type.Tiled;

            if (addStains)
            {
                AddPaperStain(paper, new Vector2(-size.x * 0.32f, size.y * 0.30f), Mathf.Min(240f, size.x * 0.42f), 16f);
                AddPaperStain(paper, new Vector2(size.x * 0.30f, -size.y * 0.28f), Mathf.Min(260f, size.x * 0.45f), -24f);
            }

            Image outerBorder = AddPaperInsetLayer(paper, "Border", BossRushInkSkin.GetFrameSprite(), FadeColor(BossRushInkParchmentColors.Ink, 0.62f), 16f);
            outerBorder.type = Image.Type.Sliced;
            outerBorder.fillCenter = false;
            outerBorder.pixelsPerUnitMultiplier = 1.5f;

            Image innerBorder = AddPaperInsetLayer(paper, "BorderInner", BossRushInkSkin.GetFrameSprite(), FadeColor(BossRushInkParchmentColors.Ink, 0.22f), 26f);
            innerBorder.type = Image.Type.Sliced;
            innerBorder.fillCenter = false;
            innerBorder.pixelsPerUnitMultiplier = 2.4f;

            GameObject writObj = ZombieModeUIHelper.CreateRect("Writ", paper, new Vector2(0.5f, 0.5f), size);
            RectTransform writ = writObj.GetComponent<RectTransform>();

            RectTransform topRod = CreateScrollRod(root, "RodTop", size.x, top + 2f);
            RectTransform bottomRod = CreateScrollRod(root, "RodBottom", size.x, -top - 2f);

            return new InkScrollShell
            {
                Root = root,
                UnrollMask = unrollMask,
                Paper = paper,
                PaperImage = paperImage,
                TopRod = topRod,
                BottomRod = bottomRod,
                Writ = writ,
                Height = size.y,
            };
        }

        /// <summary>播放卷轴从上轴自上而下展开的动效并配翻纸声。</summary>
        internal static void PlayScrollUnroll(InkScrollShell shell, float delay = 0.18f, float duration = 0.46f)
        {
            if (shell == null || shell.Root == null || shell.UnrollMask == null || shell.BottomRod == null) return;
            float full = shell.Height;
            float top = full * 0.5f;
            shell.UnrollMask.sizeDelta = new Vector2(shell.UnrollMask.sizeDelta.x, 0f);
            shell.BottomRod.anchoredPosition = new Vector2(0f, top - 2f);
            BossRushInkMotion motion = BossRushInkMotion.For(shell.Root.gameObject);
            motion.Add(delay, duration, true, t =>
            {
                float h = full * t;
                if (shell.UnrollMask != null) shell.UnrollMask.sizeDelta = new Vector2(shell.UnrollMask.sizeDelta.x, h);
                if (shell.BottomRod != null) shell.BottomRod.anchoredPosition = new Vector2(0f, top - h - 2f);
            });
            motion.Add(delay, 0.01f, false, _ => { }, () => BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxPaperTurn));
        }

        /// <summary>为一个矩形容器挂接或获取水墨余烬 / 灵光粒子场。</summary>
        internal static BossRushInkEmberField AttachEmberField(
            RectTransform area,
            Color primaryColor,
            Color secondaryColor,
            bool startBurning = true,
            int maxMotes = 12,
            float interval = 0.12f)
        {
            if (area == null) return null;
            BossRushInkEmberField field = area.GetComponent<BossRushInkEmberField>();
            if (field == null) field = area.gameObject.AddComponent<BossRushInkEmberField>();
            field.Configure(area, primaryColor, secondaryColor, maxMotes, interval);
            field.SetBurning(startBurning);
            return field;
        }

        private static Image AddPaperInsetLayer(RectTransform parent, string name, Sprite sprite, Color color, float inset)
        {
            GameObject layer = ZombieModeUIHelper.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = layer.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void AddPaperStain(RectTransform parent, Vector2 position, float size, float angle)
        {
            GameObject stain = ZombieModeUIHelper.CreateRect("Stain", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size * 0.8f));
            RectTransform rect = stain.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = stain.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetBlotSprite();
            image.color = BossRushInkParchmentColors.Stain;
            image.raycastTarget = false;
        }

        private static RectTransform CreateScrollRod(Transform parent, string name, float paperWidth, float y)
        {
            float width = paperWidth + 40f;
            GameObject rodObj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(width, 18f));
            RectTransform rod = rodObj.GetComponent<RectTransform>();
            rod.anchoredPosition = new Vector2(0f, y);
            Image wood = rodObj.AddComponent<Image>();
            wood.sprite = BossRushInkSkin.GetSurfaceSprite();
            wood.type = Image.Type.Sliced;
            wood.pixelsPerUnitMultiplier = 3.2f;
            wood.color = BossRushInkParchmentColors.RodWood;
            wood.raycastTarget = false;
            BossRushInkSkin.CreateBrushStroke(rod, "Sheen", new Vector2(width - 36f, 3f), new Vector2(0f, 3.5f),
                FadeColor(BossRushInkColors.Gold, 0.30f));
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject capObj = ZombieModeUIHelper.CreateRect("Cap", rod, new Vector2(0.5f, 0.5f), new Vector2(14f, 28f));
                capObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(side * (width * 0.5f + 5f), 0f);
                Image cap = capObj.AddComponent<Image>();
                cap.sprite = BossRushInkSkin.GetSurfaceSprite();
                cap.type = Image.Type.Sliced;
                cap.pixelsPerUnitMultiplier = 4f;
                cap.color = BossRushInkParchmentColors.RodCap;
                cap.raycastTarget = false;
            }
            return rod;
        }

        private static Color FadeColor(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }

    /// <summary>
    /// 通用水墨火星 / 灵光粒子场：在给定矩形边沿或范围内缓缓升起微粒，走 unscaled 时间并在暂停时冻结。
    /// </summary>
    internal sealed class BossRushInkEmberField : MonoBehaviour
    {
        private RectTransform area;
        private Color primaryColor = BossRushInkColors.Ember;
        private Color secondaryColor = BossRushInkColors.Gold;
        private int maxMotes = 12;
        private float spawnInterval = 0.12f;
        private bool burning;
        private float timer;
        private int alive;

        internal void Configure(RectTransform target, Color primary, Color secondary, int cap, float interval)
        {
            area = target;
            primaryColor = primary;
            secondaryColor = secondary;
            maxMotes = Mathf.Clamp(cap, 1, 24);
            spawnInterval = Mathf.Max(0.05f, interval);
        }

        internal void SetBurning(bool value)
        {
            if (value && !burning) timer = 0f;
            burning = value;
        }

        /// <summary>在某次动作/揭晓时瞬时释放一簇微粒。</summary>
        internal void SpawnBurst(int count)
        {
            if (area == null) return;
            int spawn = Mathf.Clamp(count, 1, 14);
            for (int i = 0; i < spawn; i++)
            {
                SpawnMote(true);
            }
        }

        private void Update()
        {
            if (!burning || area == null || BossRushUI.IsGamePaused()) return;
            timer -= Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (timer > 0f || alive >= maxMotes) return;
            timer = spawnInterval;
            SpawnMote(false);
        }

        private void SpawnMote(bool burstCenter)
        {
            Vector2 size = area.rect.size;
            if (size.x < 16f || size.y < 16f) size = area.sizeDelta;
            Vector2 start;
            if (burstCenter)
            {
                start = new Vector2(UnityEngine.Random.Range(-0.26f, 0.26f) * size.x, UnityEngine.Random.Range(-0.18f, 0.18f) * size.y);
            }
            else
            {
                float side = UnityEngine.Random.value;
                if (side < 0.55f) start = new Vector2(UnityEngine.Random.Range(-0.48f, 0.48f) * size.x, -size.y * 0.48f);
                else if (side < 0.775f) start = new Vector2(-size.x * 0.48f, UnityEngine.Random.Range(-0.44f, 0.22f) * size.y);
                else start = new Vector2(size.x * 0.48f, UnityEngine.Random.Range(-0.44f, 0.22f) * size.y);
            }

            float moteSize = UnityEngine.Random.Range(burstCenter ? 6f : 5f, burstCenter ? 13f : 10f);
            GameObject mote = ZombieModeUIHelper.CreateRect("InkMote", area, new Vector2(0.5f, 0.5f), new Vector2(moteSize, moteSize));
            // 粒子场常挂在带 LayoutGroup 的面板上：不退出布局的话每颗粒子会被排成一行。
            mote.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rect = mote.GetComponent<RectTransform>();
            rect.anchoredPosition = start;
            Image image = mote.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetGlowSprite();
            image.raycastTarget = false;
            Color tint = Color.Lerp(primaryColor, secondaryColor, UnityEngine.Random.value * 0.65f);
            image.color = new Color(tint.r, tint.g, tint.b, 0f);

            float rise = UnityEngine.Random.Range(burstCenter ? 46f : 34f, burstCenter ? 110f : 86f);
            float drift = UnityEngine.Random.Range(-22f, 22f);
            float life = UnityEngine.Random.Range(0.85f, 1.55f);
            alive++;
            BossRushInkMotion.For(mote).Add(0f, life, false, t =>
            {
                if (rect == null) return;
                rect.anchoredPosition = start + new Vector2(drift * t + Mathf.Sin(t * 9f) * 3f, rise * t);
                float a = t < 0.2f ? t / 0.2f : 1f - (t - 0.2f) / 0.8f;
                image.color = new Color(tint.r, tint.g, tint.b, 0.88f * a);
            }, () =>
            {
                alive = Mathf.Max(0, alive - 1);
                if (mote != null) UnityEngine.Object.Destroy(mote);
            });
        }
    }
}
