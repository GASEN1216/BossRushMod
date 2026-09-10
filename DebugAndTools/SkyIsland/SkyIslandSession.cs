using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.MiniMaps.UI;
using Duckov.UI;
using Duckov.Utilities;
using Pathfinding;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>COMPAT / WIRE+ / SCHEMA+：官方独立出击关卡的天空岛旅程 owner。</summary>
    internal sealed class SkyIslandSession : MonoBehaviour, IInitializedQueryHandler
    {
        private readonly Vector3 origin = Vector3.zero;
        private readonly List<Transform> enemyMarkers = new List<Transform>();
        private readonly List<Transform> landmarks = new List<Transform>();
        private readonly List<Transform> searchMarkers = new List<Transform>();
        private readonly HashSet<string> searched = new HashSet<string>();
        private ModBehaviour host;
        private CharacterMainControl player;
        private Health playerHealth;
        private Scene entryScene;
        private Vector3 safePosition;
        private SkyIslandRaidLease lease;
        private TimeOfDayConfig timeOfDayTemplate;
        private SkyIslandStoryService story;
        private SkyIslandWorldStory worldStory;
        private SkyIslandResidents residents;
        private SkyIslandEncounters encounters;
        private SkyIslandContentData content;
        private SkyIslandAmbience ambience;
        private SkyIslandGates gates;
        private SkyIslandScavenging scavenging;
        private SkyIslandServices services;
        private SkyIslandExtractionRings extractionRings;
        private readonly SkyIslandBounty bounty = new SkyIslandBounty();
        private readonly SkyIslandMapFog mapFog = new SkyIslandMapFog();
        // 已给委托记过账的遭遇 id：清场回调会重投，记账只能算一次。
        private readonly HashSet<string> bountyCredited = new HashSet<string>(StringComparer.Ordinal);
        // 本次出击的随机种子：搜刮点、Boss 战利品与委托奖励共用，离开再回来同一份内容。
        private int raidSeed;
        private string assemblyError;
        private bool navigationReady, returning, deathPending, loadStarted, returnRequested;
        private SkyIslandRendering rendering;
        private SkyIslandLighting lighting;
        private ArenaPrototypeNavigation navigation;
        private IEnumerator<Progress> scan;
        private GameObject root;
        // 返航期间的输入封锁源：必须是岛场景内的临时对象，随场景卸载自动失效（见 BlockInputForReturn）。
        private GameObject returnInputBlock;
        private SkyIslandHud hud;
        private Transform playerSpawn, exitMarker, bellExit;
        private CharacterMainControl enemy;
        private CharacterRandomPreset enemyPreset;
        private Seeker probe;
        private Action<string, bool> report;
        // 撤离圈半径与停留秒数：唯一定义点，地图说明与 HUD 倒计时共用。
        private const float ExtractionRadius = 2.5f;
        private const float ExtractionHold = 3f;
        /// <summary>剧情面板的「战斗静默」半径：这个距离内还有活着的敌人就不许开面板。见 <see cref="CanOpenStoryPanel"/>。</summary>
        private const float StoryPanelQuietRadius = 35f;
        /// <summary>
        /// 存档落盘的「战斗静默」半径。比剧情面板那道门放宽一档：面板挡的是「拿暂停键」，
        /// 半径小一点更严格；落盘挡的是「在交火帧写盘掉帧」，只要手边这一段清干净就该放行，
        /// 否则已接受的事实会一直攒到离岛才写。
        /// </summary>
        private const float SaveQuietRadius = 45f;
        private int groundMask, searchCount, nextLandmark;
        private bool subscribed, closed, ready, moved, spawning, pathCompleted, pathValid;
        private float enteredAt, extractionStarted = -1, nextGroundCheck, airborneSince = -1, nextHud;

        /// <summary>三处「还没就绪」提示共用同一句文案，避免中英两份各写三遍再各漂一遍。</summary>
        private static string WaitForReady
        { get { return L10n.T("请等待天空岛就绪", "Wait for the Sky Islands to finish loading"); } }

        internal static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = null;
            if (owner == null) { reason = L10n.T("Mod 尚未就绪", "The mod is not ready yet"); return false; }
            if (!SkyIslandRaidLease.IsBundleDeployed())
            {
                reason = L10n.T("缺少天空岛独立出击场景包，请更新 Mod 资源",
                    "The Sky Island raid scene bundle is missing — update the mod's assets");
                return false;
            }
            if (owner.GetComponent<SkyIslandSession>() != null || owner.GetComponent<ArenaPrototypeSession>() != null)
            {
                reason = L10n.T("请先结束当前场景旅程并等待回收",
                    "End the current scene journey and wait for it to be recycled");
                return false;
            }
            if (SkyIslandStorySaveRecovery.IsPending())
            {
                reason = L10n.T("上一段群岛记录仍在保存，请稍后重试",
                    "The previous archipelago record is still saving — try again shortly");
                return false;
            }
            if (F3GameplayValidationRunner.IsRunning)
            { reason = L10n.T("请等待完整玩法验收结束", "Wait for the full gameplay validation to finish"); return false; }
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited)
            {
                reason = L10n.T("请等待场景与玩家初始化完成",
                    "Wait for the scene and the player to finish initialising");
                return false;
            }
            string mode;
            if (owner.ValidationHasActiveMode(out mode))
            { reason = L10n.T("请先结束当前模式：", "End the current mode first: ") + mode; return false; }
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.Health == null || main.Health.IsDead)
            { reason = L10n.T("玩家未就绪", "The player is not ready"); return false; }
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name))
            {
                reason = L10n.T("请先回基地再前往天空岛", "Return to base before departing for the Sky Islands");
                return false;
            }
            if (GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
            { reason = L10n.T("游戏相机尚未就绪", "The game camera is not ready yet"); return false; }
            return true;
        }

        internal static void Enter(ModBehaviour owner, Action<string, bool> status)
        {
            string reason;
            if (!CanEnter(owner, out reason)) { status(reason, true); return; }
            SkyIslandSession session = owner.gameObject.AddComponent<SkyIslandSession>();
            session.host = owner;
            session.report = status;
            session.Subscribe();
            session.StartCoroutine(session.BuildGuarded());
        }

        private void Subscribe()
        {
            if (subscribed) return;
            SceneLoader.onStartedLoadingScene += OnStartedLoading;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
        }

        private IEnumerator BuildGuarded()
        {
            IEnumerator steps = Build();
            while (!closed)
            {
                bool more;
                object current;
                try { more = steps.MoveNext(); current = more ? steps.Current : null; }
                catch (Exception e)
                {
                    CancelPendingInitialization();
                    Status(L10n.T("天空岛创建失败：", "Sky Island setup failed: ") + e.Message, true);
                    Close(true, "build_failed"); yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }

        private IEnumerator Build()
        {
            // 不再复用试验场那块「屏幕正上方的裸文字」：那是原型期的调试文本，
            // 常驻四行长句会一直抢视线焦点，而且 90 px 的框实测装不下（中文 4 行 / 英文 6 行）。
            // 天空岛自己的 HUD 把常驻部分收成右侧一张小卡，区域名改成进出时的一次性大标题。
            hud = new SkyIslandHud(host.transform);
            hud.SetLandingHint(L10n.T("地图键查阅全岛 · 站进撤离环停留 3 秒返航",
                "Map key views the isles · hold 3s inside an extraction ring to return"));
            Status(L10n.T("正在加载晴岚群岛…", "Loading the Qinglan Archipelago…"), false);
            timeOfDayTemplate = LevelConfig.Instance.timeOfDayConfig;
            if (timeOfDayTemplate == null) throw new InvalidOperationException("官方天气配置缺失，无法创建完整关卡");
            content = SkyIslandContent.Load();
            story = new SkyIslandStoryService(); story.Open();
            lease = new SkyIslandRaidLease();
            lease.Prepare(ModBehaviour.GetModPath(), timeOfDayTemplate);
            loadStarted = true;
            lease.BeginLoad();
            float deadline = Time.realtimeSinceStartup + 120;
            while (root == null && lease.Error == null && assemblyError == null && Time.realtimeSinceStartup < deadline)
            {
                // 官方 SceneLoader 遇到「已在切图」或身份未登记时只记一条 LogError 就同步返回：
                // LoadFinished 立刻为 true 而 root 永远不会来，不能空等 120 秒。
                // 此时从未离开基地，按「未起航」清理，不发起多余的回基地加载。
                if (lease.LoadFinished) { loadStarted = false; throw new InvalidOperationException("官方场景加载器拒绝了本次出击，请稍后再试"); }
                yield return null;
            }
            if (assemblyError != null) throw new InvalidOperationException(assemblyError);
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (root == null) throw new TimeoutException("独立天空岛场景未能就绪");
            // sceneLoaded 先于官方 SetActiveScene；协程不能依靠 UniTask continuation 与 Update 的偶然顺序。
            while ((SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null ||
                GameCamera.Instance.renderCamera == null) && lease.Error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
                throw new TimeoutException("天空岛活动场景或官方相机未能就绪");
            // 官方地图的分区灰显：场景一就绪就按存档里的到访位刷一次。
            // 放在装配最前面，是为了不给玩家留「刚落地时整张图都是彩色」的窗口——
            // 场景包里的分区图层默认可见（失败开放），这里负责立刻关掉没去过的。
            mapFog.Apply(story.Current.visitedRegions);
            root.transform.position = origin;
            if (root.transform.localScale != Vector3.one || root.transform.rotation != Quaternion.identity)
                throw new InvalidOperationException("天空岛场景根节点变换未归一化");
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            int groundLayer = FirstLayer(groundMask);
            int wallLayer = FirstLayer(GameplayDataSettings.Layers.wallLayerMask.value);
            if (groundLayer < 0 || wallLayer < 0) throw new InvalidOperationException("游戏碰撞层未就绪");
            rendering = new SkyIslandRendering();
            rendering.Apply(root, groundLayer, wallLayer);
            PrepareMarkers();
            lighting = new SkyIslandLighting();
            lighting.Apply(root);
            root.SetActive(true);
            Physics.SyncTransforms();
            VerifyGround(playerSpawn);
            VerifyGround(exitMarker);
            foreach (Transform marker in enemyMarkers) VerifyGround(marker);
            foreach (Transform marker in landmarks) VerifyGround(marker);
            foreach (Transform marker in searchMarkers) VerifyGround(marker);
            Transform nav = root.transform.Find("Navigation");
            if (nav == null || nav.localPosition != Vector3.zero || nav.localScale != Vector3.one || nav.localRotation != Quaternion.identity)
                throw new InvalidOperationException("Navigation 必须位于 root 下且为单位变换");
            MeshFilter filter = nav.GetComponent<MeshFilter>();
            if (filter == null) throw new InvalidOperationException("天空岛导航网格缺失");
            navigation = new ArenaPrototypeNavigation();
            scan = navigation.BeginScan(filter.sharedMesh, origin);
            deadline = Time.realtimeSinceStartup + 30;
            while (scan.MoveNext())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("天空岛专属导航扫描超时");
                yield return null;
            }
            scan.Dispose();
            scan = null;
            int nodes = navigation.CountWalkableNodes();
            if (nodes == 0) throw new InvalidOperationException("天空岛导航没有可走节点");
            probe = root.AddComponent<Seeker>();
            foreach (Transform marker in enemyMarkers)
            {
                pathCompleted = pathValid = false;
                probe.StartPath(playerSpawn.position, marker.position, OnProbePath, navigation.Mask);
                deadline = Time.realtimeSinceStartup + 10;
                while (!pathCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (!pathCompleted || !pathValid) throw new InvalidOperationException("导航无法从码头到达 " + marker.name);
            }
            gates = new SkyIslandGates(root, navigation, content, wallLayer);
            gates.Apply(story.Current);
            navigationReady = true;
            deadline = Time.realtimeSinceStartup + 120;
            while ((!lease.LoadFinished || !LevelManager.AfterInit) && lease.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
            // 官方 InitLevel 的异常不会传给等待 LevelInited 的外层加载；超时必须主动给它一个取消信号，
            // 否则返航会一直等同一个永远不会完成的加载任务（CR-2026-09-08-005）。
            if (lease.Error == null && (!lease.LoadFinished || !LevelManager.AfterInit))
                lease.Abort("官方关卡初始化超时，天空岛已请求取消");
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            string contractError;
            if (!SkyIslandOfficialContract.Verify(entryScene, SkyIslandSceneReferenceBridge.SceneId, out contractError))
            {
                lease.Abort(contractError);
                throw new InvalidOperationException(contractError);
            }
            lease.MarkInitialized();
            if (returnRequested)
            {
                LevelManager.UnregisterWaitForInitialization(this);
                DispatchReturnIfReady();
                yield break;
            }
            player = CharacterMainControl.Main;
            playerHealth = player.Health;
            playerHealth.OnDeadEvent.AddListener(OnPlayerDied);
            LevelManager.UnregisterWaitForInitialization(this);
            safePosition = playerSpawn.position + Vector3.up * 0.15f;
            moved = ready = true;
            enteredAt = Time.unscaledTime;
            worldStory = new SkyIslandWorldStory(this, story, root);
            ambience = new SkyIslandAmbience(root);
            ambience.ApplyStoryFlags(story.Current.flags);
            encounters = new SkyIslandEncounters(root, player, navigation.Mask, groundMask, content, IsSessionValid,
                EncounterWasSaved, OnEncounterCleared, Status, OnStormDefeated);
            raidSeed = unchecked(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f));
            services = new SkyIslandServices(player, root, groundMask, raidSeed);
            // 撤离点的地面标识。半径就是 ExtractionRadius，圈内即判定内。
            // 纯表现层，单独持有 owner：画不出来不拖垮旅程，玩家仍可正常撤离。
            Safe("extraction_rings", delegate
            {
                extractionRings = new SkyIslandExtractionRings(root.transform, exitMarker, bellExit,
                    ExtractionRadius, groundMask);
                extractionRings.Apply(BellExitIfUnlocked() != null);
            });
            // 搜刮点单独持有 owner：装配失败不拖垮旅程，玩家仍可正常走主线。
            Safe("scavenging", delegate
            {
                scavenging = new SkyIslandScavenging(root, player, groundMask, raidSeed, IsSessionValid,
                    Status, bounty.ReportScavenged);
            });
            residents = new SkyIslandResidents();
            residents.Start(root, navigation, IsSessionValid, worldStory.Talk);
            SkyIslandGuideInteractable.Attach(root, this);
            Status(L10n.T("晴岚群岛已就绪 · 地图键查阅全岛 · ",
                "Qinglan Archipelago ready · press your map key to view the isles · ") + story.CurrentObjective, false);
            Debug.Log("[SkyIsland] ENTER_PASS scene=" + entryScene.path + " nodes=" + nodes +
                " enemyPoints=" + enemyMarkers.Count + " searches=" + searchCount + " landmarks=" + landmarks.Count +
                " lootPoints=" + (scavenging == null ? 0 : scavenging.PlacedPoints) +
                " mapLayers=" + mapFog.LayerCount + " seed=" + raidSeed);
        }

        public bool HasInitialized() { return navigationReady || closed; }
        private bool IsSessionValid()
        { return this != null && !closed && ready && !returning && !deathPending && root != null && player != null && player == CharacterMainControl.Main && !SceneLoader.IsSceneLoading; }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (closed || !SkyIslandRaidLease.IsRaidScene(scene)) return;
            try
            {
                entryScene = scene;
                foreach (GameObject candidate in scene.GetRootGameObjects())
                {
                    if (candidate.name == "SkyIslandWorld") root = candidate;
                }
                if (root == null) throw new InvalidOperationException("独立场景缺少地形根节点");
                LevelManager.RegisterWaitForInitialization(this);
            }
            catch (Exception e)
            {
                navigationReady = true;
                assemblyError = e.Message;
                Status(L10n.T("独立关卡装配失败：", "Standalone level assembly failed: ") + e.Message, true);
            }
        }

        private void PrepareMarkers()
        {
            foreach (Transform marker in root.GetComponentsInChildren<Transform>(true))
            {
                bool search = marker.name.StartsWith("Search", StringComparison.Ordinal);
                bool enemyPoint = marker.name.StartsWith("EnemySpawn", StringComparison.Ordinal);
                bool landmark = marker.name.StartsWith("POI_", StringComparison.Ordinal);
                bool markerNode = marker.name == "PlayerSpawn" || marker.name == "Exit" || marker.name == "BellExtraction" || search || enemyPoint || landmark;
                if (!markerNode) continue;
                if (marker.parent != root.transform || marker.localScale != Vector3.one || marker.localRotation != Quaternion.identity)
                    throw new InvalidOperationException("标记未归一化：" + marker.name);
                if (marker.name == "PlayerSpawn") playerSpawn = marker;
                else if (marker.name == "Exit") exitMarker = marker;
                else if (marker.name == "BellExtraction") bellExit = marker;
                else if (enemyPoint) enemyMarkers.Add(marker);
                else if (landmark) landmarks.Add(marker);
                else if (search)
                {
                    searchMarkers.Add(marker);
                    int layer = LayerMask.NameToLayer("Interactable");
                    if (layer < 0) throw new InvalidOperationException("官方交互层缺失");
                    marker.gameObject.layer = layer;
                    BoxCollider trigger = marker.gameObject.AddComponent<BoxCollider>();
                    trigger.center = Vector3.up * 0.8f;
                    trigger.size = new Vector3(2.2f, 1.8f, 2.2f);
                    trigger.isTrigger = true;
                    string key = marker.name;
                    marker.gameObject.AddComponent<SkyIslandSearchPoint>().Bind(delegate
                    {
                        if (closed || worldStory == null) return;
                        worldStory.ReadPoint(key, delegate { searched.Add(key); });
                    }, SkyIslandWorldStory.PointName(key));
                    searchCount++;
                }
            }
            enemyMarkers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            landmarks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (playerSpawn == null || exitMarker == null || enemyMarkers.Count == 0 || landmarks.Count == 0 || searchCount == 0)
                throw new InvalidOperationException("出生/撤离/敌人/探索/地标点位缺失");
        }

        private void VerifyGround(Transform marker)
        {
            RaycastHit hit;
            if (!Physics.Raycast(marker.position + Vector3.up * 2, Vector3.down, out hit, 4, groundMask, QueryTriggerInteraction.Ignore) ||
                !hit.transform.IsChildOf(root.transform)) throw new InvalidOperationException("点位脚下未命中自建地面：" + marker.name);
        }

        private void OnProbePath(Pathfinding.Path path)
        {
            if (closed) return;
            pathCompleted = true;
            pathValid = !path.error && path.vectorPath != null && path.vectorPath.Count >= 2;
        }

        internal async void SpawnEnemy()
        {
            if (closed || !ready || spawning || (enemy != null && enemy.Health != null && !enemy.Health.IsDead))
            { Status(L10n.T("请等待场景就绪或先击败现有测试敌人",
                "Wait for the scene, or defeat the existing test enemy first"), true); return; }
            Transform spawn = Nearest(enemyMarkers, player.transform.position);
            if (spawn == null || Vector3.Distance(spawn.position, player.transform.position) > 120)
            { Status(L10n.T("请先沿路靠近一个岛区，再生成测试敌人",
                "Walk closer to an island region before spawning a test enemy"), true); return; }
            spawning = true;
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            try
            {
                CharacterRandomPreset source = null;
                foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                    if (preset != null && !preset.isBoss && !preset.isZombie && preset.team == Teams.scav &&
                        preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !preset.name.StartsWith("BossRush_", StringComparison.Ordinal) &&
                        (source == null || string.CompareOrdinal(preset.name, source.name) < 0)) source = preset;
                if (source == null) throw new InvalidOperationException("未找到已加载的普通拾荒者 preset");
                clone = Instantiate(source);
                clone.name = "BossRush_SkyIsland_TestEnemy";
                clone.dropBoxOnDead = false;
                clone.hasSoul = false;
                clone.exp = 0;
                clone.canDieIfNotRaidMap = true;
                clone.setActiveByPlayerDistance = false;
                created = await clone.CreateCharacterAsync(spawn.position, Vector3.forward, -1, null, false);
                if (this == null || closed || root == null || player == null || player != CharacterMainControl.Main || SceneLoader.IsSceneLoading)
                { if (created != null) Destroy(created.gameObject); return; }
                if (created == null) throw new InvalidOperationException("测试敌人创建失败");
                Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                if (seekers.Length == 0) throw new InvalidOperationException("测试敌人没有 Seeker");
                foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                created.SetTeam(Teams.wolf);
                AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                if (ai == null) throw new InvalidOperationException("测试敌人缺少 AI 控制器");
                ai.forceTracePlayerDistance = 120f;
                DestroyEnemy();
                enemy = created;
                enemy.transform.SetParent(root.transform, true);
                enemyPreset = clone;
                clone = null;
                Status(L10n.T("测试敌人已生成（无掉落/经验）· ",
                    "Test enemy spawned (no loot, no XP) · ") + spawn.name, false);
                Debug.Log("[SkyIsland] ENEMY_READY preset=" + source.name + " point=" + spawn.name);
            }
            catch (Exception e) { if (created != null) Destroy(created.gameObject); if (this != null && !closed) Status(L10n.T("生成敌人失败：", "Enemy spawn failed: ") + e.Message, true); }
            finally { if (clone != null) Destroy(clone, created != null ? 0.1f : 0f); if (this != null) spawning = false; }
        }

        internal void VisitNextLandmark()
        {
            if (closed || !ready || landmarks.Count == 0)
            { Status(WaitForReady, true); return; }
            Transform landmark = landmarks[nextLandmark++ % landmarks.Count];
            try { VerifyGround(landmark); }
            catch (Exception e) { Status(e.Message, true); return; }
            safePosition = landmark.position + Vector3.up * 0.15f;
            player.SetPosition(safePosition);
            airborneSince = -1;
            Status(L10n.T("场景巡览 · ", "Scene tour · ") + LandmarkLabel(landmark.name), false);
        }

        internal bool CanChangeLighting { get { return !closed && ready && lighting != null; } }
        internal bool IsReady { get { return IsSessionValid(); } }
        internal string StorySummary
        { get { return story == null ? L10n.T("正在准备群岛记录", "Preparing archipelago records") : story.Summary; } }
        internal string LightingPresetName { get { return lighting == null ? L10n.T("晴昼", "Daylight") : lighting.PresetName; } }

        internal void CycleLighting()
        {
            if (!CanChangeLighting) { Status(WaitForReady, true); return; }
            try
            {
                lighting.CyclePreset();
                Status(L10n.T("天空岛光色 · ", "Sky Island lighting · ") + lighting.PresetName, false);
            }
            catch (Exception e) { Status(L10n.T("天空岛光色切换失败：", "Lighting switch failed: ") + e.Message, true); }
        }

        /// <summary>归航钟庭撤离点：只在敲钟结局后开放，未解锁返回 null。地图与撤离判定共用这一个事实源。</summary>
        internal Transform BellExitIfUnlocked()
        {
            return bellExit != null && story != null && story.Current.Has(SkyIslandStoryFlag.Ending) ? bellExit : null;
        }

        /// <summary>撤离唯一判定：码头恒开、钟庭需结局，两者都必须站进圈内。没有第二条返航入口。</summary>
        private bool IsInsideExtraction(out Transform marker)
        {
            marker = null;
            if (player == null) return false;
            Vector3 position = player.transform.position;
            if (exitMarker != null && Vector3.Distance(position, exitMarker.position) < ExtractionRadius)
            { marker = exitMarker; return true; }
            Transform bell = BellExitIfUnlocked();
            if (bell != null && Vector3.Distance(position, bell.position) < ExtractionRadius)
            { marker = bell; return true; }
            return false;
        }

        /// <summary>
        /// 打开**官方**地图。天空岛的地图数据（`MiniMapSettings`）随场景包一起提供，
        /// 玩家平时直接按自己绑定的地图键即可（`CharacterInputControl.OnUIMapInput`）；
        /// 这里只是给航路图交互体和 F3 菜单一个同样的入口，不自绘任何界面。
        /// </summary>
        internal void OpenMap()
        {
            if (closed || !ready) { Status(WaitForReady, true); return; }
            if (worldStory != null) worldStory.Hide();
            try { MiniMapView.Show(); }
            catch (Exception e) { Status(L10n.T("官方地图打开失败：", "Could not open the game map: ") + e.Message, true); }
        }

        internal static void HideMapBeforeF3(ModBehaviour owner)
        {
            SkyIslandSession session = owner.GetComponent<SkyIslandSession>();
            if (session != null && session.worldStory != null) session.worldStory.Hide();
        }

        private void Update()
        {
            if (closed) return;
            if (returnRequested) { DispatchReturnIfReady(); return; }
            if (!ready) return;
            if (worldStory != null) worldStory.Tick();
            // HUD 的淡入淡出与过期只吃 unscaled 时间：剧情面板把 timeScale 压到 0 时
            // 区域大标题仍应正常淡出，公告也仍应正常过期。
            if (hud != null) hud.Tick(Time.unscaledDeltaTime);
            if (player == null || player != CharacterMainControl.Main || root == null || !entryScene.isLoaded)
            { Close(false, "owner_lost"); return; }
            if (story != null && !story.IsCurrentSlot) { Close(true, "save_slot_changed"); return; }
            string mode;
            if (host == null || host.ValidationHasActiveMode(out mode))
            { Close(true, "mode_changed"); return; }
            if (worldStory != null && worldStory.Visible)
            {
                airborneSince = -1;
                // 与官方界面同口径：剧情面板开着时撤离读条冻结而不是清零。
                // 面板把 timeScale 压到 0，但 unscaledTime 照走，不顺延起点读条会自己走完。
                if (extractionStarted >= 0) extractionStarted += Time.unscaledDeltaTime;
                return;
            }
            if (encounters != null) encounters.Tick();
            if (scavenging != null) scavenging.Tick();
            if (residents != null)
            {
                // 折翎被战胜后不再露面：战斗实例用的就是他自己的脸和名字，若照旧放回剧情体，
                // 玩家会看到刚打死的人站在自己的尸体和掉落箱旁边，头顶还挂着「聊聊航路」。
                // 原地改留一块旧腰牌（SkyIslandWorldStory 按持久 flag 重建）。
                //
                // 判据用「本局是否打响过」这**一个**事实源，不再是「不在战斗中 且 持久 flag 已落」：
                // 后者的两个条件延迟不同——最后一名倒下的那一帧 IsBusy 就转 false，而 flag 要等
                // encounters.Tick（0.25 s 节流）提交并被存档接受，中间那段窗口他会站回尸体旁；
                // 存档有写屏障时 flag 永远落不下来，那就是持久可见（CR-2026-09-09-013）。
                // 钟守不同：战斗对象是「失控的守钟装置」，人本来就该活着，照旧只在战斗中隐藏。
                residents.SetVisible("sky_zheling", !ZhelingDefeated && !HasStoryChallengeStarted("Zheling"));
                residents.SetVisible("sky_bellkeeper", !IsStoryChallengeActive("BellKeeper"));
            }
            // 落盘门按半径而不是全图。自动组改成按出击刷新之后，全岛几乎总有活着的敌人，
            // 用 `HasLivingEnemies` 等于把这道门永久关上：已接受的剧情事实只能等离岛或死亡才写盘，
            // 中途崩溃或强退就全丢。半径口径既保留「不在交火帧写盘」的本意，又让玩家清完手边这一段
            // 就能安全落盘。
            if (story != null)
                story.Tick(encounters == null ||
                    !encounters.HasLivingEnemiesWithin(player.transform.position, SaveQuietRadius));
            if (gates != null) gates.Apply(story.Current);
            // 钟庭环与 BellExitIfUnlocked() 同一事实源：地上看得到的圈，就是站进去能走的圈。
            if (extractionRings != null) extractionRings.Apply(BellExitIfUnlocked() != null);
            if (lighting != null) lighting.Tick();
            if (ambience != null) { ambience.ApplyStoryFlags(story.Current.flags); ambience.Tick(player.transform.position); }
            Vector3 local = player.transform.position - origin;
            if (local.y < -8 || Mathf.Abs(local.x) > 475 || Mathf.Abs(local.z) > 425)
            { Rescue(); return; }
            if (Time.unscaledTime >= nextGroundCheck)
            {
                nextGroundCheck = Time.unscaledTime + 0.2f;
                RaycastHit hit;
                if (Physics.Raycast(player.transform.position + Vector3.up * 0.25f, Vector3.down, out hit, 1.4f,
                    groundMask, QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(root.transform))
                { safePosition = hit.point + Vector3.up * 0.35f; airborneSince = -1; }
                else if (airborneSince < 0) airborneSince = Time.unscaledTime;
                else if (Time.unscaledTime - airborneSince > 2.5f) { Rescue(); return; }
            }
            Transform extraction;
            bool insideExtraction = IsInsideExtraction(out extraction);
            // 与官方 CountDownArea 一致：打开任何官方界面（背包、地图等）时撤离计时不推进，免得开着背包被送回基地。
            if (Time.unscaledTime > enteredAt + 1 && View.ActiveView == null && insideExtraction)
            {
                if (extractionStarted < 0) extractionStarted = Time.unscaledTime;
                float remaining = ExtractionHold - (Time.unscaledTime - extractionStarted);
                if (hud != null) hud.SetExtraction(L10n.T("返回基地 · ", "Returning to base · ") +
                    Mathf.CeilToInt(Mathf.Max(0, remaining)) + L10n.T(" 秒", "s"));
                if (remaining <= 0) Close(true, extraction == bellExit ? "bell_extract" : "dock_extract");
                return;
            }
            // 官方 CountDownArea 的口径是「不推进」而不是「清零」：人还在圈里、只是开着背包时，
            // 把起点顺延同样的时长，读条冻结在原处。真正离开圈子才归零。
            if (insideExtraction && extractionStarted >= 0)
            {
                extractionStarted += Time.unscaledDeltaTime;
                return;
            }
            extractionStarted = -1;
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.5f;
                Transform nearest = Nearest(landmarks, player.transform.position);
                // 60 米，不是 120。按 ArtSource/SkyIsland/layout.json 的实际坐标，四条支路的
                // 主岛侧桥头到对岸地标只有 73.2 / 103.9 / 73.9 / 117.3 米，旧阈值下站在主岛边缘
                // 就能点亮 S1–S4 的地图迷雾并刷完「巡视群岛区域」，根本不用过桥。
                // 下界由主岛正常路线决定：B→C→D 直线穿越对 POI_C 的最近距离约 50 米。
                // 可用区间 (50, 73.2)，取 60 两头留余量。
                if (nearest != null && (nearest.position - player.transform.position).sqrMagnitude < 60 * 60 &&
                    story.RecordRegionVisited(nearest.name.Substring(4)))
                {
                    bounty.ReportRegionVisited();
                    // 刚踏足的区域立刻在官方地图上点亮。
                    mapFog.Apply(story.Current.visitedRegions);
                }
                if (hud != null)
                {
                    // 不在圈里就把读秒整行撤掉，卡片不留空位。
                    hud.SetExtraction(null);
                    hud.SetRegion(nearest == null ? string.Empty : LandmarkLabel(nearest.name));
                    hud.SetObjective(story.CurrentObjective);
                    hud.SetChips(FieldStatus());
                    // 存档状态**正常时一个字都不说**：只有真出问题（写屏障 / 单向故障 / 换槽）
                    // 才值得占玩家一行。旧版把「群岛记录已同步」也常驻着，等于每帧都在报平安。
                    if (story != null && !story.CanWrite) hud.Announce(story.SaveStatus);
                }
            }
        }

        /// <summary>HUD 第三行：搜刮进度与在手委托，让「这趟出击还能做什么」一眼可见。</summary>
        private string FieldStatus()
        {
            string loot = scavenging == null
                ? L10n.T("物资点 --", "caches --")
                : L10n.T("物资 ", "caches ") + scavenging.OpenedPoints + "/" + scavenging.PlacedPoints;
            string contract = bounty.HasActive
                ? L10n.T(" · 委托 ", " · contract ") + bounty.Describe()
                : (bounty.CompletedRounds > 0
                    ? L10n.T(" · 已交委托 ", " · contracts delivered ") + bounty.CompletedRounds
                    : L10n.T(" · 风铃集可接委托", " · contracts at Windchime Market"));
            return loot + contract;
        }

        private void Rescue()
        {
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionStarted = -1;
            Status(L10n.T("已返回最近安全落脚点", "Returned to the nearest safe footing"), false);
            Debug.Log("[SkyIsland] FALL_RESCUE position=" + safePosition);
        }
        private static Transform Nearest(List<Transform> markers, Vector3 point)
        {
            Transform result = null;
            float distance = float.MaxValue;
            foreach (Transform marker in markers)
            {
                if (marker == null) continue;
                float value = (marker.position - point).sqrMagnitude;
                if (value < distance) { distance = value; result = marker; }
            }
            return result;
        }
        internal static string LandmarkLabel(string name)
        {
            string[] names = L10n.IsChinese
                ? new[] { "登云码头", "风铃集", "青穗梯田", "悬根林", "鸣风栈道", "镜水寺", "残星工坊", "归航钟庭" }
                : new[] { "Cloudrise Dock", "Windchime Market", "Green Terraces", "Hanging Root Wood",
                    "Windsong Boardwalk", "Mirrorwater Temple", "Fallen Star Workshop", "Bell Court" };
            if (name.Length > 4 && name[4] >= 'A' && name[4] <= 'H') return names[name[4] - 'A'];
            if (name == "POI_S1") return L10n.T("蛙鸣池", "Frogsong Pool");
            if (name == "POI_S2") return L10n.T("倒挂邮亭", "Upturned Post Hut");
            if (name == "POI_S3") return L10n.T("听雨洞", "Rainlisten Grotto");
            if (name == "POI_S4") return L10n.T("残星瞭台", "Starfall Overlook");
            return name.Replace("POI_", "").Replace('_', ' ');
        }
        private void OnStartedLoading(SceneLoadingContext context)
        {
            if (context.sceneName == SkyIslandSceneReferenceBridge.SceneName && !moved) return;
            if (!moved && !entryScene.IsValid()) return;
            returning = true;
            ready = false;
            CancelPendingInitialization();
            StopAllCoroutines();
            Safe("ambience_stop", delegate { if (ambience != null) ambience.Dispose(); });
            if (worldStory != null) worldStory.Hide();
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (SkyIslandRaidLease.IsRaidScene(scene)) Cleanup("raid_unloaded");
        }
        private void OnPlayerDied(DamageInfo damage)
        {
            // 官方 CharacterDieTask 保存墓碑、处理损失与返回基地；不移动尸体或抢先卸载场景。
            deathPending = true; ready = false;
            Safe("ambience_stop", delegate { if (ambience != null) ambience.Dispose(); });
            if (worldStory != null) worldStory.Hide();
            if (story != null) story.Tick(true);
        }
        private void DestroyEnemy()
        {
            if (enemy != null) { enemy.gameObject.SetActive(false); Destroy(enemy.gameObject); }
            if (enemyPreset != null) Destroy(enemyPreset, 0.1f);
            enemy = null;
            enemyPreset = null;
        }

        internal void Close(bool restorePlayer, string reason)
        {
            if (closed) return;
            if (restorePlayer && loadStarted && !deathPending)
            {
                returnRequested = returning = true;
                if (worldStory != null) worldStory.Hide();
                    Status(L10n.T("正在返回基地，已完成的群岛故事会保留…",
                        "Returning to base — everything you finished on the isles is kept…"), false);
                BlockInputForReturn();
                DispatchReturnIfReady();
                return;
            }
            if (deathPending && entryScene.IsValid() && entryScene.isLoaded) return;
            Cleanup(reason);
        }
        /// <summary>
        /// 照官方 SceneLoaderProxy.LoadScene：返航一旦派发就封锁输入，黑幕淡入那一秒玩家不能再移动、开火或触发交互。
        /// 封锁源必须是岛场景内的临时对象（挂在地形根下即属于本场景），随场景卸载自动失效：
        /// InputManager 只在源对象销毁或失活时解除封锁，若挂在 DontDestroyOnLoad 的 Mod 宿主上，回到基地后输入会被永久锁死。
        /// </summary>
        private void BlockInputForReturn()
        {
            if (returnInputBlock != null || root == null || !entryScene.IsValid() || !entryScene.isLoaded) return;
            Safe("input_block", delegate
            {
                GameObject block = new GameObject("SkyIslandReturnInputBlock");
                block.transform.SetParent(root.transform, false);
                InputManager.DisableInput(block);
                returnInputBlock = block;
            });
        }
        private void DispatchReturnIfReady()
        {
            if (lease == null || !lease.LoadFinished || SceneLoader.IsSceneLoading || lease.IsReturning) return;
            // 官方返回失败后仍保留请求；租约内部节流重试，不能由首次派发永久封死。
            if (story != null) story.Tick(true);
            lease.ReturnToBase(moved);
        }
        private void CancelPendingInitialization()
        {
            navigationReady = true;
            // 取消要传到官方加载等待，不能只停自己的协程。
            if (lease != null) lease.Abort("天空岛会话已取消初始化");
            LevelManager.UnregisterWaitForInitialization(this);
            Safe("cancel_path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("cancel_scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
        }
        private void Cleanup(string reason)
        {
            if (closed) return;
            closed = true;
            ready = false;
            StopAllCoroutines();
            LevelManager.UnregisterWaitForInitialization(this);
            if (subscribed)
            {
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                if (playerHealth != null) playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);
                subscribed = false;
            }
            Safe("encounters", delegate { if (encounters != null) encounters.Dispose(); });
            Safe("scavenging", delegate { if (scavenging != null) scavenging.Dispose(); });
            Safe("map_fog", delegate { mapFog.Dispose(); });
            // 归航菜的本局属性加成必须在离岛时摘掉，否则会跟着主角带回基地（owner 是 services）。
            Safe("services", delegate { if (services != null) services.Dispose(); });
            Safe("extraction_rings", delegate { if (extractionRings != null) extractionRings.Dispose(); });
            Safe("residents", delegate { if (residents != null) residents.Dispose(); });
            Safe("ambience", delegate { if (ambience != null) ambience.Dispose(); });
            Safe("story_ui", delegate { if (worldStory != null) worldStory.Dispose(); });
            // 待保存 owner 与 Mod 宿主无关：CloseOrRetain 一律移交给独立持久对象，宿主销毁不再吞掉已接受事实。
            Safe("story_save", delegate { SkyIslandStorySaveRecovery.CloseOrRetain(story); });
            Safe("enemy", DestroyEnemy);
            Safe("path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
            Safe("gates", delegate { if (gates != null) gates.Dispose(); });
            Safe("graph", delegate { if (navigation != null) navigation.Dispose(); });
            navigation = null;
            Safe("lighting", delegate { if (lighting != null) lighting.Dispose(); });
            Safe("materials", delegate { if (rendering != null) rendering.Dispose(); });
            if (hud != null) hud.Dispose();
            hud = null;
            if (returnInputBlock != null) Destroy(returnInputBlock);
            Debug.Log("[SkyIsland] CLEANUP reason=" + reason + " searches=" + searched.Count +
                " looted=" + (scavenging == null ? 0 : scavenging.OpenedPoints) +
                " bounties=" + bounty.CompletedRounds);
            if (lease != null) lease.Release(delegate { if (this != null) Destroy(this); });
            else Destroy(this);
        }
        private void OnDestroy() { Cleanup("component_destroyed"); }

        internal SkyIslandServices Services { get { return services; } }
        internal SkyIslandBounty Bounty { get { return bounty; } }

        /// <summary>
        /// 本局这类委托**最多还能推进多少**。派单前用它门控，避免派出永远做不完的单子。
        ///
        /// 三个计数器的信号源不同「寿命」，这正是必须有这个查询的原因：
        /// - <see cref="SkyIslandBountyKind.Salvage"/> 的搜刮点按出击重刷，本局有多少就是多少；
        /// - <see cref="SkyIslandBountyKind.Threats"/> 的清场是**持久存档事实**，
        ///   已清过的组这局根本不会再触发回调（`Tick` 直接短路成 Cleared）；
        /// - <see cref="SkyIslandBountyKind.Survey"/> 的区域访问同样持久
        ///   （`RecordRegionVisited` 对已访问区域返回 false），走遍一次就再也不涨。
        ///
        /// 于是老档上后两类的可完成量可能是 0。这些量只会随进度单调递减
        /// （每推进 1 点，剩余量减 1），所以接单时 available ≥ target 就保证这一单做得完。
        /// </summary>
        internal int AvailableBountyProgress(SkyIslandBountyKind kind)
        {
            if (kind == SkyIslandBountyKind.Salvage)
                return scavenging == null ? 0 : scavenging.AvailablePoints;
            if (kind == SkyIslandBountyKind.Threats)
                return encounters == null ? 0 : encounters.RemainingClearable;
            if (kind == SkyIslandBountyKind.Survey)
            {
                if (story == null) return 0;
                int count = 0;
                for (int i = 0; i < landmarks.Count; i++)
                {
                    Transform landmark = landmarks[i];
                    if (landmark == null || landmark.name.Length <= 4) continue;
                    if (!story.HasVisitedRegion(landmark.name.Substring(4))) count++;
                }
                return count;
            }
            return 0;
        }
        internal bool HasPlantingDelivered
        { get { return story != null && story.Current.Has(SkyIslandStoryFlag.PlantingDelivered); } }
        internal bool BothBeaconsLit { get { return story != null && story.Current.BothBeacons; } }
        internal bool StormResolved { get { return story != null && story.Current.StormResolved; } }
        internal bool ZhelingDefeated
        { get { return story != null && story.Current.Has(SkyIslandStoryFlag.ZhelingDefeated); } }

        /// <summary>
        /// 现在能不能打开剧情面板。
        ///
        /// 面板走共享模态租约，会把 `Time.timeScale` 压到 0，而这个暂停是**可靠的**：
        /// `ModBehaviour.LateUpdate` → `LateUpdateZombieModeRuntime` → `EnforceModalInputPause()`
        /// 排在官方 `TimeScaleManager.Update()` 之后，Unity 保证所有 Update 先于所有 LateUpdate。
        /// 不加门的话，搜索点、居民与完成纪念物就都是战斗中随手可用的暂停键；更糟的是面板里
        /// 还挂着眠苔的苔药与浮舟的整备，等于可以定格战斗再花钱回满血。
        ///
        /// 这不是理论问题：五个自动遭遇的生成点本身就是搜索点（Search_C_02 / D_02 / E_02 /
        /// F_02 / G_02），眠苔站在 POI_D、距 EnemySpawn_D 只有 47 米。
        ///
        /// 按半径而不是全图判定：玩家把一组敌人丢在岛的另一头，不该让全岛剧情交互一起失效。
        /// </summary>
        internal bool CanOpenStoryPanel(out string reason)
        {
            reason = null;
            if (!IsSessionValid())
            { reason = L10n.T("请等待群岛就绪。", "Wait for the archipelago to finish loading."); return false; }
            if (encounters != null &&
                encounters.HasLivingEnemiesWithin(player.transform.position, StoryPanelQuietRadius))
            {
                reason = L10n.T("附近还有威胁 —— 先把这一段航路清干净，再静下心来。",
                    "There are still threats nearby — clear this stretch of the lane before you settle down.");
                return false;
            }
            return true;
        }
        internal string ScavengeStatus
        {
            get
            {
                return scavenging == null
                    ? L10n.T("物资点尚未就绪", "Scavenging points are not ready yet")
                    : L10n.T("已搜刮 ", "Looted ") + scavenging.OpenedPoints + " / " + scavenging.PlacedPoints +
                        L10n.T(" 处", " points");
            }
        }

        /// <summary>交单奖励落在苇白脚边；位置由调用方给，避免奖励箱堆在玩家身上。</summary>
        internal bool DropBountyReward(Vector3 position, SkyIslandLootTier tier, int round)
        {
            return services != null && services.DropBountyReward(position, tier, round);
        }

        internal bool IsEncounterCleared(string id) { return encounters != null && encounters.IsCleared(id); }
        internal bool IsStoryChallengeActive(string id) { return encounters != null && encounters.IsBusy(id); }
        /// <summary>本局是否已经打响过这一组具名对手；剧情体的露面判据见 <see cref="Update"/>。</summary>
        internal bool HasStoryChallengeStarted(string id) { return encounters != null && encounters.HasStarted(id); }
        internal bool BeginStoryChallenge(string id)
        {
            if (!IsSessionValid() || story == null || !story.CanWrite || encounters == null) return false;
            if (id == "Zheling" && story.Current.ZhelingResolved) return false;
            if (id == "BellKeeper" && (!story.Current.BothBeacons || story.Current.BellKeeperResolved)) return false;
            // 噬风循着重新亮起的两盏灯而来：双航标是它到场的唯一前置，击败后不再出现。
            if (id == "Storm" && (!story.Current.BothBeacons || story.Current.StormResolved)) return false;
            return encounters.BeginChallenge(id);
        }
        private bool EncounterWasSaved(string id)
        {
            if (story == null) return false;
            if (id == "Zheling") return story.Current.ZhelingResolved;
            if (id == "BellKeeper") return story.Current.BellKeeperResolved;
            if (id == "Storm") return story.Current.StormResolved;
            return story.Current.EncounterCleared(id);
        }
        private void OnEncounterCleared(string id)
        {
            string message;
            if (id == "Zheling") story.TryApply(SkyIslandStoryAction.ZhelingDefeated, out message);
            else if (id == "BellKeeper") story.TryApply(SkyIslandStoryAction.BellKeeperDefeated, out message);
            else if (id == "Storm") story.TryApply(SkyIslandStoryAction.StormSlain, out message);
            else story.RecordEncounterCleared(id);
            // 遭遇 owner 在存档接受之前会每秒重投同一个 id（写屏障/暂时失败），
            // 委托记账必须按 id 幂等，否则一次延迟保存会把「清理航路威胁」刷成好几单。
            if (bountyCredited.Add(id)) bounty.ReportEncounterCleared();
        }

        /// <summary>
        /// 噬风倒下：战利品落在它倒下的位置。
        /// 剧情事实挂在**本体死亡**上而不是整组清空——否则杀了 Boss、留着两名随从直接离岛，
        /// 下次进来还能再挑战一次，等于无限刷星工遗存箱。随从仍需清完才算清场，
        /// `OnEncounterCleared` 里的同一条 TryApply 保留为写屏障失败时的重试兜底。
        /// </summary>
        private void OnStormDefeated(Vector3 position)
        {
            if (closed || root == null) return;
            if (story != null)
            {
                string message;
                story.TryApply(SkyIslandStoryAction.StormSlain, out message);
            }
            SkyIslandStormBoss.DropTrophy(root.transform, position, raidSeed);
        }
        // ====================================================================
        // F3 天空岛验收的只读观测面（F3GameplayValidationSkyIsland 独占消费）
        // ====================================================================
        // 纪律：这一段**只读**。不得在这里推进剧情、移动玩家、生成敌人或写存档——
        // 岛内验收跑在玩家的真实旅程上，任何副作用都会污染他正在做的这一趟。
        // 需要「做点什么才能验」的用例一律进人工清单，不许在这里偷偷改状态。

        internal GameObject ValidationWorldRoot { get { return root; } }
        internal Scene ValidationScene { get { return entryScene; } }
        internal SkyIslandStoryService ValidationStory { get { return story; } }
        internal SkyIslandResidents ValidationResidents { get { return residents; } }
        internal Transform ValidationPlayerSpawn { get { return playerSpawn; } }
        internal Transform ValidationExitMarker { get { return exitMarker; } }
        internal Transform ValidationBellMarker { get { return bellExit; } }
        internal GraphMask ValidationNavigationMask
        { get { return navigation == null ? default(GraphMask) : navigation.Mask; } }
        internal static float ValidationExtractionRadius { get { return ExtractionRadius; } }
        internal static float ValidationExtractionHold { get { return ExtractionHold; } }
        internal static float ValidationStoryPanelQuietRadius { get { return StoryPanelQuietRadius; } }
        internal static float ValidationSaveQuietRadius { get { return SaveQuietRadius; } }

        /// <summary>
        /// 只读几何探测：给定世界坐标**是否**落在某个撤离圈内。
        ///
        /// 与 <see cref="IsInsideExtraction"/> 共用同一条距离判据，但不读玩家位置、不改任何状态，
        /// 因此可以在验收里对「圆心 / 半径内侧 / 半径外侧」三点取样，证明画出来的圈与判定一致
        /// （`M_SKY_ISLAND_08`），而不必真的把玩家搬过去。
        /// </summary>
        internal bool ValidationIsInsideExtractionAt(Vector3 position, out string markerName)
        {
            markerName = null;
            if (exitMarker != null && Vector3.Distance(position, exitMarker.position) < ExtractionRadius)
            { markerName = exitMarker.name; return true; }
            Transform bell = BellExitIfUnlocked();
            if (bell != null && Vector3.Distance(position, bell.position) < ExtractionRadius)
            { markerName = bell.name; return true; }
            return false;
        }

        /// <summary>会话与内容装配的一次性快照。字段全部来自已有 owner，不触发任何重算。</summary>
        internal SkyIslandValidationSnapshot ValidationSnapshot()
        {
            var snapshot = new SkyIslandValidationSnapshot();
            snapshot.Ready = ready;
            snapshot.Closed = closed;
            snapshot.Returning = returning;
            snapshot.DeathPending = deathPending;
            snapshot.NavigationReady = navigationReady;
            snapshot.RaidSeed = raidSeed;
            snapshot.SearchPoints = searchCount;
            snapshot.Landmarks = landmarks.Count;
            snapshot.EnemyMarkers = enemyMarkers.Count;
            snapshot.WorldRootActive = root != null && root.activeInHierarchy;
            snapshot.NavigationNodes = navigation == null ? 0 : navigation.CountWalkableNodes();
            snapshot.ContentSource = content == null ? "None" : content.Source;
            snapshot.ContentEncounters = content == null || content.Encounters == null ? 0 : content.Encounters.Length;
            snapshot.ContentGates = content == null || content.Gates == null ? 0 : content.Gates.Length;
            snapshot.EncounterGroups = encounters == null ? 0 : encounters.GroupCount;
            snapshot.EncounterRemainingClearable = encounters == null ? 0 : encounters.RemainingClearable;
            snapshot.ScavengeAnchors = SkyIslandLootTables.Anchors.Length;
            snapshot.ScavengePlaced = scavenging == null ? 0 : scavenging.PlacedPoints;
            snapshot.ScavengeOpened = scavenging == null ? 0 : scavenging.OpenedPoints;
            snapshot.ScavengeAvailable = scavenging == null ? 0 : scavenging.AvailablePoints;
            snapshot.ScavengeFailed = scavenging == null ? 0 : scavenging.FailedPoints;
            snapshot.ResidentsSpawned = residents == null ? 0 : residents.SpawnedCount;
            snapshot.ExtractionRingsBuilt = extractionRings != null;
            snapshot.BellUnlocked = BellExitIfUnlocked() != null;
            snapshot.ServicesReady = services != null;
            snapshot.BountyRounds = bounty.CompletedRounds;
            snapshot.BountyActive = bounty.HasActive;
            if (story != null)
            {
                snapshot.StoryFlags = story.Current.flags;
                snapshot.VisitedRegions = story.Current.visitedRegions;
                snapshot.StoryCurrentSlot = story.IsCurrentSlot;
                snapshot.StoryCanWrite = story.CanWrite;
                snapshot.Objective = story.CurrentObjective;
                snapshot.SaveStatus = story.SaveStatus;
                snapshot.Summary = story.Summary;
            }
            return snapshot;
        }

        /// <summary>剧情 owner 的对外提示通道：与其它天空岛消息共用 HUD + 官方 toast，不另开一套。</summary>
        internal void Announce(string message, bool error) { Status(message, error); }
        private void Status(string message, bool error)
        {
            if (hud != null) hud.Announce(message);
            if (report != null) report(message, error);
            if (error) Debug.LogWarning("[SkyIsland] " + message); else Debug.Log("[SkyIsland] " + message);
        }
        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }
        private static void Safe(string step, Action action)
        {
            try { action(); } catch (Exception e) { Debug.LogWarning("[SkyIsland] cleanup " + step + ": " + e.Message); }
        }
    }

    /// <summary>
    /// 会话与内容装配的只读快照，由 <see cref="SkyIslandSession.ValidationSnapshot"/> 填充。
    ///
    /// 刻意是**纯数据**且不引用任何 Unity 对象：F3 验收把它整份写进报告的 metrics 行，
    /// 往返两次的差值就是 `M_SKY_ISLAND_01` 要的「重复进入无残留」基线。
    /// </summary>
    internal sealed class SkyIslandValidationSnapshot
    {
        internal bool Ready, Closed, Returning, DeathPending, NavigationReady, WorldRootActive;
        internal bool StoryCurrentSlot, StoryCanWrite, BellUnlocked, ExtractionRingsBuilt, ServicesReady, BountyActive;
        internal int RaidSeed, SearchPoints, Landmarks, EnemyMarkers, NavigationNodes;
        internal int ContentEncounters, ContentGates, EncounterGroups, EncounterRemainingClearable;
        internal int ScavengeAnchors, ScavengePlaced, ScavengeOpened, ScavengeAvailable, ScavengeFailed;
        internal int ResidentsSpawned, StoryFlags, VisitedRegions, BountyRounds;
        internal string ContentSource, Objective, SaveStatus, Summary;

        /// <summary>报告用的一行式描述。字段顺序冻结，方便两次出击的行做逐字对照。</summary>
        internal string Describe()
        {
            return "ready=" + Ready + ",world_active=" + WorldRootActive + ",nav_nodes=" + NavigationNodes
                + ",searches=" + SearchPoints + ",landmarks=" + Landmarks + ",enemy_markers=" + EnemyMarkers
                + ",content=" + ContentSource + "/" + ContentEncounters + "e" + ContentGates + "g"
                + ",encounters=" + EncounterGroups + ",clearable=" + EncounterRemainingClearable
                + ",scav=" + ScavengePlaced + "/" + ScavengeAnchors + "(open=" + ScavengeOpened
                + ",avail=" + ScavengeAvailable + ",failed=" + ScavengeFailed + ")"
                + ",residents=" + ResidentsSpawned + ",rings=" + ExtractionRingsBuilt + ",bell=" + BellUnlocked
                + ",flags=" + StoryFlags + ",regions=" + VisitedRegions + ",slot=" + StoryCurrentSlot
                + ",can_write=" + StoryCanWrite + ",bounty=" + BountyRounds + "/" + BountyActive
                + ",seed=" + RaidSeed;
        }
    }
}
