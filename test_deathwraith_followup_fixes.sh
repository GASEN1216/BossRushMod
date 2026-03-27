#!/usr/bin/env bash
set -euo pipefail

file="Integration/DeathWraith/DeathWraithSystem.cs"
content="$(cat "$file")"
errors=()

grep -q "GetWraithMoveabilityTarget_DeathWraith" <<<"$content" || errors+=("moveability helper missing")
perl -0ne 'exit((/case WraithTier\.Strong:[\s\S]{0,120}?return 1f;/s) ? 0 : 1)' "$file" || errors+=("strong moveability target is not 1.0")
perl -0ne 'exit((/case WraithTier\.Balanced:[\s\S]{0,120}?return 0\.9f;/s) ? 0 : 1)' "$file" || errors+=("balanced moveability target is not 0.9")
perl -0ne 'exit((/default:[\s\S]{0,120}?return 0\.8f;/s) ? 0 : 1)' "$file" || errors+=("weak moveability target is not 0.8")
perl -0ne 'exit((/float speedMult = tier == WraithTier\.Strong \? 1\.9f :[\s\S]{0,80}\(tier == WraithTier\.Balanced \? 1\.5f : 1\.2f\);/s) ? 0 : 1)' "$file" || errors+=("weak speed multiplier is not 1.2")
if grep -Eq 'SetTarget\(main\.mainDamageReceiver\.transform\)|SetNoticedToTarget\(main\.mainDamageReceiver\)|已强制锁定玩家为初始仇恨目标' "$file"; then
    errors+=("forced initial aggro still present")
fi
grep -q 'forceTracePlayerDistance = 0f;' "$file" || errors+=("natural aggro reset missing")
grep -q 'RequestHealthBar()' "$file" || errors+=("health bar request missing")

if [ "${#errors[@]}" -gt 0 ]; then
    printf 'FAIL: %s\n' "${errors[@]}"
    exit 1
fi

echo "PASS: death wraith follow-up fixes checks"
