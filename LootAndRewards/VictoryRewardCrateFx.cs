// ============================================================================
// VictoryRewardCrateFx.cs - 通关奖励箱的表现组件（染色、落地回弹、常驻灯跟随）
// ============================================================================
// 由 VictoryRewardShadowCrateController.cs 原样拆出（2026-09-23 特效审美审查 VA-16 / VA-17，
// 控制器文件超出 LargeFileBudgetGuard 的 1200 行新文件预算）。这里只放不属于虚影状态机的独立类型：
//   - VictoryRewardCrateTint：虚影与英雄外壳的 _Tint / _EmissionColor 属性块染色；
//   - VictoryRewardCrateLandingBounce：英雄外壳接住虚影触地压扁后的回弹；
//   - VictoryRewardCrateAuraFollower：英雄箱常驻灯跟随箱子，打开 / 销毁时淡出。
// 调用方是同命名空间的 VictoryRewardShadowCrateController 与 VictoryRewardCrateHeroVisual。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 通关奖励箱（虚影与英雄外壳）的金色。
    /// 官方箱子 / 角色着色器（SodaCraft/SodaLit_EdgeLight、SodaCharacter）的颜色字段是 _Tint，
    /// 没有 _Color / _BaseColor / _TintColor（UnityPy 直读属性表；实机枚举见 ArenaPrototypeSession）。
    /// 视觉模板 Mat_Bags_Red 没有 _MainTex，整只袋子的颜色就是 _Tint（官方原值是深红 0.58,0.05,0.05）；
    /// _EmissionMap 默认白图，自发光会均匀铺满整只袋子，所以只给一点点，不把它压成一块发光的金。
    /// 走 MaterialPropertyBlock：不复制材质（旧版逐个渲染器复制材质实例，每次通关泄漏一份）、不改渲染队列与混合。
    /// </summary>
    internal static class VictoryRewardCrateTint
    {
        private static readonly int TintPropertyId = Shader.PropertyToID("_Tint");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        /// <summary>虚影期：偏白的淡金（sRGB，与官方材质面板同口径）。</summary>
        internal static readonly Color PhantomTint = new Color(1f, 0.92f, 0.72f, 1f);
        /// <summary>凝实后与英雄外壳：中等饱和的暖金（H≈38°、S≈64%）。</summary>
        internal static readonly Color HeroTint = new Color(0.9f, 0.68f, 0.32f, 1f);
        /// <summary>自发光，线性原值（SetVector 写入，不经 sRGB 换算）：虚影期亮一点读作「虚」，凝实后只留一丝。</summary>
        internal static readonly Vector4 PhantomEmission = new Vector4(0.3f, 0.22f, 0.09f, 1f);
        internal static readonly Vector4 HeroEmission = new Vector4(0.06f, 0.04f, 0.012f, 1f);
        /// <summary>灯色：暖琥珀、饱和度压到中等，不把地面刷成黄色。</summary>
        internal static readonly Color AuraLightColor = new Color(1f, 0.8f, 0.5f, 1f);

        private static MaterialPropertyBlock block;

        internal static void Apply(Renderer[] renderers, Color tint, Vector4 emission)
        {
            if (renderers == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Material material = renderer.sharedMaterial;
                if (material == null)
                {
                    continue;
                }

                int colorId;
                if (material.HasProperty(TintPropertyId))
                {
                    colorId = TintPropertyId;
                }
                else if (material.HasProperty(BaseColorPropertyId))
                {
                    colorId = BaseColorPropertyId;
                }
                else if (material.HasProperty(ColorPropertyId))
                {
                    colorId = ColorPropertyId;
                }
                else
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetColor(colorId, tint);
                if (material.HasProperty(EmissionColorPropertyId))
                {
                    block.SetVector(EmissionColorPropertyId, emission);
                }
                renderer.SetPropertyBlock(block);
            }
        }
    }

    /// <summary>
    /// 英雄外壳的落地回弹：从虚影触地时的压扁（y 0.9、水平略鼓）弹回原尺寸，约 0.24 s，结束即移除自己。
    /// 只在虚影落地生成的那一只箱子上挂（回退的直接生成不挂）。
    /// </summary>
    internal sealed class VictoryRewardCrateLandingBounce : MonoBehaviour
    {
        private const float DurationSeconds = 0.24f;
        private const float StartY = 0.9f;
        private const float Overshoot = 0.04f;

        private Vector3 baseScale;
        private float elapsed;

        private void Awake()
        {
            baseScale = transform.localScale;
            ApplyScale(0f);
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / DurationSeconds);
            ApplyScale(t);
            if (t >= 1f)
            {
                Destroy(this);
            }
        }

        private void ApplyScale(float t)
        {
            float y = Mathf.Lerp(StartY, 1f, BossRushUI.EaseOut(t)) + Overshoot * Mathf.Sin(t * Mathf.PI);
            float xz = 1f + (1f - y) * 0.5f;
            transform.localScale = new Vector3(baseScale.x * xz, baseScale.y * y, baseScale.z * xz);
        }
    }

    /// <summary>
    /// 通关奖励箱常驻灯的跟随者：灯跟着箱子走（可被搬运），并挂到箱子所在的场景容器下；
    /// 箱子被打开（官方 LootView 记为已搜过）或被销毁时 0.3 s 淡出后连灯一起自毁。
    /// 只在奖励箱存在期间运行，每帧一次位置同步，打开检测 0.25 s 一次。
    /// </summary>
    internal sealed class VictoryRewardCrateAuraFollower : MonoBehaviour
    {
        private const float FadeOutSeconds = 0.3f;
        private const float PollIntervalSeconds = 0.25f;

        private InteractableLootbox lootbox;
        private Transform target;
        private Inventory inventory;
        private BossRushFxLightFade fade;
        private Vector3 offset;
        private float pollTimer;
        private bool released;

        internal void Bind(InteractableLootbox box, BossRushFxLightFade lightFade, Vector3 worldOffset)
        {
            lootbox = box;
            target = box != null ? box.transform : null;
            fade = lightFade;
            offset = worldOffset;
            pollTimer = 0f;
            released = false;
        }

        private void LateUpdate()
        {
            if (released)
            {
                return;
            }

            if (lootbox == null || target == null)
            {
                Release();
                return;
            }

            transform.position = target.position + offset;
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f)
            {
                return;
            }

            pollTimer = PollIntervalSeconds;
            try
            {
                // 生成当帧箱子才被挪进场景容器：跟过去，随子场景一起启停
                if (transform.parent != target.parent)
                {
                    transform.SetParent(target.parent, true);
                }

                // 奖励箱有自己的本地 Inventory（与箱子同一物体）；取不到时退回官方 Looted
                if (inventory == null)
                {
                    inventory = lootbox.GetComponent<Inventory>();
                }

                bool opened = inventory != null
                    ? Duckov.UI.LootView.HasInventoryEverBeenLooted(inventory)
                    : lootbox.Looted;
                if (opened)
                {
                    Release();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 通关奖励箱灯跟随失败，直接淡出: " + e.Message);
                Release();
            }
        }

        private void Release()
        {
            released = true;
            if (fade != null)
            {
                fade.FadeOut(FadeOutSeconds, true);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
