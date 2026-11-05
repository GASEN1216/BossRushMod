using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.MiniMaps.UI;
using Duckov.UI;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：潮闸镇独立出击的 owner。生命周期照天空岛会话（`SkyIslandSession`）：
    /// 基地克隆天气 → 租约预载包 → 官方 SceneLoader 切图 → sceneLoaded 里登记初始化等待 → 装配层 / 光照 / 导航 →
    /// 官方合同 → 就绪；撤离走官方结算顺序，死亡交给官方 `CharacterDieTask`，清理在场景真正卸载后。
    /// 天空岛的剧情、居民、委托等都不在这里；潮汐、阀门、灯塔等本图玩法各自持有 owner。
    /// </summary>
    internal sealed class TidegateSession : MonoBehaviour, IInitializedQueryHandler
    {
        private ModBehaviour host;
        private Action<string, bool> report;
        private ModRaidProfile profile;
        private ModRaidLease lease;
        private GameObject root;
        private Scene entryScene;
        private CharacterMainControl player;
        private Health playerHealth;
        private ArenaPrototypeNavigation navigation;
        private IEnumerator<Progress> scan;
        private Seeker probe;
        private SkyIslandHud hud;
        private SkyIslandLighting lighting;
        private TidegateWorld world;
        private TidegateTide tide;
        private TidegateExtraction extraction;
        private TidegateLoot loot;
        private TidegateEnemies enemies;
        private TidegateMap map;
        private TidegateWater water;
        private TidegateDevices devices;
        private TidegateSounds sounds;
        private GameObject returnInputBlock;
        /// <summary>HUD 地名：走进地标这么近才换，离开或在两地标之间保持上一个，新地标要近这么多才换。</summary>
        private const float RegionRadius = 24f, RegionSwitchMargin = 4f;
        /// <summary>泻湖水位低于这个高度（金库地面约 -0.95 m + 深水 0.5 m）时，储蓄所金库露出水面、能进去搜。</summary>
        private const float VaultDryLevel = -0.45f;
        private Transform regionPoi;
        /// <summary>这一轮已经报过的换相前提示；换相时清零（每轮一次涨潮警报、一次渡口要走）。</summary>
        private TidegateTideNotice noticeShown;
        /// <summary>身上（整棵物品树）带没带潮汐怀表：两秒查一次，HUD 倒计时与目标行共用。</summary>
        private bool carryingWatch;
        private float nextWatchCheck;
        private string assemblyError, firstReturnReason;
        private bool subscribed, closed, ready, moved, navigationReady, returning, returnRequested, deathPending, loadStarted;
        private bool pathCompleted, pathValid;
        /// <summary>这一趟已经收尾。撤离返航时组件要等返航走完才销毁，基地关卡就绪那一刻它还在，判「还在出击」要看这个。</summary>
        internal bool IsClosed { get { return closed; } }
        private int groundMask;
        private float enteredAt, nextHud, nextGroundCheck;
        private Vector3 safePosition;
        private readonly HashSet<Transform> unreachable = new HashSet<Transform>();

        internal bool IsReady { get { return IsSessionValid(); } }
        internal TidegateTide Tide { get { return tide; } }

        internal static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = null;
            if (owner == null) { reason = L10n.T("Mod 还没准备好", "The mod is not ready yet"); return false; }
            if (!TidegateRaidScene.IsBundleDeployed())
            {
                reason = L10n.T("少了潮闸镇的出击场景包，先更新 Mod 资源", "The Tidegate raid scene bundle is missing. Update the mod's assets");
                return false;
            }
            if (owner.GetComponent<TidegateSession>() != null || owner.GetComponent<SkyIslandSession>() != null
                || owner.GetComponent<ArenaPrototypeSession>() != null)
            {
                reason = L10n.T("先结束当前的场景旅程，等它收拾完", "End the current scene journey and wait for it to clean up");
                return false;
            }
            if (SkyIslandStorySaveRecovery.IsPending())
            {
                reason = L10n.T("上一段出击记录还在保存，稍后再试", "The previous raid record is still saving. Try again shortly");
                return false;
            }
            if (F3GameplayValidationRunner.IsRunning)
            { reason = L10n.T("请等待完整玩法验收结束", "Wait for the full gameplay validation to finish"); return false; }
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited)
            { reason = L10n.T("场景和玩家还在加载，稍等一下", "The scene and the player are still loading. Hold on a moment"); return false; }
            string mode;
            if (owner.ValidationHasActiveMode(out mode))
            { reason = L10n.T("先结束正在进行的模式", "End the current mode first"); return false; }
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.Health == null || main.Health.IsDead)
            { reason = L10n.T("玩家还没准备好", "The player is not ready"); return false; }
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name))
            { reason = L10n.T("先回基地，再去潮闸镇", "Return to base before heading to Tidegate"); return false; }
            if (GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
            { reason = L10n.T("游戏相机还没准备好", "The game camera is not ready yet"); return false; }
            return true;
        }

        internal static void Enter(ModBehaviour owner, Action<string, bool> status)
        {
            string reason;
            if (!CanEnter(owner, out reason)) { status(reason, true); return; }
            TidegateSession session = owner.gameObject.AddComponent<TidegateSession>();
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
                    string notice = SkyIslandStoryRules.WithDetail(L10n.T("潮闸镇没能建起来：", "Tidegate setup failed"), e.Message);
                    RememberReturnReason("build_failed: " + e.Message, notice);
                    CancelPendingInitialization();
                    Debug.LogWarning(TidegateRaidScene.LogTag + " setup failed: " + e);
                    Status(notice, true);
                    Close(true, "build_failed"); yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }

        private IEnumerator Build()
        {
            Safe("hud", delegate
            {
                hud = new SkyIslandHud(host.transform, delegate { return L10n.T("潮闸镇", "TIDEGATE"); });
                hud.SetLandingHint(L10n.T("地图键看全镇 · 站进撤离环读完条返航",
                    "Map key: see the town · stand in an extraction ring until the bar fills"));
            });
            Status(L10n.T("正在前往潮闸镇…", "Heading to Tidegate…"), false);
            // 基地场景的组件不留成字段：出图即成已销毁引用。取到就交给租约克隆（CR-2026-09-10-003）。
            TimeOfDayConfig timeOfDayTemplate = LevelConfig.Instance.timeOfDayConfig;
            if (timeOfDayTemplate == null) throw new InvalidOperationException("官方天气配置缺失，无法创建完整关卡");
            profile = TidegateRaidScene.CreateProfile();
            lease = new ModRaidLease(profile);
            lease.BeginPrepare(ModBehaviour.GetModPath(), timeOfDayTemplate);
            while (lease.StepPreparation()) yield return null;
            loadStarted = true;
            lease.BeginLoad();
            float deadline = Time.realtimeSinceStartup + 120;
            while (root == null && lease.Error == null && assemblyError == null && lease.HasSceneLoadTimeRemaining(ref deadline))
            {
                // 官方 SceneLoader 遇到「已在切图」或身份未登记时只记一条 LogError 就同步返回：按「未起航」清理，不空等。
                if (lease.LoadFinished) { loadStarted = false; throw new InvalidOperationException("官方场景加载器拒绝了本次出击，请稍后再试"); }
                yield return null;
            }
            if (assemblyError != null) throw new InvalidOperationException(assemblyError);
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (root == null) throw new TimeoutException("潮闸镇场景未能就绪");
            // sceneLoaded 先于官方 SetActiveScene；协程不能依靠 UniTask continuation 与 Update 的偶然顺序。
            deadline = Time.realtimeSinceStartup + 120f;
            while ((SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null ||
                GameCamera.Instance.renderCamera == null) && lease.Error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
                throw new TimeoutException("潮闸镇活动场景或官方相机未能就绪");
            if (root.transform.position != Vector3.zero || root.transform.localScale != Vector3.one || root.transform.rotation != Quaternion.identity)
                throw new InvalidOperationException("潮闸镇场景根节点变换未归一化");
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            world = new TidegateWorld(root);
            world.ApplyLayers();
            lighting = new SkyIslandLighting();
            lighting.Apply(root);
            tide = new TidegateTide(world);
            tide.Tick(0f);
            // 涨退潮是这张图的主机制，换相要有字幕与警报（CR-2026-11-05-017）；首个 Tick 只定相位、不触发。
            tide.PhaseChanged += OnTidePhaseChanged;
            Physics.SyncTransforms();
            world.VerifyGround(groundMask);
            Transform nav = root.transform.Find("Navigation");
            if (nav == null || nav.localPosition != Vector3.zero || nav.localScale != Vector3.one || nav.localRotation != Quaternion.identity)
                throw new InvalidOperationException("Navigation 必须位于世界根下且为单位变换");
            MeshFilter filter = nav.GetComponent<MeshFilter>();
            if (filter == null) throw new InvalidOperationException("潮闸镇导航网格缺失");
            navigation = new ArenaPrototypeNavigation();
            // 其它关卡组件可能仍在扫描共享 A*；它结束后再接专属图，不能把短暂 busy 当装配失败返航。
            deadline = Time.realtimeSinceStartup + 30f;
            while (global::AstarPath.active == null || global::AstarPath.active.data == null || global::AstarPath.active.isScanning)
            {
                if (lease.Error != null) throw new InvalidOperationException(lease.Error);
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("潮闸镇导航服务未能就绪");
                yield return null;
            }
            scan = navigation.BeginScan(filter.sharedMesh, Vector3.zero);
            deadline = Time.realtimeSinceStartup + 30;
            while (scan.MoveNext())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("潮闸镇导航扫描超时");
                yield return null;
            }
            scan.Dispose();
            scan = null;
            if (navigation.CountWalkableNodes() == 0) throw new InvalidOperationException("潮闸镇导航没有可走节点");
            // 官方物资池与敌人武器池趁读条预热，每个池子占一帧（口径同天空岛 CR-2026-09-14-014）：懒建时第一只箱子、
            // 第一名守卫出来那一帧要现查官方物品表（CR-2026-11-05-028）。预热失败只记一次，箱子照常懒建。
            IEnumerator warm = null;
            Safe("loot_prewarm", delegate { warm = SkyIslandLootPools.Prewarm(); });
            while (warm != null)
            {
                bool more = false;
                object current = null;
                try { more = warm.MoveNext(); if (more) current = warm.Current; }
                catch (Exception e) { TickFault("loot_prewarm", e); }
                if (!more) break;
                yield return current;
            }
            Safe("loot_prewarm_release", delegate { (warm as IDisposable)?.Dispose(); });
            // 低潮全图连通：从出生点到每个刷怪点探一次路（潮位封锁在下面才挂上，这里看的是完整的图）。
            // 与天空岛不同，这里软失败：够不着的点本趟不刷、记一行日志给场景包那一侧修，不让一个点拖垮整趟出击。
            probe = root.AddComponent<Seeker>();
            foreach (Transform marker in world.SpawnMarkers)
            {
                pathCompleted = pathValid = false;
                probe.StartPath(world.PlayerSpawn.position, marker.position, OnProbePath, navigation.Mask);
                deadline = Time.realtimeSinceStartup + 10;
                while (!pathCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (pathCompleted && pathValid) continue;
                unreachable.Add(marker);
                Debug.LogWarning(TidegateRaidScene.LogTag + " NAV_UNREACHABLE " + marker.name + " at=" + marker.position);
            }
            if (unreachable.Count == world.SpawnMarkers.Count) throw new InvalidOperationException("导航从出生点一个刷怪点都到不了");
            tide.BindNavigation(navigation);
            navigationReady = true;
            deadline = Time.realtimeSinceStartup + 120;
            while ((!lease.LoadFinished || !LevelManager.AfterInit) && lease.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
            // 官方 InitLevel 的异常不会传给等待 LevelInited 的外层加载；超时必须主动给它一个取消信号（CR-2026-09-08-005）。
            if (lease.Error == null && (!lease.LoadFinished || !LevelManager.AfterInit))
                lease.Abort("官方关卡初始化超时，潮闸镇已请求取消");
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            string contractError;
            if (!ModRaidOfficialContract.Verify(profile, entryScene, out contractError))
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
            carryingWatch = TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
            nextWatchCheck = Time.unscaledTime + 2f;
            // 落地横幅的提示：没带怀表的人告诉他怀表有什么用（倒计时只在带着怀表时出现在小标行）。
            if (hud != null && !carryingWatch)
                Safe("landing_hint", delegate
                {
                    hud.SetLandingHint(L10n.T("地图键看全镇 · 站进撤离环读完条返航 · 带潮汐怀表能看涨落倒计时",
                        "Map key: see the town · stand in an extraction ring until the bar fills · a tide pocket watch shows the countdown"));
                });
            safePosition = world.PlayerSpawn.position + Vector3.up * 0.15f;
            moved = ready = true;
            enteredAt = Time.unscaledTime;
            int seed = unchecked(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f));
            // 撤离、搜刮、刷怪各自持有 owner：表现层或单项装配失败不拖垮整趟出击，撤离环与公路检查站始终在。
            Safe("map", delegate { map = new TidegateMap(world); });
            Safe("water", delegate { water = new TidegateWater(world, tide, player, IsSessionValid); });
            Safe("extraction", delegate { extraction = new TidegateExtraction(world, tide, groundMask); });
            Safe("loot", delegate { loot = new TidegateLoot(world, tide, seed, IsSessionValid, Status); });
            Safe("enemies", delegate { enemies = new TidegateEnemies(world, tide, navigation, player, seed, IsSessionValid, unreachable); });
            Safe("sounds", delegate { sounds = new TidegateSounds(world); });
            Safe("devices", delegate
            {
                devices = new TidegateDevices(world, tide, extraction, enemies, sounds, IsSessionValid, Status);
                if (loot != null) loot.Opened += OnLootOpened;
            });
            Debug.Log(TidegateRaidScene.LogTag + " ENTER_PASS scene=" + entryScene.path + " nodes=" + navigation.CountWalkableNodes() +
                " loot=" + world.LootMarkers.Count + " spawns=" + world.SpawnMarkers.Count + " exits=" + world.Exits.Count +
                " tide=" + tide.Level.ToString("F2") + " phase=" + tide.Phase);
        }

        public bool HasInitialized() { return navigationReady || closed; }

        private bool IsSessionValid()
        {
            return this != null && !closed && ready && !returning && !deathPending && root != null && player != null
                && player == CharacterMainControl.Main && !SceneLoader.IsSceneLoading;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (closed || !TidegateRaidScene.IsScene(scene)) return;
            try
            {
                entryScene = scene;
                foreach (GameObject candidate in scene.GetRootGameObjects())
                    if (candidate.name == TidegateRaidScene.WorldRootName) root = candidate;
                if (root == null) throw new InvalidOperationException("独立场景缺少地形根节点");
                LevelManager.RegisterWaitForInitialization(this);
            }
            catch (Exception e)
            {
                navigationReady = true;
                assemblyError = e.Message;
                Debug.LogWarning(TidegateRaidScene.LogTag + " standalone level assembly failed: " + e);
                Status(SkyIslandStoryRules.WithDetail(L10n.T("关卡没能搭起来：", "Standalone level assembly failed"), e.Message), true);
            }
        }

        private void OnProbePath(Path path)
        {
            if (closed) return;
            pathCompleted = true;
            pathValid = !path.error && path.vectorPath != null && path.vectorPath.Count >= 2;
        }

        private void Update()
        {
            if (closed) return;
            if (hud != null) hud.Tick(Time.unscaledDeltaTime, !ready || returnRequested || deathPending);
            if (returnRequested) { DispatchReturnIfReady(); return; }
            if (!ready) return;
            if (player == null || player != CharacterMainControl.Main || root == null || !entryScene.isLoaded)
            { Close(false, "owner_lost"); return; }
            string mode;
            if (host == null || host.ValidationHasActiveMode(out mode)) { Close(true, "mode_changed"); return; }
            // 各子系统各自隔离异常；直接 try/catch，不在每帧路径上分配委托。
            try { if (lighting != null) lighting.Tick(); } catch (Exception e) { TickFault("lighting", e); }
            try { if (tide != null) tide.Tick(Time.deltaTime); } catch (Exception e) { TickFault("tide", e); }
            try { if (water != null) water.Tick(); } catch (Exception e) { TickFault("water", e); }
            try { if (loot != null) loot.Tick(player); } catch (Exception e) { TickFault("loot", e); }
            try { if (enemies != null) enemies.Tick(); } catch (Exception e) { TickFault("enemies", e); }
            try { if (devices != null) devices.Tick(); } catch (Exception e) { TickFault("devices", e); }
            try { if (map != null) map.Tick(); } catch (Exception e) { TickFault("map", e); }
            Vector3 position = player.transform.position;
            try { if (sounds != null) sounds.Tick(position); } catch (Exception e) { TickFault("sounds", e); }
            // 四边有墙（TidegateWorld.AddBoundary），这里只兜物理穿模或掉出世界：出了可玩区 8 m 或掉到 -14 m 以下拉回落脚点。
            if (position.y < -14f || TidegateWorld.OutsidePlayArea(position)) { Rescue(); return; }
            if (Time.unscaledTime >= nextGroundCheck)
            {
                nextGroundCheck = Time.unscaledTime + 0.25f;
                RaycastHit hit;
                if (Physics.Raycast(position + Vector3.up * 0.25f, Vector3.down, out hit, 1.4f, groundMask, QueryTriggerInteraction.Ignore)
                    && hit.transform.IsChildOf(root.transform))
                    safePosition = hit.point + Vector3.up * 0.35f;
            }
            if (extraction != null && Time.unscaledTime > enteredAt + 1f)
            {
                string exitId;
                // 与官方 CountDownArea 同口径：任何官方界面打开时冻结不清零，时基是游戏时间（暂停与拍照模式压 timeScale 到 0）。
                if (extraction.Tick(player.transform.position, View.ActiveView == null ? Time.deltaTime : 0f, out exitId))
                {
                    Close(true, "extract_" + exitId);
                    return;
                }
                // 官方读条接管时这里是 null（整行收掉）；HUD 自己按字符串比较去重，每帧调不重排版。
                if (hud != null) hud.SetExtraction(extraction.HudFallback);
            }
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.5f;
                if (Time.unscaledTime >= nextWatchCheck)
                {
                    nextWatchCheck = Time.unscaledTime + 2f;
                    carryingWatch = TidegateInventory.Carries(player, BossRushItemIds.TidegateTideWatch);
                }
                TickTideNotices();
                if (hud != null && tide != null)
                {
                    UpdateRegion(player.transform.position);
                    hud.SetObjective(Objective());
                    hud.SetChips(FieldStatus());
                }
            }
        }

        /// <summary>
        /// 换相字幕（原型 game.js 的 onPhase）：涨潮用警示色并拉警报，高潮在渡口拉汽笛。只在出击进行中报；
        /// 换相清零本轮的提前提示，下一轮再报一次（CR-2026-11-05-017）。
        /// </summary>
        private void OnTidePhaseChanged(TidegateTidePhase previous, TidegateTidePhase next)
        {
            noticeShown = TidegateTideNotice.None;
            if (!IsSessionValid()) return;
            switch (next)
            {
                case TidegateTidePhase.Ebb:
                    Status(L10n.T("开始退潮了。", "The tide is going out."), false);
                    break;
                case TidegateTidePhase.Low:
                    Status(L10n.T("低潮：滩涂、沉船货舱和排水暗渠都露出来了，淘沙帮下滩了。",
                        "Low tide: the flats, the wreck's hold and the drain culvert are above water. The sand gang is heading out."), false);
                    break;
                case TidegateTidePhase.Flood:
                    Status(L10n.T("涨潮了！滩涂很快就是深水，背得重的赶紧上岸。",
                        "The tide is coming in! The flats will be deep water soon. Get ashore if you're carrying a heavy load."), true);
                    if (sounds != null) sounds.Play(TidegateCue.Siren, null);
                    break;
                default:
                    Status(L10n.T("高潮：栈桥渡口来船了，可以从栈桥撤离。", "High tide: a boat is at the ferry pier. You can leave from there."), false);
                    Transform ferry;
                    if (sounds != null && world.Exits.TryGetValue("ferry", out ferry)) sounds.Play(TidegateCue.Horn, ferry.position);
                    break;
            }
        }

        /// <summary>换相前的提前提示：低潮最后 15 秒拉涨潮警报，高潮最后 12 秒提醒渡口的船要走；每轮各一次。</summary>
        private void TickTideNotices()
        {
            if (tide == null) return;
            TidegateTideNotice notice = TidegateTide.Notice();
            if (notice == TidegateTideNotice.None || notice == noticeShown) return;
            noticeShown = notice;
            int seconds = Mathf.Max(1, Mathf.CeilToInt((float)TidegateTide.RealSecondsUntilNextPhase()));
            if (notice == TidegateTideNotice.FloodSoon)
            {
                Status(L10n.T("潮汐警报：", "Tide alarm: the flood starts in ") + seconds + L10n.T(" 秒后涨潮，滩涂上的往高处走。", " s. Get off the flats."), true);
                if (sounds != null) sounds.Play(TidegateCue.Siren, null);
                return;
            }
            Status(L10n.T("潮水快退了，渡口的船再过 ", "The tide is turning. The ferry leaves in ") + seconds + L10n.T(" 秒就走。", " s."), false);
        }

        /// <summary>
        /// HUD 地名：走进某个地标 24 m 内才换；离开所有地标、或站在两个地标之间时保持上一个，新地标要近 4 m 以上才换。
        /// 遵守 `SkyIslandHud.SetRegion` 的约定，免得地名来回跳、游在外海也报地名（CR-2026-11-05-025）。
        /// </summary>
        private void UpdateRegion(Vector3 position)
        {
            float distance;
            Transform nearest = world.NearestPoi(position, out distance);
            if (nearest != null && nearest != regionPoi && distance <= RegionRadius)
            {
                float current = float.MaxValue;
                if (regionPoi != null)
                {
                    Vector3 offset = regionPoi.position - position;
                    offset.y = 0f;
                    current = offset.magnitude;
                }
                if (current > RegionRadius || distance + RegionSwitchMargin < current) regionPoi = nearest;
            }
            hud.SetRegion(regionPoi == null ? TidegateRaidScene.DisplayName : TidegateText.PlaceName(regionPoi.name));
        }

        /// <summary>
        /// HUD 目标行：按进度给下一步，只在进度变化时换字，「目标更新」才名副其实（CR-2026-11-05-018）。
        /// 潮相、潮位与怀表倒计时放小标行（<see cref="FieldStatus"/>）。
        /// </summary>
        private string Objective()
        {
            if (carryingWatch && !TidegateStory.WatchDelivered)
                return L10n.T("带着潮汐怀表撤离，回地堡下水道交给闸伯", "Extract with the tide pocket watch and bring it to Old Zha in the bunker sewer");
            if (devices != null && tide != null && !tide.SluiceOpen && devices.ValveCount > 0)
            {
                string count = L10n.T("（", " (") + devices.OpenValves + "/" + devices.ValveCount + L10n.T("）", ")");
                bool lastIsGauge = !devices.TideGaugeOpen && devices.OpenValves == devices.ValveCount - 1;
                if (lastIsGauge && TidegateInventory.Find(player, BossRushItemIds.TidegateHandwheel) != null)
                    return L10n.T("带着阀门手轮去灯塔岬的潮位站，拧开最后一处阀门", "Take the handwheel to the tide gauge station on the cape and open the last valve") + count;
                if (lastIsGauge)
                    return L10n.T("潮位站的阀门缺手轮：去挖沙船船舱的储物柜拿，跳板涨潮会淹",
                        "The tide gauge valve has no handwheel. It's in the dredger's cabin locker; the gangway floods at high tide") + count;
                return L10n.T("拧开三处阀门提起大闸", "Open the three valves to lift the tide gate") + count
                    + L10n.T("：还剩 ", " · still to open: ") + devices.RemainingValves();
            }
            if (tide != null && tide.SluiceOpen)
            {
                if (tide.LagoonLevel > VaultDryLevel && tide.LagoonLevel > tide.Level)
                    return L10n.T("大闸已提起，泻湖正在放水：退潮后进储蓄所金库", "The tide gate is up and the lagoon is draining. Enter the bank vault at low tide");
                return tide.LagoonLevel <= VaultDryLevel
                    ? L10n.T("储蓄所金库露出水面了：所长的保险柜在最里面", "The bank vault is above water. The manager's safe is at the very back")
                    : L10n.T("潮水又淹了金库：等下一次退潮", "The tide has flooded the vault again. Wait for the next low tide");
            }
            return L10n.T("搜刮后从开着的出口撤离", "Loot, then leave through an open exit");
        }

        /// <summary>搜刮点第一次被翻开：固定箱柜里的闸工日志记进剧情存档（官方笔记图鉴同步点亮），字幕报一下题目。</summary>
        private void OnLootOpened(string pointId)
        {
            int page = TidegateLoot.PageIndexOf(pointId);
            if (page < 0 || !TidegateStory.RecordPage(page)) return;
            Status(L10n.T("找到了", "Found ") + TidegateText.PageTitle(page) + L10n.T("（笔记图鉴里能翻）", " (in your notes)"), false);
        }

        /// <summary>
        /// HUD 小标行：潮相与潮位（带着怀表时加换相倒计时）· 负重与能不能游（游泳时敌人更难发现）· 开着的出口。
        /// 每秒都在变的读数放这一行，不触发目标行的「目标更新」（CR-2026-11-05-018）。
        /// </summary>
        private string FieldStatus()
        {
            var parts = new List<string>(3);
            if (tide != null) parts.Add(tide.Chip(carryingWatch));
            if (water != null && player != null && player.CharacterItem != null)
                parts.Add(L10n.T("负重 ", "Load ") + player.CharacterItem.TotalWeight.ToString("F0") + "/" + water.WeightLimit.ToString("F0")
                    + (water.Stealthy ? L10n.T(" 游泳中，敌人难发现", " · swimming, harder to spot")
                        : water.CanSwimNow ? L10n.T(" 能游", " · can swim") : L10n.T(" 下水会沉", " · will sink")));
            if (extraction != null) parts.Add(extraction.Describe());
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>打开官方地图。潮闸镇的地图数据（`MiniMapSettings`）随场景包提供，玩家直接按地图键即可。</summary>
        internal void OpenMap()
        {
            if (closed || !ready) return;
            try { MiniMapView.Show(); }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " open map failed: " + e); }
        }

        private void Rescue()
        {
            player.SetPosition(safePosition);
            Status(L10n.T("已回到最近的安全落脚点", "Returned to the nearest safe footing"), false);
            Debug.Log(TidegateRaidScene.LogTag + " FALL_RESCUE position=" + safePosition);
        }

        private void OnStartedLoading(SceneLoadingContext context)
        {
            if (context.sceneName == TidegateRaidScene.SceneName && !moved) return;
            if (!moved && !entryScene.IsValid()) return;
            returning = true;
            ready = false;
            CancelPendingInitialization();
            StopAllCoroutines();
            Safe("extraction_hide", delegate { if (extraction != null) extraction.HideCountdown(); });
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (TidegateRaidScene.IsScene(scene) || string.Equals(scene.path, TidegateRaidScene.ScenePath, StringComparison.OrdinalIgnoreCase))
                Cleanup("raid_unloaded");
        }

        private void OnPlayerDied(DamageInfo damage)
        {
            // 官方 CharacterDieTask 保存墓碑、处理损失与返回基地；不移动尸体或抢先卸载场景。
            deathPending = true; ready = false;
            Safe("extraction_hide", delegate { if (extraction != null) extraction.HideCountdown(); });
        }

        internal void Close(bool restorePlayer, string reason)
        {
            if (closed) return;
            RememberReturnReason(reason, null);
            if (restorePlayer && loadStarted && !deathPending)
            {
                returnRequested = returning = true;
                if (extraction != null) extraction.HideCountdown();
                Status(L10n.T("正在返回基地…", "Returning to base…"), false);
                BlockInputForReturn();
                DispatchReturnIfReady();
                return;
            }
            if (deathPending && entryScene.IsValid() && entryScene.isLoaded) return;
            Cleanup(reason);
        }

        /// <summary>
        /// 照官方 SceneLoaderProxy.LoadScene：返航一旦派发就封锁输入。封锁源必须是本图场景内的临时对象，随场景卸载自动失效；
        /// 挂在 DontDestroyOnLoad 的 Mod 宿主上会让回基地后输入永久锁死。
        /// </summary>
        private void BlockInputForReturn()
        {
            if (returnInputBlock != null || root == null || !entryScene.IsValid() || !entryScene.isLoaded) return;
            Safe("input_block", delegate
            {
                GameObject block = new GameObject("TidegateReturnInputBlock");
                block.transform.SetParent(root.transform, false);
                InputManager.DisableInput(block);
                returnInputBlock = block;
            });
        }

        private void DispatchReturnIfReady()
        {
            if (lease == null || !lease.LoadFinished || SceneLoader.IsSceneLoading || lease.IsReturning) return;
            lease.ReturnToBase(moved);
            // 入图场景从未出现时也不会收到它的 sceneUnloaded；返航已派发就交回租约收尾，避免不断重载基地。
            if (!entryScene.IsValid() && lease.IsReturning) Cleanup("entry_cancelled");
        }

        private void CancelPendingInitialization()
        {
            navigationReady = true;
            if (lease != null) lease.Abort("潮闸镇会话已取消初始化");
            LevelManager.UnregisterWaitForInitialization(this);
            Safe("cancel_path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("cancel_scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
        }

        private void RememberReturnReason(string reason, string notice)
        {
            if (firstReturnReason != null) return;
            firstReturnReason = reason;
            Debug.LogWarning(TidegateRaidScene.LogTag + " RETURN_REASON " + reason);
            if (lease != null && notice != null) lease.SetFailureNotice(notice);
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
            Safe("devices", delegate { if (devices != null) devices.Dispose(); });
            devices = null;
            // 循环海浪不随发声体销毁而停：先 StopAll 再销毁（TidegateSounds.Dispose）。
            Safe("sounds", delegate { if (sounds != null) sounds.Dispose(); });
            sounds = null;
            Safe("enemies", delegate { if (enemies != null) enemies.Dispose(); });
            enemies = null;
            Safe("loot", delegate { if (loot != null) loot.Dispose(); });
            loot = null;
            Safe("extraction", delegate { if (extraction != null) extraction.Dispose(); });
            extraction = null;
            Safe("map", delegate { if (map != null) map.Dispose(); });
            map = null;
            Safe("water", delegate { if (water != null) water.Dispose(); });
            water = null;
            Safe("tide", delegate
            {
                if (tide == null) return;
                tide.PhaseChanged -= OnTidePhaseChanged;
                tide.Dispose();
            });
            tide = null;
            Safe("path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
            Safe("graph", delegate { if (navigation != null) navigation.Dispose(); });
            navigation = null;
            Safe("lighting", delegate { if (lighting != null) lighting.Dispose(); });
            lighting = null;
            Safe("world", delegate { if (world != null) world.Dispose(); });
            world = null;
            Safe("hud", delegate { if (hud != null) hud.Dispose(); });
            hud = null;
            if (returnInputBlock != null) Destroy(returnInputBlock);
            Debug.Log(TidegateRaidScene.LogTag + " CLEANUP reason=" + reason + " firstReason=" + firstReturnReason);
            if (lease != null) lease.Release(delegate { if (this != null) Destroy(this); });
            else Destroy(this);
        }

        private void OnDestroy() { Cleanup("component_destroyed"); }

        internal void Announce(string message, bool error) { Status(message, error); }

        private void Status(string message, bool error)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (hud != null && ready) { Safe("caption", delegate { hud.Caption(message, error); }); return; }
            if (report != null) report(message, error);
        }

        private readonly HashSet<string> reportedFaults = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>每帧推进里的异常：每个子系统只记第一次完整堆栈，之后不再刷日志（每帧热路径不加日志）。</summary>
        private void TickFault(string step, Exception e)
        {
            if (reportedFaults.Add(step)) Debug.LogWarning(TidegateRaidScene.LogTag + " tick " + step + " failed: " + e);
        }

        private static void Safe(string step, Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " " + step + " failed: " + e.Message); }
        }
    }
}
