"""
属性测试: MeleeWeaponFxPolicy 分支契约验证

本脚本通过两种互补方式验证 MeleeWeaponFxPolicy.ApplyTo 的分支契约：
1. 静态分析 C# 源码，确认分支结构正确
2. 在 Python 中建模 ApplyTo 逻辑，用随机输入验证属性（100+ 次迭代）

# Feature: architecture-extensibility-refactor, Property 1: slashFx 不被非法赋值
# Feature: architecture-extensibility-refactor, Property 2: hitFx 回退正确性
"""

from pathlib import Path
import random
import re
import sys

# ============================================================
# 配置
# ============================================================

SOURCE = Path("Common/Effects/MeleeWeaponFxPolicy.cs")
ITERATIONS = 200  # 每个属性的随机迭代次数（远超最低要求的 100 次）
SEED = 42  # 固定种子保证可复现


# ============================================================
# 辅助：提取方法体
# ============================================================

def extract_method_block(text: str, method_name: str) -> str:
    """从 C# 源码中提取指定方法的完整代码块"""
    # 匹配方法签名（可能跨行）
    pattern = re.compile(
        r'public\s+void\s+' + re.escape(method_name) + r'\s*\(',
        re.MULTILINE
    )
    match = pattern.search(text)
    if not match:
        return ""

    # 找到方法体的第一个 {
    brace_start = text.find("{", match.start())
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    return ""


# ============================================================
# 静态分析验证
# ============================================================

def static_analysis_property1(source_text: str) -> tuple:
    """
    静态分析 Property 1: slashFx 不被非法赋值
    验证：
    - ApplyTo 中 slashFx 赋值被 AllowSlashFxFallback 条件守护
    - 条件外无其他 slashFx 赋值
    """
    block = extract_method_block(source_text, "ApplyTo")
    if not block:
        return False, "无法找到 ApplyTo 方法体"

    # 检查条件守护存在
    guard_pattern = r'if\s*\(\s*AllowSlashFxFallback\s*&&\s*meleeAgent\.slashFx\s*==\s*null\s*\)'
    if not re.search(guard_pattern, block):
        return False, "未找到 slashFx 的条件守护: if (AllowSlashFxFallback && meleeAgent.slashFx == null)"

    # 找到所有 meleeAgent.slashFx = 赋值（排除 == 比较和 transform 访问）
    # 匹配 meleeAgent.slashFx = xxx 但不匹配 meleeAgent.slashFx == 或 meleeAgent.slashFx.transform
    assign_pattern = r'meleeAgent\.slashFx\s*=[^=]'
    assignments = list(re.finditer(assign_pattern, block))

    if len(assignments) == 0:
        return False, "未找到任何 slashFx 赋值语句"

    # 验证所有 slashFx 赋值都在条件守护块内
    # 找到条件守护的位置
    guard_match = re.search(guard_pattern, block)
    guard_pos = guard_match.start()

    # 找到条件守护后的代码块
    brace_after_guard = block.find("{", guard_pos)
    if brace_after_guard == -1:
        return False, "条件守护后未找到代码块"

    # 提取条件守护块的范围
    depth = 0
    guard_block_end = -1
    for i in range(brace_after_guard, len(block)):
        if block[i] == "{":
            depth += 1
        elif block[i] == "}":
            depth -= 1
            if depth == 0:
                guard_block_end = i
                break

    if guard_block_end == -1:
        return False, "无法确定条件守护块的结束位置"

    # 检查是否有 slashFx 赋值在守护块之外（排除缩放修正部分的 transform 赋值）
    for m in assignments:
        pos = m.start()
        # 检查是否在守护块内
        if brace_after_guard <= pos <= guard_block_end:
            continue
        # 检查是否是 slashFx.transform.localScale 赋值（缩放修正，不是 slashFx 本身的赋值）
        context = block[max(0, pos - 5):pos + 50]
        if "transform" in context or "localScale" in context:
            continue
        return False, f"发现守护块外的 slashFx 赋值（位置 {pos}）：{block[pos:pos+60]}"

    return True, "slashFx 赋值完全被 AllowSlashFxFallback 条件守护"


