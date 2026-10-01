"""
Guard: 鸭王杯群战血条队伍后缀不得每次处理都现拼字符串。

ModeHGroupTeamTags.Apply 由统一血条名字补丁（BossRushHealthBarNamePatch，HealthBar.LateUpdate 后缀）
在群战期间对每条 Boss 血条每 6 帧调用一次。以前 Apply 每次都现拼 5 段后缀（Strip 里 4 段 + 拼回 1 段）、
建一个 string[] 再比较（2026-10-01 发布前审查 P3：血条名字每次都重新拼字符串）。

不变式：
1. 四段后缀是 static readonly 字段，声明在 BlueOpen / RedOpen 之后（静态字段按文本顺序初始化）。
2. Apply / Strip 方法体内不调用 Suffix( 与 TeamName(，Strip 不再 new string[]。
3. Apply 在剥后缀之前先判「已带当前语言的正确后缀」并早返（稳态零分配）。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

SOURCE = Path("ModeH/ModeHGroupBattle.cs")


def fail(message: str) -> int:
    print("ModeHGroupTeamTagsNoRebuildGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for idx in range(brace, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace : idx + 1]
    return None


def norm(text: str) -> str:
    return " ".join(text.split())


def main() -> int:
    if not SOURCE.exists():
        return fail(f"missing {SOURCE}")
    text = clean_source(SOURCE.read_text(encoding="utf-8-sig"))

    cls = extract_block(text, "internal static class ModeHGroupTeamTags")
    if cls is None:
        return fail("missing ModeHGroupTeamTags class body")
    cls_n = norm(cls)

    declarations = [
        'private static readonly string BlueSuffixCn = Suffix(false, "蓝队");',
        'private static readonly string BlueSuffixEn = Suffix(false, "Blue Team");',
        'private static readonly string RedSuffixCn = Suffix(true, "红队");',
        'private static readonly string RedSuffixEn = Suffix(true, "Red Team");',
        "private static readonly string[] AllSuffixes = { BlueSuffixCn, BlueSuffixEn, RedSuffixCn, RedSuffixEn };",
    ]
    red_open = cls_n.find("private static readonly string RedOpen =")
    blue_open = cls_n.find("private static readonly string BlueOpen =")
    if red_open < 0 or blue_open < 0:
        return fail("missing BlueOpen / RedOpen declarations")
    for decl in declarations:
        pos = cls_n.find(decl)
        if pos < 0:
            return fail(f"missing cached suffix declaration -> {decl}")
        if pos < red_open or pos < blue_open:
            return fail(f"cached suffix must be declared after BlueOpen / RedOpen (static init order) -> {decl}")

    apply_body = extract_block(cls, "internal static void Apply(HealthBar bar, TextMeshProUGUI nameText)")
    strip_body = extract_block(cls, "private static string Strip(string text)")
    if apply_body is None or strip_body is None:
        return fail("missing Apply or Strip body")

    for name, body in (("Apply", apply_body), ("Strip", strip_body)):
        if re.search(r"\bSuffix\s*\(", body):
            return fail(f"{name} still builds suffix strings via Suffix(")
        if re.search(r"\bTeamName\s*\(", body):
            return fail(f"{name} still resolves TeamName( per call")
    if re.search(r"\bstring\s*\[\s*\]", strip_body) or "new string[" in strip_body:
        return fail("Strip still allocates a string[] per call")
    if "AllSuffixes" not in strip_body:
        return fail("Strip does not iterate cached AllSuffixes")

    apply_n = norm(apply_body)
    pick = (
        "string suffix = team == Teams.scav ? L10n.T(BlueSuffixCn, BlueSuffixEn) "
        ": team == Teams.wolf ? L10n.T(RedSuffixCn, RedSuffixEn) : null;"
    )
    early = "if (suffix != null && text.EndsWith(suffix, StringComparison.Ordinal)) return;"
    strip_call = "string baseText = Strip(text);"
    for required in (pick, early, strip_call):
        if required not in apply_n:
            return fail(f"Apply lacks -> {required}")
    if not (apply_n.find(pick) < apply_n.find(early) < apply_n.find(strip_call)):
        return fail("Apply must pick cached suffix, then early-return when already tagged, before stripping")

    print("ModeHGroupTeamTagsNoRebuildGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
