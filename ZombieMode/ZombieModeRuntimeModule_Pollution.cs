using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly Dictionary<int, bool> zombieModeMeleeWeaponTypeCache = new Dictionary<int, bool>();
        private readonly HashSet<string> zombieModeVisualScaleDiagnostics = new HashSet<string>();
        private int zombieModeVisualUnsupportedLoggedRunId = int.MinValue;
        private readonly MaterialPropertyBlock zombieModeRendererColorBlock = new MaterialPropertyBlock();
        private const string ZombieModePlagueAuraCarrierName = "ZombieMode_PlagueFrostmourneAura";
        private const string ZombieModeFrostmourneAuraRootName = "Frostmourne_IceAura";
        private static readonly int ZombieModeRendererColorProperty = Shader.PropertyToID("_Color");
        private static readonly int ZombieModeRendererTintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int ZombieModeRendererBaseColorProperty = Shader.PropertyToID("_BaseColor");
        // 瘟疫光环去荧光（审美审查 VA-30）：病态的灰绿，不再是 (0.35,1,0.55) 的霓虹绿；alpha 由 colorOverLifetime 控制淡入淡出。
        private static readonly Color ZombieModePlagueAuraCoreColor = new Color(0.55f, 0.82f, 0.45f, 0.55f);
        private static readonly Color ZombieModePlagueAuraFadeColor = new Color(0.35f, 0.50f, 0.30f, 0.35f);

        internal void SetZombieModeRendererColor(Renderer renderer, Color color)
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
        // 改造：所有调用直接 new GameObject + AddComponent<MeshFilter+MeshRenderer> 用 sharedMesh / sharedMaterial。
        // 2026-09-23 审美审查（VA-27 / VA-28 / UC-03）：mesh 从 Cylinder 换成单面平面 quad（圆柱上下两个盖叠画，alpha 翻倍），
        // 材质从首选 Standard（URP 正式构建不画）换成共享半透明粒子材质 + 「内淡外亮」圆盘贴图，
        // 淡入 / 读条 / 淡出在 ZombieModeZoneVisuals.cs 的 ZombieModeZoneVisualFx 里。
        // ====================================================================
        private static Mesh s_zoneDiskMesh;
        private static Material s_zoneDiskMaterial;

        private static void EnsureZoneDiskAssets()
        {
            if (s_zoneDiskMesh == null)
            {
                s_zoneDiskMesh = ZombieModeZoneVisuals.GetDiskMesh();
            }

            if (s_zoneDiskMaterial == null || s_zoneDiskMaterial.mainTexture == null)
            {
                s_zoneDiskMaterial = ZombieModeZoneVisuals.GetDiskMaterial();
            }
        }

        /// <summary>
        /// 创建丧尸模式平面区域 visual（telegraph / 区域伤害指示）。
        /// 替换 GameObject.CreatePrimitive(Cylinder) + Destroy(collider) 模式。
        /// 调用方仍负责 RegisterZombieModeRunOnlyObject + AddComponent<TickRuntime>。
        /// </summary>
        internal GameObject CreateZombieModeFlatZoneVisual(string name, Vector3 origin, float radius, float height, Color color)
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
                ZombieModeZoneVisuals.Attach(go, mr, color);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] FlatZone visual 调色失败: " + e.Message);
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
                ModBehaviour.DevLog("[ZombieMode] 瘟疫丧尸霜之哀伤特效初始化失败: " + e.Message);
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

                // 贴身的瘟气（审美审查 VA-30）：发射体从 2.6×3 m 收到约 0.45×0.7 m，颗粒 0.25–0.45 m，
                // World 空间让丧尸走动时拖出一小段；上限 14 颗 / 只（旧 45 颗），总量更省。
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(ZombieModePlagueAuraCoreColor, ZombieModePlagueAuraFadeColor);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.maxParticles = 14;
                main.loop = true;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 6f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.45f;
                shape.scale = new Vector3(1f, 1.6f, 1f);

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.3f));

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
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.45f, 0.25f),
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
                light.range = 2.2f;
                light.intensity = 0.9f;
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
                // 共享软圆粒子材质（审美审查 VA-30）：旧兜底 Sprites/Default 没有贴图，粒子是硬边方块。共享材质不进 tracker 销毁。
                renderer.sharedMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Alpha);
            }
            // 发射参数随后由 ConfigureZombieModePlagueFrostmourneAura 的循环统一写（贴身、小颗粒、淡入淡出）。
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(ZombieModePlagueAuraCoreColor, ZombieModePlagueAuraFadeColor);
            main.maxParticles = 14;
            main.loop = true;
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

        private void ApplyZombieModeMutationVisualIdentity(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss || marker.VisualIdentityApplied ||
                (marker.EnemyKind != ZombieModeEnemyKind.Special && marker.EnemyKind != ZombieModeEnemyKind.Elite))
            {
                return;
            }

            marker.VisualIdentityApplied = true;
            float visualScale = 1f;
            Color targetColor = Color.white;
            bool hasTargetColor = false;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                switch (marker.SpecialKind)
                {
                    case ZombieModeSpecialKind.Sprinter:
                        visualScale = ZombieModeTuning.ZombieModeSprinterVisualScale;
                        targetColor = new Color(1f, 0.85f, 0.12f, 1f);
                        hasTargetColor = true;
                        break;
                    case ZombieModeSpecialKind.Exploder:
                    case ZombieModeSpecialKind.OfficialExploder:
                        visualScale = ZombieModeTuning.ZombieModeExploderVisualScale;
                        targetColor = new Color(1f, 0.30f, 0.08f, 1f);
                        hasTargetColor = true;
                        break;
                    case ZombieModeSpecialKind.Plague:
                        visualScale = ZombieModeTuning.ZombieModePlagueVisualScale;
                        targetColor = new Color(0.18f, 1f, 0.35f, 1f);
                        hasTargetColor = true;
                        break;
                    case ZombieModeSpecialKind.Summoner:
                        visualScale = ZombieModeTuning.ZombieModeSummonerVisualScale;
                        targetColor = new Color(0.75f, 0.30f, 1f, 1f);
                        hasTargetColor = true;
                        break;
                    case ZombieModeSpecialKind.Harasser:
                        visualScale = ZombieModeTuning.ZombieModeHarasserVisualScale;
                        targetColor = new Color(0.15f, 0.95f, 1f, 1f);
                        hasTargetColor = true;
                        break;
                }
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                int affixCount = marker.EliteAffixes == null ? 0 : marker.EliteAffixes.Count;
                if (affixCount == 1)
                    visualScale = ZombieModeTuning.ZombieModeEliteOneAffixVisualScale;
                else if (affixCount == 2)
                    visualScale = ZombieModeTuning.ZombieModeEliteTwoAffixVisualScale;
                else if (affixCount >= 3)
                    visualScale = ZombieModeTuning.ZombieModeEliteThreeAffixVisualScale;

                bool highThreat = marker.EliteAffixes != null &&
                                  (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Tough) ||
                                   marker.EliteAffixes.Contains(ZombieModeEliteAffix.Stalwart) ||
                                   marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded));
                if (highThreat)
                {
                    visualScale += ZombieModeTuning.ZombieModeHighThreatAffixVisualBonus;
                }
                targetColor = GetZombieModeEliteVisualColor(marker.EliteAffixes);
                hasTargetColor = true;
            }

            visualScale = Mathf.Clamp(visualScale, 1f, ZombieModeTuning.ZombieModeMaxVisualScale);
            marker.VisualFaceApplied = TryApplyZombieModeCustomFaceIdentity(enemy, marker, targetColor);
            Renderer[] renderers = null;
            try { renderers = enemy.GetComponentsInChildren<Renderer>(true); } catch { renderers = null; }
            HashSet<Transform> safeVisualRoots = new HashSet<Transform>();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (!IsZombieModeSafeVisualRenderer(enemy, renderer))
                    {
                        continue;
                    }

                    safeVisualRoots.Add(renderer.transform);
                    try
                    {
                        if (hasTargetColor)
                        {
                            Color current = Color.white;
                            Material shared = renderer.sharedMaterial;
                            if (shared != null && shared.HasProperty("_Color"))
                                current = shared.GetColor("_Color");
                            else if (shared != null && shared.HasProperty("_BaseColor"))
                                current = shared.GetColor("_BaseColor");
                            SetZombieModeRendererColor(renderer, Color.Lerp(current, targetColor, 0.65f));
                        }
                    }
                    catch (System.Exception e)
                    {
                        if (ModBehaviour.DevModeEnabled)
                            ModBehaviour.DevLog("[ZombieMode] 变异视觉节点应用失败: " + e.Message);
                    }
                }
            }

            int scaledRootCount = 0;
            foreach (Transform root in safeVisualRoots)
            {
                if (root == null || HasZombieModeSafeVisualAncestor(root, safeVisualRoots))
                {
                    continue;
                }

                try
                {
                    Vector3 originalScale = root.localScale;
                    marker.VisualScaleRecords.Add(new ZombieModeVisualScaleRecord
                    {
                        Target = root,
                        OriginalScale = originalScale
                    });
                    root.localScale = originalScale * visualScale;
                    scaledRootCount++;

                    if (ModBehaviour.DevModeEnabled)
                    {
                        string presetName = enemy.characterPreset != null
                            ? enemy.characterPreset.name
                            : enemy.gameObject.name;
                        string diagnostic = presetName + "::" + GetZombieModeVisualTransformPath(enemy.transform, root);
                        if (zombieModeVisualScaleDiagnostics.Add(diagnostic))
                        {
                            ModBehaviour.DevLog("[ZombieMode] safe visual scale node=" + diagnostic);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    if (ModBehaviour.DevModeEnabled)
                        ModBehaviour.DevLog("[ZombieMode] 变异视觉根缩放失败: " + e.Message);
                }
            }

            marker.VisualScaleApplied = scaledRootCount > 0 && visualScale > 1.0001f;
            if (!marker.VisualFaceApplied && !marker.VisualScaleApplied)
            {
                EnsureZombieModeFootMarkerFallback(enemy, marker, targetColor);
            }
        }

        private static Color GetZombieModeEliteVisualColor(List<ZombieModeEliteAffix> affixes)
        {
            if (affixes == null) return new Color(1f, 0.65f, 0.12f, 1f);

            if (affixes.Contains(ZombieModeEliteAffix.Commander) ||
                affixes.Contains(ZombieModeEliteAffix.Splitting))
            {
                return new Color(0.75f, 0.30f, 1f, 1f);
            }
            if (affixes.Contains(ZombieModeEliteAffix.Plague) ||
                affixes.Contains(ZombieModeEliteAffix.ToxicAura) ||
                affixes.Contains(ZombieModeEliteAffix.Regenerating))
            {
                return new Color(0.18f, 1f, 0.35f, 1f);
            }
            if (affixes.Contains(ZombieModeEliteAffix.Shielded) ||
                affixes.Contains(ZombieModeEliteAffix.Adaptive))
            {
                return new Color(0.15f, 0.95f, 1f, 1f);
            }
            if (affixes.Contains(ZombieModeEliteAffix.Burst))
            {
                return new Color(1f, 0.30f, 0.08f, 1f);
            }
            if (affixes.Contains(ZombieModeEliteAffix.Swift) ||
                affixes.Contains(ZombieModeEliteAffix.Frenzied))
            {
                return new Color(1f, 0.85f, 0.12f, 1f);
            }

            return new Color(1f, 0.65f, 0.12f, 1f);
        }

        private bool TryApplyZombieModeCustomFaceIdentity(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            Color targetColor)
        {
            try
            {
                return TryApplyZombieModeCustomFaceIdentityCore(enemy, marker, targetColor);
            }
            catch (System.Exception e)
            {
                LogZombieModeVisualUnsupportedOnce(enemy, marker, "CustomFace probe failed: " + e.Message);
                return false;
            }
        }

        private bool TryApplyZombieModeCustomFaceIdentityCore(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            Color targetColor)
        {
            CharacterModel characterModel = enemy != null ? enemy.characterModel : null;
            CustomFaceInstance face = characterModel != null ? characterModel.CustomFace : null;
            if (!IsZombieModeCustomFaceRefreshSafe(face))
            {
                LogZombieModeVisualUnsupportedOnce(
                    enemy,
                    marker,
                    "preset lacks a complete CustomFace; using safe scale/foot marker");
                return false;
            }

            CustomFaceHeadSetting originalHead = face.headSetting;
            CustomFacePartInfo originalEye = face.eyePart.partInfo;
            CustomFacePartInfo originalEyebrow = face.eyebrowPart.partInfo;
            CustomFacePartInfo originalMouth = face.mouthPart.partInfo;
            CustomFaceHeadSetting nextHead = originalHead;
            CustomFacePartInfo nextEye = originalEye;
            CustomFacePartInfo nextEyebrow = originalEyebrow;
            CustomFacePartInfo nextMouth = originalMouth;

            float headDelta = 0.06f;
            float foreheadDelta = 0.04f;
            float foreheadRoundFactor = 1.05f;
            float eyeScaleFactor = 1.15f;
            float eyebrowScaleFactor = 1.08f;
            float mouthScaleFactor = 1.08f;
            float eyeDistanceDelta = 6f;
            float eyebrowDistanceDelta = 4f;
            float mouthLeftRightDelta = 0f;

            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                switch (marker.SpecialKind)
                {
                    case ZombieModeSpecialKind.Sprinter:
                        headDelta = -0.10f;
                        foreheadDelta = -0.08f;
                        foreheadRoundFactor = 0.88f;
                        eyeScaleFactor = 1.30f;
                        eyeDistanceDelta = 10f;
                        eyebrowDistanceDelta = 8f;
                        break;
                    case ZombieModeSpecialKind.Exploder:
                    case ZombieModeSpecialKind.OfficialExploder:
                        headDelta = 0.12f;
                        foreheadDelta = 0.06f;
                        foreheadRoundFactor = 1.18f;
                        eyeScaleFactor = 0.90f;
                        eyebrowScaleFactor = 1.20f;
                        mouthScaleFactor = 1.30f;
                        eyebrowDistanceDelta = -8f;
                        break;
                    case ZombieModeSpecialKind.Plague:
                        headDelta = 0.04f;
                        foreheadDelta = 0.12f;
                        foreheadRoundFactor = 1.12f;
                        eyeScaleFactor = 1.18f;
                        mouthScaleFactor = 1.20f;
                        eyeDistanceDelta = 8f;
                        mouthLeftRightDelta = -8f;
                        break;
                    case ZombieModeSpecialKind.Summoner:
                        headDelta = 0.12f;
                        foreheadDelta = 0.08f;
                        foreheadRoundFactor = 1.10f;
                        eyeScaleFactor = 1.25f;
                        eyebrowScaleFactor = 1.22f;
                        eyeDistanceDelta = 12f;
                        eyebrowDistanceDelta = 10f;
                        break;
                    case ZombieModeSpecialKind.Harasser:
                        headDelta = 0.05f;
                        foreheadDelta = 0.03f;
                        foreheadRoundFactor = 0.95f;
                        eyeScaleFactor = 1.35f;
                        eyebrowScaleFactor = 1.18f;
                        eyeDistanceDelta = 12f;
                        eyebrowDistanceDelta = -10f;
                        break;
                }
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                headDelta = marker.EliteAffixes.Count >= 3 ? 0.12f : 0.08f;
                foreheadDelta = marker.EliteAffixes.Contains(ZombieModeEliteAffix.Tough) ? 0.10f : 0.05f;
                foreheadRoundFactor = marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded) ? 1.18f : 1.08f;
                eyeScaleFactor = marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive) ? 1.35f : 1.20f;
                eyebrowScaleFactor = 1.18f;
                eyeDistanceDelta = marker.EliteAffixes.Contains(ZombieModeEliteAffix.Swift) ? 12f : 8f;
                eyebrowDistanceDelta = 8f;
            }

            nextHead.headScaleOffset = Mathf.Clamp(
                originalHead.headScaleOffset + Mathf.Clamp(headDelta, -0.12f, 0.12f), -0.4f, 0.4f);
            nextHead.foreheadHeight = Mathf.Clamp(
                originalHead.foreheadHeight + Mathf.Clamp(foreheadDelta, -0.12f, 0.12f), 0f, 4f);
            nextHead.foreheadRound = Mathf.Clamp(
                originalHead.foreheadRound * Mathf.Clamp(foreheadRoundFactor, 0.85f, 1.20f), 0.4f, 4f);
            nextHead.mainColor = Color.Lerp(originalHead.mainColor, targetColor, 0.65f);

            ApplyZombieModeFacePartProfile(ref nextEye, originalEye, eyeScaleFactor, eyeDistanceDelta, 0f, targetColor);
            ApplyZombieModeFacePartProfile(ref nextEyebrow, originalEyebrow, eyebrowScaleFactor, eyebrowDistanceDelta, 0f, targetColor);
            ApplyZombieModeFacePartProfile(ref nextMouth, originalMouth, mouthScaleFactor, 0f, mouthLeftRightDelta, targetColor);

            try
            {
                face.headSetting = nextHead;
                face.eyePart.partInfo = nextEye;
                face.eyebrowPart.partInfo = nextEyebrow;
                face.mouthPart.partInfo = nextMouth;
                face.RefreshAll();
                return true;
            }
            catch (System.Exception e)
            {
                bool restored = RestoreZombieModeCustomFaceNoThrow(
                    face,
                    originalHead,
                    originalEye,
                    originalEyebrow,
                    originalMouth);
                LogZombieModeVisualUnsupportedOnce(
                    enemy,
                    marker,
                    "CustomFace refresh failed; rollback=" + restored + ": " + e.Message);
                return false;
            }
        }

        private static bool RestoreZombieModeCustomFaceNoThrow(
            CustomFaceInstance face,
            CustomFaceHeadSetting head,
            CustomFacePartInfo eye,
            CustomFacePartInfo eyebrow,
            CustomFacePartInfo mouth)
        {
            if (face == null) return false;
            bool restored = true;
            try { face.headSetting = head; } catch { restored = false; }
            try { if (face.eyePart != null) face.eyePart.partInfo = eye; else restored = false; }
            catch { restored = false; }
            try { if (face.eyebrowPart != null) face.eyebrowPart.partInfo = eyebrow; else restored = false; }
            catch { restored = false; }
            try { if (face.mouthPart != null) face.mouthPart.partInfo = mouth; else restored = false; }
            catch { restored = false; }
            try { face.RefreshAll(); } catch { restored = false; }
            return restored;
        }

        private void LogZombieModeVisualUnsupportedOnce(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            string reason)
        {
            if (!ModBehaviour.DevModeEnabled || marker == null || marker.RunId <= 0 ||
                zombieModeVisualUnsupportedLoggedRunId == marker.RunId)
            {
                return;
            }

            zombieModeVisualUnsupportedLoggedRunId = marker.RunId;
            string presetName = enemy != null && enemy.characterPreset != null
                ? enemy.characterPreset.name
                : (enemy != null && enemy.gameObject != null ? enemy.gameObject.name : "<unknown>");
            ModBehaviour.DevLog("[ZombieMode] mutant visual unsupported: run=" + marker.RunId +
                ", preset=" + presetName + ", reason=" + reason);
        }

        private static void ApplyZombieModeFacePartProfile(
            ref CustomFacePartInfo next,
            CustomFacePartInfo original,
            float scaleFactor,
            float distanceAngleDelta,
            float leftRightAngleDelta,
            Color targetColor)
        {
            next.scale = original.scale * Mathf.Clamp(scaleFactor, 0.75f, 1.35f);
            next.distanceAngle = Mathf.Clamp(
                original.distanceAngle + Mathf.Clamp(distanceAngleDelta, -12f, 12f), 0f, 90f);
            next.leftRightAngle = Mathf.Clamp(
                original.leftRightAngle + Mathf.Clamp(leftRightAngleDelta, -12f, 12f), -90f, 90f);
            next.color = Color.Lerp(original.color, targetColor, 0.65f);
        }

        private static bool IsZombieModeCustomFaceRefreshSafe(CustomFaceInstance face)
        {
            if (face == null || face.mainRenderers == null || face.headJoint == null || face.foreheadJoint == null ||
                face.hairPart == null || face.eyePart == null || face.eyebrowPart == null || face.mouthPart == null ||
                face.tailPart == null || face.footLPart == null || face.footRPart == null ||
                face.wingLPart == null || face.wingRPart == null)
            {
                return false;
            }

            if (face.eyePart.PartInstance == null || face.eyebrowPart.PartInstance == null || face.mouthPart.PartInstance == null)
            {
                return false;
            }

            CustomFacePartUtility[] utilities =
            {
                face.hairPart, face.eyePart, face.eyebrowPart, face.mouthPart, face.tailPart,
                face.footLPart, face.footRPart, face.wingLPart, face.wingRPart
            };
            for (int i = 0; i < utilities.Length; i++)
            {
                CustomFacePart part = utilities[i].PartInstance;
                if (part != null && part.customColorRenderers == null)
                {
                    return false;
                }
                if (part != null)
                {
                    for (int rendererIndex = 0; rendererIndex < part.customColorRenderers.Count; rendererIndex++)
                    {
                        if (part.customColorRenderers[rendererIndex] == null)
                        {
                            return false;
                        }
                    }
                }
            }
            for (int i = 0; i < face.mainRenderers.Length; i++)
            {
                if (face.mainRenderers[i] == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsZombieModeSafeVisualRenderer(
            CharacterMainControl enemy,
            Renderer renderer)
        {
            if (enemy == null || renderer == null || renderer.transform == null ||
                (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
            {
                return false;
            }

            Transform visualRoot = renderer.transform;
            Transform characterModelRoot = enemy.characterModel != null
                ? enemy.characterModel.transform
                : null;
            if (visualRoot == enemy.transform || visualRoot == characterModelRoot ||
                !visualRoot.IsChildOf(enemy.transform) ||
                HasZombieModeUnsafeVisualAncestor(enemy.transform, visualRoot))
            {
                return false;
            }

            try
            {
                if (visualRoot.GetComponentInChildren<Collider>(true) != null ||
                    visualRoot.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) != null ||
                    visualRoot.GetComponentInChildren<CharacterMainControl>(true) != null ||
                    visualRoot.GetComponentInChildren<CharacterModel>(true) != null ||
                    visualRoot.GetComponentInChildren<Item>(true) != null)
                {
                    return false;
                }

                Transform[] descendants = visualRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < descendants.Length; i++)
                {
                    string nodeName = descendants[i] != null && descendants[i].name != null
                        ? descendants[i].name.ToLowerInvariant()
                        : string.Empty;
                    if (nodeName.Contains("socket") || nodeName.Contains("collider") ||
                        nodeName.Contains("attack") || nodeName.Contains("weapon") ||
                        nodeName.Contains("handheld") || nodeName.Contains("muzzle") ||
                        nodeName.Contains("navmesh") || nodeName.Contains("hitbox") ||
                        nodeName.Contains("hurtbox"))
                    {
                        return false;
                    }
                }

                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    if (skinned.rootBone == null ||
                        (skinned.rootBone != visualRoot && !skinned.rootBone.IsChildOf(visualRoot)))
                    {
                        return false;
                    }
                    Transform[] bones = skinned.bones;
                    if (bones == null || bones.Length <= 0) return false;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform bone = bones[i];
                        if (bone == null || (bone != visualRoot && !bone.IsChildOf(visualRoot)))
                        {
                            return false;
                        }
                    }
                }
            }
            catch { return false; }
            return true;
        }

        private static bool HasZombieModeUnsafeVisualAncestor(
            Transform enemyRoot,
            Transform visualRoot)
        {
            Transform current = visualRoot;
            while (current != null && current != enemyRoot)
            {
                try
                {
                    if (current.GetComponent<Item>() != null)
                    {
                        return true;
                    }
                }
                catch { return true; }

                string nodeName = current.name != null
                    ? current.name.ToLowerInvariant()
                    : string.Empty;
                if (nodeName.Contains("socket") || nodeName.Contains("collider") ||
                    nodeName.Contains("attack") || nodeName.Contains("weapon") ||
                    nodeName.Contains("handheld") || nodeName.Contains("muzzle") ||
                    nodeName.Contains("navmesh") || nodeName.Contains("hitbox") ||
                    nodeName.Contains("hurtbox"))
                {
                    return true;
                }

                current = current.parent;
            }

            return current != enemyRoot;
        }

        private static bool HasZombieModeSafeVisualAncestor(
            Transform candidate,
            HashSet<Transform> safeVisualRoots)
        {
            if (candidate == null || safeVisualRoots == null) return false;
            Transform parent = candidate.parent;
            while (parent != null)
            {
                if (safeVisualRoots.Contains(parent)) return true;
                parent = parent.parent;
            }
            return false;
        }

        private static string GetZombieModeVisualTransformPath(Transform root, Transform target)
        {
            if (target == null) return "<null>";
            List<string> names = new List<string>();
            Transform current = target;
            while (current != null)
            {
                names.Add(current.name ?? "<unnamed>");
                if (current == root) break;
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private void EnsureZombieModeFootMarkerFallback(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            Color targetColor)
        {
            if (enemy == null || marker == null || marker.VisualFootMarkerFallbackApplied || enemy.transform == null)
            {
                return;
            }

            GameObject footMarker = null;
            try
            {
                EnsureZoneDiskAssets();
                if (s_zoneDiskMesh == null || s_zoneDiskMaterial == null)
                {
                    throw new System.InvalidOperationException("shared foot marker assets unavailable");
                }
                footMarker = ZombieModeFootMarkerPool.Acquire(
                    enemy.transform,
                    s_zoneDiskMesh,
                    s_zoneDiskMaterial);
                if (footMarker == null)
                {
                    throw new System.InvalidOperationException("foot marker pool returned null");
                }
                MeshRenderer meshRenderer = footMarker.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                {
                    throw new System.InvalidOperationException("pooled foot marker renderer missing");
                }
                ZombieModeZoneVisuals.SetColor(meshRenderer, Color.Lerp(Color.white, targetColor, 0.65f));
                marker.VisualFootMarker = footMarker;
                marker.VisualFootMarkerFallbackApplied = true;
            }
            catch (System.Exception e)
            {
                if (footMarker != null) ZombieModeFootMarkerPool.Release(footMarker);
                marker.VisualFootMarker = null;
                marker.VisualFootMarkerFallbackApplied = false;
                LogZombieModeVisualUnsupportedOnce(enemy, marker, "foot marker fallback failed: " + e.Message);
            }
        }

    }
}
