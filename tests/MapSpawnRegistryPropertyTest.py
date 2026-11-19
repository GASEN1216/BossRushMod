"""
属性测试: MapSpawnPointRegistry JSON 读取格式与 TryGet 行为验证

本脚本通过互补方式验证 MapSpawnPointRegistry 的核心属性：
1. 静态分析 C# 源码，确认读取逻辑结构正确：整份解析成树，每个字段都从树的顶层键取，不在原文本上搜
2. 在 Python 中建模读取口径（顶层键、标准 JSON 转义、缺失 / 类型不符取默认），
   用随机 JSON 排版（键序、缩进、ASCII 转义、嵌套干扰键）验证读取与排版无关（200+ 次迭代）
3. 读取实际 JSON 文件验证字段类型符合读取格式与 TryGet 行为
生产代码对实际文件的逐字段执行比对在执行回归 AuditCoreParsing（C# 链接生产 MapSpawnPointRegistry）。
2026-11-19：生产侧删除了从未被调用的导出器 / 序列化器（ExportFromHardcoded / SerializeToJson），
Property 3 从「导出格式往返」改为「读取格式」。

# Feature: architecture-extensibility-refactor, Property 3: JSON 读取格式
# Feature: architecture-extensibility-refactor, Property 4: TryGet 对已注册地图返回非 null
# Feature: architecture-extensibility-refactor, Property 5: TryGet 对未注册地图返回 null
"""

from pathlib import Path
import json
import random
import re
import string
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

# ============================================================
# 配置
# ============================================================

REGISTRY_SOURCE = Path("Common/MapConfig/MapSpawnPointRegistry.cs")
SPAWN_POINTS_DIR = Path("Assets/SpawnPoints")
ITERATIONS = 200  # 每个属性的随机迭代次数（远超最低要求的 100 次）
SEED = 42  # 固定种子保证可复现

# JSON 必须包含的字段列表
REQUIRED_FIELDS = [
    "sceneName", "sceneID", "displayNameCN", "displayNameEN",
    "spawnPoints", "beaconIndex", "mapNorth"
]

ALL_FIELDS = [
    "sceneName", "sceneID", "displayNameCN", "displayNameEN",
    "spawnPoints", "customSpawnPos", "defaultSignPos",
    "beaconIndex", "previewImageName", "mapNorth",
    "modeESpawnPoints", "modeEPlayerSpawnPos"
]


# ============================================================
# 辅助：Python 模型 - 模拟 C# 读取口径（MapSpawnPointRegistry.DeserializeFromJson）
# ============================================================

INT_MAX = 2147483647
DEFAULT_NORTH = [-0.959, 0.0, 0.284]
STRING_FIELDS = ["sceneName", "sceneID", "displayNameCN", "displayNameEN"]
NULLABLE_VECTOR_FIELDS = ["customSpawnPos", "defaultSignPos", "modeEPlayerSpawnPos",
                          "modeHStagingPos", "modeHSpectatorPos", "modeHPlayerSpawnPos", "modeHExitPos"]
VECTOR_ARRAY_FIELDS = ["spawnPoints", "modeESpawnPoints", "modeHSpawnPoints"]


def read_int(value, default):
    """BossRushJsonValue.GetInt：整数直接取；浮点四舍五入（与 C# Math.Round 同为银行家舍入）；其它类型取默认。"""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    rounded = round(value)
    return rounded if -2147483648 <= rounded <= INT_MAX else default


def read_vector(value):
    if not isinstance(value, list) or len(value) != 3:
        return None
    if any(isinstance(c, bool) or not isinstance(c, (int, float)) for c in value):
        return None
    return [float(c) for c in value]


def read_vector_array(value):
    if not isinstance(value, list) or not value:
        return None
    points = [read_vector(item) for item in value]
    return None if any(p is None for p in points) else points


def deserialize_config(json_str: str) -> dict:
    """
    模拟 C# MapSpawnPointRegistry.DeserializeFromJson：只认顶层键；字符串字段缺失 / null / 非字符串为 ""，
    previewImageName 为 None；sortOrder 缺失取 int.MaxValue、beaconIndex 缺失取 0；必填字段缺失返回 None。
    """
    data = json.loads(json_str)
    if not isinstance(data, dict):
        return None
    config = {field: data.get(field) if isinstance(data.get(field), str) else "" for field in STRING_FIELDS}
    preview = data.get("previewImageName")
    config["previewImageName"] = preview if isinstance(preview, str) else None
    config["sortOrder"] = read_int(data.get("sortOrder"), INT_MAX)
    config["beaconIndex"] = read_int(data.get("beaconIndex"), 0)
    for field in VECTOR_ARRAY_FIELDS:
        config[field] = read_vector_array(data.get(field))
    for field in NULLABLE_VECTOR_FIELDS:
        config[field] = read_vector(data.get(field))
    north = read_vector(data.get("mapNorth"))
    config["mapNorth"] = north if north is not None else list(DEFAULT_NORTH)
    if not all(config[field] for field in STRING_FIELDS) or not config["spawnPoints"]:
        return None
    return config


