using System;

namespace BossRush
{
    /// <summary>空仓地雷的单弹匣计数与冷却；不依赖 Unity，可直接执行边界回归。</summary>
    internal sealed class EmptyMagazineMineRules
    {
        private readonly int minimumShots;
        private readonly float cooldownSeconds;
        private int weaponInstanceId;
        private int lastAmmoAfterShot = -1;
        private int shotCount;
        private float nextDeployAt;

        internal EmptyMagazineMineRules(int minimumShots, float cooldownSeconds)
        {
            if (minimumShots <= 0) throw new ArgumentOutOfRangeException("minimumShots");
            if (cooldownSeconds < 0f || float.IsNaN(cooldownSeconds) || float.IsInfinity(cooldownSeconds))
                throw new ArgumentOutOfRangeException("cooldownSeconds");
            this.minimumShots = minimumShots;
            this.cooldownSeconds = cooldownSeconds;
        }

        internal int ShotCount { get { return shotCount; } }
        internal float NextDeployAt { get { return nextDeployAt; } }

        /// <summary>
        /// 每次官方实际射击后调用一次，传扣弹后的余量。冷却中的空仓当场作废，不留待补发。
        /// 霰弹的多颗弹丸不会增加次数；切枪或观察到补弹时，前一个弹匣的计数不能接续。
        /// </summary>
        internal bool ObserveShot(int weaponId, int ammoAfterShot, float now, bool hasPendingMine)
        {
            if (weaponId == 0 || ammoAfterShot < 0 || float.IsNaN(now) || float.IsInfinity(now))
            {
                ResetMagazine();
                return false;
            }

            if (weaponInstanceId != weaponId || (lastAmmoAfterShot >= 0 && ammoAfterShot > lastAmmoAfterShot))
            {
                ResetMagazine();
                weaponInstanceId = weaponId;
            }

            if (shotCount < minimumShots) shotCount++;
            lastAmmoAfterShot = ammoAfterShot;
            if (ammoAfterShot > 0) return false;

            bool deploy = shotCount >= minimumShots && !hasPendingMine && now >= nextDeployAt;
            ResetMagazine();
            if (deploy) nextDeployAt = now + cooldownSeconds;
            return deploy;
        }

        /// <summary>提前换弹、切枪与卸下图腾清次数，不能靠穿脱洗掉已开始的冷却。</summary>
        internal void ResetMagazine()
        {
            weaponInstanceId = 0;
            lastAmmoAfterShot = -1;
            shotCount = 0;
        }

        /// <summary>死亡、切图与 runtime cleanup 结束整段会话。</summary>
        internal void ResetAll()
        {
            ResetMagazine();
            nextDeployAt = 0f;
        }
    }
}
