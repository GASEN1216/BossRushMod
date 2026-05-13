using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private readonly Dictionary<int, bool> zombieModeMeleeWeaponTypeCache = new Dictionary<int, bool>();
        private readonly MaterialPropertyBlock zombieModeRendererColorBlock = new MaterialPropertyBlock();
        private const string ZombieModePlagueAuraCarrierName = "ZombieMode_PlagueFrostmourneAura";
        private const string ZombieModeFrostmourneAuraRootName = "Frostmourne_IceAura";
        private static readonly int ZombieModeRendererColorProperty = Shader.PropertyToID("_Color");
        private static readonly int ZombieModeRendererTintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int ZombieModeRendererBaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly Color ZombieModePlagueAuraCoreColor = new Color(0.35f, 1.00f, 0.55f, 0.88f);
        private static readonly Color ZombieModePlagueAuraFadeColor = new Color(0.72f, 1.00f, 0.75f, 0.45f);

        private void SetZombieModeRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(zombieModeRendererColorBlock);
            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null && sharedMaterial.HasProperty(ZombieModeRendererColorProperty))
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererColorProperty, color);
            }
            else if (sharedMaterial != null && sharedMaterial.HasProperty(ZombieModeRendererTintColorProperty))
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererTintColorProperty, color);
            }
            else
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererBaseColorProperty, color);
            }

            renderer.SetPropertyBlock(zombieModeRendererColorBlock);
        }

        // ====================================================================
        // 共享 disk visual mesh / material（审查 §3.3）
        // ====================================================================
        // 之前 8 处 telegraph / 区域 visual 都用 GameObject.CreatePrimitive(Cylinder)，
        // 自带 MeshFilter+MeshRenderer+CapsuleCollider，然后又 Destroy(collider)；
        // 高峰每秒 5–15 个新 GameObject 触发 GC + 物理初始化 ≈ 0.5–1 ms/帧。
        //
        // 改造：第一次仍然用 CreatePrimitive 提取一个 mesh 副本作为 shared mesh，
        // material 重新构造一次 standard shader 实例并共享；之后所有调用直接 new
        // GameObject + AddComponent<MeshFilter+MeshRenderer> 用 sharedMesh / sharedMaterial。
        // 省去 collider 构造 + destroy、省去 default-material 实例化。
        // ====================================================================
        private static Mesh s_zoneDiskMesh;
        private static Material s_zoneDiskMaterial;

        private static void EnsureZoneDiskAssets()
        {
            if (s_zoneDiskMesh == null)
            {
                GameObject scratch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                MeshFilter mf = scratch.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    s_zoneDiskMesh = mf.sharedMesh;
                }
                UnityEngine.Object.Destroy(scratch);
            }

            if (s_zoneDiskMaterial == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }
                s_zoneDiskMaterial = shader != null ? new Material(shader) : null;
            }
        }

        /// <summary>
        /// 创建丧尸模式平面区域 visual（telegraph / 区域伤害指示）。
        /// 替换 GameObject.CreatePrimitive(Cylinder) + Destroy(collider) 模式。
        /// 调用方仍负责 RegisterZombieModeRunOnlyObject + AddComponent<TickRuntime>。
        /// </summary>
        private GameObject CreateZombieModeFlatZoneVisual(string name, Vector3 origin, float radius, float height, Color color)
        {
            EnsureZoneDiskAssets();

            GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "ZombieMode_Zone" : name);
            go.transform.position = origin + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(radius * 2f, Mathf.Max(0.01f, height), radius * 2f);

            if (s_zoneDiskMesh != null)
            {
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = s_zoneDiskMesh;
            }

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (s_zoneDiskMaterial != null)
            {
                mr.sharedMaterial = s_zoneDiskMaterial;
            }
            try
            {
                SetZombieModeRendererColor(mr, color);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] FlatZone visual 调色失败: " + e.Message);
            }
            return go;
        }

        private void ApplyZombieModePlagueFrostmourneAura(CharacterMainControl enemy)
        {
            if (enemy == null || enemy.gameObject == null)
            {
                return;
            }

            Transform carrierTransform = enemy.transform.Find(ZombieModePlagueAuraCarrierName);
            GameObject carrier;
            if (carrierTransform != null)
            {
                carrier = carrierTransform.gameObject;
            }
            else
            {
                carrier = new GameObject(ZombieModePlagueAuraCarrierName);
                carrier.transform.SetParent(enemy.transform, false);
                carrier.transform.localPosition = Vector3.up * 0.85f;
                carrier.transform.localRotation = Quaternion.identity;
                carrier.transform.localScale = Vector3.one;
            }

            try
            {
                FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(carrier);
                ConfigureZombieModePlagueFrostmourneAura(carrier);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 瘟疫丧尸霜之哀伤特效初始化失败: " + e.Message);
            }
        }

        private void ConfigureZombieModePlagueFrostmourneAura(GameObject carrier)
        {
            if (carrier == null)
            {
                return;
            }

            Transform auraRoot = FindZombieModeChildRecursive(carrier.transform, ZombieModeFrostmourneAuraRootName);
            if (auraRoot == null)
            {
                CreateZombieModePlagueFallbackMist(carrier.transform);
            }

            ParticleSystem[] particles = carrier.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length <= 0)
            {
                CreateZombieModePlagueFallbackMist(carrier.transform);
                particles = carrier.GetComponentsInChildren<ParticleSystem>(true);
            }

            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                if (ps == null)
                {
                    continue;
                }

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = new ParticleSystem.MinMaxGradient(ZombieModePlagueAuraCoreColor, ZombieModePlagueAuraFadeColor);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
                main.maxParticles = 45;
                main.loop = true;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 3f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 1.25f;
                shape.scale = new Vector3(2.1f, 2.4f, 2.1f);

                var velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.y = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
                velocity.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
                velocity.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);

                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(ZombieModePlagueAuraCoreColor, 0f),
                        new GradientColorKey(ZombieModePlagueAuraFadeColor, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(0.65f, 0f),
                        new GradientAlphaKey(0.28f, 0.65f),
                        new GradientAlphaKey(0f, 1f)
                    });

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                if (!ps.isPlaying)
                {
                    ps.Play(true);
                }
            }

            FrostmourneRuntimeMaterialTracker tracker = FrostmourneRuntimeMaterialTracker.GetOrAdd(carrier);
            Renderer[] renderers = carrier.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterials == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                Material[] tintedMaterials = new Material[materials.Length];
                for (int j = 0; j < materials.Length; j++)
                {
                    Material source = materials[j];
                    if (source == null)
                    {
                        continue;
                    }

                    Material tinted = new Material(source);
                    if (tinted.HasProperty(ZombieModeRendererColorProperty)) tinted.color = ZombieModePlagueAuraCoreColor;
                    if (tinted.HasProperty(ZombieModeRendererTintColorProperty)) tinted.SetColor(ZombieModeRendererTintColorProperty, ZombieModePlagueAuraCoreColor);
                    if (tinted.HasProperty(ZombieModeRendererBaseColorProperty)) tinted.SetColor(ZombieModeRendererBaseColorProperty, ZombieModePlagueAuraCoreColor);
                    if (tinted.HasProperty("_EmissionColor")) tinted.SetColor("_EmissionColor", ZombieModePlagueAuraCoreColor * 0.55f);
                    tintedMaterials[j] = tinted;
                    if (tracker != null)
                    {
                        tracker.Track(tinted);
                    }
                }

                renderer.sharedMaterials = tintedMaterials;
            }

            Light[] lights = carrier.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null)
                {
                    continue;
                }
                light.color = ZombieModePlagueAuraCoreColor;
                light.range = 2.8f;
                light.intensity = 1.25f;
            }
        }

        private void CreateZombieModePlagueFallbackMist(Transform parent)
        {
            if (parent == null || parent.Find("ZombieMode_PlagueFallbackMist") != null)
            {
                return;
            }

            GameObject mist = new GameObject("ZombieMode_PlagueFallbackMist");
            mist.transform.SetParent(parent, false);
            mist.transform.localPosition = Vector3.zero;
            mist.transform.localRotation = Quaternion.identity;
            mist.transform.localScale = Vector3.one;

            ParticleSystem ps = mist.AddComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = mist.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }
                if (shader != null)
                {
                    Material material = new Material(shader);
                    if (material.HasProperty(ZombieModeRendererColorProperty)) material.color = ZombieModePlagueAuraCoreColor;
                    if (material.HasProperty(ZombieModeRendererTintColorProperty)) material.SetColor(ZombieModeRendererTintColorProperty, ZombieModePlagueAuraCoreColor);
                    if (material.HasProperty(ZombieModeRendererBaseColorProperty)) material.SetColor(ZombieModeRendererBaseColorProperty, ZombieModePlagueAuraCoreColor);
                    renderer.sharedMaterial = material;
                    FrostmourneRuntimeMaterialTracker tracker = FrostmourneRuntimeMaterialTracker.GetOrAdd(parent.gameObject);
                    if (tracker != null)
                    {
                        tracker.Track(material);
                    }
                }
            }
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = new ParticleSystem.MinMaxGradient(ZombieModePlagueAuraCoreColor, ZombieModePlagueAuraFadeColor);
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            main.maxParticles = 45;
            main.loop = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 3f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.25f;
            shape.scale = new Vector3(2.1f, 2.4f, 2.1f);
            ps.Play(true);
        }

        private Transform FindZombieModeChildRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (root.name == targetName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindZombieModeChildRecursive(root.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            int pollution = zombieModeRunState.TotalPollution;
            int eliteChance = GetZombieModeEliteChancePercent(pollution);
            int specialChance = GetZombieModeSpecialChancePercent(pollution);
            int roll = Random.Range(0, 100);
            if (roll < eliteChance)
            {
                return ZombieModeEnemyKind.Elite;
            }

            if (roll < eliteChance + specialChance)
            {
                return ZombieModeEnemyKind.Special;
            }

            return ZombieModeEnemyKind.Normal;
        }

        private int GetZombieModeSpecialChancePercent(int pollution)
        {
            if (pollution >= 25) return 30;
            if (pollution >= 20) return 25;
            if (pollution >= 15) return 20;
            if (pollution >= 10) return 15;
            if (pollution >= 5) return 10;
            return 5;
        }

        private int GetZombieModeEliteChancePercent(int pollution)
        {
            if (pollution >= 25) return 10;
            if (pollution >= 20) return 8;
            if (pollution >= 15) return 6;
            if (pollution >= 10) return 4;
            if (pollution >= 5) return 2;
            return 1;
        }

        // 静态读取以避免每次刷怪 new[] 装箱（审查 §3.7）。
        private static readonly ZombieModeSpecialKind[] s_zombieModeSpecialKindOrder = new ZombieModeSpecialKind[]
        {
            ZombieModeSpecialKind.Sprinter,
            ZombieModeSpecialKind.Exploder,
            ZombieModeSpecialKind.Plague,
            ZombieModeSpecialKind.Summoner,
            ZombieModeSpecialKind.Harasser
        };

        private static readonly ZombieModeSpecialKind[] s_zombieModeEarlySpecialKindOrder = new ZombieModeSpecialKind[]
        {
            ZombieModeSpecialKind.Sprinter,
            ZombieModeSpecialKind.Plague,
            ZombieModeSpecialKind.Summoner,
            ZombieModeSpecialKind.Harasser
        };

        // System.Enum.GetValues 返回 Array 会装箱；缓存为强类型数组，避免每次 Roll 触发 GC。
        private static readonly ZombieModeEliteAffix[] s_zombieModeEliteAffixAll = (ZombieModeEliteAffix[])
            System.Enum.GetValues(typeof(ZombieModeEliteAffix));

        // RollZombieModeEliteAffixes 内的两个临时容器；Clear() + 复用避免每次 new。
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchSelected = new List<ZombieModeEliteAffix>();
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchPool = new List<ZombieModeEliteAffix>();

        private ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            ZombieModeSpecialKind[] order = zombieModeRunState.CurrentWave <= 5
                ? s_zombieModeEarlySpecialKindOrder
                : s_zombieModeSpecialKindOrder;
            return order[Random.Range(0, order.Length)];
        }

        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            int pollution = zombieModeRunState.TotalPollution;
            int desiredCount = 1;
            if (pollution >= 25)
            {
                desiredCount = Random.Range(2, 4);
            }
            else if (pollution >= 15)
            {
                desiredCount = 2;
            }
            else if (pollution >= 5 && Random.value < 0.35f)
            {
                desiredCount = 2;
            }

            // 注：调用方拿到 list 后会持久持有（marker.EliteAffixes），所以 selected 必须 new 出独立副本；
            // 但 pool 是临时枚举池，可以复用 scratch 容器。
            rollEliteAffixesScratchPool.Clear();
            for (int i = 0; i < s_zombieModeEliteAffixAll.Length; i++)
            {
                ZombieModeEliteAffix affix = s_zombieModeEliteAffixAll[i];
                if (GetZombieModeAffixUnlockTier(affix) <= zombieModeRunState.PollutionTier)
                {
                    rollEliteAffixesScratchPool.Add(affix);
                }
            }

            List<ZombieModeEliteAffix> selected = new List<ZombieModeEliteAffix>();
            while (selected.Count < desiredCount && rollEliteAffixesScratchPool.Count > 0)
            {
                int index = Random.Range(0, rollEliteAffixesScratchPool.Count);
                ZombieModeEliteAffix candidate = rollEliteAffixesScratchPool[index];
                rollEliteAffixesScratchPool.RemoveAt(index);
                selected.Add(candidate);
                if (!IsZombieModeAffixCombinationAllowed(selected))
                {
                    selected.Remove(candidate);
                }
            }

            if (selected.Count <= 0)
            {
                selected.Add(ZombieModeEliteAffix.Tough);
            }

            rollEliteAffixesScratchPool.Clear();
            return selected;
        }

        private int GetZombieModeAffixUnlockTier(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                case ZombieModeEliteAffix.Frenzied:
                case ZombieModeEliteAffix.Tough:
                    return 0;
                case ZombieModeEliteAffix.Stalwart:
                case ZombieModeEliteAffix.Regenerating:
                case ZombieModeEliteAffix.Burst:
                case ZombieModeEliteAffix.Plague:
                    return 1;
                case ZombieModeEliteAffix.Commander:
                case ZombieModeEliteAffix.ToxicAura:
                case ZombieModeEliteAffix.Splitting:
                case ZombieModeEliteAffix.Shielded:
                    return 3;
                default:
                    return 5;
            }
        }

        private bool IsZombieModeAffixCombinationAllowed(List<ZombieModeEliteAffix> affixes)
        {
            if (affixes == null)
            {
                return true;
            }

            bool stalwart = affixes.Contains(ZombieModeEliteAffix.Stalwart);
            bool shielded = affixes.Contains(ZombieModeEliteAffix.Shielded);
            bool regenerating = affixes.Contains(ZombieModeEliteAffix.Regenerating);
            bool swift = affixes.Contains(ZombieModeEliteAffix.Swift);
            bool toxicAura = affixes.Contains(ZombieModeEliteAffix.ToxicAura);
            bool plague = affixes.Contains(ZombieModeEliteAffix.Plague);

            if (stalwart && shielded && regenerating)
            {
                return false;
            }

            if (stalwart && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            if (toxicAura && plague && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            return true;
        }

        private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            if (isBoss)
            {
                return Random.Range(300, 801);
            }

            int min = ZombieModeTuning.NormalPurificationMin;
            int max = ZombieModeTuning.NormalPurificationMax;
            if (enemyKind == ZombieModeEnemyKind.Special)
            {
                min = ZombieModeTuning.SpecialPurificationMin;
                max = ZombieModeTuning.SpecialPurificationMax;
            }
            else if (enemyKind == ZombieModeEnemyKind.Elite)
            {
                min = ZombieModeTuning.ElitePurificationMin;
                max = ZombieModeTuning.ElitePurificationMax;
            }

            int baseValue = Random.Range(min, max + 1);
            int pollutionSteps = Mathf.FloorToInt(zombieModeRunState.TotalPollution / 10f);
            float multiplier = Mathf.Min(1f + pollutionSteps * 0.10f, ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss)
            {
                return;
            }

            float healthMultiplier = 1f;
            float damageMultiplier = 1f;
            float speedMultiplier = 1f;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                healthMultiplier = ZombieModeTuning.SpecialHealthMultiplier;
                damageMultiplier = ZombieModeTuning.SpecialDamageMultiplier;
                speedMultiplier = ZombieModeTuning.SpecialMoveSpeedMultiplier;
                ApplyZombieModeSpecialKindTuning(marker.SpecialKind, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
                if (marker.SpecialKind == ZombieModeSpecialKind.Plague)
                {
                    ApplyZombieModePlagueFrostmourneAura(enemy);
                }
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                bool enhanced = zombieModeRunState.TotalPollution >= 15;
                healthMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteHealthMultiplier : ZombieModeTuning.EliteHealthMultiplier;
                damageMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteDamageMultiplier : ZombieModeTuning.EliteDamageMultiplier;
                speedMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteMoveSpeedMultiplier : ZombieModeTuning.EliteMoveSpeedMultiplier;
                ApplyZombieModeEliteAffixTuning(enemy, marker, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
            }

            float pollutionHealthScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionHealthScalePerPoint;
            float pollutionDamageScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionDamageScalePerPoint;
            marker.HealthMultiplier = healthMultiplier * pollutionHealthScale;
            marker.DamageMultiplier = damageMultiplier * pollutionDamageScale;
            marker.MoveSpeedMultiplier = speedMultiplier;
            ApplyZombieModeHealthMultiplier(enemy, marker.HealthMultiplier, marker);
            ApplyZombieModeEnemyCombatStatMultipliers(enemy, marker.DamageMultiplier, marker.MoveSpeedMultiplier, marker);
            ApplyZombieModeEnemyName(enemy, marker);
            EnsureZombieModeThreatRuntime(enemy, marker);
        }

        private void ApplyZombieModeSpecialKindTuning(
            ZombieModeSpecialKind specialKind,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    speedMultiplier *= 1.20f;
                    break;
                case ZombieModeSpecialKind.Exploder:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
                case ZombieModeSpecialKind.Plague:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Summoner:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Harasser:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
            }
        }

        private void ApplyZombieModeEliteAffixTuning(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                ZombieModeEliteAffix affix = marker.EliteAffixes[i];
                if (affix == ZombieModeEliteAffix.Swift)
                {
                    speedMultiplier *= 1.30f;
                }
                else if (affix == ZombieModeEliteAffix.Tough)
                {
                    healthMultiplier *= 1.40f;
                }
                else if (affix == ZombieModeEliteAffix.Frenzied)
                {
                    damageMultiplier *= 1.15f;
                    speedMultiplier *= 1.10f;
                }
                else if (affix == ZombieModeEliteAffix.Stalwart)
                {
                    healthMultiplier *= 1.15f;
                }
                else if (affix == ZombieModeEliteAffix.Regenerating)
                {
                    ZombieModeRegenerationAffixRuntime regen = enemy.gameObject.GetComponent<ZombieModeRegenerationAffixRuntime>();
                    if (regen == null)
                    {
                        regen = enemy.gameObject.AddComponent<ZombieModeRegenerationAffixRuntime>();
                    }
                    regen.Initialize(marker.RunId);
                }
                else if (affix == ZombieModeEliteAffix.Shielded)
                {
                    healthMultiplier *= 1.25f;
                }
            }
        }

        private void ApplyZombieModeEnemyHurtAffixes(
            int runId,
            Health health,
            DamageInfo damageInfo,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                damageInfo.fromCharacter == null ||
                marker == null ||
                marker.EnemyKind != ZombieModeEnemyKind.Elite)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                ZombieModeShieldedAffixRuntime shield = marker.ShieldedAffix;
                if (shield != null)
                {
                    float dmg = damageInfo.damageValue;
                    if (shield.AbsorbDamage(ref dmg))
                    {
                        float absorbed = damageInfo.damageValue - dmg;
                        if (absorbed > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + absorbed));
                        }
                    }
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Stalwart) &&
                damageInfo.fromCharacter.IsMainCharacter &&
                !IsZombieModeDamageFromMeleeWeapon(damageInfo))
            {
                float restore = Mathf.Max(0f, damageInfo.damageValue * (1f - ZombieModeTuning.StalwartRangedDamageMultiplier));
                if (restore > 0f && health.CurrentHealth > 0f)
                {
                    health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + restore));
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive) &&
                damageInfo.fromCharacter.IsMainCharacter)
            {
                float now = GetZombieModeRuntimeNow();
                bool isMelee = IsZombieModeDamageFromMeleeWeapon(damageInfo);
                if (now > marker.AdaptiveReductionEndTime)
                {
                    marker.AdaptiveRangedActive = false;
                    marker.AdaptiveMeleeActive = false;
                }

                if (isMelee)
                {
                    marker.AdaptiveMeleeHitCount++;
                    marker.AdaptiveRangedHitCount = 0;
                    if (marker.AdaptiveMeleeHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveMeleeActive)
                    {
                        marker.AdaptiveMeleeActive = true;
                        marker.AdaptiveRangedActive = false;
                        marker.AdaptiveReductionEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveMeleeHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveMeleeActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
                else
                {
                    marker.AdaptiveRangedHitCount++;
                    marker.AdaptiveMeleeHitCount = 0;
                    if (marker.AdaptiveRangedHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveRangedActive)
                    {
                        marker.AdaptiveRangedActive = true;
                        marker.AdaptiveMeleeActive = false;
                        marker.AdaptiveReductionEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveRangedHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveRangedActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
            }
        }

        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            if (damageInfo.fromWeaponItemID <= 0)
            {
                return false;
            }

            bool cached;
            if (zombieModeMeleeWeaponTypeCache.TryGetValue(damageInfo.fromWeaponItemID, out cached))
            {
                return cached;
            }

            return CacheZombieModeMeleeWeaponType(damageInfo.fromWeaponItemID);
        }

        private bool CacheZombieModeMeleeWeaponType(int typeId)
        {
            bool melee = false;
            bool resolved = false;
            try
            {
                ItemStatsSystem.Item item = null;
                try { item = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId); } catch (System.Exception e) { DevLog("[ZombieMode] GetPrefab(" + typeId + ") 失败，回退 ItemFactory: " + e.Message); }
                if (item == null)
                {
                    try { item = ItemFactory.GetLoadedItem(typeId); } catch (System.Exception e) { DevLog("[ZombieMode] ItemFactory.GetLoadedItem(" + typeId + ") 失败: " + e.Message); }
                }

                if (item != null)
                {
                    melee = ItemHasZombieModeTag(item, "MeleeWeapon") ||
                            item.GetComponent<ItemAgent_MeleeWeapon>() != null;
                    resolved = true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 近战武器类型缓存失败: " + e.Message);
            }

            if (resolved)
            {
                zombieModeMeleeWeaponTypeCache[typeId] = melee;
            }
            return melee;
        }

        private bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            if (item == null || string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            try
            {
                Duckov.Utilities.Tag target = FindZombieModeTagByName(tagName);
                if (target == null || item.Tags == null)
                {
                    return false;
                }
                if (item.Tags.Contains(target))
                {
                    return true;
                }
                // 名称回退：避免 Tag 实例不一致（hot reload / 不同程序集）导致 Contains 漏判
                foreach (Duckov.Utilities.Tag tag in item.Tags)
                {
                    if (tag != null && string.Equals(tag.name, target.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyZombieModeHealthMultiplier(
            CharacterMainControl enemy,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || enemy.Health == null)
            {
                return;
            }

            marker.BaseMaxHealth = enemy.Health.MaxHealth;

            ApplyZombieModeHealthOnlyMultiplier(enemy, healthMultiplier, marker);

            enemy.Health.showHealthBar = marker.EnemyKind == ZombieModeEnemyKind.Elite;
            if (enemy.Health.MaxHealth > 0f)
            {
                enemy.Health.CurrentHealth = enemy.Health.MaxHealth;
            }
        }

        private void ApplyZombieModeHealthOnlyMultiplier(
            CharacterMainControl character,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker = null)
        {
            if (character == null || character.CharacterItem == null || Mathf.Approximately(healthMultiplier, 1f))
            {
                return;
            }

            try
            {
                if (marker != null && character.Health != null)
                {
                    marker.BaseMaxHealth = character.Health.MaxHealth;
                }

                if (marker == null)
                {
                    return;
                }

                RuntimeStatModifierTracker.TryAdd(
                    character,
                    ZombieModeStatNames.MaxHealth,
                    healthMultiplier - 1f,
                    marker,
                    marker.RuntimeModifierRecords,
                    "ZombieMode Enemy MaxHealth");

                if (character.Health != null && character.Health.MaxHealth > 0f)
                {
                    character.Health.SetHealth(character.Health.MaxHealth);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Health-only MaxHealth 倍率应用失败: " + e.Message);
            }
        }

        private void ApplyZombieModeEnemyCombatStatMultipliers(
            CharacterMainControl enemy,
            float damageMultiplier,
            float speedMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || enemy.CharacterItem == null || marker == null)
            {
                return;
            }

            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.WalkSpeed, speedMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.RunSpeed, speedMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.MeleeDamageMultiplier, damageMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.GunDamageMultiplier, damageMultiplier, marker);
        }

        private void TryApplyZombieModeEnemyStatMultiplier(
            CharacterMainControl character,
            string statName,
            float multiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (character == null || marker == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(multiplier, 1f))
            {
                return;
            }

            try
            {
                RuntimeStatModifierTracker.TryAdd(
                    character,
                    statName,
                    multiplier - 1f,
                    marker,
                    marker.RuntimeModifierRecords,
                    "ZombieMode Enemy Stat");
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] AI 倍率 Modifier 应用失败: " + e.Message);
            }
        }


        private void ApplyZombieModeEnemyName(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null)
            {
                return;
            }

            string label = string.Empty;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                label = GetZombieModeSpecialDisplayName(marker.SpecialKind);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                label = GetZombieModeEliteAffixLabel(marker);
            }

            if (!string.IsNullOrEmpty(label))
            {
                enemy.gameObject.name = enemy.gameObject.name + "_" + label;
                enemy.PopText(label);
            }
        }

        private string GetZombieModeSpecialDisplayName(ZombieModeSpecialKind specialKind)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return L10n.T("BossRush_ZombieMode_Special_Sprinter");
                case ZombieModeSpecialKind.Exploder:
                    return L10n.T("BossRush_ZombieMode_Special_Exploder");
                case ZombieModeSpecialKind.Plague:
                    return L10n.T("BossRush_ZombieMode_Special_Plague");
                case ZombieModeSpecialKind.Summoner:
                    return L10n.T("BossRush_ZombieMode_Special_Summoner");
                case ZombieModeSpecialKind.Harasser:
                    return L10n.T("BossRush_ZombieMode_Special_Harasser");
                default:
                    return string.Empty;
            }
        }

        private string GetZombieModeEliteAffixLabel(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.EliteAffixes.Count <= 0)
            {
                return L10n.T("BossRush_ZombieMode_Elite");
            }

            string label = string.Empty;
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                if (i > 0)
                {
                    label += "\u00B7";
                }
                label += GetZombieModeEliteAffixDisplayName(marker.EliteAffixes[i]);
            }
            return "[" + label + "]" + L10n.T("BossRush_ZombieMode_Elite");
        }

        private string GetZombieModeEliteAffixDisplayName(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                    return L10n.T("BossRush_ZombieMode_Affix_Swift");
                case ZombieModeEliteAffix.Frenzied:
                    return L10n.T("BossRush_ZombieMode_Affix_Frenzied");
                case ZombieModeEliteAffix.Tough:
                    return L10n.T("BossRush_ZombieMode_Affix_Tough");
                case ZombieModeEliteAffix.Stalwart:
                    return L10n.T("BossRush_ZombieMode_Affix_Stalwart");
                case ZombieModeEliteAffix.Regenerating:
                    return L10n.T("BossRush_ZombieMode_Affix_Regenerating");
                case ZombieModeEliteAffix.Burst:
                    return L10n.T("BossRush_ZombieMode_Affix_Burst");
                case ZombieModeEliteAffix.Plague:
                    return L10n.T("BossRush_ZombieMode_Affix_Plague");
                case ZombieModeEliteAffix.Commander:
                    return L10n.T("BossRush_ZombieMode_Affix_Commander");
                case ZombieModeEliteAffix.ToxicAura:
                    return L10n.T("BossRush_ZombieMode_Affix_ToxicAura");
                case ZombieModeEliteAffix.Splitting:
                    return L10n.T("BossRush_ZombieMode_Affix_Splitting");
                case ZombieModeEliteAffix.Shielded:
                    return L10n.T("BossRush_ZombieMode_Affix_Shielded");
                default:
                    return L10n.T("BossRush_ZombieMode_Affix_Adaptive");
            }
        }

    }
}
