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
        private void SetStealthMode(PhantomWitchStealthMode mode)
        {
            SetStealthMode(mode, "package");
        }

        private void SetStealthMode(PhantomWitchStealthMode mode, string reason)
        {
            if (currentStealthMode == mode)
            {
                return;
            }

            PhantomWitchStealthMode previousMode = currentStealthMode;
            if (previousMode != PhantomWitchStealthMode.Visible)
            {
                EmitTelemetry("stealth_exit", "mode=" + previousMode + ",nextMode=" + mode + ",reason=" + reason);
            }

            RestoreStealthVisuals();
            currentStealthMode = mode;
            stealthModeEnteredAt = Time.time;

            if (bossCharacter == null || bossCharacter.gameObject == null)
            {
                EmitTelemetry("stealth_enter", "mode=" + mode + ",prevMode=" + previousMode + ",reason=no_boss");
                return;
            }

            CacheStealthRenderers();
            switch (mode)
            {
                case PhantomWitchStealthMode.TrueStealthTransition:
                    for (int i = 0; i < stealthCachedRenderers.Count; i++)
                    {
                        if (stealthCachedRenderers[i] != null)
                        {
                            stealthCachedRenderers[i].enabled = false;
                        }
                    }
                    break;
                case PhantomWitchStealthMode.SemiStealthWindup:
                    if (!alphaSupported)
                    {
                        currentStealthMode = PhantomWitchStealthMode.Visible;
                        mode = PhantomWitchStealthMode.Visible;
                        EmitTelemetry("stealth_downgrade", "mode=SemiStealthWindup,reason=no_alpha_support");
                        break;
                    }

                    for (int i = 0; i < stealthCachedRenderers.Count; i++)
                    {
                        MaterialPropertyBlock block = i < stealthCachedBlocks.Count ? stealthCachedBlocks[i] : null;
                        SetRendererAlpha(stealthCachedRenderers[i], block, 0.33f);
                    }

                    if (bossCharacter != null && activeSemiStealthEffect == null)
                    {
                        activeSemiStealthEffect = PhantomWitchAssetManager.CreateSemiStealthWindupEffect(bossCharacter.transform);
                        if (activeSemiStealthEffect != null)
                        {
                            TrackEffect(activeSemiStealthEffect);
                        }
                    }
                    break;
                case PhantomWitchStealthMode.Visible:
                    break;
            }

            EmitTelemetry("stealth_enter", "mode=" + mode + ",prevMode=" + previousMode + ",reason=" + reason);
        }

        private void CacheStealthRenderers()
        {
            stealthCachedRenderers.Clear();
            stealthCachedAlphas.Clear();
            stealthCachedBlocks.Clear();

            if (bossCharacter == null || bossCharacter.gameObject == null)
            {
                return;
            }

            Renderer[] renderers = bossCharacter.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                stealthCachedRenderers.Add(renderer);
                stealthCachedBlocks.Add(new MaterialPropertyBlock());
                stealthCachedAlphas.Add(GetRendererAlpha(renderer));
            }

            if (!alphaSupportChecked)
            {
                alphaSupported = PhantomWitchPerformancePolicy.SupportsAlphaModulation(stealthCachedRenderers);
                alphaSupportChecked = true;
            }
        }

        private void RestoreStealthVisuals()
        {
            if (activeSemiStealthEffect != null)
            {
                UnityEngine.Object.Destroy(activeSemiStealthEffect);
                activeSemiStealthEffect = null;
            }

            for (int i = 0; i < stealthCachedRenderers.Count; i++)
            {
                Renderer renderer = stealthCachedRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                float alpha = i < stealthCachedAlphas.Count ? stealthCachedAlphas[i] : 1f;
                MaterialPropertyBlock block = i < stealthCachedBlocks.Count ? stealthCachedBlocks[i] : null;
                SetRendererAlpha(renderer, block, alpha);
            }
        }

        private float GetRendererAlpha(Renderer renderer)
        {
            if (renderer == null)
            {
                return 1f;
            }

            return PhantomWitchFxRenderUtil.GetRendererColor(renderer, null).a;
        }

        private void SetRendererAlpha(Renderer renderer, MaterialPropertyBlock block, float alpha)
        {
            if (renderer == null)
            {
                return;
            }

            Color color = PhantomWitchFxRenderUtil.GetRendererColor(renderer, block);
            color.a = alpha;
            PhantomWitchFxRenderUtil.SetRendererColor(renderer, block, color);
        }

        private IEnumerator RunBodySinkWindup(float duration, float depth)
        {
            if (bossCharacter == null)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }

            Transform bossTf = bossCharacter.transform;
            Vector3 basePosition = bossTf.position;
            float safeDuration = Mathf.Max(duration, 0.01f);
            float safeDepth = Mathf.Max(0f, depth);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                if (bossCharacter == null)
                {
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float sink = Mathf.Sin(t * Mathf.PI) * safeDepth;
                Vector3 nextPosition = basePosition;
                nextPosition.y = basePosition.y - sink;
                bossTf.position = nextPosition;
                yield return null;
            }

            if (bossCharacter != null)
            {
                bossTf.position = basePosition;
            }
        }

        private IEnumerator ExecuteScytheSweep()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("ScytheSweep", "before PauseAI", target);

            PauseAI();

            if (target != null && GetFlatDistance(bossCharacter.transform.position, target.transform.position) >
                PhantomWitchConfig.ScytheSweepRadius + 0.6f)
            {
                Vector3 targetPos = ResolveTeleportPosition(target, 1.4f, 2.8f);
                ModBehaviour.DevLog("[PhantomWitch] [ScytheSweep] target out of range, teleporting to " + targetPos);
                yield return TeleportTo(targetPos);
            }

            FaceTarget(target);
            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position + bossCharacter.transform.forward * 0.4f,
                0.85f,
                PhantomWitchConfig.ScytheSweepWindup,
                false));
            yield return waitScytheSweepWindup;

            if (!CanContinueAttacking())
            {
                LogSkillState("ScytheSweep", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            yield return ExecuteImmediateScytheSweep(target);

            yield return waitScytheSweepRecovery;
            LogSkillState("ScytheSweep", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteImmediateScytheSweep(CharacterMainControl target)
        {
            if (!CanContinueAttacking() || bossCharacter == null)
            {
                yield break;
            }

            ForceScytheAttackAnimation(target);
            Vector3 sweepForward = ResolveAttackForward(target);
            float sweepVisualScale = ResolveBossSweepVisualScale();
            Vector3 sweepOrigin = bossCharacter.transform.position +
                sweepForward * (PhantomWitchConfig.ScytheSweepForwardOffset * sweepVisualScale);
            TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(
                sweepOrigin,
                sweepForward,
                PhantomWitchConfig.ScytheSweepRadius * sweepVisualScale,
                PhantomWitchConfig.ScytheSweepHalfAngle));

            // Keep the gameplay hitbox unchanged; this request only enlarges the visuals.
            DealConeDamage(
                PhantomWitchConfig.ScytheSweepRadius,
                PhantomWitchConfig.ScytheSweepHalfAngle,
                PhantomWitchConfig.ScytheSweepDamage,
                false,
                PhantomWitchConfig.ScytheSweepForwardOffset,
                target);
            yield break;
        }

        private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)
        {
            if (target == null)
            {
                yield break;
            }

            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);

            GameObject markerEffect = null;
            float telegraphStartedAt = Time.time;
            Vector3 lockedTeleportPos = ResolveTrackedTeleportStrikePosition(target);
            float markerDuration = Mathf.Max(
                PhantomWitchConfig.BlinkTrackedMarkerFxDuration,
                PhantomWitchConfig.BlinkTrackedTelegraphDuration + 0.2f);

            while (Time.time - telegraphStartedAt < PhantomWitchConfig.BlinkTrackedTelegraphDuration)
            {
                if (!CanContinueAttacking() || target == null || target.Health == null || target.Health.IsDead)
                {
                    SetStealthMode(PhantomWitchStealthMode.Visible);
                    yield break;
                }

                TouchAttackLoopProgress();
                Vector3 currentTargetPosition = target.transform.position;
                Vector3 trackedTeleportPos = ResolveTrackedTeleportStrikePosition(target);
                lockedTeleportPos = trackedTeleportPos;
                if (markerEffect == null)
                {
                    markerEffect = PhantomWitchAssetManager.CreateTrackedTeleportMarkerEffect(trackedTeleportPos, markerDuration);
                    if (markerEffect != null)
                    {
                        TrackEffect(markerEffect);
                    }
                }
                else
                {
                    markerEffect.transform.position = trackedTeleportPos;
                }

                if (currentTargetPosition.y != trackedTeleportPos.y && markerEffect != null)
                {
                    markerEffect.transform.position = new Vector3(
                        markerEffect.transform.position.x,
                        currentTargetPosition.y,
                        markerEffect.transform.position.z);
                }

                yield return null;
            }

            if (!CanContinueAttacking() || target == null || target.Health == null || target.Health.IsDead)
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                yield break;
            }

            PauseAI();
            TouchAttackLoopProgress();

            // Teleport exactly to the last tracked marker position instead of recalculating again.
            if (markerEffect != null)
            {
                markerEffect.transform.position = lockedTeleportPos;
            }

            TrackEffect(PhantomWitchAssetManager.CreateTrackedTeleportFlashEffect(lockedTeleportPos));
            yield return TeleportTo(lockedTeleportPos);
            SetStealthMode(PhantomWitchStealthMode.Visible);
            yield return ExecuteImmediateScytheSweep(target);
            ResumeAI(target);
        }

    }
}
