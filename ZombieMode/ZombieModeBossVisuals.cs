using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    // 五种 Boss 的实例级外观。轮廓先于颜色：重肩王冠 / 双刃 / 裂晶脊 / 环盾 / 枝角。
    // 只挂在已进入 RunOnlyObjects 的 Boss 子树；不改角色、碰撞体、装备或共享 preset。
    // 装甲与发光接缝分别合批为一个网格，动画只转环与改属性块，生成后不再扫描 renderer。
    internal sealed class ZombieModeBossVisuals : MonoBehaviour
    {
        private ModBehaviour owner;
        private ZombieModeBossInstance instance;
        private ZombieModeEnemyRuntimeMarker marker;
        private Mesh armorMesh;
        private Mesh seamMesh;
        private Renderer seams;
        private Transform orbit;
        private LineRenderer orbitLine;
        private Transform pulseRing;
        private LineRenderer pulseLine;
        private ParticleSystem sparks;
        private Color accent;
        private float phase;
        private float pulse;
        private bool particlesPaused;
        private CharacterRandomPreset originalPreset;
        private CharacterRandomPreset displayPreset;

        internal static Color GetAccent(ZombieModeBossKind kind)
        {
            switch (kind)
            {
                case ZombieModeBossKind.Titan: return new Color(1f, 0.38f, 0.08f, 0.8f);
                case ZombieModeBossKind.Hunter: return new Color(1f, 0.12f, 0.24f, 0.8f);
                case ZombieModeBossKind.Splitter: return new Color(0.80f, 0.28f, 1f, 0.8f);
                case ZombieModeBossKind.Shielder: return new Color(0.12f, 0.80f, 1f, 0.8f);
                default: return new Color(0.62f, 1f, 0.12f, 0.8f);
            }
        }

        internal static void Attach(ModBehaviour owner, ZombieModeBossInstance instance)
        {
            if (instance == null || instance.Character == null || instance.Marker == null
                || instance.Marker.BossVisuals != null) return;
            GameObject root = new GameObject("ZombieMode_BossLook_" + instance.Kind);
            // 官方 Movement 只转 modelRoot；挂角色根节点会让背刃、枝角不随 Boss 转身。
            Transform modelRoot = instance.Character.modelRoot;
            root.transform.SetParent(modelRoot != null ? modelRoot : instance.Character.transform, false);
            ZombieModeBossVisuals look = root.AddComponent<ZombieModeBossVisuals>();
            instance.Marker.BossVisuals = look;
            try { look.Initialize(owner, instance); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ZombieMode] Boss appearance failed: " + instance.Kind + " " + e.Message);
                Destroy(root);
            }
        }

        private void Initialize(ModBehaviour newOwner, ZombieModeBossInstance newInstance)
        {
            owner = newOwner;
            instance = newInstance;
            marker = instance.Marker;
            accent = GetAccent(instance.Kind);
            // 官方血条读 preset.showName / DisplayName。只改实例副本，名字与图鉴用同一组本地化键。
            originalPreset = instance.Character.characterPreset;
            if (originalPreset != null)
            {
                displayPreset = Instantiate(originalPreset);
                displayPreset.nameKey = "BossRush_ZombieMode_Boss_" + instance.Kind;
                displayPreset.showName = true;
                instance.Character.characterPreset = displayPreset;
            }
            // 用真实角色 renderer 的局部包围盒定衣架；子树无额外碰撞，不放大命中盒。
            CharacterModel model = instance.Character.characterModel;
            Bounds bounds = new Bounds(new Vector3(0f, 0.65f, 0f), new Vector3(0.8f, 1.3f, 0.8f));
            bool found = false;
            if (model != null)
            {
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                    Bounds world = renderer.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = world.center + Vector3.Scale(world.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 point = transform.InverseTransformPoint(corner);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
            }
            float height = Mathf.Clamp(bounds.size.y, 0.6f, 3f);
            transform.localPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            transform.localScale = Vector3.one * height;

            var armor = new Geometry();
            var glow = new Geometry();
            BuildSilhouette(instance.Kind, armor, glow, accent);
            armorMesh = armor.CreateMesh("ZombieBossArmor");
            seamMesh = glow.CreateMesh("ZombieBossSeams");
            CreateSurface("Armor", armorMesh, BossRushFxBlend.Alpha);
            seams = CreateSurface("Seams", seamMesh, BossRushFxBlend.Additive);
            orbitLine = CreateRing("Crest", instance.Kind == ZombieModeBossKind.Shielder ? 6 : 36, 0.012f);
            orbit = orbitLine.transform;
            orbit.localPosition = new Vector3(0f, 0.72f, -0.06f);
            orbit.localScale = Vector3.one * (instance.Kind == ZombieModeBossKind.Shielder ? 0.65f : 0.48f);
            pulseLine = CreateRing("SkillPulse", 48, 0.026f);
            pulseRing = pulseLine.transform;
            pulseRing.localPosition = Vector3.up * 0.06f;
            pulseLine.enabled = false;
            CreateSparks();
            marker.VisualIdentityApplied = true;
            Pulse(marker);
        }

        internal static void Pulse(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.BossVisuals == null) return;
            ZombieModeBossVisuals look = marker.BossVisuals;
            look.pulse = 1f;
            if (look.sparks != null) look.sparks.Emit(8);
        }

        private void Update()
        {
            if (owner == null || marker == null || marker.RemovedFromRuntime || marker.DeathSettled
                || owner.ZombieModeCurrentRunId != marker.RunId || !owner.IsZombieModeActive
                || instance.Character == null || instance.Character.Health == null || instance.Character.Health.IsDead)
            {
                gameObject.SetActive(false);
                return;
            }
            bool paused = owner.IsZombieModeRuntimePaused();
            if (sparks != null && particlesPaused != paused)
            {
                if (paused) sparks.Pause(false); else sparks.Play(false);
                particlesPaused = paused;
            }
            if (paused) return;

            float delta = Time.unscaledDeltaTime;
            phase += delta;
            pulse = Mathf.Max(0f, pulse - delta * 1.8f);
            ZombieModeTitanState titan = instance.SkillState as ZombieModeTitanState;
            ZombieModeHunterState hunter = instance.SkillState as ZombieModeHunterState;
            bool defended = (titan != null && titan.DamageReductionActive)
                || (marker.AllyShield != null && marker.AllyShield.IsShieldActive());
            bool frenzy = hunter != null && hunter.FrenzyActive;
            orbit.localRotation = Quaternion.Euler(55f, phase * (frenzy ? 120f : 24f), 0f);
            Color light = accent * (defended || frenzy ? 1.8f : 1.05f + 0.12f * Mathf.Sin(phase * 2f));
            light.a = defended ? 0.9f : 0.55f;
            ZombieModeZoneVisuals.SetColor(seams, light);
            orbitLine.startColor = orbitLine.endColor = light;
            pulseLine.enabled = pulse > 0f;
            if (pulse > 0f)
            {
                pulseRing.localScale = Vector3.one * Mathf.Lerp(0.85f, 0.24f, pulse);
                Color pulseColor = accent * 1.6f;
                pulseColor.a = pulse * 0.7f;
                pulseLine.startColor = pulseLine.endColor = pulseColor;
            }
        }

        private Renderer CreateSurface(string name, Mesh mesh, BossRushFxBlend blend)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BossRushFxMaterials.Get(blend, Texture2D.whiteTexture);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private LineRenderer CreateRing(string name, int segments, float width)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(transform, false);
            LineRenderer line = part.AddComponent<LineRenderer>();
            line.sharedMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Additive, ZombieModeZoneVisuals.GetBandTexture());
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = accent;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
            return line;
        }

        private void CreateSparks()
        {
            sparks = BossRushFxKit.CreateEmitter("Embers", transform, Vector3.up * 0.65f,
                BossRushFxMaterials.Get(BossRushFxBlend.Additive), 20, false);
            if (sparks == null) return;
            var main = sparks.main;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.startLifetime = 0.85f;
            main.startSpeed = 0.22f;
            main.startSize = 0.025f;
            main.startColor = accent;
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.38f;
            var emission = sparks.emission;
            emission.rateOverTime = 12f;
            var lifetime = sparks.colorOverLifetime;
            lifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(accent, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0f, 1f) });
            lifetime.color = gradient;
            ParticleSystemRenderer renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sparks.Play(false);
        }

        private void OnDestroy()
        {
            if (marker != null && marker.BossVisuals == this) marker.BossVisuals = null;
            if (instance != null && instance.Character != null && instance.Character.characterPreset == displayPreset)
                instance.Character.characterPreset = originalPreset;
            if (displayPreset != null) Destroy(displayPreset);
            if (armorMesh != null) Destroy(armorMesh);
            if (seamMesh != null) Destroy(seamMesh);
        }

        private static void BuildSilhouette(ZombieModeBossKind kind, Geometry armor, Geometry glow, Color color)
        {
            // 数值为角色身高的比例；每种轮廓独立，不靠改整体体型或复制同一顶帽子区分。
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Shard(armor, glow, color, new Vector3(side * 0.32f, 0.61f, 0f), new Vector3(side * 0.40f, 0.95f, -0.06f), 0.22f);
                        Shard(armor, glow, color, new Vector3(side * 0.17f, 0.90f, 0f), new Vector3(side * 0.24f, 1.22f, 0f), 0.10f);
                    }
                    Shard(armor, glow, color, new Vector3(0f, 0.92f, 0f), new Vector3(0f, 1.31f, -0.06f), 0.12f);
                    Shard(armor, glow, color, new Vector3(0f, 0.40f, 0.22f), new Vector3(0f, 0.72f, 0.27f), 0.20f);
                    break;
                case ZombieModeBossKind.Hunter:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Shard(armor, glow, color, new Vector3(side * 0.23f, 0.57f, -0.12f), new Vector3(side * 0.63f, 1.04f, -0.45f), 0.12f);
                        Shard(armor, glow, color, new Vector3(side * 0.30f, 0.42f, 0.03f), new Vector3(side * 0.40f, 0.28f, 0.52f), 0.07f);
                    }
                    Shard(armor, glow, color, new Vector3(0f, 0.92f, 0f), new Vector3(0f, 1.16f, -0.33f), 0.09f);
                    break;
                case ZombieModeBossKind.Splitter:
                    for (int i = -2; i <= 2; i++)
                    {
                        float x = i * 0.15f;
                        Shard(armor, glow, color, new Vector3(x, 0.51f, -0.22f),
                            new Vector3(x * 1.8f, 1.22f - Mathf.Abs(i) * 0.12f, -0.32f), 0.12f);
                    }
                    Shard(armor, glow, color, new Vector3(-0.38f, 0.75f, 0.02f), new Vector3(-0.48f, 0.99f, 0.06f), 0.10f);
                    Shard(armor, glow, color, new Vector3(0.38f, 0.42f, 0.06f), new Vector3(0.49f, 0.72f, 0.10f), 0.12f);
                    break;
                case ZombieModeBossKind.Shielder:
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 3f;
                        Vector3 center = new Vector3(Mathf.Sin(angle) * 0.48f, 0.56f, Mathf.Cos(angle) * 0.48f);
                        Shard(armor, glow, color, center - Vector3.up * 0.23f, center + Vector3.up * 0.30f, 0.22f);
                    }
                    Shard(armor, glow, color, new Vector3(0f, 0.96f, -0.05f), new Vector3(0f, 1.12f, -0.05f), 0.18f);
                    break;
                case ZombieModeBossKind.Corruptor:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 fork = new Vector3(side * 0.34f, 1.13f, -0.16f);
                        Shard(armor, glow, color, new Vector3(side * 0.16f, 0.83f, -0.06f), fork, 0.09f);
                        Shard(armor, glow, color, fork, new Vector3(side * 0.48f, 1.37f, -0.27f), 0.07f);
                        Shard(armor, glow, color, fork, new Vector3(side * 0.19f, 1.32f, -0.17f), 0.055f);
                        Shard(armor, glow, color, new Vector3(side * 0.25f, 0.42f, -0.12f), new Vector3(side * 0.32f, 0.84f, -0.33f), 0.15f);
                    }
                    break;
            }
        }

        private static void Shard(Geometry armor, Geometry glow, Color accent, Vector3 from, Vector3 to, float width)
        {
            Vector3 axis = (to - from).normalized;
            Vector3 right = Vector3.Cross(axis, Vector3.forward).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 forward = Vector3.Cross(axis, right).normalized;
            Vector3 middle = Vector3.Lerp(from, to, 0.32f);
            Vector3[] rim = { middle + right * width, middle + forward * width * 0.65f,
                middle - right * width, middle - forward * width * 0.65f };
            for (int i = 0; i < 4; i++)
            {
                Color dark = Color.Lerp(new Color(0.045f, 0.05f, 0.07f, 1f), accent, 0.12f + i * 0.05f);
                dark.a = 1f;
                armor.Triangle(from, rim[i], rim[(i + 1) % 4], dark);
                armor.Triangle(to, rim[(i + 1) % 4], rim[i], dark);
                // 窄接缝嵌在两片甲面之间；白顶点色由运行时 MPB 一次性着色。
                Vector3 inset = Vector3.Lerp(rim[i], middle, 0.06f);
                glow.Triangle(to, rim[i], inset, Color.white);
                glow.Triangle(from, inset, rim[i], Color.white);
            }
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Color> colors = new List<Color>();
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                colors.Add(color); colors.Add(color); colors.Add(color);
            }
            internal Mesh CreateMesh(string name)
            {
                Mesh mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                int[] indices = new int[vertices.Count];
                Vector2[] uv = new Vector2[vertices.Count];
                for (int i = 0; i < indices.Length; i++) { indices[i] = i; uv[i] = new Vector2(0.5f, 0.5f); }
                mesh.triangles = indices;
                mesh.uv = uv;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
