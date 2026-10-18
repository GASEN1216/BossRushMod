#if BOSSRUSH_DEV
// ============================================================================
// OfficialViewTabsProbe.cs - 官方页签栏层级探针（只在 Dev 构建）
// ============================================================================
// 用途：
//   为「好感度挂进 Tab 背包上方页签」摸清官方预制体层级。页签按钮、页签栏和各页面的
//   父子关系都在预制体里，反编译源看不到，只能实机导出。
//
// 行为：
//   官方任意 View 打开后约 0.4 秒（等布局与淡入跑完），把它所在的页签栏 ViewTabs、
//   页面容器、本页面前几层导出成文本；同一页面类型每次进游戏只导一次。
//   落盘 persistentDataPath/BossRushTestReports/ViewTabsProbe/<时间>_<页面类型>.txt，
//   Player.log 写一行 [VIEWTABS_PROBE] 指向文件。只读：不改任何对象、不写存档。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossRush
{
    internal static class OfficialViewTabsProbe
    {
        private const string LogTag = "[VIEWTABS_PROBE] ";
        private const float DumpDelay = 0.4f;
        private const int TabsTreeMaxDepth = 10;
        private const int ViewTreeMaxDepth = 4;
        private const int DeepViewTreeMaxDepth = 9;
        private const int MaxNodes = 4000;

        private static bool _installed;
        private static readonly HashSet<string> DumpedViewTypes = new HashSet<string>();
        private static View _pendingView;
        private static float _pendingDumpAt;

        internal static void Install()
        {
            if (_installed) return;
            ManagedUIElement.onOpen += OnManagedUIElementOpen;
            _installed = true;
        }

        internal static void Uninstall()
        {
            if (_installed)
            {
                ManagedUIElement.onOpen -= OnManagedUIElementOpen;
                _installed = false;
            }
            DumpedViewTypes.Clear();
            _pendingView = null;
        }

        private static void OnManagedUIElementOpen(ManagedUIElement element)
        {
            try
            {
                View view = element as View;
                if (view == null) return;
                if (DumpedViewTypes.Contains(view.GetType().FullName)) return;
                _pendingView = view;
                _pendingDumpAt = Time.unscaledTime + DumpDelay;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogTag + "onOpen 处理失败: " + e.Message);
            }
        }

        internal static void Tick()
        {
            if (_pendingView == null) return;
            if (Time.unscaledTime < _pendingDumpAt) return;
            View view = _pendingView;
            _pendingView = null;
            try
            {
                if (view == null) return;
                string typeName = view.GetType().FullName;
                if (!DumpedViewTypes.Add(typeName)) return;
                Dump(view, typeName);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogTag + "导出失败: " + e);
            }
        }

        private static void Dump(View view, string typeName)
        {
            StringBuilder sb = new StringBuilder(64 * 1024);
            sb.AppendLine("# ViewTabsProbe " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("view_type=" + typeName);
            sb.AppendLine("view_path=" + PathOf(view.transform));
            sb.AppendLine("active_view=" + (View.ActiveView == null ? "null" : View.ActiveView.GetType().FullName));
            sb.AppendLine("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            sb.AppendLine("screen=" + Screen.width + "x" + Screen.height);

            ViewTabs tabs = ReadField(view, typeof(View), "viewTabs") as ViewTabs;
            sb.AppendLine("view.viewTabs(field)=" + (tabs == null ? "null" : PathOf(tabs.transform)));
            Transform parent = view.transform.parent;
            Transform grand = parent != null ? parent.parent : null;
            ViewTabs grandTabs = grand != null ? grand.GetComponent<ViewTabs>() : null;
            sb.AppendLine("parent.parent.ViewTabs=" + (grandTabs == null ? "null" : PathOf(grandTabs.transform)));
            if (tabs == null) tabs = grandTabs;

            Canvas canvas = view.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Canvas root = canvas.rootCanvas;
                sb.AppendLine("root_canvas=" + PathOf(root.transform) + " renderMode=" + root.renderMode
                    + " sortingOrder=" + root.sortingOrder + " scaleFactor=" + root.scaleFactor);
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    sb.AppendLine("canvas_scaler mode=" + scaler.uiScaleMode + " ref=" + scaler.referenceResolution
                        + " match=" + scaler.matchWidthOrHeight);
                }
            }

            int budget = MaxNodes;
            if (tabs != null)
            {
                sb.AppendLine();
                sb.AppendLine("## ViewTabs 子树（views 容器只列一层，页面本体见下一节）");
                DumpTree(sb, tabs.transform, 0, TabsTreeMaxDepth, parent, ref budget);
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("## 本页面不在页签栏下");
            }

            if (parent != null)
            {
                sb.AppendLine();
                sb.AppendLine("## 页面容器的直接子物体（" + PathOf(parent) + "）");
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    View childView = child.GetComponent<View>();
                    sb.AppendLine("  [" + i + "] " + child.name + " active=" + child.gameObject.activeSelf
                        + " view=" + (childView == null ? "-" : childView.GetType().FullName));
                }
            }

            // 好感页与它借骨架的官方笔记页要看到列表行内部，导深一些
            int viewDepth = view is AffinityTabView || view is NoteIndexView ? DeepViewTreeMaxDepth : ViewTreeMaxDepth;
            sb.AppendLine();
            sb.AppendLine("## 本页面前 " + viewDepth + " 层");
            DumpTree(sb, view.transform, 0, viewDepth, null, ref budget);
            if (budget <= 0) sb.AppendLine("（节点数超过上限 " + MaxNodes + "，已截断）");

            string dir = Path.Combine(Application.persistentDataPath, "BossRushTestReports", "ViewTabsProbe");
            Directory.CreateDirectory(dir);
            string shortName = view.GetType().Name;
            string file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + shortName + ".txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Debug.Log(LogTag + typeName + " -> " + file);
            try
            {
                NotificationText.Push("[Dev] 已导出页签层级: " + shortName);
            }
            catch { }
        }

        private static void DumpTree(StringBuilder sb, Transform node, int depth, int maxDepth, Transform collapse, ref int budget)
        {
            if (node == null || budget <= 0) return;
            budget--;
            string indent = new string(' ', depth * 2);
            sb.Append(indent).Append("- ").Append(node.name)
                .Append(node.gameObject.activeSelf ? "" : " (inactive)");
            RectTransform rt = node as RectTransform;
            if (rt != null)
            {
                sb.Append(" | rect anchorMin=").Append(V2(rt.anchorMin)).Append(" anchorMax=").Append(V2(rt.anchorMax))
                    .Append(" pivot=").Append(V2(rt.pivot)).Append(" pos=").Append(V2(rt.anchoredPosition))
                    .Append(" size=").Append(V2(rt.sizeDelta)).Append(" world=").Append(V2(rt.rect.size));
            }
            sb.AppendLine();

            Component[] comps = node.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null || c is Transform) continue;
                sb.Append(indent).Append("    * ").Append(c.GetType().FullName);
                AppendComponentDetail(sb, c);
                sb.AppendLine();
                if (!(c is Behaviour) || IsUnityBuiltin(c.GetType())) continue;
                AppendSerializedFields(sb, c, indent + "        ");
            }

            if (collapse != null && node == collapse)
            {
                sb.Append(indent).AppendLine("  …（页面容器，子物体见下一节）");
                return;
            }
            if (depth >= maxDepth)
            {
                if (node.childCount > 0) sb.Append(indent).AppendLine("  …（还有 " + node.childCount + " 个子物体，超出深度）");
                return;
            }
            for (int i = 0; i < node.childCount; i++)
            {
                DumpTree(sb, node.GetChild(i), depth + 1, maxDepth, collapse, ref budget);
            }
        }

        private static void AppendComponentDetail(StringBuilder sb, Component c)
        {
            Behaviour b = c as Behaviour;
            if (b != null && !b.enabled) sb.Append(" (disabled)");
            TMP_Text tmp = c as TMP_Text;
            if (tmp != null)
            {
                sb.Append(" text=\"").Append(Clip(tmp.text)).Append("\" size=").Append(tmp.fontSize)
                    .Append(" font=").Append(tmp.font != null ? tmp.font.name : "null")
                    .Append(" color=").Append(ColorHex(tmp.color));
                return;
            }
            Text uText = c as Text;
            if (uText != null)
            {
                sb.Append(" text=\"").Append(Clip(uText.text)).Append("\"");
                return;
            }
            Image img = c as Image;
            if (img != null)
            {
                sb.Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "null")
                    .Append(" type=").Append(img.type).Append(" color=").Append(ColorHex(img.color))
                    .Append(" raycast=").Append(img.raycastTarget);
                return;
            }
            Button btn = c as Button;
            if (btn != null)
            {
                sb.Append(" interactable=").Append(btn.interactable).Append(" transition=").Append(btn.transition);
                AppendPersistentListeners(sb, btn.onClick);
                return;
            }
            LayoutGroup lg = c as LayoutGroup;
            if (lg != null)
            {
                HorizontalOrVerticalLayoutGroup hv = lg as HorizontalOrVerticalLayoutGroup;
                sb.Append(" padding=").Append(lg.padding.left).Append(',').Append(lg.padding.right).Append(',')
                    .Append(lg.padding.top).Append(',').Append(lg.padding.bottom).Append(" align=").Append(lg.childAlignment);
                if (hv != null) sb.Append(" spacing=").Append(hv.spacing).Append(" ctrlW=").Append(hv.childControlWidth)
                    .Append(" expandW=").Append(hv.childForceExpandWidth);
                return;
            }
            LayoutElement le = c as LayoutElement;
            if (le != null)
            {
                sb.Append(" min=").Append(le.minWidth).Append('x').Append(le.minHeight)
                    .Append(" pref=").Append(le.preferredWidth).Append('x').Append(le.preferredHeight)
                    .Append(" ignore=").Append(le.ignoreLayout);
                return;
            }
            CanvasGroup cg = c as CanvasGroup;
            if (cg != null)
            {
                sb.Append(" alpha=").Append(cg.alpha).Append(" interactable=").Append(cg.interactable)
                    .Append(" blocks=").Append(cg.blocksRaycasts);
            }
        }

        /// <summary>官方 / 第三方脚本的序列化字段：基本类型写值，对象引用写名字，UnityEvent 写持久监听。</summary>
        private static void AppendSerializedFields(StringBuilder sb, Component c, string indent)
        {
            try
            {
                Type t = c.GetType();
                while (t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour))
                {
                    FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo f = fields[i];
                        if (f.IsNotSerialized) continue;
                        if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), false).Length == 0) continue;
                        object value;
                        try { value = f.GetValue(c); } catch { continue; }
                        sb.Append(indent).Append(t.Name).Append('.').Append(f.Name).Append(" = ").Append(DescribeValue(value)).AppendLine();
                    }
                    t = t.BaseType;
                }
            }
            catch (Exception e)
            {
                sb.Append(indent).Append("(字段读取失败: ").Append(e.Message).AppendLine(")");
            }
        }

        private static string DescribeValue(object value)
        {
            if (value == null) return "null";
            UnityEngine.Object uo = value as UnityEngine.Object;
            if (uo != null) return value.GetType().Name + ":" + uo.name;
            if (value is UnityEngine.Object) return value.GetType().Name + ":(destroyed)";
            UnityEventBase ev = value as UnityEventBase;
            if (ev != null)
            {
                StringBuilder sb = new StringBuilder("UnityEvent");
                AppendPersistentListeners(sb, ev);
                return sb.ToString();
            }
            Type t = value.GetType();
            if (t.IsPrimitive || t.IsEnum || value is string || value is Vector2 || value is Vector3 || value is Color)
            {
                return Clip(value.ToString());
            }
            System.Collections.ICollection col = value as System.Collections.ICollection;
            if (col != null)
            {
                StringBuilder sb = new StringBuilder(t.Name + "[" + col.Count + "]");
                int n = 0;
                foreach (object item in col)
                {
                    if (n++ >= 12) { sb.Append(" …"); break; }
                    sb.Append(n == 1 ? " " : ", ").Append(DescribeValue(item));
                }
                return sb.ToString();
            }
            return t.Name;
        }

        private static void AppendPersistentListeners(StringBuilder sb, UnityEventBase ev)
        {
            if (ev == null) return;
            int count = ev.GetPersistentEventCount();
            sb.Append(" listeners=").Append(count);
            for (int i = 0; i < count; i++)
            {
                UnityEngine.Object target = ev.GetPersistentTarget(i);
                sb.Append(" [").Append(target == null ? "null" : target.GetType().Name + ":" + target.name)
                    .Append("->").Append(ev.GetPersistentMethodName(i)).Append(']');
            }
        }

        private static bool IsUnityBuiltin(Type t)
        {
            string ns = t.Namespace ?? "";
            return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }

        private static object ReadField(object obj, Type declaring, string name)
        {
            try
            {
                FieldInfo f = declaring.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return f != null ? f.GetValue(obj) : null;
            }
            catch { return null; }
        }

        private static string PathOf(Transform t)
        {
            if (t == null) return "null";
            List<string> parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string V2(Vector2 v)
        {
            return "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + ")";
        }

        private static string ColorHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        private static string Clip(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", "").Replace("\n", "\\n");
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }
    }
}
#endif
