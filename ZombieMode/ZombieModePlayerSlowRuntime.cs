using UnityEngine;

namespace BossRush
{
    public sealed class ZombieModePlayerSlowRuntime : MonoBehaviour
    {
        private int runId;
        private float slowEndTime;
        private float currentSlowPercent;
        private bool slowActive;
        private ModBehaviour owner;
        private readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> slowModifierRecords = new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        public void ApplySlow(int newRunId, float percent, float duration)
        {
            float now = GetRuntimeNow();
            if (slowActive && now >= slowEndTime)
            {
                ClearSlowState();
            }

            runId = newRunId;
            owner = ModBehaviour.Instance;
            float endCandidate = now + duration;
            if (percent > currentSlowPercent || endCandidate > slowEndTime)
            {
                float newPercent = Mathf.Max(currentSlowPercent, percent);
                slowEndTime = Mathf.Max(slowEndTime, endCandidate);
                slowActive = true;
                if (!Mathf.Approximately(newPercent, currentSlowPercent))
                {
                    currentSlowPercent = newPercent;
                    ReapplySlowModifiers();
                }
                else
                {
                    currentSlowPercent = newPercent;
                    if (slowModifierRecords.Count <= 0)
                    {
                        ReapplySlowModifiers();
                    }
                }
            }
        }

        public float GetCurrentSlowPercent()
        {
            if (!slowActive || GetRuntimeNow() >= slowEndTime)
            {
                return 0f;
            }
            return currentSlowPercent;
        }

        private void Update()
        {
            if (!slowActive) return;
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                ClearSlowState();
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (inst.GetZombieModeRuntimeNow() >= slowEndTime)
            {
                ClearSlowState();
            }
        }

        private void OnDisable()
        {
            RemoveSlowModifiers();
        }

        private void OnDestroy()
        {
            RemoveSlowModifiers();
        }

        private void ClearSlowState()
        {
            slowActive = false;
            currentSlowPercent = 0f;
            slowEndTime = 0f;
            RemoveSlowModifiers();
        }

        private void ReapplySlowModifiers()
        {
            RemoveSlowModifiers();
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null)
            {
                return;
            }

            // 收口到 ZombieModeStatNames（审查 §2.3）。
            AddSlowModifier(character, ZombieModeStatNames.MoveSpeed);
            AddSlowModifier(character, ZombieModeStatNames.WalkSpeed);
            AddSlowModifier(character, ZombieModeStatNames.RunSpeed);
        }

        private void AddSlowModifier(CharacterMainControl character, string statName)
        {
            if (character == null || currentSlowPercent <= 0f)
            {
                return;
            }

            // 用 PercentageAdd 而非 Add（审查 §3.2）：传 -percent 等价于减速 percent。
            // 之前 Add(stat.BaseValue * -percent) 在玩家叠了 +50% MoveSpeed 装备时，
            // 50% 减速实际只削掉基础速度的 50%，叠加值不动 → 减速被装备稀释。
            RuntimeStatModifierTracker.TryAdd(
                character,
                statName,
                -currentSlowPercent,
                this,
                slowModifierRecords,
                "Player Slow " + statName);
        }

        private void RemoveSlowModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(slowModifierRecords, "Player Slow");
        }

        private float GetRuntimeNow()
        {
            ModBehaviour inst = GetRuntimeOwner();
            return inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }
    }
}
