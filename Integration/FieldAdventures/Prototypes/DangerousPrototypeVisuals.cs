using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>巨型外置炮、压力灯、螺旋束与冲击环。资源属于本次手持，离手即回收。</summary>
    internal sealed class DangerousPrototypeVisuals : IDisposable
    {
        private readonly GameObject root;
        private readonly Transform barrel;
        private readonly Transform rotor;
        private readonly Transform needle;
        private readonly Renderer[] chargeLamps = new Renderer[5];
        private readonly List<Flash> flashes = new List<Flash>();
        private readonly DangerousPrototypeKind kind;
        private readonly Color accent;
        private float kick;
        private float spin;
        private float lastCharge = -1f;
        private bool disposed;
        private readonly MaterialPropertyBlock colors = new MaterialPropertyBlock();
        private sealed class Flash
        {
            internal GameObject Root;
            internal LineRenderer Line;
            internal float Age;
            internal float Lifetime;
            internal float Width;
            internal Color Color;
            internal bool Expand;
        }
        internal DangerousPrototypeVisuals(Transform muzzle, CharacterMainControl player, DangerousPrototypeKind selected)
        {
            try
            {
            kind = selected;
            accent = kind == DangerousPrototypeKind.JetCannon ? new Color(1f, 0.39f, 0.04f) : new Color(0.12f, 0.95f, 1f);
            root = new GameObject("DangerousPrototype_HeldRig");
            root.transform.SetParent(muzzle, false);
            FieldAdventureVisualAssets.Acquire(root);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localPosition = new Vector3(0f, 0f, -0.28f);
            Color dark = new Color(0.09f, 0.13f, 0.16f);
            Color steel = new Color(0.41f, 0.5f, 0.56f);
            Part(root.transform, "PressureHousing", PrimitiveType.Cube, new Vector3(0f, 0f, 0.15f), new Vector3(0.52f, 0.43f, 0.8f), dark);
            barrel = new GameObject("TelescopingBarrel").transform;
            barrel.SetParent(root.transform, false);
            Cylinder(barrel, "MainTube", new Vector3(0f, 0f, 0.5f), 0.26f, 0.65f, steel);
            Cylinder(barrel, "HugeMuzzle", new Vector3(0f, 0f, 1.07f), 0.43f, 0.15f, dark);
            Cylinder(barrel, "RadiantMuzzle", new Vector3(0f, 0f, 1.24f), 0.32f, 0.025f, accent);
            Cylinder(barrel, "DarkBore", new Vector3(0f, 0f, 1.27f), 0.21f, 0.03f, new Color(0.025f, 0.035f, 0.05f));
            for (int i = 0; i < 4; i++)
                Cylinder(barrel, "CoolingFin", new Vector3(0f, 0f, 0.24f + i * 0.2f), 0.34f, 0.026f, i % 2 == 0 ? accent : dark);
            rotor = new GameObject("AbsurdCoolingRotor").transform;
            rotor.SetParent(root.transform, false); rotor.localPosition = new Vector3(0f, 0f, 0.86f);
            for (int i = 0; i < 3; i++)
            {
                var arm = new GameObject("RotatingArm").transform;
                arm.SetParent(rotor, false); arm.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                Part(arm, "Fin", PrimitiveType.Cube, new Vector3(0f, 0.49f, 0f), new Vector3(0.14f, 0.48f, 0.09f), accent);
                Part(arm, "Counterweight", PrimitiveType.Sphere, new Vector3(0f, 0.72f, 0f), new Vector3(0.17f, 0.17f, 0.17f), steel);
            }
            Part(root.transform, "PressureGauge", PrimitiveType.Sphere, new Vector3(0.38f, 0.18f, 0.15f), new Vector3(0.26f, 0.26f, 0.07f), steel);
            needle = Part(root.transform, "RedNeedle", PrimitiveType.Cube, new Vector3(0.38f, 0.18f, 0.196f), new Vector3(0.025f, 0.16f, 0.025f), new Color(1f, 0.13f, 0.05f)).transform;
            for (int i = 0; i < 5; i++)
                chargeLamps[i] = Part(root.transform, "PressureLamp", PrimitiveType.Cube,
                    new Vector3(-0.2f + i * 0.1f, 0.24f, 0.12f), new Vector3(0.065f, 0.05f, 0.15f), accent).GetComponent<Renderer>();
            for (int side = -1; side <= 1; side += 2)
            {
                Cylinder(root.transform, "OversizedPressureTank", new Vector3(side * 0.35f, -0.13f, -0.1f), 0.15f, 0.38f, accent);
                Cylinder(root.transform, "TankCap", new Vector3(side * 0.35f, -0.13f, 0.28f), 0.18f, 0.04f, dark);
            }
            // 挂枪后新增的模型须接入官方角色显隐缓存，模型构建结束后才 SetRenderers。
            var subVisuals = root.AddComponent<CharacterSubVisuals>();
            subVisuals.SetRenderers();
            subVisuals.SetCharacter(player);
            if (player.characterModel != null) player.characterModel.SyncHiddenToMainCharacter();
            ModEntityFogVisibility.Attach(root, player.transform);
            }
            catch { Dispose(); throw; }
        }

        internal void Tick(float deltaTime, float charge)
        {
            if (root == null || BossRushUI.IsGamePaused()) return;
            kick = Mathf.Max(0f, kick - deltaTime * 5f);
            barrel.localPosition = new Vector3(0f, 0f, -kick * 0.32f);
            spin += deltaTime * (100f + charge * 320f + kick * 700f);
            rotor.localRotation = Quaternion.Euler(0f, 0f, spin);
            needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(75f, -75f, charge));
            if (Mathf.Abs(charge - lastCharge) > 0.03f)
            {
                lastCharge = charge;
                for (int i = 0; i < chargeLamps.Length; i++)
                    Tint(chargeLamps[i], charge >= (i + 1) * 0.2f ? accent : new Color(0.07f, 0.1f, 0.12f), colors);
            }
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash flash = flashes[i];
                flash.Age += deltaTime;
                if (flash.Root == null || flash.Age >= flash.Lifetime)
                {
                    if (flash.Root != null) UnityEngine.Object.Destroy(flash.Root);
                    flashes.RemoveAt(i); continue;
                }
                float t = flash.Age / flash.Lifetime;
                if (flash.Expand) flash.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 2.2f, t);
                flash.Line.widthMultiplier = flash.Width * (1f - t * 0.6f);
                Color color = flash.Color; color.a *= 1f - t;
                flash.Line.startColor = color; flash.Line.endColor = color;
            }
        }
        internal void Fire()
        {
            kick = 1f;
            if (root == null) return;
            Vector3 muzzle = barrel.TransformPoint(new Vector3(0f, 0f, 1.3f));
            Impact(muzzle, root.transform.forward, 0);
            // 连续六个锥形烟焰节，限制在枪口附近，不创建遮住屏幕的巨大面片。
            for (int i = 0; i < 6; i++)
            {
                Vector3 center = muzzle + root.transform.forward * (0.22f + i * 0.29f);
                Ring(center, root.transform.forward, 0.19f + i * 0.06f, accent, 0.16f, 0.34f + i * 0.025f);
            }
        }
        internal void HideRig() { if (root != null) root.SetActive(false); }
        internal void Beam(Vector3 from, Vector3 to, bool jet, int bounce)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.02f) return;
            Color color = jet ? new Color(1f, 0.42f, 0.08f, 0.85f)
                : (bounce == 0 ? new Color(0.15f, 0.65f, 1f, 0.75f) : new Color(1f, 0.67f, 0.1f, 0.95f));
            // 长光束分段采样迷雾，不能因枪口可见就显示整条穿入黑区的轨迹。
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / 4f), 1, 16);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Vector3.Lerp(from, to, (float)i / segments);
                Vector3 b = Vector3.Lerp(from, to, (float)(i + 1) / segments);
                Stroke(new[] { a, b }, color, jet ? 0.58f : (bounce > 0 ? 0.32f : 0.15f), 0.42f, false);
                if (jet || bounce > 0)
                    Stroke(new[] { a, b }, new Color(1f, 0.96f, 0.65f, 0.8f), jet ? 0.13f : 0.07f, 0.27f, false);
            }
            Vector3 axis = delta / length;
            Vector3 right = Vector3.Cross(axis, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, axis).normalized;
            for (int section = 0; section < segments; section++)
            {
                Vector3[] spiral = new Vector3[13];
                for (int i = 0; i < spiral.Length; i++)
                {
                    float t = (section + i / 12f) / segments;
                    float angle = t * length * 4f;
                    spiral[i] = Vector3.Lerp(from, to, t) + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * (jet ? 0.5f : 0.26f);
                }
                Stroke(spiral, color, 0.07f, 0.5f, false);
            }
        }
        internal void Impact(Vector3 point, Vector3 normal, int bounce)
        {
            Color color = bounce == 0 ? accent : new Color(1f, 0.65f, 0.1f, 0.9f);
            Ring(point + normal * 0.04f, normal, 0.5f, color, 0.12f, 0.55f);
            Ring(point + normal * 0.06f, normal, 0.29f, Color.white, 0.055f, 0.32f);
        }
        private void Ring(Vector3 center, Vector3 normal, float radius, Color color, float width, float lifetime)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.1f) right = Vector3.right;
            Vector3 up = Vector3.Cross(normal, right).normalized;
            Vector3[] points = new Vector3[25];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / (points.Length - 1);
                points[i] = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            }
            Stroke(points, color, width, lifetime, true);
        }
        private void Stroke(Vector3[] points, Color color, float width, float lifetime, bool expand)
        {
            // 最多 112 条短命线，连续枪械的事件频率不影响此上限。
            if (flashes.Count >= 112) return;
            GameObject go = new GameObject("DangerousPrototype_BlastSegment");
            Vector3 center = (points[0] + points[points.Length - 1]) * 0.5f;
            if (expand)
            {
                center = Vector3.zero;
                for (int i = 0; i < points.Length - 1; i++) center += points[i];
                center /= points.Length - 1;
            }
            go.transform.position = center;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Additive);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) line.SetPosition(i, points[i] - center);
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.startColor = color; line.endColor = color;
            flashes.Add(new Flash { Root = go, Line = line, Lifetime = lifetime, Width = width, Color = color, Expand = expand });
            ModEntityFogVisibility.Attach(go);
        }
        internal static GameObject CreateStation(Vector3 position)
        {
            GameObject station = new GameObject("Dingdang_DangerousPrototypeBench");
            try
            {
            FieldAdventureVisualAssets.Acquire(station);
            station.transform.position = position;
            Color dark = new Color(0.1f, 0.16f, 0.2f);
            Color orange = new Color(1f, 0.52f, 0.05f);
            Part(station.transform, "Workbench", PrimitiveType.Cube, new Vector3(0f, 0.7f, 0f), new Vector3(2.5f, 0.2f, 1.25f), dark);
            for (int i = -1; i <= 1; i += 2)
            {
                Part(station.transform, "Leg", PrimitiveType.Cube, new Vector3(i, 0.34f, 0f), new Vector3(0.2f, 0.7f, 1f), orange);
                Cylinder(station.transform, "PressureTank", new Vector3(i * 0.9f, 1.1f, 0f), 0.32f, 0.5f, orange);
            }
            Part(station.transform, "ControlPanel", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 0.5f, 0.5f), dark);
            for (int i = 0; i < 5; i++)
                Part(station.transform, "ConsoleLight", PrimitiveType.Sphere, new Vector3(-0.28f + i * 0.14f, 1.2f, 0.28f), Vector3.one * 0.09f, orange);
            Part(station.transform, "WarningPole", PrimitiveType.Cube, new Vector3(0f, 1.7f, -0.4f), new Vector3(0.09f, 1.4f, 0.09f), dark);
            Part(station.transform, "WarningSign", PrimitiveType.Cube, new Vector3(0f, 2.3f, -0.4f), new Vector3(1.1f, 0.65f, 0.09f), orange);
            Part(station.transform, "ExclamationStroke", PrimitiveType.Cube, new Vector3(0f, 2.38f, -0.33f), new Vector3(0.14f, 0.3f, 0.04f), dark);
            Part(station.transform, "ExclamationDot", PrimitiveType.Cube, new Vector3(0f, 2.13f, -0.33f), new Vector3(0.14f, 0.1f, 0.04f), dark);
            ModEntityFogVisibility.Attach(station);
            return station;
            }
            catch { station.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(station); UnityEngine.Object.Destroy(station); throw; }
        }
        private static GameObject Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = FieldAdventureVisualAssets.Solid;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            Tint(renderer, color, new MaterialPropertyBlock());
            return part;
        }
        private static void Cylinder(Transform parent, string name, Vector3 position, float radius, float halfLength, Color color)
        {
            GameObject cylinder = Part(parent, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2f, halfLength, radius * 2f), color);
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        private static void Tint(Renderer renderer, Color color, MaterialPropertyBlock block)
        {
            if (renderer == null) return;
            block.Clear(); block.SetColor("_BaseColor", color); block.SetColor("_TintColor", color * 0.5f);
            renderer.SetPropertyBlock(block);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (root != null) { root.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(root); UnityEngine.Object.Destroy(root); }
            foreach (Flash flash in flashes)
                if (flash.Root != null) { flash.Root.SetActive(false); UnityEngine.Object.Destroy(flash.Root); }
            flashes.Clear();
        }
    }
}
