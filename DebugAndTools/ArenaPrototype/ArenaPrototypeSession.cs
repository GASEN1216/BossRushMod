using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Duckov.UI;
using Duckov.Utilities;
using Pathfinding;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：手动开发实验，支持原型平台与单独的前哨 Scene 资源。
    /// 复用关卡服务，所有资源随会话回收；不注册正式地图、不写存档、不修改官方 preset。
    /// </summary>
    internal sealed class ArenaPrototypeSession : MonoBehaviour
    {
        internal const string BundleRelativePath = "Assets/arenas/prototype_arena";
        private const float PlatformHeight = 80f;
        private const float ExtractionHold = 3f;
        private bool outpost;
        private StoneOutpostSceneLease sceneLease;
        private ArenaPrototypeLighting lighting;
        private StoneOutpostMap map;
        private int searchedPoints;
        // 前哨撤离圈里已停留的游戏时间秒数，-1 表示不在圈里。
        private float extractionHeld = -1;
        private readonly List<CharacterMainControl> enemies = new List<CharacterMainControl>();
        private readonly List<CharacterRandomPreset> enemyPresets = new List<CharacterRandomPreset>();
        private readonly List<Transform> enemyMarkers = new List<Transform>();
        private ModBehaviour host;
        private CharacterMainControl player;
        private Health playerHealth;
        private Scene entryScene;
        private Vector3 returnPosition;
        private Vector3 origin;
        private GameObject arena;
        private GameObject hudRoot;
        private TextMeshProUGUI hud;
        private AssetBundle bundle;
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private ArenaPrototypeNavigation navigation;
        private IEnumerator<Progress> scan;
        private Seeker probeSeeker;
        private Transform playerSpawn;
        private Transform enemySpawn;
        private Transform exitMarker;
        private CharacterMainControl enemy;
        private Action<string, bool> report;
        private bool subscribed;
        private bool closed;
        private bool ready;
        private bool playerMoved;
        private bool pathCompleted;
        private bool pathValid;
        private bool spawning;
        private bool enemyDeathReported;
        private float enteredAt;
        private int wallMask;

        internal static bool CanEnter(ModBehaviour owner, out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled || owner == null) { reason = "仅开发版可使用场景实验"; return false; }
            if (owner.GetComponent<ArenaPrototypeSession>() != null) { reason = "试验场已存在或正在回收"; return false; }
            if (owner.GetComponent<SkyIslandSession>() != null) { reason = "请先退出天空岛并等待回收"; return false; }
            if (F3GameplayValidationRunner.IsRunning) { reason = "请等待完整玩法验收结束"; return false; }
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited)
            { reason = "请等待场景和玩家初始化完成"; return false; }
            string mode;
            if (owner.ValidationHasActiveMode(out mode)) { reason = "请先结束当前模式：" + mode; return false; }
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.Health == null || main.Health.IsDead) { reason = "玩家未就绪"; return false; }
            if (AstarPath.active == null) { reason = "当前关卡没有可用的 A* 系统"; return false; }
            return true;
        }

        internal static void Enter(ModBehaviour owner, Action<string, bool> report, bool outpost = false)
        {
            string reason;
            if (!CanEnter(owner, out reason)) { report(reason, true); return; }
            if (outpost && !SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name))
            { report("请先回基地再出发前往石堡前哨", true); return; }
            ArenaPrototypeSession session = owner.gameObject.AddComponent<ArenaPrototypeSession>();
            session.outpost = outpost;
            session.host = owner;
            session.report = report;
            session.player = CharacterMainControl.Main;
            session.playerHealth = session.player.Health;
            session.entryScene = SceneManager.GetActiveScene();
            session.returnPosition = session.player.transform.position;
            session.origin = outpost ? new Vector3(2048, 0, 2048) : session.returnPosition + Vector3.up * PlatformHeight;
            session.Subscribe();
            session.StartCoroutine(session.BuildGuarded());
        }

        private void Subscribe()
        {
            if (subscribed) return;
            SceneLoader.onStartedLoadingScene += OnStartedLoading;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            playerHealth.OnDeadEvent.AddListener(OnPlayerDied);
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
                    Status("场地创建失败：" + e.Message, true);
                    Close(true, "build_failed");
                    yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }

        private IEnumerator Build()
        {
            hud = ArenaPrototypeControls.CreateHud(out hudRoot);
            Status(outpost ? "正在加载石堡前哨…" : "正在加载石砌遗迹…", false);
            string modDir = System.IO.Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
            GameObject prefab = null;
            if (outpost)
            {
                sceneLease = new StoneOutpostSceneLease();
                yield return sceneLease.BeginLoad(modDir);
                // isDone/协程恢复不保证 completed 里的所有权绑定已完成；必须等待租约自己就绪。
                float resolveDeadline = Time.realtimeSinceStartup + 10;
                while (!sceneLease.LoadResolved && Time.realtimeSinceStartup < resolveDeadline) yield return null;
                if (!sceneLease.LoadResolved) throw new TimeoutException("前哨场景加载回调超时");
                if (sceneLease.LoadError != null) throw new InvalidOperationException(sceneLease.LoadError);
                if (!sceneLease.Loaded || sceneLease.Root == null) throw new InvalidOperationException("前哨场景根节点缺失");
                arena = sceneLease.Root;
                arena.transform.position = origin;
            }
            else
            {
                string path = System.IO.Path.Combine(modDir, BundleRelativePath);
                if (!File.Exists(path)) throw new FileNotFoundException("缺少试验场资源包", path);
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidOperationException("试验场资源包加载失败");
                prefab = bundle.LoadAsset<GameObject>("ArenaPrototype");
                if (prefab == null) throw new InvalidOperationException("资源包内缺少 ArenaPrototype");
            }
            var layers = GameplayDataSettings.Layers;
            // 官方 Projectile 的命中层包含 wall；blockBulletLayers 是另一路 BulletBlocker 特殊层。
            wallMask = layers.wallLayerMask.value;
            int groundLayer = FirstLayer(layers.groundLayerMask.value);
            int wallLayer = FirstLayer(wallMask);
            if (groundLayer < 0 || wallLayer < 0) throw new InvalidOperationException("游戏地面/子弹碰撞层未就绪");
            if (!outpost && Physics.CheckBox(origin + Vector3.up * 2, new Vector3(15, 4, 15), Quaternion.identity,
                layers.groundLayerMask.value | layers.wallLayerMask.value, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("上方场地区域已被占用，请换一处空旷位置");
            if (!outpost)
            {
                arena = Instantiate(prefab, origin, Quaternion.identity);
                arena.name = "BossRush_ArenaPrototype";
                SceneManager.MoveGameObjectToScene(arena, entryScene);
            }
            foreach (BoxCollider collider in arena.GetComponentsInChildren<BoxCollider>(true))
                collider.gameObject.layer = collider.name == "COL_Ground" ? groundLayer : wallLayer;
            Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
            if (gameShader == null) throw new InvalidOperationException("官方模型着色器未就绪");
            var convertedMaterials = new Dictionary<Material, Material>();
            // FBX 可见网格与碰撞体分离；可见部分也要进入官方环境渲染层。
            foreach (MeshRenderer renderer in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.gameObject.layer = groundLayer;
                // 作者工程的 URP/Lit 不能假定与游戏渲染配置兼容；沿用现有 NPC 的官方 shader 适配入口。
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    Material converted;
                    if (!convertedMaterials.TryGetValue(source, out converted))
                    {
                        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
                        converted = new Material(gameShader);
                        runtimeMaterials.Add(converted);
                        converted.name = "ArenaPrototype_" + source.name;
                        // 实机枚举确认 SodaCharacter 使用 _Tint，既非 _Color 也非 URP 的 _BaseColor。
                        converted.SetColor("_Tint", color);
                        convertedMaterials.Add(source, converted);
                    }
                    materials[i] = converted;
                }
                renderer.sharedMaterials = materials;
                Debug.Log("[ArenaPrototype] RENDER mesh=" + renderer.name + " enabled=" + renderer.enabled +
                    " bounds=" + renderer.bounds + " shader=" + renderer.sharedMaterial.shader.name);
            }
            lighting = new ArenaPrototypeLighting();
            lighting.Apply(arena);
            Camera renderCamera = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : Camera.main;
            Debug.Log("[ArenaPrototype] CAMERA mask=" + (renderCamera == null ? 0 : renderCamera.cullingMask) +
                " environmentLayer=" + groundLayer);
            foreach (Transform marker in arena.GetComponentsInChildren<Transform>(true))
            {
                if (outpost && (marker.name == "PlayerSpawn" || marker.name == "Exit" ||
                    marker.name.StartsWith("Search", StringComparison.Ordinal)) &&
                    (marker.parent != arena.transform || marker.localScale != Vector3.one))
                    throw new InvalidOperationException("前哨标记尚未归一化，请更新 stone_outpost 场景资源包");
                if (marker.name == "PlayerSpawn") playerSpawn = marker;
                if (marker.name == "EnemySpawn") enemySpawn = marker;
                if (marker.name == "Exit") exitMarker = marker;
                if (marker.name.StartsWith("EnemySpawn", StringComparison.Ordinal) && !marker.name.EndsWith("_Ring", StringComparison.Ordinal))
                    enemyMarkers.Add(marker);
                if (outpost && marker.name.StartsWith("Search", StringComparison.Ordinal))
                {
                    int interactLayer = LayerMask.NameToLayer("Interactable");
                    if (interactLayer < 0) throw new InvalidOperationException("交互层缺失");
                    marker.gameObject.layer = interactLayer;
                    BoxCollider trigger = marker.gameObject.AddComponent<BoxCollider>();
                    trigger.center = Vector3.up * 0.7f;
                    trigger.size = new Vector3(2.2f, 1.6f, 1.8f);
                    trigger.isTrigger = true;
                    marker.gameObject.AddComponent<StoneOutpostSearchPoint>().Bind(delegate
                    {
                        searchedPoints++;
                        if (map != null) map.MarkSearched(marker.name);
                        Status("已搜索前哨记录 " + searchedPoints + "/3 · 东南蓝环停留 3 秒撤离", false);
                    });
                }
            }
            if (playerSpawn == null || enemySpawn == null || exitMarker == null)
                throw new InvalidOperationException("场地出生/退出标记缺失");
            arena.SetActive(true);
            Physics.SyncTransforms();
            RaycastHit hit;
            if (!Physics.Raycast(playerSpawn.position + Vector3.up, Vector3.down, out hit, 3,
                layers.groundLayerMask, QueryTriggerInteraction.Ignore) || !hit.transform.IsChildOf(arena.transform))
                throw new InvalidOperationException("出生点脚下未命中自建地面");
            if (!Physics.Linecast(origin + new Vector3(-4, 1, outpost ? -13 : 0), origin + new Vector3(4, 1, outpost ? -13 : 0),
                out hit, wallMask, QueryTriggerInteraction.Ignore) || !hit.transform.IsChildOf(arena.transform))
                throw new InvalidOperationException("中央墙体未进入子弹碰撞层");
            Debug.Log("[ArenaPrototype] COLLISION_PASS ground=" + groundLayer + " wall=" + wallLayer);

            navigation = new ArenaPrototypeNavigation();
            Mesh mesh = arena.transform.Find("Navigation").GetComponent<MeshFilter>().sharedMesh;
            scan = navigation.BeginScan(mesh, origin);
            float deadline = Time.realtimeSinceStartup + 15;
            while (scan.MoveNext())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("专属导航图扫描超时");
                yield return null;
            }
            scan.Dispose();
            scan = null;
            int nodes = navigation.CountWalkableNodes();
            if (nodes == 0) throw new InvalidOperationException("导航图没有可走节点");
            Debug.Log("[ArenaPrototype] GRAPH_PASS nodes=" + nodes);
            probeSeeker = arena.AddComponent<Seeker>();
            probeSeeker.StartPath(playerSpawn.position, enemySpawn.position, OnProbePath, navigation.Mask);
            deadline = Time.realtimeSinceStartup + 10;
            while (!pathCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (!pathCompleted || !pathValid) throw new InvalidOperationException("出生点到敌人点的绕障路径验证失败");
            if (player == null || player != CharacterMainControl.Main || SceneLoader.IsSceneLoading)
                throw new InvalidOperationException("创建期间玩家或场景已改变");
            if (outpost)
            {
                map = new StoneOutpostMap();
                map.Apply(arena, origin);
            }
            player.SetPosition(playerSpawn.position + Vector3.up * 0.2f);
            if (map != null) map.Tick();
            playerMoved = true;
            enteredAt = Time.unscaledTime;
            ready = true;
            Status(outpost ? "石堡前哨 · 搜索 3 处记录 · 东南蓝环停留 3 秒撤离" :
                "石砌遗迹已就绪 · 走入蓝环返回 · F3 场景调试可生成敌人", false);
            Debug.Log("[ArenaPrototype] ENTER_PASS origin=" + origin + " return=" + returnPosition);
            if (outpost)
            {
                for (int i = 0; i < 3; i++)
                {
                    SpawnEnemy();
                    while (spawning && !closed) yield return null;
                    if (closed) yield break;
                }
                Status("石堡前哨 · 搜索记录 0/3 · 警戒敌人 " + enemies.Count + " · 东南蓝环撤离", false);
            }
        }

        private void OnProbePath(Pathfinding.Path path)
        {
            if (closed) return;
            pathCompleted = true;
            pathValid = !path.error && path.vectorPath != null && path.vectorPath.Count >= 2;
            if (pathValid)
            {
                for (int i = 1; i < path.vectorPath.Count; i++)
                    if (Physics.Linecast(path.vectorPath[i - 1] + Vector3.up, path.vectorPath[i] + Vector3.up,
                        wallMask, QueryTriggerInteraction.Ignore)) { pathValid = false; break; }
            }
            Debug.Log("[ArenaPrototype] PATH_" + (pathValid ? "PASS" : "FAIL") +
                " points=" + (path.vectorPath == null ? 0 : path.vectorPath.Count));
        }

        internal async void SpawnEnemy()
        {
            if (closed || !ready || spawning || (outpost ? enemies.Count >= 3 : (enemy != null && !enemy.Health.IsDead)))
            { Status("请等待场地就绪或先击败现有测试敌人", true); return; }
            spawning = true;
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            try
            {
                CharacterRandomPreset source = null;
                foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                    if (preset != null && !preset.isBoss && !preset.isZombie && preset.team == Teams.scav &&
                        preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0 &&
                        !preset.name.StartsWith("BossRush_ArenaPrototype", StringComparison.Ordinal) &&
                        (source == null || string.CompareOrdinal(preset.name, source.name) < 0)) source = preset;
                if (source == null) throw new InvalidOperationException("未找到已加载的普通拾荒者 preset");
                clone = Instantiate(source);
                clone.name = "BossRush_ArenaPrototype_TestEnemy";
                clone.dropBoxOnDead = false;
                clone.hasSoul = false;
                clone.canDieIfNotRaidMap = true;
                clone.setActiveByPlayerDistance = false;
                Transform spawn = outpost && enemyMarkers.Count > enemies.Count ? enemyMarkers[enemies.Count] : enemySpawn;
                created = await clone.CreateCharacterAsync(spawn.position, Vector3.forward, -1, null, false);
                // 官方异步 API 没有取消参数：迟到结果必须回收，不能在离场后漏出敌人。
                if (this == null || closed || arena == null || player == null || SceneLoader.IsSceneLoading)
                { if (created != null) Destroy(created.gameObject); return; }
                if (created == null) throw new InvalidOperationException("测试敌人创建失败");
                if (!outpost)
                {
                    foreach (CharacterMainControl old in enemies) if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
                    foreach (CharacterRandomPreset old in enemyPresets) if (old != null) Destroy(old);
                    enemies.Clear();
                    enemyPresets.Clear();
                }
                enemy = created;
                enemy.transform.SetParent(arena.transform, true);
                Seeker[] seekers = enemy.GetComponentsInChildren<Seeker>(true);
                if (seekers.Length == 0) throw new InvalidOperationException("测试敌人没有 A* Seeker");
                foreach (Seeker seeker in seekers)
                {
                    seeker.CancelCurrentPathRequest();
                    seeker.graphMask = navigation.Mask;
                }
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(enemy);
                enemy.SetTeam(Teams.wolf);
                AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>();
                if (ai == null) throw new InvalidOperationException("测试敌人缺少 AI 控制器");
                // 只为本次试验主动追击，不依赖初始朝向或跨墙视野才能验证寻路。
                ai.forceTracePlayerDistance = outpost ? 18f : 50f;
                // 角色仍引用此 preset；成功后将其所有权留到敌人回收，不能在 finally 销毁。
                enemies.Add(enemy);
                enemyPresets.Add(clone);
                clone = null;
                enemyDeathReported = false;
                Status("测试敌人已生成（无掉落）· 验证追击与障碍碰撞 · 蓝环返回", false);
                Debug.Log("[ArenaPrototype] ENEMY_READY preset=" + source.name + " seekers=" + seekers.Length);
            }
            catch (Exception e)
            {
                if (created != null) Destroy(created.gameObject);
                if (this != null && !closed) Status("生成敌人失败：" + e.Message, true);
            }
            finally
            {
                if (clone != null) Destroy(clone);
                if (this != null) spawning = false;
            }
        }

        private void Update()
        {
            if (closed || !ready) return;
            if (map != null) map.Tick();
            if (player == null || arena == null || !entryScene.isLoaded) { Close(entryScene.isLoaded, "owner_lost"); return; }
            string mode;
            if (host == null || !ModBehaviour.DevModeEnabled || host.ValidationHasActiveMode(out mode))
            { Close(true, "mode_changed"); return; }
            Vector3 local = player.transform.position - origin;
            if (local.y < -5 || Mathf.Abs(local.x) > (outpost ? 51 : 17) || Mathf.Abs(local.z) > (outpost ? 51 : 17))
            { Close(true, "bounds_return"); return; }
            if (Time.unscaledTime > enteredAt + 1 && Vector3.Distance(player.transform.position, exitMarker.position) < 1.3f)
            {
                if (!outpost) { Close(true, "exit_marker"); return; }
                if (extractionHeld < 0) extractionHeld = 0f;
                // 与官方 CountDownArea 同口径：背包、地图等官方界面打开时读秒冻结而不是清零；
                // 走游戏时间，暂停菜单（PauseMenu 不是 View）与拍照模式把 timeScale 压到 0 时同样冻结。
                if (View.ActiveView == null) extractionHeld += Time.deltaTime;
                float remaining = ExtractionHold - extractionHeld;
                if (hud != null) hud.text = "正在撤离 · " + Mathf.CeilToInt(Mathf.Max(0, remaining)) + " 秒 · 搜索记录 " + searchedPoints + "/3";
                if (remaining <= 0) { Debug.Log("[StoneOutpost] EXTRACT records=" + searchedPoints); Close(true, "outpost_extract"); return; }
            }
            else if (outpost && extractionHeld >= 0)
            {
                extractionHeld = -1;
                Status("撤离已中止 · 搜索记录 " + searchedPoints + "/3 · 回到蓝环可再次撤离", false);
            }
            if (!outpost && enemy != null && enemy.Health != null && enemy.Health.IsDead && !enemyDeathReported)
            {
                enemyDeathReported = true;
                Status("测试敌人已击败 · 可再生成一个，或走入蓝环返回", false);
                Debug.Log("[ArenaPrototype] ENEMY_DEAD");
            }
        }

        private void OnStartedLoading(SceneLoadingContext context) { Close(true, "scene_loading"); }
        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.handle == entryScene.handle) Close(false, "scene_unloaded");
            else if (outpost && StoneOutpostSceneLease.IsResourceScene(scene)) Close(true, "outpost_scene_unloaded");
        }
        private void OnPlayerDied(DamageInfo damage) { Close(true, "player_dead"); }

        internal void Close(bool restorePlayer, string reason)
        {
            if (closed) return;
            closed = true;
            ready = false;
            StopAllCoroutines();
            Safe("return", delegate
            {
                if (restorePlayer && playerMoved && player != null && entryScene.isLoaded)
                    player.SetPosition(returnPosition);
            });
            if (subscribed)
            {
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                if (playerHealth != null) playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);
                subscribed = false;
            }
            Safe("map", delegate { if (map != null) map.Dispose(); });
            map = null;
            Safe("enemy", delegate { foreach (CharacterMainControl owned in enemies) if (owned != null) { owned.gameObject.SetActive(false); Destroy(owned.gameObject); } enemies.Clear(); });
            Safe("preset", delegate { foreach (CharacterRandomPreset owned in enemyPresets) if (owned != null) Destroy(owned); enemyPresets.Clear(); });
            Safe("path", delegate { if (probeSeeker != null) probeSeeker.CancelCurrentPathRequest(); });
            Safe("scan", delegate { if (scan != null) scan.Dispose(); });
            scan = null;
            Safe("graph", delegate { if (navigation != null) navigation.Dispose(); });
            navigation = null;
            Safe("lighting", delegate { if (lighting != null) lighting.Dispose(); });
            Safe("arena", delegate { if (arena != null) { arena.SetActive(false); if (!outpost) Destroy(arena); } });
            Safe("materials", delegate { foreach (Material material in runtimeMaterials) if (material != null) Destroy(material); });
            runtimeMaterials.Clear();
            Safe("bundle", delegate { if (bundle != null) bundle.Unload(true); });
            bundle = null;
            if (hudRoot != null) Destroy(hudRoot);
            Debug.Log("[ArenaPrototype] CLEANUP reason=" + reason);
            if (sceneLease != null) sceneLease.Release(delegate { if (this != null) Destroy(this); });
            else Destroy(this);
        }

        private void OnDestroy() { Close(true, "component_destroyed"); }

        private void Status(string message, bool error)
        {
            if (hud != null) hud.text = message;
            if (report != null) report(message, error);
            if (error) Debug.LogWarning("[ArenaPrototype] " + message);
            else Debug.Log("[ArenaPrototype] " + message);
        }

        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }

        private static void Safe(string step, Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning("[ArenaPrototype] cleanup " + step + ": " + e.Message); }
        }
    }
}
