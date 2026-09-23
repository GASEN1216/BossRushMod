// ============================================================================
// BossRushFxMaterials.cs - 全 Mod 共享的程序化粒子 / 线 / 面片材质工厂
// ============================================================================
// 模块说明：
//   2026-09-23 特效审美审查（VB-02 / VB-27 / VA-01 / VA-34）查实：各特效各写一串 Shader.Find 回退，
//   首选的 `Legacy Shaders/Particles/Additive`、`Particles/Additive`、`Particles/Alpha Blended`、
//   `Mobile/Particles/*` 在游戏里**都找不到**（UnityPy 直读本机 resources.assets），于是：
//     - 设计成「加色柔光」的线与面片实际落到 `Sprites/Default`，普通半透明，发不了光；
//     - 粒子若先撞上龙王包里带的内置 `Particles/Standard Unlit`，没开 _ALPHABLEND_ON 时 alpha 恒为 1，
//       每颗粒子是一块加色方片，还写深度；
//     - 共享材质把 Legacy Alpha Blended 的 `_TintColor` 设成白（默认 0.5 灰），片元是 2×顶点色×TintColor，
//       颜色与 alpha 都翻倍，灰烟变成发光白团。
//
//   这里只保留游戏里确认存在的两条路：
//     1. `Universal Render Pipeline/Particles/Unlit`（resources.assets，保留了 _SURFACE_TYPE_TRANSPARENT 变体）：
//        透明面（_Surface=1），按 Alpha / Additive 设混合，关 ZWrite。
//     2. 兜底 `Legacy Shaders/Particles/Alpha Blended`，`_TintColor` = 0.5 灰（中性，1×）——加色也只能退成半透明。
//   两条都找不到时返回 null，调用方不画（owner 口径：资源不对就硬失败，不做降级的「看起来差不多」）。
//
//   颜色一律走粒子 / 顶点色（ParticleSystem.startColor、LineRenderer.colorGradient），材质本身是白。
//   要给某个渲染器整体调色，用 MaterialPropertyBlock 写 `_BaseColor`（URP）或 `_TintColor`（Legacy，存半值）；
//   **不要写 `_Color`**：URP 粒子着色器留着一个废弃的隐藏 `_Color`，HasProperty 为真、写进去却不上色。
//   更不要改 Get() 返回的共享材质本身——那会把全 Mod 用同一份材质的特效一起染色。
//   不同贴图各缓存一份材质（按贴图实例区分），同一贴图 + 同一混合只有一份。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    internal enum BossRushFxBlend
    {
        /// <summary>普通半透明：烟、雾、面片、预警填充。</summary>
        Alpha = 0,
        /// <summary>加色：火花、光点、能量线、闪光核心。只有 URP 粒子着色器可用时才真的是加色。</summary>
        Additive = 1
    }

    internal static class BossRushFxMaterials
    {
        private const string UrpParticleShader = "Universal Render Pipeline/Particles/Unlit";
        private const string LegacyAlphaShader = "Legacy Shaders/Particles/Alpha Blended";

        private static readonly Dictionary<long, Material> materialCache = new Dictionary<long, Material>();
        private static Texture2D softCircleTexture;
        private static Shader urpShader;
        private static Shader legacyShader;
        private static bool shadersResolved;

        /// <summary>URP 粒子着色器是否可用。为假时 <see cref="BossRushFxBlend.Additive"/> 会退成半透明。</summary>
        internal static bool SupportsAdditive
        {
            get
            {
                ResolveShaders();
                return urpShader != null;
            }
        }

        /// <summary>
        /// 取一份共享材质。<paramref name="texture"/> 为 null 时用 <see cref="GetSoftCircleTexture"/>。
        /// 返回的材质是共享的，**不要**改它的属性；要调色改粒子的 startColor / 线的 colorGradient。
        /// </summary>
        internal static Material Get(BossRushFxBlend blend, Texture texture = null)
        {
            if (texture == null)
            {
                texture = GetSoftCircleTexture();
            }
            long key = ((long)(texture != null ? texture.GetInstanceID() : 0) << 2) | (long)blend;
            Material cached;
            if (materialCache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            ResolveShaders();
            Material material = null;
            if (urpShader != null)
            {
                material = new Material(urpShader);
                material.name = "BossRushFx_" + blend + "_URP";
                bool additive = blend == BossRushFxBlend.Additive;
                SetFloatIfPresent(material, "_Surface", 1f);                 // Transparent
                SetFloatIfPresent(material, "_Blend", additive ? 2f : 0f);   // 0 Alpha / 2 Additive
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                SetFloatIfPresent(material, "_ZWrite", 0f);
                SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);   // 面片两面都看得见
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
                }
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", Color.white);
                }
                material.mainTexture = texture;
            }
            else if (legacyShader != null)
            {
                material = new Material(legacyShader);
                material.name = "BossRushFx_" + blend + "_Legacy";
                // Legacy Alpha Blended 的片元是 2 × 顶点色 × _TintColor × 贴图：着色器里的值要恰好是 0.5 才是 1×。
                // 用 SetVector 不用 SetColor：Linear 色彩空间下 SetColor 会把 RGB 当 sRGB 换成线性值（0.5 → 0.214），
                // 兜底路径会整体暗一半多（2026-09-23 特效修复代理回报）。
                if (material.HasProperty("_TintColor"))
                {
                    material.SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                }
                material.mainTexture = texture;
            }

            if (material == null)
            {
                return null;
            }
            material.renderQueue = (int)RenderQueue.Transparent;
            material.hideFlags = HideFlags.HideAndDontSave;
            materialCache[key] = material;
            return material;
        }

        /// <summary>
        /// 64×64 软圆：中心不透明，按 SmoothStep 羽化到边缘 0。白色，颜色走顶点色。
        /// 全 Mod 共用这一张（审查 VA-34：此前四份「圆形渐变」各写一份，衰减曲线各不相同）。
        /// </summary>
        internal static Texture2D GetSoftCircleTexture()
        {
            if (softCircleTexture != null)
            {
                return softCircleTexture;
            }
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "BossRushFx_SoftCircle";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 1f - BossRushUI.SmoothStep(r);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            softCircleTexture = texture;
            return softCircleTexture;
        }

        private static void ResolveShaders()
        {
            if (shadersResolved)
            {
                return;
            }
            shadersResolved = true;
            urpShader = Shader.Find(UrpParticleShader);
            legacyShader = Shader.Find(LegacyAlphaShader);
            if (urpShader == null && legacyShader == null)
            {
                Debug.LogWarning("[BossRushFxMaterials] 找不到 URP 粒子着色器与 Legacy Alpha Blended，程序化特效不绘制");
            }
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        /// <summary>销毁程序化材质与贴图（HideAndDontSave，切场景不会自动回收）。经 BossRushUI.ResetStaticCaches 调用。</summary>
        internal static void ResetStaticCaches()
        {
            foreach (KeyValuePair<long, Material> pair in materialCache)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value);
                }
            }
            materialCache.Clear();
            if (softCircleTexture != null)
            {
                Object.Destroy(softCircleTexture);
                softCircleTexture = null;
            }
            urpShader = null;
            legacyShader = null;
            shadersResolved = false;
        }
    }
}