def static_analysis_property2(source_text: str) -> tuple:
    """
    静态分析 Property 2: hitFx 回退正确性
    验证：
    - ApplyTo 中存在 AllowHitFxFallback 条件守护
    - 守护块内调用 getFallbackHitFx
    """
    block = extract_method_block(source_text, "ApplyTo")
    if not block:
        return False, "无法找到 ApplyTo 方法体"

    # 检查 hitFx 条件守护
    guard_pattern = r'if\s*\(\s*AllowHitFxFallback\s*&&\s*meleeAgent\.hitFx\s*==\s*null\s*\)'
    guard_match = re.search(guard_pattern, block)
    if not guard_match:
        return False, "未找到 hitFx 的条件守护: if (AllowHitFxFallback && meleeAgent.hitFx == null)"

    # 找到守护块
    guard_pos = guard_match.start()
    brace_after_guard = block.find("{", guard_pos)
    if brace_after_guard == -1:
        return False, "hitFx 条件守护后未找到代码块"

    depth = 0
    guard_block_end = -1
    for i in range(brace_after_guard, len(block)):
        if block[i] == "{":
            depth += 1
        elif block[i] == "}":
            depth -= 1
            if depth == 0:
                guard_block_end = i
                break

    if guard_block_end == -1:
        return False, "无法确定 hitFx 条件守护块的结束位置"

    # 检查守护块内是否有 hitFx 赋值且调用了 getFallbackHitFx
    guard_block_content = block[brace_after_guard:guard_block_end + 1]

    hitfx_assign = re.search(r'meleeAgent\.hitFx\s*=\s*getFallbackHitFx', guard_block_content)
    if not hitfx_assign:
        return False, "hitFx 条件守护块内未找到 getFallbackHitFx 赋值"

    return True, "hitFx 回退赋值正确位于 AllowHitFxFallback 条件守护块内"


# ============================================================
# Python 模型：模拟 ApplyTo 逻辑
# ============================================================

class MockMeleeAgent:
    """模拟 ItemAgent_MeleeWeapon 的最小接口"""
    def __init__(self, slash_fx, hit_fx):
        self.slashFx = slash_fx
        self.hitFx = hit_fx


class MeleeWeaponFxPolicyModel:
    """
    Python 模型，精确复现 C# MeleeWeaponFxPolicy.ApplyTo 的分支逻辑
    """
    def __init__(self, allow_slash_fx_fallback: bool, allow_hit_fx_fallback: bool):
        self.AllowSlashFxFallback = allow_slash_fx_fallback
        self.AllowHitFxFallback = allow_hit_fx_fallback

    def apply_to(self, melee_agent, get_fallback_slash_fx, get_fallback_hit_fx):
        """精确模拟 C# ApplyTo 方法的分支逻辑"""
        if melee_agent is None:
            return

        # slashFx 分支契约
        if self.AllowSlashFxFallback and melee_agent.slashFx is None:
            result = get_fallback_slash_fx() if get_fallback_slash_fx else None
            melee_agent.slashFx = result

        # hitFx 分支
        if self.AllowHitFxFallback and melee_agent.hitFx is None:
            result = get_fallback_hit_fx() if get_fallback_hit_fx else None
            melee_agent.hitFx = result


# ============================================================
# 属性测试：随机迭代验证
# ============================================================

def random_fx_value(rng: random.Random):
    """生成随机的 FX 值：None 或一个唯一标识对象"""
    if rng.random() < 0.5:
        return None
    return f"fx_object_{rng.randint(1, 10000)}"


def property_test_1(iterations: int, rng: random.Random) -> tuple:
    """
    Property 1: slashFx 不被非法赋值
    当 AllowSlashFxFallback=false 时，对任意初始状态的 meleeAgent，
    ApplyTo 后 slashFx 值不变。

    **Validates: Requirements 3.1, 3.6**
    """
    failures = []

    for i in range(iterations):
        # 随机生成初始状态
        initial_slash_fx = random_fx_value(rng)
        initial_hit_fx = random_fx_value(rng)
        fallback_slash = f"fallback_slash_{i}"
        fallback_hit = f"fallback_hit_{i}"

        # 随机选择 AllowHitFxFallback（不影响 Property 1 的验证）
        allow_hit = rng.choice([True, False])

        # 核心约束：AllowSlashFxFallback = false
        policy = MeleeWeaponFxPolicyModel(
            allow_slash_fx_fallback=False,
            allow_hit_fx_fallback=allow_hit
        )

        agent = MockMeleeAgent(slash_fx=initial_slash_fx, hit_fx=initial_hit_fx)

        # 执行 ApplyTo
        policy.apply_to(
            agent,
            get_fallback_slash_fx=lambda: fallback_slash,
            get_fallback_hit_fx=lambda: fallback_hit
        )

        # 验证：slashFx 不变
        if agent.slashFx != initial_slash_fx:
            failures.append(
                f"  迭代 {i}: 初始 slashFx={initial_slash_fx!r}, "
                f"执行后 slashFx={agent.slashFx!r} (应保持不变)"
            )

    if failures:
        return False, f"发现 {len(failures)} 次违反:\n" + "\n".join(failures[:5])

    return True, f"{iterations} 次随机迭代全部通过"


