"""Original, palette-only life props for the sky island authoring generator.

COMPAT: visual geometry only. Units are metres, Y is up, ground is Y=0,
and the useful/front side faces -Z. This module has no Blender dependency.
The injected generator supplies the primitives from generate_sky_island.py.

model_geometry returns {material: {v, f, uv, smooth}} with independent lists.
stamp returns {key, bounds: {min, max, size, center}, triangles}; bounds describe
the vertices actually placed, including ropes, handles and projecting tools.
Yaw is in radians and scale is a positive, uniform multiplier.
"""

import math
from collections import OrderedDict
from copy import deepcopy


MODEL_SPECS = [
    ('produce_stall', '双色篷布菜摊'),
    ('supply_handcart', '双轮补给手推车'),
    ('camp_tent', '野营 A 字帐篷'),
    ('tool_workbench', '工具钳台工作台'),
    ('rain_barrel', '圆肚雨水桶架'),
    ('windmill_planter', '小风车菜圃'),
    ('floatboat_cargo', '浮舟物资箱组'),
    ('firewood_rack', '木柴与劈柴架'),
    ('duck_mailbox', '鸭嘴邮筒'),
    ('laundry_rack', '晾晒架'),
    ('compost_planter', '堆肥种植箱'),
    ('picnic_table', '野餐桌凳组合'),
    ('wayfinding_sign', '生活区路牌'),
    ('tea_stove', '茶水灶'),
]

_LOCAL_CACHE = OrderedDict()
_CACHE_LIMIT = len(MODEL_SPECS) * 4


def _rope(g, points, material='BrassLight', radius=.032):
    for start, end in zip(points, points[1:]):
        g.beam(start, end, radius, material, sides=5)


def _prism(g, outline, front, back, material):
    """Extrude an XY outline along Z, including concave cloth/sign outlines."""
    count = len(outline)
    vertices = [(x, y, z) for z in (front, back) for x, y in outline]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces.extend((i, (i + 1) % count, (i + 1) % count + count, i + count)
                 for i in range(count))
    g.addmesh(material, vertices, faces)


def _sheet(g, rows, material, thickness=.035, axis=1):
    """A folded grid with a closed hem, not a single-sided floating plane."""
    height, width = len(rows), len(rows[0])
    front = [tuple(point) for row in rows for point in row]
    back = [tuple(value - thickness if index == axis else value
                  for index, value in enumerate(point)) for point in front]
    count = len(front)
    faces = []
    for row in range(height - 1):
        for column in range(width - 1):
            a = row * width + column
            face = (a, a + 1, a + width + 1, a + width)
            faces.append(face)
            faces.append(tuple(index + count for index in reversed(face)))
    boundary = (list(range(width))
                + [row * width + width - 1 for row in range(1, height)]
                + list(range(count - 2, count - width - 1, -1))
                + [row * width for row in range(height - 2, 0, -1)])
    for a, b in zip(boundary, boundary[1:] + boundary[:1]):
        faces.append((a, a + count, b + count, b))
    uv = [(column / (width - 1), row / (height - 1))
          for row in range(height) for column in range(width)]
    g.addmesh(material, front + back, faces, uv=uv + uv)


def _leaf(g, start, direction, width, material='LeafLight'):
    x, y, z = start
    dx, dy, dz = direction
    horizontal = math.hypot(dx, dz)
    sx, sz = ((-dz / horizontal, dx / horizontal) if horizontal > 1e-8
              else (1, 0))
    middle = (x + dx * .48, y + dy * .48, z + dz * .48)
    vertices = [(x, y, z),
                (middle[0] + sx * width, middle[1], middle[2] + sz * width),
                (x + dx, y + dy, z + dz),
                (middle[0] - sx * width, middle[1], middle[2] - sz * width),
                (middle[0], middle[1] + width * .45, middle[2]),
                (middle[0], middle[1] - width * .12, middle[2])]
    faces = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4),
             (1, 0, 5), (2, 1, 5), (3, 2, 5), (0, 3, 5)]
    g.addmesh(material, vertices, faces)


def _sprout(g, x, y, z, scale=1):
    for index in range(3):
        angle = .35 + index * math.tau / 3
        _leaf(g, (x, y, z),
              (.35 * scale * math.cos(angle), (.35 + .08 * index) * scale,
               .35 * scale * math.sin(angle)), .115 * scale,
              'Leaf' if index == 1 else 'LeafLight')


def _cabbage(g, x, y, z, scale=1):
    g.sphere((x, y + .22 * scale, z), (.3 * scale, .27 * scale, .29 * scale),
             'LeafLight', segments=8, rings=4, smooth=False)
    for index in range(4):
        angle = index * math.pi / 2 + .3
        _leaf(g, (x, y + .1 * scale, z),
              (.46 * scale * math.cos(angle), .28 * scale,
               .46 * scale * math.sin(angle)), .19 * scale,
              'Leaf' if index % 2 else 'Fern')


def _carrot(g, x, y, z, scale=1):
    g.beam((x - .07 * scale, y, z), (x, y + .46 * scale, z + .07 * scale),
           .025 * scale, 'Copper', sides=7, r_end=.13 * scale)
    _sprout(g, x, y + .46 * scale, z + .07 * scale, .52 * scale)


def _crate(g, x, y, z, width=1.2, height=.65, depth=.95, lid=False):
    g.box((x, y + .065, z), (width, .13, depth), 'WoodDark', bevel=.04)
    for sign in (-1, 1):
        for level in (.3, .77):
            g.box((x, y + height * level, z + sign * (depth / 2 - .055)),
                  (width, height * .43, .11), 'WoodLight', bevel=.055)
        g.box((x + sign * (width / 2 - .055), y + height * .54, z),
              (.11, height * .9, depth), 'Wood', bevel=.045)
        for end in (-1, 1):
            g.beam((x + sign * (width / 2 - .025), y + .08,
                    z + end * (depth / 2 - .025)),
                   (x + sign * (width / 2 - .025), y + height,
                    z + end * (depth / 2 - .025)), .05, 'Brass', sides=4)
    if lid:
        for index in range(3):
            g.box((x + (index - 1) * width / 3, y + height + .035, z),
                  (width / 3 - .025, .10, depth), 'WoodLight', bevel=.045)
        for side in (-1, 1):
            g.box((x + side * width * .28, y + height + .095, z),
                  (.10, .035, depth + .035), 'PaintTealDeep', bevel=.006)
            g.box((x + side * width * .28, y + height * .5, z - depth / 2 - .02),
                  (.10, height, .035), 'PaintTealDeep', bevel=.006)


