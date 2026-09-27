using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // 旧宿主签名保持在原调度位置，状态与具体实现归唯一 UIAndSignsRuntimeModule。
        private string statusMessage { get { return uiAndSignsRuntime.StatusMessage; } set { uiAndSignsRuntime.StatusMessage = value; } }
        private float messageTimer { get { return uiAndSignsRuntime.MessageTimer; } set { uiAndSignsRuntime.MessageTimer = value; } }
        private BossRushSignInteractable bossRushSignInteract { get { return uiAndSignsRuntime.SignInteract; } set { uiAndSignsRuntime.SignInteract = value; } }
        private GameObject _bossRushSignGameObject { get { return uiAndSignsRuntime.SignGameObject; } set { uiAndSignsRuntime.SignGameObject = value; } }
        internal static readonly string RichDangerTag = UIAndSignsRuntimeModule.RichDangerTag;
        internal static readonly string RichWarningTag = UIAndSignsRuntimeModule.RichWarningTag;
        internal static readonly string RichSuccessTag = UIAndSignsRuntimeModule.RichSuccessTag;
        internal Vector3 UIAndSignsBaseEntryPosition { get { return BaseEntryPosition; } }
        internal WaitForSeconds UIAndSignsSharedWait1s { get { return sharedWait1s; } }
        internal void SetArenaCenterFromSign_UIAndSigns(Vector3 position)
        {
            WavesArenaRuntimeModule.SetArenaCenterFromSign(position);
        }
        private string GetDirectionFromPlayer(Vector3 enemyPos, Vector3 playerPos)
        {
            return uiAndSignsRuntime.GetDirectionFromPlayer(enemyPos, playerPos);
        }

        private void UpdateMessage_UIAndSigns() { uiAndSignsRuntime.UpdateMessage_UIAndSigns(); }
        private void ShowMessage_UIAndSigns(string msg) { uiAndSignsRuntime.ShowMessage_UIAndSigns(msg); }
        private void ShowEnemyBanner_UIAndSigns(string enemyName, Vector3 enemyPos, Vector3 playerPos,
            int currentEnemyIndexParam, int totalEnemiesParam, bool infiniteHellModeParam,
            int infiniteHellWaveIndexParam, int bossesPerWaveParam)
        {
            uiAndSignsRuntime.ShowEnemyBanner_UIAndSigns(enemyName, enemyPos, playerPos,
                currentEnemyIndexParam, totalEnemiesParam, infiniteHellModeParam,
                infiniteHellWaveIndexParam, bossesPerWaveParam);
        }
        private void ShowBigBanner_UIAndSigns(string text) { uiAndSignsRuntime.ShowBigBanner_UIAndSigns(text); }
        private IEnumerator FindInteractionTargets(int scanTimes) { return uiAndSignsRuntime.FindInteractionTargets(scanTimes); }
        private string GetGameObjectPath(GameObject obj) { return uiAndSignsRuntime.GetGameObjectPath(obj); }
        private bool InjectIntoInteractableBaseGroup(InteractableBase target) { return uiAndSignsRuntime.InjectIntoInteractableBaseGroup(target); }
        private bool InjectIntoInteractableBaseGroup_UIAndSigns(InteractableBase target) { return uiAndSignsRuntime.InjectIntoInteractableBaseGroup_UIAndSigns(target); }

        private void CreateRescueTeleportBubble()
        {
            uiAndSignsRuntime.CreateRescueTeleportBubble_UIAndSigns();
        }

        private void TryCreateArenaDifficultyEntryPoint()
        {
            uiAndSignsRuntime.TryCreateArenaDifficultyEntryPoint_UIAndSigns();
        }

        private void TryCreateArenaDifficultyEntryPoint(Vector3 position)
        {
            uiAndSignsRuntime.TryCreateArenaDifficultyEntryPoint_UIAndSigns(position);
        }

        private IEnumerator EnsureArenaEntryPointCreated()
        {
            return uiAndSignsRuntime.EnsureArenaEntryPointCreated_UIAndSigns();
        }

    }
}
