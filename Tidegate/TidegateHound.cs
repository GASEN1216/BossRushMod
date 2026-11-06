using Duckov.Utilities;

namespace BossRush
{
    internal static partial class TidegateEnemyActors
    {
        // 只用原版普通狼的外形、近战 AI 与原生掉落。冰狼、狼王和玩家的宠物都不是这个位置的候选。
        internal const string PatrolWolfPreset = "EnemyPreset_Animal_Wolf";

        static partial void PrepareHound(ref CharacterRandomPreset source)
        {
            var data = GameplayDataSettings.CharacterRandomPresetData;
            if (data != null && data.presets != null)
                foreach (CharacterRandomPreset preset in data.presets)
                    if (IsPatrolWolf(preset)) { source = preset; return; }
            var loaded = SkyIslandEnemySources.Named(PatrolWolfPreset);
            if (IsPatrolWolf(loaded)) source = loaded; // 游戏版本缺少成年普通狼时保留原来的拾荒者。
        }

        internal static bool IsPatrolWolf(CharacterRandomPreset preset)
        {
            return preset != null && preset.name == PatrolWolfPreset && !preset.isBoss && !preset.isZombie && !preset.isVehicle;
        }

        static partial void TuneHound(CharacterRandomPreset clone)
        {
            // 狼与首哨同行，适合绕出近战距离；听见玩家或看见玩家才追，不给穿墙索敌半径。
            clone.forceTracePlayerDistance = 0f;
        }
    }
}
