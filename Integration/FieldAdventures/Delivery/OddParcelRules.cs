using System;

namespace BossRush
{
    internal enum OddParcelKind { QuackingEgg, AngrySoda }

    /// <summary>纯规则。快递不进背包、不碰物品和存档；退单仍占本次出击的一单额度。</summary>
    internal sealed class OddParcelCondition
    {
        internal readonly OddParcelKind Kind;
        internal float Pressure { get; private set; }
        internal float Integrity { get; private set; }
        internal bool Paid { get; private set; }
        private bool paying;
        internal OddParcelCondition(OddParcelKind kind) { Kind = kind; Integrity = 100f; }

        /// <returns>本次是否发生弹跳 / 喷发。暂停由 owner 门控；超长帧限制单次变化。</returns>
        internal bool Step(float seconds, float speed, bool carried)
        {
            if (Paid || seconds <= 0f || float.IsNaN(seconds) || float.IsNaN(speed)
                || float.IsInfinity(seconds) || float.IsInfinity(speed)) return false;
            seconds = Math.Min(seconds, 0.5f);
            float rate;
            if (Kind == OddParcelKind.QuackingEgg)
                rate = !carried ? -28f : speed > 4.4f ? 24f : speed > 1f ? 3f : -18f;
            else
                rate = !carried ? -36f : speed > 4.4f ? 31f : 12f;
            Pressure = Math.Max(0f, Math.Min(100f, Pressure + rate * seconds));
            if (Pressure < 100f) return false;
            Pressure = Kind == OddParcelKind.QuackingEgg ? 28f : 10f;
            Integrity = Math.Max(0f, Integrity - (Kind == OddParcelKind.QuackingEgg ? 16f : 22f));
            return true;
        }

        internal bool Ruined { get { return Integrity <= 0f; } }
        internal int Reward { get { return 1200 + (int)Math.Round(Math.Max(0f, Math.Min(100f, Integrity)) * 12f); } }

        /// <summary>只有官方收款返回成功才关账；失败保持原状态，下一次签收可重试。</summary>
        internal bool TryPay(Func<long, bool> addMoney)
        {
            if (Paid || paying || Ruined || addMoney == null) return false;
            paying = true;
            try
            {
                if (!addMoney(Reward)) return false;
                Paid = true;
                return true;
            }
            finally { paying = false; }
        }
    }
}
