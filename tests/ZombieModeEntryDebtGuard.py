"""Guard: 丧尸模式入场回滚不再吞掉玩家的入场费与邀请函（CR-2026-09-11-019）。

## 为什么要有这份

入场先扣邀请函、再扣现金，任一后续步骤失败就要把两样退回去。但退款发生在**切图途中**：
`EconomyManager` 是场景里的 MonoBehaviour，卸场景那一刻 `Instance` 已被销毁，
官方 `Add` 直接 `return false`；`ItemAssetsCollection.InstantiateSync` 在资源未就绪时返回 null。
修复前两处都把返回值丢掉，还在 `finally` 里无条件清事务状态，于是玩家花掉的入场费与邀请函
**永久蒸发**，而且没有任何地方记得欠了他。

修复后的结构由本守卫钉住：

1. **账本是模块自有类型**（AGENTS 4.15）：退款与欠账的算法在 `ZombieModeEntryDebt`，
   宿主 partial 里只剩「问账本要结果 → 决定清不清事务状态」几行；
2. **不吞**：宿主两处退款都必须先看账本返回值，返回 false 就 `return`（保留事务状态待重试），
   绝不能再出现无条件清状态的 `finally`；
3. **落存档**：两个原始类型 key（SCHEMA+，老档读出 0），写入一律回读核对
   （官方 `SavesSystem.Save` 在没有当前存档文件时只打日志就返回）；
4. **先送达再销账**：`TrySettleCash` 必须先 `Add` 成功才写 0；
5. **接得回来**：订阅官方 `OnEconomyManagerLoaded`（命名方法、幂等、成对退订），
   另在每次入场扣款前顺手结一次账。

它证明的是接线与顺序；真实的切图时序、ES3 落盘与官方送达仍要实机 smoke。
行为本身由执行回归 `tests/fixtures/ZombieModeEntryDebt` 逐条断言。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

LEDGER = 'ZombieMode/ZombieModeEntryDebt.cs'
ENTRY = 'ZombieMode/ZombieModeEntry.cs'
MODULE = 'ZombieMode/ZombieModeRuntimeModule.cs'
MANIFEST = 'compile_official.bat'
RUNNER = 'tools/run_runtime_regressions.py'

errors = []


def read(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append('读不到 ' + rel)
        return ''
    return clean_source(path.read_text(encoding='utf-8-sig'))


def body(src, signature, label):
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


def require(haystack, needle, why):
    if needle not in haystack:
        errors.append('%s（找不到 %r）' % (why, needle))


def forbid(haystack, needle, why):
    if needle in haystack:
        errors.append('%s（不该出现 %r）' % (why, needle))


ledger = read(LEDGER)
entry = read(ENTRY)
module = read(MODULE)
manifest = (ROOT / MANIFEST).read_text(encoding='utf-8', errors='ignore')
runner = (ROOT / RUNNER).read_text(encoding='utf-8')

# ---- 1. 存档 key 冻结且互不相同 ----
keys = dict(re.findall(r'internal const string (\w+Key) = "([^"]+)";', ledger))
if keys.get('CashKey') != 'BossRush_ZombieMode_RefundDebt_Cash':
    errors.append('现金欠账的存档 key 变了（发布后冻结）：%r' % keys.get('CashKey'))
if keys.get('InvitationKey') != 'BossRush_ZombieMode_RefundDebt_Invitations':
    errors.append('邀请函欠账的存档 key 变了（发布后冻结）：%r' % keys.get('InvitationKey'))

# ---- 2. 写入必须回读核对 ----
for name, loader in (('WriteCash', 'SavesSystem.Load<long>(CashKey) == amount'),
                     ('WriteInvitations', 'SavesSystem.Load<int>(InvitationKey) == count')):
    block = body(ledger, 'private static bool %s(' % name, LEDGER)
    require(block, 'return ' + loader + ';',
            '%s 写入后必须回读核对（没有当前存档文件时官方 Save 只打日志就返回）' % name)

# ---- 3. 读取只退化不抛；负数按 0 ----
for name, zero in (('ReadCash', 'return value > 0L ? value : 0L;'), ('ReadInvitations', 'return value > 0 ? value : 0;')):
    block = body(ledger, 'internal static %s %s()' % ('long' if name == 'ReadCash' else 'int', name), LEDGER)
    require(block, zero, '%s 的负数/缺省一律按 0' % name)
    require(block, 'catch (Exception e)', '%s 必须 no-throw' % name)

# ---- 4. 退款：付不出去就落账本，落不下就如实返回 false ----
refund_cash = body(ledger, 'internal static bool RefundCash(long amount)', LEDGER)
require(refund_cash, 'paid = EconomyManager.Add(amount);', '退款要看官方 Add 的返回值')
require(refund_cash, 'return OweCash(amount);', '付不出去要落进存档账本，并把记账结果如实返回')
refund_inv = body(ledger, 'internal static bool RefundInvitation()', LEDGER)
require(refund_inv, 'if (refund == null) return OweInvitation(1);',
        '邀请函造不出来（资源未就绪）要落进存档账本')

# ---- 5. 结账：先到账再销账 ----
settle_cash = body(ledger, 'internal static bool TrySettleCash()', LEDGER)
pay_at = settle_cash.find('EconomyManager.Add(owed)')
clear_at = settle_cash.find('WriteCash(0L)')
if pay_at < 0 or clear_at < 0 or pay_at > clear_at:
    errors.append('结清现金必须「先 Add 成功、再写 0 销账」，顺序不能倒')
require(settle_cash, 'ModBehaviour.DevLog(LogPrefix + "经济实例暂不可用，现金欠账继续保留: " + owed);',
        '付不出去时账目必须原样保留并留下日志')
settle_inv = body(ledger, 'internal static bool TrySettleInvitations()', LEDGER)
require(settle_inv, 'owed--;', '邀请函逐张推进：中途失败时剩下的张数还留在账上')
require(settle_inv, 'if (refund == null)', '造不出来就停在这一张，不继续吃掉剩余账目')

# ---- 6. 订阅：命名方法、幂等、成对退订 ----
attach = body(ledger, 'internal static void Attach()', LEDGER)
require(attach, 'if (subscribed) return;', '订阅必须幂等（AGENTS 4.6）')
require(attach, 'EconomyManager.OnEconomyManagerLoaded += OnEconomyLoaded;', '订阅官方「经济加载完成」')
detach = body(ledger, 'internal static void Detach()', LEDGER)
require(detach, 'EconomyManager.OnEconomyManagerLoaded -= OnEconomyLoaded;', '必须成对退订')
require(detach, 'subscribed = false;', '退订后复位标志')
if re.search(r'OnEconomyManagerLoaded\s*[+-]=\s*(?:delegate|\()', ledger):
    errors.append('订阅不许用 lambda / 匿名委托：退订退不掉（AGENTS 4.6）')

# ---- 7. 宿主只做转发，且不再无条件清事务状态 ----
host_cash = body(entry, 'private void RefundZombieModeCashIfNeeded()', ENTRY)
require(host_cash, 'if (!ZombieModeEntryDebt.RefundCash(zombieModeEntryTransaction.CashWithheldAmount)) return;',
        '宿主必须按账本返回值决定清不清事务状态')
forbid(host_cash, 'finally', '退款路径不许再有无条件清事务状态的 finally')
forbid(host_cash, 'EconomyManager', '退款算法属于账本，宿主不该直接碰经济（AGENTS 4.15）')
for token in ('zombieModeEntryTransaction.CashTemporarilyHeld = false;',
              'zombieModeEntryTransaction.CashWithheldAmount = 0L;',
              'zombieModeRunState.ConfirmedCashInvested = 0L;'):
    require(host_cash, token, '了结之后仍要清掉这一项事务状态：' + token)

host_inv = body(entry, 'private void RefundZombieModeInvitationIfNeeded()', ENTRY)
require(host_inv, 'if (!ZombieModeEntryDebt.RefundInvitation()) return;',
        '邀请函同样按账本返回值决定清不清事务状态')
forbid(host_inv, 'finally', '邀请函返还路径不许再有无条件清事务状态的 finally')
forbid(host_inv, 'ItemAssetsCollection', '实例化属于账本，宿主不该直接造物品（AGENTS 4.15）')
require(host_inv, 'zombieModeEntryTransaction.InvitationTemporarilyHeld = false;', '了结之后要清邀请函事务状态')

# ---- 8. 扣款前顺手结一次旧账 ----
commit = body(entry, 'private bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)', ENTRY)
settle_at = commit.find('ZombieModeEntryDebt.TrySettleAll();')
pay_at = commit.find('EconomyManager.Pay(')
if settle_at < 0:
    errors.append('入场扣款前要先把上一次欠下的现金/邀请函结清')
elif 0 <= pay_at < settle_at:
    errors.append('结清旧账必须排在本次扣款之前')

# ---- 9. 模块生命周期 ----
require(body(module, 'public override void OnAwake(ModBehaviour owner)', MODULE),
        'ZombieModeEntryDebt.Attach();', '运行时模块启动时接上账本')
require(body(module, 'public override void OnDestroy()', MODULE),
        'ZombieModeEntryDebt.ResetStaticCaches();', '运行时模块销毁时退订账本')

# ---- 10. 登记 ----
if 'ZombieMode\\ZombieModeEntryDebt.cs' not in manifest:
    errors.append('账本没登记进 compile_official.bat（源码在但不会被编译）')
if '"ZombieModeEntryDebt"' not in runner:
    errors.append('执行回归没登记进 tools/run_runtime_regressions.py')

if errors:
    for e in errors:
        print('  - ' + e)
    print('ZombieModeEntryDebtGuard: FAIL')
    raise SystemExit(1)

print('ZombieModeEntryDebtGuard: PASS（两个存档 key 冻结、写入回读核对、读取只退化；'
      '付不出去落账本、落不下如实返回 false；结账先到账再销账、邀请函逐张推进；'
      '订阅命名方法且幂等成对；宿主只转发且不再无条件清状态；扣款前先结旧账；编译清单与回归清单已登记）')
