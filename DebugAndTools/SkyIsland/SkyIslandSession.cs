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
    /// <remarks>F3 岛内验收的只读观测面在 SkyIslandSessionValidation.cs（同一个 partial 类）。</remarks>
    internal sealed partial class SkyIslandSession : MonoBehaviour, IInitializedQueryHandler
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
        private SkyIslandMapMarkers mapMarkers;
        // 撤离读条的显示桥：优先驱动官方 EvacuationCountdownUI，判定仍只在 Update 的撤离圈里。
        private SkyIslandExtractionCountdown extractionCountdown;
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
        private Transform playerSpawn, exitMarker, bellExit, windExit, starExit;
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
        private float enteredAt, nextGroundCheck, airborneSince = -1, nextHud;
        // 撤离圈里已停留的**游戏时间**秒数，-1 表示不在圈里。时基与官方 CountDownArea 一致（Time.time）：
        // 暂停菜单（GameManager.Paused）、拍照模式（CameraMode.Active）与剧情面板都会把 timeScale 压到 0，
        // Time.deltaTime 为 0，读条自然冻结；旧版用 unscaledTime，开着暂停菜单也会被送回基地。
        private float extractionHeld = -1;
        // HUD 文字读秒上一次写入的整秒数：整秒没变就不重建字符串（站在圈里每帧都会走到这里）。
        private int shownExtractionSeconds = -1;
        // 脚下地面碰撞体 → 区域 id（A–H / S1–S4）。生成器按区域把地面切成 COL_Ground_<区域>，
        // 桥（AB、CS1、K1…）不登记。装配时建一次表，之后每次地面射线只查表、不拼字符串。
        private readonly Dictionary<Collider, string> groundRegions = new Dictionary<Collider, string>();
        private readonly List<string> regionIds = new List<string>();
        // 玩家此刻站着的区域；走在桥上或腾空时保持上一个。null 表示还没踩到任何区域的地面。
        private string standingRegion;
        // FieldStatus 的脏检查输入：这些计数没变就复用上一次拼好的字符串（每 0.5 秒调用一次）。
        private int chipsOpened = int.MinValue, chipsPlaced, chipsActive, chipsProgress, chipsTarget, chipsRounds;
        private bool chipsChinese;
        private string chipsText;

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
                    "The Sky Islands raid scene bundle is missing — update the mod's assets");
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
                    // 异常原文是给维护者的中文诊断：完整异常进日志，提示条经 WithDetail，英文界面只给双语前缀。
                    Debug.LogWarning("[SkyIsland] setup failed: " + e);
                    Status(SkyIslandStoryRules.WithDetail(L10n.T("天空岛创建失败：", "Sky Islands setup failed"), e.Message), true);
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
            // 基地场景的组件不留成字段：出图即成已销毁引用。取到就交给租约克隆（CR-2026-09-10-003）。
            TimeOfDayConfig timeOfDayTemplate = LevelConfig.Instance.timeOfDayConfig;
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
            IndexGroundRegions();
            lighting = new SkyIslandLighting();
            lighting.Apply(root);
            root.SetActive(true);
            Physics.SyncTransforms();
            // CR-2026-09-10-006：地形不可见的现场取证。激活当帧 `isVisible` 还没被剔除结果更新过，
            // 所以两处都打：这里是「装配完成」的静态事实，导航扫描之后那次才有真实的可见性。
            SkyIslandRendering.LogDiagnostics(root, groundLayer, wallLayer, "activated");
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
            // 导航扫描跨了很多帧，相机已经剔除过若干次：这一次的 `visible` 才是可信的。
            SkyIslandRendering.LogDiagnostics(root, groundLayer, wallLayer, "post_scan");
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
                EncounterWasSaved, OnEncounterCleared, Status, EncounterLabel, OnStormDefeated);
            raidSeed = unchecked(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f));
            services = new SkyIslandServices(player, root, groundMask, raidSeed);
            // 撤离点的地面标识。半径就是 ExtractionRadius，圈内即判定内。
            // 纯表现层，单独持有 owner：画不出来不拖垮旅程，玩家仍可正常撤离。
            Safe("extraction_rings", delegate
            {
                extractionRings = new SkyIslandExtractionRings(root.transform, exitMarker, bellExit,
                    ExtractionRadius, groundMask);
                extractionRings.Apply(BellExitIfUnlocked() != null);
                extractionRings.AddBeaconRings(root.transform, windExit, starExit, ExtractionRadius, groundMask);
                extractionRings.ApplyBeacons(WindExitIfUnlocked() != null, StarExitIfUnlocked() != null);
            });
            // 官方地图上的撤离点与当前目标：与撤离圈同一事实源，纯表现层，单独持有 owner。
            Safe("map_markers", delegate { mapMarkers = new SkyIslandMapMarkers(root.transform, Status); });
            // 撤离读条同样是纯表现层、单独持有 owner：官方控件接不上时退回 HUD 文字读秒，撤离照常。
            Safe("extraction_countdown", delegate
            {
                extractionCountdown = new SkyIslandExtractionCountdown(root.transform, ExtractionHold);
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
            // 就绪不再另发一条「已就绪 · 地图键查阅全岛 · 当前目标」：落地大标题（带一次性操作提示）
            // 与右侧目标卡已经把这两件事说完了，再补一条等于同一句话在同一秒出现三遍。
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
                Debug.LogWarning("[SkyIsland] standalone level assembly failed: " + e);
                Status(SkyIslandStoryRules.WithDetail(L10n.T("独立关卡装配失败：", "Standalone level assembly failed"), e.Message), true);
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
            // 两处航标撤离借用岛心的 Region_D / Region_G（运行时别处不读它们），不新增场景节点；缺了只是少两个出口。
            windExit = root.transform.Find("Region_D");
            starExit = root.transform.Find("Region_G");
            if (playerSpawn == null || exitMarker == null || enemyMarkers.Count == 0 || landmarks.Count == 0 || searchCount == 0)
                throw new InvalidOperationException("出生/撤离/敌人/探索/地标点位缺失");
        }

        /// <summary>
        /// 区域判定的事实源是**脚下那块地**，不是「离哪个地标最近」。
        ///
        /// 生成器按区域把地面碰撞体切成 `COL_Ground_{区域}`（12 个岛 + 15 座桥，见
        /// tools/generate_sky_island.py），会话每 0.2 秒本来就朝脚下打一次地面射线找安全落脚点，
        /// 顺手查一下命中的是哪块地即可，不多打一条射线。
        ///
        /// 旧口径「离最近地标 60 米」按 ArtSource/SkyIsland/layout.json 的导航网格复算：主岛上只罩住
        /// 30–53% 的可走面积，其余地方卡片停在上一个岛；CS1 / FS3 两座桥的大半段与 C / F 两岛边缘
        /// 却能提前点亮 S1 / S3 的迷雾并推进「巡视群岛区域」（tests/SkyIslandRegionResolutionPropertyTest.py）。
        /// </summary>
        private void IndexGroundRegions()
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                // 桥（AB、CS1、K1…）不是区域：GroundRegionOf 返回 null，走在桥上保持上一个区域，不点亮、不记账。
                string id = SkyIslandStoryService.GroundRegionOf(collider.name);
                if (id == null) continue;
                groundRegions[collider] = id;
                if (!regionIds.Contains(id)) regionIds.Add(id);
            }
            // 缺切分只影响区域名与到访记账，不拖垮旅程：卡片不显示区域行，巡岛委托因可完成量为 0 不会派出。
            if (regionIds.Count == 0)
                Debug.LogWarning("[SkyIsland] 场景包里没有按区域切分的地面碰撞体，区域名与到访记录不可用");
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
            catch (Exception e)
            {
                if (created != null) Destroy(created.gameObject);
                Debug.LogWarning("[SkyIsland] spawn enemy failed: " + e);
                if (this != null && !closed) Status(SkyIslandStoryRules.WithDetail(L10n.T("生成敌人失败：", "Enemy spawn failed"), e.Message), true);
            }
            finally { if (clone != null) Destroy(clone, created != null ? 0.1f : 0f); if (this != null) spawning = false; }
        }

        internal void VisitNextLandmark()
        {
            if (closed || !ready || landmarks.Count == 0)
            { Status(WaitForReady, true); return; }
            Transform landmark = landmarks[nextLandmark++ % landmarks.Count];
            try { VerifyGround(landmark); }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] visit landmark failed: " + e);
                Status(SkyIslandStoryRules.WithDetail(L10n.T("前往下一个地标失败：", "Could not move to the next landmark"), e.Message), true);
                return;
            }
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
                Status(L10n.T("天空岛光色 · ", "Sky Islands lighting · ") + lighting.PresetName, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] lighting switch failed: " + e);
                Status(SkyIslandStoryRules.WithDetail(L10n.T("天空岛光色切换失败：", "Lighting switch failed"), e.Message), true);
            }
        }

        /// <summary>归航钟庭撤离点：双航标都点亮后开放（布局 v2 起不再等敲钟结局），未解锁返回 null。地图、地面环与撤离判定共用这一个事实源。</summary>
        internal Transform BellExitIfUnlocked()
        {
            return bellExit != null && story != null && story.Current.BothBeacons ? bellExit : null;
        }

        /// <summary>悬根林广场撤离点：风标点亮后开放。与钟庭同一口径，未解锁返回 null。</summary>
        internal Transform WindExitIfUnlocked()
        {
            return windExit != null && story != null && story.Current.Has(SkyIslandStoryFlag.WindBeacon) ? windExit : null;
        }

        /// <summary>残星工坊广场撤离点：星灯点亮后开放。与钟庭同一口径，未解锁返回 null。</summary>
        internal Transform StarExitIfUnlocked()
        {
            return starExit != null && story != null && story.Current.Has(SkyIslandStoryFlag.StarLamp) ? starExit : null;
        }

        /// <summary>撤离唯一判定：码头恒开，钟庭与两处航标广场按剧情解锁，都必须站进圈内。没有第二条返航入口。</summary>
        private bool IsInsideExtraction(out Transform marker)
        {
            marker = player == null ? null : ExtractionMarkerAt(player.transform.position);
            return marker != null;
        }

        /// <summary>
        /// 撤离判定的几何部分：给定坐标落在哪个**开放**的撤离圈里（码头恒开，钟庭与航标广场按剧情解锁），不在任何圈里返回 null。
        /// 玩家判定（<see cref="IsInsideExtraction"/>）与 F3 只读探测（<see cref="ValidationIsInsideExtractionAt"/>）
        /// 共用这一份：以前验收那边是逐字复制的第二份，生产判据改了验收照样绿。
        /// </summary>
        private Transform ExtractionMarkerAt(Vector3 position)
        {
            if (exitMarker != null && Vector3.Distance(position, exitMarker.position) < ExtractionRadius) return exitMarker;
            Transform bell = BellExitIfUnlocked();
            if (bell != null && Vector3.Distance(position, bell.position) < ExtractionRadius) return bell;
            Transform wind = WindExitIfUnlocked();
            if (wind != null && Vector3.Distance(position, wind.position) < ExtractionRadius) return wind;
            Transform star = StarExitIfUnlocked();
            if (star != null && Vector3.Distance(position, star.position) < ExtractionRadius) return star;
            return null;
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
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] open map failed: " + e);
                Status(SkyIslandStoryRules.WithDetail(L10n.T("官方地图打开失败：", "Could not open the game map"), e.Message), true);
            }
        }

        internal static void HideMapBeforeF3(ModBehaviour owner)
        {
            SkyIslandSession session = owner.GetComponent<SkyIslandSession>();
            if (session != null && session.worldStory != null) session.worldStory.Hide();
        }

        private void Update()
        {
            if (closed) return;
            // HUD 必须赶在下面几处提前 return 之前驱动：装配中、返航派发中、死亡后都要把它收起来，
            // 否则右侧卡片、区域大标题与字幕会浮在官方读条黑幕和撤离结算画面上面。
            // 淡入淡出只吃 unscaled 时间：剧情面板把 timeScale 压到 0 时显隐过渡照常走完。
            if (hud != null) hud.Tick(Time.unscaledDeltaTime, HudSuppressed());
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
                // 与官方界面同口径：剧情面板开着时撤离读条冻结而不是清零。
                // 读条走游戏时间，面板把 timeScale 压到 0，这里既不推进也不清零 extractionHeld。
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
            if (extractionRings != null) extractionRings.ApplyBeacons(WindExitIfUnlocked() != null, StarExitIfUnlocked() != null);
            if (mapMarkers != null) mapMarkers.Apply(story.Current, exitMarker, BellExitIfUnlocked(), WindExitIfUnlocked(), StarExitIfUnlocked());
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
                {
                    safePosition = hit.point + Vector3.up * 0.35f;
                    airborneSince = -1;
                    // 同一条射线顺手认出脚下是哪个区域；桥不在表里，走在桥上保持上一个。
                    string region;
                    if (groundRegions.TryGetValue(hit.collider, out region)) standingRegion = region;
                }
                // 腾空计时同样走游戏时间：开着暂停菜单或拍照模式时，不该把人「救」回落脚点。
                else if (airborneSince < 0) airborneSince = Time.time;
                else if (Time.time - airborneSince > 2.5f) { Rescue(); return; }
            }
            Transform extraction;
            bool insideExtraction = IsInsideExtraction(out extraction);
            if (Time.unscaledTime > enteredAt + 1 && insideExtraction)
            {
                if (extractionHeld < 0) extractionHeld = 0f;
                // 与官方 CountDownArea 同口径的两道门：
                // 1. 打开任何官方界面（背包、地图等）时不推进，冻结在原处而不是清零，免得开着背包被送回基地；
                // 2. 时基是游戏时间：暂停菜单、拍照模式、剧情面板把 timeScale 压到 0 时 Time.deltaTime 为 0，读条同样冻结。
                if (View.ActiveView == null) extractionHeld += Time.deltaTime;
                float remaining = ExtractionHold - extractionHeld;
                // 冻结期间照样把已停留秒数喂给官方读条：它按 Time.time 自己算进度，不喂就会在背包后面偷偷走完。
                ShowExtraction(remaining);
                if (remaining <= 0) Close(true, extraction == bellExit ? "bell_extract" : extraction == windExit ? "wind_extract" : extraction == starExit ? "star_extract" : "dock_extract");
                return;
            }
            // 真正离开圈子才归零。HUD 文字读秒的兜底行当场撤掉，不等下一次 0.5 秒刷新。
            if (extractionHeld >= 0 && hud != null) hud.SetExtraction(null);
            extractionHeld = -1;
            shownExtractionSeconds = -1;
            if (extractionCountdown != null) extractionCountdown.Hide();
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.5f;
                // 到访记账与区域名共用「脚下那块地」这一个事实源（见 IndexGroundRegions）：
                // 真正踏上某个岛才算到访，走在桥上保持上一个区域——既不会隔着桥提前点亮支路，
                // 也不会像更早的「谁更近」写法那样站在两个地标之间来回翻、让区域大标题连弹。
                if (standingRegion != null && story.RecordRegionVisited(standingRegion))
                {
                    bounty.ReportRegionVisited();
                    // 刚踏足的区域立刻在官方地图上点亮。
                    mapFog.Apply(story.Current.visitedRegions);
                }
                if (hud != null)
                {
                    // 不在圈里就把读秒整行撤掉，卡片不留空位。
                    hud.SetExtraction(null);
                    if (standingRegion != null) hud.SetRegion(RegionLabel(standingRegion));
                    hud.SetObjective(story.CurrentObjective);
                    hud.SetChips(FieldStatus());
                    // 存档状态**正常时一个字都不说**：只有真出问题（写屏障 / 单向故障 / 换槽）
                    // 才值得占玩家一行。旧版把「群岛记录已同步」也常驻着，等于每帧都在报平安。
                    // 走卡片里的常驻状态行而不是字幕：问题解决前一直成立，每半秒重播一次字幕
                    // 会把同一时间里的 Boss 机制提示与战斗门控原因全部挤掉。
                    hud.SetStatus(story != null && !story.CanWrite ? story.SaveStatus : null);
                }
            }
        }

        /// <summary>
        /// 会话侧的 HUD 隐藏条件。官方界面、对话、拍照模式那一组由 HUD 自己照抄官方 HUDManager 判断；
        /// 这里只补会话才知道的：还没就绪、已经在返航、主角死亡、剧情面板开着（面板有自己的整屏遮罩）。
        /// </summary>
        private bool HudSuppressed()
        {
            return !ready || returnRequested || deathPending || (worldStory != null && worldStory.Visible);
        }

        /// <summary>
        /// 撤离读条的显示。优先交给官方 EvacuationCountdownUI——与原版出口同一个圆环读条、同一个位置；
        /// 官方控件不在场或反射字段对不上时，才退回 HUD 卡片里的文字读秒。
        /// 这里只负责「给玩家看」，判定仍唯一留在 Update 的撤离圈里（CR-2026-09-08-002）。
        /// </summary>
        private void ShowExtraction(float remaining)
        {
            if (extractionCountdown != null && extractionCountdown.Show(ExtractionHold - remaining))
            {
                if (hud != null) hud.SetExtraction(null);
                return;
            }
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            if (hud == null || seconds == shownExtractionSeconds) return;
            shownExtractionSeconds = seconds;
            hud.SetExtraction(L10n.T("返回基地 · ", "Returning to base · ") + seconds + L10n.T(" 秒", "s"));
        }

        /// <summary>HUD 第三行：搜刮进度与在手委托，让「这趟出击还能做什么」一眼可见。</summary>
        /// <remarks>每 0.5 秒调用一次。输入计数都没变就复用上一次的字符串：拼一次要新建六七个小字符串。</remarks>
        private string FieldStatus()
        {
            int opened = scavenging == null ? -1 : scavenging.OpenedPoints;
            int placed = scavenging == null ? -1 : scavenging.PlacedPoints;
            int active = (int)bounty.Active, progress = bounty.Progress, target = bounty.Target, rounds = bounty.CompletedRounds;
            bool chinese = L10n.IsChinese;
            if (chipsText != null && opened == chipsOpened && placed == chipsPlaced && active == chipsActive &&
                progress == chipsProgress && target == chipsTarget && rounds == chipsRounds && chinese == chipsChinese)
                return chipsText;
            chipsOpened = opened; chipsPlaced = placed; chipsActive = active;
            chipsProgress = progress; chipsTarget = target; chipsRounds = rounds; chipsChinese = chinese;
            string loot = scavenging == null
                ? L10n.T("物资点 --", "Caches --")
                : L10n.T("物资 ", "Caches ") + opened + "/" + placed;
            string contract = bounty.HasActive
                ? L10n.T(" · 委托 ", " · Contract: ") + bounty.Describe()
                : (rounds > 0
                    ? L10n.T(" · 已交委托 ", " · Contracts delivered: ") + rounds
                    : L10n.T(" · 风铃集可接委托", " · Contracts at Windchime Market"));
            chipsText = loot + contract;
            return chipsText;
        }

        private void Rescue()
        {
            player.SetPosition(safePosition);
            airborneSince = -1;
            extractionHeld = -1;
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
            if (name.Length > 4 && name[4] >= 'A' && name[4] <= 'H') return MainRegionLabel(name[4]);
            if (name == "POI_S1") return RegionLabel("S1");
            if (name == "POI_S2") return RegionLabel("S2");
            if (name == "POI_S3") return RegionLabel("S3");
            if (name == "POI_S4") return RegionLabel("S4");
            return name.Replace("POI_", "").Replace('_', ' ');
        }

        /// <summary>
        /// 区域 id（A–H / S1–S4）→ 玩家看得懂的地名。每 0.5 秒的 HUD 刷新都会走到这里，
        /// 所以不再像旧写法那样每次 new 两个八元素数组，全部是直接返回字面量的分支。
        /// </summary>
        internal static string RegionLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (id.Length == 1 && id[0] >= 'A' && id[0] <= 'H') return MainRegionLabel(id[0]);
            switch (id)
            {
                case "S1": return L10n.T("蛙鸣池", "Frogsong Pool");
                case "S2": return L10n.T("倒挂邮亭", "Upturned Post Hut");
                case "S3": return L10n.T("听雨洞", "Rainlisten Grotto");
                case "S4": return L10n.T("残星瞭台", "Starfall Overlook");
                default: return id.Replace('_', ' ');
            }
        }

        private static string MainRegionLabel(char region) { return L10n.T(MainRegionCn(region), MainRegionEn(region)); }

        private static string MainRegionCn(char region)
        {
            switch (region)
            {
                case 'A': return "登云码头";
                case 'B': return "风铃集";
                case 'C': return "青穗梯田";
                case 'D': return "悬根林";
                case 'E': return "鸣风栈道";
                case 'F': return "镜水寺";
                case 'G': return "残星工坊";
                default: return "归航钟庭";
            }
        }

        private static string MainRegionEn(char region)
        {
            switch (region)
            {
                case 'A': return "Cloudrise Dock";
                case 'B': return "Windchime Market";
                case 'C': return "Green Terraces";
                case 'D': return "Hanging Root Wood";
                case 'E': return "Windsong Boardwalk";
                case 'F': return "Mirrorwater Temple";
                case 'G': return "Fallen Star Workshop";
                default: return "Homecoming Bell Court";
            }
        }
        /// <summary>
        /// 遭遇 id → 玩家看得懂的名字。id 是 World.json 里的内部键（`C_02` / `S1` / `Zheling`），
        /// 直接拼进「航路已清理 · C_02」等于把调试键名念给玩家听。
        /// 区域遭遇取所在地标名，具名对手取角色名；解析不出来时落到 LandmarkLabel 的兜底写法。
        /// </summary>
        internal static string EncounterLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (id == "Zheling") return SkyIslandWorldStory.ResidentName("sky_zheling");
            if (id == "BellKeeper") return L10n.T("守钟装置", "the bell engine");
            if (id == "Storm") return L10n.T("噬风", "the Windeater");
            int underscore = id.IndexOf('_');
            return LandmarkLabel("POI_" + (underscore < 0 ? id : id.Substring(0, underscore)));
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
            Safe("extraction_countdown_hide", delegate { if (extractionCountdown != null) extractionCountdown.Hide(); });
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
            // 原版 CountDownArea 在圈里的人全部倒下时立刻中止读条（UpdateCountDown → AbortCountDown → Release）。
            // ready 一落 Update 就不再走撤离分支，不主动收起的话官方读条会留在屏幕上自己读到 00:00。
            Safe("extraction_countdown_hide", delegate { if (extractionCountdown != null) extractionCountdown.Hide(); });
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
                // 与官方撤离成功同口径：读条控件随成功一起收掉，不停在 00:00 一直挂到黑幕落下。
                if (extractionCountdown != null) extractionCountdown.Hide();
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
            Safe("map_markers", delegate { if (mapMarkers != null) mapMarkers.Dispose(); });
            Safe("extraction_countdown", delegate { if (extractionCountdown != null) extractionCountdown.Dispose(); });
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
                // 数的是**区域**（脚下地面切分出来的 A–H / S1–S4），不是 POI_ 节点：场景包里还有一个
                // POI_B_Mural（风铃集壁画），按节点数会把它当成永远去不了的第 13 个区域——
                // 剩 3 个真区域时可完成量算成 4，恰好派得出一张做不完的「巡视群岛区域 ×4」。
                int count = 0;
                for (int i = 0; i < regionIds.Count; i++)
                    if (!story.HasVisitedRegion(regionIds[i])) count++;
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
            // 噬风自己的尸体箱（官方 CharacterMainControl.OnDead → InteractableLootbox.CreateFromItem）落在
            // 它倒下的位置 +0.1 m。奖励箱照原坐标放会与它几乎同点：官方 CA_Interact 按到交互体轴心的距离
            // 严格小于取唯一目标，两个箱子里总有一个整局都选不中。退开一个交互间距，退不开才落回原点。
            Vector3 drop;
            if (!SkyIslandRewardCrate.TryFindCratePosition(root.transform, position,
                SkyIslandLootTables.StableHash("StormTrophy") % 360, SkyIslandRewardCrate.InteractableSeparation,
                groundMask, out drop)) drop = position;
            SkyIslandStormBoss.DropTrophy(root.transform, drop, raidSeed);
        }
        // F3 天空岛验收的只读观测面（Validation* 成员与 SkyIslandValidationSnapshot）在 SkyIslandSessionValidation.cs。

        /// <summary>风标罗盘（物品 500070）：读数由剧情 owner 给、走本岛唯一提示出口；不在有效出击里返回 false，由物品自己提示。</summary>
        internal bool UseCompass()
        {
            if (!IsSessionValid() || worldStory == null) return false;
            Status(worldStory.CompassReading(player.transform.position), false);
            return true;
        }

        /// <summary>剧情 owner 的对外提示通道：与其它天空岛消息共用同一个出口（见 Status），不另开一套。</summary>
        internal void Announce(string message, bool error) { Status(message, error); }
        /// <summary>
        /// 天空岛的对外提示只走**一个**出口，不再同时发两处：
        /// - 岛上就绪后 → 本 HUD 的中下方字幕（按字数停留、排队不吞）。官方 NotificationText 只停 1.2 秒，
        ///   岛上的长句（Boss 机制提示、战斗门控原因）根本读不完；两处同时出现又是同一句话说两遍。
        /// - 装配中 / 返航中 / 死亡或失败清理 → 交给调用方的 report（正式入口就是官方 NotificationText）：
        ///   这时 HUD 要么还没内容、要么已被收起、要么马上随会话销毁，字幕来不及播。
        /// </summary>
        private void Status(string message, bool error)
        {
            if (hud != null && ready && !returnRequested) hud.Caption(message, error);
            else if (report != null) report(message, error);
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
