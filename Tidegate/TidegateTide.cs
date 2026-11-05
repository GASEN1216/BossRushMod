using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的潮汐运行时 owner：按官方游戏时钟推进潮位（规则在 <see cref="TidegateTideRules"/>），
    /// 每帧把潮位、湿痕、昼夜与夜灯写进三套自研着色器的全局量，并按潮位封住被淹的导航三角形。
    /// 光照本体由会话里的 `SkyIslandLighting` 持有（自有太阳光、官方 LightControl、随官方时钟调色），
    /// 这里只把它写出的太阳 / 环境量镜像到 `_Tidegate*`，两套材质与官方角色用同一份光。
    /// </summary>
    internal sealed class TidegateTide : IDisposable
    {
        private const int MaxLamps = 16;
        private const float LampRange = 9f;
        private static readonly int LightingEnabledId = Shader.PropertyToID("_TidegateLightingEnabled");
        private static readonly int SunDirectionId = Shader.PropertyToID("_TidegateSunDirection");
        private static readonly int SunColorId = Shader.PropertyToID("_TidegateSunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_TidegateAmbientColor");
        private static readonly int NightId = Shader.PropertyToID("_TidegateNight");
        private static readonly int TideId = Shader.PropertyToID("_TidegateTide");
        private static readonly int LagoonId = Shader.PropertyToID("_TidegateLagoon");
        private static readonly int WetTideId = Shader.PropertyToID("_TidegateWetTide");
        private static readonly int WetLagoonId = Shader.PropertyToID("_TidegateWetLagoon");
        private static readonly int LampCountId = Shader.PropertyToID("_TidegateLampCount");
        private static readonly int LampPositionsId = Shader.PropertyToID("_TidegateLampPositions");
        private static readonly int LampColorsId = Shader.PropertyToID("_TidegateLampColors");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkyIslandSunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkyIslandSunColor");
        private static readonly int SkyAmbientId = Shader.PropertyToID("_SkyIslandAmbientColor");
        private static readonly int SkyEnabledId = Shader.PropertyToID("_SkyIslandLightingEnabled");
        private static readonly Color LampColor = new Color(1f, 0.72f, 0.42f) * 1.6f;

        private readonly TidegateWorld world;
        private readonly List<Vector3> lamps = new List<Vector3>();
        private readonly Vector4[] lampPositions = new Vector4[MaxLamps];
        private readonly Vector4[] lampColors = new Vector4[MaxLamps];
        private ArenaPrototypeNavigation navigation;
        private TidegateTidePhase phase;
        private bool sluiceOpen, disposed, phaseKnown;
        private float level, lagoon, wetTide, wetLagoon, night;
        private float nextLamps, nextNavigation;
        private float navigationLevel = float.NaN, navigationLagoon = float.NaN;

        internal float Level { get { return level; } }
        internal float LagoonLevel { get { return lagoon; } }
        internal TidegateTidePhase Phase { get { return phase; } }
        internal bool SluiceOpen { get { return sluiceOpen; } }
        internal float NightFactor { get { return night; } }
        /// <summary>换相时触发（老相位、新相位），给刷怪、撤离与提示用。</summary>
        internal event Action<TidegateTidePhase, TidegateTidePhase> PhaseChanged;

        internal TidegateTide(TidegateWorld world)
        {
            this.world = world;
            Transform group = world.Root.transform.Find("Markers/NightLamps");
            if (group != null) foreach (Transform lamp in group) lamps.Add(lamp.position);
            double hours = ClockHours();
            level = TidegateTideRules.LevelAt(hours);
            phase = TidegateTideRules.PhaseAt(hours);
            lagoon = TidegateTideRules.LagoonFull;
            // 进图时按「水刚退」处理湿痕：高潮线以下都带一层湿，退潮后的滩涂一眼看得出来。
            wetTide = Math.Max(level, TidegateTideRules.HighLevel);
            wetLagoon = lagoon;
        }

        /// <summary>潮汐只认跨天连续的官方时间（见 <see cref="TidegateTideRules.ElapsedHours"/>）；读不到时钟时是 NaN（按高潮处理）。</summary>
        internal static double ClockHours()
        {
            if (GameClock.Instance == null) return double.NaN;
            return TidegateTideRules.ElapsedHours(GameClock.Day, GameClock.TimeOfDay.TotalSeconds);
        }

        internal void BindNavigation(ArenaPrototypeNavigation value)
        {
            navigation = value;
            nextNavigation = 0f;
        }

        /// <summary>大闸提起之后泻湖开始放水；提起是单向的，本趟内不会再关。</summary>
        internal void OpenSluice() { sluiceOpen = true; }

        /// <summary>某一点的水位：泻湖内侧跟泻湖，其它地方跟外海。</summary>
        internal float WaterLevelAt(Vector3 position)
        {
            return world.InLagoon(position) ? lagoon : level;
        }

        /// <summary>这一点是不是泡在深水里（地面按射线取，取不到时用点位自身高度）。</summary>
        internal bool IsDeepAt(Vector3 position)
        {
            float ground;
            if (!world.TryGroundAt(position, out ground)) ground = position.y;
            return TidegateTideRules.IsDeep(WaterLevelAt(position), ground);
        }

        internal void Tick(float deltaTime)
        {
            if (disposed) return;
            double hours = ClockHours();
            level = TidegateTideRules.LevelAt(hours);
            TidegateTidePhase next = TidegateTideRules.PhaseAt(hours);
            lagoon = TidegateTideRules.StepLagoon(lagoon, level, sluiceOpen, Time.unscaledDeltaTime);
            wetTide = TidegateTideRules.StepWetLine(wetTide, level, Time.unscaledDeltaTime);
            wetLagoon = TidegateTideRules.StepWetLine(wetLagoon, lagoon, Time.unscaledDeltaTime);
            Vector4 sun = Shader.GetGlobalVector(SkySunColorId);
            night = Shader.GetGlobalFloat(SkyEnabledId) > 0.5f ? SkyIslandStreetLampRules.NightFactor(sun.x, sun.y, sun.z) : 0f;
            Shader.SetGlobalFloat(LightingEnabledId, Shader.GetGlobalFloat(SkyEnabledId));
            Shader.SetGlobalVector(SunDirectionId, Shader.GetGlobalVector(SkySunDirectionId));
            Shader.SetGlobalVector(SunColorId, sun);
            Shader.SetGlobalVector(AmbientId, Shader.GetGlobalVector(SkyAmbientId));
            Shader.SetGlobalFloat(NightId, night);
            Shader.SetGlobalFloat(TideId, level);
            Shader.SetGlobalFloat(LagoonId, lagoon);
            Shader.SetGlobalFloat(WetTideId, wetTide);
            Shader.SetGlobalFloat(WetLagoonId, wetLagoon);
            if (Time.unscaledTime >= nextLamps) { nextLamps = Time.unscaledTime + 0.5f; PublishLamps(); }
            if (navigation != null && Time.unscaledTime >= nextNavigation) { nextNavigation = Time.unscaledTime + 1f; ApplyNavigation(); }
            if (!phaseKnown) { phaseKnown = true; phase = next; return; }
            if (next != phase)
            {
                TidegateTidePhase previous = phase;
                phase = next;
                Action<TidegateTidePhase, TidegateTidePhase> handler = PhaseChanged;
                if (handler != null) handler(previous, next);
            }
        }

        /// <summary>夜里把离玩家最近的 16 盏灯写进着色器数组；白天只写一次 0 盏。</summary>
        private void PublishLamps()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (night <= 0.01f || player == null || lamps.Count == 0)
            {
                Shader.SetGlobalFloat(LampCountId, 0f);
                return;
            }
            Vector3 origin = player.transform.position;
            lamps.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
            int count = Math.Min(MaxLamps, lamps.Count);
            Color color = LampColor * night;
            for (int i = 0; i < MaxLamps; i++)
            {
                lampPositions[i] = i < count ? new Vector4(lamps[i].x, lamps[i].y, lamps[i].z, LampRange) : Vector4.zero;
                lampColors[i] = i < count ? new Vector4(color.r, color.g, color.b, 1f) : Vector4.zero;
            }
            Shader.SetGlobalVectorArray(LampPositionsId, lampPositions);
            Shader.SetGlobalVectorArray(LampColorsId, lampColors);
            Shader.SetGlobalFloat(LampCountId, count);
        }

        /// <summary>
        /// 被淹的导航三角形设为不可走：三角形三个顶点里最低的那个泡在深水里就封（官方游泳之外，敌人不下深水）。
        /// 水位变化不到 5 cm 不重算；整张图约三千个三角形，一秒最多算一次，只在本图的 A* 图上改。
        /// </summary>
        private void ApplyNavigation()
        {
            if (Math.Abs(level - navigationLevel) < 0.05f && Math.Abs(lagoon - navigationLagoon) < 0.05f) return;
            navigationLevel = level;
            navigationLagoon = lagoon;
            float seaDeep = level - TidegateTideRules.DeepWater, lagoonDeep = lagoon - TidegateTideRules.DeepWater;
            navigation.SetWalkable(delegate (Vector3 a, Vector3 b, Vector3 c)
            {
                float lowest = Math.Min(a.y, Math.Min(b.y, c.y));
                if (lowest >= Math.Max(seaDeep, lagoonDeep)) return true;
                Vector3 center = (a + b + c) / 3f;
                return lowest >= (world.InLagoon(center) ? lagoonDeep : seaDeep);
            });
        }

        /// <summary>
        /// HUD 目标行：当前潮相与潮位；身上带着潮汐怀表（<paramref name="precise"/>）时再加下一次换相的倒计时
        /// （现实时间，按官方时钟倍速换算）——这是怀表在潮闸镇里的用处。
        /// </summary>
        internal string Describe(bool precise)
        {
            if (!precise)
                return TidegateText.PhaseName(phase) + " " + (level >= 0 ? "+" : "") + level.ToString("F1") + L10n.T(" 米", " m")
                    + L10n.T(" · 带潮汐怀表能看准涨落", " · a tide pocket watch shows the timing");
            double hours = ClockHours();
            double scale = SkyIslandLighting.ClockScale();
            int seconds = (int)Math.Ceiling(TidegateTideRules.HoursUntilNextPhase(hours) * 3600.0 / Math.Max(1.0, scale));
            string remain = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            string next;
            switch (phase)
            {
                case TidegateTidePhase.Ebb: next = L10n.T("退完还有 ", "Low tide in "); break;
                case TidegateTidePhase.Low: next = L10n.T("开始涨潮还有 ", "Flood starts in "); break;
                case TidegateTidePhase.Flood: next = L10n.T("涨满还有 ", "High tide in "); break;
                default: next = L10n.T("开始退潮还有 ", "Ebb starts in "); break;
            }
            return TidegateText.PhaseName(phase) + " " + (level >= 0 ? "+" : "") + level.ToString("F1") + L10n.T(" 米", " m")
                + " · " + next + remain;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Shader.SetGlobalFloat(LampCountId, 0f);
            Shader.SetGlobalFloat(LightingEnabledId, 0f);
            Shader.SetGlobalFloat(NightId, 0f);
            PhaseChanged = null;
            navigation = null;
        }
    }
}
