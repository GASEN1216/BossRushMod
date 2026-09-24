#!/usr/bin/env python3
"""Read the maintained module map and print a small, ordered task context."""

import argparse
import glob
import json
import sys
from pathlib import Path

from compile_list import read_compile_sources

ROOT = Path(__file__).resolve().parent.parent
INDEX = ROOT / "architecture/modules.json"
TABLE = ROOT / "MODULES.md"
BEGIN = "<!-- BEGIN GENERATED MODULES -->"
END = "<!-- END GENERATED MODULES -->"
KINDS = {"host", "mode", "map", "system", "content", "shared", "devtools"}
FIELDS = {"id", "title", "kind", "paths", "entry", "state_owner", "public_api",
          "data", "tests", "docs", "rules", "depends_on"}


def load_modules():
    data = json.loads(INDEX.read_text(encoding="utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("modules"), list):
        raise ValueError("architecture/modules.json 缺少 modules 数组")
    return data["modules"]


def matches(pattern):
    return {Path(path).relative_to(ROOT).as_posix()
            for path in glob.glob(str(ROOT / pattern), recursive=True)
            if Path(path).is_file()}


def generated_table(modules):
    lines = [BEGIN, "| id | 职责 | 入口 | 规则文件 |",
             "| --- | --- | --- | --- |"]
    for module in modules:
        rules = "、".join("`" + rule + "`" for rule in module["rules"])
        lines.append("| `{}` | {} | `{}` | {} |".format(
            module["id"], module["title"], module["entry"], rules))
    lines.append(END)
    return "\n".join(lines)


def table_with_generated(text, modules):
    if text.count(BEGIN) != 1 or text.count(END) != 1:
        raise ValueError("MODULES.md 需要唯一的生成区标记")
    before, rest = text.split(BEGIN, 1)
    _, after = rest.split(END, 1)
    return before + generated_table(modules) + after


def check(modules):
    errors = []
    ids = [module.get("id") for module in modules]
    if len(ids) != len(set(ids)):
        errors.append("模块 id 重复")
    known = set(ids)
    covered = set()
    for module in modules:
        ident = module.get("id", "<无 id>")
        missing = FIELDS - set(module)
        if missing:
            errors.append("{} 缺字段 {}".format(ident, sorted(missing)))
            continue
        if module["kind"] not in KINDS:
            errors.append("{} kind 不合法".format(ident))
        if not module["paths"]:
            errors.append("{} paths 为空".format(ident))
        for pattern in module["paths"]:
            found = matches(pattern)
            if not found:
                errors.append("{} 的 glob 未命中文件：{}".format(ident, pattern))
            covered.update(found)
        if not (ROOT / module["entry"]).is_file():
            errors.append("{} 入口不存在：{}".format(ident, module["entry"]))
        for rule in module["rules"]:
            if not (ROOT / rule).is_file():
                errors.append("{} 规则文件不存在：{}".format(ident, rule))
        for dependency in module["depends_on"]:
            if dependency not in known:
                errors.append("{} 依赖未知模块：{}".format(ident, dependency))
    missing_sources = sorted(set(read_compile_sources()) - covered)
    if missing_sources:
        errors.append("编译清单有 {} 个文件未归属：{}".format(
            len(missing_sources), ", ".join(missing_sources[:12])))
    text = TABLE.read_text(encoding="utf-8")
    try:
        if text != table_with_generated(text, modules):
            errors.append("MODULES.md 生成区与 modules.json 不一致")
    except ValueError as error:
        errors.append(str(error))
    return errors


def show(module, task):
    if task:
        print("任务：" + task)
    print("模块：{} — {}".format(module["id"], module["title"]))
    print("1. 根规则：AGENTS.md §4、§5、§8；兼容风险与许可见 §6、§10")
    print("2. 模块规则：" + "、".join(module["rules"]))
    print("3. 入口：" + module["entry"])
    print("4. 公开接口：" + "、".join(module["public_api"]))
    print("5. 数据：" + ("、".join(module["data"]) or "无专项数据表"))
    print("6. 守卫与夹具：" + ("、".join(module["tests"]) or "按变更路径查 tests/"))
    print("7. 专题文档：" + ("、".join(module["docs"]) or "按专题查 .qoder/repowiki/"))
    print("状态归属：" + module["state_owner"])
    print("导航依赖：" + ("、".join(module["depends_on"]) or "无"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--module", help="modules.json 中的模块 id")
    parser.add_argument("--task", help="当前具体任务，一句话")
    parser.add_argument("--check", action="store_true", help="验证归属与生成区")
    parser.add_argument("--write", action="store_true", help="按 modules.json 更新 MODULES.md 生成区")
    args = parser.parse_args()
    if not (args.module or args.check or args.write):
        parser.error("请给 --module、--check 或 --write")
    try:
        modules = load_modules()
        if args.write:
            TABLE.write_text(table_with_generated(TABLE.read_text(encoding="utf-8"), modules), encoding="utf-8")
        if args.check:
            errors = check(modules)
            for error in errors:
                print("ModuleIndex: " + error, file=sys.stderr)
            if errors:
                return 1
            print("ModuleIndex: PASS ({} modules / {} compiled sources)".format(
                len(modules), len(read_compile_sources())))
        if args.module:
            module = next((item for item in modules if item["id"] == args.module), None)
            if module is None:
                print("未知模块 id：" + args.module, file=sys.stderr)
                return 2
            show(module, args.task)
        return 0
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print("ModuleIndex: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
