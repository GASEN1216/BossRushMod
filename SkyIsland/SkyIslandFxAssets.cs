// ============================================================================
// SkyIslandFxAssets.cs - 天空岛头目表现层的外部资源：特效小包 skyisland_fx 与招式音效
// ============================================================================
// 从 SkyIslandImpactFx.cs 原样拆出（LargeFileBudgetGuard 1200 行上限），行为逐字不变：
//   - SkyIslandFxAssets：热浪折射 / 泥面流动材质的一次性加载与回退判据；
//   - SkyIslandBossCue / SkyIslandBossSfx：头目招式的一次性音效。
// 静态状态仍由 SkyIslandImpactFx.ResetStaticCaches 统一收。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{

    /// <summary>
    /// 天空岛运行时特效小包 skyisland_fx（作者工程 SkyIslandFxBundleBuilder 构建，约 200 KB）：热浪折射与泥面流动两个材质，
    /// 外加搜刮箱三种外观的网格（<see cref="SkyIslandLootCrateLook"/>，2026-09-30）。
    /// 独立于场景包，首次用到时 LoadFromFile 一次（匠首过热、穗镰铺泥或第一只搜刮箱建出来时，AGENTS §4.12）。包缺失、材质缺失或着色器
    /// 不受支持时对应属性返回 null，调用方退回粒子版表现。热浪还要求管线提供不透明场景色（URP 资产的 Opaque Texture，
    /// 且主相机没有关掉），否则采样到的是灰底，宁可不用。静态状态由 <see cref="SkyIslandImpactFx.ResetStaticCaches"/> 收。
    /// </summary>
    internal static class SkyIslandFxAssets
    {
        internal const string BundleRelativePath = "Assets/ui/skyisland_fx";
        internal const string HazeMaterialPath = "assets/skyisland/fx/skyislandheathaze.mat";
        internal const string MudMaterialPath = "assets/skyisland/fx/skyislandmudflow.mat";
        /// <summary>搜刮箱网格，下标即 <see cref="SkyIslandLootTier"/>（生活物资 / 航务补给 / 星工遗存）。</summary>
        internal static readonly string[] LootCrateMeshPaths =
        {
            "assets/skyisland/fx/crates/lootcrate_supply.asset",
            "assets/skyisland/fx/crates/lootcrate_voyage.asset",
            "assets/skyisland/fx/crates/lootcrate_starworks.asset"
        };
        private static AssetBundle bundle;
        private static Material haze, mud;
        private static Mesh[] lootCrates;
        private static bool attempted;

        internal static Material Mud
        {
            get { Load(); return mud; }
        }

        internal static Material Haze
        {
            get { Load(); return haze != null && SceneColorAvailable() ? haze : null; }
        }

        /// <summary>某一档搜刮箱的网格；包或网格缺失返回 null，调用方保留官方包的样子。</summary>
        internal static Mesh LootCrate(SkyIslandLootTier tier)
        {
            Load();
            int index = (int)tier;
            return lootCrates != null && index >= 0 && index < lootCrates.Length ? lootCrates[index] : null;
        }

        private static void Load()
        {
            if (attempted) return;
            attempted = true;
            try
            {
                string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[SkyIslandBoss] 特效小包缺失，热浪与泥面只用粒子版：" + path);
                    return;
                }
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    Debug.LogWarning("[SkyIslandBoss] 特效小包读取失败，热浪与泥面只用粒子版：" + path);
                    return;
                }
                haze = Usable(bundle.LoadAsset<Material>(HazeMaterialPath));
                mud = Usable(bundle.LoadAsset<Material>(MudMaterialPath));
                lootCrates = new Mesh[LootCrateMeshPaths.Length];
                for (int i = 0; i < LootCrateMeshPaths.Length; i++) lootCrates[i] = bundle.LoadAsset<Mesh>(LootCrateMeshPaths[i]);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 特效小包加载失败（只用粒子版）：" + e.Message); }
        }

        private static Material Usable(Material material)
        {
            return material != null && material.shader != null && material.shader.isSupported ? material : null;
        }

        /// <summary>
        /// 管线此刻是否真的给了不透明场景色：URP 资产 `supportsCameraOpaqueTexture`，且主相机的
        /// `UniversalAdditionalCameraData.requiresColorTexture`（相机覆盖已按资产解析）为真。反射读，不引用 URP 程序集。
        /// </summary>
        internal static bool SceneColorAvailable()
        {
            try
            {
                object asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (asset == null) return false;
                PropertyInfo supports = asset.GetType().GetProperty("supportsCameraOpaqueTexture");
                if (supports == null || !(bool)supports.GetValue(asset, null)) return false;
                Camera camera = Camera.main;
                if (camera == null) return false;
                Component data = camera.GetComponent("UniversalAdditionalCameraData");
                if (data == null) return true;
                PropertyInfo requires = data.GetType().GetProperty("requiresColorTexture");
                return requires == null || (bool)requires.GetValue(data, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void ResetStaticCaches()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            haze = null;
            mud = null;
            lootCrates = null;
            attempted = false;
        }
    }

    /// <summary>头目 / 岛主招式的五种一次性音效（文件由 tools/gen_sky_island_sfx.py 生成，随 Assets/Sounds 树部署）。</summary>
    internal enum SkyIslandBossCue
    {
        Telegraph = 0,
        Impact = 1,
        Shatter = 2,
        Phase = 3,
        Defeat = 4,
        /// <summary>失控的守钟装置敲响的那一下：复用岛上的归航钟声（同一口钟）。</summary>
        Toll = 5
    }

    /// <summary>
    /// 头目 / 岛主招式音效。正式构建不引用 FMOD：与 <see cref="SkyIslandAmbience"/> 同一套 `AudioManager.PostCustomSFX` 反射绑定。
    /// 发声体是几只挂在地图根下的空物体，挪到位置再播（官方把事件挂在物体上做 3D 定位），轮流用，
    /// 免得挪走正在响的那只。同一种音效 0.08 s 内只响一次（三圈星焰齐落只响一声），45 m 外不播。
    /// 文件缺失或绑定失败只记一次警告，招式照打。静态状态由 <see cref="SkyIslandImpactFx.ResetStaticCaches"/> 收。
    /// </summary>
    internal static class SkyIslandBossSfx
    {
        private const int EmitterCount = 6;
        private const float MinInterval = 0.08f;
        private const float AudibleRange = 45f;
        private static readonly string[] Files =
        {
            "boss_telegraph.wav", "boss_impact.wav", "boss_shatter.wav", "boss_phase.wav", "boss_defeat.wav",
            "homecoming_bell.wav"
        };
        private static readonly float[] nextAllowed = new float[Files.Length];
        private static readonly bool[] present = new bool[Files.Length];
        private static readonly GameObject[] emitters = new GameObject[EmitterCount];
        private static MethodInfo post;
        private static string directory;
        private static bool resolved, warned;
        private static int next;

        internal static void Play(Transform root, SkyIslandBossCue cue, Vector3 at)
        {
            int index = (int)cue;
            if (index < 0 || index >= Files.Length) return;
            try
            {
                float now = Time.time;
                if (now < nextAllowed[index]) return;
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || (main.transform.position - at).sqrMagnitude > AudibleRange * AudibleRange) return;
                if (!Resolve() || !present[index]) return;
                GameObject emitter = Emitter(root);
                if (emitter == null) return;
                nextAllowed[index] = now + MinInterval;
                emitter.transform.position = at + Vector3.up;
                post.Invoke(null, new object[] { Path.Combine(directory, Files[index]), emitter, false });
            }
            catch (Exception e) { Warn(e.Message); }
        }

        private static bool Resolve()
        {
            if (resolved) return post != null;
            resolved = true;
            post = typeof(Duckov.AudioManager).GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
            directory = Path.Combine(ModBehaviour.GetModPath(), "Assets", "Sounds", "SkyIsland");
            for (int i = 0; i < Files.Length; i++) present[i] = File.Exists(Path.Combine(directory, Files[i]));
            if (post == null) Warn("官方 PostCustomSFX 入口缺失");
            return post != null;
        }

        private static GameObject Emitter(Transform root)
        {
            int slot = next;
            next = (next + 1) % EmitterCount;
            GameObject emitter = emitters[slot];
            if (emitter == null)
            {
                emitter = new GameObject("SkyIslandBossSfx_" + slot);
                if (root != null) emitter.transform.SetParent(root, true);
                emitters[slot] = emitter;
            }
            return emitter.activeInHierarchy ? emitter : null;
        }

        private static void Warn(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[SkyIslandBoss] 招式音效不可用（招式照打）：" + message);
        }

        internal static void ResetStaticCaches()
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] != null) UnityEngine.Object.Destroy(emitters[i]);
                emitters[i] = null;
            }
            for (int i = 0; i < nextAllowed.Length; i++) nextAllowed[i] = 0f;
            resolved = false;
            warned = false;
            post = null;
            next = 0;
        }
    }
}
