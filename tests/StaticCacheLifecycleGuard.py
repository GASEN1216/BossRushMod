"""
Guard: 静态缓存生命周期检查
检查所有声明了 private static readonly Dictionary<,> 或 private static ... cached... 的类，
必须存在 ResetStaticCaches 方法且在某个 OnDestroy 路径上被调用。

白名单中的类以「已登记待办」形式暂不合规但输出警告。

只扫描 compile_official.bat 的正式源码清单；历史快照不构成清理证据。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from compile_list import read_compile_sources
from cs_cleanup_reachability import clean_structure_source, reachable_reset_methods

# ============================================================================
# 配置
# ============================================================================

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

# Reset/Clear static cache 方法声明
PATTERN_RESET_DECL = re.compile(
    r"\bstatic\s+void\s+((?:Reset\w*StaticCaches)|(?:Clear\w*StaticCaches?)|ForceCleanup)\s*\("
)

# 类声明（简化版：只提取类名）
PATTERN_CLASS_DECL = re.compile(
    r"(?:public|internal|private|protected)\s+"
    r"(?:sealed\s+|abstract\s+)*"
    r"(?:static\s+)?"
    r"(?:partial\s+)?"
    r"(?:class|struct)\s+(\w+)"
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


def load_production_sources(repo_root: Path) -> dict:
    """Use the shared manifest parser and fail closed on missing source inputs."""
    sources = read_compile_sources(repo_root / "compile_official.bat")
    if not sources:
        raise ValueError("official compile source list is empty")
    return {repo_root / source: (repo_root / source).read_text(encoding="utf-8-sig")
            for source in sources}


def is_method_declaration(stripped: str) -> bool:
    """
    区分「静态字段」与「返回值恰好是字典/列表的静态方法」。

    `private static Dictionary<int,int> BuildItemMap(int[] ids, int v)` 是**方法**，
    没有任何静态状态，不该要求 ResetStaticCaches；
    `private static readonly Dictionary<string,int> _cache = new ...;` 才是字段。

    判据：`(` 出现在 `=` 之前（或压根没有 `=`）。
    字段初始化里的 `new Dictionary<...>()` 括号一定在 `=` 之后，不会误判；
    方法声明的参数表括号一定在前。
    """
    paren = stripped.find("(")
    if paren < 0:
        return False
    assign = stripped.find("=")
    if assign < 0:
        return True
    return paren < assign


def is_cache_line(line: str) -> bool:
    """判断一行是否声明了静态缓存字段"""
    stripped = line.strip()
    if stripped.startswith("//") or stripped.startswith("///") or stripped.startswith("*"):
        return False
    # 方法声明先排除，否则「返回字典的静态纯函数」会被当成静态缓存，
    # 逼着无状态的工具类去加一个空的 ResetStaticCaches（那是糊弄守卫，不是修问题）
    if is_method_declaration(stripped):
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
    """从真实 OnDestroy 出发解析调用边，不把方法名或孤立 reset 当作证据。"""
    return {owner for owner, method in reachable_reset_methods(all_files)}


def structural_braces(line: str, state: dict) -> list:
    """Return structural braces on a C# line, ignoring comments and strings."""
    braces = []
    i = 0
    while i < len(line):
        ch = line[i]
        nxt = line[i + 1] if i + 1 < len(line) else ""

        if state.get("block_comment"):
            if ch == "*" and nxt == "/":
                state["block_comment"] = False
                i += 2
            else:
                i += 1
            continue

        if state.get("string"):
            if state.get("verbatim_string"):
                if ch == '"' and nxt == '"':
                    i += 2
                elif ch == '"':
                    state["string"] = False
                    state["verbatim_string"] = False
                    i += 1
                else:
                    i += 1
            else:
                if ch == "\\":
                    i += 2
                elif ch == '"':
                    state["string"] = False
                    i += 1
                else:
                    i += 1
            continue

        if state.get("char"):
            if ch == "\\":
                i += 2
            elif ch == "'":
                state["char"] = False
                i += 1
            else:
                i += 1
            continue

        if ch == "/" and nxt == "/":
            break
        if ch == "/" and nxt == "*":
            state["block_comment"] = True
            i += 2
            continue
        if ch == "'" :
            state["char"] = True
            i += 1
            continue
        if ch == '"':
            state["string"] = True
            state["verbatim_string"] = False
            i += 1
            continue
        if ch == "@" and nxt == '"':
            state["string"] = True
            state["verbatim_string"] = True
            i += 2
            continue
        if ch == "$" and nxt == '"':
            state["string"] = True
            state["verbatim_string"] = False
            i += 2
            continue
        if ch == "$" and nxt == "@" and i + 2 < len(line) and line[i + 2] == '"':
            state["string"] = True
            state["verbatim_string"] = True
            i += 3
            continue
        if ch == "@" and nxt == "$" and i + 2 < len(line) and line[i + 2] == '"':
            state["string"] = True
            state["verbatim_string"] = True
            i += 3
            continue

        if ch == "{" or ch == "}":
            braces.append(ch)

        i += 1

    return braces


