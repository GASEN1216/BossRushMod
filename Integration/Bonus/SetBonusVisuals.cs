// ============================================================================
// SetBonusVisuals.cs - 冰霜/雷霆套装共享表现层与击杀采集辅助
// ============================================================================
// 模块说明：
//   为 FrostSetBonus / ThunderSetBonus 提供龙王套装同级的表现与公共判定：
//   - 眼光：复用 DragonSetBonus 的头骨查找（FindHeadTransform / cachedHeadTransform），
//     表现在 SetBonusFx.cs（SetBonusEyeGlow），两种脉动模式（Breathe 慢呼吸 / Flicker 电闪）
//   - 元素爆发环 + 放射碎片：转调 NewWeaponFx.PlayBurst（与新武器同一份实现）
//   - 折线电弧：委托 Common/Effects/SetBonusArcPool（独立 MonoBehaviour 池，惰性建、停用即销毁）
//   - 击杀过滤（照 CodexKillCollector.OnGlobalDead 的过滤序）与敌人扫描（照 PlayerLavaZone）
//   - 音效路径常量（照 DragonKingConfig.SoundBasePath）
//
// 设计约束（AGENTS.md 4.12）：
//   所有对象都由套装 Activate 时惰性创建、Deactivate 时销毁；不穿套装时零对象、零订阅、零每帧工作。
//   击杀/受击回调里只做过滤与调度，重活（扫描、结算、特效）都放到下一帧协程里做。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 套装音效文件路径。文件由 tools/gen_setbonus_sfx.py 生成到 Assets/Sounds/SetBonus/，
    /// 由 compile_official.bat 随构建部署；缺文件时 PlaySoundEffect 静默返回，不影响玩法。
    /// </summary>
    internal static class SetBonusSfx
    {
        public static readonly string BasePath = Path.Combine(
            Path.GetDirectoryName(typeof(SetBonusSfx).Assembly.Location),
            "Assets", "Sounds", "SetBonus");

        public static readonly string ThunderChain = Path.Combine(BasePath, "thunder_chain.wav");
        public static readonly string ThunderCounter = Path.Combine(BasePath, "thunder_counter.wav");
        public static readonly string FrostNova = Path.Combine(BasePath, "frost_nova.wav");
        public static readonly string FrostCounter = Path.Combine(BasePath, "frost_counter.wav");
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 眼光

        internal enum SetEyePulseMode
        {
            /// <summary>慢呼吸（冰霜）</summary>
            Breathe,
            /// <summary>电闪式抖动 + 偶发跳变（雷霆）</summary>
            Flicker
        }

        /// <summary>眼光句柄。表现（两颗面朝镜头的 HDR 亮点 + 一盏弱补光）在 SetBonusFx.cs 的 SetBonusEyeGlow。</summary>
        private sealed class SetEyeLightState
        {
            public GameObject Root;
        }

        /// <summary>
        /// 在主角头骨上挂一对眼光。挂点复用龙套装的查找与缓存；龙套装的场景回调会先把缓存清空，
        /// 因此过图后这里拿到的是新角色的头骨。baseIntensity 沿用旧常量，折成补光的 0.3 倍（约 1.5–1.8）。
        /// </summary>
        private SetEyeLightState CreateSetEyeLights(CharacterMainControl character, Color color, float baseIntensity, SetEyePulseMode mode)
        {
            if (character == null) return null;

            try
            {
                Transform head = cachedHeadTransform;
                if (head == null)
                {
                    head = FindHeadTransform(character);
                    cachedHeadTransform = head;
                }
                if (head == null)
                {
                    head = character.transform;
                }

                SetEyeLightState state = new SetEyeLightState();
                state.Root = SetBonusEyeGlow.Create(head, color, baseIntensity * 0.3f, mode == SetEyePulseMode.Flicker);
                return state;
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] CreateSetEyeLights 出错: " + e.Message);
                return null;
            }
        }

        private void DestroySetEyeLights(ref SetEyeLightState state)
        {
            if (state == null) return;

            try
            {
                if (state.Root != null)
                {
                    UnityEngine.Object.Destroy(state.Root);
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] DestroySetEyeLights 出错: " + e.Message);
            }

            state = null;
        }

        #endregion

        #region 爆发环与碎片

        /// <summary>
        /// 一次性元素爆发：转调 NewWeaponFx.PlayBurst（贴地空心 HDR 环 + 亮芯 + 粒子碎片），与新武器共用一份实现。
        /// 2026-09-23（VA-03）：旧版是实心圆饼放大 + 每次一盏强度 4 的点光；霜噬 / 雷噬每 1–1.4 秒一次，
        /// 地面一闪一闪。现在默认不开灯，只有冻结反击传 withLight（雷霆反震已有官方 flash 爆炸）。
        /// </summary>
        private void SpawnSetBurst(Vector3 position, Color color, float radius, float life, int shardCount, bool withLight = false)
        {
            NewWeaponFx.PlayBurst(position, color, radius, life, shardCount, withLight);
        }

        #endregion

        #region 电弧（委托 Common/Effects/SetBonusArcPool）

        // 池体本身是独立 MonoBehaviour：那段只跟 LineRenderer / 材质 / 自身协程打交道，
        // 不读任何模式状态，留在 ModBehaviour 上只会继续堆高宿主职责。
        private BossRush.Common.Effects.SetBonusArcPool setArcPool;

        /// <summary>从 from 到 to 画一道折线电弧（惰性建池；池满时丢弃这一道纯视觉）。</summary>
        private void SpawnSetArc(Vector3 from, Vector3 to, Color color, float width, float life)
        {
            try
            {
                if (setArcPool == null)
                {
                    setArcPool = BossRush.Common.Effects.SetBonusArcPool.Create();
                }
                setArcPool.Spawn(from, to, color, width, life);
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] SpawnSetArc 出错: " + e.Message);
            }
        }

        /// <summary>套装停用 / Mod 卸载时销毁整个电弧池（材质在池的 OnDestroy 里释放）。</summary>
        private void DestroySetArcPool()
        {
            if (setArcPool == null) return;

            try
            {
                UnityEngine.Object.Destroy(setArcPool.gameObject);
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] DestroySetArcPool 出错: " + e.Message);
            }
            setArcPool = null;
        }

        #endregion

        #region 击杀过滤与敌人扫描

        private Collider[] setBonusScanBuffer;
        private int setBonusCharacterLayerMask = -1;
        private readonly List<Health> setBonusScanResults = new List<Health>(8);

        /// <summary>
        /// 套装触发的统一过滤（过滤序照 CodexKillCollector.OnGlobalDead：越便宜越靠前）。
        /// 只认「主角亲手打到敌方角色」：排玩家自身、Mode H 观战互换（官方会把 fromCharacter 改写成主角）、
        /// 遗种巢随从、按玩家当前阵营判定的友军、基地场景。
        /// 霜噬 / 雷噬（普攻附带）与受击反制共用这一份判据，不另起第二套过滤。
        /// </summary>
        private bool TryResolveSetBonusEnemyTarget(Health target, DamageInfo info, out CharacterMainControl victim, out Vector3 position)
        {
            victim = null;
            position = Vector3.zero;

            if (target == null) return false;
            if (target.IsMainCharacterHealth) return false;
            if (IsModeHRunInProgressSafe()) return false;
            if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return false;
            if (PetNestCompanionAgent.IsCompanionHealth(target)) return false;

            CharacterMainControl resolved = target.TryGetCharacter();
            if (resolved == null) return false;
            if (!Team.IsEnemy(info.fromCharacter.Team, resolved.Team)) return false;

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel) return false;
            }
            catch (Exception)
            {
                return false;
            }

            victim = resolved;
            position = resolved.transform != null ? resolved.transform.position : info.damagePoint;
            return true;
        }

        /// <summary>
        /// 扫描 center 周围 radius 内、与玩家当前阵营敌对的存活角色，按距离升序最多保留 maxCount 个，
        /// 结果写入 setBonusScanResults（复用缓冲与列表，零分配）。形态照 PlayerLavaZone.DamageEnemiesInRange。
        ///
        /// 调用方在拿到结果后**不得 yield**：整张 setBonusScanResults 是冰霜/雷霆共用的复用列表，
        /// 中途让出会被另一条协程覆写。当前三个调用点都是「扫描 → 立即结算」，无 yield。
        /// </summary>
        /// <param name="excludeHealths">本次链/爆已命中过的目标，传 null 表示不排除（用于连锁去重）</param>
        private int ScanSetBonusEnemies(Vector3 center, float radius, CharacterMainControl exclude, int maxCount,
            List<Health> excludeHealths = null)
        {
            setBonusScanResults.Clear();
            if (maxCount <= 0) return 0;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return 0;

            if (setBonusScanBuffer == null)
            {
                setBonusScanBuffer = new Collider[24];
            }
            if (setBonusCharacterLayerMask == -1)
            {
                setBonusCharacterLayerMask = LayerMask.GetMask("Character");
                if (setBonusCharacterLayerMask == 0)
                {
                    setBonusCharacterLayerMask = ~0;
                }
            }

            int hits = Physics.OverlapSphereNonAlloc(center, radius, setBonusScanBuffer, setBonusCharacterLayerMask);
            for (int i = 0; i < hits; i++)
            {
                Collider col = setBonusScanBuffer[i];
                if (col == null) continue;

                CharacterMainControl character = col.GetComponentInParent<CharacterMainControl>();
                if (character == null || object.ReferenceEquals(character, exclude)) continue;

                Health health = character.Health;
                if (health == null || health.IsDead) continue;
                if (!Team.IsEnemy(player.Team, character.Team)) continue;

                if (excludeHealths != null)
                {
                    bool alreadyHit = false;
                    for (int k = 0; k < excludeHealths.Count; k++)
                    {
                        if (object.ReferenceEquals(excludeHealths[k], health))
                        {
                            alreadyHit = true;
                            break;
                        }
                    }
                    if (alreadyHit) continue;
                }

                bool duplicate = false;
                for (int k = 0; k < setBonusScanResults.Count; k++)
                {
                    if (object.ReferenceEquals(setBonusScanResults[k], health))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;

                float distanceSqr = (character.transform.position - center).sqrMagnitude;
                int insertAt = setBonusScanResults.Count;
                for (int k = 0; k < setBonusScanResults.Count; k++)
                {
                    if (distanceSqr < (setBonusScanResults[k].transform.position - center).sqrMagnitude)
                    {
                        insertAt = k;
                        break;
                    }
                }
                setBonusScanResults.Insert(insertAt, health);
                if (setBonusScanResults.Count > maxCount)
                {
                    setBonusScanResults.RemoveAt(setBonusScanResults.Count - 1);
                }
            }

            return setBonusScanResults.Count;
        }

        /// <summary>
        /// OnHurt 只消费本次官方伤害循环采集的贡献；不能在此时重新读取已被耐久破损改变的护甲。
        /// </summary>
        private static float GetSetBonusElementDamagePortion(Health health, DamageInfo damageInfo, ElementTypes element)
        {
            return SetBonusDamageObservation.GetElementDamagePortion(health, damageInfo, element);
        }

        internal bool HasSetBonusElementHealing { get { return frostSetActive || thunderSetActive; } }

        /// <summary>
        /// 套装激活代数。每次停用递增，供已经排队的延时结算协程识别「我这条属于上一次激活」。
        /// 场景重载走的是「先停用再重查」，同一帧里 xxxSetActive 会先 false 再 true，
        /// 光看这个布尔挡不住上一张图排队的连锁/霜爆拿着旧坐标在新场景里结算。
        /// </summary>
        private int setBonusGeneration = 0;

        private void BumpSetBonusGeneration()
        {
            unchecked { setBonusGeneration++; }
        }

        #endregion
    }
}
