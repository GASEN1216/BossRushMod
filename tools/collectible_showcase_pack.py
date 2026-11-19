"""收集品陈列模型 · 第 2 步（CPython）：GLB 颜色贴图 → DXT1 + mip 链，与第 1 步的网格合成 .brcm。

.brcm 布局（小端，运行时 Integration/Collectibles/CollectibleShowcaseModels.cs 读）：
    'BRCM' | int version=1
    网格段：int vertexCount | int indexCount | int indexBits(16/32) | float min[3] | float max[3]
            | uint16 位置×3（在包围盒内归一化）| int8 法线×3 + 1 字节填充 | uint16 UV×2（0..1）
            | 索引（16 位时补齐到 4 字节）
    贴图段：int width | int height | int mipCount | int format(1 = DXT1) | int dataBytes | DXT1 数据（各级 mip 依次排列）
网格量化到每顶点 14 字节（第 1 步写的是 32 字节浮点）：位置误差在 0.01 mm 量级，100 件总量少四成。

贴图在离线阶段压成 DXT1：运行时 Texture2D.LoadRawTextureData 直接交给显卡，不在游戏里解码图片，
也不留 CPU 像素副本。Unity 的原始贴图数据第一行是图像最下面一行，所以编码前先上下翻转。
DXT1 编码用每 4×4 块主成分方向取两端点（与常见快速编码器同一思路），对手绘风格贴图足够。

用法：python tools/collectible_showcase_pack.py --plan <plan.json> --glb-dir <dir> --mesh-dir <dir> --out-dir <dir> [--only key]
"""

import argparse
import io
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image

TEX_BY_QUALITY = {8: 1024, 7: 1024, 6: 1024, 5: 1024}
DEFAULT_TEX = 512

# Tripo 表现不了透明玻璃：开了去光处理的玻璃件会出成不透明的深色（免费重试结果相同，2026-11-19 实测）。
# 个别件在这里把暗部抬成冷调烟灰，亮部（骨架、金属托）不动，让它接近图标里雾蒙蒙的玻璃。
# (阈值 V, 烟色 RGB 0..1, 混合强度)
TEX_LIFT_DARK = {
    'arena_soul_orb': (0.42, (0.46, 0.47, 0.53), 0.75),
}


def lift_dark(img, threshold, smoke, strength):
    arr = np.asarray(img, dtype=np.float32) / 255.0
    value = arr.max(axis=-1, keepdims=True)
    weight = np.clip((threshold - value) / 0.18, 0.0, 1.0) * strength
    target = np.array(smoke, np.float32)[None, None, :] + arr * 0.55
    out = arr + weight * (target - arr)
    return Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB')


def glb_base_color(path):
    data = Path(path).read_bytes()
    if data[:4] != b'glTF':
        raise ValueError('不是 GLB')
    json_len = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + json_len])
    bin_off = 20 + json_len + 8
    mats = doc.get('materials') or []
    tex_index = None
    for m in mats:
        pbr = m.get('pbrMetallicRoughness') or {}
        if 'baseColorTexture' in pbr:
            tex_index = pbr['baseColorTexture']['index']
            break
    if tex_index is None:
        raise ValueError('没有颜色贴图')
    image = doc['images'][doc['textures'][tex_index]['source']]
    view = doc['bufferViews'][image['bufferView']]
    start = bin_off + view.get('byteOffset', 0)
    return Image.open(io.BytesIO(data[start:start + view['byteLength']])).convert('RGB')


def to565(c):
    r = np.clip(np.round(c[..., 0] * 31 / 255), 0, 31).astype(np.uint16)
    g = np.clip(np.round(c[..., 1] * 63 / 255), 0, 63).astype(np.uint16)
    b = np.clip(np.round(c[..., 2] * 31 / 255), 0, 31).astype(np.uint16)
    return (r << 11) | (g << 5) | b


def from565(v):
    r = ((v >> 11) & 31).astype(np.float32) * 255 / 31
    g = ((v >> 5) & 63).astype(np.float32) * 255 / 63
    b = (v & 31).astype(np.float32) * 255 / 31
    return np.stack([r, g, b], axis=-1)


