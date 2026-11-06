"""Compare a reproducible sample of original Ground Zero and Hidden Warehouse containers.

Requires UnityPy with TypeTreeGeneratorAPI on PYTHONPATH; reads installed levels/resources/Managed
and the verified metadata snapshot, never a save. Outputs basic item price, not vendor cash proceeds.
"""
import argparse
import collections
import hashlib
import json
import math
import re
import struct
from pathlib import Path


def probabilities(entries, low=0):
    total = sum(e["weight"] for e in entries)
    if not total:
        return [(None, 1.0)]
    cut = low * total
    assert 0 <= cut < total
    end = 0
    result = []
    for e in entries:
        start, end = end, end + e["weight"]
        weight = max(0, end - max(cut, start))
        if weight:
            result.append((e["value"], weight / (total - cut)))
    return result


def count_mean(interval, factor):
    # Official Setup rolls Uniform(x-.5,y+.5)*config then RoundToInt.
    lo, hi = (interval["x"] - .5) * factor, (interval["y"] + .5) * factor
    if interval["y"] == 0:
        return 0
    return sum(n * max(0, min(hi, n + .5) - max(lo, n - .5)) / (hi - lo)
               for n in range(max(0, math.floor(lo)), math.ceil(hi) + 1))


def path_and_active(go):
    names, active = [], True
    t = go.m_Component[0].component.read()
    while t:
        g = t.m_GameObject.read()
        names.append(g.m_Name)
        active = active and bool(g.m_IsActive)
        t = t.m_Father.read() if t.m_Father.m_PathID else None
    return "/".join(reversed(names)), active


