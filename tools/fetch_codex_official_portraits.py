# -*- coding: utf-8 -*-
"""用官方 wiki 的游戏内贴图重出鸭皇图鉴立绘。

owner 2026-09-20：「这个 wiki 里也有所有 boss 的游戏里贴图，请你按照这个贴图重新生成
各个 boss 的立绘，取代掉原本按感觉生成的立绘。」

做法：直接取官方渲染图（512×893 带透明通道的角色立绘），而不是拿它当参考再 AI 重画——
AI 重画只会再引入一次「画得像不像」的偏差，而图鉴要的恰恰是「这就是我打的那个 Boss」。
处理：裁掉透明边 -> 等比缩到 448 内 -> 居中贴到 512×512 透明画布（与既有立绘规格一致）。
"""
import io
import json
import os
import sys
import urllib.request

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT_DIR = os.path.join(ROOT, 'Assets', 'ui', 'Codex')
RAW_DIR = os.path.join(ROOT, 'output', 'codex_official_raw')
BASE = 'https://assets.escapefromduckov.net/creatures/'
CANVAS = 512
INNER = 448

# nameKey -> 首选 preset id（同 key 多个 preset 时取更「标准」的那一个：
# 优先非 Island / 非 Test / 非 Basaka 变体，保持与玩家在 BossRush 里遇到的那只一致）
PREFERENCE_PENALTY = ('_Island', '_Test', '_Basaka', '_Child', '_Elete', '_low', '_Snow', '_Ice', '_Farm')


def pick(ids):
    def score(pid):
        return sum(1 for token in PREFERENCE_PENALTY if token in pid), len(pid)
    return sorted(ids, key=score)[0]


def fetch(preset_id, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return True
    url = BASE + preset_id + '.png'
    try:
        request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(request, timeout=30) as response:
            data = response.read()
        if not data or data[:8] != b'\x89PNG\r\n\x1a\n':
            return False
        with open(dest, 'wb') as handle:
            handle.write(data)
        return True
    except Exception as error:
        print('  ! fetch failed', preset_id, error)
        return False


def normalize(src, dest):
    image = Image.open(src).convert('RGBA')
    box = image.getbbox()
    if box:
        image = image.crop(box)
    width, height = image.size
    scale = min(INNER / float(width), INNER / float(height))
    image = image.resize((max(1, int(width * scale)), max(1, int(height * scale))), Image.LANCZOS)
    canvas = Image.new('RGBA', (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.paste(image, ((CANVAS - image.size[0]) // 2, (CANVAS - image.size[1]) // 2), image)
    canvas.save(dest, 'PNG', optimize=True)


def main():
    data = json.load(io.open(os.path.join(HERE, 'codex_official_presets.json'), encoding='utf-8'))
    data = {k: {'ids': [v]} for k, v in data.items()}
    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)

    written, skipped, failed = [], [], []
    for name_key in sorted(data.keys()):
        entry = data[name_key]
        preset_id = pick(entry['ids'])
        raw = os.path.join(RAW_DIR, preset_id + '.png')
        if not fetch(preset_id, raw):
            failed.append((name_key, preset_id))
            continue
        dest = os.path.join(OUT_DIR, 'codex_portrait_' + name_key.lower() + '.png')
        try:
            normalize(raw, dest)
            written.append(name_key)
        except Exception as error:
            print('  ! normalize failed', name_key, error)
            failed.append((name_key, preset_id))

    print('written %d, skipped %d, failed %d' % (len(written), len(skipped), len(failed)))
    for name_key, preset_id in failed:
        print('  FAILED', name_key, preset_id)
    return 0 if not failed else 1


if __name__ == '__main__':
    sys.exit(main())
