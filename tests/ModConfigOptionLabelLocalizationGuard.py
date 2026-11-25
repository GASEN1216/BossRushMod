#!/usr/bin/env python3
"""ModConfigOptionLabelLocalizationGuard — ModConfig 下拉项标题必须有官方本地化键 Options_<配置键>。

背景（2026-11-24 owner 实测「配置里都没有本地化键」）：
  ModConfig 的下拉项（AddDropdownList，以及走同一条路的 AddBoolDropdownList）实例化官方 OptionsUIEntry_Dropdown。
  官方组件在 Awake / OnValidate / 每次切语言时把标题重设为 ("Options_" + provider.Key).ToPlainText()，
  键没注入就显示 "*Options_BossRush_xxx*"。ModConfig 自己的滑块条目不查键，不在本守卫范围。

不变式：
1. 正式编译清单里，每个以 add*Bool* / add*Dropdown* / add*Toggle* 命名的 MethodInfo 的 .Invoke(...) 注册点，
   第 3 个参数（说明文字）要么直接是 ModConfigOptionLocalization.Label(...)，要么是本方法里由它赋值的局部变量。
2. ModConfigOptionLocalization：前缀常量是 "Options_"；Label 先按当前语言解析、登记、再注入前缀 + 配置键；
   Inject 按当前语言重注入全部登记项；ResetStaticCaches 清空登记表。
3. Inject 挂在 InjectLocalization_Extra_Integration（启动与切语言都会调），ResetStaticCaches 挂在
   IntegrationRuntimeModule.OnDestroy。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from compile_list import read_compile_sources  # noqa: E402
from cs_source_util import clean_source  # noqa: E402

LABEL_CALL = "ModConfigOptionLocalization.Label("
REGISTER_INVOKE = re.compile(r"\b(add\w*(?:Bool|Dropdown|Toggle)\w*)\s*\.\s*Invoke\s*\(", re.IGNORECASE)

failures = []


def require(condition, message):
    if not condition:
        failures.append(message)


def read(rel):
    path = os.path.join(ROOT, rel)
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
        return fh.read()


def compact(text):
    return re.sub(r"\s+", "", text or "")


def balanced(text, open_index):
    """open_index 指向 '(' 或 '{'，返回配对括号之间的内容；配不上返回 None。"""
    pairs = {"(": ")", "{": "}"}
    opener = text[open_index]
    closer = pairs[opener]
    depth = 0
    for i in range(open_index, len(text)):
        c = text[i]
        if c == opener:
            depth += 1
        elif c == closer:
            depth -= 1
            if depth == 0:
                return text[open_index + 1:i]
    return None


def split_top_level(args):
    parts, depth, start = [], 0, 0
    for i, c in enumerate(args):
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "," and depth == 0:
            parts.append(args[start:i].strip())
            start = i + 1
    parts.append(args[start:].strip())
    return parts


def body_of(source, signature):
    index = (source or "").find(signature)
    if index < 0:
        return ""
    brace = source.find("{", index)
    if brace < 0:
        return ""
    return balanced(source, brace) or ""


# ---------------------------------------------------------------------------
# 1. 注册点
# ---------------------------------------------------------------------------
sites = 0
for rel in read_compile_sources():
    raw = read(rel)
    if raw is None or ".Invoke" not in raw:
        continue
    code = clean_source(raw)
    for match in REGISTER_INVOKE.finditer(code):
        args = balanced(code, match.end() - 1)
        if args is None:
            failures.append("[注册点] %s 的 %s.Invoke 括号不配对" % (rel, match.group(1)))
            continue
        array_open = args.find("{")
        if "newobject[]" not in compact(args) or array_open < 0:
            continue  # 不是 ModConfig 注册的参数数组形状（如 Load 方法）
        items = split_top_level(balanced(args, array_open) or "")
        if len(items) < 3:
            continue
        sites += 1
        label = items[2]
        line = code.count("\n", 0, match.start()) + 1
        if label.replace(" ", "").startswith(LABEL_CALL):
            continue
        if re.match(r"^[A-Za-z_]\w*$", label):
            before = code[:match.start()]
            assigns = list(re.finditer(r"\b" + re.escape(label) + r"\s*=\s*([^;]+);", before))
            if assigns and assigns[-1].group(1).strip().startswith(LABEL_CALL):
                continue
        failures.append("[注册点] %s:%d 的 %s.Invoke 说明文字 `%s` 没经 ModConfigOptionLocalization.Label，"
                        "官方下拉框切语言后会显示 *Options_<键>*" % (rel, line, match.group(1), label[:60]))

require(sites >= 12, "[注册点] 只找到 %d 个下拉项注册点，少于已知的 12 个，扫描规则可能失效" % sites)

# ---------------------------------------------------------------------------
# 2. 登记与注入
# ---------------------------------------------------------------------------
helper = clean_source(read("Localization/ModConfigOptionLocalization.cs") or "")
require('internalconststringOfficialLabelKeyPrefix="Options_";' in compact(helper),
        "[键] 官方标题键前缀必须是 \"Options_\"（OptionsUIEntry_Dropdown.LabelKey = \"Options_\" + provider.Key）")
label_body = compact(body_of(helper, "internal static string Label(string optionKey, string zh, string en)"))
require("stringtext=L10n.T(zh,en);" in label_body
        and "registeredLabels[optionKey]=new[]{zh,en};" in label_body
        and "LocalizationHelper.InjectLocalization(OfficialLabelKeyPrefix+optionKey,text);" in label_body
        and label_body.rstrip("}").endswith("returntext;"),
        "[登记] Label 要按当前语言解析、登记中英文、注入 Options_<配置键>，并返回同一句给 ModConfig")
inject_body = compact(body_of(helper, "internal static void Inject()"))
require("foreach(KeyValuePair<string,string[]>pairinregisteredLabels)" in inject_body
        and "LocalizationHelper.InjectLocalization(OfficialLabelKeyPrefix+pair.Key,L10n.T(pair.Value[0],pair.Value[1]));" in inject_body,
        "[切语言] Inject 要按当前语言重注入全部登记项")
require("registeredLabels.Clear();" in compact(body_of(helper, "internal static void ResetStaticCaches()")),
        "[清理] ResetStaticCaches 清空登记表")

# ---------------------------------------------------------------------------
# 3. 挂载点
# ---------------------------------------------------------------------------
hub = clean_source(read("Integration/BossRushIntegrationRuntimeModule_ContentRegistration.cs") or "")
require("ModConfigOptionLocalization.Inject();" in compact(body_of(hub, "internal void InjectLocalization_Extra_Integration()")),
        "[挂载] InjectLocalization_Extra_Integration 要调 ModConfigOptionLocalization.Inject()（宿主切语言时重调这条链）")
module = clean_source(read("Integration/BossRushIntegrationRuntimeModule.cs") or "")
require("ModConfigOptionLocalization.ResetStaticCaches();" in compact(body_of(module, "public override void OnDestroy()")),
        "[挂载] IntegrationRuntimeModule.OnDestroy 要清空登记表")

if failures:
    print("ModConfigOptionLabelLocalizationGuard: FAIL")
    for failure in failures:
        print("  - " + failure)
    sys.exit(1)
print("ModConfigOptionLabelLocalizationGuard: %d registration sites; PASS" % sites)