def encode_dxt1(img):
    """img: HxWx3 uint8（已翻转成 Unity 行序），H、W 为 4 的倍数。返回块数据 bytes。"""
    h, w, _ = img.shape
    blocks = img.reshape(h // 4, 4, w // 4, 4, 3).transpose(0, 2, 1, 3, 4).reshape(-1, 16, 3).astype(np.float32)
    mean = blocks.mean(axis=1, keepdims=True)
    centered = blocks - mean
    cov = np.einsum('npi,npj->nij', centered, centered)
    axis = np.ones((blocks.shape[0], 3), np.float32)
    for _ in range(8):
        axis = np.einsum('nij,nj->ni', cov, axis)
        axis /= np.maximum(np.linalg.norm(axis, axis=1, keepdims=True), 1e-6)
    proj = np.einsum('npi,ni->np', centered, axis)
    lo = mean[:, 0] + axis * proj.min(axis=1, keepdims=True)
    hi = mean[:, 0] + axis * proj.max(axis=1, keepdims=True)
    c0 = to565(np.clip(hi, 0, 255))
    c1 = to565(np.clip(lo, 0, 255))
    swap = c0 < c1
    c0, c1 = np.where(swap, c1, c0), np.where(swap, c0, c1)
    p0, p1 = from565(c0), from565(c1)
    palette = np.stack([p0, p1, (2 * p0 + p1) / 3, (p0 + 2 * p1) / 3], axis=1)  # n,4,3
    equal = c0 == c1
    dist = ((blocks[:, :, None, :] - palette[:, None, :, :]) ** 2).sum(axis=-1)  # n,16,4
    idx = dist.argmin(axis=-1).astype(np.uint32)
    idx[equal] = 0
    shifts = (np.arange(16, dtype=np.uint32) * 2)[None, :]
    bits = (idx << shifts).sum(axis=1).astype(np.uint32)
    out = np.zeros(blocks.shape[0], dtype=[('c0', '<u2'), ('c1', '<u2'), ('bits', '<u4')])
    out['c0'], out['c1'], out['bits'] = c0, c1, bits
    return out.tobytes()


def build_texture(img, size):
    base = img.resize((size, size), Image.LANCZOS)
    levels, level, s = [], base, size
    while True:
        arr = np.asarray(level.transpose(Image.FLIP_TOP_BOTTOM), dtype=np.uint8)
        if s < 4:
            padded = np.zeros((4, 4, 3), np.uint8)
            padded[:s, :s] = arr
            padded[s:, :] = padded[s - 1:s, :] if s > 0 else 0
            padded[:, s:] = padded[:, s - 1:s]
            arr = padded
        levels.append(encode_dxt1(arr))
        if s == 1:
            break
        s //= 2
        level = level.resize((s, s), Image.LANCZOS)
    return levels


def quantize_mesh(raw):
    """第 1 步的 'BRMS' 浮点网格 → .brcm 网格段（量化）。"""
    if raw[:4] != b'BRMS':
        raise ValueError('网格文件头不对')
    version, vc, ic, bits = struct.unpack_from('<iiii', raw, 4)
    mn = np.array(struct.unpack_from('<3f', raw, 20), np.float32)
    mx = np.array(struct.unpack_from('<3f', raw, 32), np.float32)
    p = 44
    pos = np.frombuffer(raw, np.float32, vc * 3, p).reshape(vc, 3); p += vc * 12
    nrm = np.frombuffer(raw, np.float32, vc * 3, p).reshape(vc, 3); p += vc * 12
    uv = np.frombuffer(raw, np.float32, vc * 2, p).reshape(vc, 2); p += vc * 8
    idx = np.frombuffer(raw, np.uint32 if bits == 32 else np.uint16, ic, p)
    span = np.maximum(mx - mn, 1e-6)
    qpos = np.clip(np.round((pos - mn) / span * 65535), 0, 65535).astype('<u2')
    n = nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-6)
    qnrm = np.zeros((vc, 4), np.int8)
    qnrm[:, :3] = np.clip(np.round(n * 127), -127, 127).astype(np.int8)
    if uv.min() < -0.01 or uv.max() > 1.01:
        raise ValueError('UV 超出 0..1：%.3f..%.3f' % (uv.min(), uv.max()))
    quv = np.clip(np.round(uv * 65535), 0, 65535).astype('<u2')
    out = struct.pack('<iii', vc, ic, bits) + struct.pack('<6f', *mn, *mx)
    out += qpos.tobytes() + qnrm.tobytes() + quv.tobytes() + idx.astype('<u4' if bits == 32 else '<u2').tobytes()
    if bits == 16 and ic % 2:
        out += b'\0\0'
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--plan', required=True)
    ap.add_argument('--glb-dir', required=True)
    ap.add_argument('--mesh-dir', required=True)
    ap.add_argument('--out-dir', required=True)
    ap.add_argument('--only', default=None)
    args = ap.parse_args()
    plan = json.loads(Path(args.plan).read_text(encoding='utf-8'))
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    rows, failures = [], 0
    for entry in plan:
        key = entry['key']
        if args.only and key != args.only:
            continue
        glb = Path(args.glb_dir) / (Path(entry['file']).stem + '.glb')
        mesh = Path(args.mesh_dir) / (key + '.mesh.bin')
        if not glb.exists() or not mesh.exists():
            continue
        try:
            size = TEX_BY_QUALITY.get(entry['quality'], DEFAULT_TEX)
            base = glb_base_color(glb)
            if key in TEX_LIFT_DARK:
                base = lift_dark(base, *TEX_LIFT_DARK[key])
            levels = build_texture(base, size)
            mesh_bytes = quantize_mesh(mesh.read_bytes())
            tex_bytes = b''.join(levels)
            with open(out_dir / (key + '.brcm'), 'wb') as f:
                f.write(b'BRCM')
                f.write(struct.pack('<i', 1))
                f.write(mesh_bytes)
                f.write(struct.pack('<iiiii', size, size, len(levels), 1, len(tex_bytes)))
                f.write(tex_bytes)
            rows.append((key, size, len(levels), (out_dir / (key + '.brcm')).stat().st_size))
            print('PACK_OK', key, size, len(levels), rows[-1][3])
        except Exception as e:
            failures += 1
            print('PACK_ERR', key, e)
    print('PACK_PASS %d' % len(rows) if not failures else 'PACK_FAIL %d' % failures)


if __name__ == '__main__':
    main()
