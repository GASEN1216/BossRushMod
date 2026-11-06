"""经配置的图像网关编辑官方笔记插图。先定信纸风格，余下五幅同时引用原稿与定稿信纸。"""
import argparse
import concurrent.futures
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SCENES = {
    'letter': 'A damp unfolded letter on bamboo beside the lantern. Draw clearly visible irregular tidal water stains: dark scalloped amber tide marks around the bottom fold, lighter soaked patches with hard ink borders. Retain the tiny hand-drawn sluice sketch and unreadable pen strokes.',
    'page1': 'The working tide gate opens between different water levels. Make its green rush of water legible.',
    'page2': 'The rusty pump wheel inside the pump station; water is creeping up the drain outside the open door.',
    'page3': 'The indispensable focal object is a large removable green cast iron handwheel in the CENTER OF THE LOCKER, taking up half the image height. Exactly four spokes and an obvious square central axle hole. Show the complete wheel rim front-on leaning against the back wall, the small rope coil partly behind it, not in its place; outside, the gangplank at risk from rising water. The illustration shows where the player can find this wheel.',
    'page4': 'The brass tide pocket watch with blue wave marks in the manager safe, resting on folded paper.',
    'ending': 'Old duck lockkeeper wearing worn work gloves listening to the same brass watch at his bamboo raft in the lamplit sewer. Preserve his bill and the hands from the reference.',
}
STYLE = ('Re-ink and repaint image 1 into a polished traditional Japanese 2D cel animation production still. '
         'Keep the staging, silhouettes, object identity, and ultra-wide 3:1 full-bleed framing of image 1. '
         'Clean deliberate dark-teal contour lines; flat local colours and exactly two clearly separated hard-edged shadow tones. '
         'No gradient shading, paper texture, paint flecks, speckles, brush strokes, photo lighting or 3D rendering. '
         'Limited palette: ink navy, harbour teal, reed cream, ochre brass. Broad uncluttered shapes with the focal objects safe in the central horizontal band. No lettering, no watermark, no borders. ')

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--input', type=Path, default=ROOT/'ArtSource/Tidegate/journal')
    p.add_argument('--model', default='gpt-image-2.5-sunburst')
    p.add_argument('--install-reviewed', action='store_true', help='只安装已审阅的六张 PNG，不调用网关')
    p.add_argument('--cli', type=Path, default=Path.home()/'.codex/skills/.system/imagegen/scripts/image_gen.py')
    args = p.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    if args.install_reviewed:
        import shutil
        from PIL import Image
        from PIL import ImageDraw, ImageFont
        sheet = Image.new('RGB',(1600,1000),'#193036')
        draw = ImageDraw.Draw(sheet)
        font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
        labels = ('序 · 闸伯的信','一 · 大闸','二 · 泵站','三 · 潮位站','四 · 所长','尾声 · 归还潮汐怀表')
        draw.text((32,15),'潮闸镇 / 闸工手记',font=font,fill='#f9e8b5')
        for i, (name,label) in enumerate(zip(SCENES,labels)):
            path = args.out/(name+'.png')
            with Image.open(path) as im:
                if abs(im.width / im.height - 3) > 0.01: raise ValueError(f'{name}: inspect framing before install')
                final = im.convert('RGB').resize((1440,480),Image.Resampling.LANCZOS)
                final.save(ROOT/'Assets/Items'/f'tidegate_{name}_art.jpg',quality=92,subsampling=0,optimize=True)
                x,y = 24 + 795*(i%2), 64 + 310*(i//2)
                sheet.paste(final.resize((762,254),Image.Resampling.LANCZOS),(x,y))
                draw.text((x,y+256),label,font=font,fill='#f9e8b5')
            shutil.copyfile(path,ROOT/'ArtSource/Tidegate/journal'/(name+'.png'))
        sheet.save(ROOT/'ArtSource/Tidegate/journal/潮闸镇手记总览.jpg',quality=92)
        return
    def run(name):
        target = args.out/(name+'.png')
        if target.exists(): return
        cmd = [sys.executable, str(args.cli), 'edit', '--image', str(args.input/(name+'.png'))]
        prompt = STYLE + SCENES[name]
        if name != 'letter':
            cmd += ['--image', str(args.out/'letter.png')]
            prompt += ' Image 2 is only the shared ink, colour and hard-shadow style reference. Do not add the letter or lantern to this scene.'
        if name == 'page3':
            cmd += ['--image', str(ROOT/'Assets/Items/tidegate_handwheel.png')]
            prompt += ' Image 3 is the inventory icon for the actual quest item. This teal wheel must be clearly visible dominating the locker; do not render a separate icon, frame, or UI.'
        response = subprocess.run(cmd+['--size','auto','--quality','high','--model',args.model,'--no-augment','--prompt',prompt,'--out',str(target)],capture_output=True, text=True,encoding='utf-8',errors='replace')
        if response.returncode:
            # SDK errors may include credentials/remote bodies. Only report the error class and code.
            import re
            reason = re.findall(r'(?:[A-Za-z]+Error|Error code: [0-9]+)',response.stderr)
            raise RuntimeError(f'{name}: gateway edit failed {reason[-3:]}')
        print('edited', name, flush=True)
    if not os.environ.get('OPENAI_API_KEY') or not os.environ.get('OPENAI_BASE_URL'):
        p.error('set OPENAI_API_KEY and OPENAI_BASE_URL for the image gateway')
    run('letter')
    with concurrent.futures.ThreadPoolExecutor(3) as pool:
        list(pool.map(run, list(SCENES)[1:]))
if __name__ == '__main__':
    main()
