using System;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static partial class TidegateEnemyActors
    {
        // 仅给挖沙船一名原有淘沙帮成员两颗普通手雷；不克隆炸弹 Boss 的技能、属性或掉落。
        static partial void EquipGunner(CharacterMainControl character, TidegateTide tide, Func<bool> valid)
        {
            if (character == null || tide == null || valid == null || character.GetComponent<TidegateGunner>() != null) return;
            try
            {
                var item = ItemAssetsCollection.GetPrefab(TidegateGunner.GrenadeId);
                if (item == null || item.TypeID != TidegateGunner.GrenadeId || item.DisplayNameRaw != "Item_Grenade") return;
                var setting = item.GetComponent<ItemSetting_Skill>();
                var skill = setting == null ? null : setting.Skill as Skill_Grenade;
                var ai = character.GetComponentInChildren<AICharacterController>();
                if (ai == null || skill == null || !skill.createExplosion || skill.grenadePfb == null || skill.isLandmine) return;
                character.gameObject.AddComponent<TidegateGunner>().Bind(character, ai, skill, tide, valid);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Tidegate] 投掷手改守船：" + e.Message); }
        }
    }

    /// <summary>已被官方 AI 发现且无遮蔽才投。落点在说出口时钉死，落偏、警告消失、投手死亡时取消。</summary>
    internal sealed class TidegateGunner : MonoBehaviour
    {
        internal const int GrenadeId = 67;
        private const float Radius = 2.4f, Fuse = 2.2f, Cooldown = 20f;
        private static readonly Color Orange = new Color(1f, .48f, .12f);
        private CharacterMainControl owner;
        private AICharacterController ai;
        private Skill_Grenade skill;
        private TidegateTide tide;
        private Func<bool> valid;
        private Grenade grenade;
        private LineRenderer ring;
        private Vector3 mark;
        private float nextCheck, started, released, settled;
        internal bool Landed { get; private set; }
        internal int Throws { get; private set; }
        internal bool WarningActive { get { return ring != null && ring.enabled; } }

        internal void Bind(CharacterMainControl c, AICharacterController controller, Skill_Grenade weapon, TidegateTide localTide, Func<bool> sessionValid)
        {
            owner = c; ai = controller; skill = weapon; nextCheck = Time.time + 4f;
            tide = localTide; valid = sessionValid;
        }

        private void Update()
        {
            if (ring == null && grenade == null && Time.time < nextCheck) return;
            try
            {
                var player = CharacterMainControl.Main;
                if (owner == null || owner.Health == null || owner.Health.IsDead || player == null || player.Health == null ||
                    player.Health.IsDead || ai == null || skill == null || transform.parent == null ||
                    tide == null || valid == null || !valid())
                { enabled = false; return; }
                if (ring != null || grenade != null)
                {
                    if (!WarningActive) { Cancel(); return; }
                    if (Landed && Time.time > settled + Fuse + .25f) { Cancel(); return; }
                    if (released > 0f && !Landed && (grenade == null || Time.time - released > 3f)) { Cancel(); return; }
                    float charge = Landed ? Mathf.Clamp01((Time.time - settled) / Fuse) : .1f;
                    SkyIslandBossForge.SetRing(ring, Radius, charge, Orange);
                    if (released == 0f && Time.time - started >= .65f) Throw();
                    return;
                }
                if (Throws >= 2) { enabled = false; return; }
                nextCheck = Time.time + .5f;
                if (ai.searchedEnemy == null || ai.searchedEnemy != player.mainDamageReceiver || !InThrowRange(transform.position,player.transform.position)) return;
                Vector3 target = player.transform.position;
                RaycastHit hit;
                if (!Physics.Raycast(target + Vector3.up * .7f, Vector3.down, out hit, 1.6f, GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore)
                    || !hit.transform.IsChildOf(transform.parent) || tide.IsDeepAt(hit.point) || !ClearSight(hit.point)) return;
                mark = hit.point;
                ring = SkyIslandBossForge.CreateGroundRing(transform.parent, mark);
                SkyIslandBossForge.SetRing(ring, Radius, 0f, Orange);
                started = Time.time;
                // 预警圈自带方向音。气泡失效不影响圈与声音。
                try { DialogueBubblesManager.Show(L10n.T("看雷！", "Grenade out!"), transform, 2f, false, false, -1f, 1.2f).Forget(); }
                catch { }
            }
            catch (Exception e)
            {
                enabled = false;
                ModBehaviour.DevLog("[Tidegate] 投掷手停用：" + e.Message);
            }
        }

        internal static bool InThrowRange(Vector3 from, Vector3 to)
        {
            float squared = (from-to).sqrMagnitude;
            return squared >= 8f*8f && squared <= 20f*20f;
        }
        private bool ClearSight(Vector3 target)
        {
            return !Physics.Linecast(transform.position + Vector3.up * 1.15f, target + Vector3.up * .6f,
                GameplayDataSettings.Layers.wallLayerMask | GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore);
        }
        private void Throw()
        {
            if (tide.IsDeepAt(mark) || !ClearSight(mark) || !InThrowRange(transform.position,mark)) { Cancel(); return; }
            Vector3 origin = transform.position + Vector3.up * 1.15f;
            Vector3 forward = mark - origin; forward.y = 0f;
            origin += forward.normalized * .45f;
            grenade = UnityEngine.Object.Instantiate(skill.grenadePfb, origin, Quaternion.identity);
            grenade.transform.SetParent(transform.parent, true);
            // 初始为哑弹。只有同一个官方手雷在预告位置撞到本图地面，才启动引信；
            // 撞墙、弹上船舷、根本没落地都不会在圈外误伤。
            grenade.createExplosion = false;
            grenade.delayFromCollide = true; grenade.delayTime = float.MaxValue; grenade.isLandmine = false;
            grenade.damageRange = Radius;
            grenade.damageInfo = new DamageInfo(owner) { damageValue = Mathf.Min(25f, skill.damageInfo.damageValue) };
            grenade.explosionShakeStrength = Mathf.Min(.45f,skill.explosionShakeStrength);
            grenade.SetWeaponIdInfo(GrenadeId);
            grenade.gameObject.AddComponent<TidegateGunnerLanding>().Owner = this;
            grenade.Launch(origin, skill.CalculateVelocity(origin, mark + Vector3.up * .12f, 6.5f),owner,false);
            // 锁的是脚边地面。原地站着的人不该靠身体碰撞把手雷磕成哑弹；伤害仍由官方爆炸检测结算。
            Collider shell = grenade.GetComponent<Collider>();
            var player = CharacterMainControl.Main;
            if (shell != null && player != null)
            {
                foreach (Collider body in player.GetComponentsInChildren<Collider>(false))
                    if (body != null && body.enabled && !body.isTrigger) Physics.IgnoreCollision(shell, body, true);
            }
            released = Time.time; Throws++;
        }

        internal void Contact(Grenade landed, Collision collision)
        {
            if (grenade != landed || Landed || !WarningActive || collision == null) return;
            if (((1 << collision.gameObject.layer) & GameplayDataSettings.Layers.groundLayerMask.value) == 0 ||
                !collision.transform.IsChildOf(transform.parent) || (grenade.transform.position-mark).sqrMagnitude > 1f)
            { Cancel(); return; }
            grenade.StickTo(null);
            // 手雷模型的半径会让第一次触地稍早；只允许一米内接地，爆心随后严格对准可见地圈。
            grenade.transform.position = mark + Vector3.up * .12f;
            grenade.createExplosion = true; grenade.delayFromCollide = false; grenade.delayTime = Fuse;
            Landed = true; settled = Time.time;
        }
        private void Cancel()
        {
            if (grenade != null)
            {
                grenade.createExplosion = false;
                grenade.gameObject.SetActive(false); Destroy(grenade.gameObject);
            }
            if (ring != null) SkyIslandBossForge.ReleaseRing(ring);
            grenade = null; ring = null; Landed = false; released = 0f;
            nextCheck = Time.time + Cooldown;
        }
        private void OnDisable() { Cancel(); }
    }

    /// <summary>把官方物理碰撞交回发起投掷的角色；角色和弹体谁先销毁都不会留下新的伤害。</summary>
    internal sealed class TidegateGunnerLanding : MonoBehaviour
    {
        internal TidegateGunner Owner;
        private void OnCollisionEnter(Collision hit)
        {
            if (Owner != null && Owner.isActiveAndEnabled) Owner.Contact(GetComponent<Grenade>(), hit);
        }
    }
}
