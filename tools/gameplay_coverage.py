#!/usr/bin/env python3
"""校验游戏功能覆盖清单，并把任意一轮 F3 日志映射为诚实的待测报告。"""
import argparse
import csv
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from compile_list import read_compile_sources  # noqa: E402

MANIFEST = ROOT / 'Assets/Data/GameplayCoverage.json'


def load_manifest():
    return json.loads(MANIFEST.read_text(encoding='utf-8-sig'))


# F3 源码里「第一个实参是字面量用例 id」的用例外壳。新增一种外壳就要登记在这里，否则经它跑的用例
# 不会被要求登记进 GameplayCoverage.json。2026-09-14 之前天空岛的 RunSkyIslandSync / RunSkyIslandCase
# 就不在这份清单里——岛内 28 条用例只是碰巧都手工登记了，新加一条忘了登记也不会有人报。
# tests/GameplayCoverageCaseRunnerGuard.py 反向核对：F3 源码里每一个 `string id` / `string caseId` 外壳都必须在这里或写明不登记的理由。
CASE_RUNNERS = ('RunSyncCase', 'RunSyncCaseGated', 'RunIsolatedCase', 'VerifyArenaCleanup', 'SamplePerformance',
                'WaitRuntimeReady', 'RunSkyIslandSync', 'RunSkyIslandCase', 'SampleResourcePerformance')


def f3_source():
    return '\n'.join(p.read_text(encoding='utf-8-sig') for p in (ROOT / 'DebugAndTools').glob('F3GameplayValidation*.cs'))


def case_ids_in(code):
    """按用例外壳与固定 Record 抽出用例 id。"""
    result = set(re.findall(r'(?:' + '|'.join(CASE_RUNNERS) + r')\("([A-Z0-9_]+)"', code))
    # 固定 Record 同样必须登记，异常/基础设施错误是诊断分支，不要求每轮触发。
    result.update(re.findall(r'Record\("([A-Z0-9_]+)"', code))
    return result


def required_automatic_ids():
    code = f3_source()
    result = case_ids_in(code)
    result.difference_update({'RUN_MARKER', 'COVERAGE_REPORT', 'SUITE_EXECUTION', 'RUNTIME_ERRORS', 'EXTERNAL_ERRORS', 'LOG_DIAGNOSTICS'})
    result.discard('RANDOM_EVENT_')
    result.update({'SCENE_ENTER_ARENA', 'SCENE_RETURN_BASE', 'SCENE_CLICK_GATE_ENTER', 'SCENE_CLICK_GATE_READY',
                   'ITEM_FACTORY_*', 'RANDOM_EVENT_*', 'MAP_TOUR_*'})
    return result


