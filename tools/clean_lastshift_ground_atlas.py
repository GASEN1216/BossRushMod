"""Replay the Last Shift Yard atlas and update only pixels under retired paint."""
import json
import math
from pathlib import Path
import subprocess

import numpy as np
from PIL import Image, ImageDraw


SIZE = (1344, 1056)
DESIGN = Path(__file__).resolve().parents[1] / 'docs/design/last-shift-yard'


def rgba(text):
    if text.startswith('#'):
        return (*bytes.fromhex(text[1:]), 255)
    r, g, b, a = (float(s) for s in text[5:-1].split(','))
    return int(r), int(g), int(b), round(a * 255)


def polylines(commands):
    points = []
    for kind, *args in commands:
        if kind == 'M' or kind == 'L':
            points.append(args)
        elif kind == 'C':
            start = points[-1]
            for t in np.linspace(0, 1, 25)[1:]:
                u = 1 - t
                points.append(tuple(u**3 * start[i] + 3 * u*u*t*args[i]
                                    + 3*u*t*t*args[2+i] + t**3*args[4+i] for i in (0, 1)))
        elif kind == 'E':
            x, z, rx, rz, angle, start, end = args
            for t in np.linspace(start, end, 73):
                dx, dz = rx * math.cos(t), rz * math.sin(t)
                points.append((x + dx * math.cos(angle) - dz * math.sin(angle),
                               z + dx * math.sin(angle) + dz * math.cos(angle)))
        else:
            raise ValueError('Unsupported atlas drawing command ' + kind)
    return [(x*8+672, z*8+528) for x, z in points]


def dash_lines(points, dash):
    if not dash:
        yield points
        return
    index, left = 0, dash[0]
    for a, b in zip(points, points[1:]):
        dx, dz = b[0]-a[0], b[1]-a[1]
        length = math.hypot(dx, dz)
        distance = 0
        while distance < length - 1e-9:
            step = min(left, length - distance)
            end = distance + step
            if index % 2 == 0:
                yield [(a[0]+dx*t/length, a[1]+dz*t/length) for t in (distance, end)]
            left -= step
            distance = end
            if left < 1e-9:
                index = (index+1) % len(dash)
                left = dash[index]


def bounds(command):
    kind, _, coords, *extra = command
    if kind == 'rect':
        x, z, w, d = coords
        points = [(x*8+672, z*8+528), ((x+w)*8+672, (z+d)*8+528)]
    else:
        points = polylines(coords)
    pad = 2 + (extra[0]*4 if extra else 0)
    x, z = zip(*points)
    box = max(0, math.floor(min(x)-pad)), max(0, math.floor(min(z)-pad)), \
          min(SIZE[0], math.ceil(max(x)+pad)), min(SIZE[1], math.ceil(max(z)+pad))
    return points, box


def coverage(command, points, box):
    kind, _, _, *extra = command
    x0, z0, x1, z1 = box
    scale = 4
    mask = Image.new('L', ((x1-x0)*scale, (z1-z0)*scale))
    draw = ImageDraw.Draw(mask)

    def local(pts):
        return [((x-x0)*scale, (z-z0)*scale) for x, z in pts]

    if kind == 'rect':
        a, b = local(points)
        draw.rectangle((a, (b[0]-.01, b[1]-.01)), fill=255)
    elif kind == 'fill':
        draw.polygon(local(points), fill=255)
    elif kind == 'stroke':
        width, dash = extra
        radius = width * 4 * scale
        for line in dash_lines(points, [n*8 for n in dash]):
            p = local(line)
            draw.line(p, fill=255, width=max(1, round(radius*2)), joint='curve')
            for x, z in (p[0], p[-1]):
                draw.ellipse((x-radius, z-radius, x+radius, z+radius), fill=255)
    else:
        raise ValueError(kind)
    return np.asarray(mask.resize((x1-x0, z1-z0), Image.Resampling.BOX))


def paint(layer, color, mask, box):
    x0, z0, x1, z1 = box
    dst = layer[z0:z1, x0:x1]
    alpha = mask.astype(np.float32)[..., None] * (color[3] / 65025.0)
    dst[:] = np.rint(dst * (1-alpha) + np.asarray(color[:3]) * alpha)


def repair_atlas(source, output, work, design=DESIGN):
    capture = work / 'ground_paint.json'
    subprocess.run(['node', str(Path(__file__).with_name('capture_lastshift_ground_atlas.mjs')),
                    str(design), str(capture)], check=True)
    drawing = json.loads(capture.read_text(encoding='utf-8'))
    png = Image.open(source).convert('RGBA')
    if png.size != SIZE:
        raise ValueError('Unexpected yard atlas dimensions')
    original = np.array(png)
    base = np.fromfile(capture.with_suffix('.json.rgba'), dtype='u1').reshape(original.shape)
    before, after = (base[:, :, :3].copy() for _ in range(2))
    affected = np.zeros(original.shape[:2], dtype=bool)
    commands = drawing['commands']
    removed = set(drawing['removedPaint'])
    for command in commands:
        if command[1] not in removed:
            continue
        points, box = bounds(command)
        x0, z0, x1, z1 = box
        if x1 > x0 and z1 > z0:
            affected[z0:z1, x0:x1] |= coverage(command, points, box) != 0
    for command in commands:
        points, box = bounds(command)
        x0, z0, x1, z1 = box
        if not np.any(affected[z0:z1, x0:x1]):
            continue
        mask = coverage(command, points, box)
        color = rgba(command[1])
        paint(before, color, mask, box)
        if command[1] not in removed:
            paint(after, color, mask, box)

    delta = after.astype(np.int16) - before
    if np.any(delta[~affected]) or np.any(delta < 0):
        raise ValueError('Retired paint changed non-target pixels or darkened the atlas')
    result = original.copy()
    # Apply the paint delta to Chromium's original PNG. This retains its base
    # shading and the antialiasing on road markings, rail and contact shadows.
    result[:, :, :3] = np.clip(original[:, :, :3].astype(np.int16) + delta, 0, 255)
    changed = np.any(result != original, axis=2)
    if not np.any(changed):
        raise ValueError('No visible paint found in the source atlas')
    Image.fromarray(result).save(output, optimize=True)
    return {'retiredCanvasOperations': drawing['types'], 'mip0ChangedPixels': int(changed.sum()),
            'maxChannelDelta': int(delta.max()), 'derivedFrom': str(source)}
