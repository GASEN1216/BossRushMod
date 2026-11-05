# -*- coding: utf-8 -*-
"""Blender 后台建模：闸伯的竹筏（潮闸镇基地入口的正式模型）。

八根粗细不一、带竹节的竹子用麻绳捆在两道横撑上，船头微翘；船尾一根撑篙斜插进水里，船头立杆挑一盏马灯
（灯罩玻璃单独一个网格，Unity 里用 BossRush/Tidegate/Environment 的 _EmissionColor + _NightGlow 发光）；
筏上一只盖着油布、捆了两道绳的木箱和一卷绳。风格对齐潮闸镇素材库：低多边形、暖色、颜色全在顶点色里。

坐标（Blender）：+Y 船头（马灯一端），-X 靠岸一侧（马灯杆、交互点那一侧），+Z 向上；原点在筏底中心。
FBX 按潮闸镇同一口径 axis_forward='-Z', axis_up='Y' 导出，并把轴向与单位烘进网格（bake_space_transform +
FBX_SCALE_ALL），进 Unity 后节点是单位变换，Unity 本地坐标 = (-x, z, -y)：长边沿 Z、马灯在 -Z 端、马灯杆在 +X 侧。

产物（--out，默认 ArtSource/Tidegate/raft）：
  TidegateRaft.fbx              两个网格：Raft_Body（顶点色）、Raft_LanternGlass（灯罩玻璃）
  TidegateRaft.blend            可编辑源文件
  tidegate_raft_report.json     三角形数、Unity 本地包围盒、马灯玻璃中心（挂点光源用）
  previews/raft_*.png           Workbench 顶点色预览（--render）

用法（后台，Blender 5.2；不碰 owner 开着的 Blender）：
  D:/blender/blender.exe -b --factory-startup --python-exit-code 1 --python tools/build_tidegate_raft.py -- \
      --out ArtSource/Tidegate/raft [--render]
成功时打印 TIDEGATE_RAFT_OK {...}，随后 os._exit(0)。
"""
import argparse
import json
import math
import os
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

TAU = math.pi * 2.0
SEED = 20261105


