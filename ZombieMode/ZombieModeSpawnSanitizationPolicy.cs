namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal static bool ShouldKeepBossRushZombieSelfDestructionSkill(CharacterMainControl character)
        {
            if (character == null || character.gameObject == null)
            {
                return false;
            }

            ZombieModeEnemyRuntimeMarker marker = character.GetComponent<ZombieModeEnemyRuntimeMarker>();
            return marker != null &&
                !marker.IsBoss &&
                marker.EnemyKind == ZombieModeEnemyKind.Special &&
                marker.SpecialKind == ZombieModeSpecialKind.OfficialExploder;
        }
    }
}
