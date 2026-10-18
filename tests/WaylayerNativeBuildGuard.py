"""Keep native ticket assembly optional, exact and owned by the claim service."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def body(source, signature):
    assert source.count(signature) == 1, signature
    start=source.index('{',source.index(signature));depth=0
    for end in range(start,len(source)):
        depth+=(source[end]=='{')-(source[end]=='}')
        if depth==0:return re.sub(r'\s+',' ',source[start+1:end]).strip()
    raise AssertionError('unclosed '+signature)

def main():
    scope=clean_source((ROOT/'SkyIsland/CoopWaylayerNativeBuildScope.cs').read_text(encoding='utf-8-sig'))
    claims=clean_source((ROOT/'SkyIsland/CoopWaylayerClaims.cs').read_text(encoding='utf-8-sig'))
    assert '[HarmonyPatch' not in scope and 'GetType(' in scope, 'optional native bridge must not fail global PatchAll'
    setup=body(scope,'internal static void EnsureInstalled()')
    for token in ('owners == 0 || patch != null || disabled', 'Type.GetType("EscapeFromDuckovCoopMod.ItemTool, EscapeFromDuckovCoopMod", false)', 'if (tool == null) return;', 'candidate.GetParameters()[0].ParameterType.FullName == "EscapeFromDuckovCoopMod.ItemSnapshot"', 'target.ReturnType != typeof(Item)', 'catch (Exception error)', 'attempt.UnpatchAll(attempt.Id)', 'disabled = true', 'ModBehaviour.CriticalLog('):
        assert token in setup,'optional install contract: '+token
    build=body(scope,'private static IEnumerable<CodeInstruction> BuildCalls(')
    assert 'roots != 1 || additions != 1 || plugs != 1' in build,'native snapshot sites must be unique'
    corpse=body(scope,'private static IEnumerable<CodeInstruction> CorpseCalls(')
    assert 'boxes != 1 || additions != 1' in corpse,'native corpse sites must be unique'
    for sig in ('internal static bool AllowInventory(', 'internal static bool AllowSlot('):
        gate=body(scope,sig)
        for token in ('scope.Consumed','ReferenceEquals(scope.Item, item)','scope.Frame.Epoch != epoch','scope.Consumed = true;'):
            assert token in gate,sig+': '+token
    fresh=body(scope,'private static bool IsFreshWorldRoot(')
    assert 'item.ParentObject != null' in fresh and 'item.ActiveAgent != null' in fresh and '!IsPlayerTree(item)' in fresh,'only new detached native roots may assemble tickets'
    players=body(scope,'private static bool IsPlayerTree(')
    assert 'CharacterMainControl.Main' in players and 'BossRushCoop.Players' in players and 'current = current.ParentItem' in players,'main and remote player trees must be excluded'
    transfer=body(scope,'private static bool AddCorpseChild(')
    for token in ('if (!CoopWaylayerClaims.IsWorldTicket(item)) return ItemUtilities.AddAndMerge(inventory, item, position);','current.Children.Remove(item)','ReferenceEquals(current.Box.Inventory, inventory)','inventory.AttachedToItem != null','CoopWaylayerClaims.Admit(item, inventory)','try { return inventory.AddItem(item); }','finally { CoopWaylayerClaims.TrackCorpse(item, inventory); }'):
        assert token in transfer,'corpse exact transfer: '+token
    before=body(scope,'private static void BeforeCorpse(')
    assert before.index('!ticketRoots.TryGetValue(item, out tracked)')<before.index('new CorpseFrame'),'ordinary corpse must not allocate or enumerate before O(1) ticket-root lookup'
    assert before.count('CoopWaylayerClaims.IsWorldTicket(')==2,'corpse freezes only direct world tickets'
    assert 'nativeBuildScope = CoopWaylayerNativeBuildScope.Acquire();' in claims and 'nativeBuildScope.Dispose();' in claims and 'CoopWaylayerNativeBuildScope.EnsureInstalled();' in claims,'claim owner must own bridge lifetime'
    assert 'if (chief != null) CoopWaylayerNativeBuildScope.RegisterTicketRoot(chief.CharacterItem);' in claims,'host Chief custody must register native corpse root'
    for sig,call in (('internal static bool AllowInventory(','CoopWaylayerNativeBuildScope.AllowInventory(item, inventory, mutation)'),('internal static bool AllowSlot(','CoopWaylayerNativeBuildScope.AllowSlot(item, slot)')):
        gate=body(claims,sig)
        assert gate.index('if (!Locked(item)) return true;')<gate.index(call),'ordinary item O(1) path must precede optional seam'
    print('WaylayerNativeBuildGuard: PASS')

if __name__=='__main__':
    try:main()
    except AssertionError as error:print('WaylayerNativeBuildGuard: FAIL - '+str(error));raise SystemExit(1)