def audit(data, catalog):
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    gm = UnityPy.load(str(data / "globalgamemanagers"))
    scenes = next(o.read_typetree()["scenes"] for o in gm.objects if o.type.name == "BuildSettings")
    scripts = {o.path_id: o.read() for o in UnityPy.load(str(data / "globalgamemanagers.assets")).objects if o.type.name == "MonoScript"}
    resources = next(iter(UnityPy.load(str(data / "resources.assets")).files.values()))
    expected_sha = catalog.read_text("utf-8").splitlines()[1].split()[-1]
    with (data / "resources.assets").open("rb") as raw:
        assert hashlib.file_digest(raw, "sha256").hexdigest() == expected_sha
    items = {}
    for line in catalog.read_text("utf-8").splitlines():
        if line and not line.startswith("#"):
            i, q, v, _, tags = line.split("|")
            items[int(i)] = (int(q), int(v), set(tags.split(",")))
    assert len(items) == 1569
    gen = TypeTreeGenerator(resources.unity_version)
    gen.load_local_game(str(data.parent))
    collection = next(o for o in resources.objects.values() if o.type.name=="MonoBehaviour"
                      and o.get_raw_data()[32:52]==b"ItemAssetsCollection")
    rows = collection.read_typetree(gen.get_nodes_up("ItemStatsSystem.dll", "ItemStatsSystem.ItemAssetsCollection"))["entries"]
    stack_price = {}
    for row in rows:
        meta=row["metaData"]; i=row["typeID"]
        assert meta["id"] == i and (meta["quality"],meta["priceEach"]) == items[i][:2]
        stack_price[i] = meta["priceEach"] * max(1, min(meta["maxStackCount"],meta["defaultStackCount"]))
    def name(serial, ptr):
        if not ptr or not ptr["m_PathID"]:
            return None
        assert serial.externals[ptr["m_FileID"]-1].path == "resources.assets", ptr
        raw = resources.objects[ptr["m_PathID"]].get_raw_data()
        size, = struct.unpack_from("<i", raw, 28)
        return raw[32:32 + size].decode("utf-8")
    def components(scene, seek):
        f = data / ("level" + str(next(i for i,s in enumerate(scenes) if Path(s).stem == scene)))
        serial = next(iter(UnityPy.load(str(f)).files.values()))
        for o in serial.objects.values():
            if o.type.name != "MonoBehaviour": continue
            beh = o.read(check_read=False)
            ref = beh.m_Script
            if ref.m_FileID < 1 or serial.externals[ref.m_FileID-1].path != "globalgamemanagers.assets": continue
            s = scripts[ref.m_PathID]
            if s.m_ClassName != seek or not beh.m_Enabled: continue
            go = beh.m_GameObject.read()
            path, active = path_and_active(go)
            if not active: continue
            full = (s.m_Namespace + "." if s.m_Namespace else "") + s.m_ClassName
            yield serial, o.path_id, go.m_Name, path, o.read_typetree(gen.get_nodes_up(s.m_AssemblyName,full))
    result = {}
    for key, main, level in (("zero","Level_GroundZero_Main","Level_GroundZero_1"),
                             ("warehouse","Level_HiddenWarehouse_Main","Level_HiddenWarehouse")):
        configs = list(components(main, "LevelConfig"))
        assert len(configs) == 1, (main, len(configs))
        cfg = configs[0][-1]
        mult, grade_mult = cfg["lootboxItemCountMultiplier"], cfg["lootBoxHighQualityChanceMultiplier"]
        row = dict(scene=level, level_config_id=configs[0][1], count_factor=mult, quality_factor=grade_mult, boxes={})
        for serial, ident, obj_name, path, box in sorted(components(level,"LootBoxLoader"),key=lambda x:x[1]):
            group = next((g for g in ("Normal","Medic","Tools3","Fishing","Lux") if re.fullmatch("LootBox_"+g+r"(?:_\d+)?",obj_name)),None)
            if not group or group in row["boxes"] or not box["autoSetup"] or not box["activeChance"] or box["dropOnSpawnItem"]:continue
            assert not box["randomFromPool"]
            excluded = {name(serial,p) for p in box["excludeTags"]} | {"AdvancedDebuffMode"}
            if not box["dontExcludeSpecial"]: excluded.add("Special")
            fixed = box["fixedItems"]; chance = box["fixedItemSpawnChance"]
            tags = [(name(serial,t),w) for t,w in probabilities(box["tags"]["entries"])]
            # Setup uses the level quality fraction in BOTH ignoreLevelConfig cases; only item count ignores the factor.
            grades = probabilities(box["qualities"]["entries"],1 - 1/grade_mult)
            p_quality = collections.defaultdict(float); value = unit_value = 0
            for tag,tp in tags:
                for quality,qp in grades:
                    for q in range(quality if quality is not None else 0, -1, -1):
                        band = [(iq,iv,stack_price[i]) for i,(iq,iv,it) in items.items() if (not tag or tag in it) and not it.intersection(excluded)
                                and iq==q]
                        if band: break
                    if not band: continue
                    share=tp*qp/len(band)
                    for iq,iv,stack in band: p_quality[iq]+=share; unit_value+=share*iv; value+=share*stack
            n=count_mean(box["randomCount"], 1 if box["ignoreLevelConfig"] else mult)
            fixed_value=sum(stack_price[i] for i in fixed if i>0)
            row["boxes"][group]=dict(component=ident,path=path,active_chance=round(box["activeChance"],3),
                                     configured_count=box["randomCount"], mean_random_count=round(n,3),
                                     slot_q4plus=round(sum(v for k,v in p_quality.items() if k>=4),5),
                                     slot_unit_value=round(unit_value,2), slot_default_stack_value=round(value,2), fixed_ids=fixed,
                                     estimated_base_value_excluding_cash=round(n*value+fixed_value*chance,2))
        result[key]=row
    return result


if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("game_data",type=Path);p.add_argument("--catalog",type=Path,default=Path("tests/fixtures/TidegateTreasure/catalog.txt"))
    a=p.parse_args();print(json.dumps(audit(a.game_data,a.catalog),ensure_ascii=False,indent=2))
