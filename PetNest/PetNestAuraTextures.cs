// ============================================================================
// PetNestAuraTextures.cs - 崽身上炫彩 / 异色特效的程序化粒子贴图
// ============================================================================
// 为什么要自己画：
//   旧光环只有 RingParticleEffect 那一张「中心亮、边缘淡」的圆形渐变，放大成 0.85 m、
//   再叠六个发射器，就是 owner 截图里那团黄色雾（2026-09-22 实测第 9 条「太廉价」）。
//   元素感要靠**形状**说话：星芒、雪花、叶片、气泡、电弧、符文环……
//   这些形状全部在这里程序化画出来，零新增美术资源、不重打 AssetBundle。
//
// 口径：
//   - 纯函数：输入种类与边长，输出 Color32[]（RGB 恒白，只有 alpha 描形状）。
//     颜色一律交给粒子的 startColor / 材质 _TintColor，所以一张贴图可以给任意颜色用；
//   - 像素按 Texture2D.SetPixels32 的口径排列（行优先、左下原点），采样点取像素中心；
//   - 所有形状在贴图边缘 alpha 归零，Clamp 采样不会出现硬边；
//   - 不持有任何 Unity 对象：Texture2D 由 PetNestAuraEffect 按需创建、随特效一起销毁，
//     本文件没有静态缓存，也就没有需要登记的清理路径。
//   - 2026-09-23 起画师本体在 Common/Effects/BossRushParticleTextures.cs（全 Mod 共享），本文件只留
//     遗种巢的枚举与转调；枚举数值与共享枚举一一对应，改一边必须同改另一边。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>崽特效用到的程序化贴图种类。数值用作数组下标，Count 必须在最后。</summary>
    internal enum PetNestAuraTexture
    {
        /// <summary>亮核 + 柔光晕的圆点（火星、光点、孢子、雨滴）。</summary>
        GlowDot = 0,
        /// <summary>四主芒 + 四副芒的星光（闪光、金星、冰晶闪烁）。</summary>
        Star = 1,
        /// <summary>带高光的空心气泡。</summary>
        Bubble = 2,
        /// <summary>六瓣雪花。</summary>
        Snowflake = 3,
        /// <summary>带叶脉的叶片。</summary>
        Leaf = 4,
        /// <summary>带噪声边缘的烟缕（暗影、寒雾）。</summary>
        Wisp = 5,
        /// <summary>折线电弧。</summary>
        Bolt = 6,
        /// <summary>细长菱形碎片（银色镜屑）。</summary>
        Shard = 7,
        /// <summary>干净细环（水面涟漪）。</summary>
        Ring = 8,
        /// <summary>异色脚下外圈：双细环 + 刻度 + 菱形符文（128 px）。</summary>
        RuneOuter = 9,
        /// <summary>异色脚下内圈：细环 + 八颗珠点。</summary>
        RuneInner = 10,
        /// <summary>拖尾条带：沿 U 恒亮、沿 V 柔边（拖尾按 Stretch 贴，头尾渐隐交给 colorOverTrail）。</summary>
        TrailStrip = 11,
        Count = 12,
    }

    /// <summary>
    /// 程序化粒子贴图画师的遗种巢入口。无状态、无静态缓存。
    /// 画师本体 2026-09-23 挪到 Common/Effects/BossRushParticleTextures（VA-34：套装、新武器、随机事件
    /// 也要用星芒 / 烟缕 / 条带），这里按枚举数值原样转调，像素与挪动前一致（雪花臂宽除外，见那边注释）。
    /// </summary>
    internal static class PetNestAuraTextures
    {
        /// <summary>各种类的边长（与共享画师同口径）。</summary>
        internal static int SizeOf(PetNestAuraTexture kind)
        {
            return BossRushParticleTextures.SizeOf(ToShared(kind));
        }

        /// <summary>画一张贴图的像素。size 必须为正；未知种类返回全透明。</summary>
        internal static Color32[] Paint(PetNestAuraTexture kind, int size)
        {
            return BossRushParticleTextures.Paint(ToShared(kind), size);
        }

        /// <summary>单点 alpha。x、y 是以贴图中心为原点、边缘为 ±1 的坐标。</summary>
        internal static float Sample(PetNestAuraTexture kind, float x, float y)
        {
            return BossRushParticleTextures.Sample(ToShared(kind), x, y);
        }

        /// <summary>两边枚举逐项同值（GlowDot = 0 … TrailStrip = 11），按数值转换。</summary>
        private static BossRushParticleShape ToShared(PetNestAuraTexture kind)
        {
            int value = (int)kind;
            if (value < 0 || value >= (int)BossRushParticleShape.Count) return BossRushParticleShape.Count;
            return (BossRushParticleShape)value;
        }
    }
}