def write_config(config: dict, rng: random.Random) -> str:
    """把 config 写成任意合法排版的 JSON：随机键序、缩进、ASCII 转义，并在嵌套对象里放同名干扰键。"""
    items = [(key, value) for key, value in config.items() if value is not None or rng.random() < 0.5]
    rng.shuffle(items)
    if rng.random() < 0.5:
        decoy = {"sceneName": "decoy", "sortOrder": -1, "beaconIndex": 99, "previewImageName": "decoy.png"}
        items.insert(rng.randint(0, len(items)), ("meta", decoy))
    indent = rng.choice([None, 2, 4])
    return json.dumps(dict(items), ensure_ascii=rng.random() < 0.5, indent=indent)


# ============================================================
# 辅助：比较函数
# ============================================================

def floats_equal(a: float, b: float) -> bool:
    """比较两个浮点数是否按位一致（使用 repr 比较）"""
    return repr(a) == repr(b)


def vectors_equal(a, b) -> bool:
    """比较两个 Vector3（列表）是否一致"""
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    if len(a) != 3 or len(b) != 3:
        return False
    return all(floats_equal(a[i], b[i]) for i in range(3))


def vector_arrays_equal(a, b) -> bool:
    """比较两个 Vector3 数组是否一致"""
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    if len(a) != len(b):
        return False
    return all(vectors_equal(a[i], b[i]) for i in range(len(a)))


def configs_equal(original: dict, restored: dict) -> tuple:
    """
    比较两个 config 字典的所有字段是否一致
    返回 (是否一致, 差异描述)
    """
    diffs = []

    # 字符串与整数字段
    for field in STRING_FIELDS + ["previewImageName", "sortOrder", "beaconIndex"]:
        orig_val = original.get(field)
        rest_val = restored.get(field)
        if orig_val != rest_val:
            diffs.append(f"  {field}: 原始={orig_val!r}, 还原={rest_val!r}")

    # Vector3 字段
    for field in NULLABLE_VECTOR_FIELDS + ["mapNorth"]:
        if not vectors_equal(original.get(field), restored.get(field)):
            diffs.append(f"  {field}: 原始={original.get(field)}, 还原={restored.get(field)}")

    # Vector3[] 字段
    for field in VECTOR_ARRAY_FIELDS:
        if not vector_arrays_equal(original.get(field), restored.get(field)):
            orig_len = len(original.get(field) or [])
            rest_len = len(restored.get(field) or [])
            diffs.append(f"  {field}: 长度 原始={orig_len}, 还原={rest_len}")

    if diffs:
        return False, "\n".join(diffs)
    return True, ""


# ============================================================
# 辅助：随机数据生成
# ============================================================

def random_string(rng: random.Random, min_len=1, max_len=20) -> str:
    """生成随机字符串（含中文字符模拟）"""
    length = rng.randint(min_len, max_len)
    # 混合 ASCII 和一些 Unicode 字符
    chars = string.ascii_letters + string.digits + "_-"
    # 偶尔加入中文字符
    if rng.random() < 0.3:
        cn_chars = "竞技场农场镇雪山军事基地地下实验室风暴区"
        chars += cn_chars
    return "".join(rng.choice(chars) for _ in range(length))


def random_vector3(rng: random.Random) -> list:
    """生成随机 Vector3（有限浮点数）"""
    return [
        round(rng.uniform(-1000.0, 1000.0), 2),
        round(rng.uniform(-100.0, 100.0), 2),
        round(rng.uniform(-1000.0, 1000.0), 2),
    ]


def random_vector3_array(rng: random.Random, min_len=1, max_len=30) -> list:
    """生成随机 Vector3 数组"""
    length = rng.randint(min_len, max_len)
    return [random_vector3(rng) for _ in range(length)]


