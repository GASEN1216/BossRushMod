using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：末班编组站独立出击的 owner。生命周期照潮闸镇会话（`TidegateSession`），差别只在出发地：
    /// 入口在官方仓库区（一趟正在进行的出击）里，不在基地。租约照常以「不通知撤离、存档」请求官方切图，官方
    /// `LevelManager.HandleRaidInitialization` 见上一趟没结束就继续同一趟（与仓库区 → 农场的官方转场同一路径）：背包随身、
    /// 中途不结算，只在编组站撤离（或阵亡）时结束这一趟。天气副本从仓库区的户外配置克隆。
    /// 第一期（2026-11-11）：装配、光照、导航、室内屋顶、HUD、北公路撤离。第二期：列车（<see cref="LastShiftTrains"/>）、
    /// 搜刮（<see cref="LastShiftLoot"/>）、押运队（<see cref="LastShiftEnemies"/>）。第三期：一趟里的剧情进度（<see cref="LastShiftRun"/>），撤离时写进本槽剧情存档。
    /// </summary>
    internal sealed partial class LastShiftSession : MonoBehaviour, IInitializedQueryHandler
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
        private LastShiftWorld world;
        private LastShiftLighting lights;
        private LastShiftExtraction extraction;
        private LastShiftTrains trains;
        private LastShiftLoot loot;
        private LastShiftEnemies enemies;
        private LastShiftRun run;
        private LastShiftPhase lastPhase = LastShiftPhase.Clear;
        private string lockedExitShown;
        private GameObject returnInputBlock;
        private const float RegionRadius = 24f, RegionSwitchMargin = 4f;
        private Transform regionPoi;
        private string assemblyError, firstReturnReason;
        private bool subscribed, closed, ready, moved, navigationReady, returning, returnRequested, deathPending, loadStarted;
        private bool pathCompleted, pathValid;
        internal bool IsClosed { get { return closed; } }
        private int groundMask;
        private float enteredAt, nextHud, nextGroundCheck;
        private Vector3 safePosition;
        private readonly HashSet<Transform> unreachable = new HashSet<Transform>();

        internal bool IsReady { get { return IsSessionValid(); } }

        /// <summary>只在一趟正在进行的官方出击里能上车（入口挂在仓库区）；不能从基地、也不能叠在别的 Mod 地图上。</summary>
        internal static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = null;
            if (owner == null) { reason = L10n.T("Mod 还没准备好", "The mod is not ready yet"); return false; }
            if (!LastShiftRaidScene.IsBundleDeployed())
            {
                reason = L10n.T("少了末班编组站的出击场景包，先更新 Mod 资源", "The Last Shift Yard raid scene bundle is missing. Update the mod's assets");
                return false;
            }
            if (owner.GetComponent<LastShiftSession>() != null || owner.GetComponent<TidegateSession>() != null
                || owner.GetComponent<SkyIslandSession>() != null || owner.GetComponent<ArenaPrototypeSession>() != null)
            {
                reason = L10n.T("先结束当前的场景旅程，等它收拾完", "End the current scene journey and wait for it to clean up");
                return false;
            }
            if (SkyIslandStorySaveRecovery.IsPending())
            { reason = L10n.T("上一段出击记录还在保存，稍后再试", "The previous raid record is still saving. Try again shortly"); return false; }
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
            if (LevelManager.Instance == null || !LevelManager.Instance.IsRaidMap || RaidUtilities.CurrentRaid.ended)
            { reason = L10n.T("末班车只在出击途中发车", "The last train only leaves during a raid"); return false; }
            if (LevelConfig.Instance == null || LevelConfig.Instance.timeOfDayConfig == null)
            { reason = L10n.T("这张图的天气配置读不到，没法转车", "This map's weather settings are unavailable, so the transfer can't start"); return false; }
            if (GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
            { reason = L10n.T("游戏相机还没准备好", "The game camera is not ready yet"); return false; }
            return true;
        }

        internal static void Enter(ModBehaviour owner, Action<string, bool> status)
        {
            string reason;
            if (!CanEnter(owner, out reason)) { status(reason, true); return; }
            LastShiftSession session = owner.gameObject.AddComponent<LastShiftSession>();
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
                    string notice = SkyIslandStoryRules.WithDetail(L10n.T("末班编组站没能建起来：", "Last Shift Yard setup failed"), e.Message);
                    RememberReturnReason("build_failed: " + e.Message, notice);
                    CancelPendingInitialization();
                    Debug.LogWarning(LastShiftRaidScene.LogTag + " setup failed: " + e);
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
                hud = new SkyIslandHud(host.transform, delegate { return L10n.T("末班编组站", "LAST SHIFT YARD"); });
                hud.SetLandingHint(L10n.T("地图键看全站 · 站进撤离环读完条返航", "Map key: see the yard · stand in an extraction ring until the bar fills"));
            });
            Status(L10n.T("剪过票了，正在转车…", "Ticket punched. Transferring…"), false);
            // 仓库区场景的组件不留成字段：出图即成已销毁引用。取到就交给租约克隆（同潮闸镇 CR-2026-09-10-003）。
            TimeOfDayConfig timeOfDayTemplate = LevelConfig.Instance.timeOfDayConfig;
            if (timeOfDayTemplate == null) throw new InvalidOperationException("官方天气配置缺失，无法创建完整关卡");
            profile = LastShiftRaidScene.CreateProfile();
            lease = new ModRaidLease(profile);
            lease.BeginPrepare(ModBehaviour.GetModPath(), timeOfDayTemplate);
            while (lease.StepPreparation()) yield return null;
            loadStarted = true;
            lease.BeginLoad();
            float deadline = Time.realtimeSinceStartup + 120;
            while (root == null && lease.Error == null && assemblyError == null && lease.HasSceneLoadTimeRemaining(ref deadline))
            {
                if (lease.LoadFinished) { loadStarted = false; throw new InvalidOperationException("官方场景加载器拒绝了本次转车，请稍后再试"); }
                yield return null;
            }
            if (assemblyError != null) throw new InvalidOperationException(assemblyError);
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (root == null) throw new TimeoutException("末班编组站场景未能就绪");
            deadline = Time.realtimeSinceStartup + 120f;
            while ((SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null ||
                GameCamera.Instance.renderCamera == null) && lease.Error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (lease.Error != null) throw new InvalidOperationException(lease.Error);
            if (SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null || GameCamera.Instance.renderCamera == null)
                throw new TimeoutException("末班编组站活动场景或官方相机未能就绪");
            if (root.transform.position != Vector3.zero || root.transform.localScale != Vector3.one || root.transform.rotation != Quaternion.identity)
                throw new InvalidOperationException("末班编组站场景根节点变换未归一化");
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            world = new LastShiftWorld(root);
            world.ApplyLayers();
            lighting = new SkyIslandLighting();
            lighting.Apply(root);
            lights = new LastShiftLighting(world);
            lights.Tick();
            Physics.SyncTransforms();
            world.VerifyGround(groundMask);
            Transform nav = root.transform.Find("Navigation");
            if (nav == null || nav.localPosition != Vector3.zero || nav.localScale != Vector3.one || nav.localRotation != Quaternion.identity)
                throw new InvalidOperationException("Navigation 必须位于世界根下且为单位变换");
            MeshFilter filter = nav.GetComponent<MeshFilter>();
            if (filter == null) throw new InvalidOperationException("末班编组站导航网格缺失");
            navigation = new ArenaPrototypeNavigation();
            deadline = Time.realtimeSinceStartup + 30f;
            while (global::AstarPath.active == null || global::AstarPath.active.data == null || global::AstarPath.active.isScanning)
            {
                if (lease.Error != null) throw new InvalidOperationException(lease.Error);
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("末班编组站导航服务未能就绪");
                yield return null;
            }
            // 四个子网格是列车占线掩码（0 不受影响、1 西线、2 东线、3 两线），A* 扫描时拼在一起；列车 owner 第二期按掩码改可走性。
            scan = navigation.BeginScan(filter.sharedMesh, Vector3.zero);
            deadline = Time.realtimeSinceStartup + 30;
            while (scan.MoveNext())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("末班编组站导航扫描超时");
                yield return null;
            }
            scan.Dispose();
            scan = null;
            if (navigation.CountWalkableNodes() == 0) throw new InvalidOperationException("末班编组站导航没有可走节点");
            // 从出生点到每个刷怪点探一次路：软失败，够不着的点本趟不刷，记一行日志给场景包那一侧修（同潮闸镇）。
            probe = root.AddComponent<Seeker>();
            foreach (Transform marker in world.SpawnMarkers)
            {
                pathCompleted = pathValid = false;
                probe.StartPath(world.PlayerSpawn.position, marker.position, OnProbePath, navigation.Mask);
                deadline = Time.realtimeSinceStartup + 10;
                while (!pathCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (pathCompleted && pathValid) continue;
                unreachable.Add(marker);
                Debug.LogWarning(LastShiftRaidScene.LogTag + " NAV_UNREACHABLE " + marker.name + " at=" + marker.position);
            }
            if (unreachable.Count == world.SpawnMarkers.Count) throw new InvalidOperationException("导航从出生点一个刷怪点都到不了");
            // 官方物资池趁读条预热，每个池子占一帧（口径同潮闸镇 CR-2026-11-05-028）；预热失败只记一次，箱子照常懒建。
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
            warm = LastShiftLoot.PrewarmThemes();
            while (warm.MoveNext()) yield return warm.Current;
            warm = new TidegateLoot.PrizeCatalog().Prewarm();
            while (warm.MoveNext()) yield return warm.Current;
            navigationReady = true;
            deadline = Time.realtimeSinceStartup + 120;
            while ((!lease.LoadFinished || !LevelManager.AfterInit) && lease.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (lease.Error == null && (!lease.LoadFinished || !LevelManager.AfterInit))
                lease.Abort("官方关卡初始化超时，末班编组站已请求取消");
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
            // 首班教学只在第一趟有；回访没有南护栏等车这回事，不写这句。
            if (hud != null && !ReturnVisit())
                Safe("landing_hint", delegate
                {
                    hud.SetLandingHint(L10n.T("首班车要等你站到南护栏后面才进站。看它过了岔，再上悬车楼司机室扳道岔。",
                        "The first train waits until you stand behind the south rail. Watch it, then climb to the suspended cab to throw the switch."));
                });
            safePosition = world.PlayerSpawn.position + Vector3.up * 0.15f;
            moved = ready = true;
            enteredAt = Time.unscaledTime;
            Safe("extraction", delegate { extraction = new LastShiftExtraction(world); });
            // 一趟一个种子：班次事件、加价台、班单、搜刮都从它派生的独立流里抽（顺序无关）。
            int seed = Guid.NewGuid().GetHashCode();
            Safe("trains", delegate
            {
                var timetable = new LastShiftTimetable(seed, ReturnVisit());
                string manifest = ManifestFor(timetable, seed);
                trains = new LastShiftTrains(world, timetable, navigation, extraction, manifest, Status, IsSessionValid);
                trains.ManifestExtra = delegate { return LastShiftStory.RecordsLine(run == null ? 0 : run.Records); };
            });
            Safe("loot", delegate
            {
                if (trains == null) return;
                loot = new LastShiftLoot(world, trains.Timetable, trains.Manifest, seed, IsSessionValid, Status);
                loot.Opened += OnLootOpened;
            });
            Safe("enemies", delegate
            {
                if (trains == null) return;
                enemies = new LastShiftEnemies(world, trains.Timetable, navigation, player, seed, ReturnVisit(), IsSessionValid, unreachable, Status);
            });
            Safe("story", delegate { run = new LastShiftRun(world, Status, IsSessionValid); });
            Safe("boarding_tip", ArrivalTip);
            Debug.Log(LastShiftRaidScene.LogTag + " ENTER_PASS scene=" + entryScene.path + " nodes=" + navigation.CountWalkableNodes() +
                " loot=" + world.LootMarkers.Count + " spawns=" + world.SpawnMarkers.Count + " unreachable=" + unreachable.Count +
                " exits=" + world.Exits.Count + " lamps=" + world.NightLamps.Count + " raidContinued=" + !RaidUtilities.CurrentRaid.ended);
        }

        public bool HasInitialized() { return navigationReady || closed; }

        /// <summary>
        /// 回访开局的一句（第七轮）：故事收尾后余响直接说出下一班的事件和加价台；在那之前只说班单，加价台写在运单牌上。
        /// 第一趟有首班教学的落地提示，这里不说。
        /// </summary>
        private void ArrivalTip()
        {
            if (trains == null || trains.Timetable.Training) return;
            LastShiftTimetable t = trains.Timetable;
            // 故事收尾后余响替你跟调度打过招呼：开局道岔已经放到加价台（owner 2026-11-11），想换边仍可自己扳。
            if (LastShiftStory.Journal) trains.PresetLine(t.Premium);
            string head = L10n.T("今晚的班单：" + LastShiftText.ManifestName(trains.Manifest) + "。",
                "Tonight's manifest: " + LastShiftText.ManifestName(trains.Manifest).ToLowerInvariant() + ".");
            if (LastShiftStory.Journal)
                Status(head + L10n.T("剪票时余响交代过：下一班是" + LastShiftText.EventLabel(t.Event) + "，调度会把它放到" + LastShiftText.PlatformName(t.Line) + "台。",
                    " Yu Xiang told you at the desk: the next one's a " + LastShiftText.EventLabel(t.Event).ToLowerInvariant()
                    + ", and dispatch is sending it to the " + LastShiftText.LineName(t.Line) + " platform."), false);
            else Status(head + L10n.T("加价台写在南班房边的运单牌上。", " The premium platform is posted on the manifest board by the south crew room."), false);
        }

        /// <summary>安全撤离：补齐本趟记录（签收、带红、警铃），整趟进度写进本槽剧情存档。阵亡与装配失败不走这里。</summary>
        private void CommitStory()
        {
            if (run == null) return;
            if (loot != null && loot.CargoClaimed && trains != null) run.Record("sign:" + trains.Manifest);
            if (CarriesRed(player)) run.Record("red");
            if (alarmRaised) run.Record("alarm");
            if (enemies != null && enemies.BossDefeated && enemies.BossRodeTrain) run.Record("bossTrain");
            LastShiftStory.CommitRun(run.Notes, run.Records);
        }

        private bool alarmRaised;

        /// <summary>风险柜第一次打开：库房警铃响，最近的押运员往回赶（原型 alarm）。</summary>
        private void OnLootOpened(string id)
        {
            if (loot == null || world == null) return;
            bool risk = false;
            foreach (string candidate in loot.RiskContainerIds)
                if (string.Equals(candidate, id, StringComparison.Ordinal)) { risk = true; break; }
            if (!risk) return;
            alarmRaised = true;
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            foreach (Transform marker in world.LootMarkers)
                if (marker != null && string.Equals(marker.name, id, StringComparison.Ordinal)) { at = marker.position; break; }
            if (enemies != null) enemies.Alarm(at);
        }

        /// <summary>身上（整棵物品树）有没有一件 Q5 以上（原型「带出一件红」）。只在撤离那一刻走一次。</summary>
        private static bool CarriesRed(CharacterMainControl who)
        {
            if (who == null || who.CharacterItem == null) return false;
            List<Item> all = who.CharacterItem.GetAllChildren(true, true);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Quality >= 5 && all[i].Quality <= 7) return true;
            return false;
        }

        /// <summary>回访跳过首班教学、从班次表随机时刻开局：按本槽剧情存档里安全撤离过的次数判。</summary>
        private static bool ReturnVisit() { return LastShiftStory.Runs > 0; }

        /// <summary>班单：第一趟按种子轮换，回访从独立流里抽（原型 layout.goods.manifests）。</summary>
        private static string ManifestFor(LastShiftTimetable timetable, int seed)
        {
            string[] all = { "medical", "repair", "precision" };
            return timetable.Training ? all[(seed & 0x7fffffff) % 3] : all[timetable.Stream("raid#manifest").Next(3)];
        }

        private bool IsSessionValid()
        {
            return this != null && !closed && ready && !returning && !deathPending && root != null && player != null
                && player == CharacterMainControl.Main && !SceneLoader.IsSceneLoading;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (closed || !LastShiftRaidScene.IsScene(scene)) return;
            try
            {
                entryScene = scene;
                foreach (GameObject candidate in scene.GetRootGameObjects())
                    if (candidate.name == LastShiftRaidScene.WorldRootName) root = candidate;
                if (root == null) throw new InvalidOperationException("独立场景缺少地形根节点");
                LevelManager.RegisterWaitForInitialization(this);
            }
            catch (Exception e)
            {
                navigationReady = true;
                assemblyError = e.Message;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " standalone level assembly failed: " + e);
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
            try { if (lighting != null) lighting.Tick(); } catch (Exception e) { TickFault("lighting", e); }
            try { if (lights != null) lights.Tick(); } catch (Exception e) { TickFault("lights", e); }
            Vector3 position = player.transform.position;
            try { if (world != null) world.IndoorRoofs.UpdateFeet(position); } catch (Exception e) { TickFault("indoor", e); }
            try { if (trains != null && trains.Tick(player, View.ActiveView == null ? Time.deltaTime : 0f)) return; }
            catch (Exception e) { TickFault("trains", e); }
            try { if (loot != null) loot.Tick(player); } catch (Exception e) { TickFault("loot", e); }
            try
            {
                if (enemies != null)
                {
                    // 拉过轨道车铃：把铃附近的巡逻引过去，读完清零。
                    Transform bell;
                    if (trains != null && trains.Ringing && world.Interacts.TryGetValue("bell", out bell))
                    {
                        trains.Ringing = false;
                        enemies.BellRung(bell.position);
                    }
                    enemies.Tick();
                }
            }
            catch (Exception e) { TickFault("enemies", e); }
            try
            {
                if (run != null && trains != null)
                {
                    LastShiftPhase phase = trains.Timetable.Phase;
                    run.Tick(position, phase);
                    // 直通快车开过去、人还活着：躲过直通快车。
                    if (phase == LastShiftPhase.Clear && lastPhase != LastShiftPhase.Clear && trains.Timetable.IsExpress) run.Record("express");
                    lastPhase = phase;
                }
            }
            catch (Exception e) { TickFault("story", e); }
            // 站界有隐形墙（场景包 `Collision/COL_Boundary`），这里只兜物理穿模或掉出世界。
            if (position.y < -14f || LastShiftWorld.OutsidePlayArea(position)) { Rescue(); return; }
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
                if (extraction.Tick(position, View.ActiveView == null ? Time.deltaTime : 0f, out exitId))
                {
                    Safe("story_commit", CommitStory);
                    Close(true, "extract_" + exitId);
                    return;
                }
                if (hud != null) hud.SetExtraction(extraction.HudFallback);
                string locked = extraction.LockedNear(position);
                if (locked != null && locked != lockedExitShown)
                    Status(LastShiftText.ExitLockedLine(locked), false);
                lockedExitShown = locked;
            }
            if (Time.unscaledTime >= nextHud)
            {
                nextHud = Time.unscaledTime + 0.5f;
                if (hud != null)
                {
                    UpdateRegion(position);
                    hud.SetObjective(trains != null ? trains.Objective(loot != null && loot.CargoClaimed)
                        : L10n.T("搜刮后从开着的出口撤离：北公路常开", "Loot, then leave through an open exit. The north road is always open"));
                    string counter = run == null ? null : run.Counter();
                    if (extraction != null) hud.SetChips((trains != null ? trains.Chip() + " · " : "") + (counter == null ? "" : counter + " · ") + extraction.Describe());
                }
            }
        }

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
            hud.SetRegion(regionPoi == null ? LastShiftRaidScene.DisplayName : LastShiftText.PlaceName(regionPoi.name));
        }

        private void Rescue()
        {
            player.SetPosition(safePosition);
            Status(L10n.T("已回到最近的安全落脚点", "Returned to the nearest safe footing"), false);
            Debug.Log(LastShiftRaidScene.LogTag + " FALL_RESCUE position=" + safePosition);
        }

        private void OnStartedLoading(SceneLoadingContext context)
        {
            if (context.sceneName == LastShiftRaidScene.SceneName && !moved) return;
            if (!moved && !entryScene.IsValid()) return;
            returning = true;
            ready = false;
            CancelPendingInitialization();
            StopAllCoroutines();
            Safe("extraction_hide", delegate { if (extraction != null) extraction.HideCountdown(); });
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (LastShiftRaidScene.IsScene(scene) || string.Equals(scene.path, LastShiftRaidScene.ScenePath, StringComparison.OrdinalIgnoreCase))
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

        /// <summary>照官方 SceneLoaderProxy.LoadScene：返航一旦派发就封锁输入；封锁源是本图场景内的临时对象，随场景卸载失效。</summary>
        private void BlockInputForReturn()
        {
            if (returnInputBlock != null || root == null || !entryScene.IsValid() || !entryScene.isLoaded) return;
            Safe("input_block", delegate
            {
                GameObject block = new GameObject("LastShiftReturnInputBlock");
                block.transform.SetParent(root.transform, false);
                InputManager.DisableInput(block);
                returnInputBlock = block;
            });
        }

        private void DispatchReturnIfReady()
        {
            if (lease == null || !lease.LoadFinished || SceneLoader.IsSceneLoading || lease.IsReturning) return;
            // moved：进了编组站才算这一趟走完，按撤离结算；没进成（装配失败）也回基地，但不发撤离通知。
            lease.ReturnToBase(moved);
            if (!entryScene.IsValid() && lease.IsReturning) Cleanup("entry_cancelled");
        }

        private void CancelPendingInitialization()
        {
            navigationReady = true;
            if (lease != null) lease.Abort("末班编组站会话已取消初始化");
            LevelManager.UnregisterWaitForInitialization(this);
            Safe("cancel_path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("cancel_scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
        }

        private void RememberReturnReason(string reason, string notice)
        {
            if (firstReturnReason != null) return;
            firstReturnReason = reason;
            Debug.LogWarning(LastShiftRaidScene.LogTag + " RETURN_REASON " + reason);
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
            Safe("story", delegate { if (run != null) run.Dispose(); });
            run = null;
            Safe("enemies", delegate { if (enemies != null) enemies.Dispose(); });
            enemies = null;
            Safe("loot", delegate { if (loot != null) { loot.Opened -= OnLootOpened; loot.Dispose(); } });
            loot = null;
            Safe("trains", delegate { if (trains != null) trains.Dispose(); });
            trains = null;
            Safe("extraction", delegate { if (extraction != null) extraction.Dispose(); });
            extraction = null;
            Safe("path", delegate { if (probe != null) probe.CancelCurrentPathRequest(); });
            Safe("scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
            Safe("graph", delegate { if (navigation != null) navigation.Dispose(); });
            navigation = null;
            Safe("lights", delegate { if (lights != null) lights.Dispose(); });
            lights = null;
            Safe("lighting", delegate { if (lighting != null) lighting.Dispose(); });
            lighting = null;
            Safe("world", delegate { if (world != null) world.Dispose(); });
            world = null;
            Safe("hud", delegate { if (hud != null) hud.Dispose(); });
            hud = null;
            if (returnInputBlock != null) Destroy(returnInputBlock);
            Debug.Log(LastShiftRaidScene.LogTag + " CLEANUP reason=" + reason + " firstReason=" + firstReturnReason);
            if (lease != null) lease.Release(delegate { if (this != null) Destroy(this); });
            else Destroy(this);
        }

        private void OnDestroy() { Cleanup("component_destroyed"); }

        private void Status(string message, bool error)
        {
            if (string.IsNullOrEmpty(message)) return;
#if BOSSRUSH_DEV
            // Dev 演练按字幕核对提示有没有出、出了几次（LastShiftDrillHooks.cs）。
            DevNoteStatus(message, error);
#endif
            if (hud != null && ready) { Safe("caption", delegate { hud.Caption(message, error); }); return; }
            if (report != null) report(message, error);
        }

        private readonly HashSet<string> reportedFaults = new HashSet<string>(StringComparer.Ordinal);

        private void TickFault(string step, Exception e)
        {
            if (reportedFaults.Add(step)) Debug.LogWarning(LastShiftRaidScene.LogTag + " tick " + step + " failed: " + e);
        }

        private static void Safe(string step, Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " " + step + " failed: " + e.Message); }
        }
    }
}
