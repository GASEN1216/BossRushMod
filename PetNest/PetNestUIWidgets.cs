// ============================================================================
// PetNestUIWidgets.cs - 遗种巢各窗口共用的两个小组件
// ============================================================================
// 从 PetNestUI.cs 原样提取（tests/LargeFileBudgetGuard.py：新文件 1200 行上限），行为逐字不变：
//   - PetNestCancelKey：ESC / 手柄取消（主面板、改名、放生、孵化揭晓、远征翻牌共用，2026-09-23 审美审查 UA-21）；
//   - PetNestShinyTextShimmer：异色名字的流光（巢卡标题、孵化揭晓结果名、单只放生确认页）。
// 立绘框与关闭淡出两个静态小件仍在 PetNestUI（CreateIconFrame / FadeOutAndDestroy）。
// ============================================================================

using System;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 遗种巢窗口的 ESC / 手柄取消（审美审查 UA-21）。
    ///
    /// 订官方 <c>UIInputManager.OnCancelEarly</c> 并 <c>Use()</c> 掉这次事件：官方在没人用掉事件、又没有官方 View 时会
    /// <c>PauseMenu.Toggle()</c>；模态租约的 <c>InputManager.DisableInput</c> 只挡角色输入、挡不住 UI 取消键，
    /// 所以旧版按 ESC 是直接开暂停菜单、面板纹丝不动（同 Mode G 的 ModeGModalCancelKey）。
    ///
    /// 多个遗种巢窗口可能同时开着（主面板 + 改名 / 放生 / 孵化揭晓），官方按订阅顺序逐个回调：
    /// <paramref name="isCovered"/> 为真（上面还压着自家更高一层的窗口）就不响应、不用掉事件，留给上层那个。
    /// 订阅幂等（私有布尔）；窗口关闭时先 <see cref="Detach"/>，根销毁时 OnDestroy 兜底退订（AGENTS §4.6）。
    /// </summary>
    internal sealed class PetNestCancelKey : MonoBehaviour
    {
        private Action _onCancel;
        private Func<bool> _isCovered;
        private bool _subscribed;

        internal static PetNestCancelKey Attach(GameObject root, Action onCancel, Func<bool> isCovered)
        {
            if (root == null || onCancel == null) return null;
            PetNestCancelKey key = root.GetComponent<PetNestCancelKey>();
            if (key == null) key = root.AddComponent<PetNestCancelKey>();
            key._onCancel = onCancel;
            key._isCovered = isCovered;
            key.Subscribe();
            return key;
        }

        /// <summary>窗口关闭时调用：不再响应取消键（淡出中的画布还会活 0.12 秒）。</summary>
        internal void Detach()
        {
            _onCancel = null;
            _isCovered = null;
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            try
            {
                global::UIInputManager.OnCancelEarly += HandleCancel;
                _subscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 订阅取消键失败（只能点按钮关闭）: " + e.Message);
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            try { global::UIInputManager.OnCancelEarly -= HandleCancel; }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] [WARNING] 退订取消键失败: " + e.Message); }
        }

        private void HandleCancel(global::UIInputEventData data)
        {
            Action onCancel = _onCancel;
            if (onCancel == null || !isActiveAndEnabled) return;
            if (data != null && data.Used) return;
            try
            {
                if (_isCovered != null && _isCovered()) return;
            }
            catch (Exception)
            {
                return;
            }
            if (data != null) data.Use();
            try { onCancel(); }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] [WARNING] 取消键回调失败: " + e.Message); }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }
    }

    /// <summary>
    /// 异色名字的流光：金字上周期性扫过一道亮带（owner：异色「金灿灿的字并带点小特效」）。
    /// 挂在面板里异色卡片的标题、孵化揭晓的结果名、单只放生弹窗的名字上，随所在界面销毁；暂停时不动。
    /// 每帧只改顶点色，不重排版、不分配（基色缓存按字符数失效）。
    /// </summary>
    internal sealed class PetNestShinyTextShimmer : MonoBehaviour
    {
        private const float SweepSeconds = 1.4f;
        private const float RestSeconds = 1.6f;
        private const float BandHalfWidth = 0.18f;
        private const float MaxGlow = 0.7f;

        private TMP_Text _text;
        private Color32[][] _baseColors;
        private int _cachedCharacterCount = -1;
        private float _phase;

        internal static void Attach(TMP_Text text)
        {
            if (text == null || text.GetComponent<PetNestShinyTextShimmer>() != null) return;
            text.gameObject.AddComponent<PetNestShinyTextShimmer>();
        }

        /// <summary>文字换了（揭晓演出从滚动名换成结果名）：下一帧重新取基色。</summary>
        internal void Invalidate()
        {
            _baseColors = null;
            _cachedCharacterCount = -1;
        }

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        private void LateUpdate()
        {
            try
            {
                if (_text == null || BossRushUI.IsGamePaused()) return;
                _phase += Time.unscaledDeltaTime;
                float cycle = SweepSeconds + RestSeconds;
                if (_phase >= cycle) _phase -= cycle;

                TMP_TextInfo info = _text.textInfo;
                if (info == null) return;
                if (_baseColors == null || _cachedCharacterCount != info.characterCount)
                {
                    _text.ForceMeshUpdate();
                    info = _text.textInfo;
                    CacheBaseColors(info);
                }
                if (info.characterCount == 0 || _baseColors == null) return;

                // 亮带从左扫到右（按整行宽度归一化），扫完休息一会儿再来
                float sweep = _phase <= SweepSeconds ? _phase / SweepSeconds : -1f;
                float left = float.MaxValue, right = float.MinValue;
                for (int i = 0; i < info.characterCount; i++)
                {
                    TMP_CharacterInfo c = info.characterInfo[i];
                    if (!c.isVisible) continue;
                    if (c.bottomLeft.x < left) left = c.bottomLeft.x;
                    if (c.topRight.x > right) right = c.topRight.x;
                }
                float width = Mathf.Max(1f, right - left);
                float center = left + (sweep * (1f + BandHalfWidth * 2f) - BandHalfWidth) * width;

                for (int i = 0; i < info.characterCount; i++)
                {
                    TMP_CharacterInfo c = info.characterInfo[i];
                    if (!c.isVisible) continue;
                    int material = c.materialReferenceIndex;
                    if (material >= _baseColors.Length || material >= info.meshInfo.Length) continue;
                    Color32[] source = _baseColors[material];
                    Color32[] target = info.meshInfo[material].colors32;
                    int vertex = c.vertexIndex;
                    if (source == null || target == null || vertex + 3 >= source.Length || vertex + 3 >= target.Length) continue;
                    float x = (c.bottomLeft.x + c.topRight.x) * 0.5f;
                    float glow = sweep < 0f ? 0f
                        : Mathf.Clamp01(1f - Mathf.Abs(x - center) / (BandHalfWidth * width)) * MaxGlow;
                    for (int k = 0; k < 4; k++)
                    {
                        Color32 baseColor = source[vertex + k];
                        Color32 lit = new Color32(255, 250, 225, baseColor.a);
                        target[vertex + k] = Color32.Lerp(baseColor, lit, glow);
                    }
                }
                _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }
            catch (Exception)
            {
                // 纯表现层：出错就停在基色，不影响面板
                enabled = false;
            }
        }

        private void CacheBaseColors(TMP_TextInfo info)
        {
            _cachedCharacterCount = info.characterCount;
            _baseColors = new Color32[info.meshInfo.Length][];
            for (int i = 0; i < info.meshInfo.Length; i++)
            {
                Color32[] colors = info.meshInfo[i].colors32;
                _baseColors[i] = colors != null ? (Color32[])colors.Clone() : null;
            }
        }
    }
}