def random_config(rng: random.Random) -> dict:
    """生成随机的 BossRushMapConfig 实例"""
    config = {
        "sceneName": "Level_" + random_string(rng, 3, 15),
        "sceneID": "Level_" + random_string(rng, 3, 15) + "_Main",
        "displayNameCN": random_string(rng, 2, 10),
        "displayNameEN": random_string(rng, 2, 15),
        "spawnPoints": random_vector3_array(rng, 3, 25),
        "sortOrder": rng.randint(-5, 40),
        "beaconIndex": rng.randint(0, 10),
        "mapNorth": random_vector3(rng),
    }

    # 可选字段随机为 null 或有值
    config["previewImageName"] = random_string(rng, 5, 20) + ".png" if rng.random() > 0.5 else None
    for field in NULLABLE_VECTOR_FIELDS:
        config[field] = random_vector3(rng) if rng.random() > 0.4 else None
    for field in ("modeESpawnPoints", "modeHSpawnPoints"):
        config[field] = random_vector3_array(rng, 5, 50) if rng.random() > 0.3 else None

    return config


# ============================================================
# 辅助：模拟 TryGet 行为
# ============================================================

class MapSpawnPointRegistryModel:
    """
    Python 模型，模拟 C# MapSpawnPointRegistry 的 TryGet 行为
    """
    def __init__(self):
        self._configs = {}  # sceneName -> config (大小写不敏感)

    def load_json_file(self, file_path: Path) -> bool:
        """加载单个 JSON 文件"""
        try:
            text = file_path.read_text(encoding="utf-8")
            data = json.loads(text)
            scene_name = data.get("sceneName", "")
            if scene_name:
                self._configs[scene_name.lower()] = data
                return True
        except Exception:
            pass
        return False

    def try_get(self, scene_name: str):
        """模拟 TryGet：已注册返回非 null，未注册返回 null"""
        if not scene_name:
            return None
        return self._configs.get(scene_name.lower())

    @property
    def all_scene_names(self) -> set:
        """获取所有已注册的 sceneName"""
        return {cfg.get("sceneName", "") for cfg in self._configs.values()}


# ============================================================
# 静态分析验证
# ============================================================

def static_analysis_read_format(source_text: str) -> tuple:
    """
    静态分析：DeserializeFromJson 先把整份 JSON 解析成树，每个字段都从树的顶层键取；
    不再在原文本上 IndexOf 搜键（会命中嵌套键、不解 \\uXXXX）。已删的导出器 / 序列化器不得回来。
    """
    code = clean_source(source_text)
    if "DeserializeFromJson" not in code:
        return False, "未找到 DeserializeFromJson 方法"
    if "BossRushJsonParser.TryParse(json, out jsonRoot, out parseError)" not in code \
            or "jsonRoot.Kind != BossRushJsonKind.Object" not in code:
        return False, "DeserializeFromJson 必须先用共享 BossRushJsonParser 解析整份 JSON 并要求根是对象"

    required_reads = [
        'jsonRoot.GetString("sceneName", "")',
        'jsonRoot.GetString("sceneID", "")',
        'jsonRoot.GetString("displayNameCN", "")',
        'jsonRoot.GetString("displayNameEN", "")',
        'jsonRoot.GetString("previewImageName", null)',
        'jsonRoot.GetInt("sortOrder", int.MaxValue)',
        'jsonRoot.GetInt("beaconIndex", 0)',
    ]
    required_reads += ['ExtractVector3Array(jsonRoot, "%s")' % field for field in VECTOR_ARRAY_FIELDS]
    required_reads += ['ExtractNullableVector3(jsonRoot, "%s")' % field for field in NULLABLE_VECTOR_FIELDS + ["mapNorth"]]
    for token in required_reads:
        if code.count(token) != 1:
            return False, f"DeserializeFromJson 应恰好一次从解析树读取: {token}"

    for forbidden in ("json.IndexOf(", "ExtractStringValue", "ExtractNullableStringValue", "ExtractIntValue",
                      "SerializeToJson", "ExportFromHardcoded", "AppendJsonString", "AppendVector3"):
        if forbidden in code:
            return False, f"原文本搜键 / 已删除的导出代码又出现了: {forbidden}"

    # 缺少关键字段时必须拒绝注册表配置，让调用方回退硬编码表。
    # 否则只有 sceneName 的坏 JSON 会覆盖硬编码配置并返回空 spawnPoints。
    required_guards = [
        'string.IsNullOrEmpty(sceneID)',
        'string.IsNullOrEmpty(displayNameCN)',
        'string.IsNullOrEmpty(displayNameEN)',
        'spawnPoints == null || spawnPoints.Length == 0',
    ]
    for guard in required_guards:
        if guard not in code:
            return False, f"DeserializeFromJson 缺少必填字段守护: {guard}"

    # Initialize 重入时必须清空旧缓存，避免删除/修坏 JSON 后仍返回旧注册项。
    if "_configs.Clear();" not in code:
        return False, "Initialize 未清空 _configs，重入时可能保留陈旧地图配置"

    return True, "读取结构正确：整份解析成树、全部字段取顶层键，必填字段守护与重入清空在位"


