// ============================================================================
// FrostmourneSwingFx.cs - 霜之哀伤左键冰色挥击拖尾
// ============================================================================
// 模块说明：
//   1. 在 CA_Attack.OnStart 时为霜之哀伤追加一层挥击拖尾
//   2. 拖尾运动轨迹对齐焚皇断界戟一阶段挥击
//   3. 视觉来源复用龙息火焰拖尾，但统一重染为冰蓝白
//
// 2026-09-23 审美修（VA-11，照 NewWeaponSwingFx 2026-09-19 已验证的做法）：
//   - 旧版 rateOverDistance 15：Unity 把一帧的量全撒在当帧那一个点上，缓出曲线下起手一帧扫掉约 21%
//     的弧，粒子堆在起手处、后半条稀薄。现在关掉自动发射，按上一帧到本帧的角度逐点 Emit（每米 26 颗、
//     单帧最多 12 颗），密度沿弧均匀、与帧率无关；
//   - 旧版 startSizeMultiplier = 1.8：官方 Smoke 若是常量 / 双常量模式，就是把（上限）尺寸直接设成 1.8 m。
//     现在显式写 0.16–0.28 m 的双常量；
//   - 旧版 0.22 s 挥击一结束，整条拖尾和那盏 3 m 的光在同一帧消失。现在挥击结束只停发射，
//     再留 0.35 s 让世界空间粒子自然淡尽；光 range 1.8，强度随拖尾衰减到 0。
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
        private const float ParticleTailDuration = 0.35f;
        private const float StartAngle = -75f;
        private const float SweepAngle = FenHuangHalberdConfig.Combo1Angle;
        private const int MaxPoolSize = 8;

        /// <summary>每米弧长撒几颗（与 NewWeaponSwingFx 同口径）。</summary>
        private const float ParticlesPerMeter = 26f;

        /// <summary>单帧最多补几个采样点，掉帧时也不会一次撒爆。</summary>
        private const int MaxSubStepsPerFrame = 12;

        private const float LightIntensity = 2f;
        private const float LightRange = 1.8f;

        private static readonly Color IceCoreColor = new Color(0.46f, 0.86f, 1f, 0.88f);
        private static readonly Color IceFadeColor = new Color(0.88f, 0.97f, 1f, 0.55f);
        private static readonly Stack<FrostmourneSwingFx> Pool = new Stack<FrostmourneSwingFx>();

        private static readonly GradientColorKey[] IceGradientColorKeys = new GradientColorKey[]
        {
            new GradientColorKey(IceCoreColor, 0f),
            new GradientColorKey(IceFadeColor, 1f)
        };
        private static readonly GradientAlphaKey[] IceGradientAlphaKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(IceCoreColor.a, 0f),
            new GradientAlphaKey(0f, 1f)
        };

        private float elapsed;
        private Transform trailRoot;
        private Transform trailNode;
        private ParticleSystem[] injectedParticles;
        private Light[] injectedLights;
        private bool isPlaying;
        private float lastEmittedAngle;
        private float trailRadius;
        private float particleSizeMin;
        private float particleSizeMax;
        private float lightScale;

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
            lightScale = Mathf.Lerp(1f, clampedRangeScale, 0.4f);

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

            trailRadius = trailDistance;
            particleSizeMin = 0.16f * sizeScale;
            particleSizeMax = 0.28f * sizeScale;
            lastEmittedAngle = StartAngle;

            TintParticlesIce(injectedParticles, sizeScale);
            RestartParticles(injectedParticles);
            TintLightsIce(injectedLights, lightScale);

            elapsed = 0f;
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
                EmitAlongArc(currentAngle);
                lastEmittedAngle = currentAngle;
            }

            // 光随整条拖尾（挥击 + 尾巴）衰减到 0，不在挥击结束那一帧硬关
            float lifeT = Mathf.Clamp01(elapsed / (Duration + ParticleTailDuration));
            SetLightIntensity(LightIntensity * (1f - lifeT) * (1f - lifeT));

            // 挥击停发后让世界空间粒子自然淡出，不能在 0.22 秒时直接清空整条尾迹。
            if (elapsed >= Duration + ParticleTailDuration)
            {
                Recycle();
            }
        }

        /// <summary>在上一帧与这一帧之间按等弧长插值补点，逐点 Emit（不能把一帧的量全撒在当帧位置上）。</summary>
        private void EmitAlongArc(float currentAngle)
        {
            if (injectedParticles == null || trailRoot == null) return;

            float deltaDegrees = currentAngle - lastEmittedAngle;
            if (Mathf.Abs(deltaDegrees) < 0.001f) return;

            float arcLength = Mathf.Abs(deltaDegrees) * Mathf.Deg2Rad * trailRadius;
            int steps = Mathf.Clamp(Mathf.CeilToInt(arcLength * ParticlesPerMeter), 1, MaxSubStepsPerFrame);

            Transform pivotParent = trailRoot.parent != null ? trailRoot.parent : transform;
            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
            emitParams.applyShapeToPosition = false;

            for (int i = 1; i <= steps; i++)
            {
                float angle = Mathf.Lerp(lastEmittedAngle, currentAngle, (float)i / steps);
                Vector3 local = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, trailRadius);
                local += new Vector3(
                    UnityEngine.Random.Range(-0.05f, 0.05f),
                    UnityEngine.Random.Range(-0.06f, 0.06f),
                    UnityEngine.Random.Range(-0.05f, 0.05f));
                emitParams.position = pivotParent.TransformPoint(local);
                emitParams.startSize = UnityEngine.Random.Range(particleSizeMin, particleSizeMax);
                emitParams.startLifetime = UnityEngine.Random.Range(0.15f, 0.35f);

                for (int k = 0; k < injectedParticles.Length; k++)
                {
                    ParticleSystem ps = injectedParticles[k];
                    if (ps != null) ps.Emit(emitParams, 1);
                }
            }
        }

        private static void TintParticlesIce(ParticleSystem[] particleSystems, float sizeScale)
        {
            if (particleSystems == null)
            {
                return;
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(IceGradientColorKeys, IceGradientAlphaKeys);

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
                // 显式写尺寸（不用 multiplier：常量模式下那是把尺寸本身设成 1.8 m）
                main.startSize3D = false;
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f * sizeScale, 0.28f * sizeScale);
                // 手撒期间系统必须一直在跑：duration 盖住整条尾迹，否则 Emit 会被吞掉
                main.duration = Duration + ParticleTailDuration;

                // 发射全部由 EmitAlongArc 逐点给出；自动发射一律关掉
                var emission = ps.emission;
                emission.enabled = false;
                emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);

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
                light.range = LightRange * lightScale;
                light.intensity = LightIntensity;
            }
        }

        private void SetLightIntensity(float intensity)
        {
            if (injectedLights == null) return;
            for (int i = 0; i < injectedLights.Length; i++)
            {
                if (injectedLights[i] != null) injectedLights[i].intensity = intensity;
            }
        }

        private void Recycle()
        {
            if (!isPlaying)
            {
                return;
            }

            isPlaying = false;

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
