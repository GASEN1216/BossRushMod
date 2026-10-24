using System;

namespace BossRush
{
    /// <summary>花冠招式的纯规则参数；地面网格与伤害共用这一份几何。</summary>
    internal static class EclipseBloomTuning
    {
        internal const int PetalCount = 4;
        internal const int AllPetalsMask = (1 << PetalCount) - 1;
        internal const float InnerRadius = 0.6f;
        internal const float OuterRadius = 13f;
        internal const float HalfAngleDegrees = 30f;
        internal const float PetalOrbitRadius = 2.8f;
        internal const float PetalCenterHeight = 1f;
        internal const float SecondPhaseHealthRatio = 0.60f;
        internal const float ThirdPhaseHealthRatio = 0.25f;
        internal const float RotationDegreesPerSecond = 9f;
        internal const float MaximumStepSeconds = 0.1f;
        internal const float FirstPhaseTelegraphSeconds = 1.45f;
        internal const float FirstPhaseDamageSeconds = 0.65f;
        internal const float FirstPhaseRestSeconds = 0.85f;
        internal const float SecondPhaseTelegraphSeconds = 1.65f;
        internal const float SecondPhaseDamageSeconds = 0.95f;
        internal const float SecondPhaseRestSeconds = 0.75f;
        internal const float CoreTelegraphSeconds = 1.8f;
        internal const float CoreDamageSeconds = 0.8f;
        internal const float CoreRestSeconds = 1.25f;
        // 预警前段把最近的攻击扇区转向玩家，之后锁定；锁定后剩余预警仍有 1 秒以上。
        internal const float AimLockSeconds = 0.45f;
        internal const float AimTurnDegreesPerSecond = 260f;
        // 玩家离花冠 9.5 m 以外放风筝时，每次新预警同时落日珥。
        internal const float FlareMinimumDistance = 9.5f;
        internal const float FlareRadius = 2.4f;
    }

    /// <summary>
    /// 不依赖 Unity 的战斗规则。调用者暂停时传入 0；每帧在 Advance 后显示当前几何。
    /// 时间步限幅且不跨段追赶，保证刚开始的预警至少完整经历一个独立时间段。
    /// </summary>
    internal sealed class EclipseBloomRules
    {
        private enum AttackStage { Telegraph, Damage, Rest }

        private AttackStage stage;
        private float stageElapsed;
        private int nextPetal;
        private int patternIndex;
        private int corePulseIndex;
        private float aimBearing;
        private bool hasAim;

        internal int Phase { get; private set; }
        internal int IntactMask { get; private set; }
        internal float RotationDegrees { get; private set; }
        internal int AttackMask { get; private set; }
        internal bool IsCoreAttack { get; private set; }
        internal bool IsStopped { get; private set; }
        internal float ElapsedSeconds { get; private set; }
        internal float LastStepSeconds { get; private set; }
        /// <summary>每开始一次新预警加一；宿主据此在同一帧追加远程压制。</summary>
        internal int AttackSerial { get; private set; }

        internal bool IsDamaging { get { return !IsStopped && stage == AttackStage.Damage && AttackMask != 0; } }
        internal bool IsResting { get { return !IsStopped && stage == AttackStage.Rest; } }
        internal float TelegraphProgress
        {
            get
            {
                if (IsStopped || stage == AttackStage.Rest) return 0f;
                if (stage == AttackStage.Damage) return 1f;
                return Math.Min(1f, stageElapsed / TelegraphDuration);
            }
        }

        private float TelegraphDuration
        {
            get
            {
                if (IsCoreAttack) return EclipseBloomTuning.CoreTelegraphSeconds;
                return Phase == 1 ? EclipseBloomTuning.FirstPhaseTelegraphSeconds : EclipseBloomTuning.SecondPhaseTelegraphSeconds;
            }
        }

        private float DamageDuration
        {
            get
            {
                if (IsCoreAttack) return EclipseBloomTuning.CoreDamageSeconds;
                return Phase == 1 ? EclipseBloomTuning.FirstPhaseDamageSeconds : EclipseBloomTuning.SecondPhaseDamageSeconds;
            }
        }

