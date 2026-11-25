// ============================================================================
// ArenaTrialsSky.cs - 出征令的天色（轻松 / 标准，只换画面）与天象（困难 / 噩梦，变天改规则）
// ============================================================================
// 节奏：开第一波就定下天色 / 天象，之后每隔几波（天色 3–4、困难 4–5、噩梦 2–3）换一次，不与上一次重复。
// 天象变天时屏幕上方一道墨色横扫、天象名墨书落印、一声低钟（等压轴 / 噩梦标题幕收完再出，一次只说一件事），
// 角标写天象名；天色换场只淡入淡出，不提示。
// 质感（owner：高级、大气、史诗、无塑料感）：雨丝稀而长、雪片慢而大，靠色调与暗角压出天气，不靠满屏粒子；
// 雷暴是天边一团冷银辉光亮两下再缓缓褪去，闷雷（战鼓）隔 0.5–1.4 秒才到。
// 天象效果（数值见 ArenaTrialsTuning）：
//   暴雪：双方移速 −15%，冰封地形权重加倍（天灾那边读 Omen）；
//   雷暴：落雷权重加倍，远处时不时一道闪光；
//   血月：首领移速 +25%、伤害 +20%（小兵不吃），复用鸭生无常血月的暗角色；
//   永夜：玩家视距 / 视角 / 近身感知大幅缩短（官方战争迷雾读这三项）；
//   暴雨：玩家视距 −25%，熔缝、毒雾持续时间减半（天灾那边读 Omen）。
// 画面全部自绘：跟随玩家的降水粒子（室内子场景不下雨雪）+ ArenaExpeditionHud 的屏幕色调层。不碰官方天气（见 Tuning 文件头）。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal sealed class ArenaTrialsSky : ArenaTrialPart
    {
        private ArenaTrialSky sky = ArenaTrialSky.Clear;
        private ArenaTrialOmen omen = ArenaTrialOmen.None;
        private int nextChangeWave;
        private bool started;
        private ArenaTrialsWeatherFx fx;
        private float flashTimer;
        private float thunderIn = -1f;
        private bool sweepPending;
        private float sweepDelay;

        internal ArenaTrialsSky(ArenaTrials trials) : base(trials) { }

        internal ArenaTrialOmen Omen { get { return omen; } }
        internal ArenaTrialSky CurrentSky { get { return sky; } }

        internal override void OnBegin()
        {
            fx = ArenaTrialsWeatherFx.Create(Trials.transform);
        }

        internal override void OnWaveStart(int wave, bool finale)
        {
            ArenaTrialProfile profile = Trials.Profile;
            if (started && wave < nextChangeWave) return;
            started = true;
            if (profile.HasOmens)
            {
                omen = PickOmen(omen);
                nextChangeWave = wave + UnityEngine.Random.Range(profile.OmenWavesMin, profile.OmenWavesMax + 1);
                ArenaExpeditionHud.SetAmbience(AmbienceOf(omen));
                if (fx != null) fx.SetAmbience(AmbienceOf(omen));
                // 横扫留到 Tick 里：等标题幕收完（压轴 / 噩梦每波都有），也让厄运先换好签
                sweepPending = true;
                sweepDelay = 0.6f;
                flashTimer = UnityEngine.Random.Range(4f, 7f);
                Trials.MarkPlayerStatsDirty();
                Trials.MarkFoeStatsDirty();
                ModBehaviour.DevLog("[BossRush] [出征令战规] 变天：" + omen + "，下次在第 " + (nextChangeWave + 1) + " 波");
            }
            else if (profile.Skies)
            {
                sky = PickSky(sky);
                nextChangeWave = wave + UnityEngine.Random.Range(ArenaTrialsTuning.SkyWavesMin, ArenaTrialsTuning.SkyWavesMax + 1);
                ArenaExpeditionHud.SetAmbience(AmbienceOf(sky));
                if (fx != null) fx.SetAmbience(AmbienceOf(sky));
            }
        }

        internal override void Tick(float deltaTime)
        {
            if (sweepPending)
            {
                if (ArenaInkTitleCard.Playing) sweepDelay = Mathf.Max(sweepDelay, 0.35f);
                else
                {
                    sweepDelay -= deltaTime;
                    if (sweepDelay <= 0f)
                    {
                        sweepPending = false;
                        ArenaExpeditionHud.PlayOmenSweep(ArenaTrialsTuning.OmenName(omen));
                        Trials.Curses.RefreshHud(true);
                    }
                }
            }
            if (thunderIn > 0f)
            {
                thunderIn -= deltaTime;
                if (thunderIn <= 0f) BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);
            }
            if (omen != ArenaTrialOmen.Thunder) return;
            flashTimer -= deltaTime;
            if (flashTimer > 0f) return;
            flashTimer = UnityEngine.Random.Range(6f, 13f);
            float strength = UnityEngine.Random.Range(0.55f, 1f);
            ArenaExpeditionHud.FlashAmbience(strength);
            // 只有近的（亮的）那几道才听得见闷雷，远处的只见光
            if (strength > 0.78f) thunderIn = UnityEngine.Random.Range(0.5f, 1.4f);
        }

        internal override void ContributePlayer(ref ArenaTrialPlayerStats stats)
        {
            switch (omen)
            {
                case ArenaTrialOmen.Blizzard:
                    stats.Move *= 1f + ArenaTrialsTuning.BlizzardMoveBonus;
                    break;
                case ArenaTrialOmen.EternalNight:
                    stats.ViewDistance *= ArenaTrialsTuning.EternalNightViewDistance;
                    stats.ViewAngle *= ArenaTrialsTuning.EternalNightViewAngle;
                    stats.SenseRange *= ArenaTrialsTuning.EternalNightSenseRange;
                    break;
                case ArenaTrialOmen.Downpour:
                    stats.ViewDistance *= ArenaTrialsTuning.DownpourViewDistance;
                    break;
            }
        }

        internal override void ContributeFoe(CharacterMainControl foe, ref ArenaTrialFoeStats stats)
        {
            if (omen == ArenaTrialOmen.Blizzard)
            {
                stats.Move *= 1f + ArenaTrialsTuning.BlizzardMoveBonus;
            }
            else if (omen == ArenaTrialOmen.BloodMoon && !Trials.Affixes.IsMinion(foe))
            {
                stats.Move *= 1f + ArenaTrialsTuning.BloodMoonBossMoveBonus;
                stats.Damage *= 1f + ArenaTrialsTuning.BloodMoonBossDamageBonus;
            }
        }

        internal override void Shutdown()
        {
            if (fx != null) fx.Release();
            fx = null;
            omen = ArenaTrialOmen.None;
            sky = ArenaTrialSky.Clear;
        }

        /// <summary>暴雨缩短熔缝 / 毒雾的持续时间。</summary>
        internal float HazardDurationFactor()
        {
            return omen == ArenaTrialOmen.Downpour ? ArenaTrialsTuning.DownpourHazardDuration : 1f;
        }

        private static ArenaTrialOmen PickOmen(ArenaTrialOmen previous)
        {
            int pick = UnityEngine.Random.Range(1, 6);
            if ((ArenaTrialOmen)pick == previous) pick = pick % 5 + 1;
            return (ArenaTrialOmen)pick;
        }

        private static ArenaTrialSky PickSky(ArenaTrialSky previous)
        {
            int pick = UnityEngine.Random.Range(1, 5);
            if ((ArenaTrialSky)pick == previous) pick = pick % 4 + 1;
            return (ArenaTrialSky)pick;
        }

        internal static ArenaExpeditionAmbience AmbienceOf(ArenaTrialOmen value)
        {
            switch (value)
            {
                case ArenaTrialOmen.Blizzard: return ArenaExpeditionAmbience.Blizzard;
                case ArenaTrialOmen.Thunder: return ArenaExpeditionAmbience.Thunder;
                case ArenaTrialOmen.BloodMoon: return ArenaExpeditionAmbience.BloodMoon;
                case ArenaTrialOmen.EternalNight: return ArenaExpeditionAmbience.EternalNight;
                case ArenaTrialOmen.Downpour: return ArenaExpeditionAmbience.Downpour;
                default: return ArenaExpeditionAmbience.None;
            }
        }

        internal static ArenaExpeditionAmbience AmbienceOf(ArenaTrialSky value)
        {
            switch (value)
            {
                case ArenaTrialSky.Rain: return ArenaExpeditionAmbience.Rain;
                case ArenaTrialSky.Snow: return ArenaExpeditionAmbience.Snow;
                case ArenaTrialSky.Dusk: return ArenaExpeditionAmbience.Dusk;
                case ArenaTrialSky.Night: return ArenaExpeditionAmbience.Night;
                default: return ArenaExpeditionAmbience.None;
            }
        }
    }

    /// <summary>
    /// 跟随玩家的降水 / 飘尘粒子（世界空间：粒子落在出生处，不随玩家平移）。
    /// 只在出征令局里存在，Release 时停发射、等最后一颗落完再销毁。室内子场景（官方 SubSceneEntry.IsInDoor）不下雨雪。
    /// </summary>
    internal sealed class ArenaTrialsWeatherFx : MonoBehaviour
    {
        private ParticleSystem rain;
        private ParticleSystem snow;
        private ParticleSystem motes;
        private ArenaExpeditionAmbience mode = ArenaExpeditionAmbience.None;
        private float indoorTimer;
        private bool indoor;

        internal static ArenaTrialsWeatherFx Create(Transform parent)
        {
            GameObject go = new GameObject("BossRush_ArenaTrialsWeather");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<ArenaTrialsWeatherFx>();
        }

        internal void SetAmbience(ArenaExpeditionAmbience value)
        {
            mode = value;
            Apply();
        }

        internal void Release()
        {
            if (this == null || gameObject == null) return;
            BossRushFxKit.Release(gameObject, 0.2f, 3f, true);
            enabled = false;
        }

        private void Apply()
        {
            try
            {
                float rainRate = 0f, rainFall = 20f, rainWind = 0f, rainAlpha = 0.35f;
                float snowRate = 0f, snowFall = 2f, snowWind = 0.6f, snowLife = 4.5f;
                float moteRate = 0f;
                switch (mode)
                {
                    // 稀而长的雨丝、慢而大的雪片：天气靠色调与暗角压出来，不靠满屏粒子
                    case ArenaExpeditionAmbience.Rain: rainRate = 140f; rainAlpha = 0.26f; break;
                    case ArenaExpeditionAmbience.Downpour: rainRate = 460f; rainFall = 25f; rainWind = -3f; rainAlpha = 0.34f; break;
                    case ArenaExpeditionAmbience.Thunder: rainRate = 300f; rainFall = 23f; rainWind = -2f; rainAlpha = 0.3f; break;
                    case ArenaExpeditionAmbience.Snow: snowRate = 55f; break;
                    case ArenaExpeditionAmbience.Blizzard: snowRate = 170f; snowFall = 3.2f; snowWind = 5f; snowLife = 3.2f; break;
                    case ArenaExpeditionAmbience.BloodMoon: moteRate = 9f; break;
                }
                if (indoor)
                {
                    rainRate = 0f;
                    snowRate = 0f;
                }
                if (rainRate > 0f && rain == null) rain = BuildRain();
                if (snowRate > 0f && snow == null) snow = BuildSnow();
                if (moteRate > 0f && motes == null) motes = BuildMotes();
                ConfigureRain(rain, rainRate, rainFall, rainWind, rainAlpha);
                ConfigureSnow(snow, snowRate, snowFall, snowWind, snowLife);
                SetRate(motes, moteRate);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令天气粒子配置失败（只留色调）: " + e.Message);
            }
        }

        private ParticleSystem BuildRain()
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Alpha, 1f);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("Rain", transform, new Vector3(0f, 9f, 0f), material, 700, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 0.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.032f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(32f, 0.1f, 32f);
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.06f;
                renderer.lengthScale = 1f;
                renderer.cameraVelocityScale = 0f;
            }
            ps.Play();
            return ps;
        }

        private static void ConfigureRain(ParticleSystem ps, float rate, float fall, float wind, float alpha)
        {
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            // 偏暖的灰银（守住原版暖色调），不是冷蓝
            main.startColor = new Color(0.80f, 0.82f, 0.85f, alpha);
            main.startLifetime = 11f / Mathf.Max(1f, fall);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind);
            velocity.y = new ParticleSystem.MinMaxCurve(-fall);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
            SetRate(ps, rate);
        }

        private ParticleSystem BuildSnow()
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Snowflake, BossRushFxBlend.Alpha, 1f);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("Snow", transform, new Vector3(0f, 7f, 0f), material, 700, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
            main.startColor = new Color(0.96f, 0.95f, 0.92f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 0.1f, 34f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.35f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(Color.white, Color.white, new Color(1f, 1f, 1f, 0f), 0.1f);
            ps.Play();
            return ps;
        }

        private static void ConfigureSnow(ParticleSystem ps, float rate, float fall, float wind, float life)
        {
            if (ps == null) return;
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind);
            velocity.y = new ParticleSystem.MinMaxCurve(-fall);
            velocity.z = new ParticleSystem.MinMaxCurve(wind * 0.3f);
            SetRate(ps, rate);
        }

        private ParticleSystem BuildMotes()
        {
            Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            ParticleSystem ps = BossRushFxKit.CreateEmitter("BloodMotes", transform, new Vector3(0f, 0.2f, 0f), material, 60, true);
            if (ps == null) return null;
            ParticleSystem.MainModule main = ps.main;
            // 少而亮的余烬：朱砂色，不是饱和的纯红
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = new Color(0.92f, 0.42f, 0.28f, 0.65f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 0.1f, 26f);
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(new Color(0.98f, 0.62f, 0.42f, 0.8f), new Color(0.82f, 0.30f, 0.20f, 0.55f),
                new Color(0.45f, 0.10f, 0.08f, 0f), 0.25f);
            ps.Play();
            return ps;
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private void Update()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null) transform.position = player.transform.position;
            indoorTimer -= Time.unscaledDeltaTime;
            if (indoorTimer > 0f) return;
            indoorTimer = 2f;
            bool nowIndoor = IsIndoor();
            if (nowIndoor == indoor) return;
            indoor = nowIndoor;
            Apply();
        }

        private static bool IsIndoor()
        {
            try
            {
                Duckov.Scenes.MultiSceneCore core = Duckov.Scenes.MultiSceneCore.Instance;
                if (core == null) return false;
                var info = core.GetSubSceneInfo();
                return info != null && info.IsInDoor;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
