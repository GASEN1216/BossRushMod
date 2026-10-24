using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>一只多关节巨口宝箱、三类可见器官、固定大小预警与碎片池。战斗判定全部由 encounter owner 持有。</summary>
    internal sealed class GreedyMimicVisual : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Transform Facing;
        internal readonly Transform Body;
        private readonly Transform jaw, healthFill, healthRoot, sparkRoot;
        private readonly Bounds bodyDamageBounds, lidWoodDamageBounds, lidLipDamageBounds;
        private const int BeamSegments = 8;
        private const int TongueSegments = 12;
        private readonly Transform[] tongue = new Transform[TongueSegments];
        private readonly Transform[] legs = new Transform[4];
        private readonly GameObject[] mutations = new GameObject[3];
        private readonly GameObject[] marks = new GameObject[5];
        private readonly LineRenderer[,] rays = new LineRenderer[5, BeamSegments];
        private readonly Transform[] bombs = new Transform[5];
        private readonly Transform[] sparks = new Transform[24];
        private readonly Vector3[] sparkDirections = new Vector3[24];
        private readonly MaterialPropertyBlock colors = new MaterialPropertyBlock();
        private readonly Material solid, glow;
        private readonly Mesh ringMesh, toothMesh;
        private float clock, swallow, burstLeft, burstDuration, scale = 1f;
        private Vector3 burstOrigin;
        private bool disposed;
        private static readonly Color Wood = new Color(0.24f, 0.08f, 0.035f);
        private static readonly Color Gold = new Color(0.95f, 0.57f, 0.13f);
        private static readonly Color Flesh = new Color(0.55f, 0.07f, 0.13f);
        private static readonly Color Red = new Color(1f, 0.19f, 0.055f);
        private static readonly Color Green = new Color(0.2f, 1f, 0.53f);

        internal GreedyMimicVisual(Scene scene, Vector3 position)
        {
            try
            {
            Root = new GameObject("BossRush_GreedyMimic");
            SceneManager.MoveGameObjectToScene(Root, scene);
            Root.transform.position = position;
            solid = FieldAdventureVisualAssets.Acquire(Root);
            glow = BossRushFxMaterials.Get(BossRushFxBlend.Additive, Texture2D.whiteTexture);
            if (solid == null || glow == null) { UnityEngine.Object.Destroy(Root); throw new InvalidOperationException("宝箱怪表现材质不可用"); }
            ringMesh = BuildRing(); toothMesh = BuildTooth();
            // 受击体与模型共享单位缩放的朝向；箱身和箱盖的当帧包络由 encounter 同步到受击盒。
            Facing = Group("StableFacing", Root.transform, Vector3.zero);
            Body = Group("BreathingBody", Facing, Vector3.zero);
            Transform[] bodyParts = new Transform[10], lidParts = new Transform[2];
            bodyParts[0] = Shape("ChestBelly", PrimitiveType.Cube, Body, new Vector3(0, 1.1f, 0), new Vector3(2.7f, 1.3f, 1.8f), Wood);
            bodyParts[1] = Shape("Gums", PrimitiveType.Cube, Body, new Vector3(0, 1.75f, 0), new Vector3(2.65f, 0.2f, 1.8f), Flesh);
            for (int i = -1; i <= 1; i++)
            {
                bodyParts[2 + (i + 1) * 2] = Shape("IronBand", PrimitiveType.Cube, Body, new Vector3(i * 1.08f, 1.12f, 0), new Vector3(0.14f, 1.4f, 1.9f), Gold);
                bodyParts[3 + (i + 1) * 2] = Shape("RivetedPlank", PrimitiveType.Cube, Body, new Vector3(0, 0.68f + (i + 1) * 0.42f, 0.925f), new Vector3(2.8f, 0.1f, 0.08f), Gold * 0.7f);
            }
            bodyParts[8] = Shape("LockPlate", PrimitiveType.Sphere, Body, new Vector3(0, 1.1f, 0.99f), new Vector3(0.55f, 0.7f, 0.15f), Gold);
            bodyParts[9] = Shape("LockHole", PrimitiveType.Cube, Body, new Vector3(0, 1.08f, 1.08f), new Vector3(0.13f, 0.28f, 0.06f), Color.black);
            jaw = Group("HingedLid", Body, new Vector3(0, 1.8f, -0.8f));
            lidParts[0] = Shape("RoundedLid", PrimitiveType.Capsule, jaw, new Vector3(0, 0.22f, 0.8f), new Vector3(1.1f, 1.4f, 1.8f), Wood);
            lidParts[0].localRotation = Quaternion.Euler(0, 0, 90);
            lidParts[1] = Shape("LidLip", PrimitiveType.Cube, jaw, new Vector3(0, 0, 0.8f), new Vector3(2.85f, 0.2f, 1.9f), Gold);
            // 只缓存主箱身与主箱盖的网格包络；眼、牙、腿、突变器官和世界效果不扩大受击区域。
            bodyDamageBounds = PartBounds(bodyParts);
            lidWoodDamageBounds = PartBounds(lidParts[0]);
            lidLipDamageBounds = PartBounds(lidParts[1]);
            for (int i = 0; i < 7; i++)
            {
                float x = -1.12f + i * 0.37f;
                Tooth(Body, new Vector3(x, 1.76f, 0.82f), false);
                Tooth(jaw, new Vector3(x, -0.08f, 1.65f), true);
            }
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.65f : 0.65f;
                Shape("EyeSocket", PrimitiveType.Sphere, jaw, new Vector3(x, 0.58f, 1.03f), new Vector3(0.7f, 0.7f, 0.45f), Color.black);
                Shape("HungryEye", PrimitiveType.Sphere, jaw, new Vector3(x, 0.58f, 1.25f), new Vector3(0.45f, 0.45f, 0.17f), Gold, true);
                Shape("EyeSlit", PrimitiveType.Cube, jaw, new Vector3(x, 0.58f, 1.36f), new Vector3(0.1f, 0.4f, 0.05f), Color.black);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -1.15f : 1.15f, 0.65f, i < 2 ? 0.6f : -0.6f);
                legs[i] = Group("BentLeg", Body, p);
                Shape("Thigh", PrimitiveType.Capsule, legs[i], new Vector3(0, -0.12f, 0), new Vector3(0.38f, 0.4f, 0.4f), Flesh).localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 35 : -35);
                Shape("ClawedFoot", PrimitiveType.Cube, legs[i], new Vector3(i % 2 == 0 ? -0.2f : 0.2f, -0.5f, 0.15f), new Vector3(0.6f, 0.2f, 0.65f), Gold);
            }
            for (int i = 0; i < TongueSegments; i++)
            {
                tongue[i] = Shape("WhippingTongueSegment", PrimitiveType.Capsule, Root.transform, Vector3.zero, Vector3.one, Flesh);
                tongue[i].gameObject.SetActive(false);
            }
            mutations[0] = Group("PopcornCannons", Body, new Vector3(0, 1.4f, 0)).gameObject;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (1.35f + (i / 2) * 0.27f);
                Shape("BrassBarrel", PrimitiveType.Cylinder, mutations[0].transform, new Vector3(x, i / 2 * 0.3f, 0.7f), new Vector3(0.34f, 0.8f, 0.34f), Gold).localRotation = Quaternion.Euler(90, 0, 0);
                Shape("BlackMuzzle", PrimitiveType.Sphere, mutations[0].transform, new Vector3(x, i / 2 * 0.3f, 1.5f), new Vector3(0.29f, 0.29f, 0.12f), Color.black);
            }
            mutations[1] = Group("BombBelly", Body, new Vector3(0, 1.15f, -1.15f)).gameObject;
            for (int i = 0; i < 3; i++)
            {
                Shape("BombSac", PrimitiveType.Sphere, mutations[1].transform, new Vector3((i - 1) * 0.8f, i % 2 * 0.5f, 0), new Vector3(1f, 1f, 1f), new Color(0.13f, 0.16f, 0.18f));
                Shape("BurningFuse", PrimitiveType.Cylinder, mutations[1].transform, new Vector3((i - 1) * 0.8f, 0.65f + i % 2 * 0.5f, 0), new Vector3(0.12f, 0.25f, 0.12f), Red, true);
            }
            mutations[2] = Group("RidiculousMedicineBottle", Body, new Vector3(0, 2.75f, -0.7f)).gameObject;
            Shape("BottleBody", PrimitiveType.Cylinder, mutations[2].transform, Vector3.zero, new Vector3(1f, 0.9f, 1f), new Color(0.13f, 0.65f, 0.43f));
            Shape("BottleCap", PrimitiveType.Cylinder, mutations[2].transform, new Vector3(0, 1f, 0), new Vector3(0.7f, 0.16f, 0.7f), Gold);
            Shape("CrossA", PrimitiveType.Cube, mutations[2].transform, new Vector3(0, 0, 0.52f), new Vector3(0.6f, 0.16f, 0.05f), Color.white);
            Shape("CrossB", PrimitiveType.Cube, mutations[2].transform, new Vector3(0, 0, 0.52f), new Vector3(0.16f, 0.6f, 0.05f), Color.white);
            Transform bar = Group("WorldHealth", Body, new Vector3(-1.3f, 3.95f, 0));
            healthRoot = bar;
            Shape("HealthBacking", PrimitiveType.Cube, bar, new Vector3(1.3f, 0, 0), new Vector3(2.7f, 0.2f, 0.12f), Color.black);
            healthFill = Shape("HealthFill", PrimitiveType.Cube, bar, new Vector3(1.3f, 0, 0.08f), new Vector3(2.6f, 0.12f, 0.12f), Red, true);
            for (int i = 0; i < 5; i++)
            {
                marks[i] = new GameObject("MimicWarningRing");
                marks[i].transform.SetParent(Root.transform, false);
                marks[i].AddComponent<MeshFilter>().sharedMesh = ringMesh;
                Paint(marks[i].AddComponent<MeshRenderer>(), Red, true);
                for (int j = 0; j < BeamSegments; j++)
                {
                    Transform beam = Group("LockedVolleyLaneSegment", Root.transform, Vector3.zero);
                    LineRenderer ray = rays[i, j] = beam.gameObject.AddComponent<LineRenderer>(); ray.sharedMaterial = glow;
                    ray.useWorldSpace = true; ray.positionCount = 2; ray.startWidth = ray.endWidth = 0.12f;
                    ray.startColor = ray.endColor = Red; beam.gameObject.SetActive(false);
                }
                bombs[i] = Shape("AirborneBomb", PrimitiveType.Sphere, Root.transform, Vector3.zero, Vector3.one * 0.75f, Gold);
                marks[i].SetActive(false); bombs[i].gameObject.SetActive(false);
            }
            sparkRoot = Group("MimicBurstPool", Root.transform, Vector3.zero);
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = Shape("CoinAndToothSpray", i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, sparkRoot, Vector3.zero, Vector3.one * 0.18f, i % 2 == 0 ? Gold : Flesh, i % 2 == 0);
                float a = i * 2.39996f; sparkDirections[i] = new Vector3(Mathf.Cos(a), 0.7f + i % 4 * 0.25f, Mathf.Sin(a));
                sparks[i].gameObject.SetActive(false);
            }
            foreach (GameObject mutation in mutations) mutation.SetActive(false);
            // 先建好所有渲染器，再接迷雾。预警落点单独采样，不能用宝箱可见性强制显示墙后的落点。
            ModEntityFogVisibility.Attach(Body.gameObject, Body);
            for (int j = 0; j < TongueSegments; j++) ModEntityFogVisibility.Attach(tongue[j].gameObject, tongue[j]);
            for (int i = 0; i < 5; i++)
            {
                ModEntityFogVisibility.Attach(marks[i]); ModEntityFogVisibility.Attach(bombs[i].gameObject);
                for (int j = 0; j < BeamSegments; j++) ModEntityFogVisibility.Attach(rays[i, j].gameObject);
            }
            ModEntityFogVisibility.Attach(sparkRoot.gameObject);
            ApplyBodyPose(false, 0f);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>世界血条同样服从官方 HUD 与暂停双门；宿主在暂停早返前也调用。</summary>
        internal void SyncHudVisibility()
        {
            if (healthRoot == null) return;
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (healthRoot.gameObject.activeSelf != visible) healthRoot.gameObject.SetActive(visible);
        }

        internal void Grow(GreedyMimicRules rules)
        {
            scale = rules.Scale; swallow = 1.4f;
            ApplyBodyPose(false, 0f);
            for (int i = 0; i < 3; i++)
            {
                int level = rules.Level((GreedyMimicFood)i);
                mutations[i].SetActive(level > 0);
                mutations[i].transform.localScale = Vector3.one * (1f + Mathf.Max(0, level - 1) * 0.22f);
            }
            Burst(Root.transform.position + Vector3.up * 2, 0.8f);
        }
        internal void Tick(float dt, Vector3 player, bool fighting, float healthFraction, float warning, GreedyMimicAttack attack)
        {
            if (disposed || Root == null) return;
            SyncHudVisibility();
            clock += dt; swallow = Mathf.Max(0, swallow - dt);
            ApplyBodyPose(fighting, warning);
            Vector3 facing = player - Root.transform.position; facing.y = 0;
            if (facing.sqrMagnitude > 0.1f && warning <= 0f) Facing.rotation = Quaternion.Slerp(Facing.rotation, Quaternion.LookRotation(facing), Mathf.Min(1, dt * 3f));
            for (int i = 0; i < legs.Length; i++) legs[i].localRotation = Quaternion.Euler(Mathf.Sin(clock * (fighting ? 8 : 3) + i * Mathf.PI) * 16, 0, 0);
            float fraction = Mathf.Clamp01(healthFraction);
            healthFill.localScale = new Vector3(2.6f * fraction, 0.12f, 0.12f);
            healthFill.localPosition = new Vector3(1.3f * fraction, 0, 0.08f);
            if (mutations[2].activeSelf) mutations[2].transform.localRotation = Quaternion.Euler(0, 0, attack == GreedyMimicAttack.Recovery && warning > 0 ? Mathf.Sin(clock * 12) * 25 : 0);
            if (burstLeft > 0)
            {
                burstLeft = Mathf.Max(0, burstLeft - dt); float t = burstDuration - burstLeft;
                for (int i = 0; i < sparks.Length; i++)
                {
                    sparks[i].position = burstOrigin + sparkDirections[i] * t * 5 + Vector3.down * t * t * 3;
                    sparks[i].localScale = Vector3.one * (0.12f + (i % 4) * 0.045f) * (burstLeft / burstDuration);
                    sparks[i].Rotate(110 * dt, 160 * dt, 80 * dt);
                    if (burstLeft <= 0) sparks[i].gameObject.SetActive(false);
                }
            }
        }
        private void ApplyBodyPose(bool fighting, float warning)
        {
            float chew = swallow > 0 ? Mathf.Sin(swallow * 17f) * 0.16f : Mathf.Sin(clock * 2.4f) * 0.025f;
            Body.localScale = new Vector3(1f + chew, 1f - chew * 0.7f, 1f + chew) * scale;
            Body.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(clock * (fighting ? 6 : 2))) * 0.09f);
            float opening = swallow > 0 ? 25f + Mathf.Abs(Mathf.Sin(swallow * 12)) * 55f : warning > 0 ? 70f : 18f + Mathf.Sin(clock * 3) * 8f;
            jaw.localRotation = Quaternion.Euler(-opening, 0, 0);
        }
        internal void GetDamageBounds(out Bounds bodyBounds, out Bounds lidBounds)
        {
            Matrix4x4 bodyToFacing = Matrix4x4.TRS(Body.localPosition, Body.localRotation, Body.localScale);
            bodyBounds = TransformBounds(bodyDamageBounds, bodyToFacing);
            Matrix4x4 lidToFacing = bodyToFacing * Matrix4x4.TRS(jaw.localPosition, jaw.localRotation, jaw.localScale);
            // 箱盖的木壳和金属唇先各自转动再合并，避免把二者未占据的局部角点也随张口放大。
            lidBounds = TransformBounds(lidWoodDamageBounds, lidToFacing);
            lidBounds.Encapsulate(TransformBounds(lidLipDamageBounds, lidToFacing));
        }
        private static Bounds PartBounds(Transform[] parts)
        {
            Bounds result = PartBounds(parts[0]);
            for (int i = 1; i < parts.Length; i++) result.Encapsulate(PartBounds(parts[i]));
            return result;
        }
        private static Bounds PartBounds(Transform part)
        {
            return TransformBounds(part.GetComponent<MeshFilter>().sharedMesh.bounds,
                Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale));
        }
        private static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
        {
            Vector3 min = source.min, max = source.max;
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)));
            return result;
        }
        internal void ShowCircle(int index, Vector3 position, float radius, bool healing)
        {
            marks[index].transform.position = position + Vector3.up * 0.055f;
            marks[index].transform.localScale = new Vector3(radius, 1, radius);
            Paint(marks[index].GetComponent<MeshRenderer>(), healing ? Green : Red, true);
            marks[index].SetActive(true);
        }
        internal void ShowLane(int index, Vector3 start, Vector3 end)
        {
            // 32 米弹道预建八段，每段独立取样迷雾，远端不能跟随中点露出黑区。
            end = start + Vector3.ClampMagnitude(end - start, 32f);
            for (int j = 0; j < BeamSegments; j++)
            {
                Vector3 a = Vector3.Lerp(start, end, (float)j / BeamSegments), b = Vector3.Lerp(start, end, (float)(j + 1) / BeamSegments);
                LineRenderer ray = rays[index, j]; ray.transform.position = (a + b) * 0.5f;
                ray.SetPosition(0, a + Vector3.up * 0.12f); ray.SetPosition(1, b + Vector3.up * 0.12f);
                ray.gameObject.SetActive(true);
            }
        }
        internal void AnimateBomb(int index, Vector3 origin, Vector3 target, float progress)
        {
            bombs[index].gameObject.SetActive(true);
            bombs[index].position = Vector3.Lerp(origin, target + Vector3.up * 0.4f, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * 7f;
            bombs[index].localScale = Vector3.one * (0.7f + Mathf.Sin(progress * 35) * 0.12f);
        }
        internal void StrikeTongue(Vector3 target)
        {
            Vector3 start = Root.transform.position + Vector3.up * 1.6f, end = target + Vector3.up * 0.4f;
            // 咬击沿用锁定落点，覆盖 45 米战斗距离；十二段把每次独立迷雾采样控制在约四米内。
            for (int j = 0; j < TongueSegments; j++)
            {
                Vector3 a = Vector3.Lerp(start, end, (float)j / TongueSegments), b = Vector3.Lerp(start, end, (float)(j + 1) / TongueSegments);
                tongue[j].gameObject.SetActive(true); tongue[j].position = (a + b) * 0.5f;
                tongue[j].rotation = Quaternion.FromToRotation(Vector3.up, b - a); tongue[j].localScale = new Vector3(0.65f, Vector3.Distance(a, b) * 0.5f, 0.65f);
            }
        }
        internal void ClearAttack()
        {
            for (int j = 0; j < TongueSegments; j++) tongue[j].gameObject.SetActive(false);
            for (int i = 0; i < marks.Length; i++)
            {
                marks[i].SetActive(false); bombs[i].gameObject.SetActive(false);
                for (int j = 0; j < BeamSegments; j++) rays[i, j].gameObject.SetActive(false);
            }
        }
        internal void Burst(Vector3 position, float duration)
        {
            sparkRoot.position = position;
            burstOrigin = position; burstLeft = burstDuration = duration;
            for (int i = 0; i < sparks.Length; i++) { sparks[i].position = position; sparks[i].gameObject.SetActive(true); }
        }
        internal void Defeated() { ClearAttack(); Body.gameObject.SetActive(false); Burst(Root.transform.position + Vector3.up * 2, 1.7f); }
        private static Transform Group(string name, Transform parent, Vector3 position)
        { Transform t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        private Transform Shape(string name, PrimitiveType kind, Transform parent, Vector3 p, Vector3 size, Color color, bool additive = false)
        {
            GameObject go = GameObject.CreatePrimitive(kind); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = size;
            Collider collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            Paint(go.GetComponent<MeshRenderer>(), color, additive); return go.transform;
        }
        private void Paint(Renderer renderer, Color color, bool additive)
        {
            renderer.sharedMaterial = additive ? glow : solid;
            colors.Clear(); colors.SetColor("_BaseColor", color); colors.SetVector("_TintColor", new Vector4(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, color.a * 0.5f)); renderer.SetPropertyBlock(colors);
            renderer.shadowCastingMode = additive ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = !additive;
        }
        private void Tooth(Transform parent, Vector3 position, bool down)
        {
            Transform t = Group("IvoryFang", parent, position); t.localScale = new Vector3(0.28f, 0.55f, 0.32f); if (down) t.localRotation = Quaternion.Euler(180, 0, 0);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = toothMesh; Paint(t.gameObject.AddComponent<MeshRenderer>(), new Color(1f, 0.87f, 0.62f), false);
        }
        private static Mesh BuildTooth()
        {
            Mesh mesh = new Mesh { name = "GreedyMimic_Fang" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0, 1, 0.1f) };
            mesh.triangles = new[] { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); return mesh;
        }
        private static Mesh BuildRing()
        {
            const int count = 64; var vertices = new Vector3[count * 2]; var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count; Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)); vertices[i * 2] = direction; vertices[i * 2 + 1] = direction * 0.9f;
                int a = i * 2, b = ((i + 1) % count) * 2, t = i * 6; triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1; triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
            }
            Mesh mesh = new Mesh { name = "GreedyMimic_WarningRing", vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); return mesh;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (Root != null) { Root.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(Root); UnityEngine.Object.Destroy(Root); }
            if (ringMesh != null) UnityEngine.Object.Destroy(ringMesh); if (toothMesh != null) UnityEngine.Object.Destroy(toothMesh);
        }
    }
}
