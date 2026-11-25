// ============================================================================
// ArenaTrialsFireRing.cs - 出征令噩梦的焚城：每波一圈火墙，开波 30 秒后收紧，圈外持续灼烧
// ============================================================================
// 规则（数字见 ArenaTrialsTuning「焚城」）：
//   - 圈心 = 本图首领刷新点的重心，贴 NavMesh；FireRingCenterSnap 米内落不到可走的地面时改用离重心最近的刷新点，
//     免得重心落在湖里 / 楼里、收紧后圈里没有站的地方；
//   - 半径 = 圈心到最远刷新点 + FireRingMargin（开波时玩家站得比所有刷新点都远就按玩家算，不让人一开波就在圈外），
//     不小于 FireRingMinRadius；
//   - 开波火墙从离玩家最近的一点燃起、分两头绕圈合拢（FireRingIgniteSeconds，燃起期间不烧人）；
//     开波 FireRingStartDelay 秒起用 FireRingShrinkSeconds 收到 FireRingRadiusFactor 倍（压轴 FireRingFinaleRadiusFactor），
//     收紧前 FireRingTelegraphSeconds 秒先在地上与 M 图画出落点圈；
//   - 玩家在圈外每秒掉 FireRingDamagePerSecond 的生命上限：火属性（火抗装备照常减免）、无视护甲与难度、翻滚躲不掉；
//     按 FireRingTickSeconds 一跳，进出圈不清计时（贴着圈边进进出出蹭不掉伤害）；
//   - 敌人不烧：火墙是困玩家的，不让首领被风筝到圈外烧死；
//   - 收波火墙向外退去、慢慢熄灭，下一波重新燃起。分裂 / 双生压着收波时火圈照常（首领倒了、身体还在打）。
// 表现在 ArenaTrialsFireRingFx；屏幕四周的热浪在 ArenaExpeditionHud.SetHeat。
// M 图：两枚官方区域标记（火圈 / 落点圈），收紧中每 MapRefreshSeconds 按新半径重设一次；收波、切图、收局时摘。
// 开销：档位不带焚城时各钩子第一句就返回（O(1)）；有火圈时每帧只算一次玩家到圈心的平面距离并驱动表现。
// ============================================================================

