"""Read authored enemy rolls from installed Duckov scenes, without running Unity or loading saves.

At spawn factor 1, RandomCharacterSpawner.CreateAsync's installed IL rolls Random.Range(x,y+1).
Selectors use Random.Range(x,y) then sample groups without replacement. Counts are potential
characters configured in this scene, not concurrent enemies or an observed raid population.
"""
from argparse import ArgumentParser
from collections import Counter
from pathlib import Path
import hashlib
import struct


def scan(data, wanted):
    import UnityPy
    gm = UnityPy.load(str(data / "globalgamemanagers"))
    scenes = next(x.read_typetree()["scenes"] for x in gm.objects if x.type.name == "BuildSettings")
    scripts = {x.path_id:x.read().m_ClassName for x in UnityPy.load(str(data/"globalgamemanagers.assets")).objects
               if x.type.name == "MonoScript"}
    classes = {"RandomCharacterSpawner","WaveCharacterSpawner","CharacterSpawnerGroup","CharacterSpawnerGroupSelector","CharacterSpawnerRoot"}
    for i, scene in enumerate(scenes):
        if not any(s in scene for s in wanted):
            continue
        path = data / f"level{i}"
        serial = next(iter(UnityPy.load(str(path)).files.values()))
        objects = {}
        for x in serial.objects.values():
            if x.type.name != "MonoBehaviour":
                continue
            a=x.read(check_read=False); script=a.m_Script
            if script.m_FileID<=0 or serial.externals[script.m_FileID-1].path!="globalgamemanagers.assets":
                continue
            cls = scripts.get(script.m_PathID)
            if cls not in classes:
                continue
            tr=a.m_GameObject.read().m_Component[0].component.read()
            active=bool(a.m_Enabled); parts=[]
            while tr:
                go=tr.m_GameObject.read(); parts.append(go.m_Name); active=active and go.m_IsActive
                tr=tr.m_Father.read() if tr.m_Father.m_PathID else None
            raw=x.get_raw_data()
            def field(fmt,k): return struct.unpack_from("<"+fmt,raw,k)[0]
            def ref(k):
                assert field("i",k)==0, (scene,x.path_id,"external spawner ref",k)
                return field("q",k+4)
            assert field("i",28)==0, (scene,x.path_id,"unexpected named MonoBehaviour")
            row=dict(cls=cls, active=bool(active), name="/".join(reversed(parts)))
            if cls in ("RandomCharacterSpawner","WaveCharacterSpawner"):
                at=72 if cls=="RandomCharacterSpawner" else 68
                n=field("i",at); assert 0<=n<=100
                lo,hi=struct.unpack_from("<ii",raw,at+4+16*n)
                assert 0<=lo<=hi<=200, (scene,x.path_id,lo,hi)
                row.update(lo=lo,hi=hi,n=n)
            elif cls=="CharacterSpawnerGroup":
                n=field("i",52); assert n>=0 and 56+12*n==len(raw)
                row.update(refs=[ref(56+12*j) for j in range(n)])
            elif cls=="CharacterSpawnerGroupSelector":
                n=field("i",44); assert n>=0 and 56+12*n==len(raw)
                row.update(refs=[ref(48+12*j) for j in range(n)],select=struct.unpack_from("<ii",raw,48+12*n))
            else:
                chance=field("f",48); weather=field("i",88); assert 0<=chance<=1 and 0<=weather<=20
                row.update(chance=chance, gated=bool(field("i",32) or field("i",64) or field("i",84)), ref=ref(92+4*weather))
            objects[x.path_id]=row
        def bounds(idx):
            r=objects[idx]
            if "lo" in r:
                assert r["n"] or not r["hi"],r
                return (r["lo"],r["hi"])
            choices=[bounds(v) if v else (0,0) for v in r["refs"]]
            if "select" not in r:
                return (sum(c[0] for c in choices),sum(c[1] for c in choices))
            low,up=r["select"]; n=len(choices); low,up=min(n,low),min(n,up)
            assert 0<=low<=up
            up=max(low,up-1)
            return sum(sorted(c[0] for c in choices)[:low]),sum(sorted((c[1] for c in choices),reverse=True)[:up])
        root=[r for r in objects.values() if r["cls"]=="CharacterSpawnerRoot" and r["active"]]
        total=low_total=0
        print(scene, path.name, "sha256="+hashlib.sha256(path.read_bytes()).hexdigest())
        for r in root:
            low,high=bounds(r["ref"]) if r["ref"] else (0,0)
            low_total+=low if r["chance"]>=1 and not r["gated"] else 0
            total+=high if r["chance"]>0 else 0
            print(f'  {r["name"]}: {low}..{high} (p={r["chance"]:.2f},trigger/time/weather={r["gated"]})')
        print("  active roots:",len(root),"component counts:",dict(Counter(v["cls"] for v in objects.values())),
              "ungated configured minimum / all eligible upper:",low_total,total)

if __name__=="__main__":
    ap=ArgumentParser(description=__doc__); ap.add_argument("data",type=Path); ap.add_argument("scenes",nargs="+")
    args=ap.parse_args(); scan(args.data,args.scenes)
