using System;
using System.Collections.Generic;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>COMPAT：天空岛居民只由本次会话持有；关系事实复用全局永久 NPC 框架。</summary>
    internal sealed class SkyIslandResidents : IDisposable
    {
        private readonly Dictionary<string, CharacterMainControl> owned =
            new Dictionary<string, CharacterMainControl>(StringComparer.Ordinal);
        private readonly HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
        private GameObject root;
        private ArenaPrototypeNavigation navigation;
        private Func<bool> sessionValid;
        private Action<string, Transform> onTalk;
        private bool disposed;

        private static readonly string[] Ids = {
            "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling", "sky_bellkeeper"
        };
        // 全部锚点已在资源加载时校验地面与胶囊。双居民分占集市两侧现有标记。
        // 折翎站在自己那一战的锚点上（`World.json` 里 Zheling 遭遇的 EnemySpawn_F）：布局 v2 把 POI_F 与它拉开到 63 m，
        // 在他面前选「挑战」时本人消失、战斗体却刷在两屏之外（可玩性评估 R-1）。守卫按 World.json 核对两者同点。
        private static readonly string[] Markers = { "POI_B", "EnemySpawn_B", "POI_A", "POI_D", "EnemySpawn_F", "POI_H" };
        // 显示名不再在这里写第二份：与剧情面板共用 SkyIslandWorldStory.ResidentName 的中英对照，
        // 否则英文玩家在交互提示与血条上仍会看到中文名。

        internal void Start(GameObject sceneRoot, ArenaPrototypeNavigation graph,
            Func<bool> isSessionValid, Action<string, Transform> talk)
        {
            if (root != null || disposed) throw new InvalidOperationException("天空岛居民 owner 已启动或关闭");
            root = sceneRoot;
            navigation = graph;
            sessionValid = isSessionValid;
            onTalk = talk;
            PermanentDuckNpcModule.RegisterAllAffinityConfigs();
            SpawnAllAsync().Forget();
        }

        private bool IsValid()
        {
            return !disposed && root != null && sessionValid != null && sessionValid();
        }

        private async UniTaskVoid SpawnAllAsync()
        {
            for (int i = 0; i < Ids.Length && IsValid(); i++)
            {
                try { await SpawnOneAsync(Ids[i], Markers[i], SkyIslandWorldStory.ResidentName(Ids[i])); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandResidents] " + Ids[i] + " 生成失败：" + e.Message); }
                await UniTask.Yield();
            }
        }

        private async UniTask SpawnOneAsync(string id, string markerName, string displayName)
        {
            DuckNpcBlueprint blueprint;
            if (!DuckNpcRegistry.TryGet(id, out blueprint) || blueprint == null)
                throw new InvalidOperationException("缺少居民蓝图 " + id);
            if (blueprint.isPermanent && (AffinityManager.IsMarriedToPlayer(id)
                || PermanentDuckNpcRegistry.GetInstance(id) != null)) return;
            Transform marker = root.transform.Find(markerName);
            if (marker == null) throw new InvalidOperationException("缺少居民落点 " + markerName);
            RaycastHit hit;
            if (!Physics.Raycast(marker.position + Vector3.up * 2f, Vector3.down, out hit, 5f,
                GameplayDataSettings.Layers.groundLayerMask.value, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("居民落点没有地面 " + markerName);
            Vector3 home = hit.point + Vector3.up * 0.15f;
            CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(blueprint, home, Vector3.back);
            if (npc == null) throw new InvalidOperationException("官方角色生成失败 " + id);
            bool retained = false;
            try
            {
                // await 期间离岛、结婚或其他 owner 先登记时，只回收本请求刚创建的对象。
                if (!IsValid() || (blueprint.isPermanent && (AffinityManager.IsMarriedToPlayer(id)
                    || PermanentDuckNpcRegistry.GetInstance(id) != null))) return;
                if (blueprint.isPermanent)
                {
                    NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn(id, "[SkyIslandResidents]");
                    PermanentDuckNpcModule.AttachPermanentParts(npc, blueprint, home);
                    PermanentDuckNpcRegistry.RegisterInstance(id, npc);
                }
                else
                {
                    NPCNameTagHelper.RegisterOriginalHealthBarName(npc.transform, displayName, 2.2f, "[SkyIslandResidents]");
                    if (blueprint.canWander)
                        npc.gameObject.AddComponent<DuckNpcMovement>().Bind(npc, home, blueprint.wanderRadius);
                }
                foreach (Seeker seeker in npc.GetComponentsInChildren<Seeker>(true))
                {
                    seeker.CancelCurrentPathRequest();
                    seeker.graphMask = navigation.Mask;
                }
                AttachStoryInteraction(npc, id, displayName);
                owned.Add(id, npc);
                retained = true;
                if (hidden.Contains(id)) npc.gameObject.SetActive(false);
                Debug.Log("[SkyIslandResidents] SPAWN_PASS id=" + id + " home=" + home);
            }
            finally
            {
                if (!retained)
                {
                    if (blueprint.isPermanent && PermanentDuckNpcRegistry.GetInstance(id) == npc)
                        PermanentDuckNpcRegistry.UnregisterInstance(id);
                    DuckNpcSpawner.Despawn(npc);
                }
            }
        }

        private void AttachStoryInteraction(CharacterMainControl npc, string id, string displayName)
        {
            PermanentDuckNpcInteractable relationship = npc.GetComponentInChildren<PermanentDuckNpcInteractable>();
            if (relationship != null)
            {
                List<InteractableBase> group = NPCInteractionGroupHelper.GetOrCreateGroupList(relationship, "[SkyIslandResidents]");
                NPCInteractionGroupHelper.AddSubInteractable(relationship.transform, "IslandStoryOption", group,
                    (SkyIslandResidentInteractable component) => component.Bind(id, displayName, npc.transform, onTalk, IsValid));
                return;
            }
            GameObject child = new GameObject("IslandStoryInteractRoot");
            child.SetActive(false);
            child.transform.SetParent(npc.transform, false);
            CapsuleCollider capsule = child.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.6f;
            capsule.center = Vector3.up;
            foreach (Collider other in npc.GetComponentsInChildren<Collider>(true))
                if (other != capsule) Physics.IgnoreCollision(capsule, other, true);
            child.AddComponent<SkyIslandResidentInteractable>().Bind(id, displayName, npc.transform, onTalk, IsValid);
            child.SetActive(true);
        }

        /// <summary>本次会话实际持有的居民数。只读，给 F3 验收用。</summary>
        internal int SpawnedCount { get { return owned.Count; } }

        /// <summary>
        /// 全部居民身份，含**本会话没有持有**的那些。
        /// 已婚居民由婚姻系统接管、不再上岛（<see cref="SpawnOneAsync"/> 直接跳过），
        /// 因此「没生成」不等于「出错」，验收必须能分辨这两种情况。
        /// </summary>
        internal static string[] AllIds { get { return (string[])Ids.Clone(); } }

        /// <summary>某位居民本会话是否真的在岛上。只读。</summary>
        internal bool IsSpawned(string id)
        {
            CharacterMainControl npc;
            return owned.TryGetValue(id, out npc) && npc != null;
        }

        /// <summary>
        /// 某位在岛居民身上有没有「聊聊航路」交互体（含被剧情隐藏的）。只读，给 F3 验收按人逐个核对。
        /// 居民由官方 CharacterCreator 生成、不挂在地形根下，按根节点扫交互体一个都扫不到。
        /// </summary>
        internal bool HasTalkInteraction(string id)
        {
            CharacterMainControl npc;
            return owned.TryGetValue(id, out npc) && npc != null
                && npc.GetComponentInChildren<SkyIslandResidentInteractable>(true) != null;
        }

        /// <summary>某位居民此刻是否被剧情隐藏（折翎战败后不再露面）。只读。</summary>
        internal bool IsHidden(string id) { return hidden.Contains(id); }

        /// <summary>把在岛且未隐藏的居民身上的交互体追加进 <paramref name="into"/>。只读，给 F3 交互竞争用例补上居民。</summary>
        internal void CollectActiveInteractables(List<InteractableBase> into)
        {
            if (into == null) return;
            foreach (KeyValuePair<string, CharacterMainControl> pair in owned)
            {
                if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy) continue;
                foreach (InteractableBase interactable in pair.Value.GetComponentsInChildren<InteractableBase>(false))
                    if (interactable != null && interactable.isActiveAndEnabled) into.Add(interactable);
            }
        }

        /// <summary>剧情体隐藏不会触发死亡；战斗实例必须由遭遇 owner 单独生成。</summary>
        internal void SetVisible(string id, bool visible)
        {
            if (visible) hidden.Remove(id); else hidden.Add(id);
            CharacterMainControl npc;
            if (owned.TryGetValue(id, out npc) && npc != null) npc.gameObject.SetActive(visible);
        }

        internal static void ApplyBattleFace(CharacterMainControl npc, string id)
        {
            DuckNpcBlueprint blueprint;
            CustomFaceSettingData face;
            if (npc != null && npc.characterModel != null && DuckNpcRegistry.TryGet(id, out blueprint)
                && DuckNpcRegistry.TryResolveFace(blueprint, out face)) npc.characterModel.SetFaceFromData(face);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (KeyValuePair<string, CharacterMainControl> pair in owned)
            {
                CharacterMainControl npc = pair.Value;
                // 婚后角色由教堂/跟随服务接管，不能在离岛时销毁配偶的新 owner。
                if (AffinityManager.IsMarriedToPlayer(pair.Key)) continue;
                if (PermanentDuckNpcRegistry.GetInstance(pair.Key) == npc)
                    PermanentDuckNpcRegistry.UnregisterInstance(pair.Key);
                DuckNpcSpawner.Despawn(npc);
            }
            owned.Clear();
            hidden.Clear();
            onTalk = null;
            sessionValid = null;
            navigation = null;
            root = null;
        }
    }
}
