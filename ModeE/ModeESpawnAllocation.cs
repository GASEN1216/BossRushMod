using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeERuntimeModule
    {
        // 保留 E 内部与旧宿主入口；E/F 的分配与场景缓存只由共享服务持有。
        internal Dictionary<Teams, List<Vector3>> modeESpawnAllocation { get { return spawnPreparation.SpawnAllocation; } }
        internal Vector3[] modeECachedSpawnerPositions { get { return spawnPreparation.CachedSpawnerPositions; } }
        internal string modeECachedSpawnerSceneName { get { return spawnPreparation.CachedSpawnerSceneName; } }
        internal Vector3[] GetModeEFlattenedSpawnPoints() { return spawnPreparation.GetModeEFlattenedSpawnPoints(); }
        internal void AllocateSpawnPoints() { spawnPreparation.AllocateSpawnPoints(); }
        internal void TeleportPlayerToSafePosition() { spawnPreparation.TeleportPlayerToSafePosition(); }
        public void PreCacheMapSpawnerPositions() { spawnPreparation.PreCacheMapSpawnerPositions(); }
    }
}
