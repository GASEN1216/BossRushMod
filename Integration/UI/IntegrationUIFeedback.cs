// ============================================================================
// IntegrationUIFeedback.cs - 集成层界面（许愿台 / 重铸 / 图鉴 / 成就 / 好感 / 变异词条 / 婚礼）的反馈小件
// ============================================================================
// 模块说明：
//   2026-09-23 审美审查 D 区（UD-11…UD-49）。四件事：
//   1. 高光时刻的官方 UI 事件音效（UI/confirm、UI/level_up、UI/pop、UI/sell…）。
//      共享库 BossRushUISound 只公开了悬停 / 点击两条，其余事件名在这里按同一口径走反射：
//      AudioManager.Post 返回 FMOD 类型，编译清单没有 FMOD 引用（见 RandomEventEffectsBridge 的注释）。
//      只在玩家操作或结果揭晓时调用，不在每帧路径上。
//      【待并入】主会话在 BossRushUISound 上开放「按事件名播放」后，这里改成一行转发。
//   2. 富文本颜色串：玩家可见的 <color=...> 一律由 token 预先转成 hex（AGENTS §4.14、审美口径第 2 条），
//      不再在各界面写 #00FFFF / #4DFF4D 一类霓虹字面量，也不每帧拼串。
//   3. 常驻单例面板（图鉴、成就页：建一次、之后只 SetActive 开关）的关闭淡出。
//      共享库的 BossRushUIKit.PlayCloseAndDestroy 播完会销毁根物体，只适合每次新建的界面；
//      这里是「淡出后 SetActive(false)、alpha 复位」的同口径版本（0.12 秒 SmoothStep、unscaled、暂停门）。
//   4. 标题栏右上角的「×」幽灵关闭按钮：常态透明、悬停才显出 Danger 底（审美审查 UD-32）。
// ============================================================================

