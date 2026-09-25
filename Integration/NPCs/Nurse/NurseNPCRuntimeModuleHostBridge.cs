using UnityEngine;

namespace BossRush
{
    /// <summary>护士旧入口与跨 NPC 只读查询；运行状态由护士模块持有。</summary>
    public partial class ModBehaviour
    {
        private NurseNpcRuntimeModule nurseNpcRuntime;

        private GameObject nurseNPCInstance { get { return nurseNpcRuntime != null ? nurseNpcRuntime.NurseNPCInstance : null; } }
        private NurseNPCController nurseController { get { return GetNurseController(); } }
        internal GameObject NurseCourierNpcInstanceForRuntime { get { return courierNPCInstance; } }
        internal GameObject NurseGoblinNpcInstanceForRuntime { get { return goblinNPCInstance; } }

        public void SpawnNurseNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        { if (nurseNpcRuntime != null) nurseNpcRuntime.SpawnNurseNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn); }
        public void DestroyNurseNPC() { if (nurseNpcRuntime != null) nurseNpcRuntime.DestroyNurseNPC(); }
        public NurseNPCController GetNurseController() { return nurseNpcRuntime != null ? nurseNpcRuntime.GetNurseController() : null; }
        public bool IsNurseSpawned() { return nurseNpcRuntime != null && nurseNpcRuntime.IsNurseSpawned(); }
    }
}