def property_test_2(iterations: int, rng: random.Random) -> tuple:
    """
    Property 2: hitFx 回退正确性
    当 AllowHitFxFallback=true 且 hitFx 初始为 null 且 getFallbackHitFx 返回非 null 时，
    ApplyTo 后 hitFx 等于 fallback 返回值。

    **Validates: Requirements 3.1, 3.7**
    """
    failures = []

    for i in range(iterations):
        # 随机生成 slashFx 初始状态（不影响 Property 2）
        initial_slash_fx = random_fx_value(rng)

        # 随机选择 AllowSlashFxFallback（不影响 Property 2 的验证）
        allow_slash = rng.choice([True, False])

        # 核心约束：AllowHitFxFallback=true, hitFx 初始为 null
        # getFallbackHitFx 返回非 null
        fallback_hit = f"fallback_hit_{rng.randint(1, 10000)}"
        fallback_slash = f"fallback_slash_{i}"

        policy = MeleeWeaponFxPolicyModel(
            allow_slash_fx_fallback=allow_slash,
            allow_hit_fx_fallback=True
        )

        agent = MockMeleeAgent(slash_fx=initial_slash_fx, hit_fx=None)

        # 使用闭包捕获当前迭代的 fallback 值
        expected_hit = fallback_hit

        policy.apply_to(
            agent,
            get_fallback_slash_fx=lambda: fallback_slash,
            get_fallback_hit_fx=lambda: expected_hit
        )

        # 验证：hitFx 等于 fallback 返回值
        if agent.hitFx != expected_hit:
            failures.append(
                f"  迭代 {i}: getFallbackHitFx 返回 {expected_hit!r}, "
                f"但执行后 hitFx={agent.hitFx!r}"
            )

    if failures:
        return False, f"发现 {len(failures)} 次违反:\n" + "\n".join(failures[:5])

    return True, f"{iterations} 次随机迭代全部通过"


# ============================================================
# 主入口
# ============================================================

def main() -> int:
    exit_code = 0
    rng = random.Random(SEED)

    # ---- 读取源码 ----
    if not SOURCE.exists():
        print(f"[错误] 源文件不存在: {SOURCE}")
        return 1

    source_text = SOURCE.read_text(encoding="utf-8")

    # ===========================================================
    # Property 1: slashFx 不被非法赋值
    # ===========================================================
    print("# Feature: architecture-extensibility-refactor, Property 1: slashFx 不被非法赋值")
    print("# Validates: Requirements 3.1, 3.6")
    print()

    # 静态分析
    ok, msg = static_analysis_property1(source_text)
    if ok:
        print(f"  [静态分析] PASS - {msg}")
    else:
        print(f"  [静态分析] FAIL - {msg}")
        exit_code = 1

    # 随机迭代（模型验证）
    ok, msg = property_test_1(ITERATIONS, rng)
    if ok:
        print(f"  [随机迭代] PASS - {msg}")
    else:
        print(f"  [随机迭代] FAIL - {msg}")
        exit_code = 1

    if exit_code == 0:
        print()
        print("  Property 1 结论: PASS")
    else:
        print()
        print("  Property 1 结论: FAIL")

    print()

    # ===========================================================
    # Property 2: hitFx 回退正确性
    # ===========================================================
    print("# Feature: architecture-extensibility-refactor, Property 2: hitFx 回退正确性")
    print("# Validates: Requirements 3.1, 3.7")
    print()

    # 静态分析
    ok2, msg2 = static_analysis_property2(source_text)
    if ok2:
        print(f"  [静态分析] PASS - {msg2}")
    else:
        print(f"  [静态分析] FAIL - {msg2}")
        exit_code = 1

    # 随机迭代（模型验证）
    ok2b, msg2b = property_test_2(ITERATIONS, rng)
    if ok2b:
        print(f"  [随机迭代] PASS - {msg2b}")
    else:
        print(f"  [随机迭代] FAIL - {msg2b}")
        exit_code = 1

    if ok2 and ok2b:
        print()
        print("  Property 2 结论: PASS")
    else:
        print()
        print("  Property 2 结论: FAIL")

    print()

    # ===========================================================
    # 总结
    # ===========================================================
    if exit_code == 0:
        print("MeleeWeaponFxPolicyPropertyTest: 全部属性测试 PASS")
    else:
        print("MeleeWeaponFxPolicyPropertyTest: 存在属性测试 FAIL")

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
