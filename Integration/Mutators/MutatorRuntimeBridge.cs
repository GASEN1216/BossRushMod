namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void TryRollMutatorsForMode(string modeTag)
        {
            MutatorModeFlow.TryRollMutatorsForMode(modeTag, config != null && config.enableMutators,
                config != null ? config.mutatorCount : 0, MutatorCountMin, MutatorCountMax);
        }

        internal void ClearMutatorsForMode(string modeTag) { MutatorModeFlow.ClearMutatorsForMode(modeTag); }
    }
}
