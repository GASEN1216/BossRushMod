using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT（owner 2026-09-27 批准）：两位具名剧情对手的专属招式。此前折翎战斗体与失控的守钟装置只有官方 AI，
    /// 剧情高潮战打起来和普通拾荒者一样。各自一种招式、一个控制器（防换皮，和岛主的星焰 / 落石 / 冲步都不是同一种读法）：
    /// - 折翎「三刀封路」：刀光沿着他指向你的一条直线落四处，同时亮圈、由近到远依次落下——横着让开，别顺着线往后退；
    /// - 守钟装置「钟鸣」：以自己为圆心亮一大圈，钟响时圈内吃一下并耳鸣减速；血量过半之后紧跟着再敲一次更大的圈。墙体挡得住。
    ///
    /// 全部复用头目共用件：预警圈与结算走 SkyIslandBossForge（蓄力声、闷响、闪光自带），预警时长过 TelegraphSeconds（静听耳罩），
    /// 站定蓄力用 BossAIController 暂停、每条收尾路径都恢复，减速用 SkyIslandPlayerSlow。只订自己身上的死亡事件，OnDestroy 退订。
    /// 由遭遇 owner 经 <see cref="SkyIslandBossForge.BindChampionMoves"/> 挂上；可失败：装不上只是这一位没有专属招式。
    /// 逃圈速度（半径 ÷ 预警）全部 ≤ <see cref="SkyIslandBossRules.MaxEscapeSpeed"/>，由 SkyIslandChampionMovesGuard 钉住。
    /// </summary>
    internal sealed class SkyIslandZhelingMoves : MonoBehaviour
    {
        internal const float CutRadius = 1.8f;
        internal const float CutTelegraph = 1.0f;
        internal const float CutDamage = 16f;
        internal const int CutCount = 4;
        /// <summary>第一刀离他这么远，之后每刀再远这么多：一条约 12 m 的刀路。</summary>
        internal const float CutFirst = 2.5f;
        internal const float CutSpacing = 3.2f;
        /// <summary>由近到远相邻两刀的落下间隔。</summary>
        internal const float CutStagger = 0.25f;
        internal const float CutInterval = 8f;
        /// <summary>血量过半之后出刀更勤。</summary>
        internal const float CutIntervalHurt = 5.5f;
        internal const float CutMinRange = 3f;
        internal const float CutMaxRange = 18f;
        internal const float HurtBelow = 0.5f;
        private const float TickInterval = 0.25f;
        private const float FirstCastDelay = 4f;
        private static readonly Color CutTint = new Color(0.95f, 0.78f, 0.46f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private readonly List<LineRenderer> rings = new List<LineRenderer>();
        private float nextTick, nextCastAt;
        private bool subscribed, finished, casting, hurt, firstAnnounced;

        internal void Bind(CharacterMainControl character, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("折翎战斗体缺少生命组件");
            try { aiControl = new BossAIController(character, "SkyIslandZheling"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 折翎 AI 暂停控制不可用，出刀时不站定：" + e.Message);
            }
            nextCastAt = Time.time + FirstCastDelay;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (Aborted()) return;
            float max = health.MaxHealth;
            if (!hurt && max > 0f && health.CurrentHealth / max < HurtBelow)
            {
                hurt = true;
                SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, CutTint);
                Announce("折翎收刀又拔：出刀更快了。", "Zheling sheathes and draws again: the cuts come faster now.", true);
            }
            if (!casting && Time.time >= nextCastAt) TryCast();
        }

        private void TryCast()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 flat = player.transform.position - boss.transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < CutMinRange || distance > CutMaxRange)
            {
                nextCastAt = Time.time + 1f;
                return;
            }
            nextCastAt = Time.time + (hurt ? CutIntervalHurt : CutInterval);
            StartCoroutine(CutRoutine(flat / Mathf.Max(0.01f, distance)));
        }

        private IEnumerator CutRoutine(Vector3 direction)
        {
            casting = true;
            PauseAi();
            List<Vector3> points = new List<Vector3>();
            List<LineRenderer> lines = new List<LineRenderer>();
            Vector3 origin = boss.transform.position;
            try
            {
                for (int i = 0; i < CutCount; i++)
                {
                    Vector3 ground;
                    if (!SkyIslandBossForge.SnapToGround(origin + direction * (CutFirst + i * CutSpacing), context, 0f, out ground)) continue;
                    LineRenderer line = SkyIslandBossForge.CreateGroundRing(context.Root, ground);
                    SkyIslandBossForge.SetRing(line, CutRadius, 0f, CutTint);
                    points.Add(ground);
                    lines.Add(line);
                    rings.Add(line);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 折翎刀路预警失败：" + e.Message); }
            if (lines.Count > 0 && !firstAnnounced)
            {
                firstAnnounced = true;
                Announce("折翎把刀横在路上：刀光沿着一条线由近到远砍下来，横着让开，别顺着线往后退。",
                    "Zheling lays his blade across the road: the cuts fall along a line, near to far. Sidestep; don't back away down the line.", true);
            }
            float telegraph = SkyIslandBossRules.TelegraphSeconds(CutTelegraph, SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (lines.Count > 0 && Time.time - started < telegraph && !Aborted())
            {
                float charge = Mathf.Clamp01((Time.time - started) / telegraph);
                for (int i = 0; i < lines.Count; i++) SkyIslandBossForge.SetRing(lines[i], CutRadius, charge, CutTint);
                yield return null;
            }
            for (int i = 0; i < lines.Count && !Aborted(); i++)
            {
                // 预警没画出来的点不结算：points 与 lines 一一对应，只含成功画圈的点。
                try
                {
                    SkyIslandBossForge.Detonate(boss, points[i], CutRadius, CutDamage, false,
                        i == 0 ? SkyIslandImpactFx.BossShake : 0f, CutTint);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 折翎刀路结算失败：" + e.Message); }
                ReleaseLine(lines[i]);
                lines[i] = null;
                float wait = Time.time;
                while (Time.time - wait < CutStagger && !Aborted()) yield return null;
            }
            for (int i = 0; i < lines.Count; i++) ReleaseLine(lines[i]);
            ResumeAi();
            casting = false;
        }

        private void ReleaseLine(LineRenderer line)
        {
            if (line == null) return;
            rings.Remove(line);
            SkyIslandBossForge.ReleaseRing(line);
        }

        private void PauseAi()
        {
            try { if (aiControl != null && !aiControl.IsPaused) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 折翎站定失败：" + e.Message); }
        }

        private void ResumeAi()
        {
            try
            {
                if (aiControl != null && aiControl.IsPaused && health != null && !health.IsDead)
                    aiControl.Resume(CharacterMainControl.Main);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 折翎恢复失败：" + e.Message); }
        }

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            finished = true;
            Cleanup();
        }

        private void Cleanup()
        {
            for (int i = 0; i < rings.Count; i++) if (rings[i] != null) Destroy(rings[i].gameObject);
            rings.Clear();
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            ResumeAi();
            Cleanup();
            aiControl = null;
            context = null;
            boss = null;
            health = null;
        }
    }

    /// <summary>失控的守钟装置「钟鸣」。见 <see cref="SkyIslandZhelingMoves"/> 的类说明。</summary>
    internal sealed class SkyIslandBellEngineMoves : MonoBehaviour
    {
        internal const float TollRadius = 6f;
        internal const float TollTelegraph = 1.5f;
        internal const float TollDamage = 20f;
        /// <summary>血量过半之后紧跟着的第二响：圈更大，预警同样按「从圆心跑得出去」配。</summary>
        internal const float EchoRadius = 8.5f;
        internal const float EchoTelegraph = 1.6f;
        internal const float EchoDamage = 14f;
        /// <summary>被钟声罩住的耳鸣减速（WalkSpeed / RunSpeed 的 PercentageAdd）。</summary>
        internal const float TollSlow = -0.3f;
        internal const float TollSlowSeconds = 2f;
        internal const float TollInterval = 10f;
        internal const float TollIntervalHurt = 7f;
        internal const float TollRange = 14f;
        internal const float HurtBelow = 0.5f;
        private const float TickInterval = 0.25f;
        private const float FirstCastDelay = 5f;
        private const float SlowHeightTolerance = 2f;
        private static readonly Color TollTint = new Color(1f, 0.80f, 0.44f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private readonly SkyIslandPlayerSlow slow = new SkyIslandPlayerSlow("SkyIslandBellToll");
        private readonly List<LineRenderer> rings = new List<LineRenderer>();
        private float nextTick, nextCastAt;
        private bool subscribed, finished, casting, hurt, firstAnnounced;

        internal void Bind(CharacterMainControl character, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("守钟装置缺少生命组件");
            try { aiControl = new BossAIController(character, "SkyIslandBellEngine"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 守钟装置 AI 暂停控制不可用，敲钟时不站定：" + e.Message);
            }
            nextCastAt = Time.time + FirstCastDelay;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            slow.Tick(Time.time);
            if (Aborted()) return;
            float max = health.MaxHealth;
            if (!hurt && max > 0f && health.CurrentHealth / max < HurtBelow)
            {
                hurt = true;
                SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, TollTint);
                Announce("守钟装置的钟舌松了：之后每次都要连敲两下，第二下的圈更大。",
                    "The bell engine's clapper works loose: from now on it tolls twice, and the second ring is wider.", true);
            }
            if (!casting && Time.time >= nextCastAt) TryCast();
        }

        private void TryCast()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            if ((player.transform.position - boss.transform.position).sqrMagnitude > TollRange * TollRange)
            {
                nextCastAt = Time.time + 1f;
                return;
            }
            nextCastAt = Time.time + (hurt ? TollIntervalHurt : TollInterval);
            StartCoroutine(TollRoutine());
        }

        private IEnumerator TollRoutine()
        {
            casting = true;
            PauseAi();
            Vector3 ground;
            Vector3 center = SkyIslandBossForge.SnapToGround(boss.transform.position, context, 0f, out ground) ? ground : boss.transform.position;
            if (!firstAnnounced)
            {
                firstAnnounced = true;
                Announce("守钟装置要敲钟了：钟声只罩住它身边一圈，跑出圈或躲到墙后。",
                    "The bell engine is about to toll: the sound only covers the ring around it. Get out, or get behind a wall.", true);
            }
            IEnumerator first = Toll(center, TollRadius, TollTelegraph, TollDamage, SkyIslandImpactFx.BossShake);
            while (first.MoveNext()) yield return first.Current;
            if (hurt && !Aborted())
            {
                IEnumerator echo = Toll(center, EchoRadius, EchoTelegraph, EchoDamage, SkyIslandImpactFx.BossShake * 0.8f);
                while (echo.MoveNext()) yield return echo.Current;
            }
            ResumeAi();
            casting = false;
        }

        /// <summary>一响：画圈 → 蓄力 → 结算（官方爆炸，墙体挡得住）→ 钟声 → 圈内且没被墙挡住的主角耳鸣减速。</summary>
        private IEnumerator Toll(Vector3 center, float radius, float baseTelegraph, float damage, float shake)
        {
            LineRenderer line = null;
            try
            {
                line = SkyIslandBossForge.CreateGroundRing(context.Root, center);
                SkyIslandBossForge.SetRing(line, radius, 0f, TollTint);
                rings.Add(line);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 钟鸣预警失败：" + e.Message);
                line = null;
            }
            // 圈没画出来就不敲：看不见的范围伤害比没有招式更糟。
            if (line == null) yield break;
            float telegraph = SkyIslandBossRules.TelegraphSeconds(baseTelegraph, SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted())
            {
                SkyIslandBossForge.SetRing(line, radius, Mathf.Clamp01((Time.time - started) / telegraph), TollTint);
                yield return null;
            }
            if (!Aborted())
            {
                try
                {
                    SkyIslandBossForge.Detonate(boss, center, radius, damage, false, shake, TollTint);
                    SkyIslandBossSfx.Play(context.Root, SkyIslandBossCue.Toll, center);
                    SlowIfCaught(center, radius);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 钟鸣结算失败：" + e.Message); }
            }
            rings.Remove(line);
            SkyIslandBossForge.ReleaseRing(line);
        }

        private void SlowIfCaught(Vector3 center, float radius)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 feet = player.transform.position;
            float dx = feet.x - center.x, dz = feet.z - center.z;
            if (dx * dx + dz * dz > radius * radius || Mathf.Abs(feet.y - center.y) > SlowHeightTolerance) return;
            // 墙体挡住钟声（口径同官方爆炸被岩壁挡下）：躲到墙后的人不耳鸣。
            if (Physics.Linecast(center + Vector3.up * 1.2f, feet + Vector3.up * 1f,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return;
            slow.Apply(player, TollSlow, TollSlowSeconds);
        }

        private void PauseAi()
        {
            try { if (aiControl != null && !aiControl.IsPaused) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 守钟装置站定失败：" + e.Message); }
        }

        private void ResumeAi()
        {
            try
            {
                if (aiControl != null && aiControl.IsPaused && health != null && !health.IsDead)
                    aiControl.Resume(CharacterMainControl.Main);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 守钟装置恢复失败：" + e.Message); }
        }

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            finished = true;
            Cleanup();
        }

        private void Cleanup()
        {
            slow.Release();
            for (int i = 0; i < rings.Count; i++) if (rings[i] != null) Destroy(rings[i].gameObject);
            rings.Clear();
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            ResumeAi();
            Cleanup();
            aiControl = null;
            context = null;
            boss = null;
            health = null;
        }
    }
}
