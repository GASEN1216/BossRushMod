"""野外趣事的宿主、场景门、交互装配与生命周期结构守卫。

只检查生产调用链及官方 API 所需的装配顺序，不证明实机可达、碰撞效果或性能。
玩法数值、规则重入和场景判定行为由 FieldAdventureRules / FieldAdventureGate 执行回归覆盖。
"""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from compile_list import read_compile_sources

PREFIX = "Integration/FieldAdventures/"
FIXED_PATHS = ("ModBehaviourRuntimeModules.cs", "compile_official.bat")


def compact(value):
    return re.sub(r"\s+", " ", value).strip()


def validate(root=ROOT):
    root = Path(root)
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
        check(value.count(signature) == 1, path + ": 方法签名缺失或不唯一 " + signature)
        start = value.index("{", value.index(signature)) + 1
        end, depth = start, 1
        # 本守卫选取的方法没有含花括号的字符串字面量；注释由共享解析器去除。
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

    support = PREFIX + "FieldAdventureSupport.cs"
    registration = PREFIX + "ExplorationRuntimeRegistration.cs"
    mimic = PREFIX + "Mimic/GreedyMimicRuntimeModule.cs"
    encounter = PREFIX + "Mimic/GreedyMimicEncounter.cs"
    mimic_visual = PREFIX + "Mimic/GreedyMimicVisual.cs"
    delivery = PREFIX + "Delivery/OddParcelRuntimeModule.cs"
    prototype = PREFIX + "Prototypes/DangerousPrototypeRuntimeModule.cs"

    compiled = set(read_compile_sources(root / "compile_official.bat"))
    files = sorted((root / PREFIX).rglob("*.cs"))
    check(bool(files), PREFIX + ": 生产源码目录为空")
    for path in files:
        relative = path.relative_to(root).as_posix()
        check(relative in compiled, relative + ": 缺正式编译清单登记")
        check("partial class ModBehaviour" not in compact(source(relative)),
              relative + ": 新子系统状态不得落入宿主 partial")
    require("ModBehaviourRuntimeModules.cs", "private void RegisterRuntimeModules()",
            "ExplorationRuntimeRegistration.Register(runtimeModuleHost);")
    registration_body = body(registration, "internal static void Register(BossRushRuntimeModuleHost host)")
    for name in ("GreedyMimicRuntimeModule", "OddParcelRuntimeModule", "DangerousPrototypeRuntimeModule"):
        statement = "host.Register(new " + name + "());"
        check(registration_body.count(statement) == 1, registration + ": 必须唯一登记 " + name)

    require(support, "internal static bool TryGetRaid(",
            "if (owner == null || level == null || level.IsBaseLevel || !level.IsRaidMap || !LevelManager.AfterInit || SceneLoader.IsSceneLoading) return false;",
            "if (core != null && core.IsLoading) return false;",
            "if (!IsRaidScene(ownerScene)) return false;",
            "Scene? subScene = MultiSceneCore.ActiveSubScene;",
            "if (!IsRaidScene(scene) || !SceneRuntimeGate.CanRunGameplayRuntimeNow(scene.name)) return false;",
            "return player != null && player.Health != null && !player.Health.IsDead;")
    require(support, "internal static bool TryFindGround(",
            "if (!SpawnPositionHelper.IsReachableAnchor(origin)) return false;",
            "for (int i = 0; i < 12; i++)",
            "if (!SpawnPositionHelper.TryResolveReachableFrom(candidate, origin, 1.5f, 0f, out resolved)) continue;")

    update = require(mimic, "public override void OnUpdate(",
                     "if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene) || scene.handle != encounter.Scene.handle) { Cleanup(); return; }",
                     "encounter.SyncHudVisibility();",
                     "if (BossRushUI.IsGamePaused()) return;")
    ordered(update, mimic + ": HUD 必须先于暂停早返更新",
            "encounter.SyncHudVisibility();", "if (BossRushUI.IsGamePaused()) return;",
            "encounter.Tick(Mathf.Clamp(deltaTime, 0f, 0.1f), player);")
    require(encounter, "internal void SyncHudVisibility()", "if (visual != null) visual.SyncHudVisibility();")
    require(mimic_visual, "internal void SyncHudVisibility()",
            "bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();",
            "if (healthRoot.gameObject.activeSelf != visible) healthRoot.gameObject.SetActive(visible);")
    check("private const int BeamSegments = 8;" in compact(source(mimic_visual)),
          mimic_visual + ": 32 米长线必须使用八段独立迷雾采样")
    check("private const int TongueSegments = 12;" in compact(source(mimic_visual)),
          mimic_visual + ": 完整咬击射程必须使用十二段独立迷雾采样")
    check("private readonly Transform[] tongue = new Transform[TongueSegments];" in compact(source(mimic_visual)),
          mimic_visual + ": 舌头对象池必须独立于弹幕长度")
    require(mimic_visual, "internal GreedyMimicVisual(",
            'Facing = Group("StableFacing", Root.transform, Vector3.zero);',
            'Body = Group("BreathingBody", Facing, Vector3.zero);',
            "for (int j = 0; j < TongueSegments; j++) ModEntityFogVisibility.Attach(tongue[j].gameObject, tongue[j]);",
            "for (int j = 0; j < BeamSegments; j++) ModEntityFogVisibility.Attach(rays[i, j].gameObject);")
    require(mimic_visual, "internal void Tick(",
            "ApplyBodyPose(fighting, warning);",
            "Facing.rotation = Quaternion.Slerp(Facing.rotation, Quaternion.LookRotation(facing), Mathf.Min(1, dt * 3f));")
    require(mimic_visual, "internal void Grow(", "scale = rules.Scale; swallow = 1.4f;", "ApplyBodyPose(false, 0f);")
    require(mimic_visual, "internal GreedyMimicVisual(",
            "bodyDamageBounds = PartBounds(bodyParts);", "lidWoodDamageBounds = PartBounds(lidParts[0]);",
            "lidLipDamageBounds = PartBounds(lidParts[1]);")
    damage_bounds = require(mimic_visual, "internal void GetDamageBounds(",
                           "Matrix4x4 bodyToFacing = Matrix4x4.TRS(Body.localPosition, Body.localRotation, Body.localScale);",
                           "bodyBounds = TransformBounds(bodyDamageBounds, bodyToFacing);",
                           "Matrix4x4 lidToFacing = bodyToFacing * Matrix4x4.TRS(jaw.localPosition, jaw.localRotation, jaw.localScale);",
                           "lidBounds = TransformBounds(lidWoodDamageBounds, lidToFacing);",
                           "lidBounds.Encapsulate(TransformBounds(lidLipDamageBounds, lidToFacing));")
    check("GetComponent" not in damage_bounds and "PartBounds(" not in damage_bounds,
          mimic_visual + ": 当帧受击包络不得重新扫描网格，须复用构造时缓存")
    require(mimic_visual, "internal void ShowLane(", "end = start + Vector3.ClampMagnitude(end - start, 32f);",
            "for (int j = 0; j < BeamSegments; j++)")
    tongue = require(mimic_visual, "internal void StrikeTongue(",
                     "Vector3 start = Root.transform.position + Vector3.up * 1.6f, end = target + Vector3.up * 0.4f;",
                     "for (int j = 0; j < TongueSegments; j++)",
                     "Vector3 a = Vector3.Lerp(start, end, (float)j / TongueSegments), b = Vector3.Lerp(start, end, (float)(j + 1) / TongueSegments);")
    check("ClampMagnitude" not in tongue, mimic_visual + ": 舌尖必须到达 Encounter 锁定的实际落点")
    require(mimic_visual, "internal void ClearAttack()",
            "for (int j = 0; j < TongueSegments; j++) tongue[j].gameObject.SetActive(false);")
    receiver = body(encounter, "private void CreateReceiver()")
    require(encounter, "private void CreateReceiver()", "receiverRoot.transform.SetParent(visual.Facing, false);",
            "receiverRoot.transform.localPosition = new Vector3(0, 1.35f * Rules.Scale, 0);")
    check(compact(source(encounter)).count("AddComponent<BoxCollider>()") == 1,
          encounter + ": 轻量血量必须只有一个受击 Collider，官方爆炸不会为其去重")
    ordered(receiver, encounter + ": 官方 HealthSimpleBase.Awake 之前装配完整接收器",
            "receiverRoot.SetActive(false);", "receiver = receiverRoot.AddComponent<DamageReceiver>();",
            "receiver.OnHurtEvent = new UnityEvent<DamageInfo>();", "receiver.OnDeadEvent = new UnityEvent<DamageInfo>();",
            "health = receiverRoot.AddComponent<HealthSimpleBase>();", "health.team = Teams.wolf;",
            "health.maxHealthValue = Rules.MaximumHealth;", "health.dmgReceiver = receiver;",
            "receiver.simpleHealth = health;", "health.OnHurtEvent += OnHurt;", "health.OnDeadEvent += OnDead;",
            "SyncReceiverGeometry();", "receiverRoot.SetActive(true);")
    sync_geometry = require(encounter, "private void SyncReceiverGeometry()",
                            "visual.GetDamageBounds(out bodyBounds, out lidBounds);", "bodyBounds.Encapsulate(lidBounds);",
                            "Vector3 receiverOffset = new Vector3(0, 1.35f * Rules.Scale, 0);",
                            "bodyCollider.center = bodyBounds.center - receiverOffset; bodyCollider.size = bodyBounds.size;",
                            "receiverBody.position = visual.Facing.TransformPoint(receiverOffset);", "receiverBody.rotation = visual.Facing.rotation;")
    check("AddComponent" not in sync_geometry and "Physics.SyncTransforms" not in sync_geometry,
          encounter + ": 动画同步不得重建碰撞体或逐帧全局同步物理场景")
    encounter_tick = body(encounter, "internal void Tick(")
    ordered(encounter_tick, encounter + ": 同帧动画后必须发布受击几何",
            "visual.Tick(dt, player.transform.position, fighting, health != null ? health.HealthValue / Rules.MaximumHealth : 1, warningLeft, attack);",
            "SyncReceiverGeometry();", "if (!fighting) return;")
    feed = body(encounter, "private void Feed(")
    ordered(feed, encounter + ": 成长必须先落实模型再装配受击体并进入第三餐战斗",
            "visual.Grow(Rules);", "CreateReceiver();", "if (Rules.Meals == GreedyMimicRules.MaximumMeals) BeginCombat();")
    require(encounter, "private void RemoveReceiver()", "health.OnHurtEvent -= OnHurt;",
            "health.OnDeadEvent -= OnDead;", "receiverRoot.SetActive(false);", "UnityEngine.Object.Destroy(receiverRoot);")
    require(encounter, "public void Dispose()", "if (disposed) return;", "disposed = true;", "RemoveReceiver();",
            "if (visual != null) visual.Dispose();")
    require(mimic, "private void Cleanup()", "if (encounter != null) encounter.Dispose();", "encounter = null;")
    require(mimic, "public override void OnDestroy()", "Cleanup();")
    settle = body(encounter, "private void SettleVictory()")
    # 宝箱会扑跳移动，奖励落在战败时的实际位置；扑跳中被击杀先落回落点再取位置。
    ordered(settle, encounter + ": 奖励之前封口", "visual.Root.transform.position = leapTo;", "Vector3 center = Center;",
            "settled = true;", "RemoveReceiver();",
            "GreedyMimicInventory.Reward(EconomyManager.CashItemID, 600 + Rules.Meals * 400, center);")

    # 官方在 Awake / Start 启用交互 collider，组员必须随后禁用，避免抢占主入口。
    for relative, hide in (("Mimic/GreedyMimicInteractable.cs", "HideGroupCollider"),
                           ("Delivery/OddParcelInteractable.cs", "HideOwnGroupMarker"),
                           ("Prototypes/DangerousPrototypeInteractable.cs", "HideGroupCollider")):
        path = PREFIX + relative
        for phase in ("Awake", "Start"):
            chain = body(path, "protected override void " + phase + "()")
            ordered(chain, path + ": 组员装配", "base." + phase + "();", hide + "();")
        require(path, "private void " + hide + "()", "MarkerActive = false;",
                "if (interactCollider != null) interactCollider.enabled = false;")

    tick = require(delivery, "private void Tick(float deltaTime)",
                   "if (!ReferenceEquals(level, raidOwner)) { ClearWorld(); raidOwner = level; acceptedOrders = 0; }",
                   "if (!FieldAdventureSupport.TryGetRaid(owner, out currentPlayer, out scene)) { if (worldRoot != null || cancellation != null) ClearWorld(); return; }",
                   "if (BossRushUI.IsGamePaused() || deltaTime <= 0f) return;")
    ordered(tick, delivery + ": 暂停冻结包裹", "if (BossRushUI.IsGamePaused() || deltaTime <= 0f) return;",
            "parcelFx.Tick(deltaTime, condition.Pressure, carried);", "bool burst = condition.Step(sampleElapsed, speed, carried);")
    for signature in ("private void ClearWorld()", "private void ClearOrder()"):
        check(not re.search(r"\bacceptedOrders\s*(?:=|\+\+|--|\+=|-=)", body(delivery, signature)),
              delivery + ": 子区域清理不得重置整趟额度 " + signature)
    courier_sync = require(delivery, "private void SyncCourierOption(",
                           "bool available = !clearing && CanInteract(OddParcelAction.Offer);",
                           "InteractableBase selected = current ? action.InteractTarget : null;",
                           "if (available) courierGroup.Add(courierOption);",
                           "else courierGroup.Remove(courierOption);",
                           "courierMaster.GetInteractableList().IndexOf(selected)",
                           "action.SwitchInteractable(index - action.InteractIndexInGroup);",
                           "courierOption.RefreshVisibleGroup(courierMaster, selected);")
    ordered(courier_sync, delivery + ": 变更成员前保留选中对象",
            "InteractableBase selected = current ? action.InteractTarget : null;",
            "if (available) courierGroup.Add(courierOption);",
            "courierOption.RefreshVisibleGroup(courierMaster, selected);")
    clear_world = require(delivery, "private void ClearWorld()", "generation++;", "cancellation.Cancel();",
                          "SyncCourierOption(true);", "ClearOrder();", "UnityEngine.Object.Destroy(worldRoot);")
    ordered(clear_world, delivery + ": 退组并清引用后才能结束当前订单，避免重新挂入",
            "SyncCourierOption(true);", "UnityEngine.Object.Destroy(courierOption.gameObject);",
            "courierOption = null; courierGroup = null; courierMaster = null;", "ClearOrder();")
    require(delivery, "public override void OnDestroy()", "ClearWorld();")
    start = body(delivery, "private void StartOrder(")
    ordered(start, delivery + ": 接单成功才消耗额度", "condition = new OddParcelCondition(kind);",
            "parcel.SetActive(true);", "destination.SetActive(true);",
            'SetupMapPoint(destination, "BossRush_OddParcel_ReceiptMap");', "acceptedOrders++;")
    require(delivery, "private GameObject NewAnchor(",
            "go.SetActive(false);", "collider.center = new Vector3(0f, .35f, 0f);", "collider.radius = .95f;")
    pay = body(PREFIX + "Delivery/OddParcelRules.cs", "internal bool TryPay(")
    ordered(pay, "OddParcelCondition: 收款重入与拒绝", "if (Paid || paying || Ruined || addMoney == null) return false;",
            "paying = true;", "if (!addMoney(Reward)) return false;", "Paid = true;", "finally { paying = false; }")
    require(delivery, "private static bool TryGrantCash(", "return Duckov.Economy.EconomyManager.Add(amount);",
            "bool credited = Duckov.Economy.EconomyManager.Money >= before + amount;", "return credited;")

    require(prototype, "public override void OnAwake(", "if (subscribed) return;", "subscribed = true;",
            "SceneLoader.onStartedLoadingScene += OnLoading;", "LevelManager.OnMainCharacterDead += OnDead;")
    require(prototype, "public override void OnDestroy()", "SceneLoader.onStartedLoadingScene -= OnLoading;",
            "LevelManager.OnMainCharacterDead -= OnDead;", "CleanupRaid();")
    for signature in ("private void OnLoading(", "private void OnDead("):
        require(prototype, signature, "CleanupRaid();")
    require(prototype, "private void CleanupRaid()", "generation++;", "dialogueCancellation.Cancel();",
            "EndLease(true);", "ClearEntries();")
    require(prototype, "private void ClearEntries()", "entry.Runtime = null;", "list.Remove(entry);",
            "UnityEngine.Object.Destroy(entry.gameObject);", "station.SetActive(false);")
    require(prototype, "private void BeginLease(", "if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene)) return;",
            "if (!rules.Borrow(kind)) return;", "borrowedItem = gun.Item;", "if (!leaseSubscribed)",
            "CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;", "ItemAgent_Gun.OnMainCharacterShootEvent += OnShot;")
    require(prototype, "private void OnHoldChanged(", "if (player != borrower) return;",
            "borrowedGun = heldGun != null && heldGun.Item == borrowedItem ? heldGun : null;", "RefreshVisuals();")
    require(prototype, "private bool IsHeld()",
            "return rules.Active && borrower != null && borrowedGun != null && borrower.CurrentHoldItemAgent == borrowedGun && borrowedGun.Holder == borrower;")
    require(prototype, "private void RefreshVisuals()",
            "if (IsHeld()) visuals = new DangerousPrototypeVisuals(borrowedGun.muzzle, borrower, rules.Kind);")
    require(prototype, "private void EndLease(", "CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;",
            "ItemAgent_Gun.OnMainCharacterShootEvent -= OnShot;", "borrowedItem = null;", "if (cancel) rules.Cancel();")
    require(prototype, "private void OnShot(",
            "if (gun != borrowedGun || !IsHeld() || BossRushUI.IsGamePaused() || !FieldAdventureSupport.TryGetRaid(owner, out player, out scene) || player != borrower || !rules.TryFire()) return;")
    claim = require(prototype, "private void TryClaim(",
                    "if (!rules.Completed || delivering) return;", "delivering = true;",
                    'if (ItemAssetsCollection.GetPrefab(ColdQuenchFluidConfig.TYPE_ID) == null) throw new InvalidOperationException("Cold Quench Fluid prefab is unavailable");',
                    "if (delivered) { rules.Claim();", "if (!delivered) SkyIslandInventoryTransaction.DestroyUnowned(reward);",
                    "delivering = false;")
    ordered(claim, prototype + ": 合堆后入空槽、确认实际归属、最后落地",
            "delivered = SkyIslandInventoryTransaction.TryDeliver(reward, inventory, remaining =>",
            "inventory.AddItem(remaining);",
            "if (SkyIslandInventoryTransaction.HasOwner(remaining) || inventory.Content.Contains(remaining)) return true;",
            "remaining.Drop(position + Vector3.up * 0.2f, true, Vector3.forward, 0f);",
            "return remaining != null && remaining.ActiveAgent != null;", "rules.Claim();")
    prototype_update = body(prototype, "public override void OnUpdate(")
    ordered(prototype_update, prototype + ": 暂停冻结试验与特效",
            "if (owner == null || SceneLoader.IsSceneLoading || BossRushUI.IsGamePaused()) return;",
            "if (rules.Tick(deltaTime, held))")
    require(PREFIX + "Prototypes/DangerousPrototypeVisuals.cs", "internal void Beam(",
            "int segments = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 1, 16);", "for (int i = 0; i < segments; i++)")
    require(PREFIX + "Prototypes/DangerousPrototypeVisuals.cs", "private void Stroke(",
            "if (flashes.Count >= 112) return;", "ModEntityFogVisibility.Attach(go);")
    return checks


if __name__ == "__main__":
    try:
        count = validate()
        print("[PASS] FieldAdventureIntegrationGuard: " + str(count) + " 项结构检查")
    except (AssertionError, OSError, ValueError) as error:
        print("[FAIL] FieldAdventureIntegrationGuard: " + str(error))
        sys.exit(1)
