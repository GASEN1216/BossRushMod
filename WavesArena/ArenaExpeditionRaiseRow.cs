// ============================================================================
// ArenaExpeditionRaiseRow.cs - 出征令面板「加码」一栏：战帖卡下面一排由易到难的朱印台阶
// ============================================================================
// 2026-11-27 owner：「第三批搞成杀戮尖塔那种，战规简单到难排序，玩家自己选择加码」；UI、交互、特效要高端大气、有史诗感。
// 一排台阶：最左「不加」，往右 Ⅰ、Ⅱ……每级一枚印，印下写这一级背上的战规名；点哪一级就加到哪一级（累进：左边的全亮）。
//   加码：新亮的印从左到右依次按下（落印、墨环散开，只第一枚出印泥声），一道朱笔从「不加」写到当前级；
//   减码：右边几级的印慢慢褪回暗处，朱笔收回；
//   加满：最高一级燃起余烬、外圈一道金环，标准加满一声磬，困难加满一声古钟（与噩梦同一口钟：这时局内机制已与噩梦相同）。
//   本档通关过的最高级上方一个小金字「最高」。轻松 / 噩梦没有梯子：整栏压暗，只写一句为什么不加码。
// 归属：本类只管这一栏的画面与点击；选中的级数由 ArenaExpeditionOrderPanel 持有（每档各记一份），点了回调面板，
//   面板改完数再调 Show 让这里演出来、重写悬赏令。动效走 BossRushInkMotion（unscaled，暂停冻结），缓动只用 EaseOut / SmoothStep。
// 连点不乱：每枚印的印面每次换状态都重建（旧印面上的落印动效随物体一起停），朱笔按代号只认最后一次。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ArenaExpeditionRaiseRow
    {
        private const float Span = 860f;
        private const float MaxStep = 132f;
        private const float RungSize = 58f;
        private const float FaceSize = 44f;
        private const float RungY = 12f;
        private const float NameTop = -22f;
        private const float StampStagger = 0.1f;

        private static readonly Color IdleFill = BossRushInkColors.SurfaceRaised;
        private static readonly Color LitFill = new Color(0.20f, 0.085f, 0.065f, 0.97f);
        private static readonly Color IdleFrame = new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.26f);
        private static readonly Color LitFrame = new Color(BossRushInkColors.Cinnabar.r, BossRushInkColors.Cinnabar.g, BossRushInkColors.Cinnabar.b, 0.85f);

        private sealed class Rung
        {
            internal int Level;
            internal Button Button;
            internal RectTransform Rect;
            internal RectTransform Face;
            internal TextMeshProUGUI Numeral;
            internal TextMeshProUGUI Name;
            internal CanvasGroup Group;
            internal ArenaExpeditionEmberField Embers;
            internal bool Lit;
        }

        private readonly RectTransform row;
        private readonly float centerX;
        private readonly Action<int> onPick;
        private readonly Func<bool> blocked;
        private readonly CanvasGroup rowGroup;
        private readonly List<Rung> rungs = new List<Rung>(9);
        private RectTransform ladder;
        private RectTransform trail;
        private float trailFull;
        private float step;
        private ArenaExpeditionTier shownTier;
        private int shownRaise = -1;
        private bool built;
        private int trailGeneration;

        /// <param name="row">这一栏的容器（面板已在左边放好行标）。</param>
        /// <param name="centerX">台阶居中的横坐标（与上面几排卡片同一条中线）。</param>
        internal ArenaExpeditionRaiseRow(RectTransform row, float centerX, Action<int> onPick, Func<bool> blocked)
        {
            this.row = row;
            this.centerX = centerX;
            this.onPick = onPick;
            this.blocked = blocked;
            rowGroup = row.gameObject.GetComponent<CanvasGroup>();
            if (rowGroup == null) rowGroup = row.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>显示某档加到第几级。换档整排重排（从左到右淡入）；同档改级数只演变化的那几级。</summary>
        internal void Show(ArenaExpeditionTier tier, int raise, bool animate)
        {
            raise = ArenaExpeditionRaise.Clamp(tier, raise);
            if (!built || tier != shownTier)
            {
                Rebuild(tier, raise, animate);
                return;
            }
            if (raise == shownRaise) return;
            Transition(shownRaise, raise, animate);
        }

        // ------------------------------------------------------------------
        // 排布
        // ------------------------------------------------------------------

        private void Rebuild(ArenaExpeditionTier tier, int raise, bool animate)
        {
            built = true;
            shownTier = tier;
            shownRaise = raise;
            rungs.Clear();
            trail = null;
            trailGeneration++;
            if (ladder != null) UnityEngine.Object.Destroy(ladder.gameObject);
            GameObject ladderObject = ZombieModeUIHelper.CreateRect("Ladder", row, new Vector2(0.5f, 0.5f), row.sizeDelta);
            ladder = ladderObject.GetComponent<RectTransform>();

            IList<ArenaExpeditionRung> steps = ArenaExpeditionRaise.LadderFor(tier);
            int max = steps.Count;
            if (max == 0)
            {
                rowGroup.alpha = 0.5f;
                rowGroup.blocksRaycasts = false;
                string note = tier == ArenaExpeditionTier.Nightmare
                    ? L10n.T("噩梦已是顶档，无码可加", "Nightmare is the summit; nothing left to raise")
                    : L10n.T("轻松是宽限档，不设加码", "Relaxed is the grace tier; no raises");
                TextMeshProUGUI label = ZombieModeUIHelper.CreateText("NoRaise", ladder, note, 15f, new Vector2(centerX, 4f),
                    new Vector2(Span, 26f), TextAlignmentOptions.Center, BossRushInkColors.PaperDim);
                BossRushUI.ApplyGameFont(label);
                label.fontStyle = FontStyles.Italic;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                return;
            }
            rowGroup.alpha = 1f;
            rowGroup.blocksRaycasts = true;

            step = Mathf.Min(MaxStep, Span / (max + 1));
            float x0 = centerX - step * max * 0.5f;
            trailFull = step * max;

            // 底下一道淡金的台阶线；朱笔压在它上面，从「不加」写到当前级
            BossRushInkSkin.CreateBrushStroke(ladder, "Rail", new Vector2(trailFull, 4f), new Vector2(x0 + trailFull * 0.5f, RungY),
                new Color(BossRushInkColors.Gold.r, BossRushInkColors.Gold.g, BossRushInkColors.Gold.b, 0.2f));
            trail = BossRushInkSkin.CreateBrushStroke(ladder, "Trail", new Vector2(trailFull, 7f), Vector2.zero,
                new Color(BossRushInkColors.Cinnabar.r, BossRushInkColors.Cinnabar.g, BossRushInkColors.Cinnabar.b, 0.72f));
            trail.pivot = new Vector2(0f, 0.5f);
            trail.anchoredPosition = new Vector2(x0, RungY);
            trail.sizeDelta = new Vector2(TrailLength(raise), trail.sizeDelta.y);

            int best = ArenaExpeditionRaise.Best(tier);
            for (int level = 0; level <= max; level++)
            {
                Rung rung = CreateRung(level, new Vector2(x0 + step * level, RungY), level > 0 ? ArenaExpeditionTuning.RaiseRule(steps[level - 1]) : null);
                rungs.Add(rung);
                if (level > 0 && level == best) CreateBestMark(rung);
                ApplyLook(rung, raise, max);
                if (level > 0 && level <= raise)
                {
                    rung.Lit = true;
                    SetFace(rung, -1f, false);
                }
                if (animate)
                {
                    CanvasGroup group = rung.Group;
                    RectTransform rect = rung.Rect;
                    Vector2 rest = rect.anchoredPosition;
                    group.alpha = 0f;
                    rect.anchoredPosition = rest + new Vector2(0f, -6f);
                    BossRushInkMotion.For(rect.gameObject).Add(level * 0.035f, 0.26f, true, t =>
                    {
                        if (group == null || rect == null) return;
                        group.alpha = t;
                        rect.anchoredPosition = rest + new Vector2(0f, -6f * (1f - t));
                    });
                }
            }
            if (raise == max) LightSummit(rungs[max], false);
        }

        private Rung CreateRung(int level, Vector2 position, ArenaExpeditionRule rule)
        {
            bool chinese = L10n.IsChinese;
            Rung rung = new Rung { Level = level };
            Vector2 size = level == 0 ? new Vector2(RungSize * 0.86f, RungSize * 0.86f) : new Vector2(RungSize, RungSize);
            int captured = level;
            rung.Button = ZombieModeUIHelper.CreateButton("Rung" + level, ladder, string.Empty, new Vector2(0.5f, 0.5f), position, size,
                IdleFill, 16f, size, () => Pick(captured), true);
            BossRushInkSkin.StyleInkButton(rung.Button, BossRushInkButtonRole.Secondary);
            rung.Rect = rung.Button.GetComponent<RectTransform>();
            rung.Group = rung.Button.gameObject.AddComponent<CanvasGroup>();

            string numeral = level == 0 ? L10n.T("不加", "NONE") : ArenaExpeditionRaise.Numeral(level);
            float glyph = level == 0 ? (chinese ? 15f : 11f) : (chinese ? 26f : 19f);
            rung.Numeral = ZombieModeUIHelper.CreateText("Numeral", rung.Rect, numeral, glyph, Vector2.zero,
                new Vector2(size.x - 6f, size.y - 6f), TextAlignmentOptions.Center, BossRushInkColors.PaperDim);
            BossRushUI.ApplyGameFont(rung.Numeral);
            rung.Numeral.fontStyle = FontStyles.Bold;
            rung.Numeral.enableWordWrapping = false;
            rung.Numeral.margin = Vector4.zero;
            rung.Numeral.enableAutoSizing = true;
            rung.Numeral.fontSizeMin = Mathf.Min(glyph, 10f);
            rung.Numeral.fontSizeMax = glyph;
            rung.Numeral.raycastTarget = false;

            if (rule != null)
            {
                float nameSize = chinese ? 13f : 10f;
                rung.Name = ZombieModeUIHelper.CreateText("RungName", ladder, rule.Name, nameSize, Vector2.zero,
                    new Vector2(step - 6f, 10f), TextAlignmentOptions.Top, BossRushInkColors.PaperDim);
                BossRushUI.ApplyGameFont(rung.Name);
                rung.Name.raycastTarget = false;
                rung.Name.enableWordWrapping = true;
                rung.Name.overflowMode = TextOverflowModes.Overflow;
                float lineBox = Mathf.Ceil(nameSize * 1.45f) + 4f;
                float height = Mathf.Max(lineBox, Mathf.Ceil(rung.Name.GetPreferredValues(rule.Name, step - 6f, 0f).y) + 4f);
                rung.Name.rectTransform.sizeDelta = new Vector2(step - 6f, height);
                rung.Name.rectTransform.anchoredPosition = new Vector2(position.x, NameTop - height * 0.5f);
            }
            return rung;
        }

        private void CreateBestMark(Rung rung)
        {
            // 紧贴在印的上沿：再往上就碰到战帖卡的下边
            TextMeshProUGUI mark = ZombieModeUIHelper.CreateText("Best", ladder, L10n.T("最高", "BEST"), L10n.IsChinese ? 12f : 10f,
                rung.Rect.anchoredPosition + new Vector2(0f, RungSize * 0.5f + 9f), new Vector2(step, 18f),
                TextAlignmentOptions.Center, BossRushInkColors.Gold);
            BossRushUI.ApplyGameFont(mark);
            mark.fontStyle = FontStyles.Bold;
            mark.characterSpacing = L10n.IsChinese ? 4f : 3f;
            mark.enableWordWrapping = false;
            mark.overflowMode = TextOverflowModes.Overflow;
            mark.raycastTarget = false;
        }

        private float TrailLength(int raise)
        {
            return Mathf.Clamp(step * raise, 0f, trailFull);
        }

        // ------------------------------------------------------------------
        // 状态与演出
        // ------------------------------------------------------------------

        private void Pick(int level)
        {
            if (blocked != null && blocked()) return;
            if (onPick != null) onPick(level);
        }

        /// <summary>一级的底色、边框与字色（不含印面）：亮着的是暗朱底白字，当前级金边，「不加」在 0 级时金边。</summary>
        private static void ApplyLook(Rung rung, int raise, int max)
        {
            bool lit = rung.Level > 0 && rung.Level <= raise;
            bool top = rung.Level == raise;
            ZombieModeUIHelper.SetButtonBaseColor(rung.Button, lit ? LitFill : IdleFill);
            BossRushInkSkin.SetFrameColor(rung.Button.targetGraphic, top ? BossRushInkColors.Gold : lit ? LitFrame : IdleFrame);
            if (rung.Numeral != null) rung.Numeral.color = lit || top ? BossRushInkColors.Paper : BossRushInkColors.PaperDim;
            if (rung.Name != null)
            {
                Color dim = BossRushInkColors.PaperDim;
                rung.Name.color = lit ? BossRushInkColors.Paper : new Color(dim.r, dim.g, dim.b, 0.72f);
            }
            if (rung.Embers != null && !(top && rung.Level == max)) rung.Embers.SetBurning(false);
        }

        private void Transition(int from, int to, bool animate)
        {
            shownRaise = to;
            int max = rungs.Count - 1;
            for (int i = 0; i < rungs.Count; i++)
            {
                Rung rung = rungs[i];
                ApplyLook(rung, to, max);
                bool lit = rung.Level > 0 && rung.Level <= to;
                if (lit && !rung.Lit)
                {
                    // 新亮的那几级从左到右依次落印；只有第一枚出印泥声，免得连成一片
                    rung.Lit = true;
                    SetFace(rung, animate ? (rung.Level - from - 1) * StampStagger : -1f, rung.Level == from + 1);
                }
                else if (!lit && rung.Lit)
                {
                    rung.Lit = false;
                    FadeFace(rung, animate);
                }
            }
            MoveTrail(to, animate, Mathf.Abs(to - from));
            if (to == max && from < max) LightSummit(rungs[max], animate, (to - from - 1) * StampStagger + 0.2f);
        }

        /// <summary>换上新印面；delay &lt; 0 直接亮着，否则 delay 秒后落印。旧印面连同它身上的动效一起销毁。</summary>
        private static void SetFace(Rung rung, float delay, bool sound)
        {
            if (rung.Face != null) UnityEngine.Object.Destroy(rung.Face.gameObject);
            RectTransform face = BossRushInkSkin.CreateSeal(rung.Rect, string.Empty, FaceSize);
            face.localRotation = Quaternion.Euler(0f, 0f, rung.Level % 2 == 0 ? 3f : -3f);
            // 第一个子物体：压在按钮底色之上，墨笔边与级数字之下
            face.SetAsFirstSibling();
            rung.Face = face;
            if (delay >= 0f) BossRushInkSkin.StampSeal(face, null, delay, sound);
        }

        /// <summary>印面褪回暗处（不落印、不出声），褪完销毁。</summary>
        private static void FadeFace(Rung rung, bool animate)
        {
            RectTransform face = rung.Face;
            rung.Face = null;
            if (face == null) return;
            if (!animate)
            {
                UnityEngine.Object.Destroy(face.gameObject);
                return;
            }
            CanvasGroup group = face.GetComponent<CanvasGroup>();
            if (group == null) group = face.gameObject.AddComponent<CanvasGroup>();
            float start = group.alpha;
            BossRushInkMotion.For(face.gameObject).Add(0f, 0.28f, false, t =>
            {
                if (group != null) group.alpha = start * (1f - t);
            }, () => { if (face != null) UnityEngine.Object.Destroy(face.gameObject); });
        }

        private void MoveTrail(int raise, bool animate, int stepsMoved)
        {
            if (trail == null) return;
            int generation = ++trailGeneration;
            float target = TrailLength(raise);
            RectTransform stroke = trail;
            if (!animate)
            {
                stroke.sizeDelta = new Vector2(target, stroke.sizeDelta.y);
                return;
            }
            float start = stroke.sizeDelta.x;
            float seconds = 0.2f + StampStagger * Mathf.Max(1, stepsMoved);
            BossRushInkMotion.For(stroke.gameObject).Add(0f, seconds, true, t =>
            {
                if (stroke == null || generation != trailGeneration) return;
                stroke.sizeDelta = new Vector2(Mathf.Lerp(start, target, t), stroke.sizeDelta.y);
            });
        }

        /// <summary>加满：最高一级燃起余烬、外圈一道金环；困难加满一声古钟，标准加满一声磬。</summary>
        private void LightSummit(Rung rung, bool animate, float delay = 0f)
        {
            if (rung == null || rung.Rect == null) return;
            if (rung.Embers == null)
            {
                rung.Embers = rung.Rect.gameObject.AddComponent<ArenaExpeditionEmberField>();
                rung.Embers.Bind(rung.Rect);
            }
            ArenaExpeditionEmberField embers = rung.Embers;
            if (!animate)
            {
                embers.SetBurning(true);
                return;
            }
            RectTransform rect = rung.Rect;
            bool hard = shownTier == ArenaExpeditionTier.Hard;
            BossRushInkMotion.For(rect.gameObject).Add(delay, 0.01f, false, t => { }, () =>
            {
                if (rect == null || shownRaise != rungs.Count - 1) return;
                if (embers != null) embers.SetBurning(true);
                BossRushInkSkin.SpawnRing(rect.parent, BossRushInkSkin.CenterInParent(rect), RungSize * 1.2f, BossRushInkColors.Gold, 0.5f, 2.1f, 0.7f);
                BossRushInkSkin.PlaySfx(hard ? BossRushInkSkin.SfxTempleBell : BossRushInkSkin.SfxChimeStone);
            });
        }
    }
}
