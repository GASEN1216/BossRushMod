// ============================================================================
// FlightCloudEffect.cs - 飞行云雾特效
// ============================================================================
// 模块说明：
//   继承 RingParticleEffect 基类
//   为飞行图腾提供云雾视觉效果
// ============================================================================

using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// 飞行云雾特效 - 玩家脚下的云雾效果
    /// 继承通用的环形粒子特效基类
    /// </summary>
    public class FlightCloudEffect : RingParticleEffect
    {
        // ========== 飞行云雾的特定配置 ==========
        
        // 使用基类的默认值，无需覆盖
        // 如果需要调整，可以覆盖对应的属性
        
        // 示例：如果想要更多发射器
        // protected override int EmitterCount => 8;
        
        // 示例：如果想要更大的环形半径
        // protected override float EmitterRadius => 0.8f;
    }
}