# ---------------------------------------------------------------- 颜色（sRGB，0..1）
def hexc(h):
    v = int(h.lstrip('#'), 16)
    return ((v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0)


def mul(c, k):
    return tuple(max(0.0, min(1.0, x * k)) for x in c)


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


BAMBOO = [hexc(h) for h in ('#bba366', '#ad9a5b', '#c2aa6c', '#a39558', '#b49c5e', '#b8a062', '#a8975a', '#bea86a')]
BAMBOO_GREEN = hexc('#97955c')
NODE = hexc('#6f5c35')
CUT_RIM = hexc('#dccb98')
CUT_CORE = hexc('#3b3023')
WET = hexc('#5d5a3d')
ROPE = hexc('#b49a6a')
WOOD = hexc('#9a7148')
WOOD_DARK = hexc('#6e4f33')
TARP = hexc('#6b7656')
LANTERN_RED = hexc('#973b2a')
TIN = hexc('#86847b')
IRON = hexc('#38352f')
BRASS = hexc('#ad8a4c')
GLASS = hexc('#ffe4ab')


# ---------------------------------------------------------------- 几何累加器
class Acc:
    """顶点 / 面 / 每面颜色、光滑角阈值（度）与是否随水位变湿。面朝向最后统一重算（全是闭合体或布面）。"""

    def __init__(self):
        self.v, self.f, self.c, self.th, self.wet = [], [], [], [], []

    def vert(self, p):
        self.v.append(Vector(p))
        return len(self.v) - 1

    def face(self, idx, col, th=50.0, wet=False):
        self.f.append(tuple(idx))
        self.c.append(tuple(col))
        self.th.append(th)
        self.wet.append(wet)


def perp(d):
    d = Vector(d).normalized()
    ref = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    return (ref - d * ref.dot(d)).normalized()


def frames(centers):
    """沿折线的平行移动标架：(切线, u, w)。"""
    n = len(centers)
    out = []
    u = None
    for i in range(n):
        t = (centers[min(i + 1, n - 1)] - centers[max(i - 1, 0)]).normalized()
        u = perp(t) if u is None else (u - t * u.dot(t)).normalized()
        out.append((t, u, t.cross(u)))
    return out


def tube(acc, centers, radii, sides, color, caps=('flat', 'flat'), th=50.0, wet=False, twist=0.0,
         rim=None, core=None):
    """沿折线的管子。color(i, j) 给第 i 段第 j 个侧面的颜色；caps 取 'flat' / 'hollow'（竹子断口）/ None。"""
    fr = frames(centers)
    rings = []
    for c, r, (t, u, w) in zip(centers, radii, fr):
        rings.append([acc.vert(c + (u * math.cos(TAU * j / sides + twist) + w * math.sin(TAU * j / sides + twist)) * r)
                      for j in range(sides)])
    for i in range(len(rings) - 1):
        for j in range(sides):
            k = (j + 1) % sides
            acc.face((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]), color(i, j), th, wet)
    for end, kind in ((0, caps[0]), (len(rings) - 1, caps[1])):
        if not kind:
            continue
        ring, center = rings[end], centers[end]
        out_dir = -fr[0][0] if end == 0 else fr[-1][0]
        base = color(0 if end == 0 else len(rings) - 2, 0)
        if kind == 'flat':
            mid = acc.vert(center)
            for j in range(sides):
                acc.face((ring[j], ring[(j + 1) % sides], mid), base, th, wet)
        else:  # 竹子断口：浅色断面一圈 + 往里凹的深色空腔
            rim_col = rim or mix(base, CUT_RIM, 0.65)
            inner = [acc.vert(center + (acc.v[ring[j]] - center) * 0.72 - out_dir * 0.004) for j in range(sides)]
            hole = acc.vert(center - out_dir * 0.035)
            for j in range(sides):
                k = (j + 1) % sides
                acc.face((ring[j], ring[k], inner[k], inner[j]), rim_col, 25.0, wet)
                acc.face((inner[j], inner[k], hole), core or CUT_CORE, 25.0, wet)
    return rings


def loop(acc, points, normal, r, sides, color, th=180.0, wet=False):
    """闭合的绳圈：points 是一个平面上的闭合折线，normal 是该平面法线（做稳定标架，不扭）。"""
    n = len(points)
    N = Vector(normal).normalized()
    rings = []
    for i in range(n):
        t = (points[(i + 1) % n] - points[i - 1]).normalized()
        u = (N - t * N.dot(t)).normalized()
        w = t.cross(u)
        rings.append([acc.vert(points[i] + (u * math.cos(TAU * j / sides) + w * math.sin(TAU * j / sides)) * r)
                      for j in range(sides)])
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        col = color(i)
        for j in range(sides):
            k = (j + 1) % sides
            acc.face((a[j], a[k], b[k], b[j]), col, th, wet)


def ring(acc, center, axis, R, r, seg=8, minor=3, base=ROPE, wet=False, squash=1.0):
    """绕 axis 的一圈麻绳；相邻两段深浅交替，像拧起来的股。"""
    A = Vector(axis).normalized()
    u = perp(A)
    w = A.cross(u)
    pts = [Vector(center) + (u * math.cos(TAU * i / seg) + w * math.sin(TAU * i / seg) * squash) * R for i in range(seg)]
    loop(acc, pts, A, r, minor, lambda i: base if i % 2 == 0 else mul(base, 0.8), wet=wet)


def box(acc, center, size, col, rot=None, th=30.0, shade=True):
    """长方体；shade=True 时顶面亮、侧面略暗、底面最暗（素材库的假 AO 口径）。"""
    cx, cy, cz = center
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    R = rot or Matrix.Identity(3)
    idx = []
    for sx, sy, sz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
        idx.append(acc.vert(Vector((cx, cy, cz)) + R @ Vector((sx * hx, sy * hy, sz * hz))))
    faces = (((0, 3, 2, 1), 0.72), ((4, 5, 6, 7), 1.06), ((0, 1, 5, 4), 0.92), ((1, 2, 6, 5), 0.9), ((2, 3, 7, 6), 0.88), ((3, 0, 4, 7), 0.9))
    for f, k in faces:
        acc.face([idx[i] for i in f], mul(col, k) if shade else col, th)


