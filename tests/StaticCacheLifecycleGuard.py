"""
Guard: 静态缓存生命周期检查
检查所有声明了 private static readonly Dictionary<,> 或 private static ... cached... 的类，
必须存在 ResetStaticCaches 方法且在某个 OnDestroy 路径上被调用。

白名单中的类以「已登记待办」形式暂不合规但输出警告。

扫描排除目录：Build/、.codex_tmp/、tests/、.git/、.kiro/
"""

from pathlib import Path
import re
import sys

# ============================================================================
# 配置
# ============================================================================

EXCLUDE_DIRS = {"Build", ".codex_tmp", "tests", ".git", ".kiro"}
ALLOWLIST_FILE = Path(__file__).parent / "static_cache_allowlist.txt"

# ============================================================================
# 匹配模式
# ============================================================================

# 模式1: private static (readonly)? Dictionary<...>
PATTERN_STATIC_DICT = re.compile(
    r"private\s+static\s+(?:readonly\s+)?Dictionary\s*<"
)

# 模式2: private static 字段名含 cache/cached（排除 bool 和 WaitForSeconds）
PATTERN_STATIC_CACHE_FIELD = re.compile(
    r"private\s+static\s+(?:readonly\s+)?(?!bool\b)(?!void\b)\w+[^(;\n]*\bcache[ds]?\b",
    re.IGNORECASE
)

# ResetStaticCaches 方法声明
PATTERN_RESET_DECL = re.compile(
    r"\bstatic\s+void\s+(Reset\w*StaticCaches)\s*\("
)

# 类声明（简化版：只提取类名）
PATTERN_CLASS_DECL = re.compile(
    r"(?:public|internal|private|protected)\s+"
    r"(?:sealed\s+|abstract\s+)*"
    r"(?:static\s+)?"
    r"(?:partial\s+)?"
    r"(?:class|struct)\s+(\w+)"
)

# Xxx.ResetYyyStaticCaches() 调用
PATTERN_RESET_CALL = re.compile(
    r"(\w+)\s*\.\s*(Reset\w*StaticCaches)\s*\("
)


# ============================================================================
# 工具函数
# ============================================================================

def load_allowlist() -> set:
    """加载白名单"""
    if not ALLOWLIST_FILE.exists():
        return set()
    names = set()
    for line in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            names.add(line)
    return names


def should_exclude(path: Path, repo_root: Path) -> bool:
    """判断文件是否在排除目录中"""
    try:
        rel = path.relative_to(repo_root)
    except ValueError:
        return True
    for part in rel.parts:
        if part in EXCLUDE_DIRS:
            return True
    return False


def is_cache_line(line: str) -> bool:
    """判断一行是否声明了静态缓存字段"""
    stripped = line.strip()
    if stripped.startswith("//") or stripped.startswith("///") or stripped.startswith("*"):
        return False
    if PATTERN_STATIC_DICT.search(line):
        return True
    if PATTERN_STATIC_CACHE_FIELD.search(line):
        if "WaitForSeconds" in line:
            return False
        if re.search(r"private\s+static\s+bool\s+", line):
            return False
        return True
    return False


def find_reset_calls_on_ondestroy_path(all_files: dict) -> set:
    """
    找到所有在 OnDestroy 路径上被调用 ResetStaticCaches 的类名。
    
    策略：全局搜索所有 Xxx.ResetYyyStaticCaches() 调用，
    检查调用所在的方法名是否匹配 OnDestroy 路径约定。
    """
    result = set()
    
    # OnDestroy 路径方法名模式
    ONDESTROY_METHOD_PATTERN = re.compile(
        r"^(?:OnDestroy|OnDestroy_\w+|Cleanup\w*OnDestroy\w*|"
        r"Reset\w*StaticCaches|CleanupIntegrationRuntime\w*|"
        r"CleanupAlwaysOnRuntime\w*|CleanupModeRuntime\w*|"
        r"CleanupGameplayRuntime\w*)$"
    )
    
    # 方法声明模式（必须有访问修饰符或 override/static 等关键字）
    METHOD_DECL_PATTERN = re.compile(
        r"(?:public|private|internal|protected|override|static|void)\s+"
        r"(?:static\s+|override\s+|virtual\s+|sealed\s+|async\s+)*"
        r"(?:void\s+|[\w<>\[\],]+\s+)?(\w+)\s*\("
    )
    
    for filepath, text in all_files.items():
        # 找到所有 ResetStaticCaches 调用
        for call_match in PATTERN_RESET_CALL.finditer(text):
            called_class = call_match.group(1)
            call_pos = call_match.start()
            
            # 确定调用所在的方法：向前搜索最近的方法声明
            search_start = max(0, call_pos - 5000)
            preceding = text[search_start:call_pos]
            
            method_sigs = list(METHOD_DECL_PATTERN.finditer(preceding))
            
            if not method_sigs:
                continue
            
            containing_method = method_sigs[-1].group(1)
            
            # 检查方法名是否在 OnDestroy 路径上
            if ONDESTROY_METHOD_PATTERN.match(containing_method):
                result.add(called_class)
    
    return result


