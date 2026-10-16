"""Mode E's direct smoke FX uses the actual native scene, without blocking personal refunds."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT=Path(__file__).resolve().parents[1]

def body(source, signature):
    assert source.count(signature)==1,signature
    opening=source.index('{',source.index(signature));depth=0
    for end in range(opening,len(source)):
        depth+=(source[end]=='{')-(source[end]=='}')
        if depth==0:return re.sub(r'\s+',' ',source[opening+1:end])
    raise AssertionError(signature)

def main():
    source=clean_source((ROOT/'ModeE/ModeERuntimeModule_CoopConsumables.cs').read_text(encoding='utf-8-sig'))
    publish=body(source,'private void PublishCoopUtilitySmoke(')
    assert '!BossRushCoop.HasBoundScene' in publish,'unbound host must not publish smoke'
    assert 'SceneToken = BossRushCoop.SceneToken' in publish,'smoke packet must carry actual token'
    receive=body(source,'private void OnCoopUtilitySmoke(')
    assert '!BossRushCoop.HasBoundScene' in receive,'unbound map cannot instantiate smoke'
    assert 'string.IsNullOrEmpty(effect.SceneToken) || effect.SceneToken != BossRushCoop.SceneToken' in receive,'old map smoke cannot use unchanged public generation'
    assert receive.index('effect.SceneToken != BossRushCoop.SceneToken')<receive.index('coopUtilitySmokeSeen.Add(effect.Id)')<receive.index('PlayCoopUtilitySmoke(effect.Position)'),'validate before replay tombstone and actual effect'
    refund=body(source,'private bool ApplyCoopUtilityReceipt(')
    assert 'HasBoundScene' not in refund and 'SceneToken' not in refund,'personal refund cannot require a bound world'
    assert 'request.AssetId != BossRushCoop.LocalAssetId' in refund and 'coopUtilityRequests.Remove(receipt.UtilityId)' in refund,'original asset and completion ownership remain'
    print('ModeECoopSmokeSceneGuard: PASS')

if __name__=='__main__':
    try:main()
    except AssertionError as error:
        print('ModeECoopSmokeSceneGuard: FAIL - '+str(error));raise SystemExit(1)