def lathe(acc, x, y, profile, sides, color, caps=('flat', 'flat'), th=50.0):
    """沿 +Z 的旋转体：profile = [(z, r), …]。"""
    centers = [Vector((x, y, z)) for z, _ in profile]
    radii = [r for _, r in profile]
    return tube(acc, centers, radii, sides, color, caps, th)


# ---------------------------------------------------------------- 部件
def bamboo(acc, rng, start, direction, length, radius, base, *, sides=8, lift=None, taper=0.0,
           node_gap=(0.34, 0.47), caps=('hollow', 'hollow'), wet=False):
    """一根竹子：竹节处略鼓、颜色深一圈，节间颜色各自浮动；lift=(高度, 长度) 让末端那一段往上翘。"""
    D = Vector(direction).normalized()
    up = Vector((0, 0, 1)) if abs(D.z) < 0.9 else perp(D)
    nodes = []
    s = rng.uniform(0.1, 0.26)
    while s < length - 0.09:
        nodes.append(s)
        s += rng.uniform(*node_gap)
    stations = {0.0, length}
    for n in nodes:
        stations.update((n - 0.021, n, n + 0.021))
    b0 = length - lift[1] if lift else length
    if lift:
        k = 0
        while b0 + 0.11 * k < length - 0.02:
            stations.add(b0 + 0.11 * k)
            k += 1
    st = []
    for v in sorted(stations):
        if 0.0 <= v <= length and (not st or v - st[-1] > 0.006):
            st.append(v)
    if st[-1] < length:
        st.append(length)

    def lifted(v):
        if not lift or v <= b0:
            return 0.0
        t = (v - b0) / lift[1]
        return lift[0] * t * t

    centers = [Vector(start) + D * v + up * lifted(v) for v in st]
    radii = []
    for v in st:
        r = radius * (1.0 - taper * v / length)
        radii.append(r * 1.075 if any(abs(v - n) < 1e-4 for n in nodes) else r)
    factors = [rng.uniform(0.9, 1.06) for _ in range(len(nodes) + 1)]
    greens = [max(0.0, rng.uniform(-0.15, 0.3)) for _ in range(len(nodes) + 1)]
    side = [rng.uniform(0.95, 1.05) for _ in range(sides)]

    def color(i, j):
        mid = (st[i] + st[i + 1]) * 0.5
        if any(abs(mid - n) < 0.0215 for n in nodes):
            return mix(mul(base, 0.78), NODE, 0.4)
        k = sum(1 for n in nodes if n < mid)
        return mul(mix(base, BAMBOO_GREEN, greens[k]), factors[k] * side[j])

    tube(acc, centers, radii, sides, color, caps, th=50.0, wet=wet)
    return centers, radii, st


def rope_path(acc, points, r=0.008, sides=4, base=ROPE):
    pts = [Vector(p) for p in points]
    tube(acc, pts, [r] * len(pts), sides, lambda i, j: base if i % 2 == 0 else mul(base, 0.84), ('flat', 'flat'), th=180.0)


def sag(a, b, depth, n=6):
    a, b = Vector(a), Vector(b)
    return [a.lerp(b, i / n) - Vector((0, 0, depth * 4 * (i / n) * (1 - i / n))) for i in range(n + 1)]


