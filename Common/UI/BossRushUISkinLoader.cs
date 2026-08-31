// ============================================================================
// BossRushUISkinLoader.cs - UI 图集换皮加载器
// ============================================================================
// 规格见 docs/制作教程/BossRushUI_图集规格.md。做的事只有一件：
// 把 AssetBundle 里的九宫格底图喂给 BossRushUISkin 的注入点，全 Mod 面板即刻换皮，
// **调用方代码零改动**（ApplyPanelSkin 每次调用都现查皮肤，不缓存 Sprite 引用）。
//
// 【fail-open，与 Mode G 的 fail-closed 相反】
//   bundle 缺失/加载失败/资源名不匹配时一律不注入，程序化圆角皮肤继续工作。
//   UI 底图缺失只是观感降级，不该挡住玩法；Mode G 那边缺资源会拒绝进入，是另一回事。
//
// 【时序】必须在**任何面板被创建之前**注入：ApplyPanelSkin 只在创建时给 Image
//   赋一次 sprite，已建好的面板不会追溯换图。因此挂在 ModBehaviour.Awake 的
//   常驻初始化阶段，而不是等某个界面第一次打开。
//
// 【九宫格 border 必须在 Unity Sprite Editor 里设好】
//   运行时 LoadImage 出来的散 PNG 没有 border 信息，拉伸会糊。所以这里只走 bundle，
//   不提供 raw PNG fallback——那会让人误以为素材生效了，实际是坏的九宫格。
// ============================================================================

using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>UI 皮肤图集加载器。全静态，每 runtime 至多 LoadFromFile 一次。</summary>
    internal static class BossRushUISkinLoader
    {
        #region 常量

        /// <summary>AssetBundle 名（Unity 侧 label 与文件名一致）。</summary>
        internal const string SkinBundleName = "bossrush_ui_skin";

        private const string BundleRelativePath = "Assets/ui/bossrush_ui_skin";

        /// <summary>面板底图资源名（48×48，border 16）。</summary>
        private const string PanelAssetName = "panel_surface";

        /// <summary>按钮底图资源名（32×32，border 10）。</summary>
        private const string ButtonAssetName = "button_normal";

        private const string LogPrefix = "[BossRushUISkin] ";

        #endregion

        #region 状态

        private static AssetBundle _bundle;
        private static bool _loadAttempted;
        private static bool _injected;

        #endregion

        /// <summary>是否已成功注入至少一张美术底图（诊断与守卫用）。</summary>
        internal static bool IsSkinInjected { get { return _injected; } }

        #region 注入

        /// <summary>
        /// 幂等注入。找不到 bundle 就什么都不做，程序化皮肤继续生效。
        /// 必须早于任何面板创建，见文件头「时序」。
        /// </summary>
        internal static void EnsureInjected()
        {
            if (_injected) return;
            if (_loadAttempted && _bundle == null) return;

            try
            {
                if (!EnsureBundleLoaded()) return;

                Sprite panel = LoadBundleSprite(PanelAssetName);
                Sprite button = LoadBundleSprite(ButtonAssetName);

                if (panel == null && button == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "bundle 里没有可用底图，保持程序化皮肤");
                    return;
                }

                // 允许只有其中一张：另一张继续用程序化，注入 null 就是「保持默认」
                if (panel != null) BossRushUISkin.InjectPanelSprite(panel);
                if (button != null) BossRushUISkin.InjectButtonSprite(button);

                _injected = true;
                ModBehaviour.DevLog(LogPrefix + "美术皮肤已注入 panel=" + (panel != null)
                    + " button=" + (button != null));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 注入异常，保持程序化皮肤: " + e.Message);
            }
        }

        #endregion

        #region bundle 加载（每 runtime 至多一次）

        private static bool EnsureBundleLoaded()
        {
            if (_bundle != null) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;

            try
            {
                // 复用已加载实例：同一个 bundle 被 LoadFromFile 两次会直接失败
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (loaded != null && loaded.name != null
                        && loaded.name.IndexOf(SkinBundleName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _bundle = loaded;
                        return true;
                    }
                }

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return false;

                string bundlePath = Path.Combine(
                    modPath, BundleRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(bundlePath))
                {
                    // 素材还没做是完全正常的状态，不喊错
                    ModBehaviour.DevLog(LogPrefix + "未找到皮肤 bundle，使用程序化皮肤: " + bundlePath);
                    return false;
                }

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 皮肤 bundle 加载失败: " + bundlePath);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 皮肤 bundle 加载异常: " + e.Message);
                return false;
            }
        }

        private static Sprite LoadBundleSprite(string assetName)
        {
            if (_bundle == null) return null;
            try
            {
                return _bundle.LoadAsset<Sprite>(assetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 资源加载失败 " + assetName + ": " + e.Message);
                return null;
            }
        }

        #endregion

        #region 清理

        /// <summary>
        /// 卸载并回退到程序化皮肤（幂等）。宿主销毁时调用。
        ///
        /// 必须先把注入点复位再 Unload(true)：否则 BossRushUISkin 里留着的是已销毁的
        /// Sprite 引用，之后任何 ApplyPanelSkin 都会给面板贴一张空图（白板）。
        /// </summary>
        internal static void Cleanup()
        {
            try
            {
                BossRushUISkin.InjectPanelSprite(null);
                BossRushUISkin.InjectButtonSprite(null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 清理 UI 皮肤失败: " + e.Message);
            }

            try
            {
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                    _bundle = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 清理 UI 皮肤失败: " + e.Message);
            }

            _loadAttempted = false;
            _injected = false;
        }

        #endregion
    }
}
