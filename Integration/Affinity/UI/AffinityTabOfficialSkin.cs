// ============================================================================
// AffinityTabOfficialSkin.cs - 好感页借官方「笔记」页的骨架（原版画风）
// ============================================================================
// owner 2026-10-17：好感页要「符合原版画风」。官方笔记页 NoteIndexView 本身就是「左列表 + 右详情」：
//   Content（TranslucentImage 毛玻璃框 Frame_Basic_Rectangle + TrueShadow，1560×1160）
//     Text (TMP)          48 号白字页名，压在框顶上方
//     LeftLayout          600 宽：CountText（20 号「47 / 92」）+ Entries Scroll View（Viewport 圆角 16 + Mask）
//     Inspector           936 宽：Rect16 #0000001F 底；Scroll View 里是 Title / Image / Content
//   列表行 NoteIndexView_Entry：highlightIndicator / titleText / indexText / notReadIndicator。
// 圆角（ProceduralImage）、毛玻璃（TranslucentImage）、投影（TrueShadow）都在第三方程序集里，
// 编译脚本不引用它们（加引用属于全局构建改造，要 owner 同意），所以这里**复制官方现成的零件**：
// 克隆时宿主还没激活，零件上的官方脚本既不 Awake 也不 OnEnable；先读出要用的引用，
// 再立即删掉会自己改字、自己淡入淡出或订阅官方事件的脚本（NoteIndexView_Inspector、NoteIndexView_Entry、
// TextLocalizor、FadeGroup / FadeElement），只留下画面。官方笔记页不在（结构变了、场景里没有）时返回 null，
// 由 AffinityTabView 退回自绘版本。层级来源：Dev 探针 OfficialViewTabsProbe 的实机导出。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>从官方笔记页克隆出来、已经去掉官方脚本的好感页骨架。</summary>
    internal sealed class AffinityTabSkeleton
    {
        public RectTransform Frame;
        public TextMeshProUGUI PageTitle;
        public TextMeshProUGUI Count;
        public ScrollRect ListScroll;
        public RectTransform ListContent;
        public GameObject ListPlaceholder;
        public TextMeshProUGUI DetailTitle;
        public TextMeshProUGUI DetailBody;
        public RectTransform DetailHeader;
        public ScrollRect DetailScroll;

        /// <summary>停在宿主下、保持关闭的官方列表行（已去掉 NoteIndexView_Entry）；每位 NPC 克隆一份。</summary>
        public GameObject RowTemplate;
        public string RowHighlightPath;
        public string RowTitlePath;
        public string RowIndexPath;
        public string RowDotPath;
    }

    internal static class AffinityTabOfficialSkin
    {
        private const string LogPrefix = "[AffinityTab] ";

        // owner 2026-10-17「字体和 UI 整体再大一点」：在官方笔记页尺寸上整体放大约 1.3 倍。
        // 框宽 1560 → 2000；纵向撑满页签栏下方（顶边离屏顶 220 给 52 号页名留位、底边 60）。
        // 左栏 600 → 760，右栏随框宽变成 2000 - 8 - 760 - 8 - 8 = 1216（官方左右各留 8、两栏间隔 8）。
        internal const float FrameWidth = 2000f;
        internal const float FrameTop = 220f;
        internal const float FrameBottom = 60f;
        internal const float FrameInset = 8f;
        internal const float ListWidth = 760f;
        internal const float PageTitleSize = 52f;
        internal const float CountSize = 26f;
        internal const float RowHeight = 84f;
        internal const float RowNameSize = 30f;
        internal const float DetailTitleSize = 46f;
        internal const float DetailBodySize = 30f;
        internal const float DetailHeaderHeight = 144f;

        private static Sprite officialHeart;
        private static bool officialHeartSearched;

        /// <summary>
        /// 原版的白色扁平心形（resources.assets 里的 Sprite「Heart」，与页签栏其它图标同一画法）。
        /// 它不在 Resources 路径下，只能从已加载的贴图里按名字找；没加载到时返回 null，调用方退回 Mod 自带的心。
        /// </summary>
        internal static Sprite GetOfficialHeart()
        {
            if (officialHeartSearched) return officialHeart;
            officialHeartSearched = true;
            try
            {
                Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] != null && string.Equals(sprites[i].name, "Heart", StringComparison.Ordinal))
                    {
                        officialHeart = sprites[i];
                        break;
                    }
                }
                ModBehaviour.DevLog(LogPrefix + "原版心形图标 " + (officialHeart != null ? "已找到" : "没加载到，退回 Mod 自带的心"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 找原版心形图标失败: " + e.Message);
            }
            return officialHeart;
        }

        /// <summary>上次没找到时允许再找一次（每块新画布挂页签时调一次，不是每帧）。</summary>
        internal static void RetryLookupIfMissing()
        {
            if (officialHeart == null) officialHeartSearched = false;
        }

        internal static void ResetLookup()
        {
            officialHeart = null;
            officialHeartSearched = false;
        }

        /// <summary>在（尚未激活的）宿主下克隆官方笔记页骨架；任一必需零件不在就整份撤回并返回 null。</summary>
        internal static AffinityTabSkeleton TryBuild(Transform viewArea, RectTransform host)
        {
            GameObject frameObj = null;
            try
            {
                NoteIndexView notes = FindNoteIndexView(viewArea);
                if (notes == null) return Fail("官方笔记页不在 ViewArea 下", null);
                Transform templateFrame = notes.transform.Find("Content");
                NoteIndexView_Entry entryTemplate = ReadField<NoteIndexView_Entry>(notes, "entryTemplate");
                if (templateFrame == null || entryTemplate == null) return Fail("笔记页缺 Content 或 entryTemplate", null);

                frameObj = UnityEngine.Object.Instantiate(templateFrame.gameObject, host, false);
                frameObj.name = "OfficialFrame";
                frameObj.SetActive(true);
                AffinityTabSkeleton skeleton = new AffinityTabSkeleton();
                skeleton.Frame = frameObj.GetComponent<RectTransform>();

                // 先读右栏引用，再剥脚本（剥掉之后字段就读不到了）
                NoteIndexView_Inspector inspector = frameObj.GetComponentInChildren<NoteIndexView_Inspector>(true);
                if (inspector == null) return Fail("笔记页右栏结构变了", frameObj);
                FadeGroup inspectorPlaceholder = ReadField<FadeGroup>(inspector, "placeHolder");
                FadeGroup inspectorContent = ReadField<FadeGroup>(inspector, "content");
                skeleton.DetailTitle = ReadField<TextMeshProUGUI>(inspector, "textTitle");
                skeleton.DetailBody = ReadField<TextMeshProUGUI>(inspector, "textContent");
                Image inspectorImage = ReadField<Image>(inspector, "image");
                if (inspectorContent == null || skeleton.DetailTitle == null || skeleton.DetailBody == null)
                {
                    return Fail("笔记页右栏缺标题或正文", frameObj);
                }
                GameObject detailContentObj = inspectorContent.gameObject;
                GameObject detailPlaceholderObj = inspectorPlaceholder != null ? inspectorPlaceholder.gameObject : null;

                StripOfficialBehaviours(frameObj);

                // 框的几何整份重设（也顺带抹掉克隆时官方页可能正处在的淡出位移）：水平居中定宽、纵向撑满
                RectTransform frameRect = skeleton.Frame;
                frameRect.anchorMin = new Vector2(0.5f, 0f);
                frameRect.anchorMax = new Vector2(0.5f, 1f);
                frameRect.pivot = new Vector2(0.5f, 0.5f);
                frameRect.offsetMin = new Vector2(-FrameWidth * 0.5f, FrameBottom);
                frameRect.offsetMax = new Vector2(FrameWidth * 0.5f, -FrameTop);

                skeleton.PageTitle = FindDirectText(frameObj.transform, "Text (TMP)");
                if (skeleton.PageTitle != null)
                {
                    skeleton.PageTitle.fontSize = PageTitleSize;
                    RectTransform titleRect = skeleton.PageTitle.rectTransform;
                    titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, Mathf.Ceil(PageTitleSize * 1.45f + 4f));
                }
                Transform left = frameObj.transform.Find("LeftLayout");
                ResizeColumns(left, frameObj.transform.Find("Inspector") as RectTransform);
                skeleton.Count = left != null ? FindDirectText(left, "CountText") : null;
                Transform entries = left != null ? left.Find("Entries Scroll View") : null;
                skeleton.ListScroll = entries != null ? entries.GetComponent<ScrollRect>() : null;
                skeleton.ListContent = skeleton.ListScroll != null ? skeleton.ListScroll.content : null;
                if (skeleton.PageTitle == null || skeleton.ListContent == null) return Fail("笔记页列表结构变了", frameObj);
                Transform listPlaceholder = entries.Find("PlaceHolder");
                skeleton.ListPlaceholder = listPlaceholder != null ? listPlaceholder.gameObject : null;
                for (int i = skeleton.ListContent.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(skeleton.ListContent.GetChild(i).gameObject);
                }

                if (detailPlaceholderObj != null) detailPlaceholderObj.SetActive(false);
                detailContentObj.SetActive(true);
                skeleton.DetailScroll = detailContentObj.GetComponent<ScrollRect>();
                if (inspectorImage != null) inspectorImage.gameObject.SetActive(false);
                if (skeleton.Count != null) skeleton.Count.fontSize = CountSize;
                skeleton.DetailTitle.fontSize = DetailTitleSize;
                skeleton.DetailBody.fontSize = DetailBodySize;
                skeleton.DetailBody.lineSpacing = 8f;
                skeleton.DetailHeader = CreateDetailHeader(skeleton.DetailTitle);

                if (!BuildRowTemplate(entryTemplate, host, skeleton)) return Fail("笔记页列表行结构变了", frameObj);
                return skeleton;
            }
            catch (Exception e)
            {
                return Fail("克隆官方笔记页异常: " + e.Message, frameObj);
            }
        }

        /// <summary>
        /// 左栏加宽到 ListWidth、右栏吃掉剩下的宽度（官方：左栏贴左 8、右栏贴右 8，两栏各自锚在框的左右边）。
        /// 左栏是竖排布局：计数行按字高、列表滚动区吃满剩余高度，框变高时列表跟着变高。
        /// </summary>
        private static void ResizeColumns(Transform left, RectTransform inspector)
        {
            RectTransform leftRect = left as RectTransform;
            if (leftRect != null)
            {
                leftRect.sizeDelta = new Vector2(ListWidth, leftRect.sizeDelta.y);
                leftRect.anchoredPosition = new Vector2(FrameInset + ListWidth * 0.5f, leftRect.anchoredPosition.y);
                LayoutElement leftLayout = left.GetComponent<LayoutElement>();
                if (leftLayout != null) leftLayout.preferredWidth = ListWidth;
                VerticalLayoutGroup group = left.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    group.childControlHeight = true;
                    group.childForceExpandHeight = false;
                }
                for (int i = 0; i < left.childCount; i++)
                {
                    Transform child = left.GetChild(i);
                    LayoutElement element = child.GetComponent<LayoutElement>();
                    if (element == null) element = child.gameObject.AddComponent<LayoutElement>();
                    element.preferredWidth = ListWidth;
                    if (child.GetComponent<ScrollRect>() != null) element.flexibleHeight = 1f;
                    else element.preferredHeight = Mathf.Ceil(CountSize * 1.45f + 4f);
                }
            }
            if (inspector != null)
            {
                float width = FrameWidth - FrameInset * 3f - ListWidth;
                inspector.sizeDelta = new Vector2(width, inspector.sizeDelta.y);
                inspector.anchoredPosition = new Vector2(-(FrameInset + width * 0.5f), inspector.anchoredPosition.y);
            }
        }

        private static NoteIndexView FindNoteIndexView(Transform viewArea)
        {
            if (viewArea == null) return null;
            for (int i = 0; i < viewArea.childCount; i++)
            {
                NoteIndexView view = viewArea.GetChild(i).GetComponent<NoteIndexView>();
                if (view != null) return view;
            }
            return null;
        }

        /// <summary>
        /// 剥掉克隆里所有会自己动的官方脚本：改字的 TextLocalizor、笔记页逻辑、淡入淡出（先 FadeGroup 后 FadeElement）。
        /// 宿主没激活，这些脚本还没跑过任何回调；CanvasGroup 留下并拉满，免得停在淡出时的 alpha。
        /// </summary>
        private static void StripOfficialBehaviours(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            List<MonoBehaviour> later = new List<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour b = behaviours[i];
                if (b == null) continue;
                if (b is NoteIndexView_Inspector || b is NoteIndexView_Entry || b is FadeGroup
                    || string.Equals(b.GetType().Name, "TextLocalizor", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }
                else if (b is FadeElement)
                {
                    later.Add(b);
                }
            }
            for (int i = 0; i < later.Count; i++)
            {
                if (later[i] != null) UnityEngine.Object.DestroyImmediate(later[i]);
            }

            CanvasGroup[] groups = root.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].alpha = 1f;
                groups[i].interactable = true;
                groups[i].blocksRaycasts = true;
            }
        }

        /// <summary>
        /// 右栏标题下面插一块「心 / 等级 / 进度条」。官方 Inspector 的 Scroll View 内容是竖排布局，
        /// 插进去的块带 LayoutElement 占高；没有竖排布局时退成贴在标题下方。
        /// </summary>
        private static RectTransform CreateDetailHeader(TextMeshProUGUI title)
        {
            Transform parent = title.transform.parent;
            GameObject header = new GameObject("AffinityHeader", typeof(RectTransform));
            header.transform.SetParent(parent, false);
            header.transform.SetSiblingIndex(title.transform.GetSiblingIndex() + 1);
            RectTransform rect = header.GetComponent<RectTransform>();
            LayoutElement layout = header.AddComponent<LayoutElement>();
            layout.minHeight = DetailHeaderHeight;
            layout.preferredHeight = DetailHeaderHeight;
            if (parent.GetComponent<VerticalLayoutGroup>() == null)
            {
                RectTransform titleRect = title.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, titleRect.anchoredPosition.y - titleRect.rect.height - 8f);
                rect.sizeDelta = new Vector2(0f, DetailHeaderHeight);
            }
            return rect;
        }

        private static bool BuildRowTemplate(NoteIndexView_Entry entry, RectTransform host, AffinityTabSkeleton skeleton)
        {
            GameObject highlight = ReadField<GameObject>(entry, "highlightIndicator");
            TextMeshProUGUI title = ReadField<TextMeshProUGUI>(entry, "titleText");
            TextMeshProUGUI index = ReadField<TextMeshProUGUI>(entry, "indexText");
            GameObject dot = ReadField<GameObject>(entry, "notReadIndicator");
            if (title == null) return false;

            skeleton.RowHighlightPath = RelativePath(entry.transform, highlight != null ? highlight.transform : null);
            skeleton.RowTitlePath = RelativePath(entry.transform, title.transform);
            skeleton.RowIndexPath = RelativePath(entry.transform, index != null ? index.transform : null);
            skeleton.RowDotPath = RelativePath(entry.transform, dot != null ? dot.transform : null);

            // 宿主此刻没激活：克隆体不会 OnEnable（官方行在 OnEnable 里订阅笔记事件，空 note 会在事件里抛异常）
            GameObject row = UnityEngine.Object.Instantiate(entry.gameObject, host, false);
            row.name = "AffinityRowTemplate";
            StripOfficialBehaviours(row);
            row.SetActive(false);
            skeleton.RowTemplate = row;
            return true;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (target == root) return string.Empty;
            List<string> parts = new List<string>();
            Transform t = target;
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            if (t != root) return null;
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        internal static Transform FindByPath(Transform root, string path)
        {
            if (root == null || path == null) return null;
            return path.Length == 0 ? root : root.Find(path);
        }

        private static TextMeshProUGUI FindDirectText(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        }

        private static T ReadField<T>(object target, string name) where T : class
        {
            if (target == null) return null;
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field != null ? field.GetValue(target) as T : null;
        }

        private static AffinityTabSkeleton Fail(string reason, GameObject partial)
        {
            ModBehaviour.DevLog(LogPrefix + "[WARNING] " + reason + "，好感页退回自绘版本");
            if (partial != null) UnityEngine.Object.DestroyImmediate(partial);
            return null;
        }
    }
}