using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class IntegrationUIFeedback
    {
        // ---------------- token → 富文本色串（只算一次） ----------------
        internal static readonly string SuccessHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText);
        internal static readonly string WarningHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText);
        internal static readonly string DangerHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText);
        internal static readonly string SecondaryHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary);
        internal static readonly string PrimaryHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary);
        internal static readonly string AccentHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent);
        internal static readonly string LegendaryHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.RarityLegendary);

        // ---------------- 官方 UI 事件音效 ----------------
        /// <summary>确认 / 成功提交。</summary>
        internal const string SoundConfirm = "UI/confirm";
        /// <summary>升级、稀有结果揭晓。官方结算页的升级音（ClosureView）。</summary>
        internal const string SoundLevelUp = "UI/level_up";
        /// <summary>普通结果揭晓、小提示弹出。</summary>
        internal const string SoundPop = "UI/pop";
        /// <summary>钱 / 奖励到账（官方商店卖出）。</summary>
        internal const string SoundSell = "UI/sell";
        /// <summary>任务类小完成。</summary>
        internal const string SoundMissionSmall = "UI/mission_small";

        private static MethodInfo postMethod;
        private static bool postResolved;

        /// <summary>
        /// 播一条官方 UI 事件音效。没有 AudioManager 实例、FMOD 未就绪、事件名不存在时静默跳过——
        /// 音效不是关键路径，绝不能让揭晓 / 领取流程因为它抛异常。
        /// </summary>
        internal static void PlaySound(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }
            try
            {
                if (!postResolved)
                {
                    postResolved = true;
                    postMethod = typeof(global::Duckov.AudioManager).GetMethod("Post",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                }
                if (postMethod != null)
                {
                    postMethod.Invoke(null, new object[] { eventName });
                }
            }
            catch (System.Exception)
            {
                // 音效失败静默降级
            }
        }

        // ---------------- 常驻单例面板的关闭淡出 ----------------

        /// <summary>
        /// 淡出 <paramref name="group"/> 后把 <paramref name="target"/> 置为 inactive，并把 alpha / 点击复位，
        /// 下次 SetActive(true) 就是完整的面板。开始时立刻关掉点击；输入租约由调用方在启动协程**之前**归还。
        /// 重新打开时调用方先 StopCoroutine 再 <see cref="ResetFade"/>。
        /// </summary>
        internal static IEnumerator FadeOutAndDeactivate(CanvasGroup group, GameObject target, float seconds)
        {
            if (group == null || target == null)
            {
                yield break;
            }
            float from = group.alpha;
            float duration = Mathf.Max(0.01f, seconds);
            float elapsed = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            while (elapsed < duration)
            {
                if (group == null || target == null)
                {
                    yield break;
                }
                // 暂停菜单开着时停推进：它整个盖在面板上面，不停下来玩家回来只看到面板已经没了
                if (!BossRushUI.IsGamePaused())
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                group.alpha = Mathf.Lerp(from, 0f, BossRushUI.SmoothStep(elapsed / duration));
                yield return null;
            }
            if (target != null)
            {
                target.SetActive(false);
            }
            ResetFade(group);
        }

        /// <summary>把淡出中途打断的面板恢复成完整可交互的样子。</summary>
        internal static void ResetFade(CanvasGroup group)
        {
            if (group == null)
            {
                return;
            }
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        // ---------------- 幽灵关闭按钮 ----------------

        /// <summary>
        /// 标题栏「×」：常态底色透明、字形 TextSecondary；悬停显出 Danger 底，按下再压暗。
        /// 旧写法是一块 36px 的平涂暗红方块常驻右上角（审美审查 UD-32）。
        /// 底色 α=0 时共享层不会自动挂手感（整屏透明遮罩按钮也走那条路，挂了会一划就响），这里手动挂上官方音效与回弹；
        /// 幽灵按钮没有「面」，不加投影 / 斜面。调用方建按钮时就传透明底色，别先传实色再改（实色那次已经挂上了投影）。
        /// </summary>
        internal static void StyleGhostCloseButton(Button button, TextMeshProUGUI label)
        {
            if (button == null)
            {
                return;
            }
            Color danger = BossRushUIColors.Danger;
            Color rest = new Color(danger.r, danger.g, danger.b, 0f);
            Color hover = new Color(danger.r, danger.g, danger.b, 0.85f);
            ZombieModeUIHelper.ApplyButtonColors(button, rest, hover, rest);
            ColorBlock colors = button.colors;
            colors.pressedColor = BossRushUI.GetPressedColor(hover);
            colors.selectedColor = rest;   // 点过之后不要一直停在红底
            button.colors = colors;
            BossRushButtonFeel.Attach(button);
            if (label != null)
            {
                label.color = BossRushUIColors.TextSecondary;
            }
        }

        // ---------------- 分段按钮 / 危险次级按钮（UI 共识第 4、6 节） ----------------

        /// <summary>
        /// 分段按钮（页签、「全部 / 待收集」这类互斥视图）的选中态：选中压一点 Accent 底色 + WarningText 描边 + 粗体，
        /// 未选中是次级按钮（SurfaceRaised + Stroke）。选中态不置灰；可原地反复调用（切换时只改颜色，不重建）。
        /// 口径照遗种巢 SpawnSegments；图鉴筛选与 Boss 池页签共用这一份（2026-09-24 UI 共识对照审查 A-18 / A-28）。
        /// </summary>
        internal static void StyleSegment(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }
            BossRushUIKit.StyleSecondaryButton(button);   // 幂等：保证有描边、底色回到次级、标签色按底色复位
            if (selected)
            {
                ZombieModeUIHelper.SetButtonBaseColor(button,
                    Color.Lerp(BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, 0.22f));
            }
            SetButtonStrokeColor(button, selected ? BossRushUIColors.WarningText : BossRushUIColors.Stroke);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        /// <summary>
        /// 危险但还没到最后一步的按钮（后面还有一道确认）：深底不变、DangerText 描边 + 红字，不铺实心红块。
        /// 真正执行的那颗实心 Danger 只在确认弹窗里（BossRushConfirmDialog）。
        /// </summary>
        internal static void StyleDangerSecondary(Button button)
        {
            if (button == null)
            {
                return;
            }
            BossRushUIKit.StyleSecondaryButton(button);
            SetButtonStrokeColor(button, BossRushUIColors.DangerText);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = BossRushUIColors.DangerText;
            }
        }

        private static void SetButtonStrokeColor(Button button, Color color)
        {
            Image image = button.targetGraphic as Image;
            Transform strokeTransform = image != null ? image.transform.Find("Stroke") : null;
            Image stroke = strokeTransform != null ? strokeTransform.GetComponent<Image>() : null;
            if (stroke != null)
            {
                stroke.color = color;
            }
        }
    }
}
