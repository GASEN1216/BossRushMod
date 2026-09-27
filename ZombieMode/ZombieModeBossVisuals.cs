using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    // 五种 Boss 的实例级外观。轮廓先于颜色：重肩王冠 / 双刃 / 裂晶脊 / 环盾 / 枝角。
    // 俯视镜头下最先读到的是脚下：每种 Boss 各有一枚纹章（王冠齿环 / 前指双 V / 五细胞 / 双六角 / 蚀环滴痕）。
    // 只挂在已进入 RunOnlyObjects 的 Boss 子树；不改角色、碰撞体、装备或共享 preset。
    // 装甲与发光芯分别合批为一个网格，动画只转环与改属性块，生成后不再扫描 renderer。
    // 尺寸一律按世界米数给：LineRenderer 线宽不随 Transform 缩放，粒子用 Local 缩放模式也不继承父级；
    // 低于 0.06 m 的线在游戏镜头下只剩 1–2 px（PhantomWitchFxRenderUtil.MinWorldLineWidth，审查 VB-08）。
    internal sealed class ZombieModeBossVisuals : MonoBehaviour
    {
        private const float CrestLineWidth = 0.06f;
        private const float SigilDiameter = 1.3f;       // 角色身高的倍数
        private const float DashTrailSpeed = 14f;       // m/s；追猎平时追击远低于此，冲刺约 80 m/s
        private const float TeleportJump = 9f;          // 单帧位移超过它按解卡传送处理，不画拖尾
        // Boss 尸体保留到最长的残留地面区结束：官方默认死后 0.5 s 销毁，腐蚀区 / 毒径 / 死亡毒云的
        // source 随之变空，伤害回退成玩家来源，玩家死在里面时死因显示「自己」（CR-2026-09-27-106）。
        private static readonly float CorpseKeepSeconds = Mathf.Max(ZombieModeTuning.CorruptorZoneDurationSeconds,
            Mathf.Max(ZombieModeTuning.CorruptorPoisonPathDurationSeconds, ZombieModeTuning.CorruptorDeathCloudDurationSeconds)) + 1f;
        private static readonly Texture2D[] SigilTextures = new Texture2D[5];

        private ModBehaviour owner;
        private ZombieModeBossInstance instance;
        private ZombieModeEnemyRuntimeMarker marker;
        private Mesh armorMesh;
        private Mesh seamMesh;
        private Renderer seams;
        private Renderer sigil;
        private Transform sigilTransform;
        private Renderer pulseDisk;
        private Transform pulseTransform;
        private Transform orbit;
        private LineRenderer orbitLine;
        private ParticleSystem sparks;
        private TrailRenderer dashTrail;
        private Color accent;
        private Color dustColor;
        private float worldHeight = 1.6f;
        private float phase;
        private float pulse;
        private bool particlesPaused;
        private bool frenzyEmbers;
        private Vector3 lastPosition;
        private bool hasLastPosition;
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
            dustColor = new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.55f);
            // 官方血条读 preset.showName / DisplayName / 图标。只改实例副本，名字与图鉴用同一组本地化键；
            // 致死一击前由 RestoreOfficialPreset 换回，官方击杀计数与任务仍记在原 preset 名下。
            originalPreset = instance.Character.characterPreset;
            if (originalPreset != null)
            {
                displayPreset = Instantiate(originalPreset);
                displayPreset.nameKey = "BossRush_ZombieMode_Boss_" + instance.Kind;
                displayPreset.showName = true;
                // 对齐龙裔 / 龙皇 / 幽灵女巫：名字左侧挂官方 Boss 图标，不然看起来和普通丧尸同级。
                if (BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                {
                    try
                    {
                        BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(
                            displayPreset, CharacterIconTypes.boss);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[ZombieMode] Boss health-bar icon failed: " + e.Message);
                    }
                }
                instance.Character.characterPreset = displayPreset;
            }
            KeepCorpseForResidualZones(instance.Character);
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
            worldHeight = Mathf.Max(0.3f, transform.lossyScale.y);

            var armor = new Geometry();
            var glow = new Geometry();
            BuildSilhouette(instance.Kind, armor, glow, accent);
            armorMesh = armor.CreateMesh("ZombieBossArmor");
            seamMesh = glow.CreateMesh("ZombieBossSeams");
            // 发光芯先画、暗甲后画：甲面不写深度，后画才能盖住藏在甲里的那段芯，只留甲尖外的亮刃。
            seams = CreateSurface("Seams", seamMesh, BossRushFxMaterials.Get(BossRushFxBlend.Additive, Texture2D.whiteTexture), 0);
            CreateSurface("Armor", armorMesh, BossRushFxMaterials.Get(BossRushFxBlend.Alpha, Texture2D.whiteTexture), 1);

            // 脚下纹章排在预警圈之前画（sortingOrder -1），危险区永远压在上面。
            sigil = CreateSurface("Sigil", ZombieModeZoneVisuals.GetDiskMesh(),
                BossRushFxMaterials.Get(BossRushFxBlend.Additive, GetSigilTexture(instance.Kind)), -1);
            sigilTransform = sigil.transform;
            sigilTransform.localPosition = Vector3.up * 0.02f;
            sigilTransform.localScale = Vector3.one * SigilDiameter;
            pulseDisk = CreateSurface("SkillPulse", ZombieModeZoneVisuals.GetDiskMesh(),
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Ring, BossRushFxBlend.Additive, BossRushFxKit.GainBright), -1);
            pulseTransform = pulseDisk.transform;
            pulseTransform.localPosition = Vector3.up * 0.03f;
            pulseDisk.enabled = false;

            orbitLine = CreateRing("Crest", instance.Kind == ZombieModeBossKind.Shielder ? 6 : 36, CrestLineWidth);
            orbit = orbitLine.transform;
            orbit.localPosition = new Vector3(0f, 0.72f, -0.06f);
            orbit.localScale = Vector3.one * (instance.Kind == ZombieModeBossKind.Shielder ? 0.65f : 0.48f);
            CreateSparks();
            if (instance.Kind == ZombieModeBossKind.Hunter) CreateDashTrail();
            marker.VisualIdentityApplied = true;
            Pulse(marker);
        }

        internal static void Pulse(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.BossVisuals == null) return;
            ZombieModeBossVisuals look = marker.BossVisuals;
            look.pulse = 1f;
            if (look.sparks != null) look.sparks.Emit(8);
            // 技能起手：胸口一簇亮芯火花，脚下一道冲击环（Update 推进）。一次性粒子播完自毁。
            BossRushFxBurst burst = BossRushFxKit.Sparks(look.accent, 16);
            burst.Upward = true;
            BossRushFxKit.PlayBurst(look.transform.position + Vector3.up * look.worldHeight * 0.55f, burst);
        }

        /// <summary>
        /// 致死一击扣血前调用（ZombieModeDamageRuntime）：官方 CharacterMainControl.OnDead 按
        /// characterPreset.nameKey 写击杀计数与击杀任务进度，那一刻必须是原 preset，不能是显示用副本。
        /// </summary>
        internal static void RestoreOfficialPreset(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeBossVisuals look = marker != null ? marker.BossVisuals : null;
            if (look == null || look.instance == null || look.instance.Character == null) return;
            if (look.originalPreset != null && look.instance.Character.characterPreset == look.displayPreset)
                look.instance.Character.characterPreset = look.originalPreset;
        }

        /// <summary>
        /// Boss 死亡结算时调用（静态 Health.OnDead，晚于写击杀计数的实例 OnDeadEvent）：
        /// 同色火花冲天 + 贴地烟环 + 一道扩散冲击环，只是表现、不带判定。
        /// 击杀计数已记在原 preset 名下，这里再把显示副本挂回尸体：残留地面区以尸体为伤害来源，
        /// 官方结算页的死因读 fromCharacter.characterPreset.DisplayName，显示的就是这只 Boss 的名字。
        /// </summary>
        internal static void PlayDeath(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeBossVisuals look = marker != null ? marker.BossVisuals : null;
            if (look == null) return;
            if (look.displayPreset != null && look.instance != null && look.instance.Character != null)
                look.instance.Character.characterPreset = look.displayPreset;
            Vector3 feet = look.transform.position;
            float h = look.worldHeight;

            BossRushFxBurst sparks = BossRushFxKit.Sparks(look.accent, 36);
            sparks.Upward = true;
            sparks.SpeedMin = 5f;
            sparks.SpeedMax = 11f;
            sparks.SizeMin = 0.08f;
            sparks.SizeMax = 0.16f;
            sparks.LifeMin = 0.35f;
            sparks.LifeMax = 0.7f;
            sparks.Gravity = 0.6f;
            sparks.Trail = 0.25f;
            BossRushFxKit.PlayBurst(feet + Vector3.up * h * 0.5f, sparks);

            BossRushFxKit.PlayBurst(feet + Vector3.up * 0.1f, BossRushFxKit.Dust(look.dustColor, 10));

            BossRushFxBurst ring = new BossRushFxBurst();
            ring.Shape = BossRushParticleShape.Ring;
            ring.Blend = BossRushFxBlend.Additive;
            ring.Gain = BossRushFxKit.GainBright;
            ring.Count = 1;
            ring.SizeMin = ring.SizeMax = h * 1.2f;
            ring.LifeMin = ring.LifeMax = 0.5f;
            ring.GrowTo = 4.5f;
            ring.ShapeRadius = 0.01f;
            ring.FlatOnGround = true;
            ring.Core = new Color(1f, 1f, 1f, 1f);
            ring.Main = new Color(look.accent.r, look.accent.g, look.accent.b, 0.85f);
            ring.End = new Color(look.accent.r, look.accent.g, look.accent.b, 0f);
            BossRushFxKit.PlayBurst(feet + Vector3.up * 0.05f, ring);
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

            // 追猎的双 V 永远指向它的朝向；其余纹章慢转，护体 / 开盾 / 狂暴时转快并提亮。
            float spin = instance.Kind == ZombieModeBossKind.Hunter ? 0f : phase * (defended ? 60f : 16f);
            sigilTransform.localRotation = Quaternion.Euler(0f, spin, 0f);
            Color mark = accent * (defended || frenzy ? 1.9f : 1.15f + 0.25f * Mathf.Sin(phase * 2.2f));
            mark.a = defended || frenzy ? 0.95f : 0.7f;
            ZombieModeZoneVisuals.SetColor(sigil, mark);

            pulseDisk.enabled = pulse > 0f;
            if (pulse > 0f)
            {
                pulseTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, 2.8f, 1f - pulse);
                Color pulseColor = accent * 2f;
                pulseColor.a = pulse * 0.9f;
                ZombieModeZoneVisuals.SetColor(pulseDisk, pulseColor);
            }

            if (sparks != null && frenzyEmbers != frenzy)
            {
                ParticleSystem.EmissionModule emission = sparks.emission;
                emission.rateOverTime = frenzy ? 30f : 14f;
                frenzyEmbers = frenzy;
            }

            if (dashTrail != null)
            {
                Vector3 position = transform.position;
                float moved = hasLastPosition ? (position - lastPosition).magnitude : 0f;
                if (moved > TeleportJump) dashTrail.Clear();
                dashTrail.emitting = moved <= TeleportJump && moved > DashTrailSpeed * delta;
                lastPosition = position;
                hasLastPosition = true;
            }
        }

        private static void KeepCorpseForResidualZones(CharacterMainControl character)
        {
            System.Reflection.FieldInfo field = BossRushEagerReflectionCache.Health_DeadDestroyDelay;
            if (field == null || character == null || character.Health == null) return;
            try
            {
                float current = (float)field.GetValue(character.Health);
                if (current < CorpseKeepSeconds) field.SetValue(character.Health, CorpseKeepSeconds);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ZombieMode] Boss corpse lifetime failed: " + e.Message);
            }
        }

        private Renderer CreateSurface(string name, Mesh mesh, Material material, int sortingOrder)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = material != null;
            renderer.sortingOrder = sortingOrder;
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
            // 余烬从脚下一圈升起，绕身体飘散。大小、半径、速度都是世界米数（Local 缩放模式不继承父级）。
            sparks = BossRushFxKit.CreateEmitter("Embers", transform, Vector3.up * 0.04f,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot),
                24, false);
            if (sparks == null) return;
            float size = Mathf.Clamp(worldHeight / 1.6f, 0.8f, 1.6f);
            var main = sparks.main;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f * size, 0.13f * size);
            main.startColor = Color.white;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radius = 0.42f * worldHeight;
            shape.radiusThickness = 0.35f;
            var velocity = sparks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f * worldHeight, 0.9f * worldHeight);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var emission = sparks.emission;
            emission.rateOverTime = 14f;
            var shrink = sparks.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
            var lifetime = sparks.colorOverLifetime;
            lifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(accent, 0.35f), new GradientColorKey(accent, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
            lifetime.color = gradient;
            sparks.Play(false);
        }

        private void CreateDashTrail()
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (material == null) return;
            GameObject part = new GameObject("DashTrail");
            part.transform.SetParent(transform, false);
            part.transform.localPosition = Vector3.up * 0.5f;
            dashTrail = part.AddComponent<TrailRenderer>();
            dashTrail.sharedMaterial = material;
            dashTrail.time = 0.3f;
            dashTrail.minVertexDistance = 0.15f;
            dashTrail.widthMultiplier = 0.6f * worldHeight;
            dashTrail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            dashTrail.textureMode = LineTextureMode.Stretch;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(accent, 0.3f), new GradientColorKey(accent, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            dashTrail.colorGradient = gradient;
            dashTrail.shadowCastingMode = ShadowCastingMode.Off;
            dashTrail.receiveShadows = false;
            dashTrail.emitting = false;
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

        internal static void ResetStaticCaches()
        {
            for (int i = 0; i < SigilTextures.Length; i++)
            {
                if (SigilTextures[i] != null) Destroy(SigilTextures[i]);
                SigilTextures[i] = null;
            }
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
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 0.0001f) return;
            axis /= length;
            Vector3 right = Vector3.Cross(axis, Vector3.forward);
            if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            Vector3 forward = Vector3.Cross(axis, right).normalized;
            Vector3 middle = from + axis * length * 0.32f;
            Vector3[] rim = { middle + right * width, middle + forward * width * 0.65f,
                middle - right * width, middle - forward * width * 0.65f };
            Color baseDark = new Color(0.045f, 0.05f, 0.07f, 1f);
            for (int i = 0; i < 4; i++)
            {
                // 粒子着色器不打光：逐面给不同的暗色，上半截偏主色，靠色阶读出棱面。
                Color lower = Color.Lerp(baseDark, accent, 0.10f + i * 0.04f);
                Color upper = Color.Lerp(baseDark, accent, 0.22f + i * 0.05f);
                lower.a = upper.a = 1f;
                armor.Triangle(from, rim[i], rim[(i + 1) % 4], lower, lower, lower);
                armor.Triangle(to, rim[(i + 1) % 4], rim[i], upper, upper, upper);
            }
            // 发光芯从甲片中段伸出甲尖约 16%：甲内那段被后画的暗甲盖住，露出的是一截发亮的刃尖。
            // 白顶点色由运行时 MPB 着主色；尖端满亮、根部半透明。
            Vector3 coreBase = from + axis * length * 0.45f;
            Vector3 coreTip = to + axis * length * 0.16f;
            float coreWidth = width * 0.42f;
            Vector3[] core = { coreBase + right * coreWidth, coreBase + forward * coreWidth * 0.65f,
                coreBase - right * coreWidth, coreBase - forward * coreWidth * 0.65f };
            Color tip = Color.white;
            Color root = new Color(1f, 1f, 1f, 0.35f);
            for (int i = 0; i < 4; i++)
                glow.Triangle(coreTip, core[i], core[(i + 1) % 4], tip, root, root);
        }

        // ------------------------------------------------------------------
        // 脚下纹章：128 px 白色 alpha 贴图，五种各画一张、全局只画一次。
        // 贴图 +V = 角色前方（地面 quad 的 +Z），角度 0° 指向前。
        // ------------------------------------------------------------------
        private static Texture2D GetSigilTexture(ZombieModeBossKind kind)
        {
            int index = Mathf.Clamp((int)kind, 0, SigilTextures.Length - 1);
            if (SigilTextures[index] != null) return SigilTextures[index];
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.name = "ZombieMode_BossSigil_" + kind;
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f) / size * 2f - 1f;
                    float py = (y + 0.5f) / size * 2f - 1f;
                    float alpha = SigilAlpha(kind, new Vector2(px, py));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            SigilTextures[index] = texture;
            return texture;
        }

        private static float SigilAlpha(ZombieModeBossKind kind, Vector2 p)
        {
            float r = p.magnitude;
            if (r >= 1f) return 0f;
            float theta = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;
            float a = 0f;
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    // 粗外环 + 细内环，三枚向心的王冠齿，六颗铆钉。
                    a = Mathf.Max(Band(r - 0.86f, 0.05f), Band(r - 0.70f, 0.016f));
                    for (int k = 0; k < 3; k++)
                    {
                        float arc = Mathf.Abs(Mathf.DeltaAngle(theta, k * 120f)) * Mathf.Deg2Rad * r;
                        float half = 0.20f * Mathf.Clamp01((r - 0.42f) / 0.40f);
                        if (r > 0.42f && r < 0.84f) a = Mathf.Max(a, Band(arc, half));
                    }
                    for (int k = 0; k < 6; k++) a = Mathf.Max(a, Band(Distance(p, Polar(k * 60f + 30f, 0.78f)), 0.035f));
                    if (r < 0.86f) a = Mathf.Max(a, 0.10f);
                    break;
                case ZombieModeBossKind.Hunter:
                    // 身后开口的外环 + 两道前指的 V，读作「冲锋方向」。
                    if (Mathf.Abs(Mathf.DeltaAngle(theta, 180f)) > 40f) a = Band(r - 0.90f, 0.02f);
                    a = Mathf.Max(a, Band(r - 0.30f, 0.012f));
                    for (int k = 0; k < 2; k++)
                    {
                        Vector2 tipPoint = new Vector2(0f, k == 0 ? 0.80f : 0.44f);
                        Vector2 back = tipPoint - new Vector2(0f, 0.34f);
                        a = Mathf.Max(a, Band(Segment(p, tipPoint, back + Vector2.left * 0.42f), 0.045f));
                        a = Mathf.Max(a, Band(Segment(p, tipPoint, back + Vector2.right * 0.42f), 0.045f));
                    }
                    if (r < 0.90f) a = Mathf.Max(a, 0.06f);
                    break;
                case ZombieModeBossKind.Splitter:
                    // 五枚细胞环各带核，细辐条连到内环。
                    a = Mathf.Max(Band(r - 0.92f, 0.014f), Band(r - 0.30f, 0.02f));
                    for (int k = 0; k < 5; k++)
                    {
                        Vector2 cell = Polar(k * 72f, 0.60f);
                        float d = Distance(p, cell);
                        a = Mathf.Max(a, Band(d - 0.19f, 0.028f));
                        a = Mathf.Max(a, 0.30f * Band(d, 0.11f));
                        a = Mathf.Max(a, Band(Segment(p, cell * 0.50f, cell * 0.68f), 0.015f));
                    }
                    break;
                case ZombieModeBossKind.Shielder:
                    // 外六角粗边 + 错开 30° 的内六角，中间是一圈盾板，六个角各一颗亮点。
                    float outer = Hex(p, 0f);
                    float inner = Hex(p, 30f);
                    a = Mathf.Max(Band(outer - 0.84f, 0.045f), Band(inner - 0.56f, 0.018f));
                    if (outer < 0.84f && inner > 0.56f) a = Mathf.Max(a, 0.16f);
                    for (int k = 0; k < 6; k++) a = Mathf.Max(a, Band(Distance(p, Polar(k * 60f + 30f, 0.84f / 0.866f)), 0.045f));
                    break;
                default:
                    // 起伏的蚀环，往外淌七道滴痕，环内散着孢子斑。
                    float waveRad = theta * Mathf.Deg2Rad;
                    float edge = 0.78f + 0.07f * Mathf.Sin(5f * waveRad) + 0.035f * Mathf.Sin(13f * waveRad + 1.3f);
                    a = Band(r - edge, 0.04f);
                    for (int k = 0; k < 7; k++)
                    {
                        float arc = Mathf.Abs(Mathf.DeltaAngle(theta, k * 51.4f + 20f)) * Mathf.Deg2Rad * r;
                        float run = (r - edge) / 0.16f;
                        if (run > 0f && run < 1f) a = Mathf.Max(a, Band(arc, 0.035f * (1f - run)));
                    }
                    float spores = Mathf.Sin(p.x * 9.1f + 1.7f) * Mathf.Sin(p.y * 8.3f + 0.4f) + 0.5f * Mathf.Sin((p.x + p.y) * 13.7f);
                    if (r < edge - 0.08f) a = Mathf.Max(a, Mathf.Max(0.08f, 0.55f * Step(0.9f, 1.05f, spores)));
                    break;
            }
            return Mathf.Clamp01(a) * (1f - Step(0.94f, 1f, r));
        }

        private static float Step(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>到中线距离为 distance、半宽 halfWidth 的柔边带（约 1.5 px 羽化）。</summary>
        private static float Band(float distance, float halfWidth)
        {
            const float feather = 0.022f;
            return 1f - Step(halfWidth - feather, halfWidth + feather, Mathf.Abs(distance));
        }

        private static Vector2 Polar(float degrees, float radius)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius;
        }

        private static float Distance(Vector2 a, Vector2 b) { return (a - b).magnitude; }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>六角形「半径」：到六条边法线投影的最大值（等于内切圆半径时正好在边上）。</summary>
        private static float Hex(Vector2 p, float rotation)
        {
            float best = 0f;
            for (int i = 0; i < 6; i++) best = Mathf.Max(best, Vector2.Dot(p, Polar(rotation + i * 60f, 1f)));
            return best;
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Color> colors = new List<Color>();
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                colors.Add(ca); colors.Add(cb); colors.Add(cc);
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
