// ============================================================================
// BossRushProceduralSprites.cs - 运行时程序化生成的共享精灵
// ============================================================================
// 模块说明：
//   零新增美术资源的特效需要一张「中心亮、边缘淡」的圆形贴图。
//   此前只有 DragonSetBonus_Dash.CreateSimpleCircleSprite() 一份，
//   但它是 partial class ModBehaviour 的私有实例方法，静态类（新武器特效）调不到，
//   而把它改成 internal 会继续给宿主加职责（ModBehaviourPartialBudgetGuard 的文件数已顶格）。
//   因此新代码统一走本类。
//
//   既有的 DragonSetBonus_Dash 那份暂不改动：它已随龙套装/套装表现层验证过，
//   改它属于与本次任务无关的重构（AGENTS.md 第 7 节）。两份贴图各自静态缓存，
//   总计两张 64x64 RGBA，代价可忽略。
//
// 生命周期（AGENTS.md 4.12）：
//   惰性创建、静态缓存、全 Mod 共享一份；ResetStaticCaches 只清引用与销毁自建对象，
//   不触碰调用方持有的 SpriteRenderer。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>运行时程序化生成的共享精灵（零新增美术资源）</summary>
    internal static class BossRushProceduralSprites
    {
        private const int CircleTextureSize = 64;

        private static Sprite cachedCircleSprite;
        private static Texture2D cachedCircleTexture;
        private static Sprite cachedRingSprite;
        private static Texture2D cachedRingTexture;

        /// <summary>
        /// 中心亮、边缘 SmoothStep 淡出到 0 的圆形精灵。用于爆发环、碎片、护盾闪光等一次性表现。
        /// 失败返回 null，调用方按「没有特效但功能正常」处理。
        /// </summary>
        public static Sprite GetCircleSprite()
        {
            if (cachedCircleSprite != null)
            {
                return cachedCircleSprite;
            }

            try
            {
                int size = CircleTextureSize;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.name = "BossRushCircleSprite_Shared";
                tex.hideFlags = HideFlags.DontSave;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;

                Color[] pixels = new Color[size * size];
                // 圆心取像素中心 (size-1)/2，半径取 size/2（2026-09-23 审美审查 UE-22）：旧写法圆心偏半个像素，
                // 衰减是锥形再开 0.7 次幂——0 附近导数趋于无穷，放大后外缘一道清楚的硬圈（d=0.9 处还有 0.2），中心是锥尖。
                // 现在与 SkyIslandUiArt.GetRadialGlow / BossRushFxMaterials 的软圆同一条 SmoothStep：中心实、外缘收到 0 没有台阶。
                float center = (size - 1) * 0.5f;
                float radius = size * 0.5f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center;
                        float dy = y - center;
                        float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / radius);
                        float alpha = t * t * (3f - 2f * t);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply();

                // 先建 Sprite 再写缓存：反过来的话 Sprite.Create 抛异常时贴图已经进了字段，
                // 下次调用会把它覆盖掉，泄漏前一张 64x64。
                Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
                cachedCircleTexture = tex;
                cachedCircleSprite = sprite;
                // 与 RingParticleEffect 的共享贴图同款：标 DontSave，避免被资源清理顺手销毁
                cachedCircleSprite.hideFlags = HideFlags.DontSave;
                return cachedCircleSprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ProceduralSprites] 生成圆形精灵失败: " + e.Message);
                return null;
            }
        }

        /// <summary>薄边空心环，技能扩散时保留中心可见性，不把实心圆放大成一团雾。</summary>
        public static Sprite GetRingSprite()
        {
            if (cachedRingSprite != null) return cachedRingSprite;
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "BossRushRingSprite_Shared";
            texture.hideFlags = HideFlags.DontSave;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                    float band = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.82f) / 0.12f);
                    // 环带剖面过一道 SmoothStep（UE-22）：三角剖面的两条边在放大后是两道折线，收成柔边。
                    float alpha = band * band * (3f - 2f * band);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            cachedRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            cachedRingSprite.hideFlags = HideFlags.DontSave;
            cachedRingTexture = texture;
            return cachedRingSprite;
        }

        /// <summary>清理静态缓存（由 NewWeaponBootstrap 的 cleanup 路径调用）。</summary>
        public static void ResetStaticCaches()
        {
            try
            {
                if (cachedRingSprite != null) UnityEngine.Object.Destroy(cachedRingSprite);
                if (cachedRingTexture != null) UnityEngine.Object.Destroy(cachedRingTexture);
                if (cachedCircleSprite != null)
                {
                    UnityEngine.Object.Destroy(cachedCircleSprite);
                }
                if (cachedCircleTexture != null)
                {
                    UnityEngine.Object.Destroy(cachedCircleTexture);
                }
            }
            catch { /* best-effort fallback intentionally ignored */ }

            cachedRingSprite = null;
            cachedRingTexture = null;
            cachedCircleSprite = null;
            cachedCircleTexture = null;
        }
    }
}
