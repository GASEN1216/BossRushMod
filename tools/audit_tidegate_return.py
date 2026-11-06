"""Identify shipped Base-scene lootboxes, including enablement of every ancestor. No saves or live process."""
from argparse import ArgumentParser
from pathlib import Path
import hashlib
import re


def inspect_scenes(data):
    import UnityPy
    gm = UnityPy.load(str(data / "globalgamemanagers"))
    scenes = next(x.read_typetree()["scenes"] for x in gm.objects if x.type.name == "BuildSettings")
    scripts = {x.path_id: x.read().m_ClassName for x in UnityPy.load(str(data / "globalgamemanagers.assets")).objects
               if x.type.name == "MonoScript"}
    for name in ("Base", "Base_SceneV2"):
        path = "Assets/Scenes/" + name + ".unity"
        file = data / ("level" + str(scenes.index(path)))
        serial = next(iter(UnityPy.load(str(file)).files.values()))
        print(path, file.name, "sha256=" + hashlib.sha256(file.read_bytes()).hexdigest())
        for x in serial.objects.values():
            if x.type.name != "MonoBehaviour":
                continue
            beh = x.read(check_read=False)
            script = beh.m_Script
            if script.m_FileID <= 0 or serial.externals[script.m_FileID-1].path != "globalgamemanagers.assets":
                continue
            cls = scripts.get(script.m_PathID)
            if cls not in ("InteractableLootbox", "LevelManager"):
                continue
            go = beh.m_GameObject.read()
            t = go.m_Component[0].component.read()
            enabled, names = bool(beh.m_Enabled), []
            while t:
                go = t.m_GameObject.read()
                enabled = enabled and go.m_IsActive
                names.append(go.m_Name + ("" if go.m_IsActive else "(inactive)"))
                t = t.m_Father.read() if t.m_Father.m_PathID else None
            print(f"{cls} component={x.path_id} go={beh.m_GameObject.m_PathID} active={enabled} {'/'.join(reversed(names))}")


def inspect_log(path):
    # Some mod payloads contain Unicode line separators; report the physical Player.log line.
    lines = [line.rstrip("\r") for line in path.read_bytes().decode("utf-8-sig").split("\n")]
    for i, s in enumerate(lines):
        if "at LevelManager.get_LootBoxInventories" in s and any("InteractableLootbox.Start" in v for v in lines[i:i+12]):
            prev = lines[:i]
            for token in ("UI提示: 正在返回基地", "[Tidegate] CLEANUP", "-> Base_SceneV2"):
                match = next((n for n in range(i - 1, -1, -1) if token in prev[n]), None)
                if match is not None:
                    print(f"line {match + 1}: {lines[match]}")
            print(f"line {i + 1}: {s.strip()} (official Start stack lacks instance ID)")
    start = next((i for i,v in enumerate(lines) if "TIDE_DRILL_REGION_SOUNDS |" in v), None)
    if start is None:
        return
    for case in ("TIDE_PERF_DOCK_10S", "TIDE_PERF_TOWN_10S"):
        stop = next((i for i in range(start+1,len(lines)) if case + " |" in lines[i]), None)
        if stop is None:
            continue
        times = [float(x) for row in lines[start+1:stop] for x in re.findall(r"Path Completed : Computation Time ([\d.]+) ms", row)]
        print(lines[stop]); print(f"Between report lines (includes station settling): {len(times)} path computations,"
                                f" {sum(times):.2f} ms in total, maximum {max(times, default=0):.2f} ms; logging/AI costs not measured.")
        start = stop


if __name__ == "__main__":
    p = ArgumentParser(description=__doc__)
    p.add_argument("game_data", type=Path); p.add_argument("--log", type=Path)
    args = p.parse_args(); inspect_scenes(args.game_data)
    if args.log:
        inspect_log(args.log)
