using Cysharp.Threading.Tasks;

namespace BossRush
{
    public partial class FrostmourneAction
    {
        private CharacterMainControl coopOwner;
        private string coopRun;
        private int coopScene;
        private string coopAsset, coopToken;
        internal async UniTask SpawnForCoop(CharacterMainControl player)
        {
            coopOwner = player; coopRun = BossRushCoop.RunId; coopScene = BossRushCoop.SceneGeneration; coopToken = BossRushCoop.SceneToken;
            coopAsset = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(player))?.AssetId;
            activeSpawnRequestId++; spawningStarted = true;
            await SpawnZombiesAsync(activeSpawnRequestId);
        }
        private void OnCoopSummonResult(int attempt, bool accepted)
        {
            if (this == null || attempt != coopSummonAttempt || accepted) return;
            coopSummonRejected = true;
            lastEndTime = -999f; isCoolingDown = false;
        }
        protected override void OnStop()
        {
            base.OnStop();
            if (coopSummonRejected) { lastEndTime = -999f; isCoolingDown = false; }
        }

        protected override void OnAbilityStop()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
        }

        internal static bool TryGetSummonOwner(CharacterMainControl summoned, out CharacterMainControl owner)
        {
            owner = null;
            CharacterMainControl candidate;
            if (summoned == null || !summonOwners.TryGetValue(summoned, out candidate) || candidate == null) return false;
            if (BossRushCoop.IsConnected ? !BossRushCoopContentRuntimeModule.IsSummonOwnedBy(summoned, candidate)
                : candidate != CharacterMainControl.Main) return false;
            owner = candidate;
            return true;
        }

    }
}