def _sack(g, x, y, z, scale=1, material='Ivory'):
    g.lathe((x, y, z), [(0, .27 * scale), (.12 * scale, .42 * scale),
                        (.5 * scale, .46 * scale), (.82 * scale, .31 * scale),
                        (.94 * scale, .14 * scale), (1.08 * scale, .24 * scale),
                        (1.12 * scale, .13 * scale)], material, sides=9)
    g.cylinder((x, y + 1.105 * scale, z), .13 * scale, .025 * scale,
               'WoodDark', sides=9)
    g.torus((x, y + .94 * scale, z), .15 * scale, .035 * scale, 'BrassLight',
            segments=10, sides=4)
    _rope(g, [(x + .12 * scale, y + .94 * scale, z),
              (x + .29 * scale, y + .78 * scale, z - .08 * scale),
              (x + .26 * scale, y + .61 * scale, z - .1 * scale)],
          radius=.025 * scale)
    g.box((x, y + .5 * scale, z - .432 * scale),
          (.3 * scale, .24 * scale, .025 * scale), 'Coral', bevel=.025 * scale)


def _mug(g, x, y, z, scale=1, material='Ivory'):
    g.lathe((x, y, z), [(0, .12 * scale), (.03 * scale, .16 * scale),
                        (.27 * scale, .17 * scale), (.29 * scale, .14 * scale),
                        (.075 * scale, .12 * scale)], material, sides=8)
    g.cylinder((x, y + .22 * scale, z), .135 * scale, .012 * scale,
               'WoodDark', sides=8)
    g.torus((x + .20 * scale, y + .16 * scale, z), .105 * scale, .034 * scale,
            material, axis='z', segments=8, sides=4)


def _jug(g, x, y, z, scale=1, material='PaintTealLight'):
    g.lathe((x, y, z), [(0, .2 * scale), (.1 * scale, .31 * scale),
                        (.46 * scale, .33 * scale), (.66 * scale, .21 * scale),
                        (.75 * scale, .15 * scale), (.9 * scale, .18 * scale),
                        (.91 * scale, .12 * scale)], material, sides=10)
    g.cylinder((x, y + .86 * scale, z), .11 * scale, .02 * scale,
               'WoodDark', sides=10)
    g.torus((x + .31 * scale, y + .49 * scale, z), .23 * scale, .055 * scale,
            material, axis='z', segments=10, sides=4)


def _stool(g, x, y, z, scale=1):
    g.cylinder((x, y + .78 * scale, z), .45 * scale, .16 * scale,
               'WoodLight', sides=10)
    for index in range(3):
        angle = index * math.tau / 3 + .4
        g.box((x + .32 * scale * math.cos(angle), y + .05 * scale,
               z + .32 * scale * math.sin(angle)),
              (.16 * scale, .10 * scale, .17 * scale), 'Wood', bevel=.028 * scale)
        g.beam((x + .32 * scale * math.cos(angle), y + .08 * scale,
                z + .32 * scale * math.sin(angle)),
               (x + .23 * scale * math.cos(angle), y + .74 * scale,
                z + .23 * scale * math.sin(angle)), .07 * scale,
               'Wood', sides=6)


