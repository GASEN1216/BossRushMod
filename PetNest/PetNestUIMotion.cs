// ============================================================================
// PetNestUIMotion.cs - 遗种巢主面板的局部刷新与动效接线（2026-10-17 UI 与交互对照审查）
// ============================================================================
// 与 PetNestUI.cs 是同一个 partial 类，拆开为行数预算（PetNestUI.cs 贴着 1200 行）。
// 只在 PetNestUI 已建好的 surface / canvas 里摆东西：**不创建 canvas、不碰 sortingOrder**。
//
// 审查结论里「塑料感」的主要来源是同页任何点击都整页 ClearSpawned 再重建：选中、勾选瞬切，
// 被点的那颗按钮连按下回弹都放不完。这里分两层：
//   - A-1 巢页点选 / 勾选：行不动，只改新旧两行的选中描边（淡入 / 淡回），右栏详情单独重画并交叉淡入；
//   - 其余同页重绘仍整页重建，但「刚被选中」的那一项（按 Key 判断）描边淡入，没变的不重播；
//   - A-2 页签下一条指示条滑到选中页签，选中页签不再整块填 AccentFill（每屏只留一个主操作色块）；
//   - A-3 进度条从上次的值平滑走到新值、在途远征的进度条跟着走表一起走，数字滚动；
//   - A-4 成功操作弹底部轻提示 + 官方确认音；
//   - G-1 官方翻页键（Q / E、手柄肩键）切页签；
//   - 演出（孵化揭晓 / 远征翻牌）关掉时面板自动刷新一次，就地揭晓之后列表立刻是新的。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class PetNestUI
    {
        #region 状态

        private static readonly PetNestUIPage[] PageOrder =
        {
            PetNestUIPage.Nest, PetNestUIPage.Hatch, PetNestUIPage.Expedition, PetNestUIPage.Museum,
        };

        private const float SelectFadeSeconds = 0.14f;
        private const float DetailFadeSeconds = 0.16f;
        private const float TabSlideSeconds = 0.18f;

        private PetNestSortMode _sortMode;
        /// <summary>下一次 Refresh 是巢页点选 / 勾选触发的，可以走局部刷新。</summary>
        private bool _softRefreshPending;
        /// <summary>本次渲染是同页重绘：新选中的项描边淡入。切页时为假（整页入场动画已经够了）。</summary>
        private bool _animateSelection;

        // 巢页左栏的行（含出战席位格）：Key -> 卡片，局部刷新据此原地改选中态
        private readonly Dictionary<string, GameObject> _nestRows = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly List<string> _nestRowOrder = new List<string>();

        // 选中项的 Key：上一次渲染 / 这一次渲染
        private HashSet<string> _selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _selectedKeysNext = new HashSet<string>(StringComparer.Ordinal);

        // 进度条与它旁边的数字上一次的值（按 Key），新值不同就从旧值走过去
        private readonly Dictionary<string, float> _lastProgress = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lastProgressNumber = new Dictionary<string, int>(StringComparer.Ordinal);

        // 在途远征的进度条：跟着一秒一次的走表一起走
        private readonly List<PetNestHudBarSmoother> _liveBars = new List<PetNestHudBarSmoother>();
        private readonly List<Func<float>> _liveBarSources = new List<Func<float>>();
        private float _nextBarTick;

        private RectTransform _tabIndicator;
        private PetNestToast _toast;
        private bool _revealWasOpen;

        #endregion

        #region 接线（PetNestUI.Build / BuildTabs / RefreshHeader / Refresh / Update 各调一处）

        private void InitMotion(Transform surface)
        {
            _toast = PetNestToast.Create(surface, new Vector2(0f, -326f));
            PetNestPageKeys.Attach(_canvas.gameObject, StepPage, IsCoveredByChildWindow);
        }

        /// <summary>页签下的指示条（审查 A-2）：Accent 细条（强调色；AccentFill 留给每屏唯一的主操作填充，§4.14），切页时滑过去。</summary>
        private void BuildTabIndicator(Transform parent)
        {
            GameObject bar = ZombieModeUIHelper.CreateRect("TabIndicator", parent, new Vector2(0.5f, 0.5f), new Vector2(120f, 3f));
            _tabIndicator = bar.GetComponent<RectTransform>();
            _tabIndicator.anchoredPosition = new Vector2(TabX(PetNestUIPage.Nest), 243f);
            Image image = bar.AddComponent<Image>();
            image.sprite = BossRushUI.GetSolidSprite();
            image.color = BossRushUIColors.Accent;
            image.raycastTarget = false;
        }

        private static float TabX(PetNestUIPage page)
        {
            int index = Array.IndexOf(PageOrder, page);
            return -420f + Mathf.Max(0, index) * 200f;
        }

        /// <summary>选中页签的底色：只压一点 Accent，不整块填（主操作色块留给页面底栏）。</summary>
        private static Color TabColor(bool selected)
        {
            return selected
                ? Color.Lerp(BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, 0.16f)
                : BossRushUIColors.SurfaceRaised;
        }

        private void MoveTabIndicator()
        {
            if (_tabIndicator == null) return;
            _tabIndicator.gameObject.SetActive(!_showHelp);
            PetNestSlideX.Play(_tabIndicator, TabX(_page), TabSlideSeconds, !_hasRendered);
        }

        /// <summary>官方翻页键：在四个页签之间循环。「说明」页打开时先回到当前页。</summary>
        private void StepPage(int delta)
        {
            int index = Array.IndexOf(PageOrder, _page);
            if (index < 0) index = 0;
            if (!_showHelp) index = (index + delta + PageOrder.Length) % PageOrder.Length;
            SwitchPage(PageOrder[index]);
        }

        /// <summary>每帧入口（PetNestUI.Update 第一行调）：演出关闭后刷新一次、在途进度条一秒一次。</summary>
        private void TickMotion()
        {
            bool revealOpen = PetNestHatchRevealView.IsOpen || PetNestExpeditionRevealView.IsOpen;
            if (_revealWasOpen && !revealOpen)
            {
                _revealWasOpen = false;
                Refresh();
                return;
            }
            _revealWasOpen = revealOpen;

            if (_liveBars.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < _nextBarTick || BossRushUI.IsGamePaused()) return;
            _nextBarTick = now + 1f;
            for (int i = 0; i < _liveBars.Count; i++)
            {
                if (_liveBars[i] == null) continue;
                float value;
                try { value = _liveBarSources[i](); }
                catch (Exception) { continue; }
                _liveBars[i].SetTarget(Mathf.Clamp01(value));
            }
        }

        /// <summary>整页渲染开始：清掉上一次的行表与走表进度条，记下这次是不是同页重绘。</summary>
        private void BeginRender(bool samePage)
        {
            _animateSelection = samePage;
            _selectedKeysNext.Clear();
            _nestRows.Clear();
            _nestRowOrder.Clear();
            _liveBars.Clear();
            _liveBarSources.Clear();
        }

        /// <summary>整页渲染结束：选中集合换代，成功提示弹出来。</summary>
        private void EndRender()
        {
            HashSet<string> previous = _selectedKeys;
            _selectedKeys = _selectedKeysNext;
            _selectedKeysNext = previous;
            _selectedKeysNext.Clear();
            ShowPendingToast();
        }

        private void ShowPendingToast()
        {
            string text = PetNestUIPages.LastSuccessText;
            if (string.IsNullOrEmpty(text)) return;
            PetNestUIPages.LastSuccessText = null;
            if (_toast != null) _toast.Show(text);
            IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundConfirm);
        }

        #endregion

        #region 选中态

        /// <summary>
        /// 选中描边：选中 = Accent（不再借 WarningText 的黄，审查 A-9），没选中 = 默认描边。
        /// 同页重绘时「刚被选中 / 刚取消」的那一项淡过去，没变的直接落定，不重播。
        /// </summary>
        private void ApplySelectedStroke(GameObject card, string key, bool selected)
        {
            if (card == null) return;
            Transform stroke = card.transform.Find("Stroke");
            Image image = stroke != null ? stroke.GetComponent<Image>() : null;
            if (image == null) return;
            if (selected && key != null) _selectedKeysNext.Add(key);
            Color target = selected ? BossRushUIColors.Accent : BossRushUIColors.Stroke;
            bool wasSelected = key != null && _selectedKeys.Contains(key);
            if (_animateSelection && key != null && selected != wasSelected)
            {
                PetNestColorTween.Play(image, image.color, target, SelectFadeSeconds);
                return;
            }
            image.color = target;
        }

        /// <summary>巢页左栏登记一行（RenderNest 调），局部刷新据此原地改它。</summary>
        private void RegisterNestRow(string key, GameObject card)
        {
            if (string.IsNullOrEmpty(key) || card == null || _nestRows.ContainsKey(key)) return;
            _nestRows[key] = card;
            _nestRowOrder.Add(key);
        }

        #endregion

        #region 巢页局部刷新（审查 A-1）

        /// <summary>
        /// 巢页点选 / 勾选：行集合没变（同一批 Key、同一顺序）时只改行的选中态与勾选框，右栏详情单独重画并淡入。
        /// 任何一处对不上就返回 false，退回整页重建——宁可多画一次，不画错。
        /// </summary>
        private bool TrySoftRefreshNest(PetNestPageContent content, bool samePage)
        {
            bool pending = _softRefreshPending;
            _softRefreshPending = false;
            if (!pending || !samePage || content == null || content.Nest == null || _nestRowOrder.Count == 0) return false;

            PetNestNestView view = content.Nest;
            List<PetNestCardData> items = new List<PetNestCardData>(view.Rows.Count + 1);
            if (view.Slot != null && !string.IsNullOrEmpty(view.Slot.Key)) items.Add(view.Slot);
            for (int i = 0; i < view.Rows.Count; i++)
            {
                if (view.Rows[i] != null && !string.IsNullOrEmpty(view.Rows[i].Key)) items.Add(view.Rows[i]);
            }
            if (items.Count != _nestRowOrder.Count) return false;
            for (int i = 0; i < items.Count; i++)
            {
                GameObject card;
                if (!string.Equals(items[i].Key, _nestRowOrder[i], StringComparison.Ordinal)
                    || !_nestRows.TryGetValue(items[i].Key, out card) || card == null) return false;
            }

            _animateSelection = true;
            _selectedKeysNext.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                GameObject card = _nestRows[items[i].Key];
                ApplySelectedStroke(card, items[i].Key, items[i].Selected);
                Transform box = card.transform.Find("Checkbox");
                if (box != null)
                {
                    box.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(box.gameObject);
                }
                if (items[i].Checkbox) CreateCheckbox(card.transform, items[i].Selected);
            }
            HashSet<string> previous = _selectedKeys;
            _selectedKeys = _selectedKeysNext;
            _selectedKeysNext = previous;
            _selectedKeysNext.Clear();

            ReplaceNestDetail(view.Detail ?? new PetNestDetailData());
            ShowPendingToast();
            return true;
        }

        /// <summary>只重画右栏（详情或批量摘要）并淡入；左栏、容量行与滚动位置都不动。</summary>
        private void ReplaceNestDetail(PetNestDetailData detail)
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                GameObject go = _spawned[i];
                if (go != null && !IsInDetailRegion(go.transform)) continue;
                if (go != null)
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
                _spawned.RemoveAt(i);
            }
            for (int i = _liveBodies.Count - 1; i >= 0; i--)
            {
                if (_liveBodies[i] != null && !IsInDetailRegion(_liveBodies[i].transform)) continue;
                _liveBodies.RemoveAt(i);
                _liveBodySources.RemoveAt(i);
            }

            RenderDetail(detail);
            Canvas.ForceUpdateCanvases();
            ScrollRect scroll = _nestDetailRoot.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            Transform panel = _nestDetailRoot.parent != null ? _nestDetailRoot.parent.parent : null;
            if (panel != null) PetNestFadeIn.Play(panel.gameObject, 0.35f, DetailFadeSeconds);
        }

        private bool IsInDetailRegion(Transform t)
        {
            return t != null && ((_nestDetailRoot != null && t.IsChildOf(_nestDetailRoot))
                || (_nestFooterRoot != null && t.IsChildOf(_nestFooterRoot)));
        }

        #endregion

        #region 进度条与数字（审查 A-3）

        /// <summary>
        /// 进度条建好后登记：有上一次的值就从旧值平滑走到新值（复用随从 HUD 的 PetNestHudBarSmoother），
        /// 数字从旧值滚到新值；在途远征的进度条交给走表。
        /// </summary>
        private void RegisterProgressBar(Image fill, TMP_Text number, PetNestCardData data)
        {
            if (fill == null || data == null) return;
            float target = Mathf.Clamp01(data.Progress);
            PetNestHudBarSmoother smoother = null;
            if (!string.IsNullOrEmpty(data.Key))
            {
                float previous;
                if (_lastProgress.TryGetValue(data.Key, out previous) && !Mathf.Approximately(previous, target))
                {
                    fill.fillAmount = previous;
                    smoother = fill.gameObject.AddComponent<PetNestHudBarSmoother>();
                    smoother.SetTarget(target);
                }
                _lastProgress[data.Key] = target;

                int value;
                string rest;
                if (number != null && TrySplitLeadingNumber(data.ProgressText, out value, out rest))
                {
                    int old;
                    if (_lastProgressNumber.TryGetValue(data.Key, out old) && old != value)
                    {
                        string suffix = rest;
                        PetNestCountUp.Play(number, old, value, delegate (int v) { return v + suffix; }, 0.35f);
                    }
                    _lastProgressNumber[data.Key] = value;
                }
            }
            if (data.LiveProgress != null)
            {
                if (smoother == null)
                {
                    // 平滑器的默认目标是满格：新挂上的必须立刻设一次，否则会先冲到满格
                    smoother = fill.gameObject.AddComponent<PetNestHudBarSmoother>();
                    smoother.SetTarget(target);
                }
                _liveBars.Add(smoother);
                _liveBarSources.Add(data.LiveProgress);
            }
        }

        /// <summary>「12 / 240」拆成 12 与「 / 240」。不是数字开头就不滚。</summary>
        private static bool TrySplitLeadingNumber(string text, out int value, out string rest)
        {
            value = 0;
            rest = null;
            if (string.IsNullOrEmpty(text)) return false;
            int end = 0;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == 0 || end > 9) return false;
            value = int.Parse(text.Substring(0, end));
            rest = text.Substring(end);
            return true;
        }

        #endregion

        #region 回调（BuildPageContent 交给各页）

        /// <summary>排序（审查 B-1）：轮换四种排序，整页重画后行错峰入场，看得出顺序变了。</summary>
        private void CycleSort()
        {
            _sortMode = (PetNestSortMode)(((int)_sortMode + 1) % 4);
            Refresh();
            PlayCardEntrance();
        }

        /// <summary>就地揭晓（审查 D-1）：演出层压在面板之上，翻完关掉时 TickMotion 自动刷新面板。</summary>
        private void ShowExpeditionResults()
        {
            try
            {
                PetNestExpeditionRevealView.PlayPending();
                _revealWasOpen = PetNestExpeditionRevealView.IsOpen;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 就地揭晓失败: " + e.Message);
            }
        }

        /// <summary>全部孵化（审查 C-2）：结果已经落档，这里只交给演出层一次揭晓。</summary>
        private void OnHatchedBatch(List<PetNestHatchResult> results)
        {
            if (results == null || results.Count == 0) return;
            if (results.Count == 1) PetNestHatchRevealView.Play(results[0]);
            else PetNestHatchRevealView.PlayBatch(results);
            _revealWasOpen = PetNestHatchRevealView.IsOpen;
        }

        #endregion
    }
}
