#!/usr/bin/env python3
"""头盔校准表校验与 Unity 同步；变换在 Editor 构建器内绝对赋值，不烤进 FBX 两次。"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path, PurePosixPath
import re

from unity_project_path import find_unity_project

PROFILES_PATH = Path(__file__).with_name('helmet_fit_profiles.json')
UNITY_PROFILES = 'Assets/Editor/HelmetFitProfiles.json'


def quaternion(euler):
    """Unity Quaternion.Euler 的 Z、X、Y 应用顺序。"""
    x, y, z = (math.radians(n) / 2 for n in euler)
    sx, cx, sy, cy, sz, cz = math.sin(x), math.cos(x), math.sin(y), math.cos(y), math.sin(z), math.cos(z)
    return (cy*sx*cz + sy*cx*sz, sy*cx*cz - cy*sx*sz,
            cy*cx*sz - sy*sx*cz, cy*cx*cz + sy*sx*sz)


def rotate(q, v):
    x, y, z, w = q
    a, b, c = v
    return ((1-2*(y*y+z*z))*a + 2*(x*y-z*w)*b + 2*(x*z+y*w)*c,
            2*(x*y+z*w)*a + (1-2*(x*x+z*z))*b + 2*(y*z-x*w)*c,
            2*(x*z-y*w)*a + 2*(y*z+x*w)*b + (1-2*(x*x+y*y))*c)


def near(a, b, tolerance=0.0001):
    return len(a) == len(b) and all(math.isfinite(x) and abs(x-y) <= tolerance for x, y in zip(a, b))


def load_profiles(path=PROFILES_PATH):
    data = json.loads(Path(path).read_text(encoding='utf-8'))
    if data.get('schema') != 1 or not isinstance(data.get('profiles'), list) or not data['profiles']:
        raise ValueError('头盔校准表 schema 或 profiles 无效')
    seen = set()
    for p in data['profiles']:
        name = p['baseName']
        if not name.endswith('_Helmet') or name in seen:
            raise ValueError('重复或无效的头盔基名：' + name)
        seen.add(name)
        asset = PurePosixPath(p['prefab'])
        if asset.is_absolute() or '..' in asset.parts or not p['prefab'].startswith('Assets/') or asset.name != name + '_Model.prefab':
            raise ValueError('预制体路径不属于当前头盔：' + name)
        if not isinstance(p['referenceOnly'], bool) or not re.fullmatch(r'[a-z0-9_]+', p['bundle']):
            raise ValueError('referenceOnly 或 bundle 无效：' + name)
        for key in ('position', 'rotationEuler', 'scale', 'meshCenter', 'meshSize', 'modelUp', 'modelForward'):
            values = p[key]
            if len(values) != 3 or any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
                raise ValueError('非法向量：' + name + '/' + key)
        if min(p['scale']) <= 0 or min(p['meshSize']) <= 0:
            raise ValueError('尺寸和缩放必须为正：' + name)
        q = quaternion(p['rotationEuler'])
        if not near(rotate(q, p['modelUp']), (0, 1, 0)) or not near(rotate(q, p['modelForward']), (0, 0, 1)):
            raise ValueError('校准后上方/正面方向错误：' + name)
    return data


def validate_staging_bounds(base_name, slot, bounds, profiles):
    """导入阶段只做标准化；尺寸改变时要求重校准，不能沿用旧姿态直接发包。"""
    if slot != 'Helmat':
        return
    profile = next((p for p in profiles['profiles'] if p['baseName'] == base_name), None)
    if profile is None:
        raise ValueError('新头盔须先登记校准表：' + base_name)
    lo, hi = bounds['min'], bounds['max']  # Blender (X,Z,-Y) -> Unity (X,Y,Z)
    center = ((lo[0]+hi[0])/2, (lo[2]+hi[2])/2, -(lo[1]+hi[1])/2)
    if not near(bounds['widthHeightDepth'], profile['meshSize'], 0.005) or not near(center, profile['meshCenter'], 0.005):
        raise ValueError('头盔标准化尺寸/中心变化，请重新校准后更新 helmet_fit_profiles.json：' + base_name)


def check_project(project, profiles):
    project = Path(project)
    expected = {p['prefab'] for p in profiles['profiles']}
    found = {p.relative_to(project).as_posix() for p in (project / 'Assets').rglob('*_Helmet_Model.prefab')}
    if found != expected:
        raise ValueError('作者工程头盔覆盖不完整：' + str(sorted(found ^ expected)))
    for p in profiles['profiles']:
        text = (project / p['prefab']).read_text(encoding='utf-8')
        root = text.split('--- !u!1001', 1)[0]
        for field, wanted in (('m_LocalPosition', (0, 0, 0)), ('m_LocalScale', (1, 1, 1)), ('m_LocalRotation', (0, 0, 0, 1))):
            match = re.search(r'^  ' + field + r': \{([^}]+)\}', root, re.M)
            if not match or not near([float(v) for v in re.findall(r':\s*([-+\d.eE]+)', match[1])], wanted):
                raise ValueError('模型根节点不是单位变换：' + p['baseName'])
        fields = re.findall(r'propertyPath: (m_Local\S+)\s+value: ([^\r\n]+)', text)
        if len({k for k, _ in fields}) != len(fields):
            raise ValueError('存在多个模型 Transform 覆盖，需要人工指定网格节点：' + p['baseName'])
        values = {k: float(v) for k, v in fields}
        for key, field, default in (('position', 'm_LocalPosition', 0), ('scale', 'm_LocalScale', 1)):
            actual = [values.get(field+'.'+axis, default) for axis in 'xyz']
            if not near(actual, p[key]):
                raise ValueError('预制体姿态与校准表不一致：' + p['baseName'] + '/' + key)
        actual = [values.get('m_LocalRotation.'+axis, 1 if axis == 'w' else 0) for axis in 'xyzw']
        desired = quaternion(p['rotationEuler'])
        if not near(actual, desired) and not near(actual, [-v for v in desired]):
            raise ValueError('预制体旋转与校准表不一致：' + p['baseName'])
    copied = project / UNITY_PROFILES
    if not copied.is_file() or json.loads(copied.read_text(encoding='utf-8')) != profiles:
        raise ValueError('Unity 校准表未同步，先运行 python tools/helmet_fit.py --sync')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', help='默认由 unity_project_path.py 解析')
    parser.add_argument('--sync', action='store_true', help='同步校准表，不直接改预制体')
    parser.add_argument('--check', action='store_true', help='核对全部头盔覆盖与预制体姿态')
    args = parser.parse_args()
    profiles = load_profiles()
    if args.sync or args.check:
        project = args.project or find_unity_project()
        if not project:
            raise SystemExit('找不到 Unity 作者工程，请设置 BOSSRUSH_UNITY_PROJECT')
        if args.sync:
            destination = Path(project) / UNITY_PROFILES
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(PROFILES_PATH.read_bytes())
            print('SYNC:', destination)
        if args.check:
            check_project(project, profiles)
    print('HELMET_FIT_OK:', len(profiles['profiles']), 'profiles')


if __name__ == '__main__':
    main()
