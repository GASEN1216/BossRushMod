namespace BossRush
{
    public partial class ModBehaviour
    {
        public void TrySpawnEggForPlayer() { audioRuntime.TrySpawnEggForPlayer(); }
        private void TryPlayNgmSound() { audioRuntime.TryPlayNgmSound(); }
        private static void ResetBossRushAudioHooksStaticCaches() { BossRushAudioRuntimeService.ResetBossRushAudioHooksStaticCaches(); }
        public void PlaySoundEffect(string filePath) { audioRuntime.PlaySoundEffect(filePath); }
    }
}
