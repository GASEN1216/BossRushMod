using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void CreateRescueTeleportBubble()
        {
            CreateRescueTeleportBubble_UIAndSigns();
        }

        private void TryCreateArenaDifficultyEntryPoint()
        {
            TryCreateArenaDifficultyEntryPoint_UIAndSigns();
        }

        private void TryCreateArenaDifficultyEntryPoint(Vector3 position)
        {
            TryCreateArenaDifficultyEntryPoint_UIAndSigns(position);
        }

        private IEnumerator EnsureArenaEntryPointCreated()
        {
            return EnsureArenaEntryPointCreated_UIAndSigns();
        }

    }
}
