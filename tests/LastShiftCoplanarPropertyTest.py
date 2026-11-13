"""复算编组站生产共面检测的小面、窄边、对边、隔层和网格边界。"""
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from build_lastshift_blend import _zf_overlap_pairs  # noqa: E402


def horizontal(x, y, z, size):
    return [(x, y, z), (x + size, y, z), (x, y + size, z)]


def main():
    cases = [
        ('小于旧单面面积门槛', horizontal(0.01, 0.01, 0, 0.199), horizontal(0, 0, 0, 0.373), True),
        ('小于旧交集面积门槛且绕序相反', horizontal(0.22, 0.0, 0, 0.2), horizontal(0, 0, 0, 0.3)[::-1], True),
        ('只接触一条边', horizontal(0.3, 0, 0, 0.2), horizontal(0, 0, 0, 0.3), False),
        ('超过共面间距', horizontal(0, 0, 0.011, 0.3), horizontal(0, 0, 0, 0.3), False),
        ('平面与空间分桶边界', horizontal(0, 0, -0.001, 0.3), horizontal(0, 0, 0.001, 0.3), True),
    ]
    errors = []
    for label, a, b, expected in cases:
        triangles = np.asarray([a, b])
        pairs, _ = _zf_overlap_pairs(triangles[:, 0], triangles[:, 1], triangles[:, 2], np.array([0, 1]))
        if bool(pairs) != expected:
            errors.append(f'{label}：期望重叠={expected}，实际 {dict(pairs)}')
    for error in errors:
        print(error)
    if errors:
        return 1
    print(f'LastShiftCoplanarPropertyTest: PASS ({len(cases)} cases)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
