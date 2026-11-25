// ============================================================================
// BossMapBeaconFx.cs - 烽烟圈的表现：M 图烽火图标 + 显形那一刻的暗红墨烟光柱
// ============================================================================
// 图标：64×64 白色形状贴图（形状只在 alpha 里），官方地图按 POI 的 Color 染色。一道带飞白缺口的墨笔圈，
//   圈里一簇向上收尖的火苗；小图上也认得出来。贴图与精灵归本文件所有，模块销毁时销毁。
// 光柱：全部用共享 BossRushFxKit 的一次性爆发拼（不新增着色器，不需要 UniversalGBuffer）：
//   贴地一圈朱砂墨晕 → 一束沿竖直窄锥冲起的暗红墨烟（Alpha 混合，越升越散）→ 中间一道朱砂亮芯（加色、拉伸）
//   → 上飘的余烬。约 1.5 秒散尽，粒子死完自毁。远处一声战鼓（水墨音效 war_drum）。
//   同一时刻最多一道由 BossMapBeaconRuntimeModule 按 BossMapBeaconRules.CanPlayPillar 管。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class BossMapBeaconFx
    {
        private const int IconSize = 64;

        /// <summary>烽烟圈（区域）的颜色：朱砂。官方地图里填充是它的 1/10 透明度。</summary>
        internal static readonly Color AreaColor = new Color(0.78f, 0.22f, 0.18f, 1f);
        /// <summary>显形后的点标记颜色：更亮的朱砂，深色地图上一眼能看见。</summary>
        internal static readonly Color RevealColor = new Color(0.96f, 0.36f, 0.26f, 1f);

        private static Texture2D iconTexture;
        private static Sprite iconSprite;

        /// <summary>显形后的烽火图标（懒加载；建不出来时返回 null，官方退回默认图标）。</summary>
        internal static Sprite GetIcon()
        {
            if (iconSprite != null) return iconSprite;
            try
            {
                iconTexture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, true);
                iconTexture.name = "BossRush_MapBeaconIcon";
                iconTexture.hideFlags = HideFlags.HideAndDontSave;
                iconTexture.wrapMode = TextureWrapMode.Clamp;
                iconTexture.filterMode = FilterMode.Bilinear;
                Color32[] pixels = new Color32[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                {
                    for (int x = 0; x < IconSize; x++)
                    {
                        float u = (x + 0.5f) / IconSize * 2f - 1f;
                        float v = (y + 0.5f) / IconSize * 2f - 1f;
                        float alpha = Mathf.Max(RingAlpha(u, v), FlameAlpha(u, v));
                        pixels[y * IconSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                    }
                }
                iconTexture.SetPixels32(pixels);
                iconTexture.Apply(true, false);
                iconSprite = Sprite.Create(iconTexture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
                iconSprite.name = "BossRush_MapBeaconIcon";
                iconSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossMapBeacon] 烽火图标生成失败，用官方默认图标: " + e.Message);
                ResetStaticCaches();
            }
            return iconSprite;
        }

        /// <summary>墨笔圈：粗细随角度起伏，左下留一道飞白缺口。</summary>
        private static float RingAlpha(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Atan2(v, u);
            float width = 0.085f + 0.035f * Mathf.Sin(angle * 3f + 1.3f) + 0.015f * Mathf.Sin(angle * 7f);
            float band = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.8f) / width);
            band = Mathf.SmoothStep(0f, 1f, band * 1.6f);
            float gap = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, -125f));
            return band * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((gap - 10f) / 22f));
        }

        /// <summary>火苗：底圆、向上收尖，中间挖一道浅芯。</summary>
        private static float FlameAlpha(float u, float v)
        {
            float y = v + 0.2f;
            if (y > 0.82f) return 0f;
            float taper = Mathf.Clamp01(y / 0.8f);
            float squeeze = 1f + 2.4f * taper * taper;
            float d = Mathf.Sqrt(u * squeeze * u * squeeze + y * 0.85f * y * 0.85f);
            float outer = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.4f - d) / 0.05f));
            float innerY = y + 0.12f;
            float innerSqueeze = 1f + 3.2f * Mathf.Clamp01(innerY / 0.8f);
            float di = Mathf.Sqrt(u * innerSqueeze * u * innerSqueeze + innerY * innerY);
            float inner = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.2f - di) / 0.05f));
            return Mathf.Clamp01(outer - inner * 0.55f);
        }

        /// <summary>显形光柱：在 Boss 脚下放一次，约 1.5 秒散尽；附带一声战鼓。</summary>
        internal static void PlayPillar(Vector3 foot)
        {
            try
            {
                Color ink = new Color(0.30f, 0.05f, 0.04f, 0.85f);
                Color cinnabar = new Color(0.95f, 0.28f, 0.18f, 0.9f);

                BossRushFxBurst halo = BossRushFxKit.Shockwave(new Color(0.78f, 0.2f, 0.15f, 0.8f), 1.6f, 4.2f, 0.9f, true);
                BossRushFxKit.PlayBurst(foot + Vector3.up * 0.08f, halo);

                BossRushFxBurst smoke = new BossRushFxBurst();
                smoke.Shape = BossRushParticleShape.Wisp;
                smoke.Blend = BossRushFxBlend.Alpha;
                smoke.Gain = BossRushFxKit.GainSoft;
                smoke.Count = 36;
                smoke.Cone = 7f;
                smoke.Direction = Vector3.up;
                smoke.SpeedMin = 9f;
                smoke.SpeedMax = 15f;
                smoke.Drag = 1.2f;
                smoke.SizeMin = 1.3f;
                smoke.SizeMax = 2.3f;
                smoke.GrowTo = 2.1f;
                smoke.LifeMin = 1.0f;
                smoke.LifeMax = 1.5f;
                smoke.ShapeRadius = 0.6f;
                smoke.Spin = 40f;
                smoke.FadeIn = 0.12f;
                smoke.Core = ink;
                smoke.Main = new Color(0.18f, 0.03f, 0.03f, 0.7f);
                smoke.End = new Color(0.05f, 0.01f, 0.01f, 0f);
                BossRushFxKit.PlayBurst(foot, smoke);

                BossRushFxBurst core = new BossRushFxBurst();
                core.Shape = BossRushParticleShape.GlowDot;
                core.Blend = BossRushFxBlend.Additive;
                core.Gain = BossRushFxKit.GainHot;
                core.Count = 22;
                core.Cone = 2.5f;
                core.Direction = Vector3.up;
                core.SpeedMin = 16f;
                core.SpeedMax = 24f;
                core.Drag = 0.6f;
                core.Stretch = 0.12f;
                core.SizeMin = 0.35f;
                core.SizeMax = 0.6f;
                core.GrowTo = 0.6f;
                core.LifeMin = 0.5f;
                core.LifeMax = 0.9f;
                core.ShapeRadius = 0.2f;
                core.Core = new Color(1f, 0.78f, 0.55f, 1f);
                core.Main = cinnabar;
                core.End = new Color(cinnabar.r, cinnabar.g, cinnabar.b, 0f);
                BossRushFxKit.PlayBurst(foot + Vector3.up * 0.3f, core);

                BossRushFxBurst embers = BossRushFxKit.Sparks(BossRushInkColors.Ember, 18);
                embers.Upward = true;
                embers.SpeedMin = 3f;
                embers.SpeedMax = 7f;
                embers.LifeMin = 0.8f;
                embers.LifeMax = 1.4f;
                embers.Drag = 1.5f;
                embers.Gravity = -0.3f;
                embers.ShapeRadius = 0.8f;
                BossRushFxKit.PlayBurst(foot + Vector3.up * 0.5f, embers);

                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);
            }
            catch (System.Exception e)
            {
                // 纯表现层：光柱放不出来不影响地图标记本身
                ModBehaviour.DevLog("[BossMapBeacon] 显形光柱失败: " + e.Message);
            }
        }

        /// <summary>销毁图标贴图与精灵（模块销毁 / Mod 卸载）。</summary>
        internal static void ResetStaticCaches()
        {
            if (iconSprite != null) Object.Destroy(iconSprite);
            if (iconTexture != null) Object.Destroy(iconTexture);
            iconSprite = null;
            iconTexture = null;
        }
    }
}