def find_class_body_end(lines: list, start_line: int) -> int:
    """Find the exclusive end line for a class body using structural braces."""
    state = {}
    depth = 0
    opened = False
    for i in range(start_line, len(lines)):
        for brace in structural_braces(lines[i], state):
            if brace == "{":
                depth += 1
                opened = True
            elif opened:
                depth -= 1
                if depth <= 0:
                    return i + 1

    return len(lines)


def analyze_file(filepath: Path, text: str) -> list:
    """
    分析单个文件，返回检测到的类信息列表。
    每项: (logical_name, actual_name, has_cache, has_reset)

    使用类声明后的大括号范围来分段，避免嵌套 struct/class 被误判为
    持有外层类的静态缓存字段。
    """
    text = clean_structure_source(text)
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

    # 对每个类，检查其真实大括号范围内的内容
    for idx, (start_line, actual_name, is_partial_mb) in enumerate(class_positions):
        end_line = find_class_body_end(lines, start_line)

        if is_partial_mb:
            logical_name = filepath.stem
            if logical_name.endswith("StaticCacheReset"):
                logical_name = logical_name[:-len("StaticCacheReset")]
        else:
            logical_name = actual_name

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

    try:
        all_files = load_production_sources(repo_root)
        ondestroy_called = find_reset_calls_on_ondestroy_path(all_files)
    except (OSError, UnicodeError, ValueError) as error:
        print("StaticCacheLifecycleGuard: FAIL: " + str(error))
        return 1
    print(f"  正式编译源码: {len(all_files)}")

    # 检查每个文件中的类；partial class 需要按 logical_name 聚合，否则拆分后
    # 含缓存字段的分部文件会被误判为缺少 ResetStaticCaches。
    results = []
    class_records = {}

    for filepath, text in all_files.items():
        file_classes = analyze_file(filepath, text)

        for logical_name, actual_name, has_cache, has_reset in file_classes:
            record = class_records.setdefault(logical_name, {
                "actual_names": set(),
                "has_cache": False,
                "has_reset": False,
                "files": [],
            })
            record["actual_names"].add(actual_name)
            record["has_cache"] = record["has_cache"] or has_cache
            record["has_reset"] = record["has_reset"] or has_reset
            record["files"].append(filepath)

    for logical_name, record in class_records.items():
        actual_names = record["actual_names"]
        has_cache = record["has_cache"]
        has_reset = record["has_reset"]
        filepath = record["files"][0]

        # 判断是否在 OnDestroy 路径上被调用
        in_ondestroy = False
        if has_reset:
            if logical_name in ondestroy_called:
                in_ondestroy = True
            elif any(actual_name in ondestroy_called for actual_name in actual_names):
                in_ondestroy = True
            elif "ModBehaviour" in actual_names and "ModBehaviour" in ondestroy_called:
                in_ondestroy = True

        # 判定
        if has_reset and in_ondestroy:
            results.append(("PASS", logical_name, filepath,
                            "已有 ResetStaticCaches 且在 OnDestroy 路径上被调用"))
        elif logical_name in allowlist or any(actual_name in allowlist for actual_name in actual_names):
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
    target_classes = {
        "BossRushAudioHooks",
        "DebugAndTools",
        "DialogueActorFactory",
        "EquipmentFactory",
        "NPCTeleportUI",
        "NPCUIAssetCache",
        "ItemFactory",
        "ReforgeUIManager",
        "ReflectionCache",
        "AchievementIconLoader",
        "BossRushAchievementManager",
        "CourierPaidLootSweepService",
        "EntityModelFactory",
        "ModeEMerchant",
        "StorageDepositService",
        "WishFountainRewardAnimationView",
        "WishFountainService",
    }
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
