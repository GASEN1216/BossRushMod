"""
MapSpawnRegistryConsistencyGuard: 校验每张地图的 JSON 与硬编码表在所有字段上完全一致

功能：
  1. 读取 Assets/SpawnPoints/*.json 中所有地图 JSON 文件
  2. 解析 ModBehaviour.cs 中硬编码的 BossRushMapConfigs 数组
  3. 对每张同时存在于 JSON 和硬编码表中的地图，比较所有字段：
     - sceneName, sceneID, displayNameCN, displayNameEN（精确字符串匹配）
     - spawnPoints, modeESpawnPoints（Vector3 数组比较，浮点精度 2 位小数）
     - customSpawnPos, defaultSignPos, modeEPlayerSpawnPos（可空 Vector3 比较）
     - mapNorth（Vector3 比较）
     - beaconIndex（精确整数匹配）
     - previewImageName（精确字符串匹配或均为 null）
  4. 任一字段不一致 → FAIL 并输出详情
  5. JSON 文件存在但硬编码表无对照 → WARNING（不阻塞）
  6. 硬编码表条目无对应 JSON 文件 → FAIL

Requirements: 4.7, 4.8, 4.11
"""

from pathlib import Path
import json
import re
import sys

# ============================================================
# 配置
# ============================================================

PROJECT_ROOT = Path(__file__).resolve().parent.parent
MOD_BEHAVIOUR_PATH = PROJECT_ROOT / "ModBehaviour.cs"
SPAWN_POINTS_DIR = PROJECT_ROOT / "Assets" / "SpawnPoints"

# 浮点比较精度（小数点后位数）
# 硬编码值使用 2 位小数（如 232.01f），允许 2 位小数精度比较
FLOAT_DECIMAL_PLACES = 2


# ============================================================
# 浮点比较工具
# ============================================================

def floats_match(a: float, b: float) -> bool:
    """
    比较两个浮点数是否在足够精度内一致
    使用 round 到 FLOAT_DECIMAL_PLACES 位小数后比较
    这样可以处理 Python/C# 浮点表示差异
    """
    return round(a, FLOAT_DECIMAL_PLACES) == round(b, FLOAT_DECIMAL_PLACES)


def vectors_match(a, b) -> bool:
    """比较两个 Vector3（列表 [x, y, z]）是否一致"""
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    if len(a) != 3 or len(b) != 3:
        return False
    return all(floats_match(a[i], b[i]) for i in range(3))


def vector_arrays_match(a, b) -> bool:
    """比较两个 Vector3 数组是否一致"""
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    if len(a) != len(b):
        return False
    return all(vectors_match(a[i], b[i]) for i in range(len(a)))


# ============================================================
# 解析硬编码表（复用 tools/export_spawn_points.py 的逻辑）
# ============================================================

def parse_vector3_arrays(source: str) -> dict:
    """
    解析所有 private static readonly Vector3[] XxxName = new Vector3[] { ... }; 定义
    返回 { 数组名: [[x,y,z], ...] }
    """
    arrays = {}

    pattern = r'private\s+static\s+readonly\s+Vector3\[\]\s+(\w+)\s*=\s*new\s+Vector3\[\]\s*\{'

    for match in re.finditer(pattern, source):
        array_name = match.group(1)
        start_pos = match.end()

        # 找到匹配的闭合大括号
        brace_depth = 1
        pos = start_pos
        while pos < len(source) and brace_depth > 0:
            if source[pos] == '{':
                brace_depth += 1
            elif source[pos] == '}':
                brace_depth -= 1
            pos += 1

        array_body = source[start_pos:pos - 1]

        # 解析所有 new Vector3(x, y, z) 调用
        vectors = []
        vec_pattern = r'new\s+Vector3\(\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\)'
        for vec_match in re.finditer(vec_pattern, array_body):
            x = float(vec_match.group(1))
            y = float(vec_match.group(2))
            z = float(vec_match.group(3))
            vectors.append([x, y, z])

        arrays[array_name] = vectors

    return arrays


def split_args(text: str) -> list:
    """按顶层逗号分割参数（跳过括号和字符串内的逗号）"""
    args = []
    depth = 0
    current = []
    in_string = False
    escape_next = False

    for ch in text:
        if escape_next:
            current.append(ch)
            escape_next = False
            continue

        if ch == '\\' and in_string:
            current.append(ch)
            escape_next = True
            continue

        if ch == '"':
            in_string = not in_string
            current.append(ch)
            continue

        if in_string:
            current.append(ch)
            continue

        if ch in ('(', '[', '{'):
            depth += 1
            current.append(ch)
        elif ch in (')', ']', '}'):
            depth -= 1
            current.append(ch)
        elif ch == ',' and depth == 0:
            args.append(''.join(current).strip())
            current = []
        else:
            current.append(ch)

    if current:
        args.append(''.join(current).strip())

    return args


