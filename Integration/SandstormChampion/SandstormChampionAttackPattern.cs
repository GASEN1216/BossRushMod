namespace BossRush
{
    internal enum SandstormChampionAttackKind
    {
        Dashes, BubbleBelch, SpiralBubbles, TwinTornadoSeeds, HomingCycloneSeed, TeleportDashes
    }

    internal struct SandstormChampionAttack
    {
        internal readonly SandstormChampionAttackKind Kind;
        internal readonly int Count;
        internal SandstormChampionAttack(SandstormChampionAttackKind kind, int count)
        {
            Kind = kind;
            Count = count;
        }
    }

    /// <summary>
    /// 原版专家 / 大师 / 传奇猪鲨共用的攻击顺序。场地、移动、伤害交给实例 owner；
    /// 本类不依赖 Unity，隔离回归直接执行这份生产调度逻辑。
    /// </summary>
    internal sealed class SandstormChampionAttackPattern
    {
        private int _index;
        private int _finalStorms;
        internal int Phase { get; private set; } = 1;
        internal int CompletedCycles { get; private set; }
        internal bool Enraged { get; private set; }

        internal bool UpdateState(float healthRatio, bool enraged)
        {
            int wanted = healthRatio < 0.15f ? 3 : healthRatio < 0.5f ? 2 : 1;
            bool changed = wanted > Phase;
            if (changed) Phase = wanted;
            if (changed || (Phase < 3 && Enraged != enraged)) _index = 0;
            Enraged = enraged;
            return changed;
        }

        internal SandstormChampionAttack Next()
        {
            if (Phase == 3)
            {
                // owner 2026-10-08（两次实测）：末阶段不能只剩冲锋。每组换侧一/二/三冲之后轮换放技能：
                // 双生沙卷 → 隐身绕圈喷 31 泡 → 大沙暴，循环。
                if (_index == 3)
                {
                    _index = 0;
                    CompletedCycles++;
                    switch (_finalStorms++ % 3)
                    {
                        case 0: return new SandstormChampionAttack(SandstormChampionAttackKind.TwinTornadoSeeds, 2);
                        case 1: return new SandstormChampionAttack(SandstormChampionAttackKind.SpiralBubbles, 31);
                        default: return new SandstormChampionAttack(SandstormChampionAttackKind.HomingCycloneSeed, 1);
                    }
                }
                int count = ++_index;
                return new SandstormChampionAttack(SandstormChampionAttackKind.TeleportDashes, count);
            }
            if (Enraged)
            {
                // 鸭科夫没有海洋：报名点的竞技半径承担原版离开海面的激怒条件。
                // 取消吐泡休息，连续冲锋中更频繁地补大旋风；末阶段仍只有冲锋。
                if (++_index == 4)
                {
                    _index = 0;
                    CompletedCycles++;
                    return new SandstormChampionAttack(SandstormChampionAttackKind.HomingCycloneSeed, 1);
                }
                return new SandstormChampionAttack(SandstormChampionAttackKind.Dashes, 3);
            }
            int step = _index++;
            if (_index == 4) { _index = 0; CompletedCycles++; }
            if (step == 0 || step == 2)
                return new SandstormChampionAttack(SandstormChampionAttackKind.Dashes, Phase == 1 ? 5 : 3);
            if (step == 1)
                return new SandstormChampionAttack(Phase == 1 ? SandstormChampionAttackKind.BubbleBelch
                    : SandstormChampionAttackKind.SpiralBubbles, Phase == 1 ? 21 : 31);
            return new SandstormChampionAttack(Phase == 1 ? SandstormChampionAttackKind.TwinTornadoSeeds
                : SandstormChampionAttackKind.HomingCycloneSeed, Phase == 1 ? 2 : 1);
        }
    }
}
