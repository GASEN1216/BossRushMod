using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站着色器全局量（`Assets/LastShiftYard/Shaders/LastShiftCommon.hlsl`）。环境着色器读取本图实际的官方日照和环境色，
    /// 这里再按既有钟点排站场工灯、屋内顶灯与手电：
    /// 自研着色器在 GBuffer 里把 albedo 置 0，延迟光源照不到地面和建筑，夜灯只能进灯数组。
    /// 用自己的全局量名而不是复用潮闸镇的：两张图的着色器各读各的，一张图的残留状态不会串到另一张。
    /// 室内灯（2026-11-12 实机「进屋里面应该都是亮的」）：主角在司机室 / 维修棚 / 押运库里时，屋顶虽然隐藏了（ShadowOnly）还在投影，
    /// 屋里只剩天光，暗。这时给那一间排 2×2 盏顶灯（不随昼夜，白天也亮），占灯数组的位置，夜灯照旧排最近的。雨棚、桥下不点。
    /// 角色补光沿用原版室内的 SodaPointLight：SodaLight 层的体积网格在官方延迟光照前由 RenderObjects 重绘，
    /// 读深度与 GBuffer 反照率。角色和官方物件接到补光，自研地面/墙的反照率为零，继续只接灯数组。
    /// </summary>
    internal sealed class LastShiftLighting : IDisposable
    {
        private const int MaxLamps = 16;
        /// <summary>杏黄工灯：原型 `layout.mjs` 的灯高 4.2 m，照明半径按潮闸镇夜灯 9 m。</summary>
        private const float LampRange = 9f;
        private static readonly Color LampColor = new Color(1f, 0.74f, 0.40f) * 1.6f;
        private const float FlashlightReach = 3.5f, FlashlightRange = 6.5f, FlashlightMinNight = 0.25f;
        /// <summary>室内顶灯：日光灯的冷白偏一点暖；吊在楼面上方 2.6 m（司机室 3.1 m 高、维修棚 4.4、押运库 5.1）。</summary>
        private static readonly Color IndoorColor = new Color(1f, 0.95f, 0.86f) * 1.25f;
        private const float IndoorLampHeight = 2.6f;
        private static readonly string[] LitRooms = { "signal", "repair", "bond" };
        private static readonly Color FlashlightColor = new Color(0.92f, 0.95f, 1f) * 1.3f;
        private static readonly int LightingEnabledId = Shader.PropertyToID("_LastShiftLightingEnabled");
        private static readonly int SunDirectionId = Shader.PropertyToID("_LastShiftSunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_LastShiftSunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_LastShiftAmbientColor");
        private static readonly int NightId = Shader.PropertyToID("_LastShiftNight");
        private static readonly int LampCountId = Shader.PropertyToID("_LastShiftLampCount");
        private static readonly int LampPositionsId = Shader.PropertyToID("_LastShiftLampPositions");
        private static readonly int LampColorsId = Shader.PropertyToID("_LastShiftLampColors");

        private readonly List<Vector3> lamps = new List<Vector3>();
        private LastShiftWorld world;
        private string litRoom;
        private readonly Vector4[] lampPositions = new Vector4[MaxLamps];
        private readonly Vector4[] lampColors = new Vector4[MaxLamps];
        private readonly RaidOfficialIndoorLighting indoorFill = new RaidOfficialIndoorLighting(LastShiftRaidScene.LogTag, "LastShiftIndoorFill");
        private float night, nextLamps;
        private int staticLamps;
        private bool flashlightShown, disposed;

        internal float NightFactor { get { return night; } }

        internal void PrepareIndoorFill() { indoorFill.Prepare(); }

        internal string DescribeIndoorFill() { return "room=" + (litRoom ?? "none") + " " + indoorFill.DescribeActiveLights(); }

        internal void Bind(LastShiftWorld world)
        {
            if (disposed || this.world != null) return;
            this.world = world;
            lamps.AddRange(world.NightLamps);
            indoorFill.Bind(world.Root);
        }

        internal void Tick()
        {
            if (disposed || world == null) return;
            night = SkyIslandLighting.StreetLampNightFactor(SkyIslandLighting.ClockHours());
            bool hasSun = RaidOfficialLighting.TryRead(world.Root.scene, out Vector3 direction, out Color sun, out Color ambient);
            Shader.SetGlobalFloat(LightingEnabledId, hasSun ? 1f : 0f);
            if (hasSun)
            {
                Shader.SetGlobalVector(SunDirectionId, new Vector4(direction.x, direction.y, direction.z, 0f));
                Shader.SetGlobalVector(SunColorId, new Vector4(sun.r, sun.g, sun.b, 1f));
                Shader.SetGlobalVector(AmbientId, new Vector4(ambient.r, ambient.g, ambient.b, 1f));
            }
            Shader.SetGlobalFloat(NightId, night);
            // 进出屋子当帧就换灯，不等 0.5 秒一轮
            string room; Bounds bounds;
            if (!world.IndoorRoofs.TryGetInside(out room, out bounds) || Array.IndexOf(LitRooms, room) < 0) room = null;
            if (room != litRoom)
            {
                if (room == null) indoorFill.HideRoom();
                else indoorFill.ShowRoom(bounds, IndoorLampHeight);
                litRoom = room;
                nextLamps = 0f;
            }
            if (Time.unscaledTime >= nextLamps) { nextLamps = Time.unscaledTime + 0.5f; PublishLamps(bounds); }
            TickFlashlight();
        }

        /// <summary>每 0.5 秒按离玩家远近排最近的夜灯（白天只写一次 0 盏），最后一格留给手电；人在屋里时先排这一间的顶灯。</summary>
        private void PublishLamps(Bounds bounds)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            int count = 0;
            if (litRoom != null)
            {
                // 2×2 均布在房间四分点，半径取房间长边的 0.6 倍：四盏叠起来屋里各处都亮，墙外几米就衰减完
                Vector3 e = bounds.extents;
                float range = Mathf.Max(e.x, e.z) * 2f * 0.6f;
                Color color = IndoorColor;
                for (int i = 0; i < RaidOfficialIndoorLighting.LampCount; i++, count++)
                {
                    Vector3 at = RaidOfficialIndoorLighting.LampPosition(bounds, i, IndoorLampHeight);
                    lampPositions[count] = new Vector4(at.x, at.y, at.z, range);
                    lampColors[count] = new Vector4(color.r, color.g, color.b, 1f);
                }
            }
            if (night > 0.01f && player != null && lamps.Count > 0)
            {
                Vector3 origin = player.transform.position;
                lamps.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
                int take = Math.Min(MaxLamps - 1 - count, lamps.Count);
                Color color = LampColor * night;
                for (int i = 0; i < take; i++, count++)
                {
                    lampPositions[count] = new Vector4(lamps[i].x, lamps[i].y, lamps[i].z, LampRange);
                    lampColors[count] = new Vector4(color.r, color.g, color.b, 1f);
                }
            }
            for (int i = count; i < MaxLamps; i++) { lampPositions[i] = Vector4.zero; lampColors[i] = Vector4.zero; }
            staticLamps = count;
            flashlightShown = false;
            Upload(count);
        }

        private void TickFlashlight()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            bool on = night >= FlashlightMinNight && player != null && staticLamps < MaxLamps && player.FlashLight;
            if (!on)
            {
                if (flashlightShown) { flashlightShown = false; lampColors[staticLamps] = Vector4.zero; Upload(staticLamps); }
                return;
            }
            Vector3 aim = player.CurrentAimDirection;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = player.transform.forward;
            Vector3 at = player.transform.position + aim.normalized * FlashlightReach + Vector3.up * 0.6f;
            Color color = FlashlightColor * night;
            lampPositions[staticLamps] = new Vector4(at.x, at.y, at.z, FlashlightRange);
            lampColors[staticLamps] = new Vector4(color.r, color.g, color.b, 1f);
            flashlightShown = true;
            Upload(staticLamps + 1);
        }

        private void Upload(int count)
        {
            if (count > 0)
            {
                Shader.SetGlobalVectorArray(LampPositionsId, lampPositions);
                Shader.SetGlobalVectorArray(LampColorsId, lampColors);
            }
            Shader.SetGlobalFloat(LampCountId, count);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            indoorFill.Dispose();
            if (world == null) return;
            world = null;
            Shader.SetGlobalFloat(LampCountId, 0f);
            Shader.SetGlobalFloat(LightingEnabledId, 0f);
            Shader.SetGlobalFloat(NightId, 0f);
        }
    }
}
