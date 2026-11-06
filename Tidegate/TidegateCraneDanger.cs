using System;
using UnityEngine;

namespace BossRush
{
    // 四段时间从货物的位置直接推导；只在落地的边沿判一次，停在地上和上升都没有伤害。
    internal struct TidegateCraneCycle
    {
        internal const float HalfX = 0.65f, HalfZ = 0.80f, Hanging = 3f;
        internal const float Idle = 6f, Warning = 3.6f, Drop = 0.80f, Rest = 1.3f, Raise = 2.5f;
        internal float Elapsed;
        internal bool Warns { get { return Elapsed >= Idle && Elapsed < Idle + Warning + Drop; } }
        internal bool Down { get { return Elapsed >= Idle + Warning + Drop && Elapsed < Idle + Warning + Drop + Rest; } }
        internal float Height
        {
            get
            {
                float t = Elapsed - Idle - Warning;
                if (t <= 0f) return Hanging;
                if (t < Drop) { float p = t / Drop; return Hanging * (1f - p * p); }
                t -= Drop + Rest;
                if (t <= 0f) return 0f;
                return Math.Min(Hanging, Hanging * t / Raise);
            }
        }
        internal bool Step(float delta)
        {
            if (!(delta > 0f) || float.IsInfinity(delta)) return false;
            // 掉帧不能一帧吞掉预警直接砸下去；只在玩家附近的可运行帧推进。
            float before = Elapsed, impact = Idle + Warning + Drop;
            Elapsed += Math.Min(delta, 0.125f);
            if (Elapsed >= impact + Rest + Raise) Elapsed = 0f;
            return before < impact && Elapsed >= impact;
        }
        internal static bool UnderLoad(float dx, float dy, float dz)
        {
            return Math.Abs(dx) <= HalfX && Math.Abs(dz) <= HalfZ && dy >= -0.55f && dy <= 1.5f;
        }
    }

    internal sealed class TidegateCraneDanger : IDisposable
    {
        private const string HookOwner = "Visual/VIS_props/PROP_dockCrane_-44_37";
        private const float CargoHeight = 0.73f, HookBottom = 4.62f;
        private readonly Action<string, bool> announce;
        private readonly TidegateTide tide;
        private GameObject root, load;
        private Transform cable;
        private BoxCollider landedBox;
        private Material edge, steel, trim;
        private AudioClip bell;
        private AudioSource audio;
        private Vector3 center;
        private bool running, warned, disposed;
        private TidegateCraneCycle cycle;

