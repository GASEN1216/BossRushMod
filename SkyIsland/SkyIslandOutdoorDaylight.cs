// ============================================================================
// SkyIslandOutdoorDaylight.cs - 岛上白天用官方户外后处理，不用地堡的室内那一套
// ============================================================================
// owner 2026-10-01 实机：「玩家视野外白天都和黑夜一样，烟雾太浓」。
//
// 根因（2026-10-01 UnityPy 离线读 level3 = Base.unity 与 level13 = GroundZero_Main 的 TimeOfDayConfig 查实）：
// 天空岛的 TimeOfDayConfig 是在基地克隆的那一份（场景包里造不出官方组件，见 SkyIslandRaidLease.Prepare）。
// 基地是地堡，它的晴天条目（defaultEntry）白天 / 黎明 / 夜里三个时段**全用室内的 `BaseVolume`**，
// 那份 Volume 的 `SunFogTD` 远景雾是纯黑色（fogColor 0,0,0，47–118 m）——于是岛上无论几点，镜头远处都被压成一片黑，
// 白天看起来和夜里一样。官方户外图（零号区）晴天白天用 `Volume_Level_GroundZero_Morning`：
// 浅蓝色远景雾（50–90 m），`TimeOfDayPost` 的视野系数为 1。阴雨与风暴条目基地本来就用零号区那几份，不动。
//
// 做法：只在克隆出来的副本上，把白天与黎明时段里的 `BaseVolume` 换成已加载的 `Volume_Level_GroundZero_Morning`
// （它就在基地的资源里，sharedassets3）。夜里仍保留原样（夜里本来就该暗，敌我夜间视野也不因此改变）。
// 基地自己那份配置不碰；找不到这份官方 Volume 时记一条警告、保持原样，不阻断进岛。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    internal static class SkyIslandOutdoorDaylight
    {
        /// <summary>地堡的室内后处理（远景雾纯黑）。</summary>
        internal const string IndoorProfileName = "BaseVolume";
        /// <summary>官方零号区晴天白天的户外后处理。</summary>
        internal const string DaylightProfileName = "Volume_Level_GroundZero_Morning";

        private static readonly FieldInfo PhasesField =
            typeof(TimeOfDayEntry).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>把副本里白天 / 黎明时段的室内 Volume 换成户外的；返回换了几处。只在进岛准备时调一次。</summary>
        internal static int Apply(TimeOfDayConfig clone)
        {
            if (clone == null) return 0;
            try
            {
                if (PhasesField == null) throw new MissingFieldException("TimeOfDayEntry.phases");
                VolumeProfile daylight = null;
                foreach (VolumeProfile profile in Resources.FindObjectsOfTypeAll<VolumeProfile>())
                    if (profile != null && profile.name == DaylightProfileName) { daylight = profile; break; }
                if (daylight == null)
                {
                    Debug.LogWarning("[SkyIsland] 没找到官方户外后处理 " + DaylightProfileName + "，岛上白天沿用基地的室内后处理");
                    return 0;
                }
                int replaced = 0;
                foreach (TimeOfDayEntry entry in clone.GetComponentsInChildren<TimeOfDayEntry>(true))
                {
                    var phases = PhasesField.GetValue(entry) as List<TimeOfDayPhase>;
                    if (phases == null) continue;
                    for (int i = 0; i < phases.Count; i++)
                    {
                        TimeOfDayPhase phase = phases[i];
                        if (phase.timePhaseTag == TimePhaseTags.night || phase.volumeProfile == null ||
                            phase.volumeProfile.name != IndoorProfileName) continue;
                        phase.volumeProfile = daylight;
                        phases[i] = phase;
                        replaced++;
                    }
                }
                Debug.Log("[SkyIsland] DAYLIGHT_VOLUME replaced=" + replaced + " with " + DaylightProfileName);
                return replaced;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 换户外后处理失败（沿用基地原样）：" + e.Message);
                return 0;
            }
        }
    }
}
