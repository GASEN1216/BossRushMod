// ============================================================================
// DragonKingPooledEffect.cs - 龙王特效对象池里的单个实例（租出 / 软回收 / 回池）
// ============================================================================
// 原在 DragonKingAssetManager.cs；2026-09-23 VB-15 加了软回收后那份文件超过单文件行数预算（LargeFileBudgetGuard），
// 整类原样挪到这里，行为不变。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public class DragonKingPooledEffect : MonoBehaviour
    {
        private string poolKey = null;
        private Vector3 initialLocalScale = Vector3.one;
        private ParticleSystem[] particleSystems = new ParticleSystem[0];
        private Light[] lights = new Light[0];
        private Rigidbody[] rigidbodies = new Rigidbody[0];
        private TrailRenderer[] trailRenderers = new TrailRenderer[0];
        private Action<GameObject> ownerReleaseTracker = null;
        // 软回收（VB-15）：开始时关掉的碰撞体 / 伤害组件 / 实体网格，回池前原样打开；灯的起始强度用于淡出后复原。
        private Collider[] colliders = new Collider[0];
        private Renderer[] solidRenderers = new Renderer[0];
        private Behaviour[] gameplayBehaviours = new Behaviour[0];
        private bool[] colliderWasEnabled = new bool[0];
        private bool[] rendererWasEnabled = new bool[0];
        private bool[] behaviourWasEnabled = new bool[0];
        private float[] lightBaseIntensity = new float[0];
        private Coroutine softReleaseRoutine;
        private bool softReleasing;

        public string PoolKey => poolKey;
        public bool IsInPool { get; private set; }

        public void SetOwnerReleaseTracker(Action<GameObject> tracker)
        {
            ownerReleaseTracker = tracker;
        }

        public void Initialize(string key)
        {
            poolKey = key;
            initialLocalScale = transform.localScale;
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            lights = GetComponentsInChildren<Light>(true);
            rigidbodies = GetComponentsInChildren<Rigidbody>(true);
            trailRenderers = GetComponentsInChildren<TrailRenderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            CacheSoftReleaseTargets();
            IsInPool = false;
        }

        /// <summary>
        /// 软回收要立即关掉的东西：会造成伤害的组件（岩浆区、太阳舞光束触发器）与实体网格（弹体、长矛刀身——
        /// 停在命中点 0.5 s 会像冻住了）。粒子与拖尾留着自然淡完。
        /// </summary>
        private void CacheSoftReleaseTargets()
        {
            List<Renderer> solids = new List<Renderer>();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
                solids.Add(r);
            }
            solidRenderers = solids.ToArray();

            List<Behaviour> gameplay = new List<Behaviour>();
            gameplay.AddRange(GetComponentsInChildren<DragonKingLavaZone>(true));
            gameplay.AddRange(GetComponentsInChildren<SunBeamDamageTrigger>(true));
            gameplayBehaviours = gameplay.ToArray();

            colliderWasEnabled = new bool[colliders.Length];
            rendererWasEnabled = new bool[solidRenderers.Length];
            behaviourWasEnabled = new bool[gameplayBehaviours.Length];
            lightBaseIntensity = new float[lights.Length];
        }

        /// <summary>
        /// 开始软回收：停发射、拖尾停延伸、碰撞体与伤害组件立即关（判定到此为止）、实体网格藏起来；
        /// 灯 0.2 s 内 SmoothStep 淡到 0；<paramref name="seconds"/> 后硬回收进池。物体已失活时返回 false，调用方走硬回收。
        /// </summary>
        public bool BeginSoftRelease(float seconds)
        {
            if (IsInPool) return false;
            if (softReleasing) return true;
            if (!gameObject.activeInHierarchy) return false;

            CancelScheduledRelease();
            NotifyOwnerReleased();
            softReleasing = true;

            for (int i = 0; i < colliders.Length; i++)
            {
                colliderWasEnabled[i] = colliders[i] != null && colliders[i].enabled;
                if (colliders[i] != null) colliders[i].enabled = false;
            }
            for (int i = 0; i < gameplayBehaviours.Length; i++)
            {
                behaviourWasEnabled[i] = gameplayBehaviours[i] != null && gameplayBehaviours[i].enabled;
                if (gameplayBehaviours[i] != null) gameplayBehaviours[i].enabled = false;
            }
            for (int i = 0; i < solidRenderers.Length; i++)
            {
                rendererWasEnabled[i] = solidRenderers[i] != null && solidRenderers[i].enabled;
                if (solidRenderers[i] != null) solidRenderers[i].enabled = false;
            }
            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] != null)
                {
                    particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            for (int i = 0; i < trailRenderers.Length; i++)
            {
                if (trailRenderers[i] != null)
                {
                    trailRenderers[i].emitting = false;
                }
            }
            for (int i = 0; i < lights.Length; i++)
            {
                lightBaseIntensity[i] = lights[i] != null ? lights[i].intensity : 0f;
            }

            softReleaseRoutine = StartCoroutine(SoftReleaseRoutine(Mathf.Max(0.05f, seconds)));
            return true;
        }

        private System.Collections.IEnumerator SoftReleaseRoutine(float seconds)
        {
            int generation = DragonKingAssetManager.EffectPoolGeneration;
            float started = Time.time;
            const float lightFade = 0.2f;
            while (Time.time - started < seconds)
            {
                float k = 1f - BossRushUI.SmoothStep((Time.time - started) / lightFade);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null)
                    {
                        lights[i].intensity = lightBaseIntensity[i] * k;
                    }
                }
                yield return null;
            }
            softReleaseRoutine = null;
            if (generation != DragonKingAssetManager.EffectPoolGeneration)
            {
                // 软回收期间整池已被销毁（卸包 / 强制清理）：不把旧实例塞进新池。
                softReleasing = false;
                Destroy(gameObject);
                yield break;
            }
            DragonKingAssetManager.ReleaseEffectImmediate(gameObject);
        }

        /// <summary>回池前把软回收关掉的东西原样打开，灯强度复原；下一次租出去是完整的特效。</summary>
        private void RestoreAfterSoftRelease()
        {
            if (softReleaseRoutine != null)
            {
                StopCoroutine(softReleaseRoutine);
                softReleaseRoutine = null;
            }
            if (!softReleasing) return;
            softReleasing = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = colliderWasEnabled[i];
            }
            for (int i = 0; i < gameplayBehaviours.Length; i++)
            {
                if (gameplayBehaviours[i] != null) gameplayBehaviours[i].enabled = behaviourWasEnabled[i];
            }
            for (int i = 0; i < solidRenderers.Length; i++)
            {
                if (solidRenderers[i] != null) solidRenderers[i].enabled = rendererWasEnabled[i];
            }
            for (int i = 0; i < trailRenderers.Length; i++)
            {
                if (trailRenderers[i] != null) trailRenderers[i].emitting = true;
            }
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].intensity = lightBaseIntensity[i];
            }
        }

        public void OnAcquire(Vector3 position, Quaternion rotation)
        {
            CancelScheduledRelease();

            transform.SetParent(null, false);
            transform.position = position;
            transform.rotation = rotation;
            transform.localScale = initialLocalScale;

            ResetRigidbodies();
            ClearTrails();
            gameObject.SetActive(true);

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = true;
                }
            }

            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] != null)
                {
                    particleSystems[i].gameObject.SetActive(true);
                    particleSystems[i].Play(true);
                }
            }

            IsInPool = false;
        }

        public void OnRelease(Transform poolRoot)
        {
            if (IsInPool) return;

            CancelScheduledRelease();
            RestoreAfterSoftRelease();
            NotifyOwnerReleased();

            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] != null)
                {
                    particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = false;
                }
            }

            ResetRigidbodies();
            ClearTrails();

            transform.SetParent(poolRoot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = initialLocalScale;
            gameObject.SetActive(false);
            IsInPool = true;
        }

        public void NotifyOwnerReleased()
        {
            Action<GameObject> tracker = ownerReleaseTracker;
            ownerReleaseTracker = null;
            tracker?.Invoke(gameObject);
        }

        public void ScheduleRelease(float delay)
        {
            CancelScheduledRelease();
            Invoke(nameof(ReleaseToPool), delay);
        }

        public void CancelScheduledRelease()
        {
            CancelInvoke(nameof(ReleaseToPool));
        }

        private void ReleaseToPool()
        {
            DragonKingAssetManager.ReleaseEffect(gameObject);
        }

        private void ResetRigidbodies()
        {
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                if (rigidbodies[i] != null)
                {
                    if (!rigidbodies[i].isKinematic)
                    {
                        rigidbodies[i].velocity = Vector3.zero;
                        rigidbodies[i].angularVelocity = Vector3.zero;
                    }
                }
            }
        }

        private void ClearTrails()
        {
            for (int i = 0; i < trailRenderers.Length; i++)
            {
                if (trailRenderers[i] != null)
                {
                    trailRenderers[i].Clear();
                }
            }
        }

        private void OnDestroy()
        {
            CancelScheduledRelease();
            softReleaseRoutine = null;
            ownerReleaseTracker = null;
        }
    }
}
