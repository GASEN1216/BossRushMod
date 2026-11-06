using System;
using UnityEngine;

namespace BossRush
{
    internal enum TidegateEnemyRole { Regular, Hound, Marksman, Gunner }

    // 地图 owner 决定岗位；各角色只负责自己的官方预设、克隆与装备，没接成功就保留普通守备。
    internal static partial class TidegateEnemyActors
    {
        internal static CharacterRandomPreset Source(TidegateEnemyRole role, CharacterRandomPreset fallback)
        {
            CharacterRandomPreset source = fallback;
            try
            {
                switch (role)
                {
                    case TidegateEnemyRole.Hound: PrepareHound(ref source); break;
                    case TidegateEnemyRole.Marksman: PrepareMarksman(ref source); break;
                    case TidegateEnemyRole.Gunner: PrepareGunner(ref source); break;
                }
            }
            catch (Exception e) { Warn(role, e); }
            return source != null ? source : fallback;
        }

        internal static void Tune(TidegateEnemyRole role, CharacterRandomPreset clone)
        {
            try
            {
                switch (role)
                {
                    case TidegateEnemyRole.Hound: TuneHound(clone); break;
                    case TidegateEnemyRole.Marksman: TuneMarksman(clone); break;
                    case TidegateEnemyRole.Gunner: TuneGunner(clone); break;
                }
            }
            catch (Exception e) { Warn(role, e); }
        }

        internal static bool Equip(TidegateEnemyRole role, CharacterMainControl character, TidegateTide tide, Func<bool> valid)
        {
            bool handled = false;
            try
            {
                switch (role)
                {
                    case TidegateEnemyRole.Hound: EquipHound(character); break;
                    case TidegateEnemyRole.Marksman: EquipMarksman(character, ref handled); break;
                    case TidegateEnemyRole.Gunner: EquipGunner(character, tide, valid); break;
                }
            }
            catch (Exception e) { Warn(role, e); }
            return handled;
        }

        private static void Warn(TidegateEnemyRole role, Exception e)
        {
            Debug.LogWarning(TidegateRaidScene.LogTag + " 岗位配置失败 " + role + "：" + e.Message);
        }
        static partial void PrepareHound(ref CharacterRandomPreset source);
        static partial void PrepareMarksman(ref CharacterRandomPreset source);
        static partial void PrepareGunner(ref CharacterRandomPreset source);
        static partial void TuneHound(CharacterRandomPreset clone);
        static partial void TuneMarksman(CharacterRandomPreset clone);
        static partial void TuneGunner(CharacterRandomPreset clone);
        static partial void EquipHound(CharacterMainControl character);
        static partial void EquipMarksman(CharacterMainControl character, ref bool handled);
        static partial void EquipGunner(CharacterMainControl character, TidegateTide tide, Func<bool> valid);
    }
}
