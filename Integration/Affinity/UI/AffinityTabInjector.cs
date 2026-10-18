// ============================================================================
// AffinityTabInjector.cs - 把「好感」页签挂进官方 Tab 页签栏最左一格
// ============================================================================
// 官方层级（2026-10-17 Dev 探针 OfficialViewTabsProbe 实机导出，基地 Base_SceneV2）：
//   GameplayUICanvas/Tabs                      ViewTabs + FadeGroup（页签栏随 View.ActiveView 显隐）
//     ViewButtons                              HorizontalLayoutGroup(spacing 40) + ContentSizeFitter
//       BG / line                              LayoutElement.ignoreLayout，随宽度拉伸
//       BackpackButton … NoteIndexButton       80×80；GenericButton.onPointerClick 打开对应 View，
//                                              ViewTabDisplayEntry.viewTypeName 决定哪一页亮
//     ViewArea                                 LootView / InventoryView / PlayerStatsView / … 并列
// 做法：页签栏第一次随官方 View 打开时（ManagedUIElement.onOpen），复制 NoteIndexButton
// （结构最简单、没有负重条），排到 BackpackButton 前面，换成心形图标、换掉点击事件、
// 把 viewTypeName 改成 AffinityTabView；同时在 ViewArea 下建好好感页。按名字判重，幂等。
// GameplayUICanvas 随关卡重建，新画布第一次打开页面时会再挂一次；旧的随画布一起销毁。
// 只在官方页面打开那一刻跑一次 Transform.Find，不占每帧开销。
// ============================================================================

using System;
using System.Reflection;
using Duckov.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossRush
{
    internal static class AffinityTabInjector
    {
        private const string LogPrefix = "[AffinityTab] ";
        private const string ButtonName = "BossRush_AffinityTabButton";
        private const string TemplateButtonName = "NoteIndexButton";
        private const string FirstButtonName = "BackpackButton";
        private const string ButtonsPath = "ViewButtons";
        private const string ViewAreaPath = "ViewArea";

        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;
            ManagedUIElement.onOpen += OnManagedUIElementOpen;
            _installed = true;
        }

        internal static void Uninstall()
        {
            AffinityTabOfficialSkin.ResetLookup();
            if (!_installed) return;
            ManagedUIElement.onOpen -= OnManagedUIElementOpen;
            _installed = false;
        }

        private static void OnManagedUIElementOpen(ManagedUIElement element)
        {
            if (element == null || element is AffinityTabView) return;
            View view = element as View;
            if (view == null) return;
            try
            {
                Transform parent = view.transform.parent;
                Transform grand = parent != null ? parent.parent : null;
                if (grand == null || grand.GetComponent<ViewTabs>() == null) return;
                EnsureInjected(grand);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 挂页签失败: " + e.Message);
            }
        }

        private static void EnsureInjected(Transform tabsRoot)
        {
            Transform buttons = tabsRoot.Find(ButtonsPath);
            Transform viewArea = tabsRoot.Find(ViewAreaPath);
            if (buttons == null || viewArea == null) return;
            if (buttons.Find(ButtonName) != null) return;

            Transform template = buttons.Find(TemplateButtonName);
            if (template == null || template.GetComponent<GenericButton>() == null
                || template.GetComponent<ViewTabDisplayEntry>() == null)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 官方页签模板 " + TemplateButtonName + " 不在或结构变了，跳过");
                return;
            }

            AffinityTabView view = FindOrCreateView(viewArea);
            if (view == null) return;

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, buttons, false);
            clone.name = ButtonName;
            Transform first = buttons.Find(FirstButtonName);
            clone.transform.SetSiblingIndex(first != null ? first.GetSiblingIndex() : template.GetSiblingIndex());

            // 克隆带着官方「笔记」页的持久监听：整份换成新的事件，只开好感页
            GenericButton button = clone.GetComponent<GenericButton>();
            button.onPointerClick = new UnityEvent();
            button.onPointerClick.AddListener(() => OpenView(viewArea));

            // 点亮判据按页面类型名；克隆的 Awake 已用旧名订阅，但判据在事件触发时才读这个字段
            FieldInfo typeNameField = typeof(ViewTabDisplayEntry).GetField("viewTypeName", BindingFlags.NonPublic | BindingFlags.Instance);
            if (typeNameField == null)
            {
                UnityEngine.Object.Destroy(clone);
                ModBehaviour.DevLog(LogPrefix + "[WARNING] ViewTabDisplayEntry.viewTypeName 不在了，撤回页签");
                return;
            }
            typeNameField.SetValue(clone.GetComponent<ViewTabDisplayEntry>(), typeof(AffinityTabView).Name);

            // 图标用原版白色扁平心形（与背包、角色等页签同一画法，保留模板的白色与蓝色外发光）；
            // 没加载到时退回 Mod 自带的心。新画布上再找一次，免得开局时贴图还没加载就一直退回。
            AffinityTabOfficialSkin.RetryLookupIfMissing();
            Transform icon = clone.transform.Find("Icon");
            Image iconImage = icon != null ? icon.GetComponent<Image>() : null;
            Sprite heart = AffinityTabOfficialSkin.GetOfficialHeart();
            if (heart == null) heart = AffinityUIManager.GetHeartSprite();
            if (iconImage != null && heart != null)
            {
                iconImage.sprite = heart;
                iconImage.preserveAspect = true;
            }

            LayoutRebuilder.MarkLayoutForRebuild(buttons as RectTransform);
            ModBehaviour.DevLog(LogPrefix + "好感页签已挂到 " + tabsRoot.name + "（第 " + clone.transform.GetSiblingIndex() + " 个子物体）");
        }

        private static AffinityTabView FindOrCreateView(Transform viewArea)
        {
            Transform existing = viewArea.Find(AffinityTabView.ObjectName);
            if (existing != null)
            {
                AffinityTabView found = existing.GetComponent<AffinityTabView>();
                if (found != null) return found;
            }
            return AffinityTabView.CreateUnder(viewArea);
        }

        private static void OpenView(Transform viewArea)
        {
            try
            {
                if (viewArea == null) return;
                AffinityTabView view = FindOrCreateView(viewArea);
                if (view != null) view.OpenFromTab();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 打开好感页失败: " + e.Message);
            }
        }
    }
}
