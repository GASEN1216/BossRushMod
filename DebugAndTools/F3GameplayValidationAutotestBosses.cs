#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestBosses.cs - 全自动实机验收里与头目 / 岛主有关的动作（Dev 构建；由步骤表驱动）
// ============================================================================
// wait_boss（等 Boss 刷出）、boss_hurt / echo_hurt（F3GameplayValidationAutotestActions.cs 里的 AutotestBossHurt）与断言 boss_alive
// 共用这里的查找与描述。从动作库原样挪出（新文件 1200 行预算）。
// loot_boss（单独击杀一位头目 / 岛主并读它的官方尸体箱）也在这里；击杀与数箱子的两个小工具与 kill_nearby、loot 共用。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        #region 头目 / 岛主

        private const float AutotestBossSearchRadius = 120f;
        /// <summary>官方在倒下处 +0.1 m 建箱；Boss 被打死那一帧就建好，这个半径只是给落点取整留余量。</summary>
        private const float AutotestBossCrateRadius = 4f;

        /// <summary><c>wait_boss:种类:秒数[:optional]</c>：等附近刷出这类 Boss（storm 噬风 / foreman 残星匠首 / stargazer 瞭台观星手），metrics 记血量、身上装备与机制状态。</summary>
        private IEnumerator AutotestWaitBoss(F3AutotestStepRecord record, string[] args)
        {
            string kind = Arg(args, 0);
            float seconds = Mathf.Clamp(ArgFloat(args, 1, 15f), 1f, 120f);
            float until = Time.realtimeSinceStartup + seconds;
            CharacterMainControl boss = null;
            while (!ShouldAbort())
            {
                boss = FindAutotestBoss(kind, AutotestBossSearchRadius);
                if (boss != null || Time.realtimeSinceStartup >= until) break;
                yield return WaitAutotestReal(0.5f);
            }
            string label = "action:wait_boss:" + kind;
            if (boss != null) record.Assertions.Add(AutotestAssertion(label, "PASS", null, DescribeAutotestBoss(boss)));
            else if (Arg(args, 2) == "optional") record.Assertions.Add(AutotestAssertion(label, "SKIP", "boss_not_seen_this_raid", null));
            else AutotestFail(record, label, "boss_not_seen_within_" + seconds.ToString("F0", CultureInfo.InvariantCulture) + "s", null, true);
        }

        /// <summary>
        /// <c>loot_boss:种类:秒数</c>：只击杀这一位头目 / 岛主（与 kill_nearby 同一条官方 <c>Health.Hurt</c>，死亡、首杀与清场照常结算），
        /// 等官方尸体箱在它倒下的地方出现，读出内容（写进 LastLoot，后面的 crate_has 可以接着判），
        /// 再按 <see cref="F3AutotestJudges.JudgeBossDrop"/> 核对「配装即掉落」。演练 SKY_DRILL_BOSS_LOADOUT 不走死亡，
        /// 官方建箱前事件（<see cref="SkyIslandBossLoot"/>）的接线只有这里在实机证。随从留给后面的 kill_nearby，
        /// 击杀前已有的箱子按实例 id 排除，所以倒下处只认得出 Boss 自己那一个箱子。
        /// </summary>
        private IEnumerator AutotestLootBoss(F3AutotestStepRecord record, string[] args)
        {
            string kind = Arg(args, 0), label = "action:loot_boss:" + kind;
            float seconds = Mathf.Clamp(ArgFloat(args, 1, 6f), 1f, 30f);
            string gate;
            if (!AutotestWriteAllowed(out gate)) { AutotestFail(record, label, gate, null, true); yield break; }
            SkyIslandBossProfile profile = FindAutotestBossProfile(kind);
            CharacterMainControl boss = profile == null ? null : FindAutotestBoss(kind, AutotestBossSearchRadius);
            SkyIslandBossLoot loot = boss == null ? null : boss.GetComponent<SkyIslandBossLoot>();
            if (loot == null)
            {
                string missing = profile == null ? "no_boss_profile:" + kind : boss == null ? "boss_not_found:" + kind : "boss_loot_not_bound";
                AutotestFail(record, label, missing, DescribeAutotestBoss(boss), true);
                yield break;
            }
            var existing = new HashSet<int>();
            foreach (InteractableLootbox box in UnityEngine.Object.FindObjectsOfType<InteractableLootbox>())
                if (box != null) existing.Add(box.GetInstanceID());
            Vector3 deathPoint = boss.transform.position;
            SetAutotestInvincible(true);
            if (!HurtAutotestToDeath(boss, CharacterMainControl.Main))
            {
                AutotestFail(record, label, "boss_survived_kill", DescribeAutotestBoss(boss), true);
                yield break;
            }
            InteractableLootbox crate = null;
            float until = Time.realtimeSinceStartup + seconds;
            while (!ShouldAbort())
            {
                crate = NewAutotestLootboxNear(existing, deathPoint, AutotestBossCrateRadius);
                if (crate != null || Time.realtimeSinceStartup >= until) break;
                yield return WaitAutotestReal(0.2f);
            }
            string outcome = "outcome=" + loot.Outcome + ",chosen=" + loot.ChosenTypeId;
            if (crate == null) { AutotestFail(record, label, "corpse_crate_not_found", outcome, false); yield break; }
            string contents;
            Dictionary<int, int> counts = CountAutotestLootbox(crate, record, out contents);
            _autotest.LastLoot = counts;
            int[] gear = new int[profile.Gear.Length];
            for (int i = 0; i < gear.Length; i++) gear[i] = profile.Gear[i].TypeId;
            string metrics, reason;
            bool ok = F3AutotestJudges.JudgeBossDrop(counts, gear, loot.ChosenTypeId, loot.Outcome, profile.NoDropWeight <= 0, out metrics, out reason);
            record.Assertions.Add(AutotestAssertion(label, ok ? "PASS" : "FAIL", reason,
                metrics + ",crate=" + AutotestShort(crate.gameObject.name, 40) + ",contents=" + contents));
        }

        /// <summary>离主角最近、还活着的这类 Boss。控制组件挂在角色物体上（残星匠首 / 观星手由 SkyIslandBossForge 挂，噬风由遭遇挂）。</summary>
        private static CharacterMainControl FindAutotestBoss(string kind, float radius)
        {
            Component[] controllers;
            switch (kind)
            {
                case "storm": controllers = UnityEngine.Object.FindObjectsOfType<SkyIslandStormBoss>(); break;
                case "foreman": controllers = UnityEngine.Object.FindObjectsOfType<SkyIslandForemanBoss>(); break;
                case "stargazer": controllers = UnityEngine.Object.FindObjectsOfType<SkyIslandStargazerChief>(); break;
                default: return null;
            }
            CharacterMainControl player = CharacterMainControl.Main;
            CharacterMainControl best = null;
            float bestDistance = float.MaxValue;
            foreach (Component controller in controllers)
            {
                CharacterMainControl character = controller == null ? null : controller.GetComponent<CharacterMainControl>();
                if (character == null || character.Health == null || character.Health.IsDead) continue;
                float distance = player == null ? 0f : Vector3.Distance(player.transform.position, character.transform.position);
                if (distance > radius || distance >= bestDistance) continue;
                best = character;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>步骤表里的 Boss 种类对应的档案：专属装备表与「岛主必出一件」都从档案读，不在步骤表里抄 TypeID。噬风没有档案。</summary>
        private static SkyIslandBossProfile FindAutotestBossProfile(string kind)
        {
            SkyIslandBossKind wanted;
            if (kind == "foreman") wanted = SkyIslandBossKind.Foreman;
            else if (kind == "stargazer") wanted = SkyIslandBossKind.Stargazer;
            else return null;
            foreach (SkyIslandBossProfile profile in SkyIslandBossRules.Profiles)
                if (profile != null && profile.Kind == wanted && profile.Gear != null) return profile;
            return null;
        }

        /// <summary>以玩家为伤害来源走官方 Health.Hurt 打死一个角色：死亡事件、遭遇清场、委托记账与掉落照常结算（kill_nearby 与 loot_boss 共用）。</summary>
        private static bool HurtAutotestToDeath(CharacterMainControl character, CharacterMainControl player)
        {
            try
            {
                DamageInfo damage = new DamageInfo(player);
                damage.damageValue = character.Health.MaxHealth * 20f;
                damage.ignoreArmor = true;
                damage.toDamageReceiver = character.mainDamageReceiver;
                damage.damagePoint = character.transform.position;
                character.Health.SetInvincible(false);
                character.Health.Hurt(damage);
                return character.Health.IsDead;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 自动验收击杀失败: " + e.Message);
                return false;
            }
        }

        /// <summary>击杀之后才出现、离倒下处最近的官方箱子（击杀前已有的箱子按实例 id 排除，随从与之前清场留下的箱子不会被认错）。</summary>
        private static InteractableLootbox NewAutotestLootboxNear(HashSet<int> existing, Vector3 point, float radius)
        {
            InteractableLootbox best = null;
            float bestDistance = radius;
            foreach (InteractableLootbox box in UnityEngine.Object.FindObjectsOfType<InteractableLootbox>())
            {
                if (box == null || existing.Contains(box.GetInstanceID())) continue;
                float distance = Vector3.Distance(point, box.transform.position);
                if (distance > bestDistance) continue;
                best = box;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>数一个箱子里每种物品几个（可堆叠的按堆叠数）；只读不拿。loot 与 loot_boss 共用，读失败记进 notes。</summary>
        private static Dictionary<int, int> CountAutotestLootbox(InteractableLootbox box, F3AutotestStepRecord record, out string contents)
        {
            var counts = new Dictionary<int, int>();
            try
            {
                if (box != null && box.Inventory != null && box.Inventory.Content != null)
                {
                    foreach (Item item in box.Inventory.Content)
                    {
                        if (item == null) continue;
                        int units = item.Stackable ? Math.Max(1, item.StackCount) : 1, existing;
                        counts.TryGetValue(item.TypeID, out existing);
                        counts[item.TypeID] = existing + units;
                    }
                }
            }
            catch (Exception e) { record.Notes.Add("loot_read_threw:" + e.GetType().Name); }
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> pair in counts) parts.Add(pair.Key + "x" + pair.Value);
            contents = string.Join("+", parts.ToArray());
            return counts;
        }

        private static string DescribeAutotestBoss(CharacterMainControl boss)
        {
            if (boss == null) return "boss=none";
            var text = new StringBuilder("boss=" + AutotestShort(boss.gameObject.name, 40));
            Health health = boss.Health;
            if (health != null)
                text.Append(",health=").Append((health.CurrentHealth / Mathf.Max(1f, health.MaxHealth)).ToString("F2", CultureInfo.InvariantCulture));
            try
            {
                Item helm = boss.GetHelmatItem(), armor = boss.GetArmorItem();
                text.Append(",helm=").Append(helm == null ? 0 : helm.TypeID).Append(",armor=").Append(armor == null ? 0 : armor.TypeID);
            }
            catch (Exception e) { text.Append(",gear_read_threw=").Append(e.GetType().Name); }
            SkyIslandForemanBoss foreman = boss.GetComponent<SkyIslandForemanBoss>();
            if (foreman != null)
                text.Append(",phase=").Append(foreman.Phase).Append(",pylons=").Append(foreman.LivePylons)
                    .Append(",shield=").Append(foreman.ShieldActive).Append(",overheated=").Append(foreman.Overheated);
            SkyIslandStargazerChief chief = boss.GetComponent<SkyIslandStargazerChief>();
            if (chief != null) text.Append(",marking=").Append(chief.Marking).Append(",lens=").Append(chief.LensWorking);
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
                text.Append(",distance_m=").Append(Vector3.Distance(player.transform.position, boss.transform.position).ToString("F1", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        #endregion
    }
}
#endif
