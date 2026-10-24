"""Mutate only isolated copies and require dedicated music identity/lifecycle failures."""
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / 'tests'))
import EclipseBloomIntegrationGuard as guard


def main():
    spec = importlib.util.spec_from_file_location('eclipse_music_fixture', HERE / 'run.py')
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    output = ROOT / 'Build/runtime-regressions/EclipseBloomMusic-negative'
    output.mkdir(parents=True, exist_ok=True)
    paths = sorted(set(fixture.PRODUCTION) | set(guard.FIXED_PATHS)
                   | {path.relative_to(ROOT).as_posix() for path in (ROOT / guard.PREFIX).glob('*.cs')})
    before = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in paths}
    with redirect_stdout(io.StringIO()):
        guard.validate(ROOT)
    code, baseline = fixture.run_fixture(output_root=output / 'baseline')
    (output / 'baseline.log').write_text(baseline, encoding='utf-8')
    if code:
        print('FAIL: baseline is red before music mutation probes')
        print(baseline[-2500:])
        return 1
    controller = guard.PREFIX + 'EclipseBloomAbilityController.cs'
    probes = [
        ('start_uses_other_boss', 'runtime', controller,
         'PlayBossBGM(BossBgmKeys.EclipseBloom, boss);', 'PlayBossBGM(BossBgmKeys.PhantomWitch, boss);',
         'each flower acquires its original dedicated EclipseBloom music key'),
        ('release_uses_other_owner', 'runtime', controller,
         'StopBossBGM(BossBgmKeys.EclipseBloom, boss);', 'StopBossBGM(BossBgmKeys.EclipseBloom, null);',
         'repeated cancellation and destruction release exactly the first flower music owner once'),
        ('destroy_does_not_release', 'runtime', controller,
         'StopBattle(false);\n            if (preset != null)', 'if (preset != null)',
         'direct pool destruction releases dedicated music through actual OnDestroy'),
        ('constant_data_key_mismatch', 'static', 'Audio/BossBgmCoordinator.cs',
         'EclipseBloom = "EclipseBloom"', 'EclipseBloom = "PhantomWitch"', '花冠独立音乐 key 必须匹配数据表'),
        ('table_aliases_other_track', 'static', 'Assets/Data/Audio/BgmTracks.json',
         '"boss_eclipse_bloom_loop.wav"', '"boss_witch_loop.wav"', '花冠必须登记唯一的独立 WAV 循环曲目'),
        ('loop_disabled', 'static', 'Assets/Data/Audio/BgmTracks.json',
         '"boss_eclipse_bloom_loop.wav", "loop": true', '"boss_eclipse_bloom_loop.wav", "loop": false',
         '花冠必须登记唯一的独立 WAV 循环曲目'),
        ('ignored_phase', 'static', 'Assets/Data/Audio/BgmTracks.json',
         '"boss_eclipse_bloom_loop.wav", "loop": true, "phase": 0',
         '"boss_eclipse_bloom_loop.wav", "loop": true, "phase": 1', '花冠必须登记唯一的独立 WAV 循环曲目'),
    ]
    results = []
    for name, kind, target, original, replacement, expected in probes:
        copy = Path(tempfile.mkdtemp(prefix=name + '-', dir=output))
        for relative in paths:
            destination = copy / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes((ROOT / relative).read_bytes())
        path = copy / target
        text = path.read_text(encoding='utf-8-sig')
        assert text.count(original) == 1, name + ': mutation anchor must occur once'
        path.write_text(text.replace(original, replacement), encoding='utf-8')
        if kind == 'runtime':
            code, log = fixture.run_fixture(source_root=copy, output_root=copy / 'build')
            passed = code != 0 and expected in log and 'FAIL dedicated_music_owner_lifecycle' in log
        else:
            capture = io.StringIO()
            try:
                with redirect_stdout(capture):
                    guard.validate(copy)
                passed = False
                log = capture.getvalue()
            except AssertionError as error:
                log = str(error)
                passed = expected in log
        (copy / 'probe.log').write_text(log, encoding='utf-8')
        results.append({'name': name, 'passed': passed, 'expected': expected, 'copy': str(copy)})
        print(('PASS ' if passed else 'FAIL ') + name, flush=True)
        if not passed:
            print(log[-2500:])
    unchanged = all(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest for name, digest in before.items())
    (output / 'results.json').write_text(json.dumps({'probes': results, 'production_unchanged': unchanged,
        'production_sha256': before}, ensure_ascii=False, indent=2), encoding='utf-8')
    return 0 if unchanged and all(row['passed'] for row in results) else 1


if __name__ == '__main__':
    raise SystemExit(main())
