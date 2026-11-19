"""校验收集品陈列模型 .brcm：与运行时 CollectibleShowcaseModels.Parse 同一份布局，逐字节读到文件末尾。

检查：文件头、版本；顶点数 / 索引数范围；索引不越界；包围盒合理（底面在 y=0，最长边不超过 0.7 m）；
DXT1 各级 mip 字节数之和等于声明的数据长度；文件恰好读完。
用法：python tools/verify_collectible_showcase_models.py [目录]（默认 Assets/Collectibles/Models）
"""

import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
MAX_EXTENT = 0.7
MAX_VERTS = 65535 * 2


def dxt1_bytes(w, h, mips):
    total = 0
    for i in range(mips):
        bw = max(1, ((w >> i) + 3) // 4) if (w >> i) > 0 else 1
        bh = max(1, ((h >> i) + 3) // 4) if (h >> i) > 0 else 1
        total += bw * bh * 8
    return total


def check(path):
    d = path.read_bytes()
    if d[:4] != b'BRCM':
        return 'bad magic'
    version = struct.unpack_from('<i', d, 4)[0]
    if version != 1:
        return 'version %d' % version
    vc, ic, bits = struct.unpack_from('<iii', d, 8)
    if not (3 <= vc <= MAX_VERTS) or ic <= 0 or ic % 3 or bits not in (16, 32):
        return 'counts vc=%d ic=%d bits=%d' % (vc, ic, bits)
    if bits == 16 and vc > 65535:
        return '16 位索引放不下 %d 个顶点' % vc
    mn = struct.unpack_from('<3f', d, 20)
    mx = struct.unpack_from('<3f', d, 32)
    p = 44 + vc * 6 + vc * 4 + vc * 4
    idx = np.frombuffer(d, np.uint32 if bits == 32 else np.uint16, ic, p)
    if int(idx.max()) >= vc:
        return '索引越界 %d >= %d' % (idx.max(), vc)
    p += ic * (4 if bits == 32 else 2)
    if bits == 16 and ic % 2:
        p += 2
    if abs(mn[1]) > 1e-3:
        return '底面不在 y=0：%.4f' % mn[1]
    ext = max(mx[i] - mn[i] for i in range(3))
    if ext > MAX_EXTENT or ext < 0.05:
        return '最长边 %.3f 不在 0.05..%.2f' % (ext, MAX_EXTENT)
    w, h, mips, fmt, tlen = struct.unpack_from('<iiiii', d, p)
    p += 20
    if fmt != 1 or w not in (256, 512, 1024, 2048) or h != w:
        return 'tex %dx%d fmt=%d' % (w, h, fmt)
    if tlen != dxt1_bytes(w, h, mips):
        return 'tex bytes %d != %d' % (tlen, dxt1_bytes(w, h, mips))
    p += tlen
    if p != len(d):
        return '文件长度 %d != 读到 %d' % (len(d), p)
    return None


def main():
    folder = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'Assets' / 'Collectibles' / 'Models'
    files = sorted(folder.glob('*.brcm'))
    bad = 0
    for f in files:
        err = check(f)
        if err:
            bad += 1
            print('  - %s: %s' % (f.name, err))
    print('verify_collectible_showcase_models: %d 件，%s' % (len(files), 'FAIL %d' % bad if bad else 'PASS'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
