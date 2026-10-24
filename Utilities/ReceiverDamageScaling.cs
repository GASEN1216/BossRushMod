using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 自定义 Boss 部位的减伤登记：蚀日花冠「花瓣护核」与贪吃宝箱怪「铁皮箱体」把各自的受击体登记进来，
    /// 由 Patches/Combat/ModeHFriendlyFireReceiverPatch（DamageReceiver.Hurt 前缀）在伤害进入官方 Health /
    /// HealthSimpleBase 之前按倍率缩小。没有登记时只读一次 Count 早返；倍率只允许 [0,1]，不会放大伤害。
    /// 登记由持有受击体的战斗 owner 在停战 / 销毁时撤销；切场景时整表清空。
    /// </summary>
    internal static class ReceiverDamageScaling
    {
        private static readonly Dictionary<DamageReceiver, Func<DamageInfo, float>> scalers =
            new Dictionary<DamageReceiver, Func<DamageInfo, float>>();

        internal static bool IsArmed { get { return scalers.Count > 0; } }

        internal static void Register(DamageReceiver receiver, Func<DamageInfo, float> multiplier)
        {
            if (receiver == null || multiplier == null) return;
            scalers[receiver] = multiplier;
        }

        internal static void Unregister(DamageReceiver receiver)
        {
            if (!ReferenceEquals(receiver, null)) scalers.Remove(receiver);
        }

        internal static void Apply(DamageReceiver receiver, ref DamageInfo info)
        {
            if (scalers.Count == 0 || ReferenceEquals(receiver, null) || !(info.damageValue > 0f)) return;
            Func<DamageInfo, float> multiplier;
            if (!scalers.TryGetValue(receiver, out multiplier)) return;
            try
            {
                float scale = multiplier(info);
                if (scale >= 0f && scale < 1f) info.damageValue *= scale;
            }
            catch (Exception e) { ModBehaviour.DevLog("[ReceiverDamageScaling] 倍率计算失败，按原伤害处理: " + e.Message); }
        }

        internal static void ResetStaticCaches()
        {
            scalers.Clear();
        }
    }
}
