// ============================================================================
// PhantomWitchScytheSwingFx.cs - 噬魂挽歌（幽灵女巫镰刀）左键紫幽色挥击拖尾
// ============================================================================
// 模块说明：
//   1. 在 CA_Attack.OnStart 时为噬魂挽歌追加一层挥击拖尾，补齐手感。
//   2. 视觉元素：紫色核心(VioletVoidCore)到灰冷粉紫(SilverAshCore)的渐变。
//   3. 添加真实点光源(Point Light)照亮环境，增强破坏力表现。
// ============================================================================

using HarmonyLib;
using ItemStatsSystem;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class PhantomWitchScytheAttackFxPatch
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
                if (melee == null || melee.Item == null || melee.Item.TypeID != PhantomWitchScytheIds.WeaponTypeId)
                {
                    return;
                }

                PhantomWitchScytheWeaponConfig.EnsureMeleeAttackFx(melee);
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
            if (melee != null && PhantomWitchScytheConfig.BaseAttackRange > 0.01f)
            {
                rangeScale = Mathf.Max(0.2f, melee.AttackRange / PhantomWitchScytheConfig.BaseAttackRange);
            }

            Vector3 forward = GetFlatAimDirection(character);
            Vector3 spawnPos = character.transform.position + Vector3.up * 1.1f + forward * 0.15f;
            Quaternion rotation = Quaternion.LookRotation(forward);
            PhantomWitchScytheSwingFx.PlayAt(spawnPos, rotation, rangeScale);
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

    public class PhantomWitchScytheSwingFx : MonoBehaviour
    {
        private const float BaseTrailDistance = 1.35f;
        private const float MinTrailDistance = 0.35f;
        private const float Duration = 0.22f;
        private const float StartAngle = -75f;
        private const float SweepAngle = 180f;
        private const int MaxPoolSize = 6;

        // 核心亮点
        private static readonly Color ScytheCoreColor = new Color(0.85f, 0.45f, 1f, 0.95f);
        // 消散尾焰亮紫色
        private static readonly Color ScytheFadeColor = new Color(0.65f, 0.2f, 0.85f, 0.45f);

        private static readonly Gradient CachedScytheGradient;

        static PhantomWitchScytheSwingFx()
        {
            CachedScytheGradient = new Gradient();
            CachedScytheGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(ScytheCoreColor, 0f),
                    new GradientColorKey(ScytheCoreColor, 0.4f),
                    new GradientColorKey(ScytheFadeColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.85f, 0f),
                    new GradientAlphaKey(0.5f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
        }

        private static readonly Stack<PhantomWitchScytheSwingFx> Pool = new Stack<PhantomWitchScytheSwingFx>();

        private float elapsed;
        private Transform trailRoot;
        private Transform trailNode;
        private ParticleSystem[] injectedParticles;
        private Light[] injectedLights;
        private bool emissionStoppedForCurrentPlay;
        private bool isPlaying;

        internal static void PlayAt(Vector3 position, Quaternion rotation, float rangeScale)
        {
            PhantomWitchScytheSwingFx swingFx = Acquire();
            if (swingFx == null || swingFx.gameObject == null)
            {
                return;
            }

            swingFx.transform.position = position;
            swingFx.transform.rotation = rotation;
            swingFx.gameObject.SetActive(true);
            swingFx.Initialize(rangeScale);
        }

        private static PhantomWitchScytheSwingFx Acquire()
        {
            while (Pool.Count > 0)
            {
                PhantomWitchScytheSwingFx pooled = Pool.Pop();
                if (pooled != null && pooled.gameObject != null)
                {
                    return pooled;
                }
            }

            GameObject fx = new GameObject("PhantomWitch_SwingFX");
            fx.SetActive(false);
            return fx.AddComponent<PhantomWitchScytheSwingFx>();
        }

        public void Initialize(float rangeScale)
        {
            float clampedRangeScale = Mathf.Max(0.2f, rangeScale);
            float trailDistance = Mathf.Max(MinTrailDistance, BaseTrailDistance * clampedRangeScale);
            float sizeScale = Mathf.Lerp(1f, clampedRangeScale, 0.35f);
            float lightScale = Mathf.Lerp(1f, clampedRangeScale, 0.4f);

            if (trailRoot == null)
            {
                trailRoot = new GameObject("ScytheSwingTrailPivot").transform;
                trailRoot.SetParent(transform, false);
                trailRoot.localPosition = Vector3.zero;

                GameObject trailNodeObject = new GameObject("TrailNode");
                trailNode = trailNodeObject.transform;
                trailNode.SetParent(trailRoot, false);

                BuildCustomScytheParticles(trailNodeObject);

                injectedParticles = trailNodeObject.GetComponentsInChildren<ParticleSystem>(true);
                injectedLights = trailNodeObject.GetComponentsInChildren<Light>(true);
            }

            trailRoot.localRotation = Quaternion.Euler(0f, StartAngle, 0f);
            if (trailNode != null)
            {
                trailNode.localPosition = new Vector3(0f, 0f, trailDistance);
            }

            TintParticles(injectedParticles, sizeScale);
            RestartParticles(injectedParticles);
            TintLights(injectedLights, lightScale);

            elapsed = 0f;
            emissionStoppedForCurrentPlay = false;
            isPlaying = true;
        }

        private void BuildCustomScytheParticles(GameObject node)
        {
            // 如果后续可以加载真正的镰刀刀光粒子模型，这里可以直接复制。
            // 当前我们使用代码动态构建一个基础的漂亮拖尾粒子。

            ParticleSystem ps = node.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(18f);
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(15f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.5f, 2.5f, 0.1f); // 高度拉长以模拟剑气

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;

            var velocityOverLife = ps.velocityOverLifetime;
            velocityOverLife.enabled = true;
            velocityOverLife.space = ParticleSystemSimulationSpace.Local;
            velocityOverLife.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velocityOverLife.y = new ParticleSystem.MinMaxCurve(-1f, 1f);
            velocityOverLife.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            PhantomWitchAssetManager.ConfigureSharedParticleRenderer(ps);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // 附加光源与动态脉冲
            Light light = node.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 5f;
            light.range = 5.5f;
            light.color = ScytheCoreColor;

            PhantomWitchLightPulse pulser = node.AddComponent<PhantomWitchLightPulse>();
            pulser.Configure(5f, 5.5f, 0.35f);

            // 附加额外星屑层
            ParticleSystem stardust = PhantomWitchVfxRedesign.CreateStardustEmitter(node.transform, 1.2f, 35f, 0.3f);
            stardust.transform.localPosition = Vector3.zero;
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

            if (!emissionStoppedForCurrentPlay && injectedParticles != null && t >= 0.7f)
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

        private static void TintParticles(ParticleSystem[] particleSystems, float sizeScale)
        {
            if (particleSystems == null)
            {
                return;
            }

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null) continue;

                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(ScytheCoreColor, ScytheFadeColor);
                main.startSizeMultiplier = 2.0f * sizeScale;

                var colorOverLifetime = ps.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(CachedScytheGradient);
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
                if (ps == null) continue;

                var emission = ps.emission;
                emission.enabled = true;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        private static void TintLights(Light[] lights, float lightScale)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;

                light.color = ScytheCoreColor;
                light.range = 4f * lightScale;
                light.intensity = 3.5f;
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