def extract_string(text: str) -> str:
    """从参数文本中提取字符串值"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    match = re.search(r'"([^"]*)"', text)
    return match.group(1) if match else ""


def extract_nullable_string(text: str):
    """提取可空字符串"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower() and '"' not in text:
        return None
    match = re.search(r'"([^"]*)"', text)
    return match.group(1) if match else None


def extract_identifier(text: str):
    """从参数文本中提取标识符"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower():
        return None
    match = re.search(r'([A-Za-z_]\w*)', text)
    return match.group(1) if match else None


def extract_nullable_vector3(text: str):
    """提取可空 Vector3 值"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower() and 'Vector3' not in text:
        return None
    match = re.search(r'new\s+Vector3\(\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\)', text)
    if match:
        return [float(match.group(1)), float(match.group(2)), float(match.group(3))]
    return None


def extract_int(text: str) -> int:
    """提取整数值"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    match = re.search(r'(-?\d+)', text)
    return int(match.group(1)) if match else 0


def parse_map_configs(source: str, arrays: dict) -> list:
    """
    解析 BossRushMapConfigs 数组中的每个 BossRushMapConfig 构造调用
    返回地图配置字典列表
    """
    configs_pattern = r'private\s+static\s+readonly\s+BossRushMapConfig\[\]\s+BossRushMapConfigs\s*=\s*new\s+BossRushMapConfig\[\]\s*\{'
    configs_match = re.search(configs_pattern, source)
    if not configs_match:
        return []

    start_pos = configs_match.end()

    # 找到数组闭合
    brace_depth = 1
    pos = start_pos
    while pos < len(source) and brace_depth > 0:
        if source[pos] == '{':
            brace_depth += 1
        elif source[pos] == '}':
            brace_depth -= 1
        pos += 1

    configs_body = source[start_pos:pos - 1]

    # 解析每个 new BossRushMapConfig(...) 调用
    maps = []
    config_pattern = r'new\s+BossRushMapConfig\s*\('

    for config_match in re.finditer(config_pattern, configs_body):
        call_start = config_match.end()

        # 找到匹配的闭合括号
        paren_depth = 1
        cpos = call_start
        while cpos < len(configs_body) and paren_depth > 0:
            if configs_body[cpos] == '(':
                paren_depth += 1
            elif configs_body[cpos] == ')':
                paren_depth -= 1
            cpos += 1

        call_body = configs_body[call_start:cpos - 1]

        # 解析构造函数参数
        map_config = parse_config_call(call_body, arrays)
        if map_config:
            maps.append(map_config)

    return maps


def parse_config_call(call_body: str, arrays: dict) -> dict:
    """
    解析单个 BossRushMapConfig 构造函数调用的参数

    构造函数签名：
    BossRushMapConfig(string name, string id, string displayCN, string displayEN,
        Vector3[] spawns, Vector3? customPos = null, Vector3? signPos = null,
        int beacon = 0, string preview = null, Vector3? north = null,
        Vector3[] modeESpawns = null, Vector3? modeEPlayerPos = null)
    """
    args = split_args(call_body)

    if len(args) < 5:
        return None

    # 参数1: sceneName
    scene_name = extract_string(args[0])
    # 参数2: sceneID
    scene_id = extract_string(args[1])
    # 参数3: displayNameCN
    display_cn = extract_string(args[2])
    # 参数4: displayNameEN
    display_en = extract_string(args[3])
    # 参数5: spawnPoints (Vector3[] 引用名)
    spawn_points_ref = extract_identifier(args[4])
    spawn_points = arrays.get(spawn_points_ref, [])

    # 参数6: customSpawnPos
    custom_spawn_pos = extract_nullable_vector3(args[5]) if len(args) > 5 else None
    # 参数7: defaultSignPos
    default_sign_pos = extract_nullable_vector3(args[6]) if len(args) > 6 else None
    # 参数8: beaconIndex
    beacon_index = extract_int(args[7]) if len(args) > 7 else 0
    # 参数9: previewImageName
    preview_image = extract_nullable_string(args[8]) if len(args) > 8 else None
    # 参数10: mapNorth
    map_north = extract_nullable_vector3(args[9]) if len(args) > 9 else None
    # 参数11: modeESpawnPoints
    mode_e_spawns_ref = extract_identifier(args[10]) if len(args) > 10 else None
    mode_e_spawn_points = arrays.get(mode_e_spawns_ref, None) if mode_e_spawns_ref else None
    # 参数12: modeEPlayerSpawnPos
    mode_e_player_pos = extract_nullable_vector3(args[11]) if len(args) > 11 else None

    # 默认 mapNorth（与 BossRushMapConfig 构造函数一致）
    if map_north is None:
        map_north = [-0.959, 0.0, 0.284]

    return {
        "sceneName": scene_name,
        "sceneID": scene_id,
        "displayNameCN": display_cn,
        "displayNameEN": display_en,
        "spawnPoints": spawn_points,
        "customSpawnPos": custom_spawn_pos,
        "defaultSignPos": default_sign_pos,
        "beaconIndex": beacon_index,
        "previewImageName": preview_image,
        "mapNorth": map_north,
        "modeESpawnPoints": mode_e_spawn_points,
        "modeEPlayerSpawnPos": mode_e_player_pos,
    }


# ============================================================
# 读取 JSON 文件
# ============================================================

def load_json_configs() -> dict:
    """
    读取 Assets/SpawnPoints/*.json 中所有地图配置
    返回 { sceneName: config_dict }
    """
    configs = {}

    if not SPAWN_POINTS_DIR.exists():
        return configs

    for json_file in sorted(SPAWN_POINTS_DIR.glob("*.json")):
        try:
            text = json_file.read_text(encoding="utf-8")
            data = json.loads(text)
            scene_name = data.get("sceneName", "")
            if scene_name:
                configs[scene_name] = data
        except Exception as e:
            print(f"  [警告] JSON 解析失败: {json_file.name} - {e}")

    return configs


# ============================================================
# 字段比较
# ============================================================

def compare_configs(json_config: dict, hardcoded_config: dict, scene_name: str) -> list:
    """
    比较 JSON 配置与硬编码配置的所有字段
    返回差异列表（空列表表示完全一致）
    """
    diffs = []

    # 字符串字段：精确匹配
    string_fields = ["sceneName", "sceneID", "displayNameCN", "displayNameEN"]
    for field in string_fields:
        json_val = json_config.get(field, "")
        hard_val = hardcoded_config.get(field, "")
        if json_val != hard_val:
            diffs.append(f"    字段 {field}: JSON=\"{json_val}\" vs 硬编码=\"{hard_val}\"")

    # previewImageName：精确字符串匹配或均为 null
    json_preview = json_config.get("previewImageName")
    hard_preview = hardcoded_config.get("previewImageName")
    if json_preview != hard_preview:
        diffs.append(f"    字段 previewImageName: JSON={json_preview!r} vs 硬编码={hard_preview!r}")

    # beaconIndex：精确整数匹配
    json_beacon = json_config.get("beaconIndex", 0)
    hard_beacon = hardcoded_config.get("beaconIndex", 0)
    if json_beacon != hard_beacon:
        diffs.append(f"    字段 beaconIndex: JSON={json_beacon} vs 硬编码={hard_beacon}")

    # 可空 Vector3 字段
    nullable_vec3_fields = ["customSpawnPos", "defaultSignPos", "modeEPlayerSpawnPos"]
    for field in nullable_vec3_fields:
        json_val = json_config.get(field)
        hard_val = hardcoded_config.get(field)
        if not vectors_match(json_val, hard_val):
            diffs.append(f"    字段 {field}: JSON={json_val} vs 硬编码={hard_val}")

    # mapNorth：Vector3 比较
    json_north = json_config.get("mapNorth")
    hard_north = hardcoded_config.get("mapNorth")
    if not vectors_match(json_north, hard_north):
        diffs.append(f"    字段 mapNorth: JSON={json_north} vs 硬编码={hard_north}")

    # Vector3[] 字段
    array_fields = ["spawnPoints", "modeESpawnPoints"]
    for field in array_fields:
        json_val = json_config.get(field)
        hard_val = hardcoded_config.get(field)
        if not vector_arrays_match(json_val, hard_val):
            json_len = len(json_val) if json_val else 0
            hard_len = len(hard_val) if hard_val else 0
            if json_len != hard_len:
                diffs.append(f"    字段 {field}: 长度不一致 JSON={json_len} vs 硬编码={hard_len}")
            else:
                # 找出具体哪个点不一致
                for i in range(min(json_len, hard_len)):
                    if not vectors_match(json_val[i], hard_val[i]):
                        diffs.append(
                            f"    字段 {field}[{i}]: JSON={json_val[i]} vs 硬编码={hard_val[i]}"
                        )
                        if len(diffs) > 20:
                            diffs.append("    ... (更多差异省略)")
                            break

    return diffs


# ============================================================
# 主入口
# ============================================================

def main() -> int:
    exit_code = 0
    warnings = []

    print("MapSpawnRegistryConsistencyGuard: 开始校验 JSON 与硬编码表一致性")
    print()

    # ---- 读取 ModBehaviour.cs 源码 ----
    if not MOD_BEHAVIOUR_PATH.exists():
        print(f"  [错误] 源文件不存在: {MOD_BEHAVIOUR_PATH}")
        return 1

    source = MOD_BEHAVIOUR_PATH.read_text(encoding="utf-8")

    # ---- 解析硬编码表 ----
    print("  [步骤1] 解析硬编码 Vector3 数组...")
    arrays = parse_vector3_arrays(source)
    print(f"           找到 {len(arrays)} 个 Vector3 数组")

    print("  [步骤2] 解析 BossRushMapConfigs 硬编码表...")
    hardcoded_maps = parse_map_configs(source, arrays)
    if not hardcoded_maps:
        print("  [错误] 未找到 BossRushMapConfigs 数组定义")
        return 1
    print(f"           找到 {len(hardcoded_maps)} 张硬编码地图配置")

    # 构建硬编码表字典 { sceneName: config }
    hardcoded_dict = {m["sceneName"]: m for m in hardcoded_maps}

    # ---- 读取 JSON 文件 ----
    print("  [步骤3] 读取 Assets/SpawnPoints/*.json...")
    json_configs = load_json_configs()
    if not json_configs:
        print(f"  [错误] 未找到任何 JSON 文件: {SPAWN_POINTS_DIR}")
        return 1
    print(f"           找到 {len(json_configs)} 个 JSON 文件")
    print()

    # ---- 校验：硬编码表中每张地图必须有对应 JSON ----
    print("  [步骤4] 校验硬编码表条目是否都有对应 JSON...")
    for scene_name in hardcoded_dict:
        if scene_name not in json_configs:
            print(f"  [FAIL] 硬编码地图 \"{scene_name}\" 缺少对应 JSON 文件")
            exit_code = 1

    # ---- 校验：JSON 文件中存在但硬编码表无对照的 → WARNING ----
    print("  [步骤5] 检查 JSON 文件是否有硬编码表未覆盖的新地图...")
    for scene_name in json_configs:
        if scene_name not in hardcoded_dict:
            warnings.append(
                f"  [警告] JSON 地图 \"{scene_name}\" 在硬编码表中无对照"
                f"（硬编码表缺失对照的新地图，不阻塞本轮合并）"
            )

    # ---- 校验：同时存在的地图，逐字段比较 ----
    print("  [步骤6] 逐字段比较 JSON 与硬编码表...")
    print()

    compared_count = 0
    pass_count = 0

    for scene_name in sorted(hardcoded_dict.keys()):
        if scene_name not in json_configs:
            continue  # 已在步骤4报告

        compared_count += 1
        json_config = json_configs[scene_name]
        hard_config = hardcoded_dict[scene_name]

        diffs = compare_configs(json_config, hard_config, scene_name)

        if diffs:
            print(f"  [FAIL] 地图 \"{scene_name}\" 存在字段不一致:")
            for diff in diffs:
                print(diff)
            print()
            exit_code = 1
        else:
            pass_count += 1
            print(f"  [PASS] 地图 \"{scene_name}\" - 所有字段一致")

    print()

    # ---- 输出警告 ----
    if warnings:
        print("  --- 警告信息 ---")
        for w in warnings:
            print(w)
        print()

    # ---- 总结 ----
    print(f"  --- 校验总结 ---")
    print(f"  硬编码地图数: {len(hardcoded_dict)}")
    print(f"  JSON 文件数: {len(json_configs)}")
    print(f"  已比较地图数: {compared_count}")
    print(f"  通过: {pass_count}")
    print(f"  失败: {compared_count - pass_count}")
    if warnings:
        print(f"  警告: {len(warnings)}")
    print()

    if exit_code == 0:
        print("MapSpawnRegistryConsistencyGuard: PASS")
    else:
        print("MapSpawnRegistryConsistencyGuard: FAIL")

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