        private float RestDuration
        {
            get
            {
                if (IsCoreAttack) return EclipseBloomTuning.CoreRestSeconds;
                return Phase == 1 ? EclipseBloomTuning.FirstPhaseRestSeconds : EclipseBloomTuning.SecondPhaseRestSeconds;
            }
        }

        internal EclipseBloomRules()
        {
            Phase = 1;
            IntactMask = EclipseBloomTuning.AllPetalsMask;
            BeginTelegraph();
        }

        internal void Advance(float deltaSeconds, float healthRatio)
        {
            LastStepSeconds = 0f;
            if (IsStopped || !IsFinite(deltaSeconds) || deltaSeconds <= 0f) return;

            float step = Math.Min(deltaSeconds, EclipseBloomTuning.MaximumStepSeconds);
            LastStepSeconds = step;
            ElapsedSeconds += step;

            int nextPhase = Phase;
            if (IsFinite(healthRatio))
            {
                if (healthRatio <= EclipseBloomTuning.ThirdPhaseHealthRatio) nextPhase = 3;
                else if (healthRatio <= EclipseBloomTuning.SecondPhaseHealthRatio) nextPhase = Math.Max(Phase, 2);
            }
            if (nextPhase > Phase)
            {
                Phase = nextPhase;
                if (Phase == 3) IntactMask = 0;
                patternIndex = 0;
                corePulseIndex = 0;
                BeginTelegraph();
                return;
            }

            if (Phase == 2)
                RotationDegrees = NormalizeDegrees(RotationDegrees + EclipseBloomTuning.RotationDegreesPerSecond * step);

            if (hasAim && stage == AttackStage.Telegraph && stageElapsed < EclipseBloomTuning.AimLockSeconds) TurnTowardAim(step);
            stageElapsed += step;
            if (stage == AttackStage.Telegraph && stageElapsed >= TelegraphDuration)
            {
                stage = AttackStage.Damage;
                stageElapsed = 0f;
            }
            else if (stage == AttackStage.Damage && stageElapsed >= DamageDuration)
            {
                BeginRest();
            }
            else if (stage == AttackStage.Rest && stageElapsed >= RestDuration)
            {
                BeginTelegraph();
            }
        }

        /// <summary>一次成功破碎永久清除一位；不把当前预警转换成未展示过的核心伤害。</summary>
        internal bool BreakPetal(int index)
        {
            if (IsStopped || index < 0 || index >= EclipseBloomTuning.PetalCount) return false;
            int bit = 1 << index;
            if ((IntactMask & bit) == 0) return false;
            IntactMask &= ~bit;
            AttackMask &= ~bit;
            if (IntactMask == 0)
            {
                BeginTelegraph();
            }
            else if (AttackMask == 0 && stage != AttackStage.Rest)
            {
                BeginRest();
            }
            return true;
        }

        /// <summary>
        /// 记录玩家方位（0 度朝本地 +Z，正角朝 +X）。只在预警前 AimLockSeconds 内生效，伤害段与锁定后的预警不再移动。
        /// </summary>
        internal void Track(float bearingDegrees)
        {
            if (IsStopped || !IsFinite(bearingDegrees)) { hasAim = false; return; }
            aimBearing = NormalizeDegrees(bearingDegrees);
            hasAim = true;
        }

        private void TurnTowardAim(float step)
        {
            if ((AttackMask & EclipseBloomTuning.AllPetalsMask) == 0) return;
            double best = double.MaxValue;
            for (int index = 0; index < EclipseBloomTuning.PetalCount; index++)
            {
                if ((AttackMask & (1 << index)) == 0) continue;
                double difference = (aimBearing - GetSectorCenterDegrees(index, RotationDegrees) + 540.0) % 360.0 - 180.0;
                if (Math.Abs(difference) < Math.Abs(best)) best = difference;
            }
            float limit = EclipseBloomTuning.AimTurnDegreesPerSecond * step;
            float turn = (float)Math.Max(-limit, Math.Min(limit, best));
            RotationDegrees = NormalizeDegrees(RotationDegrees + turn);
        }

        internal bool IsDangerous(float localX, float localZ)
        {
            return IsDamaging && ContainsSector(localX, localZ, AttackMask, RotationDegrees);
        }

        internal void Stop()
        {
            IsStopped = true;
            AttackMask = 0;
            stageElapsed = 0f;
            LastStepSeconds = 0f;
        }

