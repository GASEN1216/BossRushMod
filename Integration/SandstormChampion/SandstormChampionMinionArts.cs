// ============================================================================
// SandstormChampionMinionArts.cs - 沙暴棍卫随机使出星阙三档重击
// ============================================================================
// owner 2026-10-08：「召唤出的小弟也要随机使用这个武器的三个阶段来攻击玩家」。
//   一豆 · 流星横扫：前方大半圈，命中把玩家推开；
//   二豆 · 双龙回旋：原地两段整圈，第二段把玩家往里卷；
//   三豆 · 星陨天崩：朝玩家跃起砸落，落点一圈。
// 公平性：每招都有地面预警（敌方暖橙色，和玩家自己的金色区分开）与前摇，翻滚照常免疫，隔墙不打；
// 伤害走冠军控制器的 HurtPlayer（同一受击间隔、同一伤害倍率与激怒加成）。
// 表现复用星阙的分层表现（AstralStaffFx.WithRoot），挂在每个棍卫自己的瞬时根下，退场时自然淡出。
// 只有本场冠军显式召出的棍卫会创建本组件；普通 NPC、仓库与背包里的星阙不受影响（AGENTS §4.12）。
// ============================================================================

using System.Collections;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal sealed class SandstormChampionMinionArts
    {
        private static readonly Color Warning = new Color(1f, 0.5f, 0.2f, 0.32f);

        private readonly SandstormChampionMinionMarker _marker;
        private readonly CharacterMainControl _self;
        private readonly SandstormChampionController _controller;
        private readonly Transform _fxRoot;
        private float _nextArtAt;

        internal bool Running { get; private set; }

        internal SandstormChampionMinionArts(SandstormChampionMinionMarker marker, CharacterMainControl self,
            SandstormChampionController controller)
        {
            _marker = marker;
            _self = self;
            _controller = controller;
            _fxRoot = new GameObject("SandGuard_AstralFx").transform;
            _nextArtAt = Time.time + Random.Range(SandstormChampionConfig.MinionArtFirstDelayMin,
                SandstormChampionConfig.MinionArtFirstDelayMax);
        }

        /// <summary>退场：剩下的表现留 1.5 秒自然淡完再回收。</summary>
        internal void Release()
        {
            Running = false;
            if (_fxRoot != null) Object.Destroy(_fxRoot.gameObject, 1.5f);
        }

        /// <summary>按距离挑一招（只在可用招式里随机），能出招时启动协程并返回 true。</summary>
        internal bool TryStart(CharacterMainControl player, float distance, bool sight)
        {
            if (Running || !sight || Time.time < _nextArtAt || _controller == null || !_controller.IsFighting) return false;
            if (BossSkillDamageRules.IsDodging(player)) return false;
            bool sweep = distance <= SandstormChampionConfig.MinionSweepRadius - 0.3f;
            bool spin = distance <= SandstormChampionConfig.MinionSpinRadius - 0.4f;
            bool starfall = distance >= 2.2f && distance <= SandstormChampionConfig.MinionStarfallReach;
            float total = (sweep ? 45f : 0f) + (spin ? 35f : 0f) + (starfall ? 30f : 0f);
            if (total <= 0f) return false;
            float roll = Random.value * total;
            int tier;
            if (sweep && roll < 45f) tier = 1;
            else
            {
                if (sweep) roll -= 45f;
                if (spin && roll < 35f) tier = 2;
                else tier = starfall ? 3 : spin ? 2 : 1;
            }
            Running = true;
            _marker.StartCoroutine(Run(tier));
            return true;
        }

        private IEnumerator Run(int tier)
        {
            IEnumerator body = tier == 1 ? Sweep() : tier == 2 ? Spin() : Starfall();
            while (true)
            {
                bool moved = false;
                try { moved = body.MoveNext(); }
                catch (System.Exception e)
                {
                    ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "棍卫重击中止: " + e.Message);
                }
                if (!moved) break;
                yield return body.Current;
            }
            Running = false;
            _nextArtAt = Time.time + Random.Range(SandstormChampionConfig.MinionArtCooldownMin,
                SandstormChampionConfig.MinionArtCooldownMax);
        }

        private bool Alive
        {
            get
            {
                CharacterMainControl player = CharacterMainControl.Main;
                return _marker != null && _marker.IsCombatActive && _controller != null && _controller.IsFighting
                    && player != null && player.Health != null && !player.Health.IsDead;
            }
        }

        private Vector3 FacePlayer()
        {
            Vector3 dir = CharacterMainControl.Main.transform.position - _self.transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _self.transform.forward;
            if (_self.movementControl != null) _self.movementControl.ForceTurnTo(dir);
            _self.SetAimPoint(CharacterMainControl.Main.transform.position + Vector3.up * 0.7f);
            return dir;
        }

        private void Surge(float amount)
        {
            ItemAgent_MeleeWeapon melee = _self.GetMeleeWeapon();
            AstralStaffHandVisual hand = melee != null ? melee.GetComponent<AstralStaffHandVisual>() : null;
            if (hand != null) hand.SetSurge(amount);
        }

        private IEnumerator Wait(float seconds, bool track)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                if (!Alive) yield break;
                _self.SetForceMoveVelocity(Vector3.zero);
                if (track) FacePlayer();
                yield return null;
            }
        }

        // ---------------- 一豆 · 流星横扫 ----------------

        private IEnumerator Sweep()
        {
            Vector3 dir = FacePlayer();
            Vector3 feet = _self.transform.position;
            float radius = SandstormChampionConfig.MinionSweepRadius;
            float half = SandstormChampionConfig.MinionSweepHalfAngle;
            float windup = SandstormChampionConfig.MinionSweepWindup;
            Surge(0.6f);
            AstralStaffFx.WithRoot(_fxRoot, () =>
            {
                // 地面预警：扇形从一侧推到另一侧，推满即出手。
                AstralStaffRibbon.Play(feet + Vector3.up * 0.05f, dir, half * 2f, 0.4f, radius, Warning,
                    windup, 0f, 0.12f, 0.55f, 0f, null);
                AstralStaffSigil.Play(feet + Vector3.up * 0.06f, dir, 0.9f, 1, windup, 0.15f, _self.transform);
            });
            AstralStaffSound.PostSwing(_self, 1);
            yield return Wait(windup, false);
            if (!Alive) yield break;
            feet = _self.transform.position;
            AstralStaffFx.WithRoot(_fxRoot, () =>
                AstralStaffFx.PlaySweepArt(feet, dir, radius, half, AstralStaffConfig.Gold));
            Surge(0.8f);
            if (InSector(feet, dir, radius, half))
                Strike(SandstormChampionConfig.MinionSweepDamage, dir * SandstormChampionConfig.MinionArtPush, 1);
            yield return Wait(0.25f, false);
        }

        // ---------------- 二豆 · 双龙回旋 ----------------

        private IEnumerator Spin()
        {
            Vector3 dir = FacePlayer();
            Vector3 feet = _self.transform.position;
            float radius = SandstormChampionConfig.MinionSpinRadius;
            float windup = SandstormChampionConfig.MinionSpinWindup;
            Surge(0.7f);
            AstralStaffFx.WithRoot(_fxRoot, () =>
            {
                AstralStaffRibbon.Play(feet + Vector3.up * 0.05f, dir, 360f, radius - 0.35f, radius, Warning,
                    windup, 0f, 0.12f, 0.45f, 0f, null);
                AstralStaffSigil.Play(feet + Vector3.up * 0.06f, dir, 1.1f, 2, windup, 0.18f, _self.transform);
            });
            AstralStaffSound.PostSwing(_self, 2);
            yield return Wait(windup, true);
            for (int beat = 0; beat < 2; beat++)
            {
                if (!Alive) yield break;
                bool second = beat == 1;
                feet = _self.transform.position;
                if (second) AstralStaffSound.PostSwing(_self, 2, true);
                AstralStaffFx.WithRoot(_fxRoot, () =>
                    AstralStaffFx.PlaySpinArt(feet, dir, radius, second, _self.transform));
                Surge(0.9f);
                if (InSector(feet, dir, radius + (second ? 0.2f : 0f), 180f))
                {
                    Vector3 away = CharacterMainControl.Main.transform.position - feet;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : dir;
                    // 第一段推开、第二段卷近
                    Strike(SandstormChampionConfig.MinionSpinDamage,
                        away * (second ? -SandstormChampionConfig.MinionArtPush : SandstormChampionConfig.MinionArtPush * 0.6f), 2);
                }
                yield return Wait(second ? 0.3f : SandstormChampionConfig.MinionSpinBeat, false);
            }
        }

        // ---------------- 三豆 · 星陨天崩 ----------------

        private IEnumerator Starfall()
        {
            Vector3 dir = FacePlayer();
            Vector3 start = _self.transform.position;
            Vector3 player = CharacterMainControl.Main.transform.position;
            Vector3 flat = player - start;
            flat.y = 0f;
            float leap = Mathf.Clamp(flat.magnitude - 1.2f, 0f, SandstormChampionConfig.MinionStarfallLeap);
            float blastRadius = SandstormChampionConfig.MinionStarfallBlastRadius;
            Vector3 predicted = start + dir * (leap + 1.2f);
            float windup = SandstormChampionConfig.MinionStarfallWindup;
            float air = SandstormChampionConfig.MinionStarfallAir;
            Surge(1f);
            AstralStaffFx.WithRoot(_fxRoot, () =>
            {
                // 落点预警圈从外向内收拢，收进中心那一刻砸下。
                BossRushFxBurst landing = BossRushFxKit.Shockwave(Warning, blastRadius * 2.4f, 0.18f, windup + air, true);
                landing.Main = new Color(1f, 0.5f, 0.2f, 0.55f);
                AstralStaffFx.PlayBurst(predicted + Vector3.up * 0.06f, landing);
                AstralStaffRibbon.Play(predicted + Vector3.up * 0.05f, dir, 360f, blastRadius - 0.12f, blastRadius,
                    Warning, windup + air, 0f, 0.12f, 0.2f, 0f, null);
                AstralStaffSigil.Play(start + Vector3.up * 1.2f, dir, 1.1f, 3, windup + air, 0.2f, _self.transform, true);
            });
            yield return Wait(windup, false);
            if (!Alive) yield break;
            AstralStaffSound.PostSwing(_self, 3);
            float t = 0f;
            float speed = leap / Mathf.Max(0.05f, air);
            while (t < air)
            {
                if (!Alive) yield break;
                _self.SetForceMoveVelocity(dir * speed);
                t += Time.deltaTime;
                yield return null;
            }
            _self.SetForceMoveVelocity(Vector3.zero);
            if (!Alive) yield break;
            Vector3 origin = _self.transform.position;
            Vector3 blast = origin + dir * 1.2f;
            bool contact = false;
            Vector3 toPlayer = CharacterMainControl.Main.transform.position - blast;
            toPlayer.y = 0f;
            if (toPlayer.magnitude <= blastRadius && Mathf.Abs(CharacterMainControl.Main.transform.position.y - blast.y) <= 2f)
            {
                Vector3 away = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : dir;
                contact = Strike(SandstormChampionConfig.MinionStarfallDamage, away * SandstormChampionConfig.MinionArtPush * 1.3f, 3);
            }
            AstralStaffFx.WithRoot(_fxRoot, () =>
                AstralStaffFx.PlayStarfallImpact(origin, blast, dir, contact, blastRadius));
            SandstormChampionAssetManager.ShakeNear(blast, contact ? 0.3f : 0.14f, 14f);
            yield return Wait(0.35f, false);
        }

        // ---------------- 判定与伤害 ----------------

        private static bool InSector(Vector3 feet, Vector3 dir, float radius, float halfAngle)
        {
            Vector3 target = CharacterMainControl.Main.transform.position;
            if (Mathf.Abs(target.y - feet.y) > 2f) return false;
            Vector3 flat = target - feet;
            flat.y = 0f;
            float dist = flat.magnitude;
            return dist <= radius && (halfAngle >= 180f || dist < 0.8f || Vector3.Angle(flat, dir) <= halfAngle);
        }

        /// <summary>翻滚免疫、隔墙不打；命中时给接触表现、轻震与 0.14 秒的推 / 拉。</summary>
        private bool Strike(float damage, Vector3 push, int tier)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || BossSkillDamageRules.IsDodging(player)) return false;
            Vector3 from = _self.transform.position + Vector3.up * 0.9f;
            Vector3 to = player.transform.position + Vector3.up * 0.9f;
            int wall = GameplayDataSettings.Layers.wallLayerMask;
            if (wall != 0 && Physics.Linecast(from, to, wall, QueryTriggerInteraction.Ignore)) return false;
            _controller.HurtPlayer(damage, to);
            Vector3 dir = to - from;
            AstralStaffFx.WithRoot(_fxRoot, () => AstralStaffFx.PlayContact(to, dir, tier));
            SandstormChampionAssetManager.ShakeNear(to, 0.12f + 0.05f * tier, 3f);
            if (push.sqrMagnitude > 0.01f) _marker.StartCoroutine(PushPlayer(player, push));
            return true;
        }

        private static IEnumerator PushPlayer(CharacterMainControl player, Vector3 velocity)
        {
            float t = 0f;
            const float seconds = 0.14f;
            while (t < seconds && player != null && player.Health != null && !player.Health.IsDead)
            {
                if (!BossSkillDamageRules.IsDodging(player))
                {
                    try { player.SetForceMoveVelocity(velocity * (1f - t / seconds)); } catch { yield break; }
                }
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
