// ============================================================================
// CourierNPC.cs - 快递员 NPC 宿主兼容入口
// ============================================================================

using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private GameObject courierNPCInstance
        {
            get { return courierNpcRuntime != null ? courierNpcRuntime.CourierNPCInstance : null; }
        }

        private CourierNPCController courierController
        {
            get { return courierNpcRuntime != null ? courierNpcRuntime.CourierController : null; }
        }

        private static GameObject courierPrefab
        {
            get { return CourierNpcRuntimeModule.CourierPrefab; }
        }

        private bool LoadCourierAssetBundle()
        {
            return courierNpcRuntime != null && courierNpcRuntime.LoadCourierAssetBundle();
        }

        private void AddCourierInteraction(GameObject courier)
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.AddCourierInteraction(courier);
            }
        }

        public void SpawnCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.SpawnCourierNPC();
            }
        }

        public void DestroyCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.DestroyCourierNPC();
            }
        }

        public void NotifyCourierBossFightStart()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossFightStart();
            }
        }

        public void NotifyCourierBossFightEnd()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossFightEnd();
            }
        }

        public void NotifyCourierNoBoss(bool noBoss)
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierNoBoss(noBoss);
            }
        }

        public void NotifyCourierBossRushCompleted()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossRushCompleted();
            }
        }

        public void TeleportToCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.TeleportToCourierNPC();
            }
        }
    }
}
