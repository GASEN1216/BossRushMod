#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestBosses.cs - 全自动实机验收里与头目 / 岛主有关的动作（Dev 构建；由步骤表驱动）
// ============================================================================
// wait_boss（等 Boss 刷出）、boss_hurt / echo_hurt（F3GameplayValidationAutotestActions.cs 里的 AutotestBossHurt）与断言 boss_alive
// 共用这里的查找与描述。从动作库原样挪出（新文件 1200 行预算）。
// ============================================================================

using System;
using System.Collections;
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
