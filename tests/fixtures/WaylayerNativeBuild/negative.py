"""Isolated corruption checks; shared production source is never modified."""
from pathlib import Path
import hashlib, json, os, shutil, subprocess, sys, tempfile

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent

def main():
    out=ROOT/'Build/runtime-regressions/WaylayerNativeBuild'
    work=Path(tempfile.mkdtemp(prefix='negative-',dir=out))
    source=ROOT/'SkyIsland/CoopWaylayerNativeBuildScope.cs'
    original=source.read_bytes();text=original.decode('utf-8-sig');rows=[]
    behavior=[
      ('main-player-exclusion','return !IsPlayerTree(item);','return true;','main existing player root'),
      ('single-use-native-admission','if (!mutation || scope == null || scope.Consumed || owners == 0','if (!mutation || scope == null || false || owners == 0','notification cannot reuse'),
      ('frozen-corpse-children','!current.Children.Remove(item)','false','post prefix inserted ticket'),
      ('corpse-original-ordinary-merge','return ItemUtilities.AddAndMerge(inventory, item, position);','return false;','ordinary corpse loot retains'),
      ('corpse-anchor-finally','finally { CoopWaylayerClaims.TrackCorpse(item, inventory); }','finally { }','real corpse inventory anchors'),
      ('native-ticket-root-registration','if (ticket) RegisterTicketRoot(__result);','if (false) RegisterTicketRoot(__result);','native snapshot assembly registers corpse root'),
      ('nested-corpse-finalizer','? __state : null;','? (CorpseFrame)null : null;','ordinary and tracked nested corpse calls restore'),
      ('native-builder-finalizer','? __state.Previous : null;','? (Frame)null : null;','native inventory and slot child assembly'),
    ]
    for name,old,new,expected in behavior:
        assert text.count(old)==1,(name,text.count(old))
        altered=work/(name+'.cs');altered.write_text(text.replace(old,new),encoding='utf-8')
        result=subprocess.run([sys.executable,str(HERE/'run.py')],cwd=ROOT,env=dict(os.environ,WAYLAYER_NATIVE_SOURCE=str(altered)),stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        output=result.stdout.decode('utf-8','replace');(work/(name+'.log')).write_text(output,encoding='utf-8')
        rows.append(dict(name=name,kind='actual-native-behavior',exit=result.returncode,expected=expected,matched=result.returncode!=0 and expected in output))
    guard=ROOT/'tests/WaylayerNativeBuildGuard.py'
    guard_mutations=[
      ('optional-native-type','EscapeFromDuckovCoopMod.ItemTool, EscapeFromDuckovCoopMod", false','EscapeFromDuckovCoopMod.ItemTool, EscapeFromDuckovCoopMod", true','optional install contract'),
      ('unique-build-sites','roots != 1 || additions != 1 || plugs != 1','roots != 0 || additions != 1 || plugs != 1','native snapshot sites'),
      ('unique-corpse-sites','boxes != 1 || additions != 1','boxes != 0 || additions != 1','native corpse sites'),
      ('corpse-destination-identity','!ReferenceEquals(current.Box.Inventory, inventory)','false','corpse exact transfer'),
      ('ordinary-root-o1','!ticketRoots.TryGetValue(item, out tracked)','false','ordinary corpse'),
    ]
    for name,old,new,expected in guard_mutations:
        assert text.count(old)==1,(name,text.count(old))
        isolated=work/name;(isolated/'SkyIsland').mkdir(parents=True);(isolated/'tests').mkdir()
        (isolated/'SkyIsland/CoopWaylayerNativeBuildScope.cs').write_text(text.replace(old,new),encoding='utf-8')
        shutil.copy2(ROOT/'SkyIsland/CoopWaylayerClaims.cs',isolated/'SkyIsland/CoopWaylayerClaims.cs')
        for item in (guard,ROOT/'tests/cs_source_util.py'):shutil.copy2(item,isolated/'tests'/item.name)
        result=subprocess.run([sys.executable,str(isolated/'tests'/guard.name)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        output=result.stdout.decode('utf-8','replace');(work/(name+'.log')).write_text(output,encoding='utf-8')
        rows.append(dict(name=name,kind='structure-guard',exit=result.returncode,expected=expected,matched=result.returncode!=0 and expected in output))
    restored=subprocess.run([sys.executable,str(HERE/'run.py')],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (work/'restored.log').write_bytes(restored.stdout)
    result={'source_sha256':hashlib.sha256(original).hexdigest(),'source_unchanged':source.read_bytes()==original,'cases':rows,'restored_exit':restored.returncode,'all_expected':all(row['matched'] for row in rows) and restored.returncode==0 and source.read_bytes()==original}
    (work/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    for row in rows:print(('PASS' if row['matched'] else 'FAIL')+' '+row['name'])
    print('Negative evidence: '+str(work/'summary.json'))
    return 0 if result['all_expected'] else 1
if __name__=='__main__':raise SystemExit(main())
