"""蚀日花冠常规玩家入口、专属图腾、致死前伤害与清理接线的结构守卫。

不证明游戏内可达性、Harmony IL 安装、外观和性能；行为由生产源码执行回归补充。
"""
from pathlib import Path
import json
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from compile_list import read_compile_sources

PREFIX = "Integration/EclipseBloom/"
FIXED_PATHS = (
    "ModBehaviourRuntimeModules.cs", "Integration/FieldAdventures/ExplorationRuntimeRegistration.cs",
    "compile_official.bat", "Config/ConfigItemIds.cs",
    "Config/LootBlacklistRegistry.cs", "Assets/Data/LootBlacklist.json",
    "Integration/Items/ItemContentRegistry.cs", "Integration/EquipmentConfiguratorBootstrap.cs",
    "Integration/BossRushDynamicItemRegistry.cs", "Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs",
    "Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs",
    "Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs",
    "Integration/NewWeapons/Common/NewWeaponRuntime.cs", "Integration/NewWeapons/Common/NewWeaponEquipState.cs",
    "Localization/EclipseBloomLocalization.cs", "ZombieMode/ZombieModeDamageRuntime.cs",
    "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
    "Patches/Combat/CharacterOnDeadPatch.cs",
    "Integration/EquipmentFactory_RuntimeModels.cs",
    "WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs", "WavesArena/WavesArenaRuntimeModule_LegacySpawn.cs",
    "Utilities/EnemySpawnHostBridge.cs", "Utilities/BossPoolSpawnBindings.cs", "Utilities/EnemySpawnCore.cs", "Integration/IntegrationHostCompatibility.cs",
    "Audio/BossBgmCoordinator.cs", "Assets/Data/Audio/BgmTracks.json",
)


def compact(value):
    return re.sub(r"\s+", " ", value).strip()