def static_analysis_tryget(source_text: str) -> tuple:
    """
    静态分析：确认 TryGet 对 null/空字符串返回 null，对已注册返回非 null
    """
    # 检查 TryGet 方法存在
    if "TryGet" not in source_text:
        return False, "未找到 TryGet 方法"

    # 检查 null/空字符串守护
    if "IsNullOrEmpty" not in source_text:
        return False, "TryGet 未检查 null/空字符串参数"

    # 检查使用 TryGetValue
    if "TryGetValue" not in source_text:
        return False, "TryGet 未使用 Dictionary.TryGetValue"

    # 检查 StringComparer.OrdinalIgnoreCase
    if "OrdinalIgnoreCase" not in source_text:
        return False, "字典未使用大小写不敏感比较器"

    return True, "TryGet 方法结构正确：null 守护 + 大小写不敏感字典查询"


# ============================================================
# Property 3: JSON 序列化往返一致性
# ============================================================

def property_test_3_actual_files() -> tuple:
    """
    Property 3 - 实际文件验证：
    Assets/SpawnPoints/*.json 每个字段的类型都符合读取格式（读取口径下不会落到默认值或被拒），
    且换任意 JSON 排版重写后按读取口径读出的值不变。
    """
    if not SPAWN_POINTS_DIR.exists():
        return False, f"目录不存在: {SPAWN_POINTS_DIR}"

    json_files = list(SPAWN_POINTS_DIR.glob("*.json"))
    if not json_files:
        return False, f"目录为空: {SPAWN_POINTS_DIR}"

    rng = random.Random(SEED)
    failures = []
    for json_file in json_files:
        try:
            text = json_file.read_text(encoding="utf-8")
            data = json.loads(text)
            if not isinstance(data, dict):
                failures.append(f"  {json_file.name}: 根不是对象")
                continue
            problems = []
            for field in STRING_FIELDS:
                if not isinstance(data.get(field), str) or not data[field]:
                    problems.append(f"{field} 必须是非空字符串")
            if "previewImageName" in data and data["previewImageName"] is not None \
                    and not isinstance(data["previewImageName"], str):
                problems.append("previewImageName 必须是字符串或 null")
            for field in ("sortOrder", "beaconIndex"):
                value = data.get(field)
                if field in data and (isinstance(value, bool) or not isinstance(value, int)):
                    problems.append(f"{field} 必须是整数（浮点会被四舍五入、字符串会落到默认值）")
            if read_vector_array(data.get("spawnPoints")) is None:
                problems.append("spawnPoints 必须是非空的 [x, y, z] 数组")
            for field in VECTOR_ARRAY_FIELDS[1:]:
                if data.get(field) is not None and read_vector_array(data[field]) is None:
                    problems.append(f"{field} 必须是 null 或非空的 [x, y, z] 数组")
            for field in NULLABLE_VECTOR_FIELDS + ["mapNorth"]:
                if data.get(field) is not None and read_vector(data[field]) is None:
                    problems.append(f"{field} 必须是 null 或 [x, y, z]")
            if problems:
                failures.append(f"  {json_file.name}: " + "；".join(problems))
                continue

            config = deserialize_config(text)
            if config is None:
                failures.append(f"  {json_file.name}: 读取口径下被拒")
                continue
            restored = deserialize_config(write_config(config, rng))
            if restored is None:
                failures.append(f"  {json_file.name}: 换排版后读取口径下被拒")
                continue
            equal, diff_msg = configs_equal(config, restored)
            if not equal:
                failures.append(f"  {json_file.name}: 换排版后读出值变化\n{diff_msg}")

        except Exception as e:
            failures.append(f"  {json_file.name}: 异常 - {e}")

    if failures:
        return False, f"发现 {len(failures)} 个文件不符合读取格式:\n" + "\n".join(failures[:5])

    return True, f"{len(json_files)} 个实际 JSON 文件字段类型符合读取格式，换排版读出值不变"