def analyze_file(filepath: Path, text: str) -> list:
    """
    分析单个文件，返回检测到的类信息列表。
    每项: (logical_name, actual_name, has_cache, has_reset)
    
    使用简化策略：按类声明分段，不做完整的大括号追踪。
    对于每个类声明，检查从该声明到下一个类声明（或文件末尾）之间的内容。
    """
    results = []
    lines = text.split("\n")
    
    # 找到所有类声明的位置
    class_positions = []  # (line_idx, actual_name, is_partial_mb)
    for i, line in enumerate(lines):
        match = PATTERN_CLASS_DECL.search(line)
        if match:
            actual_name = match.group(1)
            is_partial_mb = ("partial" in line and actual_name == "ModBehaviour")
            class_positions.append((i, actual_name, is_partial_mb))
    
    # 对每个类，检查其范围内的内容
    for idx, (start_line, actual_name, is_partial_mb) in enumerate(class_positions):
        # 确定范围：到下一个同级类声明或文件末尾
        if idx + 1 < len(class_positions):
            end_line = class_positions[idx + 1][0]
        else:
            end_line = len(lines)
        
        logical_name = filepath.stem if is_partial_mb else actual_name
        
        # 检查范围内是否有缓存字段
        has_cache = False
        has_reset = False
        
        for line in lines[start_line:end_line]:
            if is_cache_line(line):
                has_cache = True
            if PATTERN_RESET_DECL.search(line):
                has_reset = True
        
        if has_cache or has_reset:
            results.append((logical_name, actual_name, has_cache, has_reset))
    
    return results


# ============================================================================
# 主逻辑
# ============================================================================

def main() -> int:
    print("StaticCacheLifecycleGuard: 开始检查静态缓存生命周期...")
    
    repo_root = Path(__file__).parent.parent
    allowlist = load_allowlist()
    
    # 收集所有 .cs 文件
    all_files = {}
    for cs_file in repo_root.rglob("*.cs"):
        if should_exclude(cs_file, repo_root):
            continue
        try:
            text = cs_file.read_text(encoding="utf-8")
            all_files[cs_file] = text
        except Exception:
            continue
    
    # 构建 OnDestroy 调用链中被调用 ResetStaticCaches 的类名集合
    ondestroy_called = find_reset_calls_on_ondestroy_path(all_files)
    
    # 检查每个文件中的类
    results = []
    seen = set()
    
    for filepath, text in all_files.items():
        file_classes = analyze_file(filepath, text)
        
        for logical_name, actual_name, has_cache, has_reset in file_classes:
            if logical_name in seen:
                continue
            seen.add(logical_name)
            
            # 判断是否在 OnDestroy 路径上被调用
            in_ondestroy = False
            if has_reset:
                if actual_name in ondestroy_called:
                    in_ondestroy = True
                elif logical_name in ondestroy_called:
                    in_ondestroy = True
                elif actual_name == "ModBehaviour" and "ModBehaviour" in ondestroy_called:
                    in_ondestroy = True
            
            # 判定
            if has_reset and in_ondestroy:
                results.append(("PASS", logical_name, filepath,
                                "已有 ResetStaticCaches 且在 OnDestroy 路径上被调用"))
            elif logical_name in allowlist or actual_name in allowlist:
                results.append(("WARN", logical_name, filepath, "已登记白名单待办"))
            elif has_cache and not has_reset:
                results.append(("FAIL", logical_name, filepath, "缺少 ResetStaticCaches 方法"))
            elif has_reset and not in_ondestroy:
                results.append(("FAIL", logical_name, filepath,
                                "有 ResetStaticCaches 但未在 OnDestroy 路径上被调用"))
    
    # 输出结果
    pass_count = 0
    warn_count = 0
    fail_count = 0
    
    passes = [r for r in results if r[0] == "PASS"]
    warns = [r for r in results if r[0] == "WARN"]
    fails = [r for r in results if r[0] == "FAIL"]
    
    if passes:
        print("\n  === 合规类 ===")
        for status, name, filepath, msg in passes:
            rel_path = filepath.relative_to(repo_root)
            print(f"  [PASS] {name} ({rel_path}): {msg}")
            pass_count += 1
    
    if warns:
        print("\n  === 白名单待办（警告） ===")
        for status, name, filepath, msg in warns:
            rel_path = filepath.relative_to(repo_root)
            print(f"  [WARN] {name} ({rel_path}): {msg}")
            warn_count += 1
    
    if fails:
        print("\n  === 不合规类 ===")
        for status, name, filepath, msg in fails:
            rel_path = filepath.relative_to(repo_root)
            print(f"  [FAIL] {name} ({rel_path}): {msg}")
            fail_count += 1
    
    # 汇总
    print(f"\n  汇总: PASS={pass_count}, WARN={warn_count}, FAIL={fail_count}")
    
    # 验证三个目标类必须 PASS
    target_classes = {"WishFountainRewardAnimationView", "ModeEMerchant", "CourierPaidLootSweepService"}
    target_results = {}
    for status, name, filepath, msg in results:
        if name in target_classes:
            target_results[name] = status
    
    # 对于未被检测到的目标类，额外检查：
    # 如果类名在 ondestroy_called 集合中，说明它有 ResetStaticCaches 且在 OnDestroy 路径上被调用
    for target in target_classes:
        if target not in target_results:
            if target in ondestroy_called:
                target_results[target] = "PASS"
    
    print("\n  === 目标类验证 ===")
    target_all_pass = True
    for target in sorted(target_classes):
        status = target_results.get(target, "未检测")
        if status == "PASS":
            print(f"  [PASS] {target}")
        elif status == "未检测":
            print(f"  [INFO] {target}: 未被检测为含静态缓存字段（跳过）")
        else:
            print(f"  [FAIL] {target}: 状态为 {status}（期望 PASS）")
            target_all_pass = False
    
    if fail_count > 0:
        print(f"\nStaticCacheLifecycleGuard: FAIL（{fail_count} 个类不合规且不在白名单中）")
        return 1
    elif not target_all_pass:
        print("\nStaticCacheLifecycleGuard: FAIL（目标类未全部通过）")
        return 1
    else:
        print("\nStaticCacheLifecycleGuard: PASS（所有类合规或已登记白名单）")
        return 0


if __name__ == "__main__":
    sys.exit(main())
