#!/usr/bin/env python3
"""头盔佩戴校准须贯穿导入、生成和打包；作者工程缺席时明确报告 PARTIAL。"""
import ast
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from helmet_fit import check_project, load_profiles
from unity_project_path import find_unity_project, describe_missing


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def method(source, name):
    source = clean_source(source)
    match = re.search(r'\b(?:public|private)\s+static\s+\w+\s+' + name + r'\([^)]*\)\s*\{', source)
    require(match is not None, '缺少方法：' + name)
    depth = 1
    for i in range(match.end(), len(source)):
        if source[i] == '{':
            depth += 1
        elif source[i] == '}':
            depth -= 1
            if depth == 0:
                return source[match.end():i]
    raise AssertionError('方法体未闭合：' + name)


def before(body, first, second, label):
    a, b = re.search(first, body), re.search(second, body)
    require(a is not None and b is not None and a.start() < b.start(), label)


def main():
    table = load_profiles()
    source = ast.parse((ROOT / 'tools/sky_island_boss_gear_import.py').read_text(encoding='utf-8'))
    entry = next(n for n in source.body if isinstance(n, ast.FunctionDef) and n.name == 'main')
    calls = [n for n in ast.walk(entry) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)]
    staging = [n for n in calls if n.func.id == 'validate_staging_bounds']
    require(len(staging) == 1 and [ast.unparse(n) for n in staging[0].args] ==
            ['base', 'slot', 'bounds', 'fit_profiles'], '导入 main 必须校验实际标准化结果')
    normalise = next(n for n in calls if n.func.id == 'normalise_equipment')
    export = next(n for n in calls if n.func.id == 'export_fbx')
    require(normalise.lineno < staging[0].lineno < export.lineno, '须先标准化并校验，再导出 FBX')
    project = find_unity_project()
    if not project:
        print('[PARTIAL] Python 校准表及导入接线通过；Unity 生成/打包接线与预制体未验证：' + describe_missing())
        return
    project = Path(project)
    editor = project / 'Assets/Editor'
    read = lambda name: (editor / name).read_text(encoding='utf-8-sig')
    body = method(read('SkyIslandBossGearBundleBuilder.cs'), 'CreateModelPrefab')
    before(body, r'BossRush\.HelmetFitUtility\.ApplyToGeneratedModel\(root,\s*piece\.BaseName\);',
           r'PrefabUtility\.SaveAsPrefabAsset\(root,\s*path\)', '生成头盔必须在保存前应用校准')
    body = method(read('DuckovBundleBuilder.cs'), 'BuildAllBundles')
    before(body, r'BossRush\.HelmetFitUtility\.ValidateAll\(\);', r'BuildPipeline\.BuildAssetBundles\(',
           '全量构建必须先检查头盔姿态')
    body = method(read('HelmetFitBundleBuilder.cs'), 'ApplyAndBuild')
    before(body, r'HelmetFitUtility\.ApplyPrefabs\(bundles\);', r'HelmetFitUtility\.ValidateAll\(\);',
           '头盔构建必须先应用再验证')
    before(body, r'HelmetFitUtility\.ValidateAll\(\);', r'BuildPipeline\.BuildAssetBundles\(',
           '头盔校验必须发生在打包前')
    require(re.search(r'HelmetFitUtility\.Validate\(bundle\.LoadAsset<GameObject>\(', body),
            '构建必须回读包内姿态')
    body = method(read('ThunderHelmetPilotBuilder.cs'), 'BuildOnly')
    require(re.search(r'HelmetFitBundleBuilder\.ApplyAndBuild\(new\[\]\s*\{\s*"thunder_set"\s*\}\);', body),
            '雷霆旧入口必须复用统一构建器')
    require('localPosition' not in body and 'localScale' not in body and 'Quaternion.Euler' not in body,
            '雷霆旧入口不得重新写死姿态')
    check_project(project, table)
    print('PASS: HelmetFitWiringGuard（导入、生成、打包及作者工程全部头盔）')


if __name__ == '__main__':
    try:
        main()
    except (AssertionError, ValueError, OSError, StopIteration) as error:
        print('FAIL: HelmetFitWiringGuard:', error)
        sys.exit(1)
