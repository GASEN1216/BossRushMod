// ============================================================================
// CodexPortraitCache.cs - 鸭皇图鉴立绘缓存
// ============================================================================
// 形态照 ModeG/ModeGPresentationAssetCache.cs（bundle 复用既有实例 → 每 runtime
// 至多一次 LoadFromFile → 幂等 Unload），但有一处 **刻意偏离**：
//
//   ★ 本缓存 fail-open，ModeG 那份 fail-closed。★
//   ModeG 缺包时 TryPreflight 返回 false、入口整个不呈现，因为那是"入口呈现的
//   正确性问题"——半张皮的入口页会误导玩家做出进本决策。
//   图鉴是**元内容**：立绘缺席只是好看程度下降，条目数据、进度、里程碑全都还在。
//   若照搬 fail-closed，美术资源没到位期间整个面板都打不开，等于让一张 PNG
//   卡死一个已经能跑的系统。因此这里任何失败都只返回 null，由 CodexView 走
//   三级占位链（bundle sprite → preset.GetCharacterIcon() → 名字首字 + 圆底）。
//
// 其它硬约束：
//   - 资产命名冻结：bundle 内 asset 名 = "codex_portrait_" + bossKey 全小写。
//   - 只认 bundle，不读散图，绝不用 raw 文件冒充发布 bundle（与 Mode G 展示包同源纪律）。
//   - Unload 幂等：Unload(true) 卸掉 bundle 后清空全部缓存。
//   - 官方图标回落只消费既有缓存 ObjectCache.GetCharacterPresets()，
//     不新增反射绑定、不新增 Resources 扫描策略。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>图鉴立绘缓存。bundle 优先 → 官方角色图标 → null（由 View 画首字圆底）。</summary>
    internal static class CodexPortraitCache
    {
        #region 状态

        private static AssetBundle _bundle;
        private static bool _loadAttempted;

        /// <summary>bossKey -&gt; 立绘。命中 null 也要留 key，避免每次刷新都重试 IO。</summary>
        private static readonly Dictionary<string, Sprite> _portraits =
            new Dictionary<string, Sprite>(64, StringComparer.Ordinal);

        /// <summary>bossKey -&gt; 官方角色图标。同样缓存 null。</summary>
        private static readonly Dictionary<string, Sprite> _officialIcons =
            new Dictionary<string, Sprite>(64, StringComparer.Ordinal);

        /// <summary>nameKey -&gt; preset。惰性建一次，Unload 时清。</summary>
        private static Dictionary<string, CharacterRandomPreset> _presetsByNameKey;

        #endregion

        #region 只读

        /// <summary>立绘 bundle 是否已加载（诊断与调试导出用）。</summary>
        internal static bool IsBundleLoaded
        {
            get { return _bundle != null; }
        }

        #endregion

        #region 取图

        /// <summary>取立绘。fail-open：任何失败都返回 null，由调用方走占位链。</summary>
        internal static Sprite GetPortrait(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return null;

            try
            {
                Sprite cached;
                if (_portraits.TryGetValue(bossKey, out cached))
                {
                    return cached;
                }

                Sprite sprite = LoadBundleSprite(BuildAssetName(bossKey));

                // 失败也写进字典：下次刷新直接命中 null，不再重试文件系统
                _portraits[bossKey] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 立绘取用异常 "
                    + bossKey + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 官方角色图标回落（CharacterRandomPreset.GetCharacterIcon()）。
        /// 官方在 characterIconType == none 时返回 null、在未知枚举值上会抛，
        /// 因此这里必须 try 包住，且调用方仍要准备好第三级占位。
        /// </summary>
        internal static Sprite GetOfficialIcon(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return null;

            try
            {
                Sprite cached;
                if (_officialIcons.TryGetValue(bossKey, out cached))
                {
                    return cached;
                }

                Sprite icon = null;
                CharacterRandomPreset preset = FindPreset(bossKey);
                if (preset != null)
                {
                    try
                    {
                        icon = preset.GetCharacterIcon();
                    }
                    catch (Exception)
                    {
                        // 未知 characterIconType 会抛 ArgumentOutOfRangeException：
                        // 当作"没有图标"处理，交给第三级占位
                        icon = null;
                    }
                }

                _officialIcons[bossKey] = icon;
                return icon;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 官方图标回落异常 "
                    + bossKey + ": " + e.Message);
                return null;
            }
        }

        #endregion

        #region 卸载

        /// <summary>幂等卸载。宿主销毁调用。</summary>
        internal static void Unload()
        {
            try
            {
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                }
            }
            catch (Exception)
            {
                // no-throw
            }

            _bundle = null;
            _loadAttempted = false;
            _portraits.Clear();
            _officialIcons.Clear();
            _presetsByNameKey = null;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Unload();
        }

        #endregion

        #region 私有

        /// <summary>
        /// bundle 加载。每 runtime 至多一次 LoadFromFile；缺包**不是错误**，
        /// 只是走占位链（fail-open，见文件头注释）。
        /// </summary>
        private static bool EnsureBundleLoaded()
        {
            if (_bundle != null) return true;
            if (_loadAttempted) return false;
            _loadAttempted = true;

            try
            {
                // 复用已加载实例，避免与其它代码并发加载同一个包时冲突
                AssetBundle loaded = ResourceBundleLoader.FindLoaded(CodexTuning.PortraitBundleName);
                if (loaded != null)
                {
                    _bundle = loaded;
                    return true;
                }

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return false;

                string bundlePath = System.IO.Path.Combine(
                    modPath,
                    CodexTuning.PortraitBundleRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(bundlePath))
                {
                    // fail-open：缺包不是错误，面板照常开，走占位链
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "立绘 bundle 不存在，使用占位链: " + bundlePath);
                    return false;
                }

                _bundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (_bundle == null)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix + "立绘 bundle 加载失败，使用占位链: " + bundlePath);
                    return false;
                }

                ModBehaviour.DevLog(CodexTuning.LogPrefix + "立绘 bundle 加载成功: " + bundlePath);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 立绘 bundle 加载异常: " + e.Message);
                return false;
            }
        }

        private static Sprite LoadBundleSprite(string assetName)
        {
            if (!EnsureBundleLoaded() || _bundle == null) return null;

            try
            {
                return _bundle.LoadAsset<Sprite>(assetName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] bundle 立绘加载失败 "
                    + assetName + ": " + e.Message);
                return null;
            }
        }

        /// <summary>资产名：codex_portrait_ + bossKey 全小写（命名已冻结）。</summary>
        private static string BuildAssetName(string bossKey)
        {
            return CodexTuning.PortraitAssetPrefix + bossKey.ToLowerInvariant();
        }

        /// <summary>
        /// 按 nameKey 找官方 preset。只消费既有的 ObjectCache.GetCharacterPresets()
        /// 缓存（那份缓存拿的是资产而非场景实例，过图不失效），不新增扫描策略。
        /// </summary>
        private static CharacterRandomPreset FindPreset(string nameKey)
        {
            try
            {
                if (_presetsByNameKey == null)
                {
                    _presetsByNameKey = new Dictionary<string, CharacterRandomPreset>(
                        128, StringComparer.Ordinal);

                    CharacterRandomPreset[] presets = ObjectCache.GetCharacterPresets();
                    if (presets != null)
                    {
                        for (int i = 0; i < presets.Length; i++)
                        {
                            CharacterRandomPreset preset = presets[i];
                            if (preset == null || string.IsNullOrEmpty(preset.nameKey)) continue;
                            // 同名 preset 取第一个：图标只有几种类型，重名不影响结果
                            if (_presetsByNameKey.ContainsKey(preset.nameKey)) continue;
                            _presetsByNameKey[preset.nameKey] = preset;
                        }
                    }
                }

                CharacterRandomPreset found;
                return _presetsByNameKey.TryGetValue(nameKey, out found) ? found : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
