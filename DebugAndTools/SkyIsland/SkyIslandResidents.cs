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
        private static readonly string[] Markers = { "POI_B", "EnemySpawn_B", "POI_A", "POI_D", "POI_F", "POI_H" };
        private static readonly string[] Names = { "晴禾", "苇白", "浮舟", "眠苔", "折翎", "无声钟守" };

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
                try { await SpawnOneAsync(Ids[i], Markers[i], Names[i]); }
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
