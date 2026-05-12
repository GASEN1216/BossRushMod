"""
Guard: 长期架构目标 Non-Goal 守门
静态检查本轮不得引入 BossRushEventBus、IBossRushEventSubscriber、EventBus、IGameWorldProbe
等类型声明（class/interface/struct）。

扫描排除目录：Build/、.codex_tmp/、tests/、docs/、.git/、.kiro/

Validates: Requirements 11.2, 11.3, 11.4, 12.2, 12.3, 12.4
"""

from pathlib import Path
import re
import sys

# ============================================================================
# 配置
# ============================================================================

EXCLUDE_DIRS = {"Build", ".codex_tmp", "tests", "docs", ".git", ".kiro"}

# 禁止在本轮引入的类型名称
FORBIDDEN_TYPE_NAMES = [
    "BossRushEventBus",
    "IBossRushEventSubscriber",
    "EventBus",
    "IGameWorldProbe",
]

# ============================================================================
# 工具函数
# ============================================================================


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


def build_forbidden_pattern() -> re.Pattern:
    """构建禁止类型声明的正则模式"""
    # 匹配 class/interface/struct + 空白 + 禁止类型名
    names_alternation = "|".join(re.escape(name) for name in FORBIDDEN_TYPE_NAMES)
    pattern = r"(class|interface|struct)\s+(" + names_alternation + r")\b"
    return re.compile(pattern)


# ============================================================================
# 主逻辑
# ============================================================================


def main() -> int:
    print("LongTermGoalNonGoalGuard: 开始检查禁止引入的长期目标类型...")

    repo_root = Path(__file__).parent.parent
    forbidden_pattern = build_forbidden_pattern()

    violations = []  # (file_path, line_num, type_keyword, type_name)

    # 扫描所有 .cs 文件
    for cs_file in sorted(repo_root.rglob("*.cs")):
        if should_exclude(cs_file, repo_root):
            continue

        try:
            text = cs_file.read_text(encoding="utf-8")
        except Exception:
            continue

        for line_num, line in enumerate(text.split("\n"), start=1):
            # 跳过注释行
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("///") or stripped.startswith("*"):
                continue

            match = forbidden_pattern.search(line)
            if match:
                type_keyword = match.group(1)
                type_name = match.group(2)
                violations.append((cs_file, line_num, type_keyword, type_name))

    # 输出结果
    if violations:
        print(f"\n  === 违规项（共 {len(violations)} 处） ===")
        for filepath, line_num, type_keyword, type_name in violations:
            rel_path = filepath.relative_to(repo_root)
            print(f"  [FAIL] {rel_path}:{line_num} - "
                  f"本轮禁止引入类型: {type_keyword} {type_name}")

        print(f"\nLongTermGoalNonGoalGuard: FAIL（发现 {len(violations)} 处禁止类型声明）")
        print("  提示: 以上类型属于长期架构目标（P3），本轮仅登记为 non-goal，不得提前引入。")
        return 1
    else:
        print(f"\n  已扫描排除目录: {', '.join(sorted(EXCLUDE_DIRS))}")
        print(f"  禁止类型列表: {', '.join(FORBIDDEN_TYPE_NAMES)}")
        print(f"\nLongTermGoalNonGoalGuard: PASS（未发现禁止类型声明）")
        return 0


if __name__ == "__main__":
    sys.exit(main())
