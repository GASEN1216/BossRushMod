using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的潮汐运行时 owner：按官方游戏时钟推进潮位（规则在 <see cref="TidegateTideRules"/>），
    /// 每帧把潮位、湿痕、昼夜与灯数组（夜灯、点亮后的灯塔、手电）写进三套自研着色器的全局量，并按潮位封住被淹的导航三角形。
    /// 光照本体由会话里的 `SkyIslandLighting` 持有（自有太阳光、官方 LightControl、随官方时钟调色），
    /// 这里只把它写出的太阳 / 环境量镜像到 `_Tidegate*`，两套材质与官方角色用同一份光。
    /// </summary>
    internal sealed partial class TidegateTide : IDisposable
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
        /// <summary>灯塔灯头那盏灯白天的亮度份额：白天太阳压着看不出来，黄昏到夜里才是一圈暖光。</summary>
        private const float BeaconDayShare = 0.35f;
        /// <summary>
        /// 手电：官方枪挂手电（`CharacterMainControl.FlashLight`）是聚光灯，延迟光源照不到自研着色器，夜里光锥落不到地上。
        /// 这里在瞄准方向前方放一盏近似的灯；入夜（夜色 ≥ 0.25）才亮，跟着夜色渐强。
        /// </summary>
        private const float FlashlightReach = 3.5f, FlashlightRange = 6.5f, FlashlightMinNight = 0.25f;
        private static readonly Color FlashlightColor = new Color(0.92f, 0.95f, 1f) * 1.3f;

        private readonly TidegateWorld world;
        private readonly List<Vector3> lamps = new List<Vector3>();
        private readonly Vector4[] lampPositions = new Vector4[MaxLamps];
        private readonly Vector4[] lampColors = new Vector4[MaxLamps];
        private ArenaPrototypeNavigation navigation;
        private TidegateTidePhase phase;
        private bool sluiceOpen, disposed, phaseKnown, beaconOn, flashlightShown;
        private float level, lagoon, wetTide, wetLagoon, night;
        private float nextLamps, nextNavigation;
        private float navigationLevel = float.NaN, navigationLagoon = float.NaN;
        private Vector4 beaconPosition;
        private Color beaconColor;
        /// <summary>每 0.5 秒排好的灯（灯塔 + 最近的夜灯）占前几格；手电每帧写在它们后面那一格。</summary>
        private int staticLamps;

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
            double hours = TidegateTideRules.ElapsedHours(GameClock.Day, GameClock.TimeOfDay.TotalSeconds);
#if BOSSRUSH_DEV
            // Dev 演练 / F3 拨潮相（TidegateDrillHooks.cs）只加在这里：不写官方时钟，也不碰存档里的游戏时间。
            hours += DevOffsetHours;