def validate(data):
    errors = []
    if data.get('version') != 1 or not data.get('features'):
        return ['覆盖清单版本/功能表缺失']
    ids, wiki, automatic, sources = set(), set(), set(), set()
    for feature in data['features']:
        key = feature.get('id', '')
        if not re.fullmatch(r'[A-Z][A-Z0-9_]+', key) or key in ids:
            errors.append(f'无效或重复的功能 ID: {key}')
        ids.add(key)
        if not feature.get('title') or not feature.get('sources'):
            errors.append(f'{key}: 缺少功能名/源码定位')
        if not feature.get('manual'):
            errors.append(f'{key}: 缺少从真实玩家入口出发的人工场景')
        for source in feature.get('sources', []):
            sources.add(source)
            if not (ROOT / source).exists():
                errors.append(f'{key}: 引用源码不存在: {source}')
        for case in feature.get('manual', []):
            if case.get('id') in ids or not str(case.get('id', '')).startswith('M_'):
                errors.append(f'{key}: 无效或重复的人工 ID: {case.get("id")}')
            ids.add(case.get('id'))
            if not case.get('steps') or not case.get('expected'):
                errors.append(f'{key}: 人工用例缺少操作/预期: {case.get("id")}')
        automatic.update(feature.get('automatic', []))
        wiki.update(feature.get('wiki', []))

    required = required_automatic_ids()
    for case in sorted(required - automatic):
        errors.append(f'F3 自动断言未登记: {case}')
    code = '\n'.join(p.read_text(encoding='utf-8-sig') for p in (ROOT / 'DebugAndTools').glob('F3GameplayValidation*.cs'))
    for case in sorted(automatic - required):
        if case not in code:
            errors.append(f'自动用例未在 F3 源码找到: {case}')

    # Wiki 的功能条目逐条对账；总览/攻略/历史版本不是独立玩法。
    catalog = list(csv.DictReader((ROOT / 'WikiContent/catalog.tsv').read_text(encoding='utf-8-sig').splitlines(), delimiter='\t'))
    required_wiki = {row['entryId'] for row in catalog
                     if row['categoryId'] not in {'tips', 'changelog', '_wiki_link'}
                     and (not row['entryId'].endswith('__overview') or row['categoryId'] in {'map', 'config'})}
    all_wiki = {row['entryId'] for row in catalog}
    for entry in sorted(required_wiki - wiki):
        errors.append(f'Wiki 游戏功能没有测试场景: {entry}')
    for entry in sorted(wiki - all_wiki):
        errors.append(f'覆盖清单引用不存在的 Wiki 条目: {entry}')

    # 用真实编译清单发现新增模块；细分 Integration/NPC/新武器/Common，避免父目录吞掉新功能。
    # 清单解析走共用的 tools/compile_list.py：这里原本自带的 `^echo\(...\.cs$` 正则看不见清单里
    # 残留的 `^` 续行写法（少 4 个文件），新模块若以那种写法登记就会整块漏掉。
    compiled = read_compile_sources()
    domains = set()
    for name in compiled:
        parts = name.replace('\\', '/').split('/')
        if len(parts) < 2:
            continue  # 根级聚合 partial 的玩法由模块入口覆盖，新增根模块需人工审查。
        depth = 1
        if parts[0] in {'Integration', 'Common'} and len(parts) > 2:
            depth = 2
        if parts[:2] in [['Integration', 'NPCs'], ['Integration', 'NewWeapons']] and len(parts) > 3:
            depth = 3
        domain = '/'.join(parts[:depth])
        if domain != 'Integration':
            domains.add(domain)
    for domain in sorted(domains):
        if not any(s == domain or s.startswith(domain + '/') for s in sources):
            errors.append(f'生产模块缺少功能测试映射: {domain}')
    return errors


def expand_case(case):
    if case == 'RANDOM_EVENT_*':
        source = (ROOT / 'RandomEvents/RandomEventModels.cs').read_text(encoding='utf-8-sig')
        block = source.split('enum RandomEventId', 1)[1].split('}', 1)[0]
        return ['RANDOM_EVENT_' + key.upper() for key in re.findall(r'^\s*(\w+)\s*=\s*\d+', block, re.M) if key != 'None']
    if case == 'MAP_TOUR_*':
        # 地图选择器的清单：游戏侧 MapSpawnPointRegistry 读 Assets/SpawnPoints/*.json、按 sortOrder 排序；
        # 用例 id 与 F3MapTourJudges.CaseId 同口径（场景名大写，非字母数字换成下划线）。
        rows = []
        for path in sorted((ROOT / 'Assets/SpawnPoints').glob('*.json')):
            row = json.loads(path.read_text(encoding='utf-8-sig'))
            if row.get('sceneName') and row.get('sceneID') and row.get('spawnPoints'):
                rows.append((row.get('sortOrder', 0), row['sceneName']))
        return ['MAP_TOUR_' + re.sub(r'[^A-Za-z0-9]', '_', name).upper() for _, name in sorted(rows)] or [case]
    if case == 'ITEM_FACTORY_*':
        # 离线报告不能猜测本轮 DLL 的运行时注册表；在清单中保留逐 ID 审核要求。
        return [case]
    if case == 'SKY_AUTOTEST_*':
        # 全自动实机验收的步骤 id 是数据（步骤表），游戏侧 ExpandAutotestCoverage 读的是同一份表。
        path = ROOT / 'Assets/Data/SkyIslandAutotest.json'
        if not path.is_file():
            return [case]
        table = json.loads(path.read_text(encoding='utf-8-sig'))
        return [step['id'] for step in table.get('steps', [])]
    return [case]


