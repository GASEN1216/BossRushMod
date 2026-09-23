// ============================================================================
// SkyIslandStoryPresentation_Parts.cs - 天空岛剧情面板：键盘手柄导航、基础构件与世界交互体
// ============================================================================
// 内容：SkyIslandStoryPresentation 的「键盘与手柄」「基础构件」两节，以及 SkyIslandStoryInteractable、SkyIslandFadeAway。
// 从主文件原样提取（2026-09-23，AGENTS §4.15：超过 1200 行的新文件拆到同一 partial 的新文件，行为逐字不变）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class SkyIslandStoryPresentation : IDisposable
    {
        #region 键盘与手柄

        private void Register(Button button, Color color)
        {
            buttons.Add(button);
            buttonColors.Add(color);
        }

        /// <summary>
        /// 键盘方向键的当前项。**与鼠标悬停同一个焦点色**：只把这一行 ColorBlock 的 normalColor
        /// 换成 <see cref="FocusColor"/>，由 Button 自己的 ColorTint 过渡过去——写法同
        /// <c>ZombieModeUIHelper.SetButtonBaseColor</c>（页签、拍铃靠它变色）。
        /// 不去动 EventSystem 的选中态：选项的 navigation 是 None（见 BuildChoice）。
        /// </summary>
        private void Select(int index)
        {
            if (buttons.Count == 0) return;
            index = Mathf.Clamp(index, 0, buttons.Count - 1);
            if (selected >= 0 && selected < buttons.Count) SetFocused(selected, false);
            selected = index;
            SetFocused(index, true);
        }

        private void SetFocused(int index, bool focused)
        {
            Button button = buttons[index];
            if (button == null) return;
            ColorBlock colors = button.colors;
            colors.normalColor = focused ? FocusColor(buttonColors[index]) : buttonColors[index];
            button.colors = colors;
            // 焦点指示物是行边（见 ChoiceFocusLift）：当前项 Accent，移走换回 Stroke。
            // 和行底的 ColorTint 同一个时长渐变（UE-17），不再边框「啪」地跳、底色却在慢慢变。
            Image stroke = index < buttonStrokes.Count ? buttonStrokes[index] : null;
            if (stroke != null) stroke.CrossFadeColor(focused ? BossRushUIColors.Accent : BossRushUIColors.Stroke, focusFade, true, true);
        }

        /// <summary>焦点色：鼠标悬停与键盘当前项共用这一个。系数与 WCAG 实算见 <see cref="ChoiceFocusLift"/>。</summary>
        private static Color FocusColor(Color row)
        {
            Color focus = Color.Lerp(row, Color.white, ChoiceFocusLift);
            focus.a = ChoiceFocusAlpha;
            return focus;
        }

        /// <summary>执行第 index 项。回调可能重开或关掉面板，调用方执行完必须立刻返回。</summary>
        private void Press(int index)
        {
            if (index < 0 || index >= buttons.Count) return;
            Button button = buttons[index];
            if (button == null || !button.interactable) return;
            button.onClick.Invoke();
        }

        private void SubscribeInput()
        {
            if (inputSubscribed) return;
            try
            {
                global::UIInputManager.OnNavigate += OnNavigate;
                global::UIInputManager.OnConfirm += OnConfirm;
                global::UIInputManager.OnCancel += OnCancel;
                inputSubscribed = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 剧情面板订阅官方 UI 输入失败，手柄将无法操作面板：" + e.Message);
            }
        }

        private void UnsubscribeInput()
        {
            if (!inputSubscribed) return;
            inputSubscribed = false;
            try
            {
                global::UIInputManager.OnNavigate -= OnNavigate;
                global::UIInputManager.OnConfirm -= OnConfirm;
                global::UIInputManager.OnCancel -= OnCancel;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 剧情面板退订官方 UI 输入失败：" + e.Message);
            }
        }

        /// <summary>
        /// 导航**按边沿走一步**。官方 `UIInputManager.Bind` 把 UI_Navigate 的 started / performed / canceled
        /// 三个阶段全订上了，`OnInputActionNavigate` 又不看阶段、每次新建一个事件对象（Use 去不了重）；
        /// 而 UI_Navigate 是 Value 型 Vector2（W/S 组合键），Input System 在同一次处理里先 Started 再 Performed。
        /// 于是按一下 W/S 会收到两条同向事件——逐条走一步就是一按跳两格。回到中位（|y| ≤ 0.5）才重新武装。
        /// </summary>
        private void OnNavigate(global::UIInputEventData data)
        {
            if (!Visible || data == null || buttons.Count == 0) return;
            int step = data.vector.y > 0.5f ? -1 : (data.vector.y < -0.5f ? 1 : 0);
            if (step == 0) { navigateHeld = 0; return; }
            data.Use();
            if (navigateHeld == step) return;
            navigateHeld = step;
            Select(selected < 0 ? (step > 0 ? 0 : buttons.Count - 1) : selected + step);
        }

        private void OnConfirm(global::UIInputEventData data)
        {
            if (!Visible || data == null) return;
            // 还没有当前项时，第一次确认只把焦点放到第一项：先让玩家看清自己选中了什么。
            // 默认键位下交互（F）与确认（Enter）不是同一个键，而且交互完成（InteractableBase.OnTimeOut）
            // 发生在 CA_Interact 的 Update 里、晚于同一次按键的输入派发，同帧误触发本来就不会出现；
            // 这一步留作玩家改键把两者绑到同一个键时的保险。
            if (selected < 0) Select(0);
            else Press(selected);
            data.Use();
        }

        private void OnCancel(global::UIInputEventData data)
        {
            if (!Visible) return;
            Close();
            if (data != null) data.Use();
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // 数字键直选：主流 PC 对话界面的通用写法，手不必离开键盘去找鼠标。
            int keyed = Mathf.Min(choiceCount, 9);
            for (int i = 0; i < keyed; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Press(i);
                    return;
                }
            }
            ZombieModeUIHelper.EnforceModalInputPause();
        }

        #endregion

        #region 基础构件

        private static RectTransform MakeRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string value, float size,
            Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = MakeRect(parent, "Text", Vector2.zero, new Vector2(ContentWidth, 40f))
                .gameObject.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.fontSize = size;
            text.text = KeepCountsTogether(value);
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 「名称 + 计数」不许被折行拆开（2026-09-15 第五轮 D8）：「（已读 / 0/4）」「到 / 访区域 5/12」「Brass / Scrap 0/3」「Greenear / Sheaf 2」。
        /// 分隔符（行首、换行、「· 」、全角或半角左括号、全角冒号、「: 」）之后、以计数结尾（后面紧跟行尾、换行、「 ·」或右括号）
        /// 的一小段包进 TMP 的 &lt;nobr&gt;：中文字之间 TMP 可以任意断，U+00A0 只管得住空格，所以用标签。
        /// 段长上限 24 字：最长一段加计数在正文与选项的一行里都放得下（布局属性测试按这个上限复算），TMP 不会被迫逐字断。
        /// 规则文案本身不带标签（隔离回归逐字核对的是纯文本）；F3 验收读面板文字时剥掉标签再匹配。
        /// </summary>
        private static readonly Regex CountedRun = new Regex(
            @"(^|\n|· |（|\(|：|: )([^\n·（）()：:<>]{1,24} \d+(?:/\d+)?)(?=$|\n| ·|）|\))",
            RegexOptions.CultureInvariant);

        private static string KeepCountsTogether(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf("<nobr>", StringComparison.Ordinal) >= 0) return value;
            return CountedRun.Replace(value, "$1<nobr>$2</nobr>");
        }

        /// <summary>「有几件/要几件」：前一个数小于后一个时标红（<see cref="MarkShortfalls"/>）。</summary>
        private static readonly Regex HaveNeed = new Regex(@"(\d+)/(\d+)", RegexOptions.CultureInvariant);
        private static readonly string DangerHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText);

        /// <summary>
        /// 配方 / 点灯按钮里材料不够的那个「有/要」标成 DangerText（UE-09）。只给 <see cref="WithItem"/> 标过的行用：
        /// 蛙鸣池 1/3 一类进度数不是「不够」。在 <see cref="KeepCountsTogether"/> 之后套，颜色标签落在 &lt;nobr&gt; 里面，不拆开「名称 + 计数」。
        /// 行底（SurfaceRaised a=0.78）上 DangerText 最坏 6.0:1。
        /// </summary>
        private static string MarkShortfalls(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return HaveNeed.Replace(value, delegate (Match m)
            {
                int have, need;
                if (!int.TryParse(m.Groups[1].Value, out have) || !int.TryParse(m.Groups[2].Value, out need) || have >= need)
                    return m.Value;
                return "<color=" + DangerHex + ">" + m.Value + "</color>";
            });
        }

        /// <summary>「· 名字 → 用处」两行之间的半行空：条目之间留白分组，名字与用处紧贴。</summary>
        private const string JournalEntrySpacer = "<size=45%> </size>";

        /// <summary>
        /// 手记类正文的显示排版（UE-06）。规则文本保持纯文本（隔离回归逐字核对 ■ / □），样式只加在这里：
        /// - 「■ 」开头的行（收到的信、读过的名册页、点亮的灯、拿到的纪念品）：去掉方块，标题加粗、大一档——条目标题不再和正文同款；
        /// - 「□ 」开头的行（还没收录 / 还没点亮 / 还没拿到）：保留方块，整行小一号，读作「还空着」；
        /// - 「· 名字 → 用处」（群岛之物）：名字加粗一行，用处缩进、小一号另起一行，条目之间留半行空。
        /// **颜色一律不动**：面板正文压在整屏底图上，最亮那张底图下只有正文色过得了 4.5:1（SkyIslandUiContrastGuard 的口径），
        /// 次级灰（2.8:1）、强调色（2.3:1）在最坏底图上都不够；层级靠字重、字号、缩进与留白。
        /// 不含这三种标记的正文原样返回。F3 读面板正文时按子串匹配，这里只在行首行尾加标签，不拆句子。
        /// </summary>
        private static string StyleJournalLines(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf("■ ", StringComparison.Ordinal) < 0 && value.IndexOf("□ ", StringComparison.Ordinal) < 0
                && value.IndexOf(" → ", StringComparison.Ordinal) < 0) return value;
            string[] lines = value.Split('\n');
            var text = new System.Text.StringBuilder(value.Length + lines.Length * 24);
            bool previousEntry = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (i > 0) text.Append('\n');
                int arrow = line.StartsWith("· ", StringComparison.Ordinal) ? line.IndexOf(" → ", StringComparison.Ordinal) : -1;
                if (arrow > 2)
                {
                    if (previousEntry) text.Append(JournalEntrySpacer).Append('\n');
                    text.Append("<b>").Append(line, 2, arrow - 2).Append("</b>\n<indent=1em><size=92%>")
                        .Append(line, arrow + 3, line.Length - arrow - 3).Append("</size></indent>");
                    previousEntry = true;
                    continue;
                }
                previousEntry = false;
                if (line.StartsWith("■ ", StringComparison.Ordinal))
                    text.Append("<b><size=108%>").Append(line, 2, line.Length - 2).Append("</size></b>");
                else if (line.StartsWith("□ ", StringComparison.Ordinal))
                    text.Append("<size=92%>").Append(line).Append("</size>");
                else
                    text.Append(line);
            }
            return text.ToString();
        }

        #endregion
    }

    public sealed class SkyIslandStoryInteractable : BossRushBuildingInteractableBase
    {
        private string label;
        private Action interact;
        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Story_" + name;
                LocalizationHelper.InjectLocalization(key, label ?? L10n.T("群岛记事", "Archipelago record"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIsland]"; } }
        protected override bool IsBuildingInteractable() { return interact != null; }
        internal void Bind(string title, Action action) { label = title; interact = action; }
        /// <summary>切了语言：换交互名与头顶的字，回调不动（标题由 owner 按当前语言重取）。</summary>
        internal void Relabel(string title)
        {
            label = title;
            Transform sign = transform.Find("Label");
            TextMeshPro text = sign != null ? sign.GetComponent<TextMeshPro>() : null;
            if (text != null) text.text = title;
        }
        protected override void OnInteractCompleted() { if (interact != null) interact(); }
        internal static GameObject Create(Transform parent, Vector3 position, string name, string title, Action action)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = position;
            go.layer = LayerMask.NameToLayer("Interactable");
            BoxCollider trigger = go.AddComponent<BoxCollider>(); trigger.isTrigger = true;
            trigger.center = Vector3.up; trigger.size = new Vector3(3, 2, 3);
            go.AddComponent<SkyIslandStoryInteractable>().Bind(title, action);
            GameObject sign = new GameObject("Label", typeof(TextMeshPro));
            sign.transform.SetParent(go.transform, false); sign.transform.localPosition = Vector3.up * 2.5f;
            sign.transform.rotation = Quaternion.Euler(60, 0, 0);
            TextMeshPro text = sign.GetComponent<TextMeshPro>(); text.font = ZombieModeUIHelper.GetGameFont();
            text.text = title; text.fontSize = 3; text.alignment = TextAlignmentOptions.Center;
            // 纪念物上方的字不再是远远就亮着的黄字：点位上的光已经把「那里有东西」说清楚了，
            // 字只在走近时浮现，用正文色而不是警示色（它不是警告）。
            text.color = BossRushUIColors.TextPrimary;
            text.rectTransform.sizeDelta = new Vector2(18, 5);
            // 压在砂岩与云海高光上的世界字要有描边托住（UE-14），共享材质按字体一份。
            Material outlined = BossRushUIKit.GetOutlinedFontMaterial(text.font);
            if (outlined != null) text.fontSharedMaterial = outlined;
            SkyIslandProximityLabel.Attach(sign, 6f, 11f);
            return go;
        }

        /// <summary>
        /// 信鸽飞走（UE-08）：交互体与触发器本帧摘掉（面板回执已经写了「信鸽抖抖翅膀飞过云海」），
        /// 光和字 0.8 秒里往上 2.5 m、背着玩家往外 2 m 飘走并淡没，然后销毁。旧写法当帧 Destroy，光和字凭空消失。
        /// </summary>
        internal static void FlyAway(GameObject pigeon)
        {
            if (pigeon == null) return;
            SkyIslandStoryInteractable interactable = pigeon.GetComponent<SkyIslandStoryInteractable>();
            if (interactable != null) Destroy(interactable);
            BoxCollider trigger = pigeon.GetComponent<BoxCollider>();
            if (trigger != null) Destroy(trigger);
            pigeon.name = "SkyIslandPigeonLeaving";
            Vector3 outward = Vector3.forward;
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null)
            {
                Vector3 away = pigeon.transform.position - main.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f) outward = away.normalized;
            }
            SkyIslandFadeAway.Begin(pigeon, 0.8f, Vector3.up * 2.5f + outward * 2f);
        }
    }

    /// <summary>
    /// 世界里一组表现物（点光、贴地光斑、浮空字）的退场（2026-09-23 审美审查 UE-05 / UE-08）：
    /// 在给定秒数里光强与透明度 SmoothStep 落到 0，可选沿一段位移 EaseOut 飘走（信鸽），完了销毁根物体。
    /// 旧写法全是当帧 Destroy：采完一处，光斑和 6 m 点光一起凭空熄灭；信鸽「飞走」其实是光和字当帧没了。
    ///
    /// 调用方先摘掉交互体与触发器（交互在本帧就结束），这里只管「看起来」怎么走。走游戏时间：暂停与模态面板时停住。
    /// 走近才浮现的字会每帧改写透明度，接手前先摘掉它（<see cref="SkyIslandProximityLabel"/>）。
    /// </summary>
    internal sealed class SkyIslandFadeAway : MonoBehaviour
    {
        private Light[] lights;
        private float[] lightStart;
        private SpriteRenderer[] sprites;
        private float[] spriteStart;
        private TMP_Text[] texts;
        private float[] textStart;
        private Vector3 origin, drift;
        private float duration, age;

        internal static void Begin(GameObject root, float seconds, Vector3 drift)
        {
            if (root == null) return;
            if (seconds <= 0f || !root.activeInHierarchy)
            {
                Destroy(root);
                return;
            }
            foreach (SkyIslandProximityLabel label in root.GetComponentsInChildren<SkyIslandProximityLabel>(true))
                Destroy(label);
            SkyIslandFadeAway fade = root.GetComponent<SkyIslandFadeAway>();
            if (fade == null) fade = root.AddComponent<SkyIslandFadeAway>();
            fade.lights = root.GetComponentsInChildren<Light>(false);
            fade.lightStart = new float[fade.lights.Length];
            for (int i = 0; i < fade.lights.Length; i++) fade.lightStart[i] = fade.lights[i].intensity;
            fade.sprites = root.GetComponentsInChildren<SpriteRenderer>(false);
            fade.spriteStart = new float[fade.sprites.Length];
            for (int i = 0; i < fade.sprites.Length; i++) fade.spriteStart[i] = fade.sprites[i].color.a;
            fade.texts = root.GetComponentsInChildren<TMP_Text>(false);
            fade.textStart = new float[fade.texts.Length];
            for (int i = 0; i < fade.texts.Length; i++) fade.textStart[i] = fade.texts[i].alpha;
            fade.origin = root.transform.position;
            fade.drift = drift;
            fade.duration = seconds;
            fade.age = 0f;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            float keep = 1f - BossRushUI.SmoothStep(t);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = lightStart[i] * keep;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color color = sprites[i].color;
                color.a = spriteStart[i] * keep;
                sprites[i].color = color;
            }
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != null) texts[i].alpha = textStart[i] * keep;
            if (drift.sqrMagnitude > 0f) transform.position = origin + drift * BossRushUI.EaseOut(t);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
