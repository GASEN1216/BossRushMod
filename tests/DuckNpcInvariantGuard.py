"""Guard: 捏脸 NPC 工具链的 load-bearing 不变式。

这些不是风格约束，每一条都对应一次实机踩过的坑或一处静默失效。
回退任何一条都不会编译报错、不会有异常日志，只会让 NPC 悄悄坏掉。
"""

from pathlib import Path
import json
import re
import sys


ROOT = Path(__file__).resolve().parent.parent
DUCK_DIR = ROOT / "Integration" / "NPCs" / "DuckNpc"
DATA_FILE = ROOT / "Assets" / "Data" / "DuckNpcs.json"

# 合法场景名：基地枢纽四个变体（Utilities/SceneRuntimeGate.cs）
# ∪ Assets/SpawnPoints/*.json 的 sceneName 全集（文件名即场景名）。
# 比对是大小写敏感的 Ordinal（DuckNpcBlueprint.AllowsScene）。
KNOWN_SCENE_NAMES = set(
    ["Base", "Base_SceneV2", "Base_SceneV2_Sub_01",
     "Level_HiddenWarehouse_CellarUnderGround"]
    + [p.stem for p in (ROOT / "Assets" / "SpawnPoints").glob("*.json")]
)
COMPILE_LIST = ROOT / "compile_official.bat"

FACTORY = DUCK_DIR / "DuckNpcFactory.cs"
CATALOG = DUCK_DIR / "DuckNpcFaceCatalog.cs"
CODEC = DUCK_DIR / "DuckNpcFaceCodec.cs"
RANDOMIZER = DUCK_DIR / "DuckNpcFaceRandomizer.cs"
BLUEPRINT = DUCK_DIR / "DuckNpcBlueprint.cs"
REGISTRY = DUCK_DIR / "DuckNpcRegistry.cs"
MOVEMENT = DUCK_DIR / "DuckNpcMovement.cs"
MODULE = DUCK_DIR / "DuckNpcModule.cs"
MARKER = DUCK_DIR / "DuckNpcRuntimeMarker.cs"
OUTFITTER = DUCK_DIR / "DuckNpcOutfitter.cs"

PERM_DIR = DUCK_DIR / "Permanent"
PERM_REGISTRY = PERM_DIR / "PermanentDuckNpcRegistry.cs"
PERM_CONFIG = PERM_DIR / "PermanentDuckNpcAffinityConfig.cs"
PERM_INTERACT = PERM_DIR / "PermanentDuckNpcInteractable.cs"
PERM_MODULE = PERM_DIR / "PermanentDuckNpcModule.cs"

# 婚姻系统里必须存在泛化分支的 6 处。漏一处 = 结婚后 NPC 卡住/不消失，
# 且这块**原本没有任何 guard 覆盖**。
#
# 每一项是 (文件, 该站点的特征串, 站点说明)。
# 必须逐站点断言而不是"数文件里出现过几次 PermanentDuckNpcRegistry" ——
# 后者在只废掉其中一处分支时仍然会通过（本 guard 的负向测试实测漏过）。
WEDDING_SITES = [
    ("Integration/Wedding/WeddingModBehaviourBridge.cs",
     "IsPermanentDuckNpc(spouseNpcId)", "教堂点强制生成"),
    ("Integration/Wedding/WeddingModBehaviourBridge.cs",
     "PermanentDuckNpcRegistry.GetInstance(spouseNpcId)", "按 npcId 取实例"),
    ("Integration/Wedding/WeddingModBehaviourBridge.cs",
     "spouseInstance.GetComponent<DuckNpcMovement>()", "设为站桩"),
    ("Integration/Wedding/WeddingModBehaviourBridge.cs",
     "IsPermanentDuckNpc(npcId)", "配偶跟随准备"),
    ("Integration/Wedding/NPCMarriageSystem.cs",
     "IsPermanentDuckNpc(npcId)", "结婚后移走"),
    ("Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs",
     "IsPermanentDuckNpc(spouseNpcId)", "教堂被拆时清理"),
]


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT_RE = re.compile(r"//[^\n]*")


