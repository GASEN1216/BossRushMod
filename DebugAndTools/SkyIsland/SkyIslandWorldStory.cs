using System;
using System.Collections.Generic;
using Duckov.Utilities;
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
        {
            this.session = session; this.story = story; this.root = root;
            // 会话在就绪那一刻建本对象，也就是「读条结束、人落地」的时间点：分段计时从这里开始算岛上时长。
            if (story != null) story.LogTiming("landed", null);
        }

        internal static string PointName(string key)
        {
            switch (key)
            {
                case "Search_A": return L10n.T("登云码头 · 渡口整备", "Cloudrise Dock · dock refit");
                case "Search_D": return L10n.T("校准风标 · 开启林边回程路",
                    "Calibrate the wind beacon · open the woodland way home");
                case "Search_G": return L10n.T("修复星灯 · 开启检修廊",
                    "Repair the star lamp · open the maintenance walk");
                case "Search_E": return L10n.T("双航标门 · 中轴旧桥与风眼",
                    "Twin-beacon gate · the old centre bridge and the storm's eye");
                case "Search_H": return L10n.T("归航钟 · 钟守留言",
                    "Homecoming Bell · the Bell Keeper's message");
                case "Search_B": return L10n.T("风铃集留言板 · 种植记录与航务委托",
                    "Windchime Market noticeboard · planting record and lane contracts");
                case "Search_C": return L10n.T("青穗梯田 · 归航菜畦", "Green Terraces · the homecoming garden");
                case "Search_F": return L10n.T("旧航路守卫 · 折翎", "Keeper of the old route · Zheling");
                case "Search_S1": return L10n.T("晴禾的种植记录", "Qinghe's planting record");
                case "Search_S2": return L10n.T("风没有送到的信", "The letter the wind never delivered");
                case "Search_S3": return L10n.T("听雨洞的旧航路图", "The old route chart in the Rainlisten Grotto");
                case "Search_S4": return L10n.T("修复观星镜", "Repair the telescope");
                // 8 个 _02 见闻点各有自己的标题与正文（以前全部落进 default，20 处见闻里 8 处是同一句话）。
                // 正文各指向一处支线或机制，见 Lore：种植记录、委托规矩、眠苔、旧信、噬风的风眼、航路图、观星镜、钟守要的证据。
                case "Search_A_02": return L10n.T("登云码头 · 渡船的等候名单", "Cloudrise Dock · the ferry waiting list");
                case "Search_B_02": return L10n.T("风铃集 · 委托单存根", "Windchime Market · contract stubs");
                case "Search_C_02": return L10n.T("青穗梯田 · 田埂上的记号", "Green Terraces · marks on the ridge");
                case "Search_D_02": return L10n.T("悬根林 · 吊在根上的邮袋", "Hanging Root Wood · a mailbag in the roots");
                case "Search_E_02": return L10n.T("鸣风栈道 · 栏杆上的刻痕", "Windsong Boardwalk · notches on the rail");
                case "Search_F_02": return L10n.T("镜水寺 · 池底的航路图拓本", "Mirrorwater Temple · a rubbing in the pool");
                case "Search_G_02": return L10n.T("残星工坊 · 瞭台检修日志", "Fallen Star Workshop · the overlook log");
                case "Search_H_02": return L10n.T("归航钟庭 · 钟架下的签名", "Homecoming Bell Court · names under the bell frame");
                default: return L10n.T("阅读群岛见闻", "Read the archipelago notes");
            }
        }

        /// <summary>
        /// 战斗静默门。面板会把时间压到 0（这个暂停是可靠的，见
        /// <see cref="SkyIslandSession.CanOpenStoryPanel"/>），所以打开时机必须挡住，
        /// 否则搜索点、居民和完成纪念物都是战斗中随手可用的暂停键。
        ///
        /// 门收在这一处而不是四个交互体类里：交互提示照常出现，按下去才告诉玩家原因，
        /// 不会因为门控疏漏把交互体永久禁用，也不需要给每个交互体再传一个谓词。
        /// </summary>
        private bool BlockedByCombat()
        {
            string reason;
            if (session.CanOpenStoryPanel(out reason)) return false;
            session.Announce(reason, true);
            return true;
        }

        /// <summary>
        /// 重新打开当前这一页面板。
        ///
        /// `SkyIslandStoryPresentation.Show` 只在打开那一刻把选项烘成按钮，之后按钮不再变化：
        /// 接完委托，「接委托 · 清理航路威胁 ×3」三个按钮还挂在那儿，再点一次只会得到
        /// 「手头这一单还没交」；交完单，本该重新出现的派单选项要退出面板再进来才看得到。
        /// 每次成功改变状态后重开一次，选项就永远与实际状态一致。
        ///
        /// 重开走的是 `Show`（内部先 Dispose 再重建）。调用点在按钮回调里，回调结束后外层会把
        /// 本次操作的返回文案写进**新**面板的正文，所以玩家看到的是「新选项 + 刚才那句回话」。
        /// </summary>
        private Action reopen;

        /// <summary>成功就重开面板；失败保持原样。返回原样的提示文案，供按钮回调写回正文。</summary>
        private string Refreshed(bool changed, string message)
        {
            if (changed && reopen != null) reopen();
            return message;
        }

        internal void ReadPoint(string key, Action recorded)
        {
            if (BlockedByCombat()) return;
            reopen = delegate { ReadPoint(key, recorded); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("收录见闻 / 物证", "Record the note / evidence"), delegate
            {
                if (key == "Search_S4" && !session.IsEncounterCleared("S4"))
                    return L10n.T("先清除瞭台上的守卫，再静下心校准观星镜。",
                        "Clear the guards on the overlook first, then calibrate the telescope in peace.");
                string message;
                bool recordedNow = story.RecordSearch(key, out message);
                if (recordedNow && recorded != null) recorded();
                return Refreshed(recordedNow, message);
            }));
            switch (key)
            {
                case "Search_D": Add(choices, L10n.T("校准西侧风标", "Calibrate the west wind beacon"),
                        SkyIslandStoryAction.RepairWindBeacon);
                    Add(choices, L10n.T("系牢林边旧运菜道 K1", "Secure the old produce path K1"),
                        SkyIslandStoryAction.OpenShortcutK1); break;
                case "Search_G": Add(choices, L10n.T("修复东侧星灯", "Repair the east star lamp"),
                        SkyIslandStoryAction.RepairStarLamp);
                    Add(choices, L10n.T("打开工坊检修廊 K2", "Open the workshop maintenance walk K2"),
                        SkyIslandStoryAction.OpenShortcutK2); break;
                case "Search_E":
                    Add(choices, L10n.T("开启中轴旧桥 K3", "Open the old centre bridge K3"),
                        SkyIslandStoryAction.OpenShortcutK3);
                    StormChoice(choices); break;
                case "Search_H": BellChoices(choices); break;
                case "Search_B":
                    Add(choices, L10n.T("把种植记录留给晴禾", "Leave the planting record for Qinghe"),
                        SkyIslandStoryAction.DeliverPlantingRecord);
                    // 委托板与苇白本人等价：她婚后离岛或尚未生成时，委托仍然可接可交。
                    BountyChoices(choices, () => BoardPosition("Search_B")); break;
                case "Search_A": ServiceChoice(choices,
                    L10n.T("渡口整备 · 修补随身装备", "Dock refit · repair what you carry"), Repair); break;
                // 菜畦与晴禾本人等价：她是永久 NPC，一旦与玩家结婚就由婚姻系统接管、不再上岛
                // （`SkyIslandResidents.SpawnOneAsync` 跳过生成，`PermanentDuckNpcModule` 对
                // SkyIslandRaid 恒返回 false），归航菜此前只挂在她身上，会永久失联。
                // 苇白的委托早有留言板兜底，这里给晴禾补上同一条纪律。`mealUsed` 是单次布尔，不会双领。
                case "Search_C": ServiceChoice(choices,
                    L10n.T("讨一份归航菜（本次出击生效）", "Ask for a homecoming meal (this raid only)"), Meal); break;
                case "Search_F": ZhelingChoices(choices); break;
            }
            // 装置/见闻面板配该区域的横幅插图；SkyIslandUiArt 是 fail-open 的，
            // 缺图就退成无插图布局，绝不因为一张图没出来就打不开挂着 K1/K2/K3 的装置。
            presentation.Show(PointName(key), Lore(key) + "\n\n" + story.CurrentObjective, choices,
                null, SkyIslandUiArt.GetScene(key));
        }

        internal void Talk(string id, Transform speaker)
        {
            if (BlockedByCombat()) return;
            reopen = delegate { Talk(id, speaker); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            if (id == "sky_qinghe")
            {
                Add(choices, L10n.T("交还种植记录", "Return the planting record"),
                    SkyIslandStoryAction.DeliverPlantingRecord);
                ServiceChoice(choices,
                    L10n.T("讨一份归航菜（本次出击生效）", "Ask for a homecoming meal (this raid only)"), Meal);
            }
            else if (id == "sky_zheling") ZhelingChoices(choices);
            else if (id == "sky_bellkeeper") BellChoices(choices);
            else if (id == "sky_weibai")
            {
                BountyChoices(choices, delegate { return speaker != null ? speaker.position : BoardPosition("Search_B"); });
                choices.Add(new SkyIslandStoryPresentation.Choice(
                    L10n.T("查阅航标与支线记录", "Review beacons and side-path records"), () => story.Summary));
            }
            else if (id == "sky_fuzhou") ServiceChoice(choices,
                L10n.T("渡口整备 · 修补随身装备", "Dock refit · repair what you carry"), Repair);
            else if (id == "sky_miantai") ServiceChoice(choices,
                L10n.T("请眠苔敷一副苔药", "Ask Miantai for a moss remedy"), Heal);
            presentation.Show(L10n.T("晴岚群岛 · ", "Qinglan · ") + ResidentName(id),
                story.DescribeNpc(id), choices, SkyIslandUiArt.GetPortrait(id), null);
        }

        /// <summary>居民显示名的唯一来源：交互提示、血条名与剧情面板标题共用同一份中英对照。</summary>
        internal static string ResidentName(string id)
        {
            switch (id)
            {
                case "sky_qinghe": return L10n.T("晴禾", "Qinghe");
                case "sky_weibai": return L10n.T("苇白", "Weibai");
                case "sky_fuzhou": return L10n.T("浮舟", "Fuzhou");
                case "sky_miantai": return L10n.T("眠苔", "Miantai");
                case "sky_zheling": return L10n.T("折翎", "Zheling");
                // 名字会直接当面板标题、交互名与血条名用，英文不带小写冠词（其他居民都是 "Zheling" 这种写法）。
                case "sky_bellkeeper": return L10n.T("无声钟守", "Silent Bell Keeper");
                default: return L10n.T("群岛居民", "Islander");
            }
        }
        /// <summary>服务类选项统一在这里做会话有效性检查，服务 owner 自己负责价格、冷却与失败原因。</summary>
        private void ServiceChoice(List<SkyIslandStoryPresentation.Choice> choices, string label, Func<string> action)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if (!session.IsReady) return L10n.T("请等待群岛就绪。", "Wait for the archipelago to finish loading.");
                // 分段计时只记「点过一次服务」；成交与否看服务 owner 的回话。
                story.LogTiming("service", action.Method.Name);
                return action();
            }));
        }

        private string Repair()
        {
            SkyIslandServices services = session.Services;
            return services == null ? L10n.T("渡口暂时没人。", "Nobody is at the dock right now.") : services.Repair();
        }

        private string Heal()
        {
            SkyIslandServices services = session.Services;
            return services == null ? L10n.T("眠苔不在。", "Miantai is not here.") : services.Heal();
        }

        private string Meal()
        {
            SkyIslandServices services = session.Services;
            return services == null
                ? L10n.T("菜畦还没开张。", "The garden is not open yet.")
                : services.Meal(session.HasPlantingDelivered);
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
                        L10n.T("航务委托 · 今日已派完", "Lane contracts · all handed out"), delegate
                        {
                            return L10n.T("苇白：今天的活都派完啦（", "Weibai: That is all the work for today (") +
                                contract.CompletedRounds + "/" + SkyIslandBounty.MaxRounds +
                                L10n.T("），剩下的留给下一趟。", "). The rest can wait for your next trip.");
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
                        L10n.T("接委托 · ", "Take contract · ") + SkyIslandBounty.Name(kind) + " ×" + target, delegate
                        {
                            string message;
                            // 接单成功后必须重开：另外两个「接委托」按钮已经不该再挂着，
                            // 该出现的是「交付委托」和「退掉这一单」。
                            string reply = Refreshed(contract.TryAccept(kind, out message), message);
                            // 派单选项只在手上没单时挂出，接成了才会有在手委托：以此判定成交，不改上面那句被守卫钉住的写法。
                            if (contract.HasActive) story.LogTiming("contract_accept", kind.ToString());
                            return reply;
                        }));
                }
                if (offered == 0)
                    choices.Add(new SkyIslandStoryPresentation.Choice(
                        L10n.T("航务委托 · 暂时没有能接的活", "Lane contracts · nothing to hand out"), delegate
                        {
                            return L10n.T("苇白：航路这阵子清得差不多了，物资点也翻遍了。下次出岛再来看看吧。",
                                "Weibai: The lanes are mostly clear and the caches are picked over. Come and see me again next trip.");
                        }));
                return;
            }
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("交付委托 · ", "Deliver contract · ") + contract.Describe(), delegate
            {
                SkyIslandLootTier tier;
                string message;
                Vector3 position = rewardPosition != null ? rewardPosition() : Vector3.zero;
                int round = contract.CompletedRounds + 1;
                // 先送达再消费：谢礼放不下时委托原样保留，不会出现「单没了、谢礼也没有」。
                if (!contract.TryClaim(reward => session.DropBountyReward(position, reward, round), out tier, out message))
                    return message;
                story.LogTiming("contract_deliver", "round" + round);
                // 交单后重开：委托槽空了，下一单的派单选项应当立刻可见。
                return Refreshed(true, message + L10n.T("（", " (") +
                    L10n.T(SkyIslandLootTables.TierNameCn(tier), SkyIslandLootTables.TierNameEn(tier)) +
                    // 谢礼可能落在苇白身旁，也可能落在留言板旁（她不在时），不写「她脚边」。
                    L10n.T(" 已放在一旁）", " set down nearby)"));
            }));
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("退掉这一单 · ", "Drop this contract · ") + contract.Describe(), delegate
            {
                string message;
                string reply = Refreshed(contract.TryAbandon(out message), message);
                if (!contract.HasActive) story.LogTiming("contract_drop", null);
                return reply;
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
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("直面云海里的那阵风 · 噬风", "Face the wind out on the cloud sea · the Windeater"), delegate
            {
                if (session.StormResolved)
                    return L10n.T("栈道上的风已经散了。剩下的只是普通的云海。",
                        "The wind on the boardwalk has broken up. What is left is ordinary cloud sea.");
                if (!session.BothBeaconsLit)
                    return L10n.T("两端航标都亮起来，它才会循着光过来。",
                        "It only comes for the light once both beacons burn.");
                if (session.IsStoryChallengeActive("Storm"))
                    return L10n.T("它已经在栈道上了 —— 别停下。", "It is already on the boardwalk — keep moving.");
                if (!session.BeginStoryChallenge("Storm"))
                    return L10n.T("当前无法开始：请站到鸣风栈道上，并等待上一场战斗结束。",
                        "Cannot start now: stand on Windsong Boardwalk and wait for the previous fight to end.");
                presentation.Dispose();
                // 同 Challenge：面板已经收起，回执改走字幕。
                string opened = L10n.T("风眼张开了", "The eye of the storm opens");
                session.Announce(opened, false);
                story.LogTiming("challenge", "Storm");
                return opened;
            }));
        }

        private void ZhelingChoices(List<SkyIslandStoryPresentation.Choice> choices)
        {
            Add(choices, L10n.T("留下来谈 · 出示旧信与航路图",
                "Stay and talk · show the old letter and the route chart"), SkyIslandStoryAction.ReconcileZheling);
            // 两条结局互斥：战胜折翎之后「留下来谈」永久关闭（ReconcileZheling 对已了结的折翎直接拒绝）。
            // 这一项就挨在「留下来谈」下面，按钮上说清楚，免得想走和平线的人手一滑打赢了才发现回不去。
            choices.Add(Challenge(L10n.T("挑战旧航路守卫（战胜后不能再和解）",
                "Challenge the keeper of the old route (no reconciling once you win)"), "Zheling"));
        }
        private void BellChoices(List<SkyIslandStoryPresentation.Choice> choices)
        {
            Add(choices, L10n.T("证明航路安全 · 与钟守和解",
                "Prove the lanes are safe · reconcile with the Bell Keeper"), SkyIslandStoryAction.ReconcileBellKeeper);
            choices.Add(Challenge(L10n.T("挑战守钟装置", "Challenge the bell engine"), "BellKeeper"));
            Add(choices, L10n.T("敲响归航钟", "Ring the Homecoming Bell"), SkyIslandStoryAction.RingHomecomingBell);
        }
        private SkyIslandStoryPresentation.Choice Challenge(string label, string id)
        {
            return new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if (!session.BeginStoryChallenge(id))
                    return L10n.T("当前无法开始：请靠近挑战地点，确认前置目标已完成或等待上次战斗结束。",
                        "Cannot start now: move closer to the site, make sure the prerequisites are done, and wait for the previous fight to end.");
                presentation.Dispose();
                // 面板已经收起，回执写不回正文（SetBodyText 见正文已销毁直接返回）：改走字幕，否则面板一关就没了下文。
                string started = L10n.T("挑战开始", "The challenge begins");
                session.Announce(started, false);
                story.LogTiming("challenge", id);
                return started;
            });
        }
        private void Add(List<SkyIslandStoryPresentation.Choice> choices, string label, SkyIslandStoryAction action)
        {
            choices.Add(new SkyIslandStoryPresentation.Choice(label, delegate
            {
                if ((action == SkyIslandStoryAction.ReconcileZheling && session.IsStoryChallengeActive("Zheling")) ||
                    (action == SkyIslandStoryAction.ReconcileBellKeeper && session.IsStoryChallengeActive("BellKeeper")))
                    return L10n.T("请先结束当前战斗，或者返航后重新来谈。",
                        "Finish the current fight first, or come back to talk after you return.");
                string message;
                // 成功后重开：修好风标，同一页上的 K1 立刻从「请先修复…」变成可用；
                // 交还种植记录后，这一项也不该再挂在选项里。
                return Refreshed(story.TryApply(action, out message), message);
            }));
        }

        internal void Tick()
        {
            presentation.Tick();
            // 面板开着时战斗才打起来（在途 async 生成会在 timeScale=0 下继续完成）就立刻收起来，
            // 否则暂停键依然成立，只是换了个打开时机。
            if (presentation.Visible)
            {
                string reason;
                if (!session.CanOpenStoryPanel(out reason))
                {
                    presentation.Dispose();
                    session.Announce(reason, true);
                    return;
                }
            }
            if (displayedFlags == story.Current.flags) return;
            // 进岛首帧 displayedFlags 为 -1：存档里早就有的结果只重建世界状态、不重播回话；之后只读真正新增的位。
            int added = displayedFlags < 0 ? 0 : story.Current.flags & ~displayedFlags;
            displayedFlags = story.Current.flags;
            AnnounceCombatOutcomes(added);
            foreach (GameObject go in feedback) if (go != null) UnityEngine.Object.Destroy(go);
            feedback.Clear();
            if (story.Current.Has(SkyIslandStoryFlag.WindBeacon))
                Beacon("Search_D", L10n.T("风标已校准", "Wind beacon calibrated"), BossRushUIColors.Success);
            if (story.Current.Has(SkyIslandStoryFlag.StarLamp))
                Beacon("Search_G", L10n.T("星灯已点亮", "Star lamp lit"), BossRushUIColors.WarningText);
            if (story.Current.Has(SkyIslandStoryFlag.Telescope))
                Beacon("Search_S4", L10n.T("星图重新连接", "Star chart reconnected"), BossRushUIColors.Accent);
            if (story.Current.Has(SkyIslandStoryFlag.PlantingDelivered))
                Beacon("Search_C", L10n.T("晴禾的归航菜畦", "Qinghe's homecoming garden"), BossRushUIColors.Success);
            if (story.Current.Has(SkyIslandStoryFlag.StormSlain))
                Beacon("Search_E", L10n.T("风眼已散 · 航路重开", "The eye is gone · the lanes reopen"), BossRushUIColors.Accent);
            // 折翎被战胜后剧情体不再露面（见 SkyIslandSession 的 SetVisible），原地留下旧腰牌。
            // 走的是同一条「按持久 flag 重建」的路子，跨局重进仍在。
            if (story.Current.Has(SkyIslandStoryFlag.ZhelingDefeated))
                Beacon("POI_F", L10n.T("折翎的旧腰牌", "Zheling's old badge"), BossRushUIColors.Accent, ZhelingBadgeText);
            if (story.Current.Has(SkyIslandStoryFlag.Ending))
                Beacon("Search_H", L10n.T("归航钟声 · 欢迎回家", "The Homecoming Bell · welcome home"), BossRushUIColors.WarningText);
        }
        /// <summary>
        /// 折翎旧腰牌（纪念物）的面板正文。Wiki 承诺「他倒下时留下的旧腰牌写着『航路交给你』，钟守认这份物证」，
        /// 而折翎战败后剧情体不再露面，<c>DescribeNpc("sky_zheling")</c> 里那句刻字再也没有入口；
        /// 纪念物以前只显示旅程摘要，玩家从头到尾读不到这块腰牌上写了什么、为什么钟守认它。
        /// </summary>
        private string ZhelingBadgeText()
        {
            return L10n.T("旧腰牌上刻着：『航路交给你。』\n钟守认得这块腰牌——它能证明折翎那一关已经有了结。",
                "The old badge is engraved: 'The route is yours now.'\nThe Bell Keeper knows this badge. It proves the matter with Zheling is settled.") +
                "\n\n" + story.Summary;
        }

        /// <summary>
        /// 战斗里了结的剧情结果读成字幕（文案唯一来源 <see cref="SkyIslandStoryRules.CombatOutcome"/>）。
        /// 这三个结果由会话在清场或噬风倒下时直接提交，没有面板可以写回话；面板里点出来的动作不走这里。
        /// </summary>
        private void AnnounceCombatOutcomes(int added)
        {
            if (added == 0) return;
            SkyIslandStoryFlag[] outcomes = SkyIslandStoryRules.CombatOutcomeFlags;
            for (int i = 0; i < outcomes.Length; i++)
                if ((added & (int)outcomes[i]) != 0) session.Announce(SkyIslandStoryRules.CombatOutcome(outcomes[i]), false);
        }

        /// <param name="body">纪念物面板正文；null 时显示旅程摘要。</param>
        private void Beacon(string marker, string label, Color color, Func<string> body = null)
        {
            Transform point = root.transform.Find(marker);
            if (point == null) return;
            GameObject go = new GameObject("SkyIslandStoryFeedback"); go.transform.SetParent(root.transform, false);
            go.transform.position = point.position + Vector3.up * 3;
            Light light = go.AddComponent<Light>(); light.type = LightType.Point; light.color = color;
            light.intensity = 1.6f; light.range = 14; light.shadows = LightShadows.None;
            feedback.Add(go);
            // 纪念物**不能**摆在装置自己的位置上。官方 `CA_Interact.SearchInteractableAround`
            // 按「玩家到 collider 的距离严格小于」取唯一交互目标：两者同点时距离完全相等，
            // 谁赢由 `Physics.OverlapSphereNonAlloc` 的返回顺序决定，而且它们不在同一交互组、
            // 滚轮也切不过去。而 Search_D / Search_G 上挂着 K1 / K2 捷径、Search_E 挂着 K3 与噬风、
            // Search_H 挂着敲响归航钟——被一块只读的纪念牌盖住，就等于捷径与终章永久点不到。
            //
            // 落点复用全岛唯一经过真实几何回归的放置算法（`SkyIslandContentPlacementPropertyTest`
            // 复算的就是它），方位角按标记名取稳定散列，同一块地形每次进岛都落在同一处。
            // 找不到净空就**只留光、不挂交互体**：纪念物是纯装饰，装置不是，绝不退回原点。
            Vector3 spot;
            if (!SkyIslandRewardCrate.TryFindCratePosition(root.transform, point.position,
                SkyIslandLootTables.StableHash(marker) % 360, SkyIslandRewardCrate.InteractableSeparation,
                GameplayDataSettings.Layers.groundLayerMask.value, out spot)) return;
            // 完成状态重入时重建，不能只在首次提交事件中点亮。
            feedback.Add(SkyIslandStoryInteractable.Create(root.transform, spot, marker + "_Completed", label,
                delegate
                {
                    if (BlockedByCombat()) return;
                    presentation.Show(label, body != null ? body() : story.Summary,
                        new List<SkyIslandStoryPresentation.Choice>(),
                        null, SkyIslandUiArt.GetScene(marker));
                }));
        }
        internal void Hide() { presentation.Dispose(); }
        public void Dispose()
        {
            // reopen 捕获了 marker key、recorded 回调与说话人 Transform，会话结束后一并放开。
            reopen = null;
            presentation.Dispose();
            foreach (GameObject go in feedback) if (go != null) UnityEngine.Object.Destroy(go);
            feedback.Clear();
        }
        private static string Lore(string key)
        {
            switch (key)
            {
                case "Search_A": return L10n.T(
                    "浮舟的渡船日志：风灾之后，码头仍每天留着一条返航的缆绳。沿北面的桥去风铃集，苇白正在等能修灯的人。渡口的工具还在，钝了的家伙可以在这里回一回火。",
                    "Fuzhou's ferry log: since the storm, the dock still keeps one mooring line free every day. Take the north bridge to Windchime Market — Weibai is waiting for someone who can mend the lamps. The dock tools are still here, so anything gone blunt can be brought back to an edge.");
                case "Search_B": return L10n.T(
                    "留言板上钉着三张纸：苇白在找修复两端航标的帮手，晴禾在找落在蛙鸣池的种植记录，还有一张空白的委托单，谁都可以揭。即使主人离岛，留言也能送到。",
                    "Three sheets are pinned to the board: Weibai wants help restoring both beacons, Qinghe is looking for the planting record she left at Frogsong Pool, and one blank contract slip anyone may take. Messages get through even when their owners are away from the island.");
                case "Search_C": return L10n.T(
                    "晴禾把菜畦一层层种向云海。田埂上的空格属于尚未归来的船员。灶还温着——种植记录回来之后，谁路过都能讨一碗归航菜。",
                    "Qinghe planted the beds in terraces stepping down toward the cloud sea. The gaps along the ridge belong to crew who have not come back. The stove is still warm — once the planting record returns, anyone passing may ask for a bowl of homecoming greens.");
                case "Search_D": return L10n.T(
                    "风标卡在巨根之间。清掉附近的威胁后，校准指针，让西侧的航路重新有方向。",
                    "The wind beacon is jammed among the great roots. Clear the threats nearby, then calibrate the needle and give the western lane its bearing back.");
                case "Search_E": return L10n.T(
                    "双航标门需要风标与星灯同时回应。门后是归航钟庭；桥边的绞盘控制回村的中轴旧桥。栏杆上有一行后来刻的字：灯亮之后，别一个人站在桥心 —— 有东西会循着光过来。",
                    "The twin-beacon gate needs the wind beacon and the star lamp answering together. Beyond it lies the Homecoming Bell Court; the winch by the bridge works the old centre span back to the village. A later hand cut a line into the rail: once the lights are up, do not stand alone at mid-span — something comes for the light.");
                case "Search_F": return L10n.T(
                    "折翎留下的告示：风灾并未夺走全部航路。旧信与听雨洞的图纸或许能让他改变决定。",
                    "A notice left by Zheling: the storm did not take every lane. The old letter and the chart from the Rainlisten Grotto might change his mind.");
                case "Search_G": return L10n.T(
                    "工坊的铜环仍然完整。星灯只等一次重新校准，便能把东侧的光送回村庄。",
                    "The workshop's brass rings are still intact. The star lamp needs only one recalibration to send the eastern light back to the village.");
                case "Search_H": return L10n.T(
                    "归航钟不再催促出航。两端的航标、归来的信件与守钟人的选择，将决定它为什么再次响起。",
                    "The Homecoming Bell no longer urges anyone to sea. The two beacons, the letter that came home and the keeper's own choice will decide why it rings again.");
                case "Search_S1": return L10n.T(
                    "池边潮湿的纸页上记着菜种、日期，以及每一个归航人的名字。",
                    "The damp pages by the pool list seeds, dates, and the name of every person expected home.");
                case "Search_S2": return L10n.T(
                    "没有寄出的旧信压在倒挂邮亭里。字迹歪斜，却还清楚地写着：请别让岛上的灯熄灭。",
                    "An unsent letter is wedged inside the Upturned Post Hut. The hand is crooked but still plain: please do not let the island's lights go out.");
                case "Search_S3": return L10n.T(
                    "三道水声从洞壁传来；旧航路图把它们标成避风口。沿图上的虚线，船其实可以平安绕过风灾。",
                    "Three streams sound through the cave wall; the old route chart marks them as shelter. Follow the dotted line and a ship can in fact pass the storm safely.");
                case "Search_S4": return L10n.T(
                    "观星镜被守卫占据。清除威胁、校准镜片，无论白天黑夜都能找回群岛的星图。",
                    "Guards have taken the telescope. Clear them out and align the lens, and the archipelago's star chart comes back day or night.");
                // _02 见闻：每处一段自己的记录，各自给一条去处或机制的线索（主线目标卡只管航标与钟庭，支线全靠这些被发现）。
                case "Search_A_02": return L10n.T(
                    "码头的等候名单上划掉了大半的名字。最后一行是浮舟的字：『梯田那头的蛙鸣池有人捎信回来——晴禾的种植记录还泡在水边。』",
                    "Most of the names on the dock's waiting list are crossed out. The last line is in Fuzhou's hand: 'Word came back from Frogsong Pool, past the terraces — Qinghe's planting record is still lying by the water.'");
                case "Search_B_02": return L10n.T(
                    "一摞交过的委托单存根：清理航路、回收补给、巡视群岛。苇白在最底下记着规矩：『一趟最多派三单，一单比一单重；第三单的谢礼从工坊的旧货里出。』",
                    "A stack of stubs from finished contracts: clearing lanes, recovering supplies, surveying the isles. At the bottom Weibai has written down her rule: 'Three contracts a trip at most, each a little heavier than the last. The third payout comes out of the workshop's old stock.'");
                case "Search_C_02": return L10n.T(
                    "田埂的木桩上刻着箭头，一路指向悬根林：『风标卡住那天，林里的人都搬到根环后面去了。受了伤就去找眠苔，她的苔药按伤势收钱。』",
                    "Arrows are cut into the ridge posts, all pointing at the Hanging Root Wood: 'The day the wind beacon jammed, the wood folk moved in behind the root ring. If you are hurt, find Miantai — her moss remedy is priced by how badly you are hurt.'");
                case "Search_D_02": return L10n.T(
                    "巨根上挂着一只空邮袋，标签写着「倒挂邮亭」。袋底粘着一张回执：寄往镜水寺，收信人折翎。那封信，一直没有送到。",
                    "An empty mailbag hangs in the great roots, tagged 'Upturned Post Hut'. A receipt is still stuck to the bottom: to Mirrorwater Temple, for Zheling. That letter never arrived.");
                case "Search_E_02": return L10n.T(
                    "栏杆上新刻了一排记号，像是有人在数日子：『两盏灯都亮的那一夜，风从云海底下翻了上来。它收拢风眼之前，脚下先亮一圈光——看见光就往圈外跑，跑不出去就躲到石头后面。』",
                    "A fresh row of notches runs along the rail, as if someone were counting the days: 'The night both lamps were lit, the wind climbed up out of the cloud sea. Before it draws its eye shut, a ring of light shows at its feet. See the light, run out of the ring — or get behind solid rock.'");
                case "Search_F_02": return L10n.T(
                    "池底压着一张被水泡软的拓本，只看得清半条航线，终点圈着「听雨洞」。另一半在折翎手里——他说旧信和航路图都齐了，才肯坐下来谈。",
                    "A water-softened rubbing is weighted down at the bottom of the pool. Only half a route is legible, ending in a circle marked 'Rainlisten Grotto'. Zheling holds the other half; he will only sit down and talk once the old letter and the route chart are both on the table.");
                case "Search_G_02": return L10n.T(
                    "检修日志的最后一页：『观星镜的镜片偏了三格，残星瞭台上来了一伙人，谁也上不去。等把他们清走，照着星灯的方向校准就行。』",
                    "The last page of the maintenance log: 'The telescope lens has drifted three notches, and a gang has taken over the Starfall Overlook, so nobody can get up there. Once they are cleared out, align it with the star lamp.'");
                case "Search_H_02": return L10n.T(
                    "钟架横梁下签满了名字，每个名字旁都记着一件东西：一封信，一张图，一面镜片。有一行只写了开头：『等那阵风散了……』钟守说，这些都是航路安全的证据。",
                    "The beam under the bell frame is covered in signatures, and beside each one something is noted: a letter, a chart, a lens. One line has only its opening: 'Once that wind is gone…' The Bell Keeper calls all of these proof that the lanes are safe.");
                default: return L10n.T(
                    "旧木牌上记录着岛民的一天：有人等信，有人修灯，有人把空船再系紧一点。你走过的地方，正在重新连接。",
                    "An old board records a day on the islands: someone waiting for a letter, someone mending a lamp, someone tying an empty boat a little tighter. The places you walk are joining back up.");
            }
        }
    }
}
