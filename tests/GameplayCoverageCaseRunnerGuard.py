#!/usr/bin/env python3
"""F3 用例外壳必须被覆盖工具认得出来（2026-09-14）。

`tools/gameplay_coverage.py` 用一张「用例外壳」名单（`CASE_RUNNERS`）从 F3 源码里抽用例 id，再要求每一个都登记进
`Assets/Data/GameplayCoverage.json`。名单漏了哪个外壳，经它跑的用例就**存在但不被要求登记**——
2026-09-14 之前天空岛的 `RunSkyIslandSync` / `RunSkyIslandCase` 正是这样：岛内 28 条只是碰巧都手工登记了，
新加一条忘了登记，`GameplayValidationCoverageGuard` 照样全绿。

钉住三件事：
1. F3 源码里每一个第一个参数是 `string id` / `string caseId` 的方法，要么在 `CASE_RUNNERS` 里，
   要么在下面的 `NOT_CASE_RUNNERS` 里写明它的 id 为什么不是用例 id；名单里的名字也必须还存在。
2. `CASE_RUNNERS` 里每个外壳都真的抽得出来（合成调用反向验证正则）；现有用例全部已登记。
3. 端到端：往真实源码里塞一条没登记的天空岛用例（同步、协程各一），必须被报出来；
   把名单退回 2026-09-14 之前的样子，岛内新用例必须「看不见」——证明这次登记是承重的。
"""
import importlib.util
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('gameplay_coverage', ROOT / 'tools/gameplay_coverage.py')
coverage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage)

# 第一个参数叫 id / caseId、但不产生「这条用例的结论」的方法，逐个写理由。
NOT_CASE_RUNNERS = {
    'Record': '最底层的记账函数：固定字面量 Record("X") 由 case_ids_in 单独抽，'
              '拼接出来的诊断 id（caseId + "_UNHANDLED" 等）刻意不要求登记',
    'EnsureArenaForCase': '只为某条用例把竞技场准备好：caseId 只进 SCENE_RECOVERY 日志与 _RESTORE_ARENA 场景记录，'
                          '这条用例的结论由调用它的外壳记',
}
WRAPPER = re.compile(r'\bprivate\s+(?:static\s+)?(?:void|IEnumerator|bool)\s+(\w+)\s*\(\s*string\s+(?:id|caseId)\b')
LEGACY_RUNNERS = ('RunSyncCase', 'RunSyncCaseGated', 'RunIsolatedCase', 'VerifyArenaCleanup', 'SamplePerformance',
                  'WaitRuntimeReady')


def main():
    errors = []
    code = coverage.f3_source()
    wrappers = set(WRAPPER.findall(code))
    runners = set(coverage.CASE_RUNNERS)
    for name in sorted(wrappers - runners - set(NOT_CASE_RUNNERS)):
        errors.append('F3 用例外壳 %s 没有登记进 tools/gameplay_coverage.py 的 CASE_RUNNERS：经它跑的用例不会被要求登记' % name)
    for name in sorted(runners - wrappers):
        errors.append('CASE_RUNNERS 里的 %s 在 F3 源码里已经不存在，删掉这一条' % name)
    for name in sorted(set(NOT_CASE_RUNNERS) - wrappers):
        errors.append('NOT_CASE_RUNNERS 里的 %s 在 F3 源码里已经不存在，删掉这一条' % name)
    for name in sorted(runners & set(NOT_CASE_RUNNERS)):
        errors.append('%s 同时在 CASE_RUNNERS 与 NOT_CASE_RUNNERS 里' % name)
    for name in sorted(runners):
        probe_id = 'ZZ_PROBE_CASE_' + name.upper()
        if probe_id not in coverage.case_ids_in('%s("%s", Something);' % (name, probe_id)):
            errors.append('覆盖工具的正则认不出外壳 %s 登记的用例' % name)

    registered = set()
    for feature in coverage.load_manifest()['features']:
        registered.update(feature.get('automatic', []))
    missing_now = sorted(coverage.required_automatic_ids() - registered)
    if missing_now:
        errors.append('现有 F3 用例没有登记进 GameplayCoverage.json：' + ', '.join(missing_now))

    for fake in ('RunSkyIslandSync("SKY_ZZ_UNREGISTERED_SYNC", ValidateSkyIslandZz);',
                 'yield return RunSkyIslandCase("SKY_ZZ_UNREGISTERED_CORO", RunSkyIslandZz);'):
        fake_id = re.search(r'"([A-Z0-9_]+)"', fake).group(1)
        if fake_id not in coverage.case_ids_in(code + '\n' + fake) - registered:
            errors.append('端到端探针失效：没登记的 %s 没有被报出来' % fake_id)

    saved = coverage.CASE_RUNNERS
    try:
        coverage.CASE_RUNNERS = LEGACY_RUNNERS
        if 'SKY_CHOICE_GATES' in coverage.case_ids_in(code):
            errors.append('反向检查失效：名单退回旧样子之后仍抽得到 SKY_CHOICE_GATES，说明它是经别的路径抽到的')
    finally:
        coverage.CASE_RUNNERS = saved

    if errors:
        print('GameplayCoverageCaseRunnerGuard: FAIL')
        for error in errors:
            print('  - ' + error)
        return 1
    print('GameplayCoverageCaseRunnerGuard: PASS（%d 个用例外壳全部登记、%d 个排除写明理由；端到端探针 2 条与反向检查生效）'
          % (len(runners), len(NOT_CASE_RUNNERS)))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
