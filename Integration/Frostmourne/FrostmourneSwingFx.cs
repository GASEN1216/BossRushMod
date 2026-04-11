// ============================================================================
// FrostmourneSwingFx.cs - 霜之哀伤左键冰色挥击拖尾
// ============================================================================
// 模块说明：
//   1. 在 CA_Attack.OnStart 时为霜之哀伤追加一层挥击拖尾
//   2. 拖尾运动轨迹对齐焚皇断界戟一阶段挥击
//   3. 视觉来源复用龙息火焰拖尾，但统一重染为冰蓝白
// ============================================================================

using HarmonyLib;
using ItemStatsSystem;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class FrostmourneAttackFxPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(CA_Attack __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            try
            {
                CharacterMainControl character = __instance.characterController;
                if (character == null)
                {
                    return;
                }

                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null || melee.Item.TypeID != FrostmourneIds.WeaponTypeId)
                {
                    return;
                }

                FrostmourneWeaponConfig.EnsureMeleeAttackFx(melee);
                SpawnSwingEffect(character, melee);
            }
            catch
            {
            }
        }

        private static void SpawnSwingEffect(CharacterMainControl character, ItemAgent_MeleeWeapon melee)
        {
            if (character == null)
            {
                return;
            }

            float rangeScale = 1f;
            if (melee != null && FrostmourneConfig.BaseAttackRange > 0.01f)
            {
                rangeScale = Mathf.Max(0.2f, melee.AttackRange / FrostmourneConfig.BaseAttackRange);
            }

            Vector3 forward = GetFlatAimDirection(character);
            Vector3 spawnPos = character.transform.position + Vector3.up * 1.1f + forward * 0.15f;
            Quaternion rotation = Quaternion.LookRotation(forward);
            FrostmourneSwingFx.PlayAt(spawnPos, rotation, rangeScale);
        }

        private static Vector3 GetFlatAimDirection(CharacterMainControl character)
        {
            Vector3 aimDirection = character.CurrentAimDirection;
            aimDirection.y = 0f;

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = character.transform.forward;
                aimDirection.y = 0f;
            }

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return aimDirection.normalized;
        }
    }

    public class FrostmourneSwingFx : MonoBehaviour
    {
        private const float BaseTrailDistance = 1.5f;
        private const float MinTrailDistance = 0.35f;
        private const float Duration = 0.22f;
        private const float StartAngle = -75f;
        private const float SweepAngle = FenHuangHalberdConfig.Combo1Angle;
        private const int MaxPoolSize = 8;

        private static readonly Color IceCoreColor = new Color(0.46f, 0.86f, 1f, 0.88f);
        private static readonly Color IceFadeColor = new Color(0.88f, 0.97f, 1f, 0.55f);
        private static readonly Stack<FrostmourneSwingFx> Pool = new Stack<FrostmourneSwingFx>();

        private float elapsed;
        private Transform trailRoot;
        private Transform trailNode;
        private ParticleSystem[] injectedParticles;
        private Light[] injectedLights;
        private bool emissionStoppedForCurrentPlay;
        private bool isPlaying;

        internal static void PlayAt(Vector3 position, Quaternion rotation, float rangeScale)
        {
            FrostmourneSwingFx swingFx = Acquire();
            if (swingFx == null || swingFx.gameObject == null)
            {
                return;
            }

            swingFx.transform.position = position;
            swingFx.transform.rotation = rotation;
            swingFx.gameObject.SetActive(true);
            swingFx.Initialize(rangeScale);
        }

        private static FrostmourneSwingFx Acquire()
        {
            while (Pool.Count > 0)
            {
                FrostmourneSwingFx pooled = Pool.Pop();
                if (pooled != null && pooled.gameObject != null)
                {
                    return pooled;
                }
            }

            GameObject fx = new GameObject("Frostmourne_SwingFX");
            fx.SetActive(false);
            return fx.AddComponent<FrostmourneSwingFx>();
        }

        public void Initialize(float rangeScale)
        {
            float clampedRangeScale = Mathf.Max(0.2f, rangeScale);
            float trailDistance = Mathf.Max(MinTrailDistance, BaseTrailDistance * clampedRangeScale);
            float sizeScale = Mathf.Lerp(1f, clampedRangeScale, 0.35f);
            float lightScale = Mathf.Lerp(1f, clampedRangeScale, 0.4f);

            if (trailRoot == null)
            {
                trailRoot = new GameObject("SwingTrailPivot").transform;
                trailRoot.SetParent(transform, false);
                trailRoot.localPosition = Vector3.zero;

                GameObject trailNodeObject = new GameObject("TrailNode");
                trailNode = trailNodeObject.transform;
                trailNode.SetParent(trailRoot, false);

                FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(trailNodeObject);

                injectedParticles = trailNodeObject.GetComponentsInChildren<ParticleSystem>(true);
                injectedLights = trailNodeObject.GetComponentsInChildren<Light>(true);
            }

            trailRoot.localRotation = Quaternion.Euler(0f, StartAngle, 0f);
            if (trailNode != null)
            {
                trailNode.localPosition = new Vector3(0f, 0f, trailDistance);
            }

            TintParticlesIce(injectedParticles, sizeScale);
            RestartParticles(injectedParticles);
            TintLightsIce(injectedLights, lightScale);

            elapsed = 0f;
            emissionStoppedForCurrentPlay = false;
            isPlaying = true;
        }

        private void Update()
        {
            if (!isPlaying)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);

            if (trailRoot != null)
            {
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                float currentAngle = StartAngle + SweepAngle * easeT;
                trailRoot.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
            }

            if (!emissionStoppedForCurrentPlay && injectedParticles != null && t >= 0.8f)
            {
                emissionStoppedForCurrentPlay = true;
                for (int i = 0; i < injectedParticles.Length; i++)
                {
                    ParticleSystem ps = injectedParticles[i];
                    if (ps != null && ps.isPlaying)
                    {
                        var emission = ps.emission;
                        emission.enabled = false;
                    }
                }
            }

            if (t >= 1f)
            {
                Recycle();
            }
        }

        private static void TintParticlesIce(ParticleSystem[] particleSystems, float sizeScale)
        {
            if (particleSystems == null)
            {
                return;
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(IceCoreColor, 0f),
                    new GradientColorKey(IceFadeColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(IceCoreColor.a, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null)
                {
                    continue;
                }

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(IceCoreColor, IceFadeColor);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                main.startSizeMultiplier = 1.8f * sizeScale;

                var emission = ps.emission;
                emission.rateOverDistance = new ParticleSystem.MinMaxCurve(15f);
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(10f);

                var colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
                }
            }
        }

        private static void RestartParticles(ParticleSystem[] particleSystems)
        {
            if (particleSystems == null)
            {
                return;
            }

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null)
                {
                    continue;
                }

                var emission = ps.emission;
                emission.enabled = true;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        private static void TintLightsIce(Light[] lights, float lightScale)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                {
                    continue;
                }

                light.color = IceCoreColor;
                light.range = 3f * lightScale;
                light.intensity = 2f;
            }
        }

        private void Recycle()
        {
            if (!isPlaying)
            {
                return;
            }

            isPlaying = false;
            emissionStoppedForCurrentPlay = false;

            if (injectedParticles != null)
            {
                for (int i = 0; i < injectedParticles.Length; i++)
                {
                    ParticleSystem ps = injectedParticles[i];
                    if (ps != null)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }

            if (Pool.Count < MaxPoolSize)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

    }
}
