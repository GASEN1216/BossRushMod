"""Guard: DisplayNameRaw 必须配本地化注入

golden rule 4.4：凡设置 `DisplayNameRaw = "BossRush_<Name>"` 的物品/装备，
必须在对应 Config 的 `InjectLocalization()` 注入该 key，否则游戏内会显示 `*BossRush_<Name>*`。

这条规则被 AGENTS.md §4.4 与 CODE_REVIEW.md 列为必查项，但此前**零自动化**（全靠人工 grep）。
本 guard 补上。

实现要点（对着实际代码写，不是照搬文档措辞）：

- 仓库里**没有**任何一处写成字面量 `DisplayNameRaw = "BossRush_xxx"`；
  统一是 `item.DisplayNameRaw = LOC_KEY_DISPLAY;` 这种常量引用。
  所以必须先把常量解析回它的字符串值。
- 注入点不一定在同一个文件：Mode F 工事包那批常量定义在各自 Config，
  注入却集中在 `Integration/BossRushIntegration.cs` 的 `InjectModeFItemLoc(...)`。
  所以要在全仓范围找注入证据，接受三种形态：
    1. 同文件里 `Inject...(LOC_KEY_DISPLAY` 直接用常量；
    2. 任意文件里 `Inject...(XxxConfig.LOC_KEY_DISPLAY` 用限定名；
    3. 任意文件里 `Inject...("BossRush_Xxx"` 直接用字面量。

扫描排除目录：Build/、tests/、.git/、.kiro/、.codex_tmp/、鸭科夫源码/、wiki-site/、.qoder/
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
ALLOWLIST_FILE = Path(__file__).parent / "localization_injection_allowlist.txt"

EXCLUDE_DIRS = {
    "Build", "tests", ".git", ".kiro", ".codex_tmp",
    "鸭科夫源码", "wiki-site", ".qoder", "obj", "bin",
}

# item.DisplayNameRaw = LOC_KEY_DISPLAY;  /  equipment.DisplayNameRaw = WeaponNameKey;
RE_ASSIGN_IDENT = re.compile(r"\.DisplayNameRaw\s*=\s*([A-Za-z_]\w*)\s*;")
# 直接字面量写法（当前仓库为 0，但规则要求覆盖）
RE_ASSIGN_LITERAL = re.compile(r'\.DisplayNameRaw\s*=\s*"([^"]+)"\s*;')
# const string LOC_KEY_DISPLAY = "BossRush_FlightTotem";
RE_CONST_STRING = re.compile(r'\bconst\s+string\s+([A-Za-z_]\w*)\s*=\s*"([^"]*)"')
# 任意 Inject 系列调用（InjectLocalization / InjectModeFItemLoc / InjectXxxLocalization ...）
RE_INJECT_CALL = re.compile(r"\bInject\w*\s*\(", re.M)
# 新一代本地化文件（Codex / ModeH / PetNest 等）不再逐个 Inject(...) 调用，
# 而是先往 Dictionary<string,string> 里塞键、最后一次性 InjectLocalizations(map)。
# 这里把「字典索引赋值」也算作注入证据，否则这种写法会被误报成漏注入。
RE_MAP_ASSIGN = re.compile(r"\w+\[\s*([^\]]+?)\s*\]\s*=", re.M)

RE_STRING_LITERAL = re.compile(r'"([^"\\]*(?:\\.[^"\\]*)*)"')
RE_QUALIFIED = re.compile(r"\b([A-Za-z_]\w*)\.([A-Za-z_]\w*)\b")
RE_IDENT = re.compile(r"\b([A-Za-z_]\w*)\b")


def fail(message):
    print("LocalizationInjectionGuard: FAIL - " + message)
    return 1


def load_allowlist():
    allowed = set()
    if not ALLOWLIST_FILE.exists():
        return allowed
    for raw in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        allowed.add(line.split("|", 1)[0].strip())
    return allowed


def iter_source_files():
    for path in sorted(ROOT.rglob("*.cs")):
        try:
            rel = path.relative_to(ROOT)
        except ValueError:
            continue
        if any(part in EXCLUDE_DIRS for part in rel.parts):
            continue
        yield rel, path


def extract_call_args(text, open_paren_index):
    """从 '(' 开始按括号配对截取实参文本，失败时退回定长截断。"""
    depth = 0
    for i in range(open_paren_index, min(len(text), open_paren_index + 4000)):
        ch = text[i]
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return text[open_paren_index + 1:i]
    return text[open_paren_index + 1:open_paren_index + 400]


def main():
    print("LocalizationInjectionGuard: 开始核对 DisplayNameRaw 与本地化注入的配对...")

    allowlist = load_allowlist()

    files = list(iter_source_files())
    texts = {}
    for rel, path in files:
        try:
            texts[rel] = path.read_text(encoding="utf-8")
        except Exception:
            continue

    # ---- 建立全仓注入证据索引 ----
    inject_literals = set()          # Inject 调用里出现过的字符串字面量
    inject_qualified = set()         # Inject 调用里出现过的 Xxx.YYY
    inject_idents_by_file = {}       # 每个文件 Inject 调用里出现过的裸标识符

    for rel, text in texts.items():
        idents = set()
        for m in RE_INJECT_CALL.finditer(text):
            args = extract_call_args(text, m.end() - 1)
            for lit in RE_STRING_LITERAL.findall(args):
                inject_literals.add(lit)
            for owner, member in RE_QUALIFIED.findall(args):
                inject_qualified.add(owner + "." + member)
            for ident in RE_IDENT.findall(args):
                idents.add(ident)
        # 字典索引式注入：map[CodexBookConfig.LOC_KEY_DISPLAY] = displayName;
        if "InjectLocalizations" in text or "SetOverrideText" in text:
            for key_expr in RE_MAP_ASSIGN.findall(text):
                for lit in RE_STRING_LITERAL.findall(key_expr):
                    inject_literals.add(lit)
                for owner, member in RE_QUALIFIED.findall(key_expr):
                    inject_qualified.add(owner + "." + member)
                for ident2 in RE_IDENT.findall(key_expr):
                    idents.add(ident2)
        inject_idents_by_file[rel] = idents

    # ---- 收集 DisplayNameRaw 赋值并解析 key ----
    missing = []
    unresolved = []
    checked = 0

    for rel, text in texts.items():
        consts = dict(RE_CONST_STRING.findall(text))
        class_names = re.findall(r"\b(?:class|struct)\s+([A-Za-z_]\w*)", text)

        candidates = []
        for m in RE_ASSIGN_IDENT.finditer(text):
            candidates.append((m.group(1), consts.get(m.group(1)), m.start()))
        for m in RE_ASSIGN_LITERAL.finditer(text):
            candidates.append((None, m.group(1), m.start()))

        for ident, key, pos in candidates:
            line_no = text.count("\n", 0, pos) + 1
            rel_str = str(rel).replace("\\", "/")

            if key is None:
                # 常量不在本文件里，无法静态解析
                unresolved.append((rel_str, line_no, ident))
                continue

            if not key.startswith("BossRush_"):
                continue

            checked += 1

            if key in allowlist or (rel_str + ":" + key) in allowlist:
                continue

            found = key in inject_literals
            if not found and ident:
                found = ident in inject_idents_by_file.get(rel, set())
            if not found and ident:
                for cls in class_names:
                    if (cls + "." + ident) in inject_qualified:
                        found = True
                        break

            if not found:
                missing.append((rel_str, line_no, ident or "<literal>", key))

    if missing:
        print("\n  === 缺少本地化注入（游戏内会显示 *key* 星号原文） ===")
        for rel_str, line_no, ident, key in missing:
            print("  [FAIL] {0}:{1} DisplayNameRaw = {2} (\"{3}\") 找不到对应的 Inject 调用".format(
                rel_str, line_no, ident, key))
        print("  提示: 在对应 Config 的 InjectLocalization() 注入该 key，")
        print("        并确认它挂进了 InjectLocalization_Extra_Integration()（AGENTS.md §4.4）。")

    if unresolved:
        print("\n  === 常量无法在同文件解析（guard 无法核对，请人工确认或补豁免） ===")
        for rel_str, line_no, ident in unresolved:
            print("  [WARN] {0}:{1} DisplayNameRaw = {2}".format(rel_str, line_no, ident))

    if missing:
        return fail("{0} 处 DisplayNameRaw 缺少本地化注入".format(len(missing)))

    print("\n  已核对 BossRush_* 显示名 key: {0} 处，全部找到注入证据".format(checked))
    print("  注入索引: 字面量 {0} 个 / 限定名 {1} 个".format(len(inject_literals), len(inject_qualified)))
    print("  豁免条目: {0} 条".format(len(allowlist)))
    print("\nLocalizationInjectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
