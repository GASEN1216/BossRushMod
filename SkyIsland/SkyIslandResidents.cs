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
        /// <summary>居民侧的头顶气泡预算。敌人侧另有一个实例，两边加起来同屏最多两个气泡。</summary>
        private SkyIslandChatter chatter;
        /// <summary>气泡推进的节流间隔（秒）。冷却本身是 15–25 秒，用不着每帧看。</summary>
        private const float ChatterTickInterval = 0.25f;
        /// <summary>气泡挂多高：与官方血条名同高，免得两块字互相压。</summary>
        private const float ResidentBubbleHeight = 2.2f;
        private float nextChatterTick;
        private bool dialogueHeld;
        private bool? namesChinese;

        private static readonly string[] Ids = {
            "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling", "sky_bellkeeper"
        };
        // 全部锚点已在资源加载时校验地面与胶囊。双居民分占集市两侧现有标记。
        // 折翎站在自己那一战的锚点上（`World.json` 里 Zheling 遭遇的 EnemySpawn_F）：布局 v2 把 POI_F 与它拉开到 63 m，
        // 在他面前选「挑战」时本人消失、战斗体却刷在两屏之外（可玩性评估 R-1）。守卫按 World.json 核对两者同点。
        private static readonly string[] Markers = { "POI_B", "EnemySpawn_B", "POI_A", "POI_D", "EnemySpawn_F", "POI_H" };

        /// <summary>
        /// 这位居民站在哪个地标。剧情面板拿它去取「他家那一区」的插图当主视觉——
        /// 居民面板此前只有立绘、没有插图，主视觉退化成一条空带。
        /// 表外的 id 返回 null，面板退回无插图布局（fail-open）。
        /// </summary>
        internal static string MarkerOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Ids.Length && i < Markers.Length; i++)
                if (string.Equals(Ids[i], id, System.StringComparison.Ordinal)) return Markers[i];
            return null;
        }
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
            chatter = new SkyIslandChatter(SkyIslandChatter.ResidentCooldownMin,
                SkyIslandChatter.ResidentCooldownMax, IsValid);
            PermanentDuckNpcModule.RegisterAllAffinityConfigs();
            SpawnAllAsync().Forget();
        }

        /// <summary>
        /// 居民的「活人感」推进：头顶气泡 + 说话时停下脚步。由会话每帧调，自己节流到
        /// <see cref="ChatterTickInterval"/>。
        ///
        /// 【停下脚步按状态推，不按事件推】官方对话是异步的，取消路径有五条（说完、选「先这样」、
        /// 死亡、切图、战斗打断）。逐条挂事件必漏，所以这里每次推进比一次「此刻在不在对话/暂停里」，
        /// 变了才动——幂等，而且漏不掉任何一条退出路径。
        /// </summary>
        /// <param name="story">当前剧情存档；台词随进度变化。为 null 时按早期版本说。</param>
        internal void Tick(Vector3 playerPosition, SkyIslandStoryData story)
        {
            if (disposed || root == null || chatter == null) return;
            RefreshLocalizedNames();
            // 暂停门与对话门同时也是气泡的静音门（SkyIslandChatter.Silenced 再判一次，这里只管脚步）。
            bool busy = DialogueManager.IsDialogueActive || BossRushUI.IsGamePaused();
            if (busy != dialogueHeld)
            {
                dialogueHeld = busy;
                ApplyDialogueHold(busy);
            }
            if (busy || Time.time < nextChatterTick) return;
            nextChatterTick = Time.time + ChatterTickInterval;
            // 静音门问一次就够：`Ready` 每位都会再问一遍，六位居民就是六次会话有效性委托。
            if (chatter.Muted) return;
            chatter.PlayerPosition = playerPosition;
            string speakerId = null;
            Transform speaker = null;
            float best = float.MaxValue;
            foreach (KeyValuePair<string, CharacterMainControl> pair in owned)
            {
                CharacterMainControl npc = pair.Value;
                if (npc == null || !npc.gameObject.activeInHierarchy) continue;
                Transform body = npc.transform;
                if (!chatter.Ready(body)) continue;
                // 同时有好几位轮得到时挑最近的：玩家站在谁跟前，就该听见谁说话。
                float distance = (body.position - playerPosition).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                speakerId = pair.Key;
                speaker = body;
            }
            if (speaker == null) return;
            chatter.TrySay(speaker, ResidentBubbleHeight, SkyIslandChatterLines.Resident(speakerId, story));
        }

        /// <summary>只在语言变化时更新现有名字登记，含隐藏居民及本会话持有的两位永久居民。</summary>
        private void RefreshLocalizedNames()
        {
            bool chinese = L10n.IsChinese;
            if (namesChinese == chinese) return;
            foreach (KeyValuePair<string, CharacterMainControl> pair in owned)
            {
                if (pair.Value == null) continue;
                NPCNameTagHelper.UpdateOriginalHealthBarDisplayName(
                    pair.Value.transform, SkyIslandWorldStory.ResidentName(pair.Key));
            }
            namesChinese = chinese;
        }

        /// <summary>
        /// 对话 / 暂停期间让会走动的居民停下；结束后隔一秒再走（口径同快递员阿稳退出服务后的延迟恢复）。
        /// 站桩的居民（折翎、无声钟守）没有这个组件，整条早退。
        /// </summary>
        private void ApplyDialogueHold(bool hold)
        {
            foreach (KeyValuePair<string, CharacterMainControl> pair in owned)
            {
                CharacterMainControl npc = pair.Value;
                if (npc == null) continue;
                DuckNpcMovement movement = npc.GetComponent<DuckNpcMovement>();
                if (movement == null) continue;
                if (hold) movement.HoldForDialogue();
                else movement.ReleaseFromDialogue(1f);
            }
        }

        /// <summary>F3 只读：居民这一侧本趟说了几句气泡、此刻有没有气泡挂着。玩法不读它。</summary>
        internal SkyIslandChatter ValidationChatter { get { return chatter; } }

        private bool IsValid()
        {
            return !disposed && root != null && sessionValid != null && sessionValid();
        }

        private async UniTaskVoid SpawnAllAsync()
        {
            try
            {
                for (int i = 0; i < Ids.Length && IsValid(); i++)
                {
                    try { await SpawnOneAsync(Ids[i], Markers[i], SkyIslandWorldStory.ResidentName(Ids[i])); }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandResidents] " + Ids[i] + " 生成失败：" + e.Message); }
                    await UniTask.Yield();
                }
            }
            finally { spawnFinished = true; }
        }

        private bool spawnFinished;
        /// <summary>整队生成流程已经跑完（成功与否都算）。官方任务给予者据此判断谁缺席、要不要挂装置兜底。只读。</summary>
        internal bool SpawnFinished { get { return spawnFinished; } }

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
                // 生成可能跨帧；取落地时的语言，避免 await 期间切语言后新居民仍登记旧名字。
                displayName = SkyIslandWorldStory.ResidentName(id);
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
                SkyIslandResidentInteractable.AttachPermanent(npc, id);
                // 发任务的居民再挂一个官方任务给予者（同组、不抢交互位）；官方任务符号全在 SkyIslandOfficialQuestGivers。
                SkyIslandOfficialQuestGivers.AttachResident(relationship.transform, group, id);
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
            SkyIslandResidentInteractable interaction = child.AddComponent<SkyIslandResidentInteractable>();
            interaction.Bind(id, displayName, npc.transform, onTalk, IsValid);
            List<InteractableBase> standaloneGroup = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(interaction, "[SkyIslandResidents]");
            SkyIslandOfficialQuestGivers.AttachResident(interaction.transform, standaloneGroup, id);
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

        /// <summary>现有居民交互组的宿主，供任务 UI 延迟就绪时补接；不另造碰撞体。</summary>
        internal InteractableBase FindQuestInteractionOwner(string id)
        {
            CharacterMainControl npc;
            if (!owned.TryGetValue(id, out npc) || npc == null) return null;
            PermanentDuckNpcInteractable relationship = npc.GetComponentInChildren<PermanentDuckNpcInteractable>(true);
            return relationship != null ? (InteractableBase)relationship : npc.GetComponentInChildren<SkyIslandResidentInteractable>(true);
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

        /// <summary>
        /// 剧情体隐藏不会触发死亡；战斗实例必须由遭遇 owner 单独生成。
        ///
        /// **值没变就整条早退**（可玩性评估 R-9 的「剧情体显隐每帧两次无变化检查」）：会话 `Update`
        /// 每帧对折翎与钟守各调一次，而这两个目标状态在整趟出击里只会翻转个位数次。旧写法每帧都要做
        /// HashSet 增删 + Dictionary 查（各一次字符串散列）再无条件 `SetActive`，即使状态一个字没变。
        /// <see cref="hidden"/> 是**唯一事实源**（异步生成落地时由 <c>SpawnOneAsync</c> 照它补一次
        /// `SetActive`），所以按它早退不会漏掉「先 SetVisible、后生成」那条时序。
        /// 口径同 <see cref="SkyIslandLighting"/> 的写入阈值与 <see cref="SkyIslandMapMarkers.Apply"/>
        /// 的早退（AGENTS 4.12：不做每帧无效重工作）。
        /// </summary>
        internal void SetVisible(string id, bool visible)
        {
            // hidden 里 == 不可见。两者不等即「已经是目标状态」，什么都不用做。
            if (hidden.Contains(id) != visible) return;
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
            if (chatter != null) chatter.Clear();
            chatter = null;
            onTalk = null;
            sessionValid = null;
            navigation = null;
            root = null;
        }
    }
}
