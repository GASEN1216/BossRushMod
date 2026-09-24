using UnityEngine;

namespace BossRush
{
    /// <summary>保留旧 ModBehaviour 入口，并转发到唯一的哥布林运行时模块。</summary>
    public partial class ModBehaviour
    {
        private GoblinNpcRuntimeModule goblinNpcRuntime;

        internal GoblinNpcRuntimeModule GoblinNpcRuntime
        {
            get { return goblinNpcRuntime; }
        }

        // 旧 partial 与 UI 仍通过这些同名只读属性读取运行状态；实例只由模块持有。
        private GameObject goblinNPCInstance
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinNPCInstance : null; }
        }

        private GoblinNPCController goblinController
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinController : null; }
        }

        // ZombieMode 复用同一资源缓存，保留原来的私有宿主调用点。
        private GameObject goblinPrefab
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinPrefab : null; }
        }

        private bool LoadGoblinAssetBundle()
        {
            return goblinNpcRuntime != null && goblinNpcRuntime.LoadGoblinAssetBundle();
        }

        internal GameObject GoblinCourierNpcInstanceForRuntime
        {
            get { return courierNPCInstance; }
        }

        public void SpawnGoblinNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.SpawnGoblinNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn);
            }
        }

        public void DestroyGoblinNPC()
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.DestroyGoblinNPC();
            }
        }

        public void SummonGoblin()
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.SummonGoblin();
            }
        }

        public GoblinNPCController GetGoblinController()
        {
            return goblinNpcRuntime != null ? goblinNpcRuntime.GetGoblinController() : null;
        }
    }
}