def build(rng):
    body, glass = Acc(), Acc()
    info = {}

    # ---- 竹排：八根竹子，最粗那根的底面贴 z = 0，其余浮得略高；船头（+Y）最后一段往上翘
    diam = [0.150, 0.172, 0.140, 0.182, 0.160, 0.168, 0.146, 0.158]
    gap = 0.004
    width = sum(diam) + gap * (len(diam) - 1)
    zc = max(diam) / 2
    x = -width / 2
    poles = []
    for i, d in enumerate(diam):
        r = d / 2
        cx = x + r
        x += d + gap
        length = rng.uniform(2.88, 3.0)
        y0 = -length / 2 + rng.uniform(-0.035, 0.035)
        centers, radii, st = bamboo(body, rng, (cx, y0, zc), (0, 1, 0), length, r, BAMBOO[i % len(BAMBOO)],
                                    lift=(rng.uniform(0.11, 0.15), 0.5), wet=True)
        poles.append({'x': cx, 'r': r, 'y0': y0, 'y1': y0 + length, 'centers': centers, 'st': st})
    info['poles'] = len(poles)
    info['raftWidth'] = round(width, 3)
    top = zc + max(diam) / 2

    def pole_center_at(p, y):
        """竹子在某个 y 处的截面中心与切线（含船头上翘），绑绳跟着竹子的走向。"""
        cs = p['centers']
        for a, b in zip(cs, cs[1:]):
            if a.y <= y <= b.y:
                t = (y - a.y) / max(1e-6, b.y - a.y)
                return a.lerp(b, t), (b - a).normalized()
        return (cs[0], (cs[1] - cs[0]).normalized()) if y < cs[0].y else (cs[-1], (cs[-1] - cs[-2]).normalized())

    # ---- 两道横撑：细竹压在竹排上，每根竹子交叉处一圈绑绳 + 一圈套竹子的绳（前后交替）
    rb = 0.038
    brace_z = top + rb - 0.006
    for by in (-1.02, 0.92):
        bamboo(body, rng, (-0.71, by, brace_z), (1, 0, 0), 1.42, rb, mul(hexc('#a88f55'), 0.95), sides=7,
               node_gap=(0.3, 0.42))
        for i, p in enumerate(poles):
            ring(body, (p['x'], by, brace_z), (1, 0, 0), rb + 0.011, 0.011, seg=8, minor=3)
            side = 1 if i % 2 == 0 else -1
            yy = by + side * (rb + 0.026)
            c, axis = pole_center_at(p, yy)
            ring(body, c, axis, p['r'] + 0.009, 0.011, seg=8, minor=3, wet=True)

    # ---- 两头的捆扎：每根竹子一圈，船头那道跟着上翘
    for yy in (-1.34, 1.27):
        for p in poles:
            if p['y0'] + 0.04 < yy < p['y1'] - 0.04:
                c, axis = pole_center_at(p, yy)
                ring(body, c, axis, p['r'] + 0.009, 0.011, seg=8, minor=3, wet=True)

    # ---- 船头立杆（靠岸一侧 -X）、挑杆、斜撑与拉绳
    px, py = -0.47, 0.92 + rb + 0.032
    pz0 = top - 0.004
    bamboo(body, rng, (px, py, pz0), (0, 0, 1), 2.06 - pz0, 0.031, BAMBOO[5], sides=7, taper=0.12,
           node_gap=(0.3, 0.4), caps=('flat', 'hollow'))
    for z in (pz0 + 0.04, pz0 + 0.09):
        ring(body, (px, py, z), (0, 0, 1), 0.04, 0.009, seg=8, minor=3)
    ring(body, (px, py - rb - 0.03, brace_z), (1, 0, 0), rb + 0.011, 0.011, seg=8, minor=3)
    arm_a, arm_b = Vector((px, py - 0.03, 1.99)), Vector((px, 1.37, 2.03))
    bamboo(body, rng, arm_a, arm_b - arm_a, (arm_b - arm_a).length, 0.017, BAMBOO[2], sides=6,
           node_gap=(0.22, 0.3), caps=('hollow', 'hollow'))
    rope_path(body, [(px, py + 0.012, 1.72), (px, py + 0.12, 1.88), (px, py + 0.2, 2.01)], r=0.011, sides=5,
              base=mul(hexc('#a88f55'), 0.9))
    ring(body, (px, py, 1.99), (0, 0, 1), 0.035, 0.008, seg=8, minor=3)
    rope_path(body, sag((px + 0.02, py, 1.88), (0.68, 0.92, brace_z + 0.02), 0.06, 8), r=0.006)

    # ---- 马灯：挂在挑杆末端；红漆油壶 + 黄铜灯头 + 玻璃罩（单独网格）+ 护丝 + 两根风管 + 灯帽 + 提梁
    lx, ly = px, 1.33
    red = lambda i, j: LANTERN_RED if i != 0 else mul(LANTERN_RED, 0.7)
    lathe(body, lx, ly, [(1.525, 0.058), (1.532, 0.074), (1.56, 0.079), (1.588, 0.073), (1.6, 0.056)], 12, red)
    lathe(body, lx, ly, [(1.598, 0.05), (1.613, 0.048)], 12, lambda i, j: BRASS)
    glass_profile = [(1.613, 0.040), (1.636, 0.056), (1.668, 0.063), (1.700, 0.056), (1.727, 0.041), (1.735, 0.037)]
    lathe(glass, lx, ly, glass_profile, 12, lambda i, j: GLASS, th=60.0)
    lathe(body, lx, ly, [(1.733, 0.064), (1.746, 0.068), (1.76, 0.052), (1.79, 0.031), (1.806, 0.027), (1.822, 0.02)],
          12, lambda i, j: LANTERN_RED if i < 2 else IRON)
    for k in range(4):
        a = TAU * (k + 0.5) / 4
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [Vector((lx, ly, z)) + d * rr for z, rr in ((1.606, 0.066), (1.668, 0.077), (1.738, 0.066))]
        tube(body, pts, [0.0035] * 3, 3, lambda i, j: IRON, ('flat', 'flat'), th=180.0)
    for s in (-1, 1):
        tube(body, [Vector((lx + s * 0.06, ly, 1.585)), Vector((lx + s * 0.084, ly, 1.6)), Vector((lx + s * 0.084, ly, 1.755)),
                    Vector((lx + s * 0.058, ly, 1.77))], [0.0075] * 4, 5, lambda i, j: TIN, ('flat', 'flat'), th=70.0)
    bail = [Vector((lx + 0.07 * math.cos(math.pi * i / 8), ly, 1.756 + 0.118 * math.sin(math.pi * i / 8))) for i in range(9)]
    tube(body, bail, [0.0045] * 9, 4, lambda i, j: IRON, ('flat', 'flat'), th=180.0)
    tube(body, [Vector((lx, ly, 2.03)), Vector((lx, ly, 1.94)), Vector((lx, ly + 0.012, 1.9)), Vector((lx, ly, 1.874))],
         [0.004] * 4, 4, lambda i, j: IRON, ('flat', 'flat'), th=180.0)
    lamp = Vector((lx, ly, (glass_profile[0][0] + glass_profile[-1][0]) / 2))
    info['lanternGlassCenterBlender'] = [round(v, 4) for v in lamp]

    # ---- 船尾撑篙：从水里斜插上来，贴着最外侧那根竹子的尾端，绑一道绳连到船尾横撑
    pb, pt = Vector((0.70, -1.60, -0.32)), Vector((0.60, -0.92, 2.52))
    bamboo(body, rng, pb, pt - pb, (pt - pb).length, 0.026, hexc('#a09271'), sides=7, taper=0.25,
           node_gap=(0.36, 0.48), caps=('flat', 'hollow'), wet=True)
    t = (0.30 - pb.z) / (pt.z - pb.z)
    tie = pb.lerp(pt, t)
    ring(body, tie, pt - pb, 0.036, 0.009, seg=8, minor=3)
    rope_path(body, [tie + Vector((-0.02, 0.02, 0)), (0.69, -1.25, brace_z + 0.06), (0.69, -1.02 - rb - 0.006, brace_z + 0.02)],
              r=0.009)
    ring(body, (0.66, -1.02, brace_z), (1, 0, 0), rb + 0.011, 0.011, seg=8, minor=3)

    # ---- 木箱：横板拼的箱体 + 四角竖条；盖着油布，靠岸那一角掀起来露出木头
    cx, cy, w, d, h = 0.16, -0.40, 0.58, 0.46, 0.40
    bz = top - 0.012
    box(body, (cx, cy, bz + h / 2), (w - 0.03, d - 0.03, h - 0.01), WOOD_DARK)
    for k in range(3):
        z = bz + 0.01 + (k + 0.5) * (h - 0.02) / 3
        hh = (h - 0.02) / 3 - 0.012
        tone = mul(WOOD, rng.uniform(0.88, 1.06))
        box(body, (cx, cy - d / 2 + 0.012, z), (w - 0.07, 0.022, hh), tone)
        box(body, (cx, cy + d / 2 - 0.012, z), (w - 0.07, 0.022, hh), mul(tone, 0.97))
        box(body, (cx - w / 2 + 0.012, cy, z), (0.022, d - 0.07, hh), mul(tone, 1.02))
        box(body, (cx + w / 2 - 0.012, cy, z), (0.022, d - 0.07, hh), mul(tone, 0.95))
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(body, (cx + sx * (w / 2 - 0.02), cy + sy * (d / 2 - 0.02), bz + h / 2), (0.044, 0.044, h), mul(WOOD_DARK, 1.15))
    top_z = bz + h
    # 油布：网格从箱顶往四边垂下，侧面起竖褶、下摆外张，四角像桌布那样垂成尖；(-X, -Y) 那一角掀起，露出木箱
    hang, e = 0.30, 0.018

    def tarp(sx, sy, out=0.0):
        """布面坐标 (sx, sy)（以箱顶中心为原点、展开后的平面坐标）→ 世界坐标；out 沿外法线再推出去（给压在布上的绳子用）。"""
        ox, oy = max(0.0, abs(sx) - w / 2), max(0.0, abs(sy) - d / 2)
        if sx < 0 and sy < 0:  # 掀起的那一角：离角越近垂得越短
            k = 0.3 + 0.7 * min(1.0, math.hypot(sx + w / 2, sy + d / 2) / 0.45) ** 1.5
            ox, oy = ox * k, oy * k
        o = math.hypot(ox, oy)
        x = math.copysign(min(abs(sx), w / 2), sx)
        y = math.copysign(min(abs(sy), d / 2), sy)
        z = top_z + e
        if o > 0:
            fl = e + 0.035 * (o / hang) + 0.01 * math.sin(sy * 26 + sx * 21) * min(1.0, o / 0.12)
            x += math.copysign(fl * ox / o, sx) + math.copysign(out * ox / o, sx)
            y += math.copysign(fl * oy / o, sy) + math.copysign(out * oy / o, sy)
            z -= o
        else:
            z += 0.012 * (1 - (sx / (w / 2)) ** 2) * (1 - (sy / (d / 2)) ** 2) + 0.004 * math.sin(sx * 17) * math.cos(sy * 13) + out
        return Vector((cx + x, cy + y, max(z, bz + 0.035)))

    hx, hy = w / 2 + hang, d / 2 + hang
    nx, ny = 18, 16
    grid = [[body.vert(tarp(-hx + 2 * hx * i / nx, -hy + 2 * hy * j / ny)) for i in range(nx + 1)] for j in range(ny + 1)]
    for j in range(ny):
        for i in range(nx):
            sx = -hx + 2 * hx * (i + 0.5) / nx
            sy = -hy + 2 * hy * (j + 0.5) / ny
            o = min(1.0, math.hypot(max(abs(sx) - w / 2, 0.0), max(abs(sy) - d / 2, 0.0)) / hang)
            n = 0.5 + 0.5 * math.sin(sx * 6.5 + sy * 4.0) * math.cos(sx * 3.0 - sy * 5.5)
            col = mul(mix(TARP, hexc('#8b9372'), 0.4 * (1 - o) * n), 0.9 + 0.1 * n - 0.22 * o)
            body.face((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]), col, 70.0)
    # 两道捆箱绳：贴着油布翻过箱顶，下摆以下贴着箱板，两头压到竹排上
    for yy in (-0.11, 0.11):
        pts = [tarp(sx, yy, 0.011) for sx in [-w / 2 - hang + 0.02 * k for k in range(0, 16)] + [-w / 2 + 0.06 * k for k in range(1, 10)]
               + [w / 2 + 0.02 * k for k in range(0, 16)]]
        hem_l, hem_r = pts[0], pts[-1]
        path = [Vector((hem_l.x - 0.03, cy + yy, top + 0.004)), Vector((cx - w / 2 - 0.011, cy + yy, bz + 0.08))] + pts
        path += [Vector((cx + w / 2 + 0.011, cy + yy, bz + 0.08)), Vector((hem_r.x + 0.03, cy + yy, top + 0.004))]
        clean = [path[0]]
        for v in path[1:]:
            if (v - clean[-1]).length > 0.008:
                clean.append(v)
        rope_path(body, clean, r=0.009)

    # ---- 一卷绳：三圈叠着，绳头搭到船头横撑上
    rx, ry = -0.2, 0.42
    for k, (R, dz, ox, oy) in enumerate(((0.15, 0.0, 0.0, 0.0), (0.138, 0.04, 0.012, -0.01), (0.112, 0.075, -0.008, 0.008))):
        ring(body, (rx + ox, ry + oy, top + 0.02 + dz), (0, 0, 1), R, 0.022, seg=14, minor=5, base=mul(ROPE, 1.0 - 0.05 * k))
    rope_path(body, [(rx + 0.11, ry + 0.06, top + 0.1), (rx + 0.2, ry + 0.22, top + 0.05), (rx + 0.24, 0.92 - rb - 0.01, brace_z),
                     (rx + 0.25, 0.92, brace_z + rb + 0.02), (rx + 0.26, 0.92 + rb + 0.01, brace_z)], r=0.02, sides=5)
    return body, glass, info, lamp


