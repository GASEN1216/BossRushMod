#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
export_spawn_points.py - 从 ModBehaviour.cs 硬编码表反向导出地图 JSON 文件

功能：
  1. 解析 ModBehaviour.cs 中所有 Vector3[] 静态数组定义
  2. 解析 BossRushMapConfigs 数组中每个 BossRushMapConfig 构造调用
  3. 将每张地图的配置导出为 Assets/SpawnPoints/{sceneName}.json

使用方式：
  python tools/export_spawn_points.py

输出目录：
  Assets/SpawnPoints/
"""

import os
import re
import json
import sys

# 项目根目录（脚本所在目录的上一级）
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
MOD_BEHAVIOUR_PATH = os.path.join(PROJECT_ROOT, "ModBehaviour.cs")
OUTPUT_DIR = os.path.join(PROJECT_ROOT, "Assets", "SpawnPoints")


def parse_vector3_arrays(source: str) -> dict:
    """
    解析所有 private static readonly Vector3[] XxxName = new Vector3[] { ... }; 定义
    返回 { 数组名: [Vector3元组列表] }
    """
    arrays = {}
    
    # 匹配数组声明：private static readonly Vector3[] Name = new Vector3[]
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
            vectors.append((x, y, z))
        
        arrays[array_name] = vectors
    
    return arrays


def parse_map_configs(source: str, arrays: dict) -> list:
    """
    解析 BossRushMapConfigs 数组中的每个 BossRushMapConfig 构造调用
    返回地图配置字典列表
    """
    # 找到 BossRushMapConfigs 数组定义
    configs_pattern = r'private\s+static\s+readonly\s+BossRushMapConfig\[\]\s+BossRushMapConfigs\s*=\s*new\s+BossRushMapConfig\[\]\s*\{'
    configs_match = re.search(configs_pattern, source)
    if not configs_match:
        print("[ERROR] 未找到 BossRushMapConfigs 数组定义")
        sys.exit(1)
    
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
    # 分割参数（需要处理嵌套括号和字符串中的逗号）
    args = split_args(call_body)
    
    if len(args) < 5:
        return None
    
    # 参数1: sceneName (string)
    scene_name = extract_string(args[0])
    # 参数2: sceneID (string)
    scene_id = extract_string(args[1])
    # 参数3: displayNameCN (string)
    display_cn = extract_string(args[2])
    # 参数4: displayNameEN (string)
    display_en = extract_string(args[3])
    # 参数5: spawnPoints (Vector3[] 引用名)
    spawn_points_ref = extract_identifier(args[4])
    spawn_points = arrays.get(spawn_points_ref, [])
    
    # 参数6: customSpawnPos (Vector3? 或 null)
    custom_spawn_pos = extract_nullable_vector3(args[5]) if len(args) > 5 else None
    # 参数7: defaultSignPos (Vector3? 或 null)
    default_sign_pos = extract_nullable_vector3(args[6]) if len(args) > 6 else None
    # 参数8: beaconIndex (int)
    beacon_index = extract_int(args[7]) if len(args) > 7 else 0
    # 参数9: previewImageName (string 或 null)
    preview_image = extract_nullable_string(args[8]) if len(args) > 8 else None
    # 参数10: mapNorth (Vector3? 或 null)
    map_north = extract_nullable_vector3(args[9]) if len(args) > 9 else None
    # 参数11: modeESpawnPoints (Vector3[] 引用名 或 null)
    mode_e_spawns_ref = extract_identifier(args[10]) if len(args) > 10 else None
    mode_e_spawn_points = arrays.get(mode_e_spawns_ref, None) if mode_e_spawns_ref else None
    # 参数12: modeEPlayerSpawnPos (Vector3? 或 null)
    mode_e_player_pos = extract_nullable_vector3(args[11]) if len(args) > 11 else None
    
    # 默认 mapNorth（与 BossRushMapConfig 构造函数一致）
    if map_north is None:
        map_north = (-0.959, 0.0, 0.284)
    
    return {
        "sceneName": scene_name,
        "sceneID": scene_id,
        "displayNameCN": display_cn,
        "displayNameEN": display_en,
        "spawnPoints": [list(v) for v in spawn_points],
        "customSpawnPos": list(custom_spawn_pos) if custom_spawn_pos else None,
        "defaultSignPos": list(default_sign_pos) if default_sign_pos else None,
        "beaconIndex": beacon_index,
        "previewImageName": preview_image,
        "mapNorth": list(map_north),
        "modeESpawnPoints": [list(v) for v in mode_e_spawn_points] if mode_e_spawn_points else None,
        "modeEPlayerSpawnPos": list(mode_e_player_pos) if mode_e_player_pos else None,
    }


def split_args(text: str) -> list:
    """
    按顶层逗号分割参数（跳过括号和字符串内的逗号）
    """
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
        
        if ch == '(' or ch == '[' or ch == '{':
            depth += 1
            current.append(ch)
        elif ch == ')' or ch == ']' or ch == '}':
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
    """从参数文本中提取字符串值（去除注释和引号）"""
    # 移除行注释
    text = re.sub(r'//[^\n]*', '', text).strip()
    match = re.search(r'"([^"]*)"', text)
    return match.group(1) if match else ""


def extract_nullable_string(text: str) -> str:
    """提取可空字符串（null 返回 None）"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower() and '"' not in text:
        return None
    match = re.search(r'"([^"]*)"', text)
    return match.group(1) if match else None


