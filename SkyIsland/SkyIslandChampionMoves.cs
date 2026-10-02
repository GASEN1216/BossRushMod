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
    /// - 守钟装置「报时」：同一圆心近响（身边一圈）与远响（内外两圈之间）交替连敲，平时三响、血量过半五响；被罩住耳鸣减速，墙体挡得住
    ///   （2026-10-02 由「钟鸣」重做，见该类说明）。
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

    /// <summary>
    /// 失控的守钟装置「报时」（owner 2026-10-02「钟守强度低、没什么特殊好玩的机制」后重做）。
    ///
    /// 旧招只有一种「钟鸣」：玩家在 14 米内才敲、只罩身边一圈。拿枪站远一点打它就一点压力都没有，等于没有机制。
    /// 现在它站定报时，**近响**与**远响**交替连敲：
    /// - 近响：以自己为圆心亮一圈（<see cref="NearRadius"/>），钟响时圈内吃一下；
    /// - 远响：亮内外两道圈，钟声从内圈荡到外圈（<see cref="FarInner"/>–<see cref="FarOuter"/>），钟响时两圈之间吃一下；
    ///   内圈里头是安全的，外圈外头也安全。
    /// 第一响挑罩得住玩家的那一种（玩家贴身先近响，站远先远响），之后近远交替：平时三响，血量过半五响、间隔更短。
    /// 读法就是「踩着钟点进出内圈」——远处放枪的人也得在远响前钻进内圈或退到外圈外，近响前再出来。
    /// 被钟声罩住还会耳鸣减速两秒；墙体挡得住钟声（躲到墙后不吃伤害也不耳鸣），这一条保留旧招的读法。
    /// 整段报时站定不动、不开枪（官方 AI 暂停），是给玩家的反打窗口；报完恢复。
    ///
    /// 和其它 Boss 的读法不撞：折翎是一条直线的刀路，岛主们是落点 / 冲步 / 换位，噬风是固定风眼；
    /// 这里是同一圆心、内外交替的节拍。逃圈判据：近响「半径 ÷ 预警」、远响「环带半宽 ÷ 预警」都 ≤ MaxEscapeSpeed（SkyIslandChampionMovesGuard）。
    /// 结算走共用件：近响是官方爆炸（墙体挡得住）；远响的环带官方爆炸画不出来，命中判定自己算，
    /// 伤害仍在玩家脚下放一个小半径的官方爆炸（口径同悬根猎首的绊索），余波画到外圈。
    /// </summary>
    internal sealed class SkyIslandBellEngineMoves : MonoBehaviour
    {
        internal const float NearRadius = 6.5f;
        internal const float NearTelegraph = 1.4f;
        internal const float NearDamage = 20f;
        /// <summary>远响的内圈就是近响的圈：远响时钻进来安全，下一响近响再出去。</summary>
        internal const float FarInner = NearRadius;
        internal const float FarOuter = 20f;
        internal const float FarTelegraph = 1.4f;
        internal const float FarDamage = 16f;
        /// <summary>远响命中时在玩家脚下放的官方爆炸半径：只炸被罩住的人。</summary>
        internal const float FarHitRadius = 0.9f;
        internal const int Beats = 3;
        internal const int BeatsHurt = 5;
        /// <summary>两响之间的空当。</summary>
        internal const float BeatGap = 0.45f;
        internal const float BeatGapHurt = 0.3f;
        /// <summary>被钟声罩住的耳鸣减速（WalkSpeed / RunSpeed 的 PercentageAdd）。</summary>
        internal const float TollSlow = -0.3f;
        internal const float TollSlowSeconds = 2f;
        internal const float StrikeInterval = 11f;
        internal const float StrikeIntervalHurt = 8f;
        /// <summary>玩家在外圈以内才报时：再远钟声罩不到，报了也白报。</summary>
        internal const float StrikeRange = FarOuter;
        internal const float HurtBelow = 0.5f;
        private const float TickInterval = 0.25f;
        private const float FirstCastDelay = 5f;
        private const float SlowHeightTolerance = 2f;
        private static readonly Color NearTint = new Color(1f, 0.80f, 0.44f, 1f);
        private static readonly Color FarTint = new Color(0.98f, 0.62f, 0.36f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private readonly SkyIslandPlayerSlow slow = new SkyIslandPlayerSlow("SkyIslandBellToll");
        private readonly List<LineRenderer> rings = new List<LineRenderer>();
        private float nextTick, nextCastAt;
        private bool subscribed, finished, casting, hurt, firstAnnounced, farAnnounced;

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
                Debug.LogWarning("[SkyIslandBoss] 守钟装置 AI 暂停控制不可用，报时时不站定：" + e.Message);
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
                SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, NearTint);
                Announce("守钟装置的钟舌松了：报时变成五响，一响紧跟一响。",
                    "The bell engine's clapper works loose: it now strikes five times, each right on the heels of the last.", true);
            }
            if (!casting && Time.time >= nextCastAt) TryCast();
        }

        private void TryCast()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            if (FlatDistance(player.transform.position, boss.transform.position) > StrikeRange)
            {
                nextCastAt = Time.time + 1f;
                return;
            }
            nextCastAt = Time.time + (hurt ? StrikeIntervalHurt : StrikeInterval);
            StartCoroutine(StrikeRoutine());
        }

        private IEnumerator StrikeRoutine()
        {
            casting = true;
            PauseAi();
            Vector3 ground;
            Vector3 center = SkyIslandBossForge.SnapToGround(boss.transform.position, context, 0f, out ground) ? ground : boss.transform.position;
            if (!firstAnnounced)
            {
                firstAnnounced = true;
                Announce("守钟装置要报时了：近响罩它身边一圈，远响罩内外两圈之间。近响时出圈，远响时钻进内圈或退到外圈外；躲到墙后也行。",
                    "The bell engine is striking the hour. The near strike covers the ring around it; the far strike covers the band between the inner and outer rings. Leave the ring for a near strike; for a far strike, get inside the inner ring or past the outer one. A wall also blocks it.", true);
            }
            CharacterMainControl player = CharacterMainControl.Main;
            // 第一响挑罩得住玩家的那一种，之后近远交替。
            bool far = player != null && FlatDistance(player.transform.position, center) > NearRadius;
            int count = hurt ? BeatsHurt : Beats;
            for (int i = 0; i < count && !Aborted(); i++)
            {
                IEnumerator beat = Beat(center, far, i == 0 ? SkyIslandImpactFx.BossShake : SkyIslandImpactFx.BossShake * 0.6f);
                while (beat.MoveNext()) yield return beat.Current;
                far = !far;
                float gap = hurt ? BeatGapHurt : BeatGap;
                float waited = Time.time;
                while (i + 1 < count && Time.time - waited < gap && !Aborted()) yield return null;
            }
            ResumeAi();
            casting = false;
        }

        /// <summary>
        /// 一响：画圈 → 蓄力 → 结算 → 钟声 → 被罩住的主角耳鸣减速。
        /// 近响一道圈、圈内随蓄力长满；远响画内外两道边界圈（不铺填充，免得把安全的内圈也涂满），
        /// 再加一道从内圈荡到外圈的声浪圈当计时读数。
        /// </summary>
        private IEnumerator Beat(Vector3 center, bool far, float shake)
        {
            Color tint = far ? FarTint : NearTint;
            LineRenderer line = null, inner = null, wave = null;
            try
            {
                line = SkyIslandBossForge.CreateGroundRing(context.Root, center);
                rings.Add(line);
                if (far)
                {
                    inner = SkyIslandBossForge.CreateGroundRing(context.Root, center);
                    rings.Add(inner);
                    wave = SkyIslandBossForge.CreateGroundRing(context.Root, center);
                    rings.Add(wave);
                    SkyIslandGroundRing.SetShape(line, FarOuter, 0.18f, Fade(tint, 0.45f));
                    SkyIslandGroundRing.SetShape(inner, FarInner, 0.18f, Fade(tint, 0.45f));
                    SkyIslandGroundRing.SetShape(wave, FarInner, 0.3f, Fade(tint, 0.35f));
                }
                else SkyIslandBossForge.SetRing(line, NearRadius, 0f, tint);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 报时预警失败：" + e.Message); }
            // 圈没画出来就不敲：看不见的范围伤害比没有招式更糟。远响要内外两道圈都在，才读得出哪儿安全。
            if (line == null || (far && inner == null))
            {
                ReleaseLine(line);
                ReleaseLine(inner);
                ReleaseLine(wave);
                yield break;
            }
            if (far && !farAnnounced)
            {
                farAnnounced = true;
                Announce("远响：两道圈之间要挨钟声，钻进内圈或者退出外圈。",
                    "Far strike: the band between the two rings gets hit. Duck inside the inner ring or step out past the outer one.", true);
            }
            float telegraph = SkyIslandBossRules.TelegraphSeconds(far ? FarTelegraph : NearTelegraph, SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted())
            {
                float charge = Mathf.Clamp01((Time.time - started) / telegraph);
                if (far)
                {
                    Color solid = Fade(tint, Mathf.Lerp(0.45f, 1f, charge));
                    float width = Mathf.Lerp(0.18f, 0.55f, charge);
                    SkyIslandGroundRing.SetShape(line, FarOuter, width, solid);
                    SkyIslandGroundRing.SetShape(inner, FarInner, width, solid);
                    SkyIslandGroundRing.SetShape(wave, Mathf.Lerp(FarInner, FarOuter, charge), 0.3f, Fade(tint, Mathf.Lerp(0.35f, 0.8f, charge)));
                }
                else SkyIslandBossForge.SetRing(line, NearRadius, charge, tint);
                yield return null;
            }
            if (!Aborted())
            {
                try
                {
                    if (far) StrikeFar(center, shake);
                    else
                    {
                        SkyIslandBossForge.Detonate(boss, center, NearRadius, NearDamage, false, shake, NearTint);
                        if (Caught(center, 0f, NearRadius)) slow.Apply(CharacterMainControl.Main, TollSlow, TollSlowSeconds);
                    }
                    SkyIslandBossSfx.Play(context.Root, SkyIslandBossCue.Toll, center);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 报时结算失败：" + e.Message); }
            }
            ReleaseLine(line);
            ReleaseLine(inner);
            ReleaseLine(wave);
        }

        /// <summary>远响结算：环带里、没被墙挡住的主角在脚下吃一个小半径的官方爆炸并耳鸣；余波画到外圈。</summary>
        private void StrikeFar(Vector3 center, float shake)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (Caught(center, FarInner, FarOuter))
            {
                SkyIslandBossForge.Detonate(boss, player.transform.position, FarHitRadius, FarDamage, false, shake, FarTint);
                slow.Apply(player, TollSlow, TollSlowSeconds);
            }
            else if (shake > 0f) SkyIslandImpactFx.Shake(center, shake * 0.5f);
            SkyIslandImpactFx.Play(context.Root, center, FarOuter, FarTint, 0, true);
        }

        /// <summary>主角站在 [inner, outer] 的环带里（inner 为 0 即整个圆盘）、高度差不大、且与圆心之间没有墙。</summary>
        private static bool Caught(Vector3 center, float inner, float outer)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return false;
            Vector3 feet = player.transform.position;
            float distance = FlatDistance(feet, center);
            if (distance > outer || distance < inner || Mathf.Abs(feet.y - center.y) > SlowHeightTolerance) return false;
            // 墙体挡住钟声（口径同官方爆炸被岩壁挡下）：躲到墙后的人不挨。
            return !Physics.Linecast(center + Vector3.up * 1.2f, feet + Vector3.up * 1f,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Color Fade(Color tint, float alpha)
        {
            return new Color(tint.r, tint.g, tint.b, alpha);
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