# ---------------------------------------------------------------- 落成对象
def finish(acc, name, material):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in acc.v], [], acc.f)
    mesh.update()
    if len(mesh.polygons) != len(acc.f):
        raise RuntimeError(name + ': polygon count changed (' + str(len(mesh.polygons)) + ' vs ' + str(len(acc.f)) + ')')
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2:
            a, b = e.link_faces
            limit = math.radians(min(acc.th[a.index], acc.th[b.index]))
            e.smooth = e.calc_face_angle(0.0) <= limit
        else:
            e.smooth = False
    uv = bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:  # 按法线主轴盒式投影，1 米一格（目前只用顶点色，留给以后贴图）
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for lp in f.loops:
            p = lp.vert.co
            lp[uv].uv = (p.x, p.y) if az >= ax and az >= ay else ((p.y, p.z) if ax >= ay else (p.x, p.z))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    # 顶点色：每面一色，再按水线 / 朝下压暗（湿竹子、底面）
    colors = []
    for poly in mesh.polygons:
        c = acc.c[poly.index]
        if acc.wet[poly.index]:
            zc = poly.center.z
            if zc < 0.04:
                c = mix(c, WET, 0.5 * min(1.0, (0.04 - zc) / 0.05 + 0.4))
            if poly.normal.z < -0.45:
                c = mul(c, 0.78)
        colors.append(c)
    attr = mesh.color_attributes.new(name='Col', type='BYTE_COLOR', domain='CORNER')
    flat = [0.0] * (len(mesh.loops) * 4)
    for poly in mesh.polygons:
        c = colors[poly.index]
        for li in poly.loop_indices:
            flat[li * 4:li * 4 + 4] = (c[0], c[1], c[2], 1.0)
    attr.data.foreach_set('color_srgb', flat)
    mesh.color_attributes.active_color = attr
    mesh.color_attributes.render_color_index = 0
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def make_material(name, emission=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    attr = nt.nodes.new('ShaderNodeVertexColor')
    attr.layer_name = 'Col'
    nt.links.new(attr.outputs[0], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.85
    if emission:
        bsdf.inputs['Emission Color'].default_value = (*emission, 1.0)
        bsdf.inputs['Emission Strength'].default_value = 3.0
    return m


def unity_local(p):
    """Blender → Unity 本地（FBX -Z 前 / Y 上，轴向烘进网格）。"""
    return [round(-p[0], 4), round(p[2], 4), round(-p[1], 4)]


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            for k in range(3):
                lo[k] = min(lo[k], v.co[k])
                hi[k] = max(hi[k], v.co[k])
    return lo, hi


def render_previews(out, objs, lamp):
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
    except TypeError as error:
        print('TIDEGATE_RAFT_WARN no workbench: ' + str(error))
        return []
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'VERTEX'
    shading.show_shadows = True
    shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('RaftPreviewWorld')
    scene.world = world
    world.color = (0.55, 0.5, 0.44)
    cam_data = bpy.data.cameras.new('RaftPreviewCamera')
    cam = bpy.data.objects.new('RaftPreviewCamera', cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = []
    target = Vector((0.0, 0.0, 0.55))
    # 靠岸一侧（-X）从船尾看；船头一侧；游戏镜头口径（FOV 20°、俯仰 55°，镜头在船尾那边）；马灯特写
    for name, pos, aim, lens in (('raft_quarter_shore.png', Vector((-3.4, -3.2, 2.4)), target, 35),
                                 ('raft_quarter_bow.png', Vector((3.0, 3.6, 2.1)), target, 35),
                                 ('raft_game_camera.png', Vector((0.0, -12 * math.cos(math.radians(55)), 12 * math.sin(math.radians(55)))), Vector((0, 0, 0.4)), 101.6),
                                 ('raft_lantern_closeup.png', lamp + Vector((-0.75, -0.6, 0.15)), lamp, 50)):
        cam.location = pos
        cam.rotation_euler = (aim - pos).to_track_quat('-Z', 'Y').to_euler()
        cam_data.lens = lens
        path = out / 'previews' / name
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        shots.append(str(path))
    return shots


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='ArtSource/Tidegate/raft')
    ap.add_argument('--render', action='store_true')
    args = ap.parse_args(argv)
    out = Path(args.out).resolve()
    (out / 'previews').mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    rng = random.Random(SEED)
    body_acc, glass_acc, info, lamp = build(rng)
    body = finish(body_acc, 'Raft_Body', make_material('TG_Raft'))
    glass = finish(glass_acc, 'Raft_LanternGlass', make_material('TG_RaftLanternGlass', emission=(1.0, 0.72, 0.38)))

    lo, hi = bounds([body, glass])
    report = {
        'fbx': str(out / 'TidegateRaft.fbx'),
        'objects': {'Raft_Body': {'triangles': tri_count(body), 'vertices': len(body.data.vertices)},
                    'Raft_LanternGlass': {'triangles': tri_count(glass), 'vertices': len(glass.data.vertices)}},
        'triangles': tri_count(body) + tri_count(glass),
        'blenderBounds': {'min': [round(v, 4) for v in lo], 'max': [round(v, 4) for v in hi]},
        'unityBounds': {'min': [round(-hi.x, 4), round(lo.z, 4), round(-hi.y, 4)], 'max': [round(-lo.x, 4), round(hi.z, 4), round(-lo.y, 4)]},
        'lanternGlassCenterUnity': unity_local(lamp),
        'info': info,
        'seed': SEED,
    }

    bpy.ops.object.select_all(action='DESELECT')
    for o in (body, glass):
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    fbx = out / 'TidegateRaft.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=False, colors_type='SRGB',
                             bake_anim=False, add_leaf_bones=False, path_mode='AUTO', embed_textures=False)
    if args.render:
        report['previews'] = render_previews(out, [body, glass], lamp)
    bpy.ops.wm.save_as_mainfile(filepath=str(out / 'TidegateRaft.blend'), compress=True)
    (out / 'tidegate_raft_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('TIDEGATE_RAFT_OK ' + json.dumps(report, ensure_ascii=False))
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(0)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        print('TIDEGATE_RAFT_FAIL')
        sys.stdout.flush()
        sys.stderr.flush()
        os._exit(1)
