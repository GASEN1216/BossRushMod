# -*- coding: utf-8 -*-
"""验证潮闸镇实包中已确认外露共面层的分离；不等于 GPU 无闪烁验收。

按生产修复工具的明确节点、全部已确认高度与真实顶点复算；缺私有包记 PARTIAL。
作者布局改变后须重新审查目标清单，不可跳过缺失节点或只读取旧报告。
"""
import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle', type=Path, default=ROOT / 'Assets/arenas/tidegate_raid')
    args = parser.parse_args()
    if not args.bundle.is_file():
        print('TidegateSurfaceRepairPropertyTest: PARTIAL 缺本地潮闸镇场景包')
        return 2
    sys.path.insert(0, str(ROOT / 'tools'))
    import UnityPy
    from repair_tidegate_surfaces import evaluate
    try:
        _, states = evaluate(UnityPy.load(str(args.bundle)), False)
        errors = [s['path'] for s in states if s['state'] != 'fixed']
        for path in errors:
            print('FAIL 未分离的外露共面层：' + path)
        print('TidegateSurfaceRepairPropertyTest: ' + ('FAIL' if errors else 'PASS') +
              ' (' + str(len(states)) + ' 个真实节点；全部目标高度)')
        return 1 if errors else 0
    except Exception as error:
        print('TidegateSurfaceRepairPropertyTest: FAIL ' + str(error))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