using System;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    internal sealed class ArenaTrialsFireRing : ArenaTrialPart
    {
        private const string MapLabelKey = "BossRush_Trials_FireRing";
        private const float MapRefreshSeconds = 0.25f;
        /// <summary>圈内离火墙这么多米以内就开始微微发烫（热浪只是提醒，不掉血）。</summary>
        private const float HeatNearEdge = 4f;

        private enum Stage
        {
            Idle,
            Igniting,
            Burning,
            Closing,
            Closed,
            Receding,
        }

        private static readonly Color MapRingColor = new Color(0.95f, 0.46f, 0.18f, 1f);
        private static readonly Color MapTargetColor = new Color(0.90f, 0.74f, 0.42f, 0.75f);

        private Stage stage = Stage.Idle;
        private Vector3 center;
        private float startRadius;
        private float targetRadius;
        private float radius;
        private float recedeFrom;
        private float stageClock;
        private float exposure;
        private bool telegraphed;
        private bool finale;
        private bool announcedOnce;
        private bool burning;
        private ArenaTrialsFireRingFx fx;
        private SimplePointOfInterest ringPoi;
        private SimplePointOfInterest targetPoi;
        private string sceneId;
        private float mapRadius = -1f;
        private float mapTimer;

        internal ArenaTrialsFireRing(ArenaTrials trials) : base(trials) { }

        // ------------------------------------------------------------------
        // 波次
        // ------------------------------------------------------------------

        internal override void OnWaveStart(int wave, bool finale)
        {
            if (!Trials.Profile.FireRing) return;
            DropRing(0.8f);
            Vector3 ringCenter;
            float ringRadius;
            if (!ResolveRing(out ringCenter, out ringRadius))
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] 焚城：本图读不到首领刷新点，这一波不起火圈");
                return;
            }
            center = ringCenter;
            startRadius = radius = ringRadius;
            targetRadius = ringRadius * (finale ? ArenaTrialsTuning.FireRingFinaleRadiusFactor : ArenaTrialsTuning.FireRingRadiusFactor);
            this.finale = finale;
            stage = Stage.Igniting;
            stageClock = 0f;
            exposure = 0f;
            telegraphed = false;
            burning = false;
            sceneId = MapPointSceneResolver.Resolve();

            Vector3 player;
            bool hasPlayer = TryPlayer(out player);
            fx = ArenaTrialsFireRingFx.Create(center, startRadius, targetRadius);
            if (fx != null) fx.Ignite(player, hasPlayer, ArenaTrialsTuning.FireRingIgniteSeconds);
            ringPoi = CreateArea("BossRush_TrialFireRingMap", MapRingColor);
            RefreshRingOnMap();
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxEmberFlare);
        }

        /// <summary>收波：火墙向外退去、慢慢熄灭（不再烧人），M 图上的圈先摘。</summary>
        internal override void OnWaveEnd()
        {
            if (stage == Stage.Idle || stage == Stage.Receding) return;
            recedeFrom = radius;
            stage = Stage.Receding;
            stageClock = 0f;
            exposure = 0f;
            burning = false;
            HideMap();
            ArenaExpeditionHud.SetHeat(0f);
            if (fx != null) fx.BeginRecede();
        }

        internal override void Shutdown()
        {
            DropRing(0.6f);
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        internal override void Tick(float deltaTime)
        {
            if (stage == Stage.Idle) return;
            stageClock += deltaTime;
            switch (stage)
            {
                case Stage.Igniting:
                    if (stageClock >= ArenaTrialsTuning.FireRingIgniteSeconds)
                    {
                        stage = Stage.Burning;
                        stageClock = 0f;
                    }
                    break;
                case Stage.Burning:
                    TickBurning();
                    break;
                case Stage.Closing:
                    {
                        float t = Mathf.Clamp01(stageClock / Mathf.Max(0.1f, ArenaTrialsTuning.FireRingShrinkSeconds));
                        // 两头缓：起步沉、收尾稳，像一堵墙压过来，不是匀速滑动的圈
                        radius = Mathf.Lerp(startRadius, targetRadius, BossRushUI.SmoothStep(t));
                        if (t >= 1f) CloseIn();
                    }
                    break;
                case Stage.Receding:
                    {
                        float t = Mathf.Clamp01(stageClock / Mathf.Max(0.1f, ArenaTrialsTuning.FireRingRecedeSeconds));
                        radius = Mathf.Lerp(recedeFrom, recedeFrom * ArenaTrialsTuning.FireRingRecedeGrowth, BossRushUI.SmoothStep(t));
                        if (fx != null) fx.SetStrength(1f - BossRushUI.SmoothStep(t));
                        if (t >= 1f)
                        {
                            if (fx != null) fx.Retire(0.6f);
                            fx = null;
                            stage = Stage.Idle;
                            return;
                        }
                    }
                    break;
            }

            Vector3 player;
            bool hasPlayer = TryPlayer(out player);
            if (stage == Stage.Burning || stage == Stage.Closing || stage == Stage.Closed) TickBurn(deltaTime);
            if (fx != null) fx.Step(deltaTime, radius, player, hasPlayer);
            TickMap(deltaTime);
        }

        private void TickBurning()
        {
            float clock = Trials.WaveClock;
            if (!telegraphed && clock >= ArenaTrialsTuning.FireRingStartDelay - ArenaTrialsTuning.FireRingTelegraphSeconds)
            {
                ShowTarget();
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxBellDing);
            }
            if (clock >= ArenaTrialsTuning.FireRingStartDelay) BeginClosing();
        }

        private void ShowTarget()
        {
            telegraphed = true;
            if (fx != null) fx.ShowTarget();
            if (targetPoi == null) targetPoi = CreateArea("BossRush_TrialFireRingTargetMap", MapTargetColor);
            SetupArea(targetPoi, targetRadius, null);
        }

        /// <summary>开始收紧：火墙一旺、一声战鼓；整局第一次与压轴那一次，屏幕上方墨书「焚城」。</summary>
        private void BeginClosing()
        {
            if (!telegraphed) ShowTarget();
            stage = Stage.Closing;
            stageClock = 0f;
            if (fx != null) fx.Flare();
            // 头一回（与压轴）收紧写一道「焚城」横扫，横扫自带战鼓；演不出来（HUD 不在、正放标题卡）就只擂鼓，下一回再写
            bool swept = (!announcedOnce || finale) && !ArenaInkTitleCard.Playing
                && ArenaExpeditionHud.PlayOmenSweep(ArenaTrialsTuning.FireRingName(), "战场", BossRushInkSkin.SfxWarDrum);
            if (swept) announcedOnce = true;
            else BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);
        }

        /// <summary>收到落点：符印一齐亮起盖印后隐去，落点圈摘掉（火圈本身就停在那里了）。</summary>
        private void CloseIn()
        {
            radius = targetRadius;
            stage = Stage.Closed;
            stageClock = 0f;
            if (fx != null) fx.Seal();
            RemoveArea(ref targetPoi);
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxSealStamp);
        }

        // ------------------------------------------------------------------
        // 灼烧
        // ------------------------------------------------------------------

        private void TickBurn(float deltaTime)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            Health health = main != null ? main.Health : null;
            if (health == null || health.IsDead)
            {
                burning = false;
                ArenaExpeditionHud.SetHeat(0f);
                return;
            }
            Vector3 at = main.transform.position;
            float distance = Mathf.Sqrt(ArenaTrials.FlatSqr(at, center));
            if (distance > radius)
            {
                exposure += deltaTime;
                float tick = ArenaTrialsTuning.FireRingTickSeconds;
                int guard = 0;
                while (exposure >= tick && guard++ < 4)
                {
                    exposure -= tick;
                    Burn(health, at, distance);
                }
                ArenaExpeditionHud.SetHeat(1f);
                return;
            }
            burning = false;
            float inner = radius - distance;
            ArenaExpeditionHud.SetHeat(inner < HeatNearEdge ? 0.3f * (1f - inner / HeatNearEdge) : 0f);
        }

        private void Burn(Health health, Vector3 at, float distance)
        {
            float damage = ArenaTrials.ShareOfMaxHealth(health,
                ArenaTrialsTuning.FireRingDamagePerSecond * ArenaTrialsTuning.FireRingTickSeconds);
            Vector3 edge = center + FlatDirection(at) * radius;
            ArenaTrials.HurtNeutral(health, damage, edge, false, ElementTypes.fire);
            if (fx != null) fx.Scorch(at, !burning);
            burning = true;
        }

        // ------------------------------------------------------------------
        // 圈心与半径
        // ------------------------------------------------------------------

        private bool ResolveRing(out Vector3 ringCenter, out float ringRadius)
        {
            ringCenter = Vector3.zero;
            ringRadius = 0f;
            Vector3[] points = Trials.SpawnPoints();
            if (points == null || points.Length == 0) return false;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Length; i++) sum += points[i];
            Vector3 centroid = sum / points.Length;
            ringCenter = centroid;
            bool snapped = false;
            try
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(centroid, out hit, ArenaTrialsTuning.FireRingCenterSnap, NavMesh.AllAreas))
                {
                    ringCenter = hit.position;
                    snapped = true;
                }
            }
            catch (Exception) { }
            if (!snapped)
            {
                // 重心落在不能走的地方：改用离重心最近的刷新点（首领站得住的地方，人也站得住）
                float best = float.MaxValue;
                for (int i = 0; i < points.Length; i++)
                {
                    float d = ArenaTrials.FlatSqr(points[i], centroid);
                    if (d < best)
                    {
                        best = d;
                        ringCenter = points[i];
                    }
                }
            }

            float far = 0f;
            for (int i = 0; i < points.Length; i++) far = Mathf.Max(far, ArenaTrials.FlatSqr(points[i], ringCenter));
            Vector3 player;
            if (TryPlayer(out player)) far = Mathf.Max(far, ArenaTrials.FlatSqr(player, ringCenter));
            ringRadius = Mathf.Max(ArenaTrialsTuning.FireRingMinRadius, Mathf.Sqrt(far) + ArenaTrialsTuning.FireRingMargin);
            return true;
        }

        private Vector3 FlatDirection(Vector3 at)
        {
            Vector3 d = at - center;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        private static bool TryPlayer(out Vector3 position)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            position = main != null ? main.transform.position : Vector3.zero;
            return main != null;
        }

        private void DropRing(float fadeSeconds)
        {
            HideMap();
            if (fx != null) fx.Retire(fadeSeconds);
            fx = null;
            if (stage != Stage.Idle) ArenaExpeditionHud.SetHeat(0f);
            stage = Stage.Idle;
            exposure = 0f;
            burning = false;
        }

        // ------------------------------------------------------------------
        // M 图
        // ------------------------------------------------------------------

        private void TickMap(float deltaTime)
        {
            if (ringPoi == null) return;
            mapTimer -= deltaTime;
            if (mapTimer > 0f) return;
            mapTimer = MapRefreshSeconds;
            if (Mathf.Abs(radius - mapRadius) > 0.3f) RefreshRingOnMap();
        }

        /// <summary>官方地图只在 Setup 时读半径，所以收紧中按新半径重设（标签按当前语言重注一次，玩家可能刚切过语言）。</summary>
        private void RefreshRingOnMap()
        {
            if (ringPoi == null) return;
            mapRadius = radius;
            LocalizationHelper.InjectLocalization(MapLabelKey, ArenaTrialsTuning.FireRingName());
            SetupArea(ringPoi, radius, MapLabelKey);
        }

        private SimplePointOfInterest CreateArea(string name, Color color)
        {
            GameObject go = null;
            try
            {
                go = new GameObject(name);
                go.transform.position = center;
                SimplePointOfInterest poi = go.AddComponent<SimplePointOfInterest>();
                poi.Color = color;
                poi.ShadowColor = color;
                poi.ScaleFactor = 1f;
                poi.IsArea = true;
                poi.HideIcon = true;
                return poi;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 焚城地图标记创建失败: " + e.Message);
                return null;
            }
        }

        private void SetupArea(SimplePointOfInterest poi, float areaRadius, string label)
        {
            if (poi == null) return;
            try
            {
                poi.AreaRadius = areaRadius;
                poi.Setup(null, label, false, sceneId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 焚城地图标记刷新失败: " + e.Message);
            }
        }

        private static void RemoveArea(ref SimplePointOfInterest poi)
        {
            if (poi != null) UnityEngine.Object.Destroy(poi.gameObject);
            poi = null;
        }

        private void HideMap()
        {
            RemoveArea(ref ringPoi);
            RemoveArea(ref targetPoi);
            mapRadius = -1f;
            mapTimer = 0f;
        }
    }
}
