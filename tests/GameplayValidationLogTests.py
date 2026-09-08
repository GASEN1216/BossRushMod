"""真实日志解析回归：旧构建、末轮隔离、失败优先和缺失结果不能变绿。"""
import importlib.util
from pathlib import Path
import tempfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('coverage', ROOT / 'tools/gameplay_coverage.py')
coverage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage)
data = {'features': [{'automatic': ['A', 'B'], 'manual': [{'id': 'M_A'}]}]}
header = 'BossRush 完整玩法验收 | runId=new | UTC=test\nBUILD | mvid=new-dll\n'

with tempfile.TemporaryDirectory(prefix='bossrush-validation-log-') as directory:
    path = Path(directory) / 'Player.log'

    def check(body, expected, prefix=False, build=None):
        text = header + body
        if prefix:
            text = '\n'.join('[BossRushValidation] ' + line for line in text.splitlines())
        path.write_text(text, encoding='utf-8')
        result = coverage.analyze(data, path, build)
        assert result['status'] == expected, result
        return result

    check('A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS | fail=0', 'PASS', prefix=True)
    check('A | PASS | 1ms\nSUMMARY | PASS | fail=0', 'INCOMPLETE')
    check('A | FAIL | 1ms\nA | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS | fail=0', 'FAIL')
    check('A | PASS | 1ms\nB | SKIP | 1ms\nSUMMARY | PASS | fail=0', 'INCOMPLETE')
    check('A | PASS | 1ms\nB | PASS | 1ms', 'INCOMPLETE')
    check('A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS | fail=0', 'BUILD_MISMATCH', build='another-dll')
    check('A | PASS | 1ms\nB | PASS | 1ms\nRUNTIME_ERROR | source=BossRush | exception\nSUMMARY | PASS', 'FAIL')
    check('A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS\nRECOVERY | CANCELLED', 'CANCELLED')
    check('A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | TIMEOUT', 'TIMEOUT')
    diagnostic = check('A | PASS | 1ms\nB | PASS | 1ms\nRUNTIME_DIAGNOSTIC | [BossRush] [ERROR] injected\nSUMMARY | PASS', 'PASS')
    assert diagnostic['needs_log_review'] and len(diagnostic['diagnostic_details']) == 1
    old = header.replace('runId=new', 'runId=old') + 'A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS\n'
    path.write_text(old + '[BossRushValidation] ' + header + 'A | PASS | 1ms\n', encoding='utf-8')
    result = coverage.analyze(data, path)
    assert result['status'] == 'INCOMPLETE' and result['auto_not_passed'] == ['B'], result
    path.write_text('A | PASS | 1ms\nB | PASS | 1ms\nSUMMARY | PASS', encoding='utf-8')
    assert coverage.analyze(data, path)['status'] == 'INCOMPLETE'

print('GameplayValidationLogTests: PASS (12 cases)')