def read_log(log_path):
    """只读取最后一轮；兼容独立报告与带 Unity 前缀的 Player.log，FAIL 不被重试抹掉。"""
    raw = log_path.read_text(encoding='utf-8-sig', errors='replace').splitlines()
    lines = []
    for line in raw:
        marker = '[BossRushValidation] '
        if marker in line:
            line = line.split(marker, 1)[1]
        if line.startswith('BossRush 完整玩法验收 | runId='):
            lines = []
        lines.append(line)
    outcomes, failures, runtime_errors, diagnostics = {}, [], [], []
    summary, mvid, run_id, recovery = None, None, None, False
    for line in lines:
        match = re.match(r'^([A-Z0-9_]+) \| (PASS|FAIL|SKIP|WARN) \|', line)
        if match:
            case, outcome = match.groups()
            if outcomes.get(case) != 'FAIL':
                outcomes[case] = outcome
            if outcome == 'FAIL':
                failures.append(line)
        if line.startswith('SUMMARY | '):
            summary = line.split(' | ', 2)[1]
        if line.startswith('BUILD | mvid='):
            mvid = line.split('=', 1)[1].strip()
        if line.startswith('BossRush 完整玩法验收 | runId='):
            run_id = line.split('runId=', 1)[1].split(' | ', 1)[0]
        if line.startswith('RUNTIME_ERROR | '):
            runtime_errors.append(line)
        if line.startswith('RUNTIME_DIAGNOSTIC | '):
            diagnostics.append(line)
        if line.startswith('RECOVERY | CANCELLED'):
            recovery = True
    return dict(outcomes=outcomes, failures=failures, summary=summary, mvid=mvid,
                run_id=run_id, runtime_errors=runtime_errors, diagnostics=diagnostics, recovery=recovery)


def analyze(data, log_path, expected_mvid=None):
    parsed = read_log(log_path)
    outcomes = parsed['outcomes']
    expected = set()
    for feature in data['features']:
        for group in feature['automatic']:
            if group == 'ITEM_FACTORY_*':
                # ITEM_FACTORY_ALL 同时断言发布注册表数量；不能拿一个 ID 冒充全表。
                expected.add('ITEM_FACTORY_ALL')
                expected.update(k for k in outcomes if k.startswith('ITEM_FACTORY_'))
            else:
                expected.update(expand_case(group))
    not_passed = sorted(case for case in expected if outcomes.get(case) != 'PASS')
    failed = sorted(case for case, outcome in outcomes.items() if outcome == 'FAIL')
    reasons = []
    status = parsed['summary'] or 'INCOMPLETE'
    if parsed['recovery']:
        status = 'CANCELLED'
        reasons.append('上次运行中断后恢复，不能视为完整验收')
    elif status in {'PASS', 'INCOMPLETE'}:
        if failed:
            status = 'FAIL'
            reasons.append('逐项 FAIL 优先于 SUMMARY PASS')
        elif not_passed:
            status = 'INCOMPLETE'
            reasons.append('当前清单有未通过/未执行自动项，旧日志不能证明新增流程')
    if not parsed['summary']:
        reasons.append('缺少 SUMMARY，可能中断或仍在运行')
    if not parsed['mvid'] or not parsed['run_id']:
        if status == 'PASS':
            status = 'INCOMPLETE'
        reasons.append('缺少本轮 DLL 标识或运行 ID')
    if expected_mvid and (parsed['mvid'] or '').lower() != expected_mvid.lower():
        status = 'BUILD_MISMATCH'
        reasons.append('日志中的 DLL 与待验 DLL 不同，请重启游戏后重跑')
    if status == 'PASS' and any('source=BossRush' in line for line in parsed['runtime_errors']):
        status = 'FAIL'
        reasons.append('记录到 BossRush 运行时异常')
    return dict(status=status, run_id=parsed['run_id'], mvid=parsed['mvid'],
                reported_summary=parsed['summary'], failed_ids=failed,
                auto_not_passed=not_passed,
                skipped_ids=sorted(k for k, v in outcomes.items() if v == 'SKIP'),
                warning_ids=sorted(k for k, v in outcomes.items() if v == 'WARN'),
                runtime_error_samples=len(parsed['runtime_errors']), reasons=reasons,
                needs_log_review=bool(parsed['runtime_errors'] or parsed['diagnostics']
                                      or any(v == 'WARN' for v in outcomes.values())),
                diagnostic_details=parsed['diagnostics'],
                manual_pending=sum(len(f['manual']) for f in data['features']),
                failure_details=parsed['failures'])


