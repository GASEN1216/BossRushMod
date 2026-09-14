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
    /// - **噬风·回响**（结局后每趟可引一次，<see cref="SkyIslandStormEchoRules"/>）复用这里全部的相位阈值、脉冲、预警与提速常量，
    ///   基底战调参时回响自动跟着变。它只多一个模式位：风眼钉在预警开始的位置、不跟着本体追人（首战的圈每波重读本体位置，
    ///   追人时逃圈要的速度高于纸面，R-12）；每档三波之后在原地再响一声（半径同最后一波）；圈与光换成风晶的暖金色。
    /// </summary>
    internal sealed class SkyIslandStormBoss : MonoBehaviour
    {
        /// <summary>
        /// 相位血线：跨过即触发一次风暴脉冲，且此后攻速提升一档。
        /// 原来只有 0.66 / 0.33 两档，配 18 倍血就是「打很久的血包 + 两次特殊时刻」。
        /// 改成四档后节奏由编排承担，血量倍率同步从 18 降到 13（见 <see cref="SkyIslandEnemyTiers"/>）。
        /// </summary>
        internal static readonly float[] PhaseThresholds = { 0.80f, 0.60f, 0.40f, 0.20f };
        /// <summary>
        /// 第一波作用半径与预警时长。**这两个数必须一起看**：官方 `ExplosionManager.CreateExplosion`
        /// 没有距离衰减，圈内一律吃满伤，所以「能不能跑出去」完全由 半径 ÷ 预警秒数 决定。
        /// 旧值 9 m / 1 s 需要 9 m/s 才逃得掉——贴脸的近战流在数学上必吃满三段 114。
        /// 现在 7 m / 1.4 s ≈ 5 m/s，正常跑动可达；后两波各多 1.5 m、各有 0.45 s，持续外跑即可拉开。
        /// </summary>
        internal const float PulseRadius = 7f;
        internal const float PulseDamage = 38f;
        internal const int PulseWaves = 3;
        internal const float PulseTelegraph = 1.4f;
        /// <summary>两波之间（以及回响那一声之前）的秒数：下一波的圈在这段间隔里提前画出来。首战与回响共用。</summary>
        internal const float WaveGap = 0.45f;
        private const float TickInterval = 0.25f;

        private CharacterMainControl boss;
        private Health health;
        private Func<bool> valid;
        private Action<string, bool> report;
        private Action defeated;
        private int phase;
        private bool subscribed, pulsing, finished;
        private float nextTick;
        private AICharacterController brain;
        private float baseReactionTime, baseCurrentReactionTime, baseShootDelay;
        /// <summary>回响模式（<see cref="SkyIslandStormEchoRules"/>）：遭遇 owner 在 Bind 时给定，之后不变。</summary>
        private bool echo;
        /// <summary>这一档风眼的位置：预警开始时读一次。回响的圈、三波与回响那一声都落在这里；首战不读它。</summary>
        private Vector3 eyeOrigin;
        /// <summary>这一档还在场的预警光与地面圈。回响挂在地图根上、不随本体销毁，所以组件销毁时自己收。</summary>
        private GameObject warningObject, ringObject;
        /// <summary>回响的圈与光：晴岚风晶那种暖金色，一眼分得出不是首战的风暴色。</summary>
        private static readonly Color EchoTint = new Color(0.96f, 0.74f, 0.40f, 1f);

        /// <summary>
        /// 末相位相对**出场时**的反应速度上限。
        ///
        /// 旧写法是每进一档就 `/= 1.25f`，两档时总量 1.56 还算克制；相位加到四档之后它复利成
        /// 1.25⁴ ≈ 2.44，再叠上档次自带的 1.7 倍，末段反应时间只有官方拾荒者的 1/4.15 ——
        /// 那已经不是「更凶」而是没有反应窗口。现在按基线算绝对值，四档线性摊到 1.6 封顶，
        /// 叠档次后总量 2.72，且**重复进入同一档不会再叠加**。
        /// </summary>
        internal const float MaxPhaseSpeedup = 1.6f;

        /// <summary>纯逻辑：第 phase 档相对出场基线的提速倍率。隔离回归直接钉住它不会复利。</summary>
        internal static float PhaseSpeedup(int phase)
        {
            if (phase <= 0) return 1f;
            int total = PhaseThresholds.Length;
            if (phase >= total) return MaxPhaseSpeedup;
            return 1f + (MaxPhaseSpeedup - 1f) * phase / total;
        }

        internal void Bind(CharacterMainControl character, Func<bool> isValid,
            Action<string, bool> status, Action onDefeated, bool echoMode)
        {
            if (character == null) throw new ArgumentNullException("character");
            boss = character;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("噬风缺少生命组件");
            valid = isValid;
            report = status;
            defeated = onDefeated;
            echo = echoMode;
            // 相位提速改成「按基线算绝对值」，所以必须在这里先记下基线。
            // 此刻 `SkyIslandEnemyTiers.ApplyAi` 已经跑过（`SkyIslandEncounters.Spawn` 里它排在
            // `ApplyIdentity` 之前），因此基线是**已含档次 1.7 倍**的值，相位倍率在它之上叠加。
            brain = character.GetComponentInChildren<AICharacterController>();
            if (brain != null)
            {
                baseReactionTime = brain.baseReactionTime;
                baseCurrentReactionTime = brain.reactionTime;
                baseShootDelay = brain.shootDelay;
            }
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            if (echo)
                Announce("噬风的回响翻上了栈道 —— 这一回，风眼落在哪儿就钉在哪儿。",
                    "The Windeater's echo climbs onto the boardwalk. This time the eye stays where it falls.", false);
            else
                Announce("噬风从云海里翻上来了 —— 它循着重新亮起的两盏灯。",
                    "The Windeater rises from the sea of cloud, drawn by the two relit beacons.", false);
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
            // **按基线算绝对值，不做 `/=` 自乘**——否则相位数一改，提速就会悄悄复利。
            try
            {
                if (brain != null)
                {
                    float speedup = PhaseSpeedup(phase);
                    brain.baseReactionTime = baseReactionTime / speedup;
                    brain.reactionTime = baseCurrentReactionTime / speedup;
                    brain.shootDelay = baseShootDelay / speedup;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 相位提速失败：" + e.Message); }
            // 四档相位各有台词：首档教学、末档提示收尾，中间两档只作提醒，避免每次都喊「最后一段」。
            // 三条分支各自成对传中英，不写成两个并列的三目：那样中文与英文各在一棵表达式树里，
            // 漏译一支时看不出来，本地化守卫也认不出这是配好的对照。
            // 相位台词是**机制提示**（1.4 秒后圈内吃伤害），走警示通道：抢在普通字幕前面播、不会被队列挤掉。
            if (phase == 1)
                Announce("噬风收拢了风眼 —— 离开它脚下的那一圈。",
                    "The Windeater draws its eye shut. Get out of the ring.", true);
            else if (phase >= PhaseThresholds.Length)
                Announce("云柱塌下来了 —— 最后一段，别站在原地。",
                    "The column collapses. Last stretch: keep moving.", true);
            else
                Announce("风眼又张开了 —— 跟着圈往外跑。",
                    "The eye opens again. Run out with the ring.", true);
            StartCoroutine(PulseRoutine());
        }

        /// <summary>第 wave 波的作用半径。Detonate 与地面预警圈共用，避免两处各写一遍。</summary>
        internal static float RadiusForWave(int wave) { return PulseRadius + wave * 1.5f; }

        private IEnumerator PulseRoutine()
        {
            pulsing = true;
            // 回响的风眼钉在预警开始这一刻：圈、三波与回响那一声都落在这里，不跟着本体追人（R-12）。首战不读它。
            eyeOrigin = boss.transform.position;
            GameObject warning = null;
            LineRenderer ring = null;
            try
            {
                warning = CreateWarningLight();
                ring = CreateWarningRing();
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 预警表现失败：" + e.Message); }
            warningObject = warning;
            ringObject = ring != null ? ring.gameObject : null;

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
            warningObject = null;

            for (int wave = 0; wave < PulseWaves && !Aborted(); wave++)
            {
                // catch 子句体内不能 yield return（CS1631）：这里只在 catch 里记账，等待留在 catch 之外。
                try { Detonate(wave); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 风暴脉冲失败：" + e.Message); }
                // 下一波的范围在这 0.45 秒间隔里就画出来，后两波各有一段真正可反应的预警。
                if (ring != null && wave + 1 < PulseWaves) SetRing(ring, RadiusForWave(wave + 1), 1f);
                // 回响：最后一波之后风眼还会在原地再响一声（半径同最后一波，圈留着不动），提示赶在这段间隔里发出。
                if (echo && wave + 1 == PulseWaves)
                    Announce("风眼在原地又响了一声 —— 别急着回到圈里。",
                        "The eye echoes where it stood. Do not step back into the ring yet.", true);
                float waveUntil = Time.time + WaveGap;
                while (Time.time < waveUntil && !Aborted()) yield return null;
            }
            if (echo && !Aborted())
            {
                // 跑出第三圈的人不会被这一声碰到：同一个圆心、同一个半径，只是晚了一个间隔。
                try { Detonate(PulseWaves - 1); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 回响脉冲失败：" + e.Message); }
            }
            if (ring != null) Destroy(ring.gameObject);
            ringObject = null;
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
            // 系着晴岚护符的这一趟，风暴伤不到那么深（护符由岛上的局内 owner 持有，每一波读一次，不在每帧路径上）。
            damage.damageValue = SkyIslandFieldcraftRules.StormPulseDamage(PulseDamage, SkyIslandFieldcraft.StormWarded);
            damage.isExplosion = true;
            // buff/effect 通道：不计入武器击杀口径，也不会被「只认直接击杀」的系统当成起链。
            damage.isFromBuffOrEffect = true;
            damage.fromWeaponItemID = 0;
            // 首战每一波重读本体当前位置（风眼跟着它走）；回响钉在预警开始时的位置。
            Vector3 origin = echo ? eyeOrigin : boss.transform.position;
            // canHurtSelf=false：官方默认 true 时 selfTeam=Teams.all，风眼中心的一切都会被判定为敌人。
            LevelManager.Instance.ExplosionManager.CreateExplosion(
                origin, RadiusForWave(wave), damage, ExplosionFxTypes.normal, 0f, false);
        }

        private GameObject CreateWarningLight()
        {
            GameObject go = new GameObject("SkyIslandStormWarning");
            if (echo && boss.transform.parent != null)
            {
                go.transform.SetParent(boss.transform.parent, true);
                go.transform.position = eyeOrigin + Vector3.up * 1.2f;
            }
            else
            {
                go.transform.SetParent(boss.transform, false);
                go.transform.localPosition = Vector3.up * 1.2f;
            }
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = RingTint;
            light.intensity = 4.5f;
            light.range = PulseRadius * 2f;
            light.shadows = LightShadows.None;
            return go;
        }

        /// <summary>
        /// 贴地预警圈。鸭科夫是俯视视角，地面圆环是唯一能让玩家看清 AoE 边界的表现方式。
        ///
        /// 建造与形状走共享的 <see cref="SkyIslandGroundRing"/>（撤离点标识同款），
        /// 这里只保留「随倒计时加粗、提亮」这一层编排。
        /// 首战的圈挂在 Boss 身下并随它移动（风眼跟着本体走，`Detonate` 每波都重新读它的当前位置）；
        /// 回响的圈挂在地图根上、钉在风眼那一点。
        /// </summary>
        private LineRenderer CreateWarningRing()
        {
            Transform map = boss.transform.parent;
            LineRenderer line = echo && map != null
                ? SkyIslandGroundRing.Create(map, map.InverseTransformPoint(eyeOrigin) + Vector3.up * 0.08f)
                : SkyIslandGroundRing.Create(boss.transform, Vector3.up * 0.08f);
            line.gameObject.name = "SkyIslandStormWarningRing";
            SetRing(line, RadiusForWave(0), 0f);
            return line;
        }

        /// <summary>
        /// 按半径重画圆环。<paramref name="charge"/> 是 0..1 的蓄力度，
        /// 只影响线宽与不透明度，**不影响半径**——半径必须始终等于真实作用范围。
        /// </summary>
        private void SetRing(LineRenderer line, float radius, float charge)
        {
            Color tint = RingTint;
            Color solid = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.45f, 1f, charge));
            SkyIslandGroundRing.SetShape(line, radius, Mathf.Lerp(0.18f, 0.55f, charge), solid);
        }

        /// <summary>圈与预警光的颜色：首战是风暴色，回响是风晶的暖金色。</summary>
        private Color RingTint
        {
            get { return echo ? EchoTint : SkyIslandEnemyTiers.Tint(SkyIslandEnemyTier.Storm); }
        }

        internal static void ResetStaticCaches() { SkyIslandGroundRing.ResetStaticCaches(); }

        /// <param name="urgent">
        /// 机制提示走警示通道（`SkyIslandHud.Caption` 的 warning）：打断正在播的普通字幕、排在队首、队满时不先丢它。
        /// 旧版一律按普通字幕排队——上一条要先停满 1.6 秒再淡出，玩家读到「离开那一圈」时第一波已经炸完了。
        /// </param>
        private void Announce(string cn, string en, bool urgent)
        {
            if (report != null) report(L10n.T(cn, en), urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            if (echo)
                Announce("回响散了。风晶烧过的地方落下一箱东西。",
                    "The echo breaks apart. Where the windcrystal burned, a cache drops.", false);
            else
                Announce("噬风散成一阵普通的风。云海重新安静下来。",
                    "The Windeater breaks apart into ordinary wind. The cloud sea goes quiet.", false);
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
            // 回响的圈与光挂在地图根上：协程随组件停下时它们不会跟着本体一起销毁。
            if (warningObject != null) Destroy(warningObject);
            if (ringObject != null) Destroy(ringObject);
            warningObject = null;
            ringObject = null;
            valid = null;
            report = null;
            defeated = null;
            boss = null;
            health = null;
            brain = null;
        }

        internal const int TrophyItemCount = 4;

        /// <summary>
        /// 击败奖励：在噬风倒下的位置留一个星工遗存箱。走和搜刮点同一条建箱路径，
        /// 因此同样使用官方战利品 UI、独立本地库存，并且不引入新 TypeID。
        /// 噬风·回响不走这里（回响遗存只装岛上的东西，见 <see cref="SkyIslandStormEchoReward"/>）。
        /// </summary>
        internal static void DropTrophy(Transform parent, Vector3 position, int raidSeed)
        {
            SkyIslandRewardCrate.Create(parent, position, SkyIslandLootTier.Starworks,
                "SkyIslandStormTrophy", "StormTrophy", raidSeed, TrophyItemCount, true);
        }
    }
}
