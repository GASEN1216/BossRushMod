"""遗魂反馈只显示真实到账增量，并串行合并短气泡。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
drop = clean_source((ROOT / 'PetNest/PetNestDropService.cs').read_text(encoding='utf-8-sig'))
assert 'int credited = PetNestService.GetSouls(lineageKey) - before;' in drop, '必须用真实余额差判断到账'
assert 'if (credited > 0)' in drop and 'PetNestSoulNotice.Queue(lineageKey, credited);' in drop, '未到账不能发成功气泡'
assert drop.index('GetPrefab(RelicEggConfig.TYPE_ID) == null') < drop.index('egg = ItemAssetsCollection.InstantiateSync'), '蛋实例化前必须拒绝空壳 prefab'
notice = clean_source((ROOT / 'PetNest/PetNestSoulNotice.cs').read_text(encoding='utf-8-sig'))
for statement in ('notice._pending[lineageKey] = old + amount;',
                  'DialogueBubblesManager.Show(text.ToString(), _anchor, 2.1f, false, true, 100000f, 1f);',
                  '_bubble.Target != _anchor', '_bubble.Interact();', '_bubble.gameObject.SetActive(false);',
                  'BossRushUI.IsGamePaused()', 'BossRushUI.IsOfficialHudHidden()', 'private void OnDisable()', 'private void OnDestroy()'):
    assert statement in notice, '缺少气泡合并/生命周期约束：' + statement
# 官方每字 await 至少一帧，speed 再高也不能代替 fade 后持续调用跳字入口。
assert re.search(r'if\s*\(_showing\)\s*\{\s*if\s*\(_bubble != null && _bubble.Target == _anchor\)\s*_bubble.Interact\(\);\s*return;\s*\}', notice), '显示中的自有气泡必须持续跳字，避免多行反馈逐帧展开'
assert '_anchor.gameObject.SetActive(true);' in notice, '重新显示前必须激活专属锚点'
assert notice.index('_anchor.gameObject.SetActive(true);') < notice.index('UniTask request = DialogueBubblesManager.Show'), '重新显示前必须激活专属锚点'
cancel = notice[notice.index('private void CancelCurrentBubble()'):notice.index('private void OnDisable()')]
assert re.search(r'if\s*\(_anchor != null\)\s*_anchor.gameObject.SetActive\(false\);', cancel), '取消必须停用专属 target，让官方异步任务自行退出'
assert cancel.index('_anchor.gameObject.SetActive(false);') < cancel.index('_bubble.gameObject.SetActive(false);'), '先让官方任务看到取消条件，再释放可复用的气泡对象'
print('PetNestSoulFeedbackGuard: PASS')