        internal TidegateCraneDanger(TidegateWorld world, TidegateTide tide, Action<string, bool> announce)
        {
            this.announce = announce; this.tide = tide;
            try
            {
                Transform crane = world.Root.transform.Find(HookOwner);
                if (crane == null || !crane.gameObject.activeInHierarchy) return;
                // 作者吊臂长 4.60m，底座绕 Y 旋转 180°。只认这一座实体，作者移动吊车时不凭旧坐标留下隐形杀区。
                Vector3 origin = crane.position;
                if ((origin - new Vector3(-44f, 0.75f, -37f)).sqrMagnitude > 0.2f) return;
                center = origin + new Vector3(-4.6f, 0f, 0.04f);
                float ground;
                if (!world.TryGroundAt(center, out ground) || Math.Abs(ground - origin.y) > 0.45f) return;
                center.y = ground + 0.025f;
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
                if (shader == null || !shader.isSupported) return;
                edge = Mat(shader, new Color(1f, 0.72f, 0.10f));
                steel = Mat(shader, new Color(0.18f, 0.26f, 0.29f));
                trim = Mat(shader, new Color(0.93f, 0.62f, 0.12f));
                root = new GameObject("TidegateCraneLoad");
                root.transform.SetParent(world.Root.transform, false); root.transform.position = center;
                // 地面黄线的内缘即致死边界；线外安全。货物和线条共用一份尺寸。
                float w = TidegateCraneCycle.HalfX * 2f, d = TidegateCraneCycle.HalfZ * 2f;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Box(root.transform, "SideLine", new Vector3(sign * (w / 2 + 0.05f), 0.035f, 0f), new Vector3(0.1f, 0.024f, d + 0.2f), edge);
                    Box(root.transform, "EndLine", new Vector3(0f, 0.035f, sign * (d / 2 + 0.05f)), new Vector3(w, 0.024f, 0.1f), edge);
                }
                load = new GameObject("BundledSteel");
                load.transform.SetParent(root.transform, false);
                // 三根暗色工字钢叠成的货捆，亮色双箍和吊索随货件一起降到警戒线内。
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * w / 3f;
                    Box(load.transform, "BeamWeb", new Vector3(x, 0.33f, 0f), new Vector3(0.18f, 0.55f, d), steel);
                    Box(load.transform, "BeamFoot", new Vector3(x, 0.06f, 0f), new Vector3(w / 3f - 0.025f, 0.12f, d), steel);
                    Box(load.transform, "BeamFlange", new Vector3(x, 0.62f, 0f), new Vector3(w / 3f - 0.025f, 0.12f, d), steel);
                }
                for (int s = -1; s <= 1; s += 2)
                    Box(load.transform, "LiftingBand", new Vector3(0f, 0.36f, s * 0.46f), new Vector3(w, CargoHeight, 0.105f), trim);
                cable = Box(root.transform, "HoistLine", Vector3.zero, Vector3.one, steel);
                landedBox = load.AddComponent<BoxCollider>();
                landedBox.center = Vector3.up * (CargoHeight * 0.5f); landedBox.size = new Vector3(w, CargoHeight, d);
                int walls = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask.value;
                for (int i = 0; i < 32; i++) if ((walls & (1 << i)) != 0) { load.layer = i; break; }
                Pose();
                audio = root.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 1f;
                audio.minDistance = 3f; audio.maxDistance = 22f; audio.volume = 0.6f;
                bell = MakeBell();
            }
            catch (Exception e) { Dispose(); Debug.LogWarning(TidegateRaidScene.LogTag + " 吊货区未启用：" + e.Message); }
        }

        internal bool Tick(Vector3 playerPosition, float delta)
        {
            if (disposed || root == null) return false;
            Vector3 local = playerPosition - center;
            if (local.x * local.x + local.z * local.z > 22f * 22f || (tide != null && tide.IsDeepAt(center)))
            {
                if (running) { running = warned = false; cycle = default(TidegateCraneCycle); Pose(); }
                return false;
            }
            running = true;
            float previous = cycle.Elapsed;
            bool impact = cycle.Step(delta);
            if (previous < TidegateCraneCycle.Idle && cycle.Warns)
            {
                Ring();
                if (!warned && announce != null)
                {
                    warned = true;
                    announce(L10n.T("码头起重机要落货了！离开吊钩下的黄框。", "Cargo descending! Clear the yellow box under the dock crane."), true);
                }
            }
            else if (previous < TidegateCraneCycle.Idle + 2f && cycle.Elapsed >= TidegateCraneCycle.Idle + 2f) Ring();
            Pose();
            return impact && TidegateCraneCycle.UnderLoad(local.x, local.y, local.z);
        }

        private void Pose()
        {
            float bottom = cycle.Height;
            load.transform.localPosition = Vector3.up * bottom;
            float top = bottom + CargoHeight;
            cable.localPosition = Vector3.up * ((top + HookBottom) * 0.5f);
            cable.localScale = new Vector3(0.045f, HookBottom - top, 0.045f);
            landedBox.enabled = cycle.Down;
            Color c = new Color(1f, 0.72f, 0.10f);
            if (cycle.Warns) c = Color.Lerp(new Color(1f, 0.17f, 0.035f), new Color(1f, 0.92f, 0.55f), 0.5f + 0.5f * Mathf.Cos(cycle.Elapsed * 5f));
            Tint(edge, c);
        }
        private void Ring() { if (audio != null && bell != null) audio.PlayOneShot(bell); }
        private static AudioClip MakeBell()
        {
            const int rate = 22050;
            var samples = new float[11025];
            for (int i = 0; i < samples.Length; i++)
            {
                double t = (double)i / rate;
                samples[i] = (float)(Math.Min(1.0, t * 115.0) * Math.Exp(-10 * t)
                    * (0.46 * Math.Sin(2 * Math.PI * 710 * t) + 0.19 * Math.Sin(2 * Math.PI * 1640 * t)));
            }
            var clip = AudioClip.Create("TidegateHoistBell", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }
        private static Material Mat(Shader shader, Color color) { var m = new Material(shader); Tint(m, color); return m; }
        private static void Tint(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }
        private static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            Collider c = go.GetComponent<Collider>(); c.enabled = false; UnityEngine.Object.Destroy(c);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            if (bell != null) UnityEngine.Object.Destroy(bell);
            if (trim != null) UnityEngine.Object.Destroy(trim);
            if (steel != null) UnityEngine.Object.Destroy(steel);
            if (edge != null) UnityEngine.Object.Destroy(edge);
        }
    }
}