def validate(root=ROOT):
    checks = 0
    cache = {}

    def check(condition, label):
        nonlocal checks
        assert condition, label
        checks += 1

    def source(path):
        if path not in cache:
            cache[path] = clean_source((root / path).read_text(encoding="utf-8-sig"))
        return cache[path]

    def body(path, signature):
        value = source(path)
        check(value.count(signature) == 1, path + ": 方法签名不唯一或缺失 " + signature)
        start = value.index("{", value.index(signature)) + 1
        end, depth = start, 1
        # 所选生产方法没有包含花括号的字面量；注释由共享 clean_source 先剥离。
        while depth:
            assert end < len(value), path + ": 方法体未闭合 " + signature
            depth += (value[end] == "{") - (value[end] == "}")
            end += 1
        result = compact(value[start:end - 1])
        check(not re.match(r"return(?:\s+(?:false|true|null))?\s*;", result),
              path + ": 方法开头无条件早返 " + signature)
        return result

    def require(path, signature, *statements):
        value = body(path, signature)
        for statement in statements:
            check(compact(statement) in value, path + " / " + signature + ": 缺少 " + statement)
        return value

    def ordered(value, label, *statements):
        previous = -1
        for statement in statements:
            position = value.find(compact(statement), previous + 1)
            check(position >= 0, label + ": 顺序缺少 " + statement)
            previous = position

    module = PREFIX + "EclipseBloomRuntimeModule.cs"
    placement = PREFIX + "EclipseBloomEncounterPlacement.cs"
    ground = PREFIX + "EclipseBloomGroundProjection.cs"
    visuals = PREFIX + "EclipseBloomVisuals.cs"
    model = PREFIX + "ResidualSunShieldModel.cs"
    icon = PREFIX + "ResidualSunShieldIcon.cs"
    authored = PREFIX + "EclipseBloomAuthoredAssets.cs"
    assets = PREFIX + "EclipseBloomAssetManager.cs"
    pool = PREFIX + "EclipseBloomPoolAdapter.cs"
    intro = PREFIX + "EclipseBloomIntroduction.cs"
    persistence = PREFIX + "EclipseBloomIntroductionPersistence.cs"
    boss = PREFIX + "EclipseBloomBoss.cs"
    combat = PREFIX + "EclipseBloomAbilityController.cs"
    petal = PREFIX + "EclipseBloomPetal.cs"
    shield = PREFIX + "ResidualSunShieldRuntime.cs"
    config = PREFIX + "ResidualSunShieldWeaponConfig.cs"
    runtime = "Integration/NewWeapons/Common/NewWeaponRuntime.cs"
    placeholder = "Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs"
    central = "Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs"
    maintenance = "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs"

    compiled = set(read_compile_sources(root / "compile_official.bat"))
    for path in sorted((root / PREFIX).glob("*.cs")):
        relative = path.relative_to(root).as_posix()
        check(relative in compiled, relative + ": 缺正式编译清单登记")
    check("Localization/EclipseBloomLocalization.cs" in compiled, "花冠本地化未进入正式编译")
    require("ModBehaviourRuntimeModules.cs", "private void RegisterRuntimeModules()",
            "ExplorationRuntimeRegistration.Register(runtimeModuleHost);")
    require("Integration/FieldAdventures/ExplorationRuntimeRegistration.cs", "internal static void Register(BossRushRuntimeModuleHost host)",
            "host.Register(new EclipseBloomRuntimeModule());",
            "host.Register(new EclipseBloomIntroduction());")
    require(module, "public override void OnAwake(", "owner = host;", "Current = this;")
    other = body(module, "internal bool OtherModeActive()")
    for state in ("owner.IsActive", "owner.IsCampaignFinalBossActive", "owner.CampaignModeDActiveForRuntime",
                  "owner.CampaignModeEActiveForRuntime", "owner.CampaignModeFActiveForRuntime",
                  "owner.CampaignModeGActiveForRuntime", "ZombieModeLifecyclePhase.None", "ModeHLifecycle.None",
                  "owner.CampaignArenaActiveForRuntime", "WavesArenaRuntimeModule.ArenaPlanned", "WavesArenaRuntimeModule.ArenaActive",
                  "BossRushMapSelectionHelper.GetPendingEntryKind() != BossRushPendingEntryKind.None"):
        check(state in other, module + ": 缺并行模式隔离 " + state)
    check("BeginChallenge" not in source(module) and "AddComponent<EclipseBloomInteractable>" not in source(module),
          "探索花冠不得恢复竞技场花苞交互入口")
    refresh = require(module, "private bool RefreshLevelOwner()", "EclipseBloomEncounterPlacement.TryGetLevelOwnerScene(out current)",
                      "levelOwner != LevelManager.Instance || levelScene.handle != current.handle", "consumed = false;",
                      "levelOwner = LevelManager.Instance;", "levelScene = current;")
    check(refresh.count("consumed = false;") == 2, "只有退出关卡和更换关卡 owner 可以恢复探索名额")
    begin = require(module, "public override void OnUpdate(", "if (!RefreshLevelOwner()) return;",
                    "if (!IsCurrent(generation)) { Cleanup(); return; }", "if (consumed || SceneLoader.IsSceneLoading",
                    "EclipseBloomIntroduction.CanSpawnEncounter", "EclipseBloomEncounterPlacement.TryGetExplorationScene(out target)",
                    "EclipseBloomEncounterPlacement.TryResolveCandidate(candidateIndex,")
    ordered(begin, module + " 自然生成提交", "EclipseBloomEncounterPlacement.TryResolveCandidate(candidateIndex,",
            "if (!found) return;", "active = true;", "spawning = true;", "int token = ++generation;", "Spawn(token, position, target).Forget();")
    require(module, "private bool CanRunEncounter()", "!player.Health.IsDead", "EclipseBloomIntroduction.CanSpawnEncounter",
            "!OtherModeActive()", "EclipseBloomEncounterPlacement.IsCurrent(encounterScene, levelScene, levelOwner)")
    require(module, "private bool IsCurrent(", "token == generation", "Current == this", "CanRunEncounter()")
    commit = require(module, "private async UniTask Spawn(", "await EclipseBloomBoss.SpawnAsync(owner, position, () => IsCurrent(token), target);",
                     "UnityEngine.Object.Destroy(created.gameObject);", "boss = created;", "consumed = true;", "Cleanup();")
    ordered(commit, module + " 旧结果不可改新状态", "if (!IsCurrent(token))", "UnityEngine.Object.Destroy(created.gameObject);",
            "return;", "boss = created;", "spawning = false;", "if (created != null)", "consumed = true;")
    require(module, "public override void OnSceneLoaded(", "RefreshLevelOwner();", "if (active && !IsCurrent(generation)) Cleanup();")
    require(module, "private void Cleanup()", "generation++;", "active = false;", "spawning = false;",
            "controller.StopBattle(false);", "UnityEngine.Object.Destroy(boss.gameObject);", "boss = null;")
    check("consumed = false;" not in body(module, "private void Cleanup()"), "取消已提交遭遇不得返还本次出击名额")
    require(module, "public override void OnDestroy()", "Cleanup();", "if (Current == this) Current = null;", "owner = null;")
    require(placement, "internal static bool TryGetLevelOwnerScene(", "level.IsBaseLevel", "level.gameObject.scene",
            "core != null ? core.gameObject.scene : level.gameObject.scene")
    require(placement, "internal static bool TryGetExplorationScene(", "SceneManager.GetSceneByName(SceneName)",
            "IsLoaded(scene)", "ModBehaviour.GetMapConfigBySceneName(scene.name) != null")
    check('internal const string SceneName = "Level_SnowMilitaryBase";' in compact(source(placement)), "探索目标必须是已登记的 37 号地图")
    require(placement, "internal static bool IsCurrent(", "LevelManager.Instance == levelOwner", "!SceneLoader.IsSceneLoading",
            "IsLoaded(encounterScene)", "currentLevel.handle == levelScene.handle")
    require(placement, "internal static void BindToScene(", "instance.transform.SetParent(null, true);", "SceneManager.MoveGameObjectToScene(instance, scene);")
    for path in (module, placement, boss, combat):
        check("SceneManager.GetActiveScene()" not in source(path), path + ": 运行时归属不得依赖活动场景")

    require(module, "internal static void NotifyOwnedBossDefeated(", "current.boss != defeated",
            "!current.IsCurrent(current.generation)", "EclipseBloomIntroduction.NotifyDefeated();")
    require(combat, "private void OnBossDead(", "EclipseBloomRuntimeModule.NotifyOwnedBossDefeated(boss);")
    check("EclipseBloomIntroduction.NotifyDefeated()" not in source(combat), "池中击杀不得绕过原生实例身份核验推进引导")
    require(pool, "internal static void Register(", "owner.FindArenaEnemyPreset(EclipseBloomBoss.NameKey) != null",
            "owner.AddArenaEnemyPreset(new EnemyPresetInfo", "team = (int)Teams.wolf", "baseHealth = EclipseBloomTuning.BaseHealth", "baseDamage = 34f")
    pool_spawn = require(pool, "internal static async UniTask<CharacterMainControl> SpawnAsync(",
            "owner.IsModeEActive || owner.IsModeFActive", "EclipseBloomEncounterPlacement.TryGetSpawnScene(out scene)",
            "EclipseBloomEncounterPlacement.IsCurrent(scene, levelScene, levelOwner)",
            "EclipseBloomBoss.SpawnAsync(owner, position, current, scene)", "if (!isNonWaveSpawn)",
            "owner.RegisterArenaWaveBossFromContent(character);", "EclipseBloomPoolWaveLease")
    ordered(pool_spawn, pool + " 异步取消先于波次归属", "await EclipseBloomBoss.SpawnAsync(",
            "if (!current())", "UnityEngine.Object.Destroy(character.gameObject);", "owner.RegisterArenaWaveBossFromContent(character);")
    check("SceneManager.GetActiveScene()" not in source(pool), "池中花冠也必须保持真实关卡场景 owner")
    require(pool, "private void OnDestroy()", "owner.ClearArenaCurrentBossFromContent(character);",
            "owner.RemoveArenaWaveBossFromContent(character);")
    require("WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs", "internal void InitializeEnemyPresets()",
            "EclipseBloomPoolAdapter.Register(owner);")
    require("WavesArena/WavesArenaRuntimeModule_LegacySpawn.cs", "internal async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(",
            "if (EclipseBloomPoolAdapter.IsPreset(preset))", "await EclipseBloomPoolAdapter.SpawnAsync(owner, position,")
    require("Utilities/EnemySpawnHostBridge.cs", "private void BindSpawnPostprocessServices()",
            "BossPoolSpawnBindings.Bind(enemySpawnRuntime, this);")
    require("Utilities/BossPoolSpawnBindings.cs", "internal static void Bind(",
            "runtime.BindEclipseBloomService(", "EclipseBloomPoolAdapter.SpawnAsync(host, position, active, nonWave, defer)")
    shared_spawn = require("Utilities/EnemySpawnCore.cs", "internal async UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(",
            "else if (EclipseBloomPoolAdapter.IsPreset(currentPreset))", "if (SpawnEclipseBloom == null)",
            "character = await SpawnEclipseBloom(position, false, deferActivationUntilNextFrame,",
            "EclipseBloomPoolAdapter.CleanupCancelled(character);")
    ordered(shared_spawn, "共享生成必须在官方 preset 查找前分派花冠", "else if (EclipseBloomPoolAdapter.IsPreset(currentPreset)) {",
            "character = await SpawnEclipseBloom(", "cachedCharacterPresets.TryGetValue(currentPreset.name, out targetPreset);")
    require("Integration/IntegrationHostCompatibility.cs", "private bool IsManagedBossPreset(", "EclipseBloomPoolAdapter.IsPreset(preset)")

    require(intro, "public override void OnAwake(", "new EclipseBloomIntroductionPersistence(", "projection.Register(BuildBinding());")
    binding = require(intro, "private OfficialQuestBinding BuildBinding()", "GiverId = (int)QuestGiverID.Jeff",
            "BeginDelivery = BeginDelivery", "EndDelivery = EndDelivery", "data.Completed || data.Defeated")
    check("RequiredItemId =" not in binding and "RewardMoney =" not in binding,
          "调查引导不得收走盾片或添加经济奖励")
    check("current.persistence.Writable && !current.abandonPending" in compact(source(intro)), "死亡待持久化期间必须封闭自然刷新")
    abandon = require(intro, "private void AbandonOnDeath()", "abandonPending = true;",
            "if (Commit(new EclipseBloomIntroductionData())) abandonPending = false;", "RemoveMapMarker();")
    ordered(abandon, intro + " 死亡先撤销投影再提交", "abandonPending = true;", "Commit(new EclipseBloomIntroductionData())")
    require(intro, "private void OnSlotChanged()", "data = null;", "openedSlot = -1;", "abandonPending = false;", "RemoveMapMarker();")
    require(intro, "private bool BeforeCollectSaveData()", "if (deliveryActive) return false;", "return !abandonPending;")
    require(intro, "private void RefreshPlayerDeathSubscription()", "if (playerHealth == next) return;",
            "playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);", "playerHealth.OnDeadEvent.AddListener(OnPlayerDied);")
    require(intro, "public override void OnDestroy()", "playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);",
            "projection.UnregisterClient(this);", "persistence.Dispose();")
    require(persistence, "internal EclipseBloomIntroductionPersistence(", "new BossRushSaveCoordinatorEngine(this, true)",
            "new BossRushSlotJsonStore<EclipseBloomIntroductionData>", "BeforeCollectSaveData = beforeCollect")
    require(persistence, "internal static EclipseBloomIntroductionData Decode(", "schema != 1",
            "(defeated && !accepted)", "(completed && (accepted || defeated))")
    require(persistence, "bool IBossRushSaveBatchSource.CollectSnapshot(", "bool allowed = canFlush == null || canFlush();", "return allowed;")
    require(persistence, "public void Dispose()", "store.ShutdownSubscription();", "store.ResetAll();", "coordinator.Reset();")

    require(boss, "private static CharacterRandomPreset FindBasePreset()", 'preset.name != "BossRush_EclipseBloom_Runtime"', "preset.nameKey == key")
    spawn = require(boss, "internal static async UniTask<CharacterMainControl> SpawnAsync(",
                    "if (!Team.IsEnemy(Teams.player, boss.Team)) boss.SetTeam(Teams.wolf);",
                    "SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(boss);", "targetScene.buildIndex, null, false);",
                    "EclipseBloomEncounterPlacement.BindToScene(boss.gameObject, targetScene);")
    ordered(spawn, boss + " 普通生成与专属掉落", "runtimePreset.hasSkill = false;", "runtimePreset.skillPfb = null;",
            "boss = await runtimePreset.CreateCharacterAsync(", "RemoveOriginalCombat(boss);",
            "BossRushDynamicItemRegistry.EnsureRegistered(BossRushItemIds.ResidualSunShield);",
            "reward = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ResidualSunShield);",
            "ResidualSunShieldWeaponConfig.TryConfigure(reward)", "controller.Initialize(", "reward = null;")
    ordered(spawn, boss + " 官方计数身份", "runtimePreset.nameKey = source.nameKey;",
            "boss = await runtimePreset.CreateCharacterAsync(",
            "controller.Initialize(boss, runtimePreset, reward, baseline, isCurrent, sceneHandle, source);",
            "runtimePreset.nameKey = NameKey;")
    ordered(spawn, boss + " 提交前不可受击", "RemoveOriginalCombat(boss);", "boss.gameObject.SetActive(false);",
            "await UniTask.Yield();", "if (boss == null || boss.Health == null || boss.Health.IsDead)",
            "ResidualSunShieldWeaponConfig.TryConfigure(reward)", "controller.Initialize(", "boss.gameObject.SetActive(true);")
    require(combat, "internal void Initialize(", "EclipseBloomPetal.Create(this, i,", "boss.BeforeCharacterSpawnLootOnDead += OnBeforeSpawnLoot;",
            "boss.Health.OnDeadEvent.AddListener(OnBossDead);", "subscribed = true;",
            "originalPreset = officialPreset;", "CreateCoreReceiver();",
            "BossRushAudioManager.Instance?.PlayBossBGM(BossBgmKeys.EclipseBloom, boss);")
    require(combat, "internal void StopBattle(",
            "BossRushAudioManager.Instance?.StopBossBGM(BossBgmKeys.EclipseBloom, boss);")
    check("BossBgmKeys.PhantomWitch" not in source(combat), "花冠不得借用魔女音乐身份")
    check('internal const string EclipseBloom = "EclipseBloom";' in compact(source("Audio/BossBgmCoordinator.cs")),
          "花冠独立音乐 key 必须匹配数据表")
    tracks = json.loads((root / "Assets/Data/Audio/BgmTracks.json").read_text(encoding="utf-8-sig"))["bossTracks"]
    flower_tracks = [entry for entry in tracks if entry.get("bossKey") == "EclipseBloom"]
    check(len(flower_tracks) == 1 and flower_tracks[0].get("file") == "boss_eclipse_bloom_loop.wav"
          and flower_tracks[0].get("loop") is True and flower_tracks[0].get("phase") == 0,
          "花冠必须登记唯一的独立 WAV 循环曲目")
    check(not any(entry.get("bossKey") != "EclipseBloom" and entry.get("file") == "boss_eclipse_bloom_loop.wav" for entry in tracks),
          "花冠独立音频不得被其他 Boss 曲目共用")
    restore = require(combat, "internal static bool RestoreOfficialPresetBeforeDeath(",
                      "character.GetComponent<EclipseBloomAbilityController>()",
                      "controller.originalPreset == null", "character.characterPreset = controller.originalPreset;", "return true;")
    ordered(restore, combat + " 恢复官方身份", "controller.originalPreset == null",
            "character.characterPreset = controller.originalPreset;", "return true;")
    prefix = body("Patches/Combat/CharacterOnDeadPatch.cs", "public static void Prefix(")
    check(prefix.startswith("if (EclipseBloomAbilityController.RestoreOfficialPresetBeforeDeath(__instance)) return;"),
          "官方死亡前缀必须先恢复花冠计数身份并跳过底模额外掉落")
    update = body(combat, "private void Update()")
    ordered(update, combat + " 同帧显示与伤害", "rules.Advance(", "ApplyFrame();", "SyncPetals();", "TryDamagePlayer();")
    require(combat, "private void TryDamagePlayer()", "rules.IsDangerous(offset.x, offset.z)", "Physics.Linecast(",
            "if (body == null || !body.HasGroundWarningAt(player.transform.position)) return;", "player.mainDamageReceiver.Hurt(info);")
    require(combat, "internal void BreakPetal(", "rules.BreakPetal(index)", "ApplyFrame();", "SyncPetals();")
    require(petal, "internal static EclipseBloomPetal Create(", "receiver.useSimpleHealth = true;",
            "petal.health.dmgReceiver = receiver;", "receiver.simpleHealth = petal.health;",
            "petal.health.OnDeadEvent += petal.OnBroken;", "go.AddComponent<CapsuleCollider>();",
            "collider.direction = 2;", "collider.height = 3.4f;", "collider.radius = 0.78f;")
    require(combat, "private void CreateCoreReceiver()", "old.GetComponents<Collider>()",
            "hiddenHitColliders.Add(collider);", "collider.enabled = false;",
            "coreHit.AddComponent<SphereCollider>();", "sphere.radius = body.GetCoreRadius();",
            "receiver.health = boss.Health;", "receiver.useSimpleHealth = false;", "coreHit.SetActive(true);")
    require(combat, "private void SyncPetals()", "coreHit.transform.position = body.GetCorePosition();",
            "petals[i].transform.position = body.GetPetalPosition(i);",
            "petals[i].transform.rotation = body.GetPetalRotation(i);",
            "petals[i].transform.localScale = body.GetPetalScale(i);")
    require(petal, "private void OnDestroy()", "health.OnDeadEvent -= OnBroken;", "owner = null;")
    preloot = require(combat, "private void OnBeforeSpawnLoot(", "!subscribed", "!boss.Health.IsDead",
            "EclipseBloomEncounterPlacement.IsLoaded(boss.gameObject.scene)", "inventory.AddAndMerge(reward, 0)", "reward.Drop(", "reward = null;")
    check("isCurrent()" not in preloot and "SceneLoader.IsSceneLoading" not in preloot,
          "已提交实例真实死亡的奖励不得被玩家同帧死亡或切场景状态撤销")
    require(combat, "internal void StopBattle(", "rules.Stop();", "Destroy(petals[i].gameObject);",
            "boss.BeforeCharacterSpawnLootOnDead -= OnBeforeSpawnLoot;", "boss.Health.OnDeadEvent.RemoveListener(OnBossDead);",
            "if (reward != null) Destroy(reward.gameObject);", "reward = null;", "if (coreHit != null) Destroy(coreHit);",
            "foreach (Collider collider in hiddenHitColliders) if (collider != null) collider.enabled = true;",
            "hiddenHitColliders.Clear();")
    require(combat, "private void OnDestroy()", "StopBattle(false);", "isCurrent = null;", "boss = null;")

    require(ground, "internal void Step(", "if (frame == Time.frameCount) return;", "frame = Time.frameCount;",
            "Mathf.Min(cursor + SamplesPerFrame, heights.Length)", "System.Array.Clear(valid, 0, valid.Length);")
    check("internal const int SamplesPerFrame = 96;" in compact(source(ground)), "地面预警采样预算必须保持每帧 96")
    require(ground, "internal bool TryHeight(", "if (!initialized) return false;",
            "if (!valid[a] || !valid[b] || !valid[c] || !valid[d]) return false;", "if (max - min > 0.65f) return false;")
    require(ground, "internal static bool ContainsTriangle(", "u >= -0.0001f && v >= -0.0001f && w >= -0.0001f",
            "Mathf.Abs(point.y - (u * a.y + v * b.y + w * c.y)) <= 0.45f")
    require(visuals, "internal void PrepareGround()", "_ground.Step(_root.position);")
    require(visuals, "private bool ProjectPoint(", "_ground.TryHeight(world, out height)",
            "point.y = height - _root.position.y + 0.045f;")
    require(visuals, "private void ProjectSector(", "valid[j] = ProjectPoint(ref points[j]);",
            "if (valid[original[j]] && valid[original[j + 1]] && valid[original[j + 2]])", "_meshes[sector].SetTriangles(indices, 0);")
    require(visuals, "internal bool CoversPoint(", "_sectors[sector].activeInHierarchy", "!_fills[sector].enabled",
            "_fills[sector].forceRenderingOff", "_fills[sector].sharedMaterial == null",
            "valid[a] && valid[b] && valid[c] && EclipseBloomGroundProjection.ContainsTriangle(",
            "valid[b] && valid[d] && valid[c] && EclipseBloomGroundProjection.ContainsTriangle(")
    require(PREFIX + "EclipseBloomBody.cs", "internal bool HasGroundWarningAt(", "_sectors.CoversPoint(position)")

    require(model, "internal static bool Bind(", "EquipmentFactory.RegisterRuntimeEquipmentModel(item, model)",
            "item.useSpriteForPickup = false;", 'item.AgentUtilities.GetPrefab("EquipmentModel") == model')
    require(model, "internal static DuckovItemAgent GetTemplate()", "EquipmentFactory.TryGetLoadedModel(ModelBaseName, out loaded)",
            "EquipmentFactory.LoadBundle(BundleName);", "DuckovItemAgent model = loaded as DuckovItemAgent;",
            'if (model == null || model.transform.Find("GroundPoint") == null || model.GetComponentsInChildren<MeshFilter>(true).Length != 6) return null;',
            "template = model;")
    check('internal const string BundleName = "residual_sun_shield_model";' in compact(source(model))
          and 'internal const string ModelBaseName = "ResidualSunShield_Totem";' in compact(source(model)), "盾片正式模型必须使用既定作者 bundle 与 prefab 名")
    check("Destroy(" not in source(model) and "ResidualSunShieldModelGeometry" not in source(model),
          "盾片模型仅借用工厂资源，不得自行销毁或用草稿工具替代正式资源")
    require(model, "internal static void ResetStaticCaches()", "template = null;")
    require("Integration/EquipmentFactory_RuntimeModels.cs", "internal static bool RegisterRuntimeEquipmentModel(",
            'SetAgentUtilityPrefab(item, "EquipmentModel", model);', "InjectItemGraphicForEquipment(item, model, true);",
            "if (item.ItemGraphic == null || item.ItemGraphic.gameObject != model.gameObject) return false;")
    borrowed = require("Integration/EquipmentFactory_RuntimeModels.cs", "internal static T GetLoadedEquipmentAsset<T>(",
            "foreach (UnityEngine.AssetBundle bundle in ownedBundles)", "System.StringComparison.OrdinalIgnoreCase",
            "return bundle.LoadAsset<T>(assetPath);")
    check("Unload(" not in borrowed and "Destroy(" not in borrowed and "LoadFromFile(" not in borrowed,
          "工厂已拥有资源查询只借用，不得再加载或卸载 bundle")
    require(icon, "internal static Sprite GetSprite(", "ResidualSunShieldModel.GetTemplate()", "owner.EnsureSprite()")
    require(icon, "private Sprite EnsureSprite()", "EquipmentFactory.GetLoadedEquipmentAsset<Sprite>(ResidualSunShieldModel.BundleName, BundleIconAssetPath)",
            "loaded.texture.width != Size", "loaded.texture.height != Size", "sprite = loaded;")
    check("private const int Size = 512;" in compact(source(icon)), "正式盾片图标尺寸必须保持 512")
    check(re.search(r"\bDestroy\s*\(", source(icon)) is None and "ProductionIconCache" not in source(icon) and "File.ReadAllBytes" not in source(icon),
          "正式盾片图标只借用专属包资源，不得销毁工厂资产或回退旧图标")
    require(icon, "private void OnDestroy()", "sprite = null;")
    require(authored, "internal static void Acquire()", "File.Exists(path)", "ResourceBundleLoader.LoadFromFile(path)",
            "Petal = LoadMesh(\"petal\");", "Core = LoadMesh(\"core\");", "Fragment = LoadMesh(\"fragment\");", "references++;")
    require(authored, "internal static void Release()", "if (references > 0) references--;", "if (references == 0) ResetStaticCaches();")
    require(authored, "internal static void ResetStaticCaches()", "bundle.Unload(true);", "references = 0;", "Petal = Core = Fragment = null;")
    require(assets, "internal EclipseBloomAssetManager(", "EclipseBloomAuthoredAssets.Acquire();", "_authoredLease = true;",
            "Petal = EclipseBloomAuthoredAssets.Petal;", "Core = EclipseBloomAuthoredAssets.Core;", "Gold = EclipseBloomAuthoredAssets.Gold;")
    require(assets, "public void Dispose()", "if (_disposed) return;", "_owned.Clear();", "if (_authoredLease) EclipseBloomAuthoredAssets.Release();")

    require("Integration/Items/ItemContentRegistry.cs", "private void RegisterItemContentConfigurators()",
            "NewWeaponItemConfigurators.RegisterAll();", "EquipmentConfiguratorBootstrap.RegisterAll();")
    item_registry = "Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs"
    require(item_registry, "public static void RegisterAll()",
            "ItemFactory.RegisterConfigurator(BossRushItemIds.ResidualSunShield, item => ResidualSunShieldWeaponConfig.TryConfigure(item));")
    require("Integration/EquipmentConfiguratorBootstrap.cs", "internal static void RegisterAll()",
            "ResidualSunShieldWeaponConfig.RegisterEquipmentConfigurator();")
    require(placeholder, "public static bool EnsureRegistered(int typeId)", "typeId == BossRushItemIds.ResidualSunShield",
            '"BossRush_ResidualSunShield"', "ResidualSunShieldConfig.BaseName")
    require(placeholder, "private static bool TryConfigureWeapon(",
            "if (typeId == BossRushItemIds.ResidualSunShield)", "ResidualSunShieldWeaponConfig.TryConfigure(clone, baseName)")
    require("Integration/BossRushDynamicItemRegistry.cs", "private static Dictionary<int, RegistrationPlan> BuildPlans()",
            "Add(plans, new RegistrationPlan { EquipmentBundles = new string[] { \"residual_sun_shield_model\" }, FallbackLoader = EnsureNewWeaponPlaceholder }, BossRushItemIds.ResidualSunShield);")
    require(central, "internal void InjectLocalization_Extra_Integration()",
            "ResidualSunShieldWeaponConfig.InjectLocalization();", "EclipseBloomLocalization.Inject();")
    require(config, "public static bool Configure(Item item)", "item.Value = ResidualSunShieldConfig.ItemValue;",
            "ModeFItemConfigHelper.ClearInheritedUsage(item);", "if (!ResidualSunShieldModel.Bind(item)) return false;",
            "ResidualSunShieldIcon.GetSprite(item)", "if (icon == null) return false;", "item.Icon = icon;")
    require(config, "public static bool TryConfigure(Item item)", "return TryConfigure(item, ResidualSunShieldConfig.BaseName);")
    require(config, "public static bool TryConfigure(Item item, string baseName)",
            "NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)", "return Configure(item);")
    check("public const int ResidualSunShield = 500110;" in compact(source("Config/ConfigItemIds.cs")), "残日盾片 TypeID 身份改变")
    require("Config/LootBlacklistRegistry.cs", "private static int[] CreateFallbackBlacklistIds()", "BossRushItemIds.ResidualSunShield,")
    blacklist = json.loads((root / "Assets/Data/LootBlacklist.json").read_text(encoding="utf-8-sig"))
    check(500110 in blacklist["itemIds"], "残日盾片缺 JSON 随机掉落黑名单")
    tracked = re.search(r"TrackedTotemTypeIds\s*=\s*\{([^}]*)\}", source("Integration/NewWeapons/Common/NewWeaponEquipState.cs"))
    check(tracked is not None and "BossRushItemIds.ResidualSunShield" in tracked.group(1), "残日盾片未进入装备缓存追踪")
    require(runtime, "internal static void Initialize()", "ResidualSunShieldRuntime.Subscribe();")
    require(runtime, "internal static void SetupForScene(", "ResidualSunShieldRuntime.SetupForScene();")
    require(runtime, "internal static void CleanupOnDestroy()", "ResidualSunShieldRuntime.Unsubscribe();",
            "ResidualSunShieldModel.ResetStaticCaches();")
    require(runtime, "internal static void ConfigureAfterLoad()",
            "TryConfigureLoaded(BossRushItemIds.ResidualSunShield, ResidualSunShieldConfig.BaseName, ResidualSunShieldWeaponConfig.TryConfigure);")

    damage = "ZombieMode/ZombieModeDamageRuntime.cs"
    final = body(damage, "internal static void ReduceFinalDamage(")
    check(final.startswith("ResidualSunShieldRuntime.ModifyFinalDamage(health, ref info);"),
          damage + ": 护盾必须在丧尸模式早返前接入真正最终伤害入口")
    require(damage, "internal static IEnumerable<CodeInstruction> InjectBeforeHealthLoss(",
            "codes.InsertRange(firstStore + 1,", "nameof(ReduceFinalDamage)")
    require(shield, "private static bool IsUsableOwner(", "NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.ResidualSunShield)",
            "!player.Health.IsDead", "!LevelManager.Instance.IsBaseLevel")
    subscribe = body(shield, "internal static void Subscribe()")
    unsubscribe = body(shield, "internal static void Unsubscribe()")
    check("if (isSubscribed) return;" in subscribe, "残日盾片事件订阅缺幂等")
    for subscription in re.findall(r"([\w.]+) \+= (\w+);", subscribe):
        statement = subscription[0] + " -= " + subscription[1] + ";"
        check(statement in unsubscribe, "残日盾片缺对称退订 " + statement)
    require(shield, "private static void Deactivate()", "owner.OnActionStartEvent -= OnActionStarted;",
            "owner = null;", "rules.CancelPending();", "DestroyDriver();")
    modify = require(shield, "internal static void ModifyFinalDamage(", "info.isFromBuffOrEffect", "info.isExplosion",
                     "rules.TryBlock(info.finalDamage, Time.time)", "info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);",
                     "rules.TryConsumeCharge(info.finalDamage, Time.time)", "info.finalDamage += bonus;")
    check("AddHealth(" not in modify and "Health.OnHurt +=" not in source(shield), "残日盾片退回受伤后补血或全局 OnHurt")
    require(shield, "internal static void Tick(", "if (!rules.HasGuard(Time.time) && !rules.HasCharge(Time.time))",
            "DestroyDriver();")
    require(shield, "private static void OnSceneLoadingStarted(", "ResetStaticCaches();")
    require(shield, "private static void OnAnyDead(", "health.IsMainCharacterHealth", "ResetStaticCaches();")

    require(maintenance, "internal void ClearEnemiesForBossRush()",
            "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) return;")
    clear = require(maintenance, "internal IEnumerator ContinuousClearEnemiesUntilWaveStart()",
                    "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) { yield return owner.ArenaSharedWait05s; continue; }")
    ordered(clear, maintenance + " 空闲清场避让花冠", "EclipseBloomRuntimeModule.IsEncounterActive",
            "yield return owner.ArenaSharedWait05s;", "continue;", "loopCount++;", "ClearEnemiesForBossRush();")
    print("EclipseBloomIntegrationGuard: PASS (" + str(checks) + " checks)")
    return checks


if __name__ == "__main__":
    try:
        validate()
    except (AssertionError, OSError, ValueError) as error:
        print("EclipseBloomIntegrationGuard: FAIL " + str(error))
        raise SystemExit(1)
