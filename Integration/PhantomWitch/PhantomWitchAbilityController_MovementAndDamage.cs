// ============================================================================
// PhantomWitchAbilityController partial - extracted from PhantomWitchAbilityController.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    public partial class PhantomWitchAbilityController : MonoBehaviour
    {
        private bool CanContinueAttacking()
        {
            return bossCharacter != null &&
                   bossHealth != null &&
                   !bossHealth.IsDead &&
                   CurrentPhase != PhantomWitchPhase.Dead;
        }

        private void PauseAI()
        {
            try
            {
                ModBehaviour.DevLog("[PhantomWitch] [AI] PauseAI request | boss=" + DescribeBossState());
                if (ambientPresence != null)
                {
                    ambientPresence.Pause();
                }
                if (aiController != null)
                {
                    aiController.Pause();
                }
                ModBehaviour.DevLog("[PhantomWitch] [AI] PauseAI done | boss=" + DescribeBossState());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] PauseAI失败: " + e.Message);
            }
        }

        private void ResumeAI(CharacterMainControl target)
        {
            try
            {
                ModBehaviour.DevLog(
                    "[PhantomWitch] [AI] ResumeAI request | target=" + DescribeCharacter(target)
                    + " | boss=" + DescribeBossState());
                if (ambientPresence != null)
                {
                    ambientPresence.SetDetailLevel(PhantomWitchFxRuntime.CurrentDetailLevel);
                    ambientPresence.Resume();
                }
                if (aiController != null)
                {
                    aiController.Resume(target);
                }
                ModBehaviour.DevLog(
                    "[PhantomWitch] [AI] ResumeAI done | target=" + DescribeCharacter(target)
                    + " | boss=" + DescribeBossState());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] ResumeAI失败: " + e.Message);
            }
        }

        private IEnumerator TeleportTo(Vector3 targetPos)
        {
            if (bossCharacter == null || bossHealth == null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] skipped because bossCharacter/bossHealth is null");
                yield break;
            }

            // 若目标位置就在原地（ResolveTeleportPosition 找不到 NavMesh 时会回退），
            // 直接跳过，避免出现"原地隐身却像卡住"的观感。
            float teleportDistanceSq = (targetPos - bossCharacter.transform.position).sqrMagnitude;
            if (teleportDistanceSq < 0.16f)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] targetPos 与当前位置几乎一致，跳过传送主体");
                yield break;
            }

            ModBehaviour.DevLog(
                "[PhantomWitch] [Teleport] begin from " + bossCharacter.transform.position
                + " to " + targetPos
                + " | boss=" + DescribeBossState());
            PhantomWitchStealthMode preTeleportStealthMode = currentStealthMode;
            bool enteredTrueStealth = currentStealthMode != PhantomWitchStealthMode.TrueStealthTransition;
            if (enteredTrueStealth)
            {
                SetStealthMode(PhantomWitchStealthMode.TrueStealthTransition, "teleport");
            }
            TrackEffect(PhantomWitchAssetManager.CreateTeleportEffect(
                bossCharacter.transform.position,
                false));
            bossHealth.SetInvincible(true);
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] invincible=true");
            TouchAttackLoopProgress();

            yield return waitBlinkHide;

            if (bossCharacter == null || bossHealth == null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] aborted after delay because bossCharacter/bossHealth is null");
                yield break;
            }

            ApplyTeleportPosition(targetPos);
            TouchAttackLoopProgress();
            bossHealth.SetInvincible(false);
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] invincible=false");
            TrackEffect(PhantomWitchAssetManager.CreateTeleportEffect(targetPos, true));
            if (enteredTrueStealth && currentStealthMode == PhantomWitchStealthMode.TrueStealthTransition)
            {
                SetStealthMode(preTeleportStealthMode, "teleport_complete");
            }
            if (CurrentPhase == PhantomWitchPhase.Phase3)
            {
                phase3TeleportDistance += Mathf.Sqrt(teleportDistanceSq);
                phase3TeleportCount++;
            }
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] end | boss=" + DescribeBossState());
        }

        private void TouchAttackLoopProgress()
        {
            attackLoopLastTickTime = Time.time;
        }

        private void ApplyTeleportPosition(Vector3 targetPos)
        {
            if (bossCharacter == null)
            {
                return;
            }

            bool usedSetPosition = false;
            try
            {
                bossCharacter.SetPosition(targetPos);
                usedSetPosition = true;
            }
            catch (Exception setPositionEx)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] SetPosition 失败，改用 transform.position: " + setPositionEx.Message);
            }

            Vector3 currentPos = bossCharacter.transform.position;
            Vector3 delta = currentPos - targetPos;
            if (!usedSetPosition || delta.sqrMagnitude > 0.0001f)
            {
                bossCharacter.transform.position = targetPos;
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] hard snap -> " + bossCharacter.transform.position);
                return;
            }

            ModBehaviour.DevLog("[PhantomWitch] [Teleport] SetPosition done, currentPos=" + currentPos);
        }

        private void RestoreVisibleState()
        {
            SetStealthMode(PhantomWitchStealthMode.Visible);

            try
            {
                if (bossCharacter != null)
                {
                    bossCharacter.Show();
                }
            }
            catch
            {
            }

            try
            {
                if (bossHealth != null)
                {
                    bossHealth.SetInvincible(false);
                }
            }
            catch
            {
            }
        }


        private Vector3 ResolveTeleportPosition(
            CharacterMainControl target,
            float minDistance,
            float maxDistance)
        {
            Vector3 bossPos = bossCharacter != null
                ? bossCharacter.transform.position
                : spawnAnchorPosition;

            // 保留一个"足够远"的保底点：如果 NavMesh 采样失败，宁可直接跳到该位置
            // 也不要停在原地——原地传送会让 Boss 表现得像卡住。
            Vector3 finalFallback = bossPos;

            if (target != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = UnityEngine.Random.Range(100f, 260f);
                    float distance = UnityEngine.Random.Range(minDistance, maxDistance);
                    Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
                    Vector3 candidate = target.transform.position + rotation * target.transform.forward * distance;
                    candidate.y = target.transform.position.y;

                    Vector3 sampled = SampleNavMeshOrFallback(candidate, bossPos);
                    if ((sampled - bossPos).sqrMagnitude > 0.25f)
                    {
                        return sampled;
                    }
                    // 记下候选里最有意义的"远点"，NavMesh 全军覆没时可直接使用
                    if ((candidate - bossPos).sqrMagnitude > (finalFallback - bossPos).sqrMagnitude)
                    {
                        finalFallback = candidate;
                    }
                }

                Vector3 behindTarget = target.transform.position - target.transform.forward * Mathf.Max(minDistance, PhantomWitchConfig.BlinkFallbackDistance);
                behindTarget.y = target.transform.position.y;
                Vector3 sampledBehind = SampleNavMeshOrFallback(behindTarget, bossPos);
                if ((sampledBehind - bossPos).sqrMagnitude > 0.25f)
                {
                    return sampledBehind;
                }

                // NavMesh 整个范围都没命中 → 用目标位置 + 偏移硬跳，避免原地传送死局
                Vector3 toTarget = target.transform.position - bossPos;
                toTarget.y = 0f;
                float toTargetDistanceSqr = toTarget.sqrMagnitude;
                if (toTargetDistanceSqr > 0.01f)
                {
                    float toTargetDistance = Mathf.Sqrt(toTargetDistanceSqr);
                    float fallbackMoveDistance = Mathf.Min(toTargetDistance, maxDistance * 0.6f);
                    Vector3 approach = bossPos + toTarget * (fallbackMoveDistance / toTargetDistance);
                    approach.y = bossPos.y;
                    return approach;
                }

                return finalFallback;
            }

            Vector3 anchor = spawnAnchorPosition;
            if (anchor == Vector3.zero && bossCharacter != null)
            {
                anchor = bossCharacter.transform.position;
            }

            for (int i = 0; i < 6; i++)
            {
                Vector2 circle = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(minDistance, maxDistance);
                Vector3 candidate = anchor + new Vector3(circle.x, 0f, circle.y);
                Vector3 sampled = SampleNavMeshOrFallback(candidate, bossPos);
                if ((sampled - bossPos).sqrMagnitude > 0.25f)
                {
                    return sampled;
                }
            }

            return SampleNavMeshOrFallback(anchor, bossPos);
        }

        private Vector3 ResolveTrackedTeleportStrikePosition(CharacterMainControl target)
        {
            Vector3 fallback = bossCharacter != null
                ? bossCharacter.transform.position
                : spawnAnchorPosition;
            if (target == null)
            {
                return fallback;
            }

            Vector3 currentTargetPos = target.transform.position;
            Vector3 offsetDirection = fallback - currentTargetPos;
            offsetDirection.y = 0f;
            if (offsetDirection.sqrMagnitude < 0.0001f)
            {
                offsetDirection = -target.transform.forward;
                offsetDirection.y = 0f;
            }

            if (offsetDirection.sqrMagnitude < 0.0001f)
            {
                offsetDirection = Vector3.back;
            }

            Vector3 candidate = currentTargetPos + offsetDirection.normalized * PhantomWitchConfig.BlinkTrackedOffsetDistance;
            candidate.y = currentTargetPos.y;
            Vector3 sampledCandidate = SampleNavMeshOrFallback(candidate, currentTargetPos);
            if ((sampledCandidate - fallback).sqrMagnitude < 1.0f)
            {
                return ResolveTeleportPosition(
                    target,
                    Mathf.Max(PhantomWitchConfig.BlinkMinDistance, 1.6f),
                    Mathf.Max(PhantomWitchConfig.BlinkMaxDistance, 2.8f));
            }

            return sampledCandidate;
        }

        private Vector3 SampleNavMeshOrFallback(Vector3 candidate, Vector3 fallback)
        {
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(
                candidate,
                out navHit,
                PhantomWitchConfig.NavMeshSampleRadius,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                return navHit.position;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(
                fallback,
                out navHit,
                PhantomWitchConfig.NavMeshFallbackRadius,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                return navHit.position;
            }

            return fallback;
        }

        private void FaceTarget(CharacterMainControl target)
        {
            if (bossCharacter == null || target == null)
            {
                return;
            }

            Vector3 dir = target.transform.position - bossCharacter.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                return;
            }

            Vector3 desiredForward = dir.normalized;
            Vector3 currentForward = bossCharacter.transform.forward;
            currentForward.y = 0f;
            float currentForwardSqr = currentForward.sqrMagnitude;
            if (currentForwardSqr > 0.01f)
            {
                float faceTargetDotThreshold = Mathf.Cos(12f * Mathf.Deg2Rad);
                float currentForwardLength = Mathf.Sqrt(currentForwardSqr);
                float currentForwardDot = Vector3.Dot(currentForward, desiredForward);
                if (currentForwardDot > currentForwardLength * faceTargetDotThreshold)
                {
                    return;
                }
            }

            bossCharacter.transform.rotation = Quaternion.LookRotation(desiredForward, Vector3.up);
        }

        private int DealConeDamage(
            float radius,
            float halfAngle,
            float damage,
            bool applyCurse,
            float forwardOffset,
            CharacterMainControl target)
        {
            if (bossCharacter == null)
            {
                return 0;
            }

            Vector3 forward = ResolveAttackForward(target);
            Vector3 origin = bossCharacter.transform.position + forward * Mathf.Max(0f, forwardOffset);
            int hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                radius,
                damageHitBuffer,
                FenHuangHalberdRuntime.DamageReceiverLayerMask);

            Buff curseBuff = applyCurse ? PhantomWitchAssetManager.GetCurseBuff() : null;
            processedReceiverIds.Clear();
            float radiusSqr = radius * radius;
            bool requiresConeAngleCheck = halfAngle < 179f;
            float angleDotThreshold = Mathf.Cos(halfAngle * Mathf.Deg2Rad);

            int dealtCount = 0;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = damageHitBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(col);
                if (receiver == null || receiver.health == null || receiver.health.IsDead)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (processedReceiverIds.Contains(receiverId))
                {
                    continue;
                }
                processedReceiverIds.Add(receiverId);

                if (!IsEnemyReceiver(receiver))
                {
                    continue;
                }

                CharacterMainControl targetCharacter = receiver.health.TryGetCharacter();
                if (targetCharacter == bossCharacter || (targetCharacter != null && targetCharacter.Dashing))
                {
                    continue;
                }

                Vector3 toTarget = receiver.transform.position - origin;
                toTarget.y = 0f;
                float sqrDistance = toTarget.sqrMagnitude;
                if (sqrDistance > radiusSqr)
                {
                    continue;
                }

                if (requiresConeAngleCheck && sqrDistance > 0.0001f)
                {
                    float targetDistance = Mathf.Sqrt(sqrDistance);
                    float targetDot = Vector3.Dot(forward, toTarget);
                    if (targetDot < targetDistance * angleDotThreshold)
                    {
                        continue;
                    }
                }

                DamageInfo damageInfo = new DamageInfo(bossCharacter);
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damageValue = damage;
                damageInfo.damagePoint = receiver.transform.position;
                damageInfo.damageNormal = -forward;
                damageInfo.fromWeaponItemID = GetCurrentWeaponTypeId();
                damageInfo.crit = -1;
                damageInfo.AddElementFactor(ElementTypes.ghost, 1f);

                receiver.Hurt(damageInfo);

                if (applyCurse && curseBuff != null)
                {
                    try
                    {
                        if (targetCharacter != null)
                        {
                            targetCharacter.AddBuff(curseBuff, bossCharacter, GetCurrentWeaponTypeId());
                        }
                        else
                        {
                            receiver.AddBuff(curseBuff, bossCharacter);
                        }
                        GameObject vfxTarget = (targetCharacter != null) ? targetCharacter.gameObject : receiver.gameObject;
                        PhantomWitchCurseSweatVfx.TryAttach(vfxTarget);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 施加诅咒失败: " + e.Message);
                    }
                }

                dealtCount++;
            }

            if (dealtCount > 0)
            {
                currentPackageHadAttackLanded = true;
                TrackEffect(PhantomWitchAssetManager.CreateDamageHitEffect(origin));
            }

            return dealtCount;
        }

        private Vector3 ResolveAttackForward(CharacterMainControl target)
        {
            Vector3 forward = bossCharacter != null ? bossCharacter.transform.forward : Vector3.forward;
            forward.y = 0f;

            if (target != null && bossCharacter != null)
            {
                Vector3 dir = target.transform.position - bossCharacter.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    forward = dir.normalized;
                }
            }

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        private void ForceScytheAttackAnimation(CharacterMainControl target)
        {
            if (bossCharacter == null)
            {
                return;
            }

            try
            {
                if (target != null)
                {
                    Vector3 aimPoint = target.transform.position;
                    aimPoint.y = bossCharacter.transform.position.y;
                    bossCharacter.SetAimPoint(aimPoint);
                }

                FaceTarget(target);

                Slot meleeSlot = bossCharacter.MeleeWeaponSlot();
                if (meleeSlot != null && meleeSlot.Content != null)
                {
                    DuckovItemAgent currentHoldAgent = bossCharacter.CurrentHoldItemAgent;
                    if (currentHoldAgent == null || currentHoldAgent.Item != meleeSlot.Content)
                    {
                        bossCharacter.ChangeHoldItem(meleeSlot.Content);
                    }
                }

                if (bossCharacter.characterModel == null)
                {
                    return;
                }

                CharacterAnimationControl animControl =
                    bossCharacter.characterModel.GetComponentInChildren<CharacterAnimationControl>();
                if (animControl == null)
                {
                    return;
                }

                if (!attackAnimationHoldAgentFieldCached)
                {
                    attackAnimationHoldAgentField = typeof(CharacterAnimationControl).GetField(
                        "holdAgent",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    attackAnimationHoldAgentFieldCached = true;
                }

                DuckovItemAgent currentAgent = bossCharacter.CurrentHoldItemAgent;
                if (currentAgent != null && attackAnimationHoldAgentField != null)
                {
                    attackAnimationHoldAgentField.SetValue(animControl, currentAgent);
                }

                bossCharacter.characterModel.ForcePlayAttackAnimation();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] ForceScytheAttackAnimation失败: " + e.Message);
            }
        }

        private float ResolveBossSweepVisualScale()
        {
            if (bossCharacter == null)
            {
                return 1f;
            }

            Vector3 lossyScale = bossCharacter.transform.lossyScale;
            float maxAxisScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
            return Mathf.Max(1f, maxAxisScale);
        }

        private bool IsEnemyReceiver(DamageReceiver receiver)
        {
            if (receiver == null || bossCharacter == null)
            {
                return false;
            }

            return Team.IsEnemy(bossCharacter.Team, receiver.Team);
        }

        private int GetCurrentWeaponTypeId()
        {
            try
            {
                Slot meleeSlot = bossCharacter != null ? bossCharacter.MeleeWeaponSlot() : null;
                if (meleeSlot != null && meleeSlot.Content != null)
                {
                    return meleeSlot.Content.TypeID;
                }
            }
            catch
            {
            }

            return PhantomWitchConfig.PlaceholderScytheTypeId;
        }

        internal int GetCurrentWeaponTypeIdForRealmRuntime()
        {
            return GetCurrentWeaponTypeId();
        }

        private float GetFlatDistanceSqr(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return (a - b).sqrMagnitude;
        }

    }
}
