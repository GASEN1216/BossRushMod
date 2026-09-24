using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        public int GetZombieModeSelectedPreparationDuration(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.GetZombieModeSelectedPreparationDuration(runId)
                : Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds);
        }

        public void OpenZombieModePreparationDurationEditor(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.OpenZombieModePreparationDurationEditor(runId);
        }

        public void SetZombieModePreparationDuration(int runId, int seconds)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SetZombieModePreparationDuration(runId, seconds);
        }
    }
}
