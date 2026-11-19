// ============================================================================
// TidegateLetterUsage.cs - 闸伯的信的「读」
// ============================================================================
// 形态照 Integration/SkyIsland/SkyIslandCompassUsage.cs。信不消耗（耐久 999，见 TidegateItems），
// 读信面板与解锁竹筏航线都在 Tidegate/TidegateStory.ReadLetter（潮闸镇运行时的 owner），这里只在玩家按下使用时转过去，
// 不在任何每帧路径上。
// ============================================================================

using System;
using System.IO;
using Duckov.NoteIndexs;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    // 正文仍由中英本地化绘制。横幅是独立插画，不走背包方形图标的压缩、裁切规则：
    // 这几张 jpg 不在 production_icons 里，正式构建也读散图，经 ModSprites.GetUnpacked 解码并由它持有、缓存。
    internal static class TidegateJournalArt
    {
        internal static Sprite Get(string id)
        {
            string name;
            switch (id)
            {
                case "Letter": name = "letter"; break;
                case "Page1": name = "page1"; break;
                case "Page2": name = "page2"; break;
                case "Page3": name = "page3"; break;
                case "Page4": name = "page4"; break;
                case "Ending": name = "ending"; break;
                default: return null;
            }
            return ModSprites.GetUnpacked(Path.Combine("Assets", "Items", "tidegate_" + name + "_art.jpg"));
        }
    }

    // 原版笔记带可滚动的图片栏，但 SetupContent 对非空图片调用 SetActive(false)。
    // 只在打开已解锁的本图笔记时取对应插画；换回别的笔记时让原版决定显隐、恢复原来的布局。
    [HarmonyPatch(typeof(NoteIndexView_Inspector), "SetupContent")]
    internal static class TidegateJournalImagePatch
    {
        private static void Postfix(Note value, Image ___image)
        {
            if (___image == null) return;
            var frame = ___image.GetComponent<TidegateJournalImageLayout>();
            if (frame != null) frame.Restore(___image);
            if (value == null || value.key == null || !value.key.StartsWith(TidegateNoteBridge.NoteKeyPrefix, StringComparison.Ordinal)
                || !NoteIndex.GetNoteUnlocked(value.key)) return;
            Sprite art = TidegateJournalArt.Get(value.key.Substring(TidegateNoteBridge.NoteKeyPrefix.Length));
            if (art == null) { ___image.gameObject.SetActive(false); return; }
            if (frame == null) frame = ___image.gameObject.AddComponent<TidegateJournalImageLayout>();
            frame.Show(___image, art);
        }
    }

    internal sealed class TidegateJournalImageLayout : MonoBehaviour
    {
        private LayoutElement layout;
        private bool applied, originalAspect;
        private float originalHeight;
        internal void Show(Image image, Sprite art)
        {
            layout = image.GetComponent<LayoutElement>();
            originalAspect = image.preserveAspect;
            if (layout != null)
            {
                originalHeight = layout.preferredHeight;
                RectTransform parent = image.transform.parent as RectTransform;
                if (parent != null) layout.preferredHeight = Mathf.Clamp(parent.rect.width / 3f, 140f, 290f);
            }
            applied = true;
            image.sprite = art;
            image.preserveAspect = true;
            image.gameObject.SetActive(true);
        }
        internal void Restore(Image image)
        {
            if (!applied) return;
            if (layout != null) layout.preferredHeight = originalHeight;
            image.preserveAspect = originalAspect;
            applied = false;
        }
    }

    /// <summary>闸伯的信使用行为：打开读信面板，不消耗。</summary>
    public class TidegateLetterUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：读信，信不会用掉。", "Use: read the letter. It isn't used up.")
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            return item != null;
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 只认主角色：别的角色（官方 AI 偶尔会试用背包里的东西）用了不开面板。
                CharacterMainControl character = user as CharacterMainControl;
                if (character != null && character != CharacterMainControl.Main) return;
                TidegateStory.ReadLetter();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[TidegateItems] 读信失败: " + e.Message);
            }
        }
    }
}