def extract_identifier(text: str) -> str:
    """从参数文本中提取标识符（变量名引用）"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower():
        return None
    # 匹配标识符
    match = re.search(r'([A-Za-z_]\w*)', text)
    return match.group(1) if match else None


def extract_nullable_vector3(text: str) -> tuple:
    """提取可空 Vector3 值"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    if 'null' in text.lower() and 'Vector3' not in text:
        return None
    match = re.search(r'new\s+Vector3\(\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\)', text)
    if match:
        return (float(match.group(1)), float(match.group(2)), float(match.group(3)))
    return None


def extract_int(text: str) -> int:
    """提取整数值"""
    text = re.sub(r'//[^\n]*', '', text).strip()
    match = re.search(r'(-?\d+)', text)
    return int(match.group(1)) if match else 0


def float_repr(value: float) -> str:
    """
    使用足够精度的浮点格式（等效于 C# float.ToString("R")）
    对于 float 类型，"R" 格式最多使用 9 位有效数字
    """
    # 使用 repr 获取足够精度，然后清理
    s = repr(value)
    # Python repr 对 float 已经保证往返精度
    return s


class FloatEncoder(json.JSONEncoder):
    """自定义 JSON 编码器，确保浮点数使用足够精度"""
    
    def encode(self, o):
        if isinstance(o, float):
            return float_repr(o)
        return super().encode(o)
    
    def iterencode(self, o, _one_shot=False):
        """重写以处理嵌套浮点数"""
        if isinstance(o, float):
            yield float_repr(o)
        elif isinstance(o, dict):
            yield '{\n'
            first = True
            for key, value in o.items():
                if not first:
                    yield ',\n'
                first = False
                yield '  ' + json.dumps(key) + ': '
                yield from self._encode_value(value, indent=2)
            yield '\n}'
        elif isinstance(o, list):
            yield from self._encode_list(o, indent=0)
        else:
            yield json.dumps(o, ensure_ascii=False)
    
    def _encode_value(self, value, indent=0):
        """编码单个值"""
        if value is None:
            yield 'null'
        elif isinstance(value, bool):
            yield 'true' if value else 'false'
        elif isinstance(value, int):
            yield str(value)
        elif isinstance(value, float):
            yield float_repr(value)
        elif isinstance(value, str):
            yield json.dumps(value, ensure_ascii=False)
        elif isinstance(value, list):
            yield from self._encode_list(value, indent)
        elif isinstance(value, dict):
            yield json.dumps(value, ensure_ascii=False)
        else:
            yield json.dumps(value, ensure_ascii=False)
    
    def _encode_list(self, lst, indent=0):
        """编码列表（支持 Vector3 数组的紧凑格式）"""
        if not lst:
            yield '[]'
            return
        
        # 检查是否是 Vector3 数组（列表的列表，内层长度为3）
        if isinstance(lst[0], list) and len(lst[0]) == 3:
            # Vector3 数组：每个元素一行
            yield '[\n'
            for i, vec in enumerate(lst):
                prefix = '    '
                suffix = ',\n' if i < len(lst) - 1 else '\n'
                yield prefix + '[' + ', '.join(float_repr(v) for v in vec) + ']' + suffix
            yield '  ]'
        elif isinstance(lst[0], (int, float)):
            # 单个 Vector3：紧凑格式
            yield '[' + ', '.join(float_repr(v) if isinstance(v, float) else str(v) for v in lst) + ']'
        else:
            yield json.dumps(lst, ensure_ascii=False)


