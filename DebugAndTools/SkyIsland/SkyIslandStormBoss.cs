using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：全图唯一 Boss「噬风」的相位编排。
    ///
    /// 组成方式刻意保守，全部复用已验证的通道：
    /// - 本体是**遭遇系统的一个手动组**（噬风 + 两名断风游猎一次生成），因此天然吃 12 活体上限、
    ///   本图 graphMask、关闭距离休眠与官方掉落/经验，不另开生成路径。
    /// - 相位只做两件事：预警 1 秒后放一圈范围伤害，并让它随血线变快。没有新资源、没有新 AI。
    ///   预警是**贴地圆环**（半径等于真实作用范围），俯视视角下这是唯一读得出边界的表现。
    /// - 范围伤害显式 `canHurtSelf:false` + `isFromBuffOrEffect:true` + `fromWeaponItemID:0`，
    ///   与套装/词条自建伤害同一口径；`OnHurt` 期间不嵌套爆炸，脉冲一律由协程延后一帧发起。
    /// </summary>
    internal sealed class SkyIslandStormBoss : MonoBehaviour
    {
        /// <summary>相位血线：跨过即触发一次风暴脉冲，且此后攻速提升一档。</summary>
        internal static readonly float[] PhaseThresholds = { 0.66f, 0.33f };
        internal const float PulseRadius = 9f;
        internal const float PulseDamage = 38f;
        internal const int PulseWaves = 3;
        internal const float PulseTelegraph = 1f;
        private const float TickInterval = 0.25f;

        private CharacterMainControl boss;
        private Health health;
        private Func<bool> valid;
        private Action<string, bool> report;
        private Action defeated;
        private int phase;
        private bool subscribed, pulsing, finished;
        private float nextTick;

        internal void Bind(CharacterMainControl character, Func<bool> isValid,
            Action<string, bool> status, Action onDefeated)
        {
            if (character == null) throw new ArgumentNullException("character");
            boss = character;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("噬风缺少生命组件");
            valid = isValid;
            report = status;
            defeated = onDefeated;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            Announce("噬风从云海里翻上来了 —— 它循着重新亮起的两盏灯。",
                "The Windeater rises from the sea of cloud, drawn by the two relit beacons.");
        }

        /// <summary>纯逻辑：给定血量比例返回应处的相位序号，供隔离回归直接验证阈值不漂移。</summary>
        internal static int PhaseForFraction(float fraction)
        {
            int result = 0;
            for (int i = 0; i < PhaseThresholds.Length; i++) if (fraction <= PhaseThresholds[i]) result = i + 1;
            return result;
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (valid != null && !valid()) return;
            if (health.IsDead) return;
            float max = health.MaxHealth;
            if (max <= 0f) return;
            int target = PhaseForFraction(Mathf.Clamp01(health.CurrentHealth / max));
            if (target <= phase || pulsing) return;
            phase = target;
            EnterPhase();
        }

        private void EnterPhase()
        {
            // 相位提速：反应/射击间隔按相位再快一档，不改伤害倍率（已在档次里封顶）。
            try
            {
                AICharacterController ai = boss.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    ai.baseReactionTime /= 1.25f;
                    ai.reactionTime /= 1.25f;
                    ai.shootDelay /= 1.25f;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 相位提速失败：" + e.Message); }
            Announce(phase == 1 ? "噬风收拢了风眼 —— 离开它脚下的那一圈。" : "云柱塌下来了 —— 最后一段，别站在原地。",
                phase == 1 ? "The Windeater draws its eye shut. Get out of the ring." :
                    "The column collapses. Last stretch: keep moving.");
            StartCoroutine(PulseRoutine());
        }

        /// <summary>第 wave 波的作用半径。Detonate 与地面预警圈共用，避免两处各写一遍。</summary>
        internal static float RadiusForWave(int wave) { return PulseRadius + wave * 1.5f; }

        private IEnumerator PulseRoutine()
        {
            pulsing = true;
            GameObject warning = null;
            LineRenderer ring = null;
            try
            {
                warning = CreateWarningLight();
                ring = CreateWarningRing();
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 预警表现失败：" + e.Message); }

            // 预警阶段：地面圈按 wave0 的实际半径画出来，并随倒计时加粗、提亮。
            // 只靠一盏点光源读不出「这一圈到底多大」，而三波 38 伤害站在中心必死。
            float started = Time.time;
            while (Time.time - started < PulseTelegraph && !Aborted())
            {
                if (ring != null)
                    SetRing(ring, RadiusForWave(0), Mathf.Clamp01((Time.time - started) / PulseTelegraph));
                yield return null;
            }
            if (warning != null) Destroy(warning);

            for (int wave = 0; wave < PulseWaves && !Aborted(); wave++)
            {
                // catch 子句体内不能 yield return（CS1631）：这里只在 catch 里记账，等待留在 catch 之外。
                try { Detonate(wave); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 风暴脉冲失败：" + e.Message); }
                // 下一波的范围在这 0.45 秒间隔里就画出来，后两波各有一段真正可反应的预警。
                if (ring != null && wave + 1 < PulseWaves) SetRing(ring, RadiusForWave(wave + 1), 1f);
                float waveUntil = Time.time + 0.45f;
                while (Time.time < waveUntil && !Aborted()) yield return null;
            }
            if (ring != null) Destroy(ring.gameObject);
            pulsing = false;
        }

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (valid != null && !valid());
        }

        private void Detonate(int wave)
        {
            if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null) return;
            DamageInfo damage = new DamageInfo(boss);
            damage.damageValue = PulseDamage;
            damage.isExplosion = true;
            // buff/effect 通道：不计入武器击杀口径，也不会被「只认直接击杀」的系统当成起链。
            damage.isFromBuffOrEffect = true;
            damage.fromWeaponItemID = 0;
            Vector3 origin = boss.transform.position;
            // canHurtSelf=false：官方默认 true 时 selfTeam=Teams.all，风眼中心的一切都会被判定为敌人。
            LevelManager.Instance.ExplosionManager.CreateExplosion(
                origin, RadiusForWave(wave), damage, ExplosionFxTypes.normal, 0f, false);
        }

        private GameObject CreateWarningLight()
        {
            GameObject go = new GameObject("SkyIslandStormWarning");
            go.transform.SetParent(boss.transform, false);
            go.transform.localPosition = Vector3.up * 1.2f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = SkyIslandEnemyTiers.Tint(SkyIslandEnemyTier.Storm);
            light.intensity = 4.5f;
            light.range = PulseRadius * 2f;
            light.shadows = LightShadows.None;
            return go;
        }

        private const int RingSegments = 64;
        private static Material ringMaterial;

        /// <summary>
        /// 贴地预警圈。鸭科夫是俯视视角，地面圆环是唯一能让玩家看清 AoE 边界的表现方式。
        ///
        /// 挂在 Boss 身下并随它移动（风眼跟着本体走，`Detonate` 每波都重新读它的当前位置）。
        /// 绕 X 轴转 90°，让 LineRenderer 的 `TransformZ` 对齐方式把带子摊平在地面上。
        /// </summary>
        private LineRenderer CreateWarningRing()
        {
            GameObject go = new GameObject("SkyIslandStormWarningRing");
            go.transform.SetParent(boss.transform, false);
            go.transform.localPosition = Vector3.up * 0.08f;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingSegments;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 120;
            Material material = GetRingMaterial();
            if (material != null) line.material = material;
            SetRing(line, RadiusForWave(0), 0f);
            return line;
        }

        /// <summary>
        /// 按半径重画圆环。<paramref name="charge"/> 是 0..1 的蓄力度，
        /// 只影响线宽与不透明度，**不影响半径**——半径必须始终等于真实作用范围。
        /// </summary>
        private static void SetRing(LineRenderer line, float radius, float charge)
        {
            if (line == null) return;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * (2f * Mathf.PI / RingSegments);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            line.widthMultiplier = Mathf.Lerp(0.18f, 0.55f, charge);
            Color tint = SkyIslandEnemyTiers.Tint(SkyIslandEnemyTier.Storm);
            Color solid = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.45f, 1f, charge));
            line.startColor = solid;
            line.endColor = solid;
        }

        private static Material GetRingMaterial()
        {
            if (ringMaterial != null) return ringMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;
            ringMaterial = new Material(shader);
            ringMaterial.name = "SkyIslandStormRingMat";
            return ringMaterial;
        }

        internal static void ResetStaticCaches() { ringMaterial = null; }

        private void Announce(string cn, string en)
        {
            if (report != null) report(L10n.T(cn, en), false);
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Announce("噬风散成一阵普通的风。云海重新安静下来。",
                "The Windeater breaks apart into ordinary wind. The cloud sea goes quiet.");
            Action callback = defeated;
            defeated = null;
            if (callback != null)
            {
                try { callback(); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 击败结算失败：" + e.Message); }
            }
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            valid = null;
            report = null;
            defeated = null;
            boss = null;
            health = null;
        }

        internal const int TrophyItemCount = 4;

        /// <summary>
        /// 击败奖励：在噬风倒下的位置留一个星工遗存箱。走和搜刮点同一条建箱路径，
        /// 因此同样使用官方战利品 UI、独立本地库存，并且不引入新 TypeID。
        /// </summary>
        internal static void DropTrophy(Transform parent, Vector3 position, int raidSeed)
        {
            SkyIslandRewardCrate.Create(parent, position, SkyIslandLootTier.Starworks,
                "SkyIslandStormTrophy", "StormTrophy", raidSeed, TrophyItemCount, true);
        }
    }
}