def _produce_stall(g):
    for x in (-2.35, 2.35):
        for z, top in ((-.95, 3.25), (.85, 3.65)):
            g.box((x, .12, z), (.48, .24, .48), 'WoodDark', bevel=.08)
            g.beam((x, .2, z), (x, top, z), .13, 'Wood', sides=6)
        g.beam((x, 2.7, -.95), (x, 3.45, .85), .075, 'WoodLight', sides=6)
        for y in (2.5, 2.58):
            g.torus((x, y, -.95), .14, .024, 'BrassLight', segments=8, sides=4)
    for z, y in ((-1.38, 3.24), (.2, 3.88), (1.14, 3.50)):
        g.beam((-2.75, y, z), (2.75, y, z), .07, 'WoodLight', sides=6)
    for stripe in range(7):
        left = -2.8 + stripe * .8
        material = 'PaintTealLight' if stripe % 2 == 0 else 'Ivory'
        rows = [[(left, y, z), (left + .4, y - .035, z), (left + .8, y, z)]
                for y, z in ((3.26, -1.5), (3.65, -.65),
                             (3.93, .2), (3.65, .85), (3.50, 1.25))]
        _sheet(g, rows, material)
        _prism(g, [(left, 3.26), (left + .8, 3.26), (left + .8, 3.03),
                   (left + .64, 2.95), (left + .4, 2.90),
                   (left + .16, 2.95), (left, 3.03)], -1.52, -1.48, material)
        _rope(g, [(left, 3.035, -1.55), (left + .16, 2.96, -1.55),
                  (left + .4, 2.91, -1.55), (left + .64, 2.96, -1.55),
                  (left + .8, 3.035, -1.55)], 'Flower', .018)
    for x in (-2.8, 2.8):
        _rope(g, [(x, 3.27, -1.5), (x, 3.67, -.65),
                  (x, 3.95, .2), (x, 3.52, 1.25)], 'Flower', .026)
    g.box((0, .17, -.1), (4.7, .22, 1.55), 'WoodDark', bevel=.08)
    for index in range(6):
        g.box((-1.975 + index * .79, .82, -.86), (.75, 1.1, .15),
              'Wood' if index % 3 else 'WoodLight', bevel=.07)
    g.box((0, 1.42, -.1), (4.95, .22, 1.82), 'WoodLight', bevel=.09)
    for tray, x in enumerate((-1.55, 0, 1.55)):
        _crate(g, x, 1.54, -.12, 1.36, .30, 1.12)
        for item in range(4):
            px, pz = x + (item % 2 - .5) * .52, -.12 + (item // 2 - .5) * .43
            if tray == 0:
                _cabbage(g, px, 1.75, pz, .64)
            elif tray == 1:
                _carrot(g, px, 1.72, pz, .72)
            else:
                g.sphere((px, 1.93, pz), (.23, .22, .22), 'Coral', 8, 4)
                _leaf(g, (px, 2.14, pz), (.12, .025, -.08), .06, 'Leaf')
    g.box((-.35, .95, -.97), (1.12, .5, .08), 'PaintTealDeep', bevel=.055)
    for index, height in enumerate((.15, .24, .19)):
        g.box((-.65 + index * .27, .96, -1.019), (.1, height, .028), 'Ivory', .012)
    _sack(g, -2.92, 0, -.45, .75)
    g.beam((2.36, 3.10, -.95), (2.36, 3.10, -1.55), .04, 'Brass', sides=6)
    _rope(g, [(2.36, 3.08, -1.5), (2.36, 2.69, -1.5)], 'Brass', .025)
    for dx in (-.31, .31):
        _rope(g, [(2.36, 2.7, -1.5), (2.36 + dx, 2.26, -1.5)], 'Brass', .018)
    g.lathe((2.36, 2.19, -1.5), [(0, .23), (.07, .4), (.12, .41)], 'Brass', sides=10)


def _supply_handcart(g):
    for x in (-1.48, 1.48):
        g.torus((x, .72, .25), .60, .12, 'WoodDark', axis='x', segments=16, sides=5)
        g.torus((x, .72, .25), .49, .045, 'WoodLight', axis='x', segments=12, sides=4)
        for index in range(6):
            angle = index * math.tau / 6
            g.beam((x, .72, .25), (x, .72 + .5 * math.cos(angle),
                                   .25 + .5 * math.sin(angle)), .048,
                   'WoodLight', sides=5)
        g.beam((x - .16, .72, .25), (x + .16, .72, .25), .13, 'Brass', sides=8)
    g.beam((-1.62, .72, .25), (1.62, .72, .25), .105, 'WoodDark', sides=6)
    for index in range(5):
        g.box(((index - 2) * .51, 1.02, .15), (.49, .18, 2.5), 'WoodLight', .07)
    for side in (-1, 1):
        for z in (-1.06, 1.34):
            g.beam((side * 1.24, 1.03, z), (side * 1.24, 1.91, z), .09,
                   'Wood', sides=6)
        for y in (1.32, 1.72):
            g.box((side * 1.25, y, .15), (.13, .23, 2.5), 'Wood', .04)
            g.box((0, y, 1.33), (2.46, .23, .13), 'WoodLight', .04)
        g.beam((side * .96, 1.02, -.55), (side * .96, 1.1, -2.80), .09,
               'Wood', sides=6)
        g.beam((side * .96, 1.10, -2.5), (side * .96, 1.11, -2.84), .11,
               'PaintTealDeep', sides=8)
        g.beam((side * .96, .11, -1.50), (side * .96, 1.00, -1.30), .07,
               'WoodDark', sides=6)
        g.box((side * .96, .07, -1.51), (.25, .14, .38), 'WoodDark', .04)
    _crate(g, -.52, 1.13, .25, 1.1, .70, 1.6)
    _sack(g, -.52, 1.78, .49, .83, 'Flower')
    _jug(g, .67, 1.12, .58, 1.15, 'Coral')
    g.beam((.54, 1.38, -1.02), (.54, 1.38, .02), .29, 'PaintTealLight', sides=10)
    for z in (-.86, -.17):
        g.torus((.54, 1.38, z), .295, .034, 'BrassLight', axis='z', segments=10, sides=4)
    _rope(g, [(-1.22, 1.45, .35), (-.85, 2.37, .35), (-.46, 2.65, .35),
              (.12, 2.21, .35), (1.24, 1.45, .35)], radius=.039)


def _camp_tent(g):
    g.box((0, .045, .03), (3.9, .09, 3.25), 'WoodDark', bevel=.025)
    for side in (-1, 1):
        rows = [[(side * x, y, z) for x, y in
                 ((0, 3.05), (.70, 2.16), (1.41, 1.02), (2.06, .12))]
                for z in (-1.68, -.70, .68, 1.68)]
        _sheet(g, rows, 'PaintTealLight' if side < 0 else 'PaintTeal')
        for z in (-1.69, 1.68):
            _rope(g, [(0, 3.07, z), (side * .7, 2.18, z),
                      (side * 1.41, 1.04, z), (side * 2.06, .15, z)], 'Flower', .033)
        g.beam((side * 1.94, .10, -1.56), (0, 3.13, -1.56), .065, 'Wood', sides=6)
        for z in (-1.6, 1.6):
            _rope(g, [(side * 1.07, 1.56, z), (side * 2.78, .12, z * 1.35)],
                  'BrassLight', .026)
            g.beam((side * 2.78, .03, z * 1.35),
                   (side * 2.84, .34, z * 1.37), .045, 'WoodDark', sides=5)
    _prism(g, [(-1.99, .1), (1.99, .1), (0, 3.015)], 1.63, 1.69, 'PaintTealDeep')
    for side in (-1, 1):
        outline = [(0, 3.035), (side * .90, 1.43), (side * 1.55, .97),
                   (side * 1.99, .12), (side * 1.14, .15),
                   (side * 1.03, .88), (side * .73, 1.53)]
        _prism(g, outline, -1.73, -1.68, 'Ivory')
        _rope(g, [(side * .02, 3.02, -1.76), (side * .88, 1.44, -1.77),
                  (side * 1.40, .98, -1.78), (side * 1.91, .15, -1.77)],
              'Flower', .025)
        g.box((side * 1.25, 1.01, -1.78), (.39, .10, .09), 'Coral', bevel=.028)
    g.beam((0, 3.12, -1.91), (0, 3.12, 1.83), .067, 'WoodLight', sides=6)
    g.box((-.24, .20, .06), (1.12, .22, 2.27), 'Coral', bevel=.10)
    g.sphere((-.24, .36, -.68), (.52, .17, .27), 'Ivory', 10, 4)
    g.beam((-.78, .39, .95), (.3, .39, .95), .25, 'Coral', sides=10)
    for x in (-.60, .12):
        g.torus((x, .39, .95), .252, .028, 'BrassLight', axis='x', segments=10, sides=4)
    g.box((2.06, .47, -.84), (.60, .94, .49), 'Coral', bevel=.11, yaw=-.15)
    g.box((2.06, .91, -.87), (.64, .19, .54), 'WoodLight', bevel=.07, yaw=-.15)
    for x in (1.9, 2.2):
        g.box((x, .49, -1.12), (.07, .76, .06), 'BrassLight', .018)
    _mug(g, 1.27, .045, -1.25, 1.05, 'PaintTealLight')


def _tool_workbench(g):
    for x in (-1.38, 1.38):
        for z in (-.58, .65):
            g.box((x, .76, z), (.24, 1.52, .27), 'Wood', bevel=.07)
        g.beam((x, .27, -.58), (x, 1.40, .65), .07, 'WoodLight', sides=5)
        g.box((x, 2.29, .81), (.18, 1.87, .2), 'WoodDark', bevel=.05)
    for index in range(4):
        g.box((0, 1.58, -.60 + index * .43), (3.64, .21, .405), 'WoodLight', .075)
    g.box((0, .40, .05), (2.95, .15, 1.22), 'WoodDark', bevel=.045)
    for y in (1.98, 2.48, 2.98):
        g.box((0, y, .83), (3.48, .45, .14), 'WoodLight', bevel=.055)
    for x, y in ((-1.04, 2.67), (-.13, 2.81), (.89, 2.83), (.41, 2.21)):
        g.beam((x, y, .73), (x, y, .56), .04, 'Brass', sides=6)
    # A hammer, toothed saw and open-jaw pliers read clearly against the backboard.
    g.beam((-1.16, 1.95, .59), (-1.01, 2.66, .59), .065, 'Wood', sides=6)
    g.beam((-1.28, 2.66, .59), (-.76, 2.66, .59), .12, 'RockDeep', sides=6)
    _prism(g, [(-.68, 2.78), (.32, 2.67), (.2, 2.46), (-.69, 2.57)], .56, .62, 'Chalk')
    for index in range(6):
        x = -.64 + index * .135
        _prism(g, [(x, 2.55 - index * .017), (x + .11, 2.536 - index * .017),
                   (x + .055, 2.47 - index * .017)], .555, .62, 'Chalk')
    g.torus((.32, 2.60, .58), .15, .052, 'Wood', axis='z', segments=8, sides=4)
    for side in (-1, 1):
        _rope(g, [(.89 + side * .16, 2.21, .56), (.89 + side * .05, 2.52, .56),
                  (.89 - side * .09, 2.71, .56), (.89 - side * .065, 2.82, .56)],
              'RockDeep', .043)
        g.beam((.89 + side * .16, 2.21, .56), (.89 + side * .10, 2.39, .56),
               .062, 'Coral', sides=6)
    g.sphere((.89, 2.52, .50), (.08, .08, .035), 'Brass', 8, 4)
    g.cylinder((1.13, 1.77, -.48), .27, .18, 'PaintTealDeep', sides=10)
    g.box((1.13, 1.91, -.50), (.68, .27, .58), 'PaintTeal', bevel=.10)
    for z in (-.89, -.37):
        g.box((1.13, 2.09, z), (.66, .31, .14), 'PaintTealDeep', bevel=.06)
        g.box((1.13, 2.245, z), (.67, .065, .16), 'Chalk', bevel=.018)
    g.beam((1.13, 1.93, -.54), (1.13, 1.93, -1.28), .065, 'Brass', sides=8)
    g.beam((1.13, 1.67, -1.29), (1.13, 2.16, -1.29), .033, 'RockDeep', sides=6)
    for y in (1.67, 2.16):
        g.sphere((1.13, y, -1.29), (.06, .06, .06), 'Brass', 7, 3)
    g.box((-.50, 1.735, -.37), (1.14, .075, .44), 'Wood', bevel=.025, yaw=.16)
    _mug(g, -.98, 1.70, .36, .92, 'Coral')
    _crate(g, -.48, .49, .11, 1.25, .49, .93)
    _stool(g, -2.10, 0, -.49, 1.0)
    for index in range(4):
        g.box((-.76 + index * .22, .035, -1.02 + .13 * (index % 2)),
              (.23, .06, .065), 'WoodLight', bevel=.012, yaw=index * .7)


def _rain_barrel(g):
    for x in (-.71, .71):
        for z in (-.64, .64):
            g.box((x, .46, z), (.23, .92, .25), 'Wood', bevel=.07)
        g.beam((x, .22, -.63), (x, .82, .63), .065, 'WoodLight', sides=6)
    g.box((0, .86, 0), (2.12, .18, 1.93), 'WoodLight', bevel=.075)
    profile = [(0, .71), (.13, .82), (.55, .99), (1.19, 1.02),
               (1.66, .90), (1.86, .77), (1.93, .78), (1.94, .66), (1.76, .65)]
    g.lathe((0, .94, 0), profile, 'PaintTealLight', sides=12)
    for y, radius in ((1.16, .85), (2.37, .965), (2.86, .775)):
        g.torus((0, y, 0), radius, .058, 'Brass', segments=12, sides=4)
    for index in range(8):
        angle = index * math.tau / 8
        _rope(g, [(radius * math.cos(angle), .94 + y, radius * math.sin(angle))
                  for y, radius in ((.2, .86), (.55, 1.00), (1.18, 1.03), (1.6, .935))],
              'PaintTeal', .018)
    g.cylinder((0, 2.72, 0), .65, .022, 'Water', sides=12)
    g.box((1.43, 1.76, .80), (.18, 3.52, .19), 'Wood', bevel=.045)
    for y in (2.86, 3.43):
        g.beam((1.43, y, .79), (1.28, y, .54), .047, 'Brass', sides=6)
    _rope(g, [(.62, 2.63, .54), (1.28, 2.63, .54), (1.28, 3.66, .54)], 'Copper', .098)
    g.lathe((1.28, 3.55, .54), [(0, .12), (.18, .15), (.45, .38), (.49, .40),
                                          (.50, .34), (.20, .10)], 'Copper', sides=10)
    g.torus((1.28, 3.18, .54), .115, .03, 'BrassLight', segments=8, sides=4)
    _rope(g, [(0, 1.34, -.86), (0, 1.34, -1.34), (0, 1.13, -1.34)], 'Brass', .08)
    g.beam((0, 1.35, -1.09), (0, 1.55, -1.09), .042, 'Brass', sides=6)
    g.beam((-.16, 1.55, -1.09), (.16, 1.55, -1.09), .045, 'PaintTealDeep', sides=6)
    g.lathe((0, 0, -1.36), [(0, .28), (.06, .32), (.66, .42), (.70, .39),
                           (.13, .27)], 'Chalk', sides=10)
    g.torus((0, .68, -1.36), .4, .04, 'RockDeep', segments=10, sides=4)
    _rope(g, [(.39 * math.cos(index * math.pi / 8),
               .55 + .45 * math.sin(index * math.pi / 8), -1.36)
              for index in range(9)], 'RockDeep', .025)
    g.pot(-1.35, 0, .14, .63, plant=False)
    _sprout(g, -1.35, .67, .14, .74)


def _windmill_planter(g):
    for x in (-1.78, 1.78):
        for z in (-1.00, 1.00):
            g.box((x, .45, z), (.19, .9, .19), 'WoodDark', bevel=.05)
        g.box((x, .41, 0), (.17, .65, 2.0), 'WoodLight', bevel=.06)
    for z in (-1.0, 1.0):
        for y in (.23, .57):
            g.box((0, y, z), (3.57, .30, .14), 'WoodLight', bevel=.055)
    g.box((0, .57, 0), (3.36, .15, 1.86), 'Soil', bevel=.07)
    g.beam((1.10, .66, .64), (1.10, 3.57, .64), .125, 'Wood', sides=7, r_end=.085)
    for side in (-1, 1):
        g.beam((1.1 + side * .53, .67, .72), (1.1, 2.35, .64), .067, 'WoodLight', sides=6)
    g.beam((1.1, 3.25, -.08), (1.1, 3.25, 1.58), .09, 'Brass', sides=8)
    _prism(g, [(1.08, 3.28), (1.77, 3.56), (1.69, 3.05), (1.08, 3.21)],
           1.41, 1.48, 'Coral')
    for index in range(4):
        angle = .34 + index * math.pi / 2
        direction, tangent = (math.cos(angle), math.sin(angle)), (-math.sin(angle), math.cos(angle))
        outline = [(1.1 + direction[0] * radius + tangent[0] * width,
                    3.25 + direction[1] * radius + tangent[1] * width)
                   for radius, width in ((.21, -.09), (.99, -.20), (1.15, -.08),
                                         (1.09, .30), (.57, .21), (.21, .065))]
        _prism(g, outline, -.20, -.12, 'Ivory' if index % 2 else 'PaintTealLight')
        _rope(g, [(1.1 + direction[0] * .2, 3.25 + direction[1] * .2, -.225),
                  (1.1 + direction[0] * 1.04, 3.25 + direction[1] * 1.04, -.225)],
              'BrassLight', .023)
    g.sphere((1.1, 3.25, -.22), (.22, .22, .15), 'Brass', 10, 5)
    for index, (x, z) in enumerate(((-1.15, -.52), (-.42, -.52), (.40, -.52),
                                   (-1.13, .40), (-.32, .40))):
        if index % 2:
            _carrot(g, x, .67, z, 1.04)
        else:
            _cabbage(g, x, .67, z, .88)
    g.beam((-1.47, .64, -.83), (-1.47, 1.36, -.83), .04, 'Wood', sides=5)
    g.box((-1.47, 1.24, -.88), (.48, .33, .08), 'Ivory', bevel=.06)
    _prism(g, [(-1.58, 1.34), (-1.37, 1.34), (-1.50, 1.12)], -.936, -.918, 'Copper')


def _floatboat_cargo(g):
    outline = [(0, -2.25), (.82, -1.88), (1.35, -1.05), (1.35, 1.18),
               (.93, 1.6), (-.93, 1.6), (-1.35, 1.18), (-1.35, -1.05), (-.82, -1.88)]
    count = len(outline)
    vertices = [(x * factor, y, z * factor)
                for y, factor in ((.12, .68), (.25, .86), (.76, 1), (.85, 1.02),
                                  (.85, .91), (.63, .86)) for x, z in outline]
    faces = [tuple(reversed(range(count)))]
    faces.extend((ring * count + i, ring * count + (i + 1) % count,
                  (ring + 1) * count + (i + 1) % count, (ring + 1) * count + i)
                 for ring in range(5) for i in range(count))
    faces.append(tuple(range(count * 5, count * 6)))
    g.addmesh('PaintTeal', vertices, faces)
    _rope(g, [(x * 1.02, .87, z * 1.02) for x, z in outline + outline[:1]], 'WoodLight', .065)
    for x in (-1.71, 1.71):
        g.sphere((x, .34, -.03), (.32, .34, 1.62), 'PaintTealDeep', 10, 6, smooth=False)
        for z in (-.85, .90):
            g.torus((x, .34, z), .30, .035, 'Brass', axis='z', segments=10, sides=4)
    for z in (-.9, .9):
        g.beam((-1.86, .46, z), (1.86, .46, z), .065, 'Wood', sides=6)
    for index in range(4):
        g.box((-.81 + index * .54, .685, .10), (.51, .10, 2.45), 'WoodLight', .035)
    _crate(g, -.1, .75, .43, 1.76, .85, 1.53, lid=True)
    _crate(g, .13, 1.69, .48, 1.20, .66, 1.10, lid=True)
    _sack(g, -.59, .74, -1.01, .79, 'Flower')
    _rope(g, [(-1.29, .9, .50), (-.67, 2.10, .5), (-.44, 2.48, .5),
              (.66, 2.48, .5), (1.29, .9, .5)], radius=.037)
    g.beam((1.55, .91, 1.54), (1.76, .85, -1.75), .065, 'WoodLight', sides=6)
    _prism(g, [(1.59, .91), (1.87, .90), (1.97, .64), (1.83, .52), (1.55, .64)],
           -1.95, -1.40, 'WoodLight')
    g.beam((-.99, .81, 1.03), (-.99, 2.98, 1.03), .055, 'Wood', sides=6)
    _prism(g, [(-.95, 2.91), (-.27, 2.77), (-.50, 2.59), (-.95, 2.57)],
           1.01, 1.05, 'Coral')


def _log(g, x, y, z, radius=.30, length=1.4, material='Wood'):
    g.beam((x, y, z - length / 2), (x + .035, y + .025, z + length / 2),
           radius, material, sides=9, r_end=radius * .9)
    g.beam((x, y, z - length / 2 - .016), (x, y, z - length / 2 - .006),
           radius * .88, 'WoodLight', sides=9)
    g.torus((x, y, z - length / 2 - .026), radius * .56, .014, 'Wood',
            axis='z', segments=10, sides=3)
    _rope(g, [(x - radius * .1, y + radius * .10, z - length / 2 - .038),
              (x + radius * .58, y + radius * .25, z - length / 2 - .038)], 'Wood', .013)


def _firewood_rack(g):
    for x in (-1.51, 1.51):
        for z in (-.64, .73):
            g.box((x, 1.06, z), (.19, 2.12, .22), 'Wood', bevel=.06)
        g.beam((x, .35, -.63), (x, 1.78, .73), .065, 'WoodLight', sides=6)
    g.box((0, .18, .02), (3.19, .19, 1.60), 'WoodDark', bevel=.055)
    for row, count in enumerate((4, 3, 2)):
        for index in range(count):
            x = (index - (count - 1) / 2) * .68
            _log(g, x, .58 + row * .50, .05 + .055 * ((index + row) % 2),
                 .30 + .015 * ((index + row) % 2), 1.6 - .09 * (index % 2),
                 'Wood' if index % 2 else 'WoodDark')
    _sheet(g, [[(-1.81, 2.12, -1.04), (0, 2.25, -1.04), (1.81, 2.12, -1.04)],
               [(-1.81, 2.34, .22), (0, 2.48, .22), (1.81, 2.34, .22)],
               [(-1.81, 2.13, 1.04), (0, 2.25, 1.04), (1.81, 2.13, 1.04)]], 'PaintTealDeep', .07)
    for x in (-1.81, 0, 1.81):
        y = 2.25 if x == 0 else 2.12
        _rope(g, [(x, y + .035, -1.04), (x, y + .26, .22), (x, y + .045, 1.04)],
              'WoodLight', .038)
    g.cylinder((2.12, .39, -.34), .64, .78, 'Wood', sides=9, radius_top=.56)
    g.cylinder((2.12, .794, -.34), .53, .03, 'WoodLight', sides=9)
    g.torus((2.12, .82, -.34), .32, .015, 'Wood', segments=12, sides=3)
    _prism(g, [(1.92, .80), (2.26, .85), (2.45, 1.14), (2.34, 1.36),
               (2.13, 1.30)], -.42, -.27, 'RockDeep')
    _prism(g, [(1.92, .80), (2.05, .82), (2.23, 1.32), (2.13, 1.30)],
           -.425, -.265, 'Chalk')
    g.beam((2.30, 1.21, -.34), (2.72, 2.05, -.34), .065, 'WoodLight', sides=7)
    g.beam((2.59, 1.78, -.34), (2.73, 2.07, -.34), .078, 'Coral', sides=7)
    _log(g, 1.86, .24, -1.32, .21, .67)
    _log(g, 2.42, .22, -1.13, .18, .86, 'WoodDark')


def _duck_mailbox(g):
    g.box((0, .09, 0), (1.64, .18, 1.25), 'Chalk', bevel=.14)
    g.box((0, .89, .02), (.32, 1.66, .34), 'Wood', bevel=.07)
    for side in (-1, 1):
        g.beam((0, .83, .03), (side * .61, 1.62, .03), .07, 'WoodLight', sides=6)
    g.box((0, 1.66, -.05), (1.84, .17, 1.64), 'WoodLight', bevel=.08)
    outline = [(-.78, 1.75), (.78, 1.75), (.78, 2.22), (.62, 2.55),
               (.28, 2.72), (-.28, 2.72), (-.62, 2.55), (-.78, 2.22)]
    _prism(g, outline, -.72, .71, 'PaintTealLight')
    front = [(x * .91, 2.22 + (y - 2.22) * .90) for x, y in outline]
    _prism(g, front, -.80, -.735, 'Ivory')
    for side in (-1, 1):
        g.sphere((side * .34, 2.35, -.825), (.062, .08, .045), 'WoodDark', 8, 4)
        g.sphere((side * .79, 2.08, -.02), (.08, .24, .43), 'PaintTeal', 9, 4)
    g.sphere((0, 2.075, -1.01), (.60, .14, .46), 'BrassLight', 12, 5)
    g.box((0, 2.067, -1.446), (.65, .066, .05), 'WoodDark', bevel=.013)
    _sheet(g, [[(-.21, 2.069, -1.61), (.23, 2.069, -1.61)],
               [(-.21, 2.082, -1.12), (.23, 2.082, -1.12)]], 'Ivory', .018)
    _rope(g, [(-.19, 2.09, -1.56), (.01, 2.094, -1.37), (.21, 2.09, -1.56)], 'Chalk', .010)
    g.box((.13, 2.09, -1.22), (.1, .016, .10), 'Coral', bevel=.008)
    g.beam((.95, 1.92, -.02), (.95, 3.11, -.02), .055, 'Brass', sides=6)
    _prism(g, [(.97, 3.08), (1.58, 3.08), (1.58, 2.78), (1.32, 2.72), (.97, 2.78)],
           -.065, .025, 'Coral')
    g.sphere((.95, 1.96, -.06), (.12, .12, .08), 'WoodDark', 8, 4)
    _sack(g, -1.04, 0, -.23, .7, 'Flower')
    _sprout(g, 1.04, .06, .35, .83)


def _laundry_rack(g):
    for x in (-2.5, 2.5):
        for z in (-.73, .73):
            g.box((x, .065, z), (.33, .13, .35), 'WoodDark', bevel=.04)
            g.beam((x, .13, z), (x, 3.05, 0), .095, 'Wood', sides=6)
        g.beam((x, 1.0, -.49), (x, 1.0, .49), .06, 'WoodLight', sides=5)
        g.torus((x, 2.87, 0), .11, .032, 'BrassLight', segments=8, sides=4)
    _rope(g, [(x, 2.62 + .26 * (x / 2.5) ** 2, 0)
              for x in (-2.5, -2, -1.5, -1, -.5, 0, .5, 1, 1.5, 2, 2.5)],
          'BrassLight', .024)
    rows = [[(-2.07 + column * .21, y + .025 * math.sin(column * 2),
              -.035 + .065 * math.sin(column * 2 + row * .45))
             for column in range(5)] for row, y in enumerate((2.72, 2.16, 1.41))]
    _sheet(g, rows, 'Coral', .029, axis=2)
    _sheet(g, [[(x, y + offset, z - .02) for x, y, z in rows[-1]]
               for offset in (.06, .14)], 'Flower', .014, axis=2)
    # The tunic has short sleeves, an indented neck and a gently folded hem.
    _sheet(g, [[(-.68, 2.48, -.05), (-.41, 2.35, -.08), (-.15, 2.35, -.06), (.12, 2.48, -.04)],
               [(-.68, 2.07, -.04), (-.41, 2.05, -.10), (-.15, 2.07, -.03), (.12, 2.07, -.06)],
               [(-.72, 1.43, -.08), (-.43, 1.46, -.12), (-.14, 1.41, -.04), (.16, 1.44, -.09)]],
           'Ivory', .025, axis=2)
    for outline in ([(-.68, 2.48), (-1.01, 2.19), (-.84, 1.99), (-.63, 2.11)],
                    [(.12, 2.48), (.46, 2.20), (.3, 1.99), (.06, 2.11)]):
        _prism(g, outline, -.075, -.045, 'Ivory')
    _rope(g, [(-.66, 2.46, -.045), (-.28, 2.61, -.01), (.1, 2.46, -.045)], 'WoodLight', .028)
    g.box((-.12, 2.03, -.09), (.23, .26, .04), 'PaintTealLight', bevel=.022)
    g.box((1.37, 2.57, -.035), (.79, .25, .06), 'PaintTealDeep', bevel=.024)
    for x in (1.14, 1.59):
        _sheet(g, [[(x - .16, 2.50, -.05), (x + .16, 2.50, -.07)],
                   [(x - .16, 1.93, -.015), (x + .16, 1.93, -.095)],
                   [(x - .18, 1.35, -.04), (x + .15, 1.38, -.08)]], 'PaintTeal', .028, axis=2)
        g.box((x, 1.42, -.075), (.34, .13, .075), 'PaintTealLight', bevel=.017)
    for x, y in ((-1.96, 2.73), (-1.33, 2.73), (-.28, 2.66), (1.06, 2.69), (1.68, 2.73)):
        g.box((x, y, -.026), (.058, .17, .085), 'WoodLight', bevel=.013)
    g.lathe((-1.62, 0, -.92), [(0, .39), (.06, .44), (.55, .59), (.61, .56),
                              (.15, .38)], 'WoodLight', sides=10)
    for y, radius in ((.14, .46), (.38, .535), (.58, .585)):
        g.torus((-1.62, y, -.92), radius, .026, 'Wood', segments=10, sides=4)
    for index, material in enumerate(('Ivory', 'PaintTealLight', 'Coral')):
        g.box((-1.62 + .045 * index, .46 + .115 * index, -.92),
              (.69, .14, .52), material, bevel=.058, yaw=.1 * index)


def _compost_planter(g):
    for x in (-1.90, 0, 1.90):
        for z in (-.83, .83):
            g.box((x, .65, z), (.20, 1.3, .20), 'Wood', bevel=.06)
        g.box((x, .60, 0), (.13, .99, 1.7), 'WoodLight', bevel=.05)
    for z in (-.85, .85):
        for index in range(3):
            g.box((0, .24 + index * .34, z), (3.88, .29, .14),
                  'WoodLight' if index % 2 else 'Wood', bevel=.065)
    for x in (-.93, .94):
        g.box((x, .80, 0), (1.68, .16, 1.49), 'Soil', bevel=.06)
    g.sphere((-.95, .88, .10), (.73, .28, .57), 'Soil', 10, 4, smooth=False)
    for index in range(4):
        x, z = -1.43 + index * .29, -.10 + .18 * (index % 2)
        _leaf(g, (x, 1.04, z), (.32, .11, .12), .13, 'LeafGold' if index % 2 else 'Leaf')
        g.box((x + .07, 1.10, z - .10), (.18, .06, .095), 'WoodLight', .014, yaw=index * .71)
    _rope(g, [(-.93, 1.16, -.26), (-.76, 1.20, -.21), (-.57, 1.16, -.13),
              (-.53, 1.10, -.07)], 'BrassLight', .052)
    _cabbage(g, .49, .89, -.26, 1.00)
    _cabbage(g, 1.23, .89, .36, .9)
    _carrot(g, 1.31, .89, -.35, .78)
    _sheet(g, [[(-1.8, 1.23, .85), (-.11, 1.23, .85)],
               [(-1.8, 2.25, 1.34), (-.11, 2.25, 1.34)]], 'WoodLight', .09, axis=2)
    for x in (-1.64, -.28):
        g.beam((x, 1.25, .79), (x, 2.21, 1.25), .038, 'Wood', sides=5)
    g.beam((-1.64, .94, .06), (-1.64, 1.80, 1.00), .04, 'Brass', sides=5)
    g.beam((-2.16, .43, -.28), (-2.45, 2.18, .03), .06, 'WoodLight', sides=6)
    g.torus((-2.46, 2.26, .04), .14, .045, 'Wood', axis='z', segments=8, sides=4)
    g.beam((-2.39, .43, -.30), (-1.95, .43, -.30), .055, 'RockDeep', sides=6)
    for x in (-2.38, -2.16, -1.96):
        g.beam((x, .43, -.30), (x + .015, .075, -.4), .035, 'RockDeep', sides=5)
    _sack(g, 2.33, 0, .17, .78, 'Flower')


def _picnic_table(g):
    for x in (-1.40, 1.40):
        for side in (-1, 1):
            g.box((x, .07, side * .77), (.33, .14, .42), 'WoodDark', bevel=.05)
            g.beam((x, .16, side * .77), (x, 1.64, side * .47), .105, 'Wood', sides=6)
            g.box((x, .41, side * 1.35), (.21, .82, .25), 'Wood', bevel=.06)
        g.beam((x, .42, -1.52), (x, .42, 1.52), .08, 'WoodLight', sides=6)
    g.beam((-1.46, .77, 0), (1.46, .77, 0), .085, 'Wood', sides=6)
    for index in range(5):
        g.box((0, 1.70, -.72 + index * .36), (4.25, .20, .34), 'WoodLight', bevel=.065)
    for side in (-1, 1):
        for offset in (-.145, .145):
            g.box((0, .89, side * 1.36 + offset), (4.65, .18, .27), 'WoodLight', bevel=.065)
    _sheet(g, [[(-.98, y, z), (-.49, y + .035, z), (0, y, z)]
               for y, z in ((1.16, -1.01), (1.83, -.94), (1.83, -.35),
                            (1.83, .55), (1.83, .94), (1.37, 1.04))], 'Coral', .026)
    for x in (-.94, -.04):
        _rope(g, [(x, 1.19, -1.045), (x, 1.86, -.95),
                  (x, 1.86, .94), (x, 1.39, 1.065)], 'Flower', .016)
    _jug(g, -1.53, 1.82, .18, .82)
    _mug(g, -.57, 1.88, -.32, 1.04, 'Ivory')
    _mug(g, .38, 1.82, -.38, .90, 'PaintTealLight')
    g.lathe((1.15, 1.81, .17), [(0, .31), (.06, .44), (.16, .46)], 'Wood', sides=10)
    g.sphere((1.15, 2.05, .17), (.61, .17, .23), 'BrassLight', 10, 5)
    for x in (.87, 1.14, 1.42):
        g.beam((x - .04, 2.19, .09), (x + .055, 2.18, .26), .028, 'Ivory', sides=5)
    g.box((.54, .26, .80), (.63, .52, .39), 'PaintTeal', bevel=.08)
    g.box((.54, .51, .79), (.67, .16, .43), 'PaintTealLight', bevel=.065)
    _rope(g, [(.27, .5, .79), (.30, .72, .79), (.77, .72, .79), (.81, .5, .79)],
          'WoodLight', .035)


def _wayfinding_sign(g):
    g.sphere((-.18, .16, 0), (.64, .16, .59), 'Chalk', 9, 4, smooth=False)
    g.box((-.18, 1.97, .04), (.26, 3.74, .30), 'Wood', bevel=.065, yaw=.08)
    boards = [(-1.23, 1.79, 3.10, 'PaintTealLight', False),
              (-1.68, 1.03, 2.40, 'Coral', True),
              (-1.03, 1.42, 1.73, 'Ivory', False)]
    for left, right, bottom, material, reverse in boards:
        if reverse:
            outline = [(left, bottom + .24), (left + .4, bottom), (right, bottom + .025),
                       (right, bottom + .47), (left + .4, bottom + .49)]
        else:
            outline = [(left, bottom), (right - .39, bottom + .02), (right, bottom + .25),
                       (right - .39, bottom + .49), (left, bottom + .47)]
        _prism(g, outline, -.21, -.045, material)
        g.beam((-.18, bottom + .25, -.244), (-.18, bottom + .25, -.213),
               .048, 'Brass', sides=6)
        g.box((.37 if reverse else -.62, bottom + .095, -.233),
              (.41, .032, .025), 'Flower', bevel=.006)
    # Small pictograms are geometry, so the signs need no text texture.
    _prism(g, [(.38, 3.49), (.62, 3.44), (.42, 3.20)], -.25, -.225, 'Copper')
    _rope(g, [(.47, 3.46, -.26), (.43, 3.61, -.26)], 'Leaf', .027)
    g.beam((-.90, 2.53, -.255), (-.67, 2.78, -.255), .037, 'WoodLight', sides=5)
    g.beam((-.85, 2.82, -.255), (-.57, 2.65, -.255), .073, 'RockDeep', sides=5)
    _prism(g, [(.37, 1.86), (.65, 1.86), (.69, 2.11), (.32, 2.11)], -.255, -.23, 'PaintTeal')
    g.torus((.72, 2.01, -.249), .083, .025, 'PaintTeal', axis='z', segments=8, sides=4)
    for x in (-.35, .06):
        _rope(g, [(x, 1.70, -.12), (x - .04, 1.26, -.12)], 'BrassLight', .023)
        _prism(g, [(x - .13, 1.30), (x + .11, 1.30), (x + .05, .99)],
               -.15, -.105, 'Coral' if x < 0 else 'PaintTealLight')
    g.pot(.74, 0, .17, .65, plant=False)
    _sprout(g, .74, .70, .17, .93)


def _tea_stove(g):
    g.lathe((-.46, 0, 0), [(0, .78), (.12, .93), (.34, .89),
                           (.96, .78), (1.13, .86)], 'RockLight', sides=10)
    g.cylinder((-.46, 1.17, 0), .94, .16, 'RockDeep', sides=12)
    _prism(g, [(-.87, .21), (-.10, .21), (-.10, .62), (-.24, .82),
               (-.68, .82), (-.87, .63)], -.855, -.80, 'WoodDark')
    for x in (-.93, -.035):
        g.box((x, .45, -.79), (.15, .61, .25), 'Chalk', bevel=.06)
    g.box((-.47, .86, -.77), (.81, .16, .27), 'Chalk', bevel=.065)
    for x in (-.73, -.44, -.21):
        g.sphere((x, .30, -.88), (.14, .09, .11), 'Coral', 7, 3, smooth=False)
    g.sphere((-.49, .43, -.87), (.075, .18, .04), 'Glow', 7, 4)
    g.lathe((-.50, 1.26, -.13), [(0, .25), (.10, .47), (.39, .55),
                                (.65, .47), (.77, .31), (.82, .30)], 'PaintTealLight', sides=12)
    g.lathe((-.50, 2.055, -.13), [(0, .33), (.08, .29), (.13, .15)], 'Brass', sides=10)
    g.sphere((-.50, 2.22, -.13), (.105, .08, .105), 'WoodDark', 8, 4)
    _rope(g, [(-.50 + .52 * math.cos(index * math.pi / 10),
               1.97 + .56 * math.sin(index * math.pi / 10), -.13)
              for index in range(11)], 'WoodDark', .056)
    _rope(g, [(-.50, 1.65, -.54), (-.50, 1.77, -.79),
              (-.50, 1.97, -1.02), (-.50, 2.07, -1.11)], 'PaintTealLight', .10)
    g.torus((-.50, 2.065, -1.13), .095, .025, 'Brass', axis='z', segments=8, sides=4)
    g.beam((.38, .90, .45), (.38, 3.42, .45), .15, 'RockDeep', sides=10)
    for y in (1.34, 2.48, 3.36):
        g.torus((.38, y, .45), .162, .031, 'Brass', segments=10, sides=4)
    g.lathe((.38, 3.40, .45), [(0, .18), (.05, .34), (.13, .35),
                              (.27, .12), (.29, 0)], 'PaintTealDeep', sides=10)
    for x in (1.20, 2.04):
        for z in (-.5, .49):
            g.box((x, .51, z), (.16, 1.02, .17), 'Wood', bevel=.045)
    for index in range(3):
        g.box((1.62, 1.08, -.41 + .41 * index), (1.33, .18, .39), 'WoodLight', bevel=.065)
    g.box((1.62, 1.225, 0), (1.05, .105, .72), 'PaintTealDeep', bevel=.05)
    _mug(g, 1.39, 1.28, -.15, 1.03)
    _mug(g, 1.83, 1.28, .13, .92, 'Coral')
    _log(g, -1.57, .22, .20, .19, .83)
    _log(g, -1.88, .20, .10, .17, .90, 'WoodDark')
    _log(g, -1.72, .51, .20, .17, .75)


_BUILDERS = {
    'produce_stall': _produce_stall,
    'supply_handcart': _supply_handcart,
    'camp_tent': _camp_tent,
    'tool_workbench': _tool_workbench,
    'rain_barrel': _rain_barrel,
    'windmill_planter': _windmill_planter,
    'floatboat_cargo': _floatboat_cargo,
    'firewood_rack': _firewood_rack,
    'duck_mailbox': _duck_mailbox,
    'laundry_rack': _laundry_rack,
    'compost_planter': _compost_planter,
    'picnic_table': _picnic_table,
    'wayfinding_sign': _wayfinding_sign,
    'tea_stove': _tea_stove,
}


def build_model(g, key):
    """Append one model at its authored local origin to g.CURRENT."""
    try:
        builder = _BUILDERS[key]
    except KeyError:
        raise ValueError('Unknown sky island life model: ' + repr(key)) from None
    builder(g)


def _append_data(destination, data, vertices=None):
    offset = len(destination['v'])
    destination['v'].extend(data['v'] if vertices is None else vertices)
    destination['f'].extend(tuple(offset + index for index in face) for face in data['f'])
    destination['uv'].extend(data['uv'])
    destination['smooth'].extend(data['smooth'])


def _empty_data():
    return {'v': [], 'f': [], 'uv': [], 'smooth': []}


def model_geometry(g, key):
    """Extract a fresh deep copy while restoring both generator globals on error."""
    saved_groups, saved_current = g.GROUPS, g.CURRENT
    try:
        g.GROUPS = {}
        g.CURRENT = 'LifeModel_' + key
        build_model(g, key)
        materials = {}
        for (_, material), data in g.GROUPS.items():
            _append_data(materials.setdefault(material, _empty_data()), data)
        return deepcopy(materials)
    finally:
        g.GROUPS = saved_groups
        g.CURRENT = saved_current


def _cached_geometry(g, key):
    cache_key = (id(g), key)
    entry = _LOCAL_CACHE.get(cache_key)
    if entry is None or entry[0] is not g:
        entry = (g, model_geometry(g, key))
        _LOCAL_CACHE[cache_key] = entry
        if len(_LOCAL_CACHE) > _CACHE_LIMIT:
            _LOCAL_CACHE.popitem(last=False)
    _LOCAL_CACHE.move_to_end(cache_key)
    return entry[1]


def stamp(g, key, position, yaw=0, scale=1):
    """Merge cached local geometry and return actual world bounds/triangle count.

    Mixed flat/smooth faces are copied individually: g.addmesh only accepts one
    bool for an entire call, so a direct merge is necessary to preserve them.
    Transform validation finishes before any destination geometry is appended.
    """
    position = tuple(float(value) for value in position)
    yaw, scale = float(yaw), float(scale)
    if len(position) != 3 or not all(math.isfinite(value) for value in position + (yaw, scale)):
        raise ValueError('Expected finite position XYZ, yaw and scale')
    if scale <= 0:
        raise ValueError('Life model scale must be positive')
    local = _cached_geometry(g, key)
    if hasattr(g, 'PALETTE'):
        missing = set(local) - set(g.PALETTE)
        if missing:
            raise ValueError('Missing sky island palette keys: ' + ', '.join(sorted(missing)))
    cosine, sine = math.cos(yaw), math.sin(yaw)
    transformed = {}
    minimum, maximum = [math.inf] * 3, [-math.inf] * 3
    triangles = 0
    for material, data in local.items():
        vertices = [(position[0] + scale * (x * cosine + z * sine),
                     position[1] + scale * y,
                     position[2] + scale * (-x * sine + z * cosine))
                    for x, y, z in data['v']]
        for vertex in vertices:
            for axis, value in enumerate(vertex):
                if not math.isfinite(value):
                    raise ValueError('Life model placement exceeds finite coordinates')
                minimum[axis] = min(minimum[axis], value)
                maximum[axis] = max(maximum[axis], value)
        transformed[material] = vertices
        triangles += sum(len(face) - 2 for face in data['f'])
    for material, vertices in transformed.items():
        destination = g.GROUPS.setdefault((g.CURRENT, material), _empty_data())
        _append_data(destination, local[material], vertices)
    return {'key': key,
            'bounds': {'min': tuple(minimum), 'max': tuple(maximum),
                       'size': tuple(high - low for low, high in zip(minimum, maximum)),
                       'center': tuple(low * .5 + high * .5 for low, high in zip(minimum, maximum))},
            'triangles': triangles}
