using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>独立小包只持有图标、两张柔光纹理与短促机械音；随装备模块卸载。</summary>
    internal static class ReloadMetronomeAssets
    {
        internal const string BundlePath = "Assets/ui/reload_metronome";
        private static AssetBundle bundle;
        private static bool attempted;
        private static Sprite icon, trail, glint;
        private static AudioClip click;

        internal static Sprite Icon { get { EnsureLoaded(); return icon; } }
        internal static Sprite Trail { get { EnsureFeedback(); return trail; } }
        internal static Sprite Glint { get { EnsureFeedback(); return glint; } }
        internal static AudioClip Click { get { EnsureFeedback(); return click; } }

        private static void EnsureLoaded()
        {
            if (bundle != null || attempted) return;
            attempted = true;
            try
            {
                bundle = ResourceBundleLoader.LoadFromFile(Path.Combine(ModBehaviour.GetModPath(), BundlePath));
                if (bundle == null) throw new InvalidOperationException("resource bundle is unavailable");
                icon = bundle.LoadAsset<Sprite>("metronome_icon");
                if (icon == null) throw new InvalidOperationException("metronome_icon is missing");
            }
            catch (Exception e) { Debug.LogWarning("[ReloadMetronome] 图标资源加载失败: " + e.Message); }
        }

        private static void EnsureFeedback()
        {
            EnsureLoaded();
            if (bundle == null || trail != null) return;
            // 只有实际参与换弹、创建 HUD 时才取出反馈资源。
            trail = bundle.LoadAsset<Sprite>("metronome_trail");
            glint = bundle.LoadAsset<Sprite>("metronome_glint");
            click = bundle.LoadAsset<AudioClip>("metronome_click");
        }

        internal static void ResetStaticCaches()
        {
            icon = trail = glint = null;
            click = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            attempted = false;
        }
    }
}