def property_test_3_random(iterations: int, rng: random.Random) -> tuple:
    """
    Property 3 - 随机迭代验证：
    生成随机 BossRushMapConfig 实例，按任意排版写成 JSON（键序、缩进、ASCII 转义、嵌套同名干扰键），
    按读取口径读回，验证所有字段按位一致。

    **Validates: Requirements 4.2, 4.8**
    """
    failures = []

    for i in range(iterations):
        config = random_config(rng)
        json_str = write_config(config, rng)

        try:
            restored = deserialize_config(json_str)
        except Exception as e:
            failures.append(f"  迭代 {i}: 读取异常 - {e}")
            continue

        if restored is None:
            failures.append(f"  迭代 {i}: 读取返回 None (sceneName={config['sceneName']})")
            continue

        equal, diff_msg = configs_equal(config, restored)
        if not equal:
            failures.append(f"  迭代 {i} (sceneName={config['sceneName']}): 字段不一致\n{diff_msg}")

    if failures:
        return False, f"发现 {len(failures)} 次违反:\n" + "\n".join(failures[:5])

    return True, f"{iterations} 次随机排版迭代全部通过"


# ============================================================
# Property 4: TryGet 对已注册地图返回非 null
# ============================================================

def property_test_4() -> tuple:
    """
    Property 4: TryGet 对已注册地图返回非 null
    对每个 Assets/SpawnPoints/*.json 文件：
    - 验证 JSON 可解析且有有效 sceneName
    - 模拟 Registry 加载后，TryGet(sceneName) 返回非 null
    - 返回对象的 sceneName 字段与查询参数一致

    **Validates: Requirements 4.3**
    """
    if not SPAWN_POINTS_DIR.exists():
        return False, f"目录不存在: {SPAWN_POINTS_DIR}"

    json_files = list(SPAWN_POINTS_DIR.glob("*.json"))
    if not json_files:
        return False, f"目录为空: {SPAWN_POINTS_DIR}"

    # 构建模拟 Registry
    registry = MapSpawnPointRegistryModel()
    load_failures = []

    for json_file in json_files:
        if not registry.load_json_file(json_file):
            load_failures.append(f"  {json_file.name}: 加载失败")

    if load_failures:
        return False, "部分文件加载失败:\n" + "\n".join(load_failures)

    # 验证每个已注册的 sceneName
    failures = []
    for json_file in json_files:
        try:
            text = json_file.read_text(encoding="utf-8")
            data = json.loads(text)
            scene_name = data.get("sceneName", "")

            if not scene_name:
                failures.append(f"  {json_file.name}: sceneName 为空")
                continue

            # TryGet 应返回非 null
            result = registry.try_get(scene_name)
            if result is None:
                failures.append(f"  {json_file.name}: TryGet(\"{scene_name}\") 返回 null（应为非 null）")
                continue

            # 返回对象的 sceneName 应与查询参数一致（大小写不敏感）
            result_scene_name = result.get("sceneName", "")
            if result_scene_name.lower() != scene_name.lower():
                failures.append(
                    f"  {json_file.name}: TryGet 返回的 sceneName=\"{result_scene_name}\" "
                    f"与查询参数 \"{scene_name}\" 不一致"
                )

            # 验证必填字段存在
            for field in REQUIRED_FIELDS:
                if field not in data or (field == "sceneName" and not data[field]):
                    failures.append(f"  {json_file.name}: 缺少必填字段 \"{field}\"")

        except Exception as e:
            failures.append(f"  {json_file.name}: 异常 - {e}")

    if failures:
        return False, f"发现 {len(failures)} 个问题:\n" + "\n".join(failures[:10])

    return True, f"{len(json_files)} 个已注册地图 TryGet 全部返回非 null 且 sceneName 一致"


# ============================================================
# Property 5: TryGet 对未注册地图返回 null
# ============================================================

