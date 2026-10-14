using Cysharp.Threading.Tasks;

namespace BossRush
{
    public partial class FrostmourneAction
    {
        private CharacterMainControl coopOwner;
        private string coopRun;
        private int coopScene;
        private string coopAsset;
        internal async UniTask SpawnForCoop(CharacterMainControl player)
        {
            coopOwner = player; coopRun = BossRushCoop.RunId; coopScene = BossRushCoop.SceneGeneration;
            coopAsset = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(player))?.AssetId;
            activeSpawnRequestId++; spawningStarted = true;
            await SpawnZombiesAsync(activeSpawnRequestId);
        }
    }
}
