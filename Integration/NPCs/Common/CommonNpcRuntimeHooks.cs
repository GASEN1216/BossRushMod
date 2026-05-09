namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 统一生成公共NPC（快递员、哥布林、护士）
        /// </summary>
        private void SpawnCommonNPCs(string context)
        {
            int count = NPCModuleRegistry.SpawnForCurrentScene(this, context);
            DevLog("[NPCSpawn] " + context + "，已触发模块数量: " + count);
        }

        private bool ShouldSpawnCommonNPCsInScene(string sceneName)
        {
            return NPCModuleRegistry.ShouldSpawnAnyInScene(this, sceneName);
        }

        private void DestroyCommonNPCs(string context)
        {
            NPCModuleRegistry.DestroyAll(this, context);
        }
    }
}