def property_test_5(iterations: int, rng: random.Random) -> tuple:
    """
    Property 5: TryGet 对未注册地图返回 null
    生成随机 sceneName 字符串（确保不匹配任何已有 JSON 文件），
    验证 TryGet 返回 null

    **Validates: Requirements 4.3, 4.4**
    """
    # 构建模拟 Registry
    registry = MapSpawnPointRegistryModel()

    if SPAWN_POINTS_DIR.exists():
        for json_file in SPAWN_POINTS_DIR.glob("*.json"):
            registry.load_json_file(json_file)

    existing_names = registry.all_scene_names
    existing_names_lower = {n.lower() for n in existing_names}

    failures = []

    for i in range(iterations):
        # 生成一个确保不在已注册集合中的随机 sceneName
        random_name = "NonExistent_" + random_string(rng, 5, 25) + "_" + str(i)

        # 确保不与已有名称冲突
        while random_name.lower() in existing_names_lower:
            random_name = "NonExistent_" + random_string(rng, 5, 25) + "_" + str(i)

        # TryGet 应返回 null
        result = registry.try_get(random_name)
        if result is not None:
            failures.append(
                f"  迭代 {i}: TryGet(\"{random_name}\") 返回非 null（应为 null）"
            )

    # 额外验证：空字符串和 None 也应返回 null
    if registry.try_get("") is not None:
        failures.append("  TryGet(\"\") 返回非 null（应为 null）")

    if registry.try_get(None) is not None:
        failures.append("  TryGet(None) 返回非 null（应为 null）")

    if failures:
        return False, f"发现 {len(failures)} 次违反:\n" + "\n".join(failures[:5])

    return True, f"{iterations} 次随机未注册 sceneName + 边界值全部返回 null"


# ============================================================
# 主入口
# ============================================================

def main() -> int:
    exit_code = 0
    rng = random.Random(SEED)

    # ---- 读取源码 ----
    if not REGISTRY_SOURCE.exists():
        print(f"[错误] 源文件不存在: {REGISTRY_SOURCE}")
        return 1

    source_text = REGISTRY_SOURCE.read_text(encoding="utf-8")

    # ===========================================================
    # Property 3: JSON 序列化往返一致性
    # ===========================================================
    print("# Feature: architecture-extensibility-refactor, Property 3: JSON 读取格式")
    print("# Validates: Requirements 4.2, 4.8")
    print()

    # 静态分析
    ok, msg = static_analysis_read_format(source_text)
    if ok:
        print(f"  [静态分析] PASS - {msg}")
    else:
        print(f"  [静态分析] FAIL - {msg}")
        exit_code = 1

    # 实际文件往返验证
    ok2, msg2 = property_test_3_actual_files()
    if ok2:
        print(f"  [实际文件] PASS - {msg2}")
    else:
        print(f"  [实际文件] FAIL - {msg2}")
        exit_code = 1

    # 随机迭代验证
    ok3, msg3 = property_test_3_random(ITERATIONS, rng)
    if ok3:
        print(f"  [随机迭代] PASS - {msg3}")
    else:
        print(f"  [随机迭代] FAIL - {msg3}")
        exit_code = 1

    if ok and ok2 and ok3:
        print()
        print("  Property 3 结论: PASS")
    else:
        print()
        print("  Property 3 结论: FAIL")

    print()

    # ===========================================================
    # Property 4: TryGet 对已注册地图返回非 null
    # ===========================================================
    print("# Feature: architecture-extensibility-refactor, Property 4: TryGet 对已注册地图返回非 null")
    print("# Validates: Requirements 4.3")
    print()

    # 静态分析
    ok4s, msg4s = static_analysis_tryget(source_text)
    if ok4s:
        print(f"  [静态分析] PASS - {msg4s}")
    else:
        print(f"  [静态分析] FAIL - {msg4s}")
        exit_code = 1

    # 实际文件验证
    ok4, msg4 = property_test_4()
    if ok4:
        print(f"  [实际文件] PASS - {msg4}")
    else:
        print(f"  [实际文件] FAIL - {msg4}")
        exit_code = 1

    if ok4s and ok4:
        print()
        print("  Property 4 结论: PASS")
    else:
        print()
        print("  Property 4 结论: FAIL")

    print()

    # ===========================================================
    # Property 5: TryGet 对未注册地图返回 null
    # ===========================================================
    print("# Feature: architecture-extensibility-refactor, Property 5: TryGet 对未注册地图返回 null")
    print("# Validates: Requirements 4.3, 4.4")
    print()

    # 随机迭代验证
    ok5, msg5 = property_test_5(ITERATIONS, rng)
    if ok5:
        print(f"  [随机迭代] PASS - {msg5}")
    else:
        print(f"  [随机迭代] FAIL - {msg5}")
        exit_code = 1

    if ok5:
        print()
        print("  Property 5 结论: PASS")
    else:
        print()
        print("  Property 5 结论: FAIL")

    print()

    # ===========================================================
    # 总结
    # ===========================================================
    if exit_code == 0:
        print("MapSpawnRegistryPropertyTest: 全部属性测试 PASS")
    else:
        print("MapSpawnRegistryPropertyTest: 存在属性测试 FAIL")

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