def serialize_map_config(config: dict) -> str:
    """
    将地图配置序列化为格式化的 JSON 字符串
    使用足够精度的浮点格式（等效于 C# float.ToString("R")）
    """
    lines = []
    lines.append('{')
    
    # sceneName
    lines.append(f'  "sceneName": {json.dumps(config["sceneName"], ensure_ascii=False)},')
    # sceneID
    lines.append(f'  "sceneID": {json.dumps(config["sceneID"], ensure_ascii=False)},')
    # displayNameCN
    lines.append(f'  "displayNameCN": {json.dumps(config["displayNameCN"], ensure_ascii=False)},')
    # displayNameEN
    lines.append(f'  "displayNameEN": {json.dumps(config["displayNameEN"], ensure_ascii=False)},')
    
    # spawnPoints
    lines.append(f'  "spawnPoints": {format_vector3_array(config["spawnPoints"])},')
    
    # customSpawnPos
    lines.append(f'  "customSpawnPos": {format_nullable_vector3(config["customSpawnPos"])},')
    
    # defaultSignPos
    lines.append(f'  "defaultSignPos": {format_nullable_vector3(config["defaultSignPos"])},')
    
    # beaconIndex
    lines.append(f'  "beaconIndex": {config["beaconIndex"]},')
    
    # previewImageName
    if config["previewImageName"] is None:
        lines.append(f'  "previewImageName": null,')
    else:
        lines.append(f'  "previewImageName": {json.dumps(config["previewImageName"], ensure_ascii=False)},')
    
    # mapNorth
    lines.append(f'  "mapNorth": {format_vector3(config["mapNorth"])},')
    
    # modeESpawnPoints
    lines.append(f'  "modeESpawnPoints": {format_vector3_array(config["modeESpawnPoints"])},')
    
    # modeEPlayerSpawnPos (最后一个字段，无逗号)
    lines.append(f'  "modeEPlayerSpawnPos": {format_nullable_vector3(config["modeEPlayerSpawnPos"])}')
    
    lines.append('}')
    return '\n'.join(lines)


def format_float(value: float) -> str:
    """
    格式化浮点数，使用足够精度保证往返一致性
    等效于 C# float.ToString("R")
    """
    # 使用 repr 保证往返精度
    s = repr(value)
    # 确保是浮点格式（有小数点）
    if '.' not in s and 'e' not in s and 'E' not in s:
        s += '.0'
    return s


def format_vector3(vec: list) -> str:
    """格式化单个 Vector3 为 [x, y, z]"""
    if vec is None:
        return 'null'
    return '[' + ', '.join(format_float(v) for v in vec) + ']'


def format_nullable_vector3(vec) -> str:
    """格式化可空 Vector3"""
    if vec is None:
        return 'null'
    return format_vector3(vec)


def format_vector3_array(arr) -> str:
    """格式化 Vector3 数组为多行格式"""
    if arr is None:
        return 'null'
    if not arr:
        return '[]'
    
    lines = ['[']
    for i, vec in enumerate(arr):
        suffix = ',' if i < len(arr) - 1 else ''
        lines.append('    ' + format_vector3(vec) + suffix)
    lines.append('  ]')
    return '\n'.join(lines)


def main():
    print(f"[INFO] 读取源文件: {MOD_BEHAVIOUR_PATH}")
    
    if not os.path.exists(MOD_BEHAVIOUR_PATH):
        print(f"[ERROR] 文件不存在: {MOD_BEHAVIOUR_PATH}")
        sys.exit(1)
    
    with open(MOD_BEHAVIOUR_PATH, 'r', encoding='utf-8') as f:
        source = f.read()
    
    print("[INFO] 解析 Vector3 数组定义...")
    arrays = parse_vector3_arrays(source)
    print(f"[INFO] 找到 {len(arrays)} 个 Vector3 数组:")
    for name, vecs in arrays.items():
        print(f"       - {name}: {len(vecs)} 个点")
    
    print("\n[INFO] 解析 BossRushMapConfigs...")
    maps = parse_map_configs(source, arrays)
    print(f"[INFO] 找到 {len(maps)} 张地图配置")
    
    # 创建输出目录
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    print(f"\n[INFO] 输出目录: {OUTPUT_DIR}")
    
    # 导出每张地图的 JSON
    for config in maps:
        scene_name = config["sceneName"]
        output_path = os.path.join(OUTPUT_DIR, f"{scene_name}.json")
        
        json_content = serialize_map_config(config)
        
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(json_content)
        
        spawn_count = len(config["spawnPoints"]) if config["spawnPoints"] else 0
        mode_e_count = len(config["modeESpawnPoints"]) if config["modeESpawnPoints"] else 0
        print(f"  [OK] {scene_name}.json (刷新点: {spawn_count}, ModeE: {mode_e_count})")
    
    print(f"\n[DONE] 成功导出 {len(maps)} 张地图配置到 {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
