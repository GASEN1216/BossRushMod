using System;
using System.Collections.Generic;
using System.Text;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>原生 LootNet 同步库存；这里只复制 Mod 动态创建的容器外壳。</summary>
    internal sealed class BossRushCoopWorldLoot : IDisposable
    {
        [Serializable] private sealed class BoxState
        {
            public string id, scene, name;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool active, inspect;
            public int capacity;
        }
        [Serializable] private sealed class Snapshot
        {
            public int generation;
            public long revision;
            public BoxState[] boxes;
        }
        [Serializable] private sealed class SnapshotPart
        {
            public int generation, part, count;
            public long revision;
            public string data;
        }
        private const int PartCharacters = 12000;
        private string[] incoming;
        private long incomingRevision;
        private int incomingCount;
        private sealed class Box
        {
            internal string Id;
            internal InteractableLootbox Value;
            internal bool Owned;
        }
        private readonly BossRushCoopTransport transport;
        private readonly IDisposable subscription;
        private readonly Dictionary<int, Box> boxes = new Dictionary<int, Box>();
        private readonly Dictionary<string, float> waiting = new Dictionary<string, float>(StringComparer.Ordinal);
        private Snapshot pending;
        private long revision;
        private float nextTick;

        internal BossRushCoopWorldLoot(BossRushCoopTransport transport)
        {
            this.transport = transport;
            subscription = BossRushCoop.Register("world-loot", OnSnapshot);
        }
        private static int Key(Vector3 p)
        {
            p *= 10f;
            return new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z)).GetHashCode();
        }
        internal bool Register(InteractableLootbox box)
        {
            if (!BossRushCoop.IsConnected || box == null) return false;
            int key = Key(box.transform.position);
            Box existing;
            if (boxes.TryGetValue(key, out existing) && existing.Value != null)
            {
                if (existing.Value == box) return transport.RegisterLoot(box);
                // 地图稍晚生成同一箱子时，替换自动补的空壳；库存仍从房主按原生位置键读取。
                if (!existing.Owned && BossRushCoop.IsHost)
                {
                    // 原生键按 0.1m 量化。先消除重叠键，再登记，否则会覆盖旧箱库存。
                    Vector3 origin = box.transform.position;
                    bool found = false;
                    for (int offset = 1; offset <= boxes.Count + 1; offset++)
                    {
                        Vector3 candidate = origin + new Vector3(offset * 0.2f, 0f, 0f);
                        int candidateKey = Key(candidate);
                        Box occupied;
                        if (boxes.TryGetValue(candidateKey, out occupied) && occupied.Value != null) continue;
                        box.transform.position = candidate; key = candidateKey; found = true; break;
                    }
                    if (!found) return false;
                    // Loader 可能在 Mod 注册前已覆盖旧位置映射，移动新箱后恢复旧箱映射。
                    if (!transport.RegisterLoot(existing.Value)) return false;
                    existing = null;
                }
            }
            if (box.Inventory == null) return false;
            if (existing != null && existing.Owned && existing.Value != null)
            {
                // 官方位置缓存可能让晚生的 authored 箱引用空壳 inventory；独立后才能安全销毁空壳。
                if (box.Inventory == existing.Value.Inventory && !InteractableLootboxInventoryHelper.EnsureLocalInventory(box)) return false;
                // 原生 Register 按 inventory 复用 entry。先注销旧实例，否则其 OnDestroy 可能删掉新映射。
                if (!transport.UnregisterLoot(existing.Value)) return false;
            }
            if (!transport.RegisterLoot(box)) return false;
            if (existing != null && existing.Owned && existing.Value != null) UnityEngine.Object.Destroy(existing.Value.gameObject);
            boxes[key] = new Box { Id = BossRushCoop.RunId + ":" + BossRushCoop.SceneGeneration + ":" + key, Value = box };
            return true;
        }
        internal void Tick()
        {
            if (!BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;
            if (BossRushCoop.IsHost)
            {
                var values = new List<BoxState>();
                var removed = new List<int>();
                foreach (var pair in boxes)
                {
                    InteractableLootbox box = pair.Value.Value;
                    if (box == null || !box.gameObject.scene.isLoaded) { removed.Add(pair.Key); continue; }
                    values.Add(new BoxState { id = pair.Value.Id, scene = box.gameObject.scene.name, name = box.name,
                        position = box.transform.position, rotation = box.transform.rotation, scale = box.transform.lossyScale,
                        active = box.gameObject.activeInHierarchy, inspect = box.needInspect, capacity = box.Inventory.Capacity });
                }
                foreach (int key in removed) boxes.Remove(key);
                PublishSnapshot(values.ToArray());
            }
            else ApplyPending();
        }
        private void PublishSnapshot(BoxState[] values)
        {
            long version = ++revision;
            string json = BossRushCoopJson.ToJson(new Snapshot { generation = BossRushCoop.SceneGeneration, revision = version, boxes = values });
            var parts = new List<string>();
            for (int offset = 0; offset < json.Length;)
            {
                int length = Math.Min(PartCharacters, json.Length - offset);
                if (offset + length < json.Length && char.IsHighSurrogate(json[offset + length - 1])
                    && char.IsLowSurrogate(json[offset + length])) length--;
                parts.Add(json.Substring(offset, length)); offset += length;
            }
            for (int i = 0; i < parts.Count; i++)
                BossRushCoop.Broadcast("world-loot", new SnapshotPart { generation = BossRushCoop.SceneGeneration,
                    revision = version, part = i, count = parts.Count, data = parts[i] });
        }
        private void OnSnapshot(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            SnapshotPart part = message.Read<SnapshotPart>();
            if (part == null || part.generation != BossRushCoop.SceneGeneration || part.revision <= revision
                || part.revision < incomingRevision || part.count < 1 || part.count > 512
                || part.part < 0 || part.part >= part.count || part.data == null || part.data.Length > PartCharacters) return;
            if (incoming == null || incomingRevision != part.revision)
            { incoming = new string[part.count]; incomingRevision = part.revision; incomingCount = 0; }
            if (incoming.Length != part.count || incoming[part.part] != null) return;
            incoming[part.part] = part.data; incomingCount++;
            if (incomingCount != incoming.Length) return;
            // 收齐同一版本才替换现态，单个分片不能把其它共享箱子误判为已删除。
            var json = new StringBuilder();
            foreach (string text in incoming) json.Append(text);
            incoming = null; incomingCount = 0;
            Snapshot value = BossRushCoopJson.FromJson<Snapshot>(json.ToString());
            if (value == null || value.generation != BossRushCoop.SceneGeneration || value.revision <= revision
                || value.revision != part.revision || value.boxes == null || value.boxes.Length > 2048) return;
            revision = value.revision; pending = value;
        }
        private void ApplyPending()
        {
            // 新代次可能先于本机实际到达；保留完整快照，等对应 Core 绑定后再操作箱子。
            if (pending == null || pending.generation != BossRushCoop.SceneGeneration || !BossRushCoop.HasBoundScene) return;
            var live = new HashSet<int>();
            foreach (var state in pending.boxes)
            {
                if (state == null || string.IsNullOrEmpty(state.id) || state.id.Length > 128 || state.capacity < 0 || state.capacity > 4096) continue;
                int key = Key(state.position); live.Add(key);
                Box box;
                if (!boxes.TryGetValue(key, out box) || box.Value == null)
                {
                    float since;
                    if (!waiting.TryGetValue(state.id, out since)) { waiting[state.id] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - since < 2f) continue;
                    Scene scene = SceneManager.GetSceneByName(state.scene);
                    if (!scene.IsValid() || !scene.isLoaded) continue;
                    var prefab = GameplayDataSettings.Prefabs != null ? GameplayDataSettings.Prefabs.LootBoxPrefab : null;
                    if (prefab == null) continue;
                    var staging = new GameObject("BossRushCoopLootStaging");
                    staging.SetActive(false);
                    InteractableLootbox replica = null;
                    try
                    {
                        replica = UnityEngine.Object.Instantiate(prefab, staging.transform);
                        LootBoxLoader loader = replica.GetComponent<LootBoxLoader>();
                        if (loader != null) UnityEngine.Object.DestroyImmediate(loader);
                        replica.transform.position = state.position; replica.transform.rotation = state.rotation;
                        replica.transform.SetParent(null, true);
                        SceneManager.MoveGameObjectToScene(replica.gameObject, scene);
                        if (!InteractableLootboxInventoryHelper.EnsureLocalInventory(replica, Math.Max(1, state.capacity)))
                        { UnityEngine.Object.Destroy(replica.gameObject); continue; }
                        replica.Inventory.SetCapacity(Math.Max(1, state.capacity));
                        if (!Register(replica)) { UnityEngine.Object.Destroy(replica.gameObject); continue; }
                        box = boxes[key]; box.Owned = true;
                    }
                    finally { UnityEngine.Object.Destroy(staging); }
                }
                box.Id = state.id;
                box.Value.name = state.name;
                box.Value.transform.position = state.position;
                box.Value.transform.rotation = state.rotation;
                if (box.Owned) box.Value.transform.localScale = state.scale;
                box.Value.needInspect = state.inspect;
                box.Value.Inventory.NeedInspection = state.inspect;
                box.Value.gameObject.SetActive(state.active);
            }
            var obsolete = new List<int>();
            foreach (var pair in boxes)
                if (!live.Contains(pair.Key) && pair.Value.Owned)
                { if (pair.Value.Value != null) UnityEngine.Object.Destroy(pair.Value.Value.gameObject); obsolete.Add(pair.Key); }
            foreach (int key in obsolete) boxes.Remove(key);
        }
        internal void Reset()
        {
            foreach (Box box in boxes.Values) if (box.Owned && box.Value != null) UnityEngine.Object.Destroy(box.Value.gameObject);
            boxes.Clear(); waiting.Clear(); pending = null; revision = 0; nextTick = 0f;
            incoming = null; incomingRevision = 0; incomingCount = 0;
        }
        internal void ChangeGeneration()
        {
            // SceneInfo 更新可能晚于本地图建箱。保留实际尚存的箱子，不能按消息到达顺序丢掉新地图登记。
            pending = null; waiting.Clear(); revision = 0; nextTick = 0f;
            incoming = null; incomingRevision = 0; incomingCount = 0;
            foreach (var pair in boxes)
                pair.Value.Id = BossRushCoop.RunId + ":" + BossRushCoop.SceneGeneration + ":" + pair.Key;
        }
        public void Dispose() { subscription.Dispose(); Reset(); }
    }
}