        private void BeginTelegraph()
        {
            stage = AttackStage.Telegraph;
            stageElapsed = 0f;
            AttackSerial++;
            IsCoreAttack = Phase == 3 || IntactMask == 0;
            if (IsCoreAttack)
            {
                AttackMask = (corePulseIndex++ & 1) == 0 ? 5 : 10;
                return;
            }
            if (Phase == 1)
            {
                for (int offset = 0; offset < EclipseBloomTuning.PetalCount; offset++)
                {
                    int index = (nextPetal + offset) % EclipseBloomTuning.PetalCount;
                    if ((IntactMask & (1 << index)) == 0) continue;
                    AttackMask = 1 << index;
                    nextPetal = (index + 1) % EclipseBloomTuning.PetalCount;
                    return;
                }
            }

            int count = CountBits(IntactMask);
            int sequence = patternIndex++;
            if (count <= 2)
            {
                AttackMask = IntactMask;
            }
            else if (sequence % 3 == 2)
            {
                // 四瓣时轮换让出一个扇区；已破瓣形成的缺口始终保留。
                AttackMask = count == 4 ? IntactMask & ~(1 << ((sequence / 3) % EclipseBloomTuning.PetalCount)) : IntactMask;
            }
            else
            {
                int pair = (sequence & 1) == 0 ? 5 : 10;
                AttackMask = IntactMask & pair;
                if (CountBits(AttackMask) < 2) AttackMask = IntactMask & (pair ^ EclipseBloomTuning.AllPetalsMask);
            }
        }

        private void BeginRest()
        {
            stage = AttackStage.Rest;
            stageElapsed = 0f;
            AttackMask = 0;
        }

        /// <summary>0 度朝本地 +Z，正角朝 +X，与 Unity 的 Y 轴旋转方向一致。</summary>
        internal static float GetSectorCenterDegrees(int index, float rotationDegrees)
        {
            if (index < 0 || index >= EclipseBloomTuning.PetalCount) throw new ArgumentOutOfRangeException("index");
            return NormalizeDegrees(rotationDegrees + index * (360f / EclipseBloomTuning.PetalCount));
        }

        /// <summary>网格使用本方法采样扇区；角偏移取 [-HalfAngleDegrees, +HalfAngleDegrees]。</summary>
        internal static void GetSectorPoint(int index, float rotationDegrees, float radius, float angularOffsetDegrees,
            out float localX, out float localZ)
        {
            double radians = (GetSectorCenterDegrees(index, rotationDegrees) + angularOffsetDegrees) * Math.PI / 180.0;
            localX = (float)(Math.Sin(radians) * radius);
            localZ = (float)(Math.Cos(radians) * radius);
        }

        /// <summary>仅计算平面几何；有限高度与玩家阵营由宿主调用点检查。</summary>
        internal static bool ContainsSector(float localX, float localZ, int mask, float rotationDegrees)
        {
            if (!IsFinite(localX) || !IsFinite(localZ) || !IsFinite(rotationDegrees)) return false;
            mask &= EclipseBloomTuning.AllPetalsMask;
            if (mask == 0) return false;
            double radiusSquared = (double)localX * localX + (double)localZ * localZ;
            const double boundaryTolerance = 0.0001;
            double inner = EclipseBloomTuning.InnerRadius;
            double outer = EclipseBloomTuning.OuterRadius;
            if (radiusSquared < inner * inner - boundaryTolerance || radiusSquared > outer * outer + boundaryTolerance) return false;

            double angle = Math.Atan2(localX, localZ) * 180.0 / Math.PI;
            for (int index = 0; index < EclipseBloomTuning.PetalCount; index++)
            {
                if ((mask & (1 << index)) == 0) continue;
                double difference = (angle - GetSectorCenterDegrees(index, rotationDegrees) + 540.0) % 360.0 - 180.0;
                if (Math.Abs(difference) <= EclipseBloomTuning.HalfAngleDegrees + boundaryTolerance) return true;
            }
            return false;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            for (; value != 0; value &= value - 1) count++;
            return count;
        }

        private static float NormalizeDegrees(float value)
        {
            float result = value % 360f;
            return result < 0f ? result + 360f : result;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
