"""验证真实交付包的压缩、资源预算和重压前后载荷；不代替实机性能采样。"""
from pathlib import Path
import argparse
import gc
import hashlib
import json
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Build/equipment-inspection'))


def inspect(path):
    import UnityPy
    from UnityPy.files.BundleFile import BundleFile
    from UnityPy.enums import TextureFormat
    records=[]
    original=BundleFile.decompress_data
    def capture(self,data,size,flags,index=0):
        result=original(self,data,size,flags,index)
        records.append((int(flags)&63,bytes(result)))
        return result
    BundleFile.decompress_data=capture
    try:
        env=UnityPy.load(str(path))
    finally:
        BundleFile.decompress_data=original
    row={'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        'stream_sha256':hashlib.sha256(b''.join(r[1] for r in records[1:])).hexdigest(),
        'uncompressed_bytes':sum(len(r[1]) for r in records[1:]),
        'compression':sorted({r[0] for r in records[1:]}),
        'container':sorted(env.container),'textures':[],'meshes':[],'sprites':[]}
    for obj in env.objects:
        if obj.type.name=='Texture2D':
            d=obj.read()
            row['textures'].append({'name':d.m_Name,'format':TextureFormat(d.m_TextureFormat).name,
                'width':d.m_Width,'height':d.m_Height,'mips':d.m_MipCount,'readable':d.m_IsReadable,
                'payload_bytes':len(d.get_image_data()) if d.m_Width and d.m_Height else 0})
        elif obj.type.name=='Mesh':
            d=obj.read()
            row['meshes'].append({'name':d.m_Name,'vertices':d.m_VertexData.m_VertexCount,
                'triangles':sum(s.indexCount//3 for s in d.m_SubMeshes), 'readable':d.m_IsReadable})
        elif obj.type.name=='Sprite':
            d=obj.read()
            texture=d.m_RD.texture.read()
            row['sprites'].append({'name':d.m_Name,'texture':texture.m_Name,'width':texture.m_Width,'height':texture.m_Height,
                'rectWidth':d.m_Rect.width,'rectHeight':d.m_Rect.height,'pixelsPerUnit':d.m_PixelsToUnits})
    if path.name == 'production_icons':
        from daily_report_art_contract import DAILY_REPORT_ALIAS, measure_chrome
        if DAILY_REPORT_ALIAS in env.container:
            layout = json.loads((ROOT/'Assets/Data/DailyReportLayout.json').read_text(encoding='utf8'))
            row['daily_report_chrome'] = measure_chrome(env.container[DAILY_REPORT_ALIAS].read().image, layout)
    return row


def validate(row,name):
    errors=[]
    if not set(row['compression'])<={0,2,3}: errors.append('LZMA/non-LZ4 payload')
    for t in row['textures']:
        if 'Crunched' in t['format']: errors.append('Crunch: '+t['name'])
        if t['width'] and t['height'] and t['readable']: errors.append('Readable texture: '+t['name'])
    if name in ('birthday_cake','bossrush_ticket'):
        expected = 'BirthdayCake' if name=='birthday_cake' else 'BossRushTicket2'
        target=[t for t in row['textures'] if t['name']==expected]
        if len(target)!=1 or any(t['width'] not in (256,512) or t['height'] not in (256,512) or t['format'] not in ('BC7','DXT5') for t in target):
            errors.append('Legacy item icon size/format: '+expected)
        if not any(s['texture']==expected for s in row.get('sprites',[])): errors.append('Legacy Sprite missing: '+expected)
        for sprite in row.get('sprites',[]):
            if sprite['texture']==expected and (sprite.get('pixelsPerUnit',0)<=0 or
                any(abs(sprite.get(axis,0)/sprite['pixelsPerUnit']-10.24)>.001 for axis in ('rectWidth','rectHeight'))):
                errors.append('Legacy Sprite display size: '+expected)
    if name in ('bossrush_daily_mailbox', 'bossrush_campaign_board', 'bossrush_backmountain_showcase', 'petnest_relic_nest'):
        profile=json.loads((ROOT/'tools/building_model_manifest.json').read_text(encoding='utf8'))
        entry=next(e for e in profile['buildings'] if e['bundle']==name)
        if len(row['meshes'])!=1 or any(m['readable'] or not 0<m['triangles']<=profile['maxTriangles'] for m in row['meshes']):
            errors.append('Building mesh budget/readability')
        if len(row['textures'])!=1 or any(t['format']!='BC7' or t['readable'] or (t['width'],t['height'])!=(profile['textureSize'],profile['textureSize']) for t in row['textures']):
            errors.append('Building albedo size/format/readability')
        if [p.lower() for p in row['container']]!=[entry['prefab'].lower()]: errors.append('Building named prefab contract')
    if name=='production_icons':
        profile=json.loads((ROOT/'tools/production_icon_manifest.json').read_text(encoding='utf8'))
        expected={r['path'].lower() for r in profile['icons']}
        if set(row['container'])!=expected: errors.append('Production icon aliases differ from manifest')
        if len(row.get('sprites',[]))!=len(expected): errors.append('Production Sprite count differs from manifest')
        budgets={Path(r['path']).stem:r['maxSize'] for r in profile['icons']}
        for t in row['textures']:
            if t['format'] not in ('BC7','DXT5') or max(t['width'],t['height'])>budgets.get(t['name'],0):
                errors.append('Production icon compression/size: '+t['name'])
        from daily_report_art_contract import validate_chrome
        layout=json.loads((ROOT/'Assets/Data/DailyReportLayout.json').read_text(encoding='utf8'))
        errors.extend(validate_chrome(row.get('daily_report_chrome'), layout))
    if name=='energyshield_totem_model':
        if sum(m['triangles'] for m in row['meshes'])>40000: errors.append('Shield triangle budget > 40000')
        if len(row['meshes'])!=1: errors.append('Shield mesh contract changed')
    if name=='modeg_presentation':
        if row['bytes']>256*1024: errors.append('Mode G bundle budget > 256 KiB')
        expected={'modeg_echo_emblem':(256,256),'modeg_echo_banner':(1024,576)}
        actual={t['name']:(t['width'],t['height']) for t in row['textures']}
        if actual!=expected: errors.append('Mode G texture dimensions contract')
    if name=='sky_island_raid':
        nav=[m for m in row['meshes'] if m['name']=='SkyIslandNavigation']
        if len(nav)!=1 or not nav[0]['readable'] or not 0<nav[0]['vertices']<=4095: errors.append('Navigation contract')
        names={'geology_handpainted_native','wood_handpainted_vanilla','sky_mural','stone_handpainted_vanilla',
            'sky_cloth','grass_handpainted_vanilla','roof_handpainted_terracotta'}
        targets=[t for t in row['textures'] if t['name'] in names]
        if len(targets)!=7 or any(t['format']!='BC7' for t in targets): errors.append('Environment BC7 contract')
        if any(m['readable'] and m['name'].startswith('VIS_') and (m['vertices']>65535 or m['name'].endswith('_DriftBounds')) for m in row['meshes']):
            errors.append('Render-only meshes retain CPU copy')
    return errors


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',type=Path,default=ROOT)
    p.add_argument('--work',type=Path,help='Compare recompressed stream to rebuilt/original source')
    p.add_argument('--report',type=Path,required=True)
    args=p.parse_args()
    manifest=json.loads((ROOT/'tools/resource_release_manifest.json').read_text(encoding='utf8'))
    rows=[]; failures=[]
    for relative in manifest['bundles']:
        file=args.root/relative
        try:
            row=inspect(file); row['path']=relative
            errors=validate(row,file.name)
            if args.work:
                rebuilt=args.work/'rebuilt'/file.name
                before=inspect(rebuilt if rebuilt.is_file() else ROOT/relative)
                if before['stream_sha256']!=row['stream_sha256']: errors.append('Recompression changed serialized payload')
                row['recompression_source_bytes']=before['bytes']
            row['errors']=errors
            rows.append(row)
            failures.extend(relative+': '+e for e in errors)
            print(relative, 'PASS' if not errors else errors, flush=True)
        except Exception as exc:
            failures.append(relative+': '+repr(exc))
        gc.collect()
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps({'bundles':rows,'errors':failures},ensure_ascii=False,indent=2),encoding='utf8')
    print('Resource verification:',len(rows),'bundles;',len(failures),'errors')
    return bool(failures)


if __name__=='__main__':
    raise SystemExit(main())
