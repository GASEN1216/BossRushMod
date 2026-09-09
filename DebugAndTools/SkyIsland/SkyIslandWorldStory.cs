using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>装置、居民对白和实体剧情反馈；必要动作在独立装置上始终可达。</summary>
    internal sealed class SkyIslandWorldStory : IDisposable
    {
        private readonly SkyIslandSession session;
        private readonly SkyIslandStoryService story;
        private readonly SkyIslandStoryPresentation presentation = new SkyIslandStoryPresentation();
        private readonly List<GameObject> feedback = new List<GameObject>();
        private readonly GameObject root;
        private int displayedFlags = -1;
        internal bool Visible { get { return presentation.Visible; } }
        internal SkyIslandWorldStory(SkyIslandSession session, SkyIslandStoryService story, GameObject root)
        { this.session = session; this.story = story; this.root = root; }

        internal static string PointName(string key)
        {
            switch (key)
            {
                case "Search_A": return "登云码头 · 渡口整备";
                case "Search_D": return "校准风标 · 开启林边回程路";
                case "Search_G": return "修复星灯 · 开启检修廊";
                case "Search_E": return "双航标门 · 中轴旧桥与风眼";
                case "Search_H": return "归航钟 · 钟守留言";
                case "Search_B": return "风铃集留言板 · 种植记录与航务委托";
                case "Search_F": return "旧航路守卫 · 折翎";
                case "Search_S1": return "晴禾的种植记录";
                case "Search_S2": return "风没有送到的信";
                case "Search_S3": return "听雨洞的旧航路图";
                case "Search_S4": return "修复观星镜";
                default: return "阅读群岛见闻";
            }
        }

        internal void ReadPoint(string key, Action recorded)
        {
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice("收录见闻 / 物证", delegate
            {
                if (key == "Search_S4" && !session.IsEncounterCleared("S4"))
                    return "先清除瞭台上的守卫，再静下心校准观星镜。";
                string message;
                if (story.RecordSearch(key, out message) && recorded != null) recorded();
                return message;
            }));
            switch (key)
            {
                case "Search_D": Add(choices, "校准西侧风标", SkyIslandStoryAction.RepairWindBeacon);
                    Add(choices, "系牢林边旧运菜道 K1", SkyIslandStoryAction.OpenShortcutK1); break;
                case "Search_G": Add(choices, "修复东侧星灯", SkyIslandStoryAction.RepairStarLamp);
                    Add(choices, "打开工坊检修廊 K2", SkyIslandStoryAction.OpenShortcutK2); break;
                case "Search_E":
                    Add(choices, "开启中轴旧桥 K3", SkyIslandStoryAction.OpenShortcutK3);
                    StormChoice(choices); break;
                case "Search_H": BellChoices(choices); break;
                case "Search_B":
                    Add(choices, "把种植记录留给晴禾", SkyIslandStoryAction.DeliverPlantingRecord);
                    // 委托板与苇白本人等价：她婚后离岛或尚未生成时，委托仍然可接可交。
                    BountyChoices(choices, () => BoardPosition("Search_B")); break;
                case "Search_A": ServiceChoice(choices, "渡口整备 · 修补随身装备", Repair); break;
                case "Search_F": ZhelingChoices(choices); break;
            }
            presentation.Show(PointName(key), Lore(key) + "\n\n" + story.CurrentObjective, choices);
        }

        internal void Talk(string id, Transform speaker)
        {
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            if (id == "sky_qinghe")
            {
                Add(choices, "交还种植记录", SkyIslandStoryAction.DeliverPlantingRecord);
                ServiceChoice(choices, "讨一份归航菜（本次出击生效）", Meal);
            }
            else if (id == "sky_zheling") ZhelingChoices(choices);
            else if (id == "sky_bellkeeper") BellChoices(choices);
            else if (id == "sky_weibai")
            {
                BountyChoices(choices, delegate { return speaker != null ? speaker.position : BoardPosition("Search_B"); });
                choices.Add(new SkyIslandStoryPresentation.Choice("查阅航标与支线记录", () => story.Summary));
            }
            else if (id == "sky_fuzhou") ServiceChoice(choices, "渡口整备 · 修补随身装备", Repair);
            else if (id == "sky_miantai") ServiceChoice(choices, "请眠苔敷一副苔药", Heal);
            presentation.Show("晴岚群岛 · " + ResidentName(id), story.DescribeNpc(id), choices);
        }

        private static string ResidentName(string id)
        {
            switch (id)
            {
                case "sky_qinghe": return "晴禾"; case "sky_weibai": return "苇白";
                case "sky_fuzhou": return "浮舟"; case "sky_miantai": return "眠苔";
                case "sky_zheling": return "折翎"; case "sky_bellkeeper": return "无声钟守";
                default: return "群岛居民";
            }
        }
        /// <summary>服务类选项统一在这里做会话有效性检查，服务 owner 自己负责价格、冷却与失败原因。</summary>
        private void ServiceChoice(List<SkyIslandStoryPresentation.Choice> choices, string label, Func<string> action)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if (!session.IsReady) return "请等待群岛就绪。";
                return action();
            }));
        }

        private string Repair()
        {
            SkyIslandServices services = session.Services;
            return services == null ? "渡口暂时没人。" : services.Repair();
        }

        private string Heal()
        {
            SkyIslandServices services = session.Services;
            return services == null ? "眠苔不在。" : services.Heal();
        }

        private string Meal()
        {
            SkyIslandServices services = session.Services;
            return services == null ? "菜畦还没开张。" : services.Meal(session.HasPlantingDelivered);
        }

        /// <summary>
        /// 苇白的派单面板。
        ///
        /// 两条硬规则：
        /// 1. **只派做得完的单**。清场与巡岛的计数源都是持久存档事实，老档上可能一件都不剩；
        ///    用 `session.AvailableBountyProgress` 与本轮目标比对后再决定是否挂出来。
        /// 2. **永远给退单出口**。哪怕门控有疏漏（比如接单后搜刮点建箱失败），
        ///    玩家也能自己退掉，不会被一张做不完的单把本局委托槽卡死。
        /// </summary>
        private void BountyChoices(List<SkyIslandStoryPresentation.Choice> choices, Func<Vector3> rewardPosition)
        {
            SkyIslandBounty contract = session.Bounty;
            if (contract == null) return;
            if (!contract.HasActive)
            {
                if (!contract.CanAcceptMore)
                {
                    choices.Add(new SkyIslandStoryPresentation.Choice(
                        "航务委托 · 今日已派完", delegate
                        {
                            return "苇白：今天的活都派完啦（" + contract.CompletedRounds + "/" +
                                SkyIslandBounty.MaxRounds + "），剩下的留给下一趟。";
                        }));
                    return;
                }
                SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;
                int offered = 0;
                for (int i = 0; i < kinds.Length; i++)
                {
                    SkyIslandBountyKind kind = kinds[i];
                    int target = contract.TargetFor(kind);
                    // 可完成量随进度单调递减，接单时够就一定做得完。
                    if (session.AvailableBountyProgress(kind) < target) continue;
                    offered++;
                    choices.Add(new SkyIslandStoryPresentation.Choice(
                        "接委托 · " + SkyIslandBounty.NameCn(kind) + " ×" + target, delegate
                        {
                            string message;
                            contract.TryAccept(kind, out message);
                            return message;
                        }));
                }
                if (offered == 0)
                    choices.Add(new SkyIslandStoryPresentation.Choice(
                        "航务委托 · 暂时没有能接的活", delegate
                        {
                            return "苇白：航路这阵子清得差不多了，物资点也翻遍了。" +
                                "下次出岛再来看看吧。";
                        }));
                return;
            }
            choices.Add(new SkyIslandStoryPresentation.Choice("交付委托 · " + contract.Describe(), delegate
            {
                SkyIslandLootTier tier;
                string message;
                Vector3 position = rewardPosition != null ? rewardPosition() : Vector3.zero;
                int round = contract.CompletedRounds + 1;
                // 先送达再消费：谢礼放不下时委托原样保留，不会出现「单没了、谢礼也没有」。
                if (!contract.TryClaim(reward => session.DropBountyReward(position, reward, round), out tier, out message))
                    return message;
                return message + "（" + SkyIslandLootTables.TierNameCn(tier) + " 已放在脚边）";
            }));
            choices.Add(new SkyIslandStoryPresentation.Choice("退掉这一单 · " + contract.Describe(), delegate
            {
                string message;
                contract.TryAbandon(out message);
                return message;
            }));
        }

        /// <summary>装置版委托的奖励落点：用标记自身位置，避免奖励箱压在玩家身上。</summary>
        private Vector3 BoardPosition(string marker)
        {
            Transform point = root == null ? null : root.transform.Find(marker);
            return point != null ? point.position : Vector3.zero;
        }

        /// <summary>噬风：双航标点亮后才会到场；已解决则给出结果说明而不是再打一次。</summary>
        private void StormChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice("直面云海里的那阵风 · 噬风", delegate
            {
                if (session.StormResolved) return "栈道上的风已经散了。剩下的只是普通的云海。";
                if (!session.BothBeaconsLit) return "两端航标都亮起来，它才会循着光过来。";
                if (session.IsStoryChallengeActive("Storm")) return "它已经在栈道上了 —— 别停下。";
                if (!session.BeginStoryChallenge("Storm"))
                    return "当前无法开始：请站到鸣风栈道上，并等待上一场战斗结束。";
                presentation.Dispose();
                return "风眼张开了";
            }));
        }

        private void ZhelingChoices(List<SkyIslandStoryPresentation.Choice> choices)
        {
            Add(choices, "留下来谈 · 出示旧信与航路图", SkyIslandStoryAction.ReconcileZheling);
            choices.Add(Challenge("挑战旧航路守卫", "Zheling"));
        }
        private void BellChoices(List<SkyIslandStoryPresentation.Choice> choices)
        {
            Add(choices, "证明航路安全 · 与钟守和解", SkyIslandStoryAction.ReconcileBellKeeper);
            choices.Add(Challenge("挑战守钟装置", "BellKeeper"));
            Add(choices, "敲响归航钟", SkyIslandStoryAction.RingHomecomingBell);
        }
        private SkyIslandStoryPresentation.Choice Challenge(string label, string id)
        {
            return new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if (!session.BeginStoryChallenge(id)) return "当前无法开始：请靠近挑战地点，确认前置目标已完成或等待上次战斗结束。";
                presentation.Dispose();
                return "挑战开始";
            });
        }
        private void Add(List<SkyIslandStoryPresentation.Choice> choices, string label, SkyIslandStoryAction action)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if ((action == SkyIslandStoryAction.ReconcileZheling && session.IsStoryChallengeActive("Zheling")) ||
                    (action == SkyIslandStoryAction.ReconcileBellKeeper && session.IsStoryChallengeActive("BellKeeper")))
                    return "请先结束当前战斗，或者返航后重新来谈。";
                string message; story.TryApply(action, out message); return message;
            }));
        }

        internal void Tick()
        {
            presentation.Tick();
            if (displayedFlags == story.Current.flags) return;
            displayedFlags = story.Current.flags;
            foreach (GameObject go in feedback) if (go != null) UnityEngine.Object.Destroy(go);
            feedback.Clear();
            if (story.Current.Has(SkyIslandStoryFlag.WindBeacon)) Beacon("Search_D", "风标已校准", BossRushUIColors.Success);
            if (story.Current.Has(SkyIslandStoryFlag.StarLamp)) Beacon("Search_G", "星灯已点亮", BossRushUIColors.WarningText);
            if (story.Current.Has(SkyIslandStoryFlag.Telescope)) Beacon("Search_S4", "星图重新连接", BossRushUIColors.Accent);
            if (story.Current.Has(SkyIslandStoryFlag.PlantingDelivered)) Beacon("Search_C", "晴禾的归航菜畦", BossRushUIColors.Success);
            if (story.Current.Has(SkyIslandStoryFlag.StormSlain)) Beacon("Search_E", "风眼已散 · 航路重开", BossRushUIColors.Accent);
            if (story.Current.Has(SkyIslandStoryFlag.Ending)) Beacon("Search_H", "归航钟声 · 欢迎回家", BossRushUIColors.WarningText);
        }
        private void Beacon(string marker, string label, Color color)
        {
            Transform point = root.transform.Find(marker);
            if (point == null) return;
            GameObject go = new GameObject("SkyIslandStoryFeedback"); go.transform.SetParent(root.transform, false);
            go.transform.position = point.position + Vector3.up * 3;
            Light light = go.AddComponent<Light>(); light.type = LightType.Point; light.color = color;
            light.intensity = 1.6f; light.range = 14; light.shadows = LightShadows.None;
            feedback.Add(go);
            // 完成状态重入时重建，不能只在首次提交事件中点亮。
            feedback.Add(SkyIslandStoryInteractable.Create(root.transform, point.position, marker + "_Completed", label,
                delegate { presentation.Show(label, story.Summary, new List<SkyIslandStoryPresentation.Choice>()); }));
        }
        internal void Hide() { presentation.Dispose(); }
        public void Dispose()
        {
            presentation.Dispose();
            foreach (GameObject go in feedback) if (go != null) UnityEngine.Object.Destroy(go);
            feedback.Clear();
        }
        private static string Lore(string key)
        {
            switch (key)
            {
                case "Search_A": return "浮舟的渡船日志：风灾之后，码头仍每天留着一条返航的缆绳。沿北面的桥去风铃集，苇白正在等能修灯的人。渡口的工具还在，钝了的家伙可以在这里回一回火。";
                case "Search_B": return "留言板上钉着三张纸：苇白在找修复两端航标的帮手，晴禾在找落在蛙鸣池的种植记录，还有一张空白的委托单，谁都可以揭。即使主人离岛，留言也能送到。";
                case "Search_C": return "晴禾把菜畦一层层种向云海。田埂上的空格属于尚未归来的船员。";
                case "Search_D": return "风标卡在巨根之间。清掉附近的威胁后，校准指针，让西侧的航路重新有方向。";
                case "Search_E": return "双航标门需要风标与星灯同时回应。门后是归航钟庭；桥边的绞盘控制回村的中轴旧桥。栏杆上有一行后来刻的字：灯亮之后，别一个人站在桥心 —— 有东西会循着光过来。";
                case "Search_F": return "折翎留下的告示：风灾并未夺走全部航路。旧信与听雨洞的图纸或许能让他改变决定。";
                case "Search_G": return "工坊的铜环仍然完整。星灯只等一次重新校准，便能把东侧的光送回村庄。";
                case "Search_H": return "归航钟不再催促出航。两端的航标、归来的信件与守钟人的选择，将决定它为什么再次响起。";
                case "Search_S1": return "池边潮湿的纸页上记着菜种、日期，以及每一个归航人的名字。";
                case "Search_S2": return "没有寄出的旧信压在倒挂邮亭里。字迹歪斜，却还清楚地写着：请别让岛上的灯熄灭。";
                case "Search_S3": return "三道水声从洞壁传来；旧航路图把它们标成避风口。沿图上的虚线，船其实可以平安绕过风灾。";
                case "Search_S4": return "观星镜被守卫占据。清除威胁、校准镜片，无论白天黑夜都能找回群岛的星图。";
                default: return "旧木牌上记录着岛民的一天：有人等信，有人修灯，有人把空船再系紧一点。你走过的地方，正在重新连接。";
            }
        }
    }
}
