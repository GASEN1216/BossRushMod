"""Guard: 纯演出的随机事件不占玩家的单局事件配额，有玩法的事件必须占。

## 为什么要有这份

「鸭生无常」的频率档（低 2 / 中 3 / 高 5）是玩家为**会发生点什么的事件**留的额度：
空投抢不抢、血月硬不硬吃、乱入的 Boss 打不打。鸭王的烟花与鸭群巡游按设计就是零伤害、
零影响、零奖励的演出（见各自类的注释与玩家 Wiki）；在修复前它们照样 `_eventsFiredThisRun++`，
于是低频档一局只有两次事件时，抽中一次纯演出就等于四分之一的对局白等一场。

修复后由 `RandomEventBase.ConsumesRunBudget` 区分，本守卫钉住三件事：

1. 基类默认 **占**配额（新事件不写这一项就是有玩法的事件，默认安全）；
2. 调度器只在 `ConsumesRunBudget` 为真时计数，且退款分支仍按 `_activeEventCounted` 走
   （否则纯演出事件异步失败会把配额扣成负数，或有玩法的事件失败后不退款）；
3. 覆写成 false 的**只有**登记在案的两个纯演出事件，且这两个类里不出现任何
   奖励 / 伤害 / 刷敌 / 词条 的符号——真给了好处就该占配额。

它证明的是接线与登记，不证明运行时观感：银幕上的烟花好不好看只能实机看。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

MODELS = 'RandomEvents/RandomEventModels.cs'
DIRECTOR = 'RandomEvents/RandomEventDirector.cs'
CATALOG = 'RandomEvents/RandomEventCatalog.cs'
FUN = 'RandomEvents/RandomEventCatalog_Fun.cs'
EFFECTS = 'RandomEvents/RandomEventEffectsBridge.cs'

# 纯演出事件的白名单。往这里加条目 = 声明「这个事件对玩家没有任何玩法回报」，
# 要同时改玩家 Wiki 的频率一节与 repowiki；给了回报就不该进这里。
FLAVOR_CLASSES = ('RandomEventFireworks', 'RandomEventDuckParade')

# 纯演出事件里不许出现的符号：出现任何一个都说明它其实给了好处或改了战场。
PAYOFF_TOKENS = ('Cash', 'Reward', 'Lootbox', 'Airdrop', 'Buff', 'Modifier',
                 'Damage', 'Hurt', 'SpawnEnemy', 'Stock', 'Aggro', 'Alert')

errors = []


def read(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append('读不到 ' + rel)
        return ''
    return clean_source(path.read_text(encoding='utf-8-sig'))


def body(src, signature, label):
    """取一个成员的花括号块。"""
    start = src.find(signature)
    if start < 0:
        errors.append('%s 找不到 %s' % (label, signature))
        return ''
    opening = src.find('{', start)
    if opening < 0:
        errors.append('%s 的 %s 没有块体' % (label, signature))
        return ''
    depth = 0
    for i in range(opening, len(src)):
        if src[i] == '{':
            depth += 1
        elif src[i] == '}':
            depth -= 1
            if depth == 0:
                return src[start:i + 1]
    errors.append('%s 的 %s 花括号不配对' % (label, signature))
    return ''


def class_decl(src, name):
    """类声明那一行的原文（`internal sealed [partial ]class X : RandomEventBase`）。"""
    match = re.search(r'internal sealed (?:partial )?class %s\s*:\s*RandomEventBase' % name, src)
    return match.group(0) if match else ''


def require(haystack, needle, why):
    if needle not in haystack:
        errors.append('%s（找不到 %r）' % (why, needle))


models = read(MODELS)
director = read(DIRECTOR)
catalog = read(CATALOG)
fun = read(FUN)
effects = read(EFFECTS)

# ---- 1. 基类默认占配额 ----
base_prop = body(models, 'internal virtual bool ConsumesRunBudget', 'RandomEventBase')
require(base_prop, 'return true;', '新事件默认必须占配额（基类 ConsumesRunBudget 要默认 true）')

# ---- 2. 调度器按它计数，退款分支不变 ----
start = body(director, 'private bool TryStartRandomEvent()', 'RandomEventDirector')
require(start, '_activeEventCounted = evt.ConsumesRunBudget;', '触发时要按事件自己的声明决定计不计配额')
require(start, 'if (_activeEventCounted) _eventsFiredThisRun++;', '只有占配额的事件才推进 _eventsFiredThisRun')
if re.search(r'(?<!if \(_activeEventCounted\) )_eventsFiredThisRun\+\+;', start):
    errors.append('TryStartRandomEvent 里还有不受 _activeEventCounted 约束的 _eventsFiredThisRun++')
require(start, 'if (_eventsFiredThisRun >= _maxEventsThisRun) return false;',
        '配额用完之后一个事件都不再触发（纯演出也不例外，低频档要真的安静）')

tick = body(director, 'private void TickEventActive(float dt)', 'RandomEventDirector')
require(tick, 'if (_activeEventCounted && _eventsFiredThisRun > 0) _eventsFiredThisRun--;',
        '异步全失败的退款分支必须仍按 _activeEventCounted 走（纯演出没计数就不能倒扣）')

# ---- 3. 覆写成 false 的只有登记在案的纯演出事件 ----
overrides = set()
for src, rel in ((catalog, CATALOG), (fun, FUN)):
    for match in re.finditer(r'internal sealed (?:partial )?class (RandomEvent\w+)\s*:\s*RandomEventBase', src):
        name = match.group(1)
        block = body(src, match.group(0), rel)
        if re.search(r'internal override bool ConsumesRunBudget\s*\{\s*get\s*\{\s*return false;', block):
            overrides.add(name)

if overrides != set(FLAVOR_CLASSES):
    errors.append('不占配额的事件与白名单对不上：实际 %r / 白名单 %r'
                  % (sorted(overrides), sorted(FLAVOR_CLASSES)))

for name in FLAVOR_CLASSES:
    block = body(fun, class_decl(fun, name), FUN)
    require(block, 'internal override bool ConsumesRunBudget { get { return false; } }',
            '%s 是纯演出事件，必须声明不占配额' % name)
    for token in PAYOFF_TOKENS:
        if token in block:
            errors.append('%s 里出现了 %r：给了玩法回报就不该算纯演出，请去掉白名单登记'
                          % (name, token))

# 纯演出的调用链也必须只播特效：0 点伤害的官方爆炸仍调用 DamageReceiver.Hurt。
harmless = body(effects, 'internal void CreateRandomEventHarmlessExplosion(', EFFECTS)
for token in ('CreateExplosion', 'Hurt', 'DamageInfo', 'OverlapSphere', 'OverlapSphereNonAlloc'):
    if re.search(r'\b' + token + r'\b', harmless):
        errors.append('纯演出桥包含战斗调用 ' + token)
compact = re.sub(r'\s+', '', harmless)
require(compact, 'UnityEngine.Object.Instantiate<GameObject>(prefab,center,Quaternion.identity);',
        '纯演出桥必须实际实例化官方特效')
require(compact, 'level.ExplosionManager.normalFxPfb', '普通演出复用官方 normalFxPfb')
require(compact, 'level.ExplosionManager.flashFxPfb', '闪光演出复用官方 flashFxPfb')

# ---- 4. 事件池仍然是登记在案的八个（防止新增事件悄悄绕过本守卫） ----
pool = body(catalog, '_all = new RandomEventBase[]', CATALOG)
registered = re.findall(r'new (RandomEvent\w+)\(\)', pool)
if len(registered) != 8 or len(set(registered)) != 8:
    errors.append('事件池不是八个互不相同的事件：%r' % registered)
for name in FLAVOR_CLASSES:
    if name not in registered:
        errors.append('白名单里的 %s 不在事件池里' % name)

if errors:
    for e in errors:
        print('  - ' + e)
    print('RandomEventFlavorBudgetGuard: FAIL')
    raise SystemExit(1)

print('RandomEventFlavorBudgetGuard: PASS（基类默认占配额；调度器按声明计数与退款；'
      '纯演出事件 %s 不占配额且无任何回报符号；事件池 8 个）' % '/'.join(FLAVOR_CLASSES))