def code_only(text: str) -> str:
    """去掉注释后的代码。

    本 guard 里好几条禁令的关键词恰恰会出现在"解释为什么不这么做"的注释里
    （例如 DuckNpcMovement 的文件头详细写了为什么不用 AICharacterController）。
    对原文做正则会把这些说明本身判成违规，所以禁令类检查一律走 code_only()。
    """
    return LINE_COMMENT_RE.sub("", BLOCK_COMMENT_RE.sub("", text))


def main() -> int:
    errors = []

    for path in (FACTORY, CATALOG, CODEC, RANDOMIZER, BLUEPRINT,
                 REGISTRY, MOVEMENT, MODULE, MARKER, OUTFITTER,
                 PERM_REGISTRY, PERM_CONFIG, PERM_INTERACT, PERM_MODULE):
        if not path.exists():
            errors.append("缺少源码文件: " + str(path.relative_to(ROOT)))
    if errors:
        for e in errors:
            print("DuckNpcInvariantGuard: FAIL " + e)
        return 1

    factory = read(FACTORY)
    catalog = read(CATALOG)
    codec = read(CODEC)
    randomizer = read(RANDOMIZER)
    blueprint = read(BLUEPRINT)
    registry = read(REGISTRY)
    movement = read(MOVEMENT)
    module = read(MODULE)

    # 所有"必须存在"的检查也要走去注释文本：文件头注释里大量出现这些名字，
    # 只查原文会让"把调用删掉但注释还在"的回退溜过去（本 guard 的负向测试实测过）。
    factory_code = code_only(factory)
    codec_code = code_only(codec)
    randomizer_code = code_only(randomizer)
    registry_code = code_only(registry)
    movement_code = code_only(movement)
    module_code = code_only(module)
    blueprint_code = code_only(blueprint)

    # ------------------------------------------------------------------
    # 1. 距离休眠：SetRelatedScene 第二参必须为 false
    # ------------------------------------------------------------------
    # 传 true 会把 NPC 注册进官方 SetActiveByPlayerDistance，
    # 该组件每帧无条件 SetActive(距玩家 < 100m)，玩家跑远后 NPC 被静默关掉。
    # 见 AGENTS.md 第 14 节。
    for m in re.finditer(r"SetRelatedScene\s*\(([^)]*)\)", code_only(factory)):
        args = m.group(1)
        if "false" not in args:
            errors.append(
                "DuckNpcFactory.SetRelatedScene 第二参必须显式传 false（官方距离休眠会静默关掉 NPC）: "
                + args.strip())

    # ------------------------------------------------------------------
    # 2. 清场豁免：工厂必须挂 DuckNpcRuntimeMarker
    # ------------------------------------------------------------------
    # Mode H 竞技场隔离靠 GetComponentInChildren<INPCController>() 放行，
    # 没有 marker 的角色会被当作原生敌人销毁。
    if "AddComponent<DuckNpcRuntimeMarker>" not in factory_code:
        errors.append("DuckNpcFactory 必须挂 DuckNpcRuntimeMarker，否则 Mode H 清场会销毁 NPC")
    if "INPCController" not in code_only(read(MARKER)):
        errors.append("DuckNpcRuntimeMarker 必须实现 INPCController，清场豁免依赖它")

    # ------------------------------------------------------------------
    # 3. 几何补全：外来脸数据必须过 baseline 补 radius / heightOffset
    # ------------------------------------------------------------------
    # radius / heightOffset 不在官方捏脸 UI 里，手写 JSON 必然漏，
    # 补不上会把五官糊在头部中心。
    if not re.search(r"\bApplyBaselineGeometry\s*\(", codec_code):
        errors.append("DuckNpcFaceCodec 必须保留 ApplyBaselineGeometry 的定义与调用（否则手写脸 JSON 会畸形）")
    # 必须真的被 Normalize 调用，光有定义没人调等于没有
    if not re.search(r"Normalize\s*\([^)]*\)\s*\{[^}]*ApplyBaselineGeometry\s*\(", codec_code, re.S):
        errors.append("DuckNpcFaceCodec.Normalize 必须调用 ApplyBaselineGeometry")
    if "radius" not in codec_code or "heightOffset" not in codec_code:
        errors.append("DuckNpcFaceCodec 的几何补全必须覆盖 radius 与 heightOffset")

    # ------------------------------------------------------------------
    # 4. 部件 ID 必须来自真实枚举，不能按 totalCount 当连续区间用
    # ------------------------------------------------------------------
    # 实测 hair 的 ID 缺 5（0,1,2,3,4,6,...,18）。
    # 官方 GetPartPrefab 找不到会静默回落 parts[0]，随机结果异常偏向 0 号且不报错。
    if not re.search(r"EnumeratePartIds\s*\(", randomizer_code):
        errors.append("DuckNpcFaceRandomizer 必须走 EnumeratePartIds 取真实 ID（hair 的 ID 不连续，缺 5）")
    if re.search(r"Random\.Range\s*\(\s*0\s*,\s*\w*totalCount", randomizer_code):
        errors.append("DuckNpcFaceRandomizer 不得用 Random.Range(0, totalCount) 当部件 ID（ID 不连续）")

    # ------------------------------------------------------------------
    # 5. 蓝图表不得使用 Unity JsonUtility
    # ------------------------------------------------------------------
    # 实机 Unity 2022.3 在「int version + 对象数组」的 internal DTO 上
    # 会只填 version、静默把数组留成 null（Campaign 实测记录）。
    for name, text in (("DuckNpcBlueprint.cs", blueprint), ("DuckNpcRegistry.cs", registry)):
        if re.search(r"JsonUtility\s*\.\s*(FromJson|ToJson)", code_only(text)):
            errors.append(
                name + " 不得用 JsonUtility 解析蓝图表（实机会静默把对象数组留成 null，"
                       "见 Campaign/CampaignContentCatalog.cs:136），应走 ModeHJsonParser")
    if "ModeHJsonParser" not in blueprint_code:
        errors.append("DuckNpcBlueprint 必须走 ModeHJsonParser 解析")

    # ------------------------------------------------------------------
    # 6. 蓝图 DTO 禁字段初始化器
    # ------------------------------------------------------------------
    # 默认值一律在 ParseRow 里显式写；DTO 带初始化器会让
    # 「JSON 没写会变成什么」有两个答案。
    dto = re.search(r"internal sealed class DuckNpcBlueprint\s*\{(.*?)\n    \}",
                    blueprint, re.S)
    if not dto:
        errors.append("找不到 DuckNpcBlueprint 类体，无法校验字段初始化器")
    else:
        for m in re.finditer(r"^\s*public\s+[\w\[\]<>]+\s+\w+\s*=", dto.group(1), re.M):
            errors.append("DuckNpcBlueprint 字段禁用初始化器（默认值只在 ParseRow 里给）: "
                          + m.group(0).strip())

    # ------------------------------------------------------------------
    # 7. Registry 必须有硬编码 fallback（AGENTS.md 4.8 第 3 层）
    # ------------------------------------------------------------------
    if "CreateFallbackBlueprints" not in registry_code:
        errors.append("DuckNpcRegistry 必须保留硬编码 fallback（AGENTS.md 4.8 第 3 层）")
    if "JsonDataRegistry.TryReadDataFile" not in registry_code:
        errors.append("DuckNpcRegistry 必须走 JsonDataRegistry.TryReadDataFile（全仓唯一 JSON 读取入口）")

    # ------------------------------------------------------------------
    # 8. 移动层：A* 图缺失必须提前判掉；朝向必须每帧刷
    # ------------------------------------------------------------------
    if not re.search(r"if\s*\(\s*AstarPath\.active\s*==\s*null\s*\)", movement_code):
        errors.append("DuckNpcMovement 必须显式判 AstarPath.active == null（无 A* 图时 NPC 会静默不走）")
    if not re.search(r"SetAimPoint\s*\(", movement_code):
        errors.append(
            "DuckNpcMovement 必须每帧刷 SetAimPoint，否则官方 Movement.UpdateAiming 会把朝向"
            "锁死在生成时的固定 aimPoint 上（NPC 会横着走）")
    # 不得引入官方战斗 AI
    if "AICharacterController" in code_only(movement):
        errors.append("DuckNpcMovement 不得引入 AICharacterController（会带来整棵战斗行为树）")

    # ------------------------------------------------------------------
    # 9. 模块层：必须可被 AutoDiscover 发现
    # ------------------------------------------------------------------
    # 注册中心要求 public 无参构造。写了 private 构造会被静默跳过。
    if re.search(r"private\s+DuckNpcModule\s*\(", module_code):
        errors.append("DuckNpcModule 不得有 private 构造函数，否则 AutoDiscoverModules 会静默跳过它")
    if "INPCModule" not in module_code:
        errors.append("DuckNpcModule 必须实现 INPCModule")

    # ------------------------------------------------------------------
    # 10. 数据表与编译清单
    # ------------------------------------------------------------------
    if not DATA_FILE.exists():
        errors.append("缺少 Assets/Data/DuckNpcs.json")
    else:
        try:
            table = json.loads(read(DATA_FILE))
        except Exception as exc:
            table = None
            errors.append("DuckNpcs.json 不是合法 JSON: " + str(exc))
        if table is not None:
            if table.get("version") != 1:
                errors.append("DuckNpcs.json 的 version 必须为 1")
            npcs = table.get("npcs")
            if not isinstance(npcs, list) or not npcs:
                errors.append("DuckNpcs.json 的 npcs 必须是非空数组")
            else:
                seen = set()
                for row in npcs:
                    npc_id = row.get("id")
                    if not npc_id:
                        errors.append("DuckNpcs.json 有蓝图缺少 id")
                        continue
                    if npc_id in seen:
                        errors.append("DuckNpcs.json 蓝图 id 重复: " + npc_id)
                    seen.add(npc_id)

    # ------------------------------------------------------------------
    # 11. 永久 NPC：交互不得挂在角色根节点
    # ------------------------------------------------------------------
    # 官方 InteractableBase.Awake 会征用同 GameObject 上的第一个 Collider，
    # 并把该 GO 的 layer 强行改成 Interactable。角色根节点那个 Collider 是
    # ECM2 的移动胶囊、层是 Character(9) —— 挂根节点会静默打坏角色物理，
    # 且全程没有任何报错。必须挂到专用子物体上。
    perm_interact = read(PERM_INTERACT)
    perm_interact_code = code_only(perm_interact)
    perm_module_code = code_only(read(PERM_MODULE))

    if not re.search(r'new GameObject\(\s*"InteractRoot"\s*\)', perm_interact_code):
        errors.append(
            "PermanentDuckNpcInteractable 必须新建 InteractRoot 子物体承载交互"
            "（挂角色根节点会被官方 Awake 征用 ECM2 移动胶囊并改掉 Character 层）")
    if not re.search(r"root\.transform\.SetParent\(", perm_interact_code):
        errors.append("PermanentDuckNpcInteractable 必须把 InteractRoot 挂到角色下")
    if not re.search(r"Physics\.IgnoreCollision\(", perm_interact_code):
        errors.append(
            "PermanentDuckNpcInteractable 必须 IgnoreCollision 掉与角色自身碰撞体的接触，"
            "否则 NPC 会被自己的交互体顶住")
    # 模块层不得图省事直接把交互 AddComponent 到角色身上
    if re.search(r"npc\.gameObject\.AddComponent<PermanentDuckNpcInteractable>", perm_module_code):
        errors.append("不得把 PermanentDuckNpcInteractable 直接挂到角色根节点，必须走 Attach()")

    # 不得照抄羽织的整体压平层级（会毁掉 DamageReceiver / HeadCollider / FOW 的层）
    for name, text in (("PermanentDuckNpcModule.cs", perm_module_code),
                       ("PermanentDuckNpcInteractable.cs", perm_interact_code)):
        if "SetLayerRecursively" in text:
            errors.append(name + " 不得使用 SetLayerRecursively（会压平角色自身各部件的层）")

    # ------------------------------------------------------------------
    # 12. 永久 NPC：10 级必须打剧情标记，否则婚礼教堂永不解锁
    # ------------------------------------------------------------------
    # 教堂解锁判据是 AffinityManager.HasAnyNPCEverReachedMaxLevel()，
    # 它查的是 hasTriggeredStory10 标记而不是当前点数。
    if not re.search(r"MarkStoryTriggered\s*\(", perm_interact_code):
        errors.append(
            "PermanentDuckNpcInteractable 必须调 AffinityManager.MarkStoryTriggered，"
            "否则该 NPC 永远解锁不了婚礼教堂（教堂查的是 hasTriggeredStory10 标记）")
    if "10" not in perm_interact_code:
        errors.append("剧情里程碑必须包含 10 级")

    # ------------------------------------------------------------------
    # 13. 永久 NPC：配置不得实现 INPCShopConfig（本版服务只留接口不显示）
    # ------------------------------------------------------------------
    perm_config_code = code_only(read(PERM_CONFIG))
    if "INPCAffinityConfig" not in perm_config_code:
        errors.append("PermanentDuckNpcAffinityConfig 必须实现 INPCAffinityConfig")
    if "INPCRelationshipDialogueConfig" not in perm_config_code:
        errors.append("PermanentDuckNpcAffinityConfig 必须实现 INPCRelationshipDialogueConfig（婚后台词）")

    # ------------------------------------------------------------------
    # 14. 婚姻系统 6 处泛化分支
    # ------------------------------------------------------------------
    wedding_cache = {}
    for rel_path, marker, label in WEDDING_SITES:
        path = ROOT / rel_path
        if not path.exists():
            errors.append("婚姻系统文件不存在: " + rel_path)
            continue
        if rel_path not in wedding_cache:
            wedding_cache[rel_path] = code_only(read(path))
        if marker not in wedding_cache[rel_path]:
            errors.append(
                rel_path + " 缺少捏脸永久 NPC 的泛化分支【" + label + "】，"
                "找不到特征串 `" + marker + "`。"
                "漏了这处，结婚后 NPC 会卡住或不消失，且不会有任何报错。")

    # ------------------------------------------------------------------
    # 15. 永久 NPC 蓝图必须用字面脸数据，不能用随机种子
    # ------------------------------------------------------------------
    # 种子只在随机算法参数顺序不变时有效，改一次 DuckNpcFaceRandomizer 就会漂。
    # 永久 NPC 的长相必须跨版本一致。
    if DATA_FILE.exists():
        try:
            table_for_perm = json.loads(read(DATA_FILE))
        except Exception:
            table_for_perm = None
        if table_for_perm is not None:
            for row in table_for_perm.get("npcs", []) or []:
                if not row.get("isPermanent"):
                    continue
                npc_id = row.get("id", "(无 id)")
                if row.get("faceMode") != "json":
                    errors.append(
                        "永久 NPC " + npc_id + " 的 faceMode 必须是 json（不能用随机种子，跨版本会漂）")
                if not row.get("faceJson"):
                    errors.append("永久 NPC " + npc_id + " 缺少 faceJson")
                if not isinstance(row.get("permanent"), dict):
                    errors.append("永久 NPC " + npc_id + " 缺少 permanent 子对象")
                else:
                    if not row["permanent"].get("displayNameCn"):
                        errors.append("永久 NPC " + npc_id + " 缺少 permanent.displayNameCn")
                # scenes 为空 = 永远不生成：DuckNpcBlueprint.AllowsScene 对空数组
                # 直接 return false，DuckNpcModule.ShouldSpawnInScene 因此恒假。
                # 出厂数据曾经三条全是 []，整条捏脸 NPC 工具链一只都刷不出来，
                # 而编译、守卫、日志全都看不出异常。
                scenes = row.get("scenes")
                if not isinstance(scenes, list) or len(scenes) == 0:
                    errors.append(
                        "永久 NPC " + npc_id + " 的 scenes 为空：AllowsScene 恒 false，永远不会生成")
                else:
                    for scene_name in scenes:
                        if scene_name not in KNOWN_SCENE_NAMES:
                            errors.append(
                                "永久 NPC " + npc_id + " 的 scenes 含未知场景名 "
                                + str(scene_name) + "（比对是大小写敏感的 Ordinal）")

    compile_text = read(COMPILE_LIST)
    for path in sorted(DUCK_DIR.glob("*.cs")) + sorted(PERM_DIR.glob("*.cs")):
        rel = path.relative_to(ROOT)
        entry = str(rel).replace("/", "\\")
        if entry not in compile_text:
            errors.append("新增 .cs 未登记进 compile_official.bat: " + entry)

    if errors:
        for e in errors:
            print("DuckNpcInvariantGuard: FAIL " + e)
        return 1

    print("DuckNpcInvariantGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
