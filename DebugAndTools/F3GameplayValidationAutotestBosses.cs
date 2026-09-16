#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestBosses.cs - 全自动实机验收里与头目 / 岛主有关的动作（Dev 构建；由步骤表驱动）
// ============================================================================
// wait_boss（等 Boss 刷出）、boss_hurt / echo_hurt（F3GameplayValidationAutotestActions.cs 里的 AutotestBossHurt）与断言 boss_alive
// 共用这里的查找与描述。从动作库原样挪出（新文件 1200 行预算）。
// loot_boss（单独击杀一位头目 / 岛主并读它的官方尸体箱）也在这里；击杀与数箱子的两个小工具与 kill_nearby、loot 共用。
// R2–R4（2026-09-15）：种类名覆盖全部档案（断风游猎按变体分三种），approach_boss（瞬移到 Boss 身边，给截信人的劫包用）、
// feed_shots（经听雨人的 Dev 钩子记枪数，替代真开枪）。
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

        /// <summary>
        /// <c>wait_boss:种类:秒数[:optional]</c>：等附近刷出这类 Boss（storm 噬风，其余是档案 id 的小写：foreman、stargazer、roothunter、waylayer、
        /// sickle、listener、piper、mirror、windhunter_chaser / windhunter_stalker / windhunter_warden），metrics 记血量、阵营、身上装备与机制状态。
        /// </summary>
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

        /// <summary>
        /// <c>approach_boss:种类:米</c>：瞬移到这位头目 / 岛主身边（从 Boss 指向主角原来那一侧，隔这么远）。走会话的「先核地面再瞬移」入口，
        /// 腾空与撤离读条照样清零。给截信人的劫包这种要贴身才出的招式用。只在失败时记一条。
        /// </summary>
        private IEnumerator AutotestApproachBoss(F3AutotestStepRecord record, string[] args)
        {
            string kind = Arg(args, 0), label = "action:approach_boss:" + kind;
            float distance = Mathf.Clamp(ArgFloat(args, 1, 1.8f), 0.5f, 20f);
            SkyIslandSession session = SkyIslandSessionOrNull();
            CharacterMainControl player = CharacterMainControl.Main;
            CharacterMainControl boss = FindAutotestBoss(kind, AutotestBossSearchRadius);
            if (session == null || player == null || boss == null)
            {
                AutotestFail(record, label, session == null ? "no_island_session" : player == null ? "player_missing" : "boss_not_found:" + kind, null, true);
                yield break;
            }
            CloseAutotestPanels();
            Vector3 away = player.transform.position - boss.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away = away.normalized * distance;
            string reason;
            if (!session.DevAutotestTeleport(boss.transform, away.x, away.z, out reason))
            {
                AutotestFail(record, label, reason, DescribeAutotestBoss(boss), true);
                yield break;
            }
            record.Notes.Add("approach_boss=" + kind + ",distance_m=" + distance.ToString("F1", CultureInfo.InvariantCulture));
            yield return WaitAutotestReal(0.3f);
        }

        /// <summary>
        /// <c>feed_shots:枪数|need</c>：给最近的听雨人记这么多枪（它的 Dev 钩子，只加计数；落不落石仍按主角在不在洞口一带判）。
        /// 写 <c>need</c> 就补到这一场真正要的枪数（<see cref="SkyIslandListenerChief.ShotsNeeded"/>）——玩家自己戴着静听耳罩时
        /// 要开的枪数翻倍（<see cref="SkyIslandBossRules.ShotsPerRockfallFor"/>），步骤表写死数字会跟不上规则
        /// （2026-09-16 第八轮 F3：规则改成 16 枪、步骤仍喂 8 枪，落石圈永远等不到）。
        /// </summary>
        private void AutotestFeedShots(F3AutotestStepRecord record, string[] args)
        {
            CharacterMainControl boss = FindAutotestBoss("listener", AutotestBossSearchRadius);
            SkyIslandListenerChief listener = boss == null ? null : boss.GetComponent<SkyIslandListenerChief>();
            if (listener == null) { AutotestFail(record, "action:feed_shots", "listener_not_found", null, true); return; }
            string raw = Arg(args, 0);
            int count = string.Equals(raw, "need", StringComparison.Ordinal)
                ? Mathf.Max(1, listener.ShotsNeeded - listener.Shots)
                : Mathf.Clamp(ArgInt(args, 0, 8), 1, 64);
            listener.DevFeedShots(count);
            record.Notes.Add("feed_shots=" + count + ",shots=" + listener.Shots + "/" + listener.ShotsNeeded);
        }

        /// <summary>
        /// <c>reset_encounter:遭遇id</c>：把这组自动遭遇复原成这一趟还没刷过，主角走近时整组照常刷出。头目步骤开头用——
        /// 前面的步骤清过这一组的话，自动组这一趟不会再刷，头目就永远等不到。只动本趟内存，不写剧情与存档。
        /// </summary>
        private void AutotestResetEncounter(F3AutotestStepRecord record, string[] args)
        {
            string id = Arg(args, 0), label = "action:reset_encounter:" + id, reason;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { AutotestFail(record, label, "no_island_session", null, true); return; }
            if (!session.DevAutotestResetEncounter(id, out reason)) { AutotestFail(record, label, reason, null, true); return; }
            record.Notes.Add(reason);
        }

        /// <summary>步骤表里的种类名对应的招式控制器类型。控制组件挂在角色物体上（头目 / 岛主由 SkyIslandBossForge 挂，噬风由遭遇挂）。</summary>
        private static Type AutotestBossControllerType(string kind)
        {
            switch (kind)
            {
                case "storm": return typeof(SkyIslandStormBoss);
                case "foreman": return typeof(SkyIslandForemanBoss);
                case "stargazer": return typeof(SkyIslandStargazerChief);
                case "roothunter": return typeof(SkyIslandRootHunterBoss);
                case "waylayer": return typeof(SkyIslandWaylayerChief);
                case "sickle": return typeof(SkyIslandSickleBoss);
                case "listener": return typeof(SkyIslandListenerChief);
                case "piper": return typeof(SkyIslandPiperChief);
                case "mirror": return typeof(SkyIslandMirrorChief);
                case "windhunter_chaser":
                case "windhunter_stalker":
                case "windhunter_warden":
                    return typeof(SkyIslandWindhunterChief);
                default: return null;
            }
        }

        /// <summary>断风游猎三种共用一个控制器：按档案变体号区分；其余种类为 0。</summary>
        private static int AutotestBossVariant(string kind)
        {
            switch (kind)
            {
                case "windhunter_chaser": return SkyIslandBossRules.WindhunterChaser;
                case "windhunter_stalker": return SkyIslandBossRules.WindhunterStalker;
                case "windhunter_warden": return SkyIslandBossRules.WindhunterWarden;
                default: return 0;
            }
        }

        /// <summary>离主角最近、还活着的这类 Boss。</summary>
        private static CharacterMainControl FindAutotestBoss(string kind, float radius)
        {
            Type type = AutotestBossControllerType(kind);
            if (type == null) return null;
            int variant = AutotestBossVariant(kind);
            CharacterMainControl player = CharacterMainControl.Main;
            CharacterMainControl best = null;
            float bestDistance = float.MaxValue;
            foreach (UnityEngine.Object found in UnityEngine.Object.FindObjectsOfType(type))
            {
                Component controller = found as Component;
                if (controller == null) continue;
                if (variant > 0)
                {
                    SkyIslandWindhunterChief ranger = controller as SkyIslandWindhunterChief;
                    if (ranger == null || ranger.Variant != variant) continue;
                }
                CharacterMainControl character = controller.GetComponent<CharacterMainControl>();
                if (character == null || character.Health == null || character.Health.IsDead) continue;
                float distance = player == null ? 0f : Vector3.Distance(player.transform.position, character.transform.position);
                if (distance > radius || distance >= bestDistance) continue;
                best = character;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>步骤表里的 Boss 种类对应的档案（种类名去掉下划线即档案 id）：专属装备表与「岛主必出一件」都从档案读，不在步骤表里抄 TypeID。噬风没有档案。</summary>
        private static SkyIslandBossProfile FindAutotestBossProfile(string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind == "storm") return null;
            SkyIslandBossProfile profile = SkyIslandBossRules.FindById(kind.Replace("_", string.Empty));
            return profile != null && profile.Gear != null ? profile : null;
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
            text.Append(",team=").Append(boss.Team);
            try
            {
                Item helm = boss.GetHelmatItem(), armor = boss.GetArmorItem();
                Item pack = SkyIslandBossProps.WornIn(boss, "Backpack"), mask = SkyIslandBossProps.WornIn(boss, "FaceMask"),
                    headset = SkyIslandBossProps.WornIn(boss, "Headset");
                text.Append(",helm=").Append(helm == null ? 0 : helm.TypeID).Append(",armor=").Append(armor == null ? 0 : armor.TypeID)
                    .Append(",pack=").Append(pack == null ? 0 : pack.TypeID).Append(",mask=").Append(mask == null ? 0 : mask.TypeID)
                    .Append(",headset=").Append(headset == null ? 0 : headset.TypeID);
            }
            catch (Exception e) { text.Append(",gear_read_threw=").Append(e.GetType().Name); }
            SkyIslandForemanBoss foreman = boss.GetComponent<SkyIslandForemanBoss>();
            if (foreman != null)
                text.Append(",phase=").Append(foreman.Phase).Append(",pylons=").Append(foreman.LivePylons)
                    .Append(",shield=").Append(foreman.ShieldActive).Append(",overheated=").Append(foreman.Overheated);
            SkyIslandStargazerChief chief = boss.GetComponent<SkyIslandStargazerChief>();
            if (chief != null) text.Append(",marking=").Append(chief.Marking).Append(",lens=").Append(chief.LensWorking);
            SkyIslandRootHunterBoss hunter = boss.GetComponent<SkyIslandRootHunterBoss>();
            if (hunter != null)
                text.Append(",phase=").Append(hunter.Phase).Append(",snare_armed=").Append(hunter.SnareArmed)
                    .Append(",stakes=").Append(hunter.LiveStakes).Append(",ambushing=").Append(hunter.Ambushing);
            SkyIslandWaylayerChief waylayer = boss.GetComponent<SkyIslandWaylayerChief>();
            if (waylayer != null)
                text.Append(",snatching=").Append(waylayer.Snatching).Append(",snatches=").Append(waylayer.Snatches)
                    .Append(",stolen_held=").Append(waylayer.StolenHeld).Append(",fleeing=").Append(waylayer.Fleeing);
            SkyIslandSickleBoss sickle = boss.GetComponent<SkyIslandSickleBoss>();
            if (sickle != null)
                text.Append(",phase=").Append(sickle.Phase).Append(",mud=").Append(sickle.LiveMudPatches)
                    .Append(",sweeping=").Append(sickle.Sweeping).Append(",helpers=").Append(sickle.HelpersCalled);
            SkyIslandListenerChief listener = boss.GetComponent<SkyIslandListenerChief>();
            if (listener != null)
                text.Append(",shots=").Append(listener.Shots).Append("/").Append(listener.ShotsNeeded)
                    .Append(",dropping=").Append(listener.Dropping).Append(",earmuffs=").Append(listener.EarmuffsWorking);
            SkyIslandPiperChief piper = boss.GetComponent<SkyIslandPiperChief>();
            if (piper != null)
                text.Append(",channeling=").Append(piper.Channeling).Append(",flutes=").Append(piper.FluteCount)
                    .Append(",lured=").Append(piper.LastLured).Append(",mask_working=").Append(piper.MaskWorking);
            SkyIslandMirrorChief mirror = boss.GetComponent<SkyIslandMirrorChief>();
            if (mirror != null)
                text.Append(",swapping=").Append(mirror.Swapping).Append(",decoy=").Append(mirror.DecoyAlive)
                    .Append(",staggered=").Append(mirror.Staggered).Append(",plate=").Append(mirror.PlateWorking);
            SkyIslandWindhunterChief ranger = boss.GetComponent<SkyIslandWindhunterChief>();
            if (ranger != null)
                // 冲步没起来时要能一眼分清原因（贴太近 / 离太远 / 落点吸不到地），别再靠翻 Player.log 猜。
                text.Append(",variant=").Append(ranger.Variant).Append(",lunging=").Append(ranger.Lunging)
                    .Append(",lunges=").Append(ranger.LungeCount).Append(",piece=").Append(ranger.PieceWorking)
                    .Append(",block=near:").Append(ranger.BlockedNear).Append("/far:").Append(ranger.BlockedFar)
                    .Append("/land:").Append(ranger.BlockedLanding)
                    .Append(",disengage=").Append(ranger.Disengages).Append("/").Append(ranger.BlockedDisengage)
                    .Append(",range_m=").Append(ranger.LastRange.ToString("F1", CultureInfo.InvariantCulture));
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
                text.Append(",distance_m=").Append(Vector3.Distance(player.transform.position, boss.transform.position).ToString("F1", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        #endregion
    }
}
#endif