def render(data, log_path=None):
    outcomes = read_log(log_path)['outcomes'] if log_path else {}
    lines = ['# 游戏内全功能验收清单', '',
        '兼容分类：COMPAT（测试与报告扩充）。登记覆盖不等于实机通过；旧日志只证明旧 DLL 实际执行的断言。', '',
        '前提：Dev 构建、基地、明确标记的专用测试档。资产/中断/恢复用可丢弃副本；每条记录 DLL、地图、语言、槽位、结果和日志/截图。', '',
        f'功能领域：{len(data["features"])}；人工场景：{sum(len(f["manual"]) for f in data["features"])}。', '',
        f'自动证据：{log_path if log_path else "无，所有用例尚未执行"}。', '',
        'F3 会在 BossRushTestReports 生成同名 .coverage.md，按当前 DLL 的注册表逐个展开物品；阶段更新会覆盖该文件，人工结果另存副本。', '',
        '人工结果使用 PASS / FAIL / BLOCKED / NOT_RUN，附原因与证据；未填写始终为 MANUAL_PENDING。']
    for feature in data['features']:
        lines += ['', f'## {feature["id"]} · {feature["title"]}', '',
                  '源码：' + '、'.join('`' + s + '`' for s in feature['sources']), '']
        for group in feature['automatic']:
            cases = expand_case(group)
            if group == 'ITEM_FACTORY_*':
                cases = sorted(k for k in outcomes if k.startswith('ITEM_FACTORY_') and k != 'ITEM_FACTORY_ALL') or cases
            for case in cases:
                lines.append(f'- 自动 `{case}`：**{outcomes.get(case, "NOT_RUN")}**')
        for case in feature['manual']:
            lines += ['', f'- [ ] `{case["id"]}` **MANUAL_PENDING**',
                      f'  - 操作：{case["steps"]}', f'  - 预期：{case["expected"]}', '  - 结果 / 证据：待填']
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--log', type=Path, help='已有 F3 日志；不修改日志或存档')
    parser.add_argument('--output', type=Path, help='输出 Markdown；省略时仅检查清单')
    parser.add_argument('--analyze', action='store_true', help='判断最后一轮日志，输出简明 JSON；需要 --log')
    parser.add_argument('--expected-mvid', help='对照待验 DLL 的 MVID，拒绝旧构建证据')
    args = parser.parse_args()
    data = load_manifest()
    errors = validate(data)
    if errors:
        print('\n'.join(errors))
        return 1
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(render(data, args.log), encoding='utf-8')
    if args.analyze:
        if not args.log:
            parser.error('--analyze requires --log')
        result = analyze(data, args.log, args.expected_mvid)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if result['status'] == 'PASS' else 2
    print(f'GameplayCoverage: PASS ({len(data["features"])} domains, {sum(len(f["manual"]) for f in data["features"])} manual cases)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
