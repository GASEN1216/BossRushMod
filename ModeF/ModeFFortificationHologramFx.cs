// ============================================================================
// ModeFFortificationHologramFx.cs - Mode F / 丧尸模式工事的放置预览与维修高亮（半透明全息）
// ============================================================================
// 2026-09-23 特效审美审查 VA-18 / VA-19 / VA-20：
//   - 放置预览首选 Unlit/Color（游戏里找不到；即使找到也不做混合），写的 0.4 alpha 不生效，
//     放置时是一块纯绿 (0,1,0) / 纯红 (1,0,0) 的实心剪影；退到 URP/Unlit 时只认 _BaseColor，可能是纯白。
//   - 维修高亮是 1.04 倍的白色 Unlit 复制体，把整座工事盖成一块白剪影，出现 / 消失都是 SetActive 硬切。
//   - 兜底方块用属性块只写 _Color，URP 默认材质只认 _BaseColor，配好的颜色不生效。
// 做法：
//   - 全息材质 = 共享特效材质工厂（BossRushFxMaterials：URP Particles/Unlit 透明、双面、不写深度，
//     找不到退 Legacy Alpha Blended）的纯白贴图版复制一份。颜色写材质自己的 _BaseColor
//     （Legacy 写线性原值的 0.5 倍 _TintColor）。两种着色器都再乘一次顶点色：
//     网格没有顶点色时 Unity 按白色 (1,1,1,1) 补这一路，颜色就是 _BaseColor；带顶点色的官方网格
//     会被顶点色再压一层（PLAUSIBLE，需实机）。
//   - 预览：可放置 SuccessText、不可放置 DangerText，alpha 在 0.26–0.36 之间 1.2 Hz SmoothStep 呼吸。
//   - 高亮：暖白 (1,0.92,0.7) α0.22 的全息罩，绕各部件包围盒中心外扩 1.5%，0.15 s SmoothStep 淡入淡出；
//     每个高亮用自己的属性块写 alpha，不动共享材质；淡出完关掉根物体，Update 随之停。
// 生命周期：两份材质仍由 ModBehaviour 原来的字段持有（每局各一份，与旧版相同）；
//   两个组件只在预览 / 高亮存在期间运行，逐帧 O(渲染器数)。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class ModeFFortificationHologramFx
    {
        internal static readonly Color PlaceableColor = new Color(
            BossRushUIColors.SuccessText.r, BossRushUIColors.SuccessText.g, BossRushUIColors.SuccessText.b, 0.32f);
        internal static readonly Color BlockedColor = new Color(
            BossRushUIColors.DangerText.r, BossRushUIColors.DangerText.g, BossRushUIColors.DangerText.b, 0.32f);
        internal static readonly Color RepairColor = new Color(1f, 0.92f, 0.7f, 0.22f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private static MaterialPropertyBlock partBlock;

        /// <summary>新建一份全息材质（调用方持有）。共享特效着色器都不可用时返回 null：调用方保留原模型，不贴品红。</summary>
        internal static Material CreateMaterial(string name, Color color)
        {
            Material shared = BossRushFxMaterials.Get(BossRushFxBlend.Alpha, Texture2D.whiteTexture);
            if (shared == null)
            {
                return null;
            }

            // 保持工厂的双面（Cull Off）：铁丝网一类单面片剔背面会在一半朝向下整片消失；
            // 维修罩的背面被工事本体的深度挡住，预览多出的内壁只是略浓一点
            Material material = new Material(shared);
            material.name = name;
            material.hideFlags = HideFlags.HideAndDontSave;
            WriteColor(material, null, color);
            return material;
        }

        /// <summary>写全息颜色：block 为 null 时写材质本身，否则写进属性块（调用方负责 SetPropertyBlock）。</summary>
        internal static void WriteColor(Material material, MaterialPropertyBlock block, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                if (block != null) block.SetColor(BaseColorId, color);
                else material.SetColor(BaseColorId, color);
            }
            else if (material.HasProperty(TintColorId))
            {
                // Legacy Alpha Blended 的片元自带 ×2：写 0.5 倍；SetVector 不做 sRGB→线性换算，所以先自己换
                Color linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
                Vector4 half = new Vector4(linear.r * 0.5f, linear.g * 0.5f, linear.b * 0.5f, color.a * 0.5f);
                if (block != null) block.SetVector(TintColorId, half);
                else material.SetVector(TintColorId, half);
            }
        }

        /// <summary>放置预览换「可放置 / 不可放置」颜色，并确保挂着呼吸组件。</summary>
        internal static void SetPreviewState(GameObject preview, Material material, bool canPlace)
        {
            if (preview == null || material == null)
            {
                return;
            }

            ModeFPlacementHologramBreath breath = preview.GetComponent<ModeFPlacementHologramBreath>();
            if (breath == null)
            {
                breath = preview.AddComponent<ModeFPlacementHologramBreath>();
            }
            breath.Bind(material, canPlace ? PlaceableColor : BlockedColor);
        }

        /// <summary>维修高亮显隐：淡入淡出代替 SetActive 硬切。要隐藏且已经关着时 O(1) 返回。</summary>
        internal static void ShowHighlight(GameObject root, bool show)
        {
            if (root == null || (!show && !root.activeSelf))
            {
                return;
            }

            ModeFRepairHologramFade fade = root.GetComponent<ModeFRepairHologramFade>();
            if (fade == null)
            {
                fade = root.AddComponent<ModeFRepairHologramFade>();
            }
            fade.SetVisible(show);
        }

        /// <summary>
        /// 兜底方块上色（VA-20）：URP 默认材质读 _BaseColor、内置管线读 _Color、官方着色器读 _Tint。
        /// 前两个一起写（属性块里未声明的属性不生效也无害），声明了 _Tint 再补一个。
        /// </summary>
        internal static void TintFallbackPart(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            if (partBlock == null)
            {
                partBlock = new MaterialPropertyBlock();
            }

            Material shared = renderer.sharedMaterial;
            renderer.GetPropertyBlock(partBlock);
            partBlock.SetColor(BaseColorId, color);
            partBlock.SetColor(ColorId, color);
            if (shared != null && shared.HasProperty(TintId))
            {
                partBlock.SetColor(TintId, color);
            }
            renderer.SetPropertyBlock(partBlock);
        }
    }

    /// <summary>
    /// 放置预览的呼吸：alpha 在 0.26–0.36 之间 1.2 Hz SmoothStep 往返（全息感，不是一块死色）。
    /// 预览期间先摘下渲染器上已有的属性块（兜底方块的配色会盖住全息色），
    /// 预览被确认成真实工事、材质换回原样后原样装回，再移除自己。
    /// </summary>
    internal sealed class ModeFPlacementHologramBreath : MonoBehaviour
    {
        private const float FrequencyHz = 1.2f;
        private const float AlphaLow = 0.26f;
        private const float AlphaHigh = 0.36f;

        private Material material;
        private Color color;
        private Renderer[] renderers;
        private MaterialPropertyBlock[] savedBlocks;
        private float phase;

        internal void Bind(Material hologram, Color baseColor)
        {
            material = hologram;
            color = baseColor;
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                savedBlocks = new MaterialPropertyBlock[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.HasPropertyBlock())
                    {
                        continue;
                    }

                    savedBlocks[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(savedBlocks[i]);
                    renderer.SetPropertyBlock(null);
                }
            }
            ApplyColor();
        }

        private void Update()
        {
            Renderer probe = renderers != null && renderers.Length > 0 ? renderers[0] : null;
            if (material == null || probe == null || probe.sharedMaterial != material)
            {
                RestoreBlocks();
                Destroy(this);
                return;
            }

            phase += Time.unscaledDeltaTime * FrequencyHz;
            ApplyColor();
        }

        private void RestoreBlocks()
        {
            if (renderers == null || savedBlocks == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length && i < savedBlocks.Length; i++)
            {
                if (renderers[i] != null && savedBlocks[i] != null)
                {
                    renderers[i].SetPropertyBlock(savedBlocks[i]);
                }
            }
            savedBlocks = null;
        }

        private void ApplyColor()
        {
            Color current = color;
            current.a = Mathf.Lerp(AlphaLow, AlphaHigh, BossRushUI.SmoothStep(Mathf.PingPong(phase * 2f, 1f)));
            ModeFFortificationHologramFx.WriteColor(material, null, current);
        }
    }

    /// <summary>
    /// 维修高亮的全息罩：0.15 s SmoothStep 淡入淡出，每个高亮用自己的属性块写 alpha（共享材质不动）。
    /// 完全显示后停用自己；淡出完关掉根物体，Update 随之停。
    /// </summary>
    internal sealed class ModeFRepairHologramFade : MonoBehaviour
    {
        private const float FadeSeconds = 0.15f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float level;
        private bool visible;

        internal void SetVisible(bool show)
        {
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                block = new MaterialPropertyBlock();
            }

            if (show && !gameObject.activeSelf)
            {
                level = 0f;
                ApplyAlpha();
                gameObject.SetActive(true);
            }

            if (visible == show)
            {
                return;
            }

            visible = show;
            enabled = true;
        }

        private void Update()
        {
            level = Mathf.MoveTowards(level, visible ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            ApplyAlpha();
            if (!visible && level <= 0f)
            {
                gameObject.SetActive(false);
            }
            else if (visible && level >= 1f)
            {
                enabled = false;
            }
        }

        private void ApplyAlpha()
        {
            if (renderers == null)
            {
                return;
            }

            Color color = ModeFFortificationHologramFx.RepairColor;
            color.a *= BossRushUI.SmoothStep(level);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(block);
                ModeFFortificationHologramFx.WriteColor(renderer.sharedMaterial, block, color);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
