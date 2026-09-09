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
        private GameObject root, hudRoot;
        // 返航期间的输入封锁源：必须是岛场景内的临时对象，随场景卸载自动失效（见 BlockInputForReturn）。
        private GameObject returnInputBlock;
        private TextMeshProUGUI hud;
        private Transform playerSpawn, exitMarker, bellExit;
        private CharacterMainControl enemy;
        private CharacterRandomPreset enemyPreset;
        private Seeker probe;
        private Action<string, bool> report;
        // 撤离圈半径与停留秒数：唯一定义点，地图说明与 HUD 倒计时共用。
        private const float ExtractionRadius = 2.5f;
        private const float ExtractionHold = 3f;
        private int groundMask, searchCount, nextLandmark;
        private bool subscribed, closed, ready, moved, spawning, pathCompleted, pathValid;
        private float enteredAt, extractionStarted = -1, nextGroundCheck, airborneSince = -1, nextHud;

        internal static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = null;
            if (owner == null) { reason = "Mod 尚未就绪"; return false; }
            if (!SkyIslandRaidLease.IsBundleDeployed())
            { reason = "缺少天空岛独立出击场景包，请更新 Mod 资源"; return false; }
            if (owner.GetComponent<SkyIslandSession>() != null || owner.GetComponent<ArenaPrototypeSession>() != null)
            { reason = "请先结束当前场景旅程并等待回收"; return false; }
            if (SkyIslandStorySaveRecovery.IsPending())
            { reason = "上一段群岛记录仍在保存，请稍后重试"; return false; }
            if (F3GameplayValidationRunner.IsRunning) { reason = "请等待完整玩法验收结束"; return false; }
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited)
            { reason = "请等待场景与玩家初始化完成"; return false; }
            string mode;
            if (owner.ValidationHasActiveMode(out mode)) { reason = "请先结束当前模式：" + mode; return false; }
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.Health == null || main.Health.IsDead) { reason = "玩家未就绪"; return false; }
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name))
            { reason = "请先回基地再前往天空岛"; return false; }
            if (GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
            { reason = "游戏相机尚未就绪"; return false; }
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
                    Status("天空岛创建失败：" + e.Message, true);
                    Close(true, "build_failed"); yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }

        private IEnumerator Build()
        {
            hud = ArenaPrototypeControls.CreateHud(out hudRoot);
            hudRoot.name = "SkyIslandHud";
            hudRoot.transform.SetParent(host.transform, false);
            Status("正在加载晴岚群岛…", false);
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
            services = new SkyIslandServices(player, root, raidSeed);
            // 搜刮点单独持有 owner：装配失败不拖垮旅程，玩家仍可正常走主线。
            Safe("scavenging", delegate
            {
                scavenging = new SkyIslandScavenging(root, player, groundMask, raidSeed, IsSessionValid,
                    Status, bounty.ReportScavenged);
            });
            residents = new SkyIslandResidents();
            residents.Start(root, navigation, IsSessionValid, worldStory.Talk);
            SkyIslandGuideInteractable.Attach(root, this);
            Status("晴岚群岛已就绪 · 地图键查阅全岛 · " + story.CurrentObjective, false);
            Debug.Log("[SkyIsland] ENTER_PASS scene=" + entryScene.path + " nodes=" + nodes +
                " enemyPoints=" + enemyMarkers.Count + " searches=" + searchCount + " landmarks=" + landmarks.Count +
                " lootPoints=" + (scavenging == null ? 0 : scavenging.PlacedPoints) + " seed=" + raidSeed);
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
                Status("独立关卡装配失败：" + e.Message, true);
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
            { Status("请等待场景就绪或先击败现有测试敌人", true); return; }
            Transform spawn = Nearest(enemyMarkers, player.transform.position);
            if (spawn == null || Vector3.Distance(spawn.position, player.transform.position) > 120)
            { Status("请先沿路靠近一个岛区，再生成测试敌人", true); return; }
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
                Status("测试敌人已生成（无掉落/经验）· " + spawn.name, false);
                Debug.Log("[SkyIsland] ENEMY_READY preset=" + source.name + " point=" + spawn.name);
            }
            catch (Exception e) { if (created != null) Destroy(created.gameObject); if (this != null && !closed) Status("生成敌人失败：" + e.Message, true); }
            finally { if (clone != null) Destroy(clone, created != null ? 0.1f : 0f); if (this != null) spawning = false; }
        }

        internal void VisitNextLandmark()
        {
            if (closed || !ready || landmarks.Count == 0) { Status("请等待天空岛就绪", true); return; }
            Transform landmark = landmarks[nextLandmark++ % landmarks.Count];
            try { VerifyGround(landmark); }
            catch (Exception e) { Status(e.Message, true); return; }
            safePosition = landmark.position + Vector3.up * 0.15f;
            player.SetPosition(safePosition);
            airborneSince = -1;
            Status("场景巡览 · " + LandmarkLabel(landmark.name), false);
        }

        internal bool CanChangeLighting { get { return !closed && ready && lighting != null; } }
        internal bool IsReady { get { return IsSessionValid(); } }
        internal string StorySummary { get { return story == null ? "正在准备群岛记录" : story.Summary; } }
        internal string LightingPresetName { get { return lighting == null ? L10n.T("晴昼", "Daylight") : lighting.PresetName; } }

        internal void CycleLighting()
        {
            if (!CanChangeLighting) { Status("请等待天空岛就绪", true); return; }
            try
            {
                lighting.CyclePreset();
                Status("天空岛光色 · " + lighting.PresetName, false);
            }
            catch (Exception e) { Status("天空岛光色切换失败：" + e.Message, true); }
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
            if (closed || !ready) { Status("请等待天空岛就绪", true); return; }
            if (worldStory != null) worldStory.Hide();
            try { MiniMapView.Show(); }
            catch (Exception e) { Status("官方地图打开失败：" + e.Message, true); }
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
            if (player == null || player != CharacterMainControl.Main || root == null || !entryScene.isLoaded)
            { Close(false, "owner_lost"); return; }
            if (story != null && !story.IsCurrentSlot) { Close(true, "save_slot_changed"); return; }
            string mode;
            if (host == null || host.ValidationHasActiveMode(out mode))
            { Close(true, "mode_changed"); return; }
            if (worldStory != null && worldStory.Visible)
            {
                airborneSince = -1;
                extractionStarted = -1;
                return;
            }
            if (encounters != null) encounters.Tick();
            if (scavenging != null) scavenging.Tick();
            if (residents != null)
            {
                residents.SetVisible("sky_zheling", !IsStoryChallengeActive("Zheling"));
                residents.SetVisible("sky_bellkeeper", !IsStoryChallengeActive("BellKeeper"));
            }
            if (story != null) story.Tick(encounters == null || !encounters.HasLivingEnemies);
            if (gates != null) gates.Apply(story.Current);
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
            // 与官方 CountDownArea 一致：打开任何官方界面（背包、地图等）时撤离计时不推进，免得开着背包被送回基地。
            if (Time.unscaledTime > enteredAt + 1 && View.ActiveView == null && IsInsideExtraction(out extraction))
            {
                if (extractionStarted < 0) extractionStarted = Time.unscaledTime;
                float remaining = ExtractionHold - (Time.unscaledTime - extractionStarted);
                if (hud != null) hud.text = "返回基地 · " + Mathf.CeilToInt(Mathf.Max(0, remaining)) + " 秒 · 群岛见闻 " + searched.Count + "/" + searchCount;
                if (remaining <= 0) Close(true, extraction == bellExit ? "bell_extract" : "dock_extract");
                return;
            }
            extractionStarted = -1;
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.5f;
                Transform nearest = Nearest(landmarks, player.transform.position);
                if (nearest != null && (nearest.position - player.transform.position).sqrMagnitude < 120 * 120 &&
                    story.RecordRegionVisited(nearest.name.Substring(4)))
                {
                    bounty.ReportRegionVisited();
                    // 刚踏足的区域立刻在官方地图上点亮。
                    mapFog.Apply(story.Current.visitedRegions);
                }
                if (hud != null) hud.text = "晴岚群岛 · " + (nearest == null ? "" : LandmarkLabel(nearest.name)) +
                    "\n" + story.CurrentObjective + "\n" + FieldStatus() +
                    "\n地图键查阅全岛 · 码头停留 3 秒撤离 · " + story.SaveStatus;
            }
        }

        /// <summary>HUD 第三行：搜刮进度与在手委托，让「这趟出击还能做什么」一眼可见。</summary>
        private string FieldStatus()
        {
            string loot = scavenging == null ? "物资点 --" :
                "物资 " + scavenging.OpenedPoints + "/" + scavenging.PlacedPoints;
            string contract = bounty.HasActive ? " · 委托 " + bounty.Describe() :
                (bounty.CompletedRounds > 0 ? " · 已交委托 " + bounty.CompletedRounds : " · 风铃集可接委托");
            return loot + contract;
        }

        private void Rescue()
        {
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionStarted = -1;
            Status("已返回最近安全落脚点", false);
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
            string[] names = { "登云码头", "风铃集", "青穗梯田", "悬根林", "鸣风栈道", "镜水寺", "残星工坊", "归航钟庭" };
            if (name.Length > 4 && name[4] >= 'A' && name[4] <= 'H') return names[name[4] - 'A'];
            if (name == "POI_S1") return "蛙鸣池";
            if (name == "POI_S2") return "倒挂邮亭";
            if (name == "POI_S3") return "听雨洞";
            if (name == "POI_S4") return "残星瞭台";
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
                    Status("正在返回基地，已完成的群岛故事会保留…", false);
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
            // 本局属性加成必须在离岛时摘掉，否则会跟着主角带回基地。
            Safe("map_fog", delegate { mapFog.Dispose(); });
            Safe("services", delegate { if (services != null) services.Dispose(); });
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
            if (hudRoot != null) Destroy(hudRoot);
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
        internal string ScavengeStatus
        {
            get
            {
                return scavenging == null ? "物资点尚未就绪" :
                    "已搜刮 " + scavenging.OpenedPoints + " / " + scavenging.PlacedPoints + " 处";
            }
        }

        /// <summary>交单奖励落在苇白脚边；位置由调用方给，避免奖励箱堆在玩家身上。</summary>
        internal bool DropBountyReward(Vector3 position, SkyIslandLootTier tier, int round)
        {
            return services != null && services.DropBountyReward(position, tier, round);
        }

        internal bool IsEncounterCleared(string id) { return encounters != null && encounters.IsCleared(id); }
        internal bool IsStoryChallengeActive(string id) { return encounters != null && encounters.IsBusy(id); }
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
        private void Status(string message, bool error)
        {
            if (hud != null) hud.text = message;
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
}