#endif
            return hours;
        }

        /// <summary>到下一次换相还有多少现实秒（官方时钟倍速换算）。</summary>
        internal static double RealSecondsUntilNextPhase()
        {
            return TidegateTideRules.RealSecondsUntilNextPhase(ClockHours(), SkyIslandLighting.ClockScale());
        }

        /// <summary>此刻该不该有换相前的提示（涨潮警报 / 渡口要走），每一轮报几次由会话记。</summary>
        internal static TidegateTideNotice Notice()
        {
            return TidegateTideRules.NoticeAt(ClockHours(), SkyIslandLighting.ClockScale());
        }

        /// <summary>
        /// 灯塔点亮后灯头那盏灯常驻灯数组（CR-2026-11-05-020）：自研着色器不吃延迟光源，官方点光只照得亮角色，
        /// 地面和塔身要靠这里。本趟内不会熄。
        /// </summary>
        internal void SetBeacon(Vector3 position, float range, Color color)
        {
            beaconOn = true;
            beaconPosition = new Vector4(position.x, position.y, position.z, range);
            beaconColor = color;
            nextLamps = 0f;
        }

        internal void BindNavigation(ArenaPrototypeNavigation value)
        {
            navigation = value;
            nextNavigation = 0f;
        }

        /// <summary>大闸提起之后泻湖开始放水；提起是单向的，本趟内不会再关。</summary>
        internal void OpenSluice() { sluiceOpen = true; }

        // 只有主角脚点进入本栋室内才切换官方 ShadowOnly；室外遮挡和准心均不触发建筑虚化。
        internal void SetViewFocus(Vector3 feet)
        {
            world.IndoorRoofs.UpdateFeet(feet);
        }

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
            // 泻湖放水与湿痕按会话传进来的游戏帧时间走：暂停与拍照模式压 timeScale 时和潮位一起停，不在菜单里偷偷放干。
            lagoon = TidegateTideRules.StepLagoon(lagoon, level, sluiceOpen, deltaTime);
            wetTide = TidegateTideRules.StepWetLine(wetTide, level, deltaTime);
            wetLagoon = TidegateTideRules.StepWetLine(wetLagoon, lagoon, deltaTime);
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
            TickFlashlight();
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

        /// <summary>
        /// 每 0.5 秒排一次灯：灯塔点亮后灯头占第一格，夜里再按离玩家远近排最近的夜灯，最后一格留给手电（<see cref="TickFlashlight"/>）。
        /// 白天又没点灯塔时只写一次 0 盏。
        /// </summary>
        private void PublishLamps()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            int count = 0;
            if (beaconOn)
            {
                Color beacon = beaconColor * Mathf.Lerp(BeaconDayShare, 1f, night);
                lampPositions[count] = beaconPosition;
                lampColors[count] = new Vector4(beacon.r, beacon.g, beacon.b, 1f);
                count++;
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
            UploadLamps(count);
        }

        /// <summary>手电那一格每帧跟着玩家与瞄准方向走（0.5 秒一排会拖在人后面）；没开时只在关掉那一帧补写一次。</summary>
        private void TickFlashlight()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            bool on = night >= FlashlightMinNight && player != null && staticLamps < MaxLamps && player.FlashLight;
            if (!on)
            {
                if (flashlightShown) { flashlightShown = false; lampColors[staticLamps] = Vector4.zero; UploadLamps(staticLamps); }
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
            UploadLamps(staticLamps + 1);
        }

        private void UploadLamps(int count)
        {
            if (count > 0)
            {
                Shader.SetGlobalVectorArray(LampPositionsId, lampPositions);
                Shader.SetGlobalVectorArray(LampColorsId, lampColors);
            }
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
        /// HUD 小标行里的潮汐一段：当前潮相与潮位；身上带着潮汐怀表（<paramref name="precise"/>）时再加下一次换相的倒计时
        /// （现实时间，按官方时钟倍速换算）——这是怀表在潮闸镇里的用处。放小标行而不是目标行：目标行的字一变就挂「目标更新」
        /// （CR-2026-11-05-018）。
        /// </summary>
        internal string Chip(bool precise)
        {
            string head = TidegateText.PhaseName(phase) + " " + (level >= 0 ? "+" : "") + level.ToString("F1") + L10n.T(" 米", " m");
            if (!precise) return head;
            int seconds = (int)Math.Ceiling(RealSecondsUntilNextPhase());
            string remain = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            string next;
            switch (phase)
            {
                case TidegateTidePhase.Ebb: next = L10n.T("退完还有 ", "Low tide in "); break;
                case TidegateTidePhase.Low: next = L10n.T("开始涨潮还有 ", "Flood starts in "); break;
                case TidegateTidePhase.Flood: next = L10n.T("涨满还有 ", "High tide in "); break;
                default: next = L10n.T("开始退潮还有 ", "Ebb starts in "); break;
            }
            return head + " · " + next + remain;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
#if BOSSRUSH_DEV
            // 拨过的潮相只属于这一趟：会话收尾清零，下一趟按真实时钟。
            DevOffsetHours = 0.0;
#endif
            Shader.SetGlobalFloat(LampCountId, 0f);
            Shader.SetGlobalFloat(LightingEnabledId, 0f);
            Shader.SetGlobalFloat(NightId, 0f);
            PhaseChanged = null;
            navigation = null;
        }
    }
}
