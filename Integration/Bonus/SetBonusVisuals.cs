// ============================================================================
// SetBonusVisuals.cs - 冰霜/雷霆套装共享表现层与击杀采集辅助
// ============================================================================
// 模块说明：
//   为 FrostSetBonus / ThunderSetBonus 提供龙王套装同级的表现与公共判定：
//   - 双眼点光：复用 DragonSetBonus 的头骨查找（FindHeadTransform / cachedHeadTransform），
//     两种脉动模式（Breathe 慢呼吸 / Flicker 电闪）
//   - 元素爆发环 + 放射碎片：复用 DragonSetBonus_Dash 的程序化圆形精灵
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

        private sealed class SetEyeLightState
        {
            public GameObject Root;
            public Light Left;
            public Light Right;
            public Coroutine Pulse;
        }

        /// <summary>
        /// 在主角头骨上挂一对点光。挂点复用龙套装的查找与缓存；龙套装的场景回调会先把缓存清空，
        /// 因此过图后这里拿到的是新角色的头骨。
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
                state.Root = new GameObject("SetBonusEyeEffect");
                state.Root.transform.SetParent(head, false);
                state.Root.transform.localPosition = new Vector3(0f, 0.15f, 0.2f);
                state.Left = CreateSetEyeLight(state.Root.transform, new Vector3(-0.08f, 0f, 0f), color, baseIntensity);
                state.Right = CreateSetEyeLight(state.Root.transform, new Vector3(0.08f, 0f, 0f), color, baseIntensity);
                state.Pulse = StartCoroutine(SetEyePulseLoop(state, baseIntensity, mode));
                return state;
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] CreateSetEyeLights 出错: " + e.Message);
                return null;
            }
        }

        private static Light CreateSetEyeLight(Transform parent, Vector3 localPosition, Color color, float intensity)
        {
            GameObject eye = new GameObject("SetBonusEyeLight");
            eye.transform.SetParent(parent, false);
            eye.transform.localPosition = localPosition;

            Light light = eye.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 0.25f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        private IEnumerator SetEyePulseLoop(SetEyeLightState state, float baseIntensity, SetEyePulseMode mode)
        {
            float seed = UnityEngine.Random.value * 10f;
            while (state != null && state.Left != null && state.Right != null)
            {
                float t = Time.time + seed;
                float intensity;
                if (mode == SetEyePulseMode.Breathe)
                {
                    intensity = baseIntensity * (1f + 0.5f * Mathf.Sin(t * 2f));
                }
                else
                {
                    float flicker = Mathf.Abs(Mathf.Sin(t * 9f) * Mathf.Sin(t * 2.3f + 1f));
                    intensity = baseIntensity * (0.6f + 0.6f * flicker);
                    if (UnityEngine.Random.value < 0.04f)
                    {
                        intensity += baseIntensity;
                    }
                }

                state.Left.intensity = intensity;
                state.Right.intensity = intensity;
                yield return null;
            }
        }

        private void DestroySetEyeLights(ref SetEyeLightState state)
        {
            if (state == null) return;

            try
            {
                if (state.Pulse != null)
                {
                    StopCoroutine(state.Pulse);
                }
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
        /// 一次性元素爆发：平铺地面的圆环放大淡出 + 点光衰减 + 可选放射碎片。
        /// 精灵复用 DragonSetBonus_Dash.CreateSimpleCircleSprite()（静态缓存，零新贴图）。
        /// </summary>
        private void SpawnSetBurst(Vector3 position, Color color, float radius, float life, int shardCount)
        {
            try
            {
                Sprite sprite = CreateSimpleCircleSprite();

                GameObject ring = new GameObject("SetBonusBurst");
                ring.transform.position = position + Vector3.up * 0.3f;
                ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                float startScale = Mathf.Max(0.2f, radius * 0.4f);
                ring.transform.localScale = new Vector3(startScale, startScale, 1f);

                SpriteRenderer sr = ring.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = color;
                sr.sortingOrder = 100;

                Light light = ring.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(color.r, color.g, color.b);
                light.intensity = 4f;
                light.range = radius * 1.5f;
                light.shadows = LightShadows.None;

                StartCoroutine(FadeOutSetBurst(ring, sr, light, life, radius * 2f));

                if (shardCount > 0)
                {
                    SpawnSetBurstShards(position, color, sprite, shardCount, radius * 2.5f, life);
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonusVisuals] SpawnSetBurst 出错: " + e.Message);
            }
        }

        private void SpawnSetBurstShards(Vector3 origin, Color color, Sprite sprite, int count, float speed, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i + UnityEngine.Random.Range(-15f, 15f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                GameObject shard = new GameObject("SetBonusShard");
                shard.transform.position = origin + Vector3.up * 0.8f;
                shard.transform.localScale = new Vector3(0.25f, 0.6f, 1f);

                SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = color;
                sr.sortingOrder = 101;

                StartCoroutine(MoveAndFadeSetShard(shard, sr, direction * speed, life));
            }
        }

        private IEnumerator MoveAndFadeSetShard(GameObject shard, SpriteRenderer sr, Vector3 velocity, float life)
        {
            if (shard == null || sr == null) yield break;

            Color start = sr.color;
            float elapsed = 0f;
            while (elapsed < life && shard != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / life);
                shard.transform.position += velocity * (Time.deltaTime * (1f - t));
                sr.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, t));
                yield return null;
            }

            if (shard != null)
            {
                UnityEngine.Object.Destroy(shard);
            }
        }

        private IEnumerator FadeOutSetBurst(GameObject ring, SpriteRenderer sr, Light light, float duration, float endScale)
        {
            if (ring == null || sr == null) yield break;

            Color start = sr.color;
            float startIntensity = light != null ? light.intensity : 0f;
            float startScale = ring.transform.localScale.x;
            float elapsed = 0f;
            while (elapsed < duration && ring != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);   // ease-out：先快后慢地扩散
                float scale = Mathf.Lerp(startScale, endScale, eased);
                ring.transform.localScale = new Vector3(scale, scale, 1f);
                sr.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, t * t));
                if (light != null)
                {
                    light.intensity = Mathf.Lerp(startIntensity, 0f, t);
                }
                yield return null;
            }

            if (ring != null)
            {
                UnityEngine.Object.Destroy(ring);
            }
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
        /// 套装击杀触发的统一过滤（过滤序照 CodexKillCollector.OnGlobalDead：越便宜越靠前）。
        /// 只认「主角亲手击杀敌方角色」：排玩家自身死亡、Mode H 观战互换（官方会把 fromCharacter 改写成主角）、
        /// 遗种巢随从、友军（宠物/雇佣兵同为 Teams.player）、基地场景。
        /// </summary>
        private bool TryResolveSetBonusKillVictim(Health target, DamageInfo info, out CharacterMainControl victim, out Vector3 position)
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
            if (resolved.Team == Teams.player) return false;

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
        /// 扫描 center 周围 radius 内的存活敌人（Team.IsEnemy(Teams.player, …)），按距离升序最多保留 maxCount 个，
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
                if (!Team.IsEnemy(Teams.player, character.Team)) continue;

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
