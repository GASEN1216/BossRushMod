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

        /// <summary>
        /// 中心亮、边缘线性淡出的圆形精灵。用于爆发环、碎片、护盾闪光等一次性表现。
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
                float center = size / 2f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float alpha = Mathf.Clamp01(1f - (dist / center));
                        // 0.7 次幂让边缘更锐利、中心更亮，与龙套装残影同款手感
                        alpha = Mathf.Pow(alpha, 0.7f);
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

        /// <summary>清理静态缓存（由 NewWeaponBootstrap 的 cleanup 路径调用）。</summary>
        public static void ResetStaticCaches()
        {
            try
            {
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

            cachedCircleSprite = null;
            cachedCircleTexture = null;
        }
    }
}
