using System;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>装置、居民对白和实体剧情反馈；必要动作在独立装置上始终可达。</summary>
    internal sealed partial class SkyIslandWorldStory : IDisposable
    {
        private readonly SkyIslandSession session;
        private readonly SkyIslandStoryService story;
        private readonly SkyIslandStoryPresentation presentation = new SkyIslandStoryPresentation();
        private readonly List<GameObject> feedback = new List<GameObject>();
        private readonly GameObject root;
        private int displayedFlags = -1;
        private SkyIslandResidentDialogue dialogue;
        private bool disposed;
        /// <summary>本趟秘境谜题解到哪一步：只活在会话里，解开后的结果仍是既有支线物证旗标。</summary>
        private readonly SkyIslandPuzzleState puzzles = new SkyIslandPuzzleState();
        /// <summary>本趟的信鸽（至多一只）与它带的信；收下之后两者都清空。</summary>
        private GameObject pigeon;
        private SkyIslandLetter pigeonLetter;
        private bool pigeonPlaced;
        /// <summary>本趟手记首页实际挂出的项数（F3 只读 SKY_CHOICE_GATES）；本趟还没打开过手记时为 -1。</summary>
        private int journalHomeChoices = -1;
        private float pigeonCaptionAt = -1f;
        /// <summary>内容批次三：采集点、合成台、局内耗材与夜风的本趟 owner（会话就绪后第一次推进时创建，随本对象销毁）。</summary>
        private SkyIslandFieldcraft fieldcraft;
        private bool fieldcraftFailed;
        /// <summary>信鸽落地字幕推迟的游戏秒数：错开落地大标题与目标卡，别在同一秒挤三句话。</summary>
        internal const float PigeonCaptionDelay = 8f;
        internal bool Visible { get { return presentation.Visible; } }
        /// <summary>F3 只读（SKY_LETTER_PIGEON）：信鸽此刻带的信、一次性落点闩、信鸽在不在场。玩法不读这三个。</summary>
        internal SkyIslandLetter PigeonLetter { get { return pigeonLetter; } }
        internal bool PigeonPlaced { get { return pigeonPlaced; } }
        internal bool PigeonPresent { get { return pigeon != null; } }
        /// <summary>F3 只读（SKY_CHOICE_GATES）：本趟手记首页实际挂出的项数，没打开过为 -1。</summary>
        internal int JournalHomeChoices { get { return journalHomeChoices; } }
        internal SkyIslandWorldStory(SkyIslandSession session, SkyIslandStoryService story, GameObject root)
        {
            this.session = session; this.story = story; this.root = root;
            // 会话在就绪那一刻建本对象，也就是「读条结束、人落地」的时间点：分段计时从这里开始算岛上时长。
            if (story != null) story.LogTiming("landed", null);
            // 见闻镜像进官方笔记图鉴：注册 20 条条目 + 把存档里已收录的点亮。幂等、fail-open。
            // 老存档靠这一步补齐（收录时的 Unlock 只覆盖本趟新收的）。
            // 放这里而不是 SkyIslandSession：剧情侧的事归剧情 owner（AGENTS 4.15），
            // 而且 Session 卡在 1200 行预算上。
            if (story != null) SkyIslandNoteBridge.EnsureRegistered(story.Current);
            pigeonCaptionAt = Time.time + PigeonCaptionDelay;
            // 头目 / 岛主 R1：首杀记手记 + 字幕（SkyIslandWorldStoryBosses.cs），Dispose 里退订。
            AttachBossEvents();
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
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            // 四座秘境的物证点先是一段三步小谜题（SkyIslandPuzzles）；解开之后、或物证早已拿到，才是普通的「收录」页。
            SkyIslandPuzzle puzzle = SkyIslandPuzzles.For(key);
            // 残星瞭台的守卫没清时不出谜题：三个选项点了都只会回「先清守卫」，那句话由 RecordChoice 收进正文。
            bool solving = puzzle != null && !story.Current.Has(puzzle.Flag) && !puzzles.IsSolved(puzzle)
                && OverlookGuarded(key) == null;
            if (solving) PuzzleChoices(choices, puzzle, recorded);
            else RecordChoice(choices, key, recorded);
            switch (key)
            {
                case "Search_D": AddIf(choices, L10n.T("校准西侧风标", "Calibrate the west wind beacon"),
                        SkyIslandStoryAction.RepairWindBeacon);
                    AddIf(choices, L10n.T("系牢林边旧运菜道 K1", "Secure the old produce path K1"),
                        SkyIslandStoryAction.OpenShortcutK1); break;
                case "Search_G": AddIf(choices, L10n.T("修复东侧星灯", "Repair the east star lamp"),
                        SkyIslandStoryAction.RepairStarLamp);
                    AddIf(choices, L10n.T("打开工坊检修廊 K2", "Open the workshop maintenance walk K2"),
                        SkyIslandStoryAction.OpenShortcutK2); break;
                case "Search_E":
                    AddIf(choices, L10n.T("开启中轴旧桥 K3", "Open the old centre bridge K3"),
                        SkyIslandStoryAction.OpenShortcutK3);
                    StormChoice(choices);
                    // 结局后的噬风·回响（SkyIslandWorldStoryEcho.cs）：打过噬风才会挂出，与首战那一项互斥。
                    StormEchoChoice(choices); break;
                case "Search_H": BellChoices(choices); break;
                case "Search_B":
                    AddIf(choices, L10n.T("把种植记录留给晴禾", "Leave the planting record for Qinghe"),
                        SkyIslandStoryAction.DeliverPlantingRecord);
                    // 委托板与苇白本人等价：她婚后离岛或尚未生成时，委托仍然可接可交。委托单独一页，见 ContractsChoice。
                    ContractsChoice(choices, () => BoardPosition("Search_B"), delegate { ReadPoint(key, recorded); });
                    break;
                case "Search_A": RepairChoice(choices);
                    // 渡口工台与浮舟本人等价：他不在时码头装置照样能做东西。
                    CraftChoice(choices, SkyIslandCraftStation.Dock);
                    // 手记与苇白本人等价：码头每趟必经，翻手记不必先去风铃集找到她。
                    JournalChoice(choices); break;
                // 菜畦与晴禾本人等价：她是永久 NPC，一旦与玩家结婚就由婚姻系统接管、不再上岛
                // （`SkyIslandResidents.SpawnOneAsync` 跳过生成，`PermanentDuckNpcModule` 对
                // SkyIslandRaid 恒返回 false），归航菜此前只挂在她身上，会永久失联。
                // 苇白的委托早有留言板兜底，这里给晴禾补上同一条纪律。`mealUsed` 是单次布尔，不会双领。
                case "Search_C": MealChoice(choices);
                    CraftChoice(choices, SkyIslandCraftStation.Stove); break;
                // 眠苔的药臼与苔药：她不是永久居民，但生成可能失败；悬根林的见闻点就在她站位旁 14 米，两样一起兜底。
                // **服务和合成台必须成对兜底**：只兜药臼的话，眠苔没生成出来的那一趟玩家连唯一的付费回血都没有，
                // 而星苔药膏恰恰要在她的药臼上做——等于把"回血"这条线整条掐断。浮舟（码头装置）、
                // 晴禾（菜畦）都是服务 + 合成台一起兜的，这里补齐同一条纪律。
                // `healReadyAt` 与 `mealUsed` 一样是 SkyIslandServices 的单例字段，两个入口共用同一次冷却，不会双领。
                case "Search_D_02":
                    HealChoice(choices);
                    CraftChoice(choices, SkyIslandCraftStation.Mortar); break;
                case "Search_F": ZhelingChoices(choices); break;
                // 内容批次四：镜水寺池边夜里捧蛙卵，这一趟里带回蛙鸣池放生（SkyIslandGnats；放生写进本槽手记）。
                case "Search_F_02": SpawnChoice(choices); break;
                case "Search_S1": ReleaseChoice(choices); break;
            }
            // 七处装置各缺一盏风晶灯（信里的请求）：亮了就不再挂这一项。
            LightChoice(choices, key);
            // 装置/见闻面板配该区域的横幅插图；SkyIslandUiArt 是 fail-open 的，
            // 缺图就退成无插图布局，绝不因为一张图没出来就打不开挂着 K1/K2/K3 的装置。
            // 正文只放一句导语 +（被隐藏选项留下的）下一步。
            // 长文去了官方笔记图鉴，目标卡常驻右上角 HUD——两处都不必在这里再说一遍。
            presentation.Show(SkyIslandPointText.Name(key),
                solving ? PuzzleBody(puzzle) : WithNextStep(SkyIslandPointText.Brief(key)),
                choices, null, SkyIslandUiArt.GetScene(key));
        }

        /// <summary>
        /// 跟居民说话的入口：**叙事走官方对话**（自带立绘位、逐句推进），说完再问一句要不要办事。
        ///
        /// 面板的 <see cref="reopen"/> 指向 <see cref="OpenResidentPanel"/> 而不是本方法——
        /// 否则每点一个选项（接一单委托、买一副苔药）都要把整段台词从头再听一遍。
        /// </summary>
        internal void Talk(string id, Transform speaker)
        {
            if (disposed || (dialogue != null && dialogue.Active) || DialogueManager.IsDialogueActive) return;
            if (BlockedByCombat()) return;
            dialogue = SkyIslandResidentDialogue.Run(id, speaker, story.DescribeNpc(id),
                delegate { OpenResidentPanel(id, speaker); }, CanContinueDialogue,
                delegate { return ResidentChoices(id, speaker).Count > 0 || NextStep() != null; });
        }

        /// <summary>居民的功能面板：接委托 / 苔药 / 整备 / 合成 / 手记。叙事不在这里，见 <see cref="Talk"/>。</summary>
        private void OpenResidentPanel(string id, Transform speaker)
        {
            if (BlockedByCombat()) return;
            reopen = delegate { OpenResidentPanel(id, speaker); };
            List<SkyIslandStoryPresentation.Choice> choices = ResidentChoices(id, speaker);
            // 主视觉给「他家那一区」的插图：居民站在自己的地标上，立绘就压在那张图上。
            // 正文**不再重复台词**——那段话官方对话刚刚一句一屏地说完了，
            // 这里只留「被隐藏的选项还差什么」，没有就空着。
            presentation.Show(L10n.T("晴岚群岛 · ", "Qinglan · ") + ResidentName(id),
                WithNextStep(string.Empty), choices, SkyIslandUiArt.GetPortrait(id),
                SkyIslandUiArt.GetScene(SkyIslandResidents.MarkerOf(id)));
        }

        /// <summary>
        /// 居民功能面板挂哪些选项。<see cref="Talk"/> 问不问「我想办点事」也用它：有选项或「还差什么」才问，
        /// 否则点下去是空面板（2026-09-15 第五轮，结局后的无声钟守）。
        /// </summary>
        private List<SkyIslandStoryPresentation.Choice> ResidentChoices(string id, Transform speaker)
        {
            hiddenHints.Clear();
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            if (id == "sky_qinghe")
            {
                // 走 AddIf：没捡到记录时这一项不挂（「还差什么」进正文），交还之后也不再挂——
                // 正是 Add 上那条注释说了三个月的事。
                AddIf(choices, L10n.T("交还种植记录", "Return the planting record"),
                    SkyIslandStoryAction.DeliverPlantingRecord);
                MealChoice(choices);
                CraftChoice(choices, SkyIslandCraftStation.Stove);
            }
            else if (id == "sky_zheling") ZhelingChoices(choices);
            else if (id == "sky_bellkeeper") BellChoices(choices);
            else if (id == "sky_weibai")
            {
                ContractsChoice(choices, delegate { return speaker != null ? speaker.position : BoardPosition("Search_B"); },
                    delegate { OpenResidentPanel(id, speaker); });
                // 以前这一项只回一段旅程摘要；群岛手记把摘要放进总览，再加上见闻、来信与名册。
                JournalChoice(choices);
            }
            else if (id == "sky_fuzhou")
            {
                RepairChoice(choices);
                CraftChoice(choices, SkyIslandCraftStation.Dock);
            }
            else if (id == "sky_miantai")
            {
                HealChoice(choices);
                CraftChoice(choices, SkyIslandCraftStation.Mortar);
            }
            return choices;
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
        /// 1. **只派做得完的单**。清场数本趟还没清的自动组、巡岛数本趟还没踏足的区域，一趟里做掉多少就少多少；
        ///    用 `session.AvailableBountyProgress` 与本轮目标比对后再决定是否挂出来。
        ///    驱蚋单是这条规则里唯一的**软门**（供给是概率事件、还能被玩家自己的驱风香与灶火烟压没，
        ///    口径见 `SkyIslandSession.AvailableBountyProgress`），所以第 2 条对它不是冗余而是兜底。
        /// 2. **永远给退单出口**。哪怕门控有疏漏（比如接单后搜刮点建箱失败），
        ///    玩家也能自己退掉，不会被一张做不完的单把本局委托槽卡死。
        /// </summary>
        private void BountyChoices(List<SkyIslandStoryPresentation.Choice> choices, Func<Vector3> rewardPosition)
        {
            SkyIslandBounty contract = session.Bounty;
            if (contract == null) return;
            if (!contract.HasActive)
            {
                // 派完了、暂时没有能接的活：都不挂占位项（点了只回一句话），那句话进正文（2026-09-14 审核 F-06）。
                if (!contract.CanAcceptMore)
                {
                    Hint(L10n.T("苇白：今天的活都派完啦（", "Weibai: That is all the work for today (") +
                        contract.CompletedRounds + "/" + SkyIslandBounty.MaxRounds +
                        L10n.T("），剩下的留给下一趟。", "). The rest can wait for your next trip."));
                    return;
                }
                SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;
                int offered = 0;
                for (int i = 0; i < kinds.Length; i++)
                {
                    SkyIslandBountyKind kind = kinds[i];
                    int target = contract.TargetFor(kind);
                    // 前三类的可完成量随进度单调递减，接单时够就一定做得完；驱蚋单是软门，靠退单兜底。
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
                // 驱蚋单是四类里唯一「白天永远挂不出来」的方向。这句不提它，
                // 玩家在游戏里第一次该遇到它的地方就完全不知道它存在。
                // 只在这一趟真会起蚋时才承诺（精灵表缺失的那趟整夜没有蚋）。
                if (offered == 0)
                    Hint(L10n.T("苇白：航路这阵子清得差不多了，物资点也翻遍了。",
                            "Weibai: The lanes are mostly clear and the caches are picked over.")
                        + (session.HasGnatBountyThisRaid && !session.IsNightNow
                            ? L10n.T("天黑以后再来一趟——起蚋的夜里我这儿还有一张驱蚋的单子。",
                                " Come back after dark — on gnat nights I still have a culling contract for you.")
                            : L10n.T("下次出岛再来看看吧。",
                                " Come and see me again next trip.")));
                return;
            }
            // 交付只在做完之后挂：没做完时点了只回「还差一点」，进度已经写在委托页正文里（ContractsBrief）。
            if (contract.IsComplete) choices.Add(new SkyIslandStoryPresentation.Choice(
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
            // 打完就不再挂：点了只会回一句「风已经散了」。
            if (session.StormResolved) return;
            // 双航标没亮时同样不挂死按钮——把「还差什么」交给正文。
            if (!session.BothBeaconsLit)
            {
                Hint(L10n.T("两端航标都亮起来，栈道上那阵风才会循着光过来。",
                    "The wind out on the boardwalk only comes for the light once both beacons burn."));
                return;
            }
            // 现场条件（站在栈道上、附近没在交战）与点下去的拒绝是同一份判据：挂不出来就把原因写进正文（2026-09-14 审核 F-28 ②）。
            string reason;
            if (!session.CanBeginStoryChallenge("Storm", out reason))
            {
                Hint(reason);
                return;
            }
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
            AddIf(choices, L10n.T("留下来谈 · 出示旧信与航路图",
                "Stay and talk · show the old letter and the route chart"), SkyIslandStoryAction.ReconcileZheling);
            // 两条结局互斥：战胜折翎之后「留下来谈」永久关闭（ReconcileZheling 对已了结的折翎直接拒绝）。
            // 这一项就挨在「留下来谈」下面，按钮上说清楚，免得想走和平线的人手一滑打赢了才发现回不去。
            // 折翎已了结（和解或战胜）之后两项一起收掉。
            if (ChallengeAvailable("Zheling"))
                choices.Add(Challenge(L10n.T("挑战旧航路守卫（战胜后不能再和解）",
                    "Challenge the keeper of the old route (no reconciling once you win)"), "Zheling"));
        }
        private void BellChoices(List<SkyIslandStoryPresentation.Choice> choices)
        {
            AddIf(choices, L10n.T("证明航路安全 · 与钟守和解",
                "Prove the lanes are safe · reconcile with the Bell Keeper"), SkyIslandStoryAction.ReconcileBellKeeper);
            if (ChallengeAvailable("BellKeeper"))
                choices.Add(Challenge(L10n.T("挑战守钟装置", "Challenge the bell engine"), "BellKeeper"));
            AddIf(choices, L10n.T("敲响归航钟", "Ring the Homecoming Bell"), SkyIslandStoryAction.RingHomecomingBell);
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
        /// <summary>
        /// 本次面板里**被隐藏的选项**留下的「下一步」。正文取第一条。
        /// 每次开面板前清空——它是一次构建的临时产物，不是状态。
        /// </summary>
        private readonly List<string> hiddenHints = new List<string>();

        /// <summary>
        /// 正文末尾接上「下一步」——也就是**被隐藏的那些选项**留下的前置说明。
        /// 选项藏起来了，但「还差什么」不能跟着一起消失，否则玩家站在装置前不知道该干嘛。
        /// </summary>
        private string WithNextStep(string body)
        {
            string next = NextStep();
            if (string.IsNullOrEmpty(next)) return body;
            return string.IsNullOrEmpty(body) ? next : body + "\n\n" + next;
        }

        private void Hint(string text)
        {
            if (string.IsNullOrEmpty(text) || hiddenHints.Contains(text)) return;
            hiddenHints.Add(text);
        }

        /// <summary>被隐藏的选项里的第一条「还差什么」；没有就返回 null。</summary>
        private string NextStep()
        {
            return hiddenHints.Count > 0 ? hiddenHints[0] : null;
        }

        /// <summary>
        /// 只在这一步**真的能做**时才挂选项。判据走 <see cref="SkyIslandStoryRules.CanApply"/>，
        /// 与 <c>TryApply</c> 共用同一个 <c>Describe</c>，所以「挂不挂得出来」和「点了会不会被拒」
        /// 不可能分叉。
        ///
        /// 做不了分两种，处理也不同：
        /// - **已经做完** → 什么都不留。「这段群岛见闻已经完成」不是引导，是噪声。
        /// - **前置没满足** → 把「还差什么」收进正文（<see cref="NextStep"/>），那句话本身就是引导。
        ///
        /// **不挂灰掉的占位项**：灰项和挂满一样吵，而且玩家还是会去点它。
        /// </summary>
        private void AddIf(List<SkyIslandStoryPresentation.Choice> choices, string label,
            SkyIslandStoryAction action)
        {
            string blocker;
            if (SkyIslandStoryRules.CanApply(story.Current, action, out blocker))
            {
                Add(choices, label, action);
                return;
            }
            Hint(blocker);
        }

        /// <summary>
        /// 挑战项挂不挂。具名对手已经了结（和解**或**战胜）之后不再挂：点了只会回一句拒绝——
        /// 与 <see cref="SkyIslandStoryData.ZhelingResolved"/> / <c>BellKeeperResolved</c> 同一个事实，不另立判据。
        /// 其余剧情前置与现场条件（钟守要双航标、要走近、附近不能还在交战）问会话的 <c>CanBeginStoryChallenge</c>，
        /// 和点下去之后的拒绝是同一份判据；挂不出来时「还差什么」进正文（2026-09-14 审核 F-28 ②）。
        /// </summary>
        private bool ChallengeAvailable(string id)
        {
            SkyIslandStoryData data = story.Current;
            if (data == null) return false;
            if (string.Equals(id, "Zheling", StringComparison.Ordinal) && data.ZhelingResolved) return false;
            if (string.Equals(id, "BellKeeper", StringComparison.Ordinal) && data.BellKeeperResolved) return false;
            string reason;
            if (session.CanBeginStoryChallenge(id, out reason)) return true;
            Hint(reason);
            return false;
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

        private bool CanContinueDialogue()
        {
            string reason;
            return !disposed && session.CanOpenStoryPanel(out reason);
        }

        internal void Tick()
        {
            if (dialogue != null && dialogue.Active && !dialogue.CanContinue()) dialogue.Dispose();
            SkyIslandNoteBridge.Tick(story.Current);
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
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.StoryUi);
            TickPigeon();
            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Pigeon);
            TickFieldcraft();
            // 玩家在岛上切了语言：纪念物按当前语言重建、信鸽换字（语言在取用时解析，AGENTS §4.4）；旗标没变，不重播回话、不补发纪念品。
            if (displayedFlags >= 0 && L10n.IsChinese != feedbackChinese)
            {
                RebuildFeedback();
                SkyIslandStoryInteractable bird = pigeon != null ? pigeon.GetComponent<SkyIslandStoryInteractable>() : null;
                if (bird != null) bird.Relabel(PigeonTitle());
            }
            if (displayedFlags == story.Current.flags) return;
            // 进岛首帧 displayedFlags 为 -1：存档里早就有的结果只重建世界状态、不重播回话；之后只读真正新增的位。
            int added = displayedFlags < 0 ? 0 : story.Current.flags & ~displayedFlags;
            displayedFlags = story.Current.flags;
            AnnounceCombatOutcomes(added);
            GrantKeepsakes();
            RebuildFeedback();
        }

        /// <summary>纪念物上一次按哪种语言建的。</summary>
        private bool feedbackChinese;

        /// <summary>按持久旗标整组重建纪念物（光 + 只读交互体），标题按当前语言取；旗标变化与岛上切语言共用。</summary>
        private void RebuildFeedback()
        {
            feedbackChinese = L10n.IsChinese;
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
                Beacon("EnemySpawn_F", L10n.T("折翎的旧腰牌", "Zheling's old badge"), BossRushUIColors.Accent, ZhelingBadgeText);
            if (story.Current.Has(SkyIslandStoryFlag.Ending))
                Beacon("Search_H", L10n.T("归航钟声 · 欢迎回家", "The Homecoming Bell · welcome home"), BossRushUIColors.WarningText);
            // 归航船名册：结局之后系在码头灯旁（Lamp_A_02，离码头撤离环 26 m、不进环）；四页内容随本存档的选择变化。
            if (story.Current.Has(SkyIslandStoryFlag.Ending))
                Beacon("Lamp_A_02", L10n.T("归航船 · 船员名册", "Homecoming boat · crew roster"), BossRushUIColors.Accent,
                    () => SkyIslandCrew.Intro(story.Current), CrewChoices);
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
        /// <param name="choices">纪念物面板的选项（归航船名册的四页）；null 时没有选项。每次打开现取。</param>
        private void Beacon(string marker, string label, Color color, Func<string> body = null, Func<List<SkyIslandStoryPresentation.Choice>> choices = null)
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
                        choices != null ? choices() : new List<SkyIslandStoryPresentation.Choice>(),
                        null, SkyIslandUiArt.GetScene(marker));
                }));
        }
        internal void Hide()
        {
            if (dialogue != null) dialogue.Dispose();
            presentation.Dispose();
        }

        /// <summary>
        /// 「收录见闻 / 物证」。收过就不挂——点了只会回「已经收进手记」（2026-09-14 审核 F-06）；判据与手记、官方图鉴镜像是同一个
        /// <see cref="SkyIslandJournal.Recorded"/>。前置没满足（残星瞭台的守卫没清、秘境物证的剧情门没开）同样不挂，「还差什么」进正文。
        /// </summary>
        private void RecordChoice(List<SkyIslandStoryPresentation.Choice> choices, string key, Action recorded)
        {
            if (SkyIslandJournal.Recorded(story.Current, key)) return;
            string overlook = OverlookGuarded(key);
            if (overlook != null) { Hint(overlook); return; }
            SkyIslandStoryAction evidence;
            string blocker;
            if (SkyIslandStoryRules.TrySearchAction(key, out evidence)
                && !SkyIslandStoryRules.CanApply(story.Current, evidence, out blocker))
            {
                Hint(blocker);
                return;
            }
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("收录见闻 / 物证", "Record the note / evidence"), delegate
            {
                string guarded = OverlookGuarded(key);
                if (guarded != null) return guarded;
                string message;
                bool recordedNow = story.RecordSearch(key, out message);
                if (recordedNow && recorded != null) recorded();
                // 权威副本已经写进我们自己的存档；这里把官方笔记图鉴那一条也点亮（镜像，fail-open）。
                if (recordedNow) SkyIslandNoteBridge.Unlock(key);
                return Refreshed(recordedNow, message);
            }));
        }

        /// <summary>残星瞭台的观星镜要先清掉守卫：普通收录、谜题与风晶灯三条路共用这一句门控。</summary>
        private string OverlookGuarded(string key)
        {
            if (key == "Search_S4" && !session.IsEncounterCleared("S4"))
                return L10n.T("先清除瞭台上的守卫，再静下心校准观星镜。",
                    "Clear the guards on the overlook first, then calibrate the telescope in peace.");
            return null;
        }

        /// <summary>
        /// 谜题页正文：谜题场景（只在第一步给）+ 当前这一步的提问。见闻全文不放这里：解开收录之后在官方笔记图鉴里读，
        /// 旧写法把见闻、场景、提问三段拼在一起，一页 80–170 字（2026-09-14 审核 F-24 ②）。
        /// </summary>
        private string PuzzleBody(SkyIslandPuzzle puzzle)
        {
            int index = puzzles.CurrentStep(puzzle);
            string step = L10n.T("第 ", "Step ") + (index + 1) + "/" + puzzle.Steps.Length + L10n.T(" 步 · ", " · ") +
                puzzle.Steps[index].Prompt;
            return index == 0 ? puzzle.Intro + "\n\n" + step : step;
        }

        /// <summary>
        /// 秘境谜题当前这一步的三个选项。答对前进（重开面板换下一步的选项）；最后一步答对才走原来的收录动作
        /// （`RecordSearch` → Find* / RepairTelescope），写屏障失败时保留「已解开」，下次进来就是普通收录页，不用再解一遍。
        /// 答错只写提示、再错直接点破，停在原步可以一直重选：没有惩罚，也不存在解不开的情况。
        /// </summary>
        private void PuzzleChoices(List<SkyIslandStoryPresentation.Choice> choices, SkyIslandPuzzle puzzle, Action recorded)
        {
            string key = puzzle.Key;
            SkyIslandPuzzleOption[] options = puzzle.Steps[puzzles.CurrentStep(puzzle)].Options;
            for (int i = 0; i < options.Length; i++)
            {
                int option = i;
                choices.Add(new SkyIslandStoryPresentation.Choice(options[option].Label, delegate
                {
                    string guarded = OverlookGuarded(key);
                    if (guarded != null) return guarded;
                    string feedback;
                    SkyIslandPuzzleOutcome outcome = puzzles.Choose(puzzle, option, out feedback);
                    if (outcome == SkyIslandPuzzleOutcome.Solved)
                    {
                        string message;
                        bool recordedNow = story.RecordSearch(key, out message);
                        if (recordedNow && recorded != null) recorded();
                        if (recordedNow) SkyIslandNoteBridge.Unlock(key);
                        return Refreshed(recordedNow, feedback + "\n\n" + message);
                    }
                    // 前进要换一组选项，只能重开；重开之后回执写进新面板的正文，所以回执里带上下一步的提问。
                    if (outcome == SkyIslandPuzzleOutcome.Advanced && reopen != null) reopen();
                    return feedback + "\n\n" + PuzzleBody(puzzle);
                }));
            }
        }

        /// <summary>
        /// 群岛手记（首页）。
        ///
        /// 【为什么从 6 项收成 2 项】旧版一打开就是 4 个见闻章节 + 2 项平铺，而且**全部无条件**：
        /// 新档点进去看到的是 20 行 `□ …（尚未收录）` 加 12 行 `□ 第 N 封（尚未收到）` 的空占位。
        /// 现在分两层：
        /// - **20 处见闻整块搬进了官方笔记图鉴**（`SkyIslandNoteBridge`）——回基地也翻得到，
        ///   自带解锁状态与现成 UI，不必在这里再列一遍；
        /// - 剩下的按「来信与人」「岛上的事」两组收进子页。
        ///
        /// 【子菜单怎么做到的】复用既有的 <see cref="reopen"/> 机制：每个开面板的方法都把
        /// `reopen` 指向自己，所以「进子页」就是调另一个开面板的方法，「返回」就是调回父页。
        /// 选项回调的返回值会写进**新开**那个面板的正文，所以每个跳转都要返回目标页自己的导语。
        /// </summary>
        private void OpenJournal()
        {
            if (BlockedByCombat()) return;
            reopen = delegate { OpenJournal(); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("来信与人", "Letters and people"), delegate
            {
                OpenJournalPeople();
                return SkyIslandJournal.PeopleBrief(story.Current);
            }));
            choices.Add(new SkyIslandStoryPresentation.Choice(
                L10n.T("岛上的事", "About the isles"), delegate
            {
                OpenJournalIsles();
                return SkyIslandJournal.IslesBrief(story.Current);
            }));
            journalHomeChoices = choices.Count;
            presentation.Show(L10n.T("群岛手记", "Archipelago journal"),
                SkyIslandJournal.Brief(story.Current), choices, null,
                SkyIslandUiArt.GetJournalBanner());
        }

        /// <summary>手记子页：来信 / 名册 / 纪念品。</summary>
        private void OpenJournalPeople()
        {
            if (BlockedByCombat()) return;
            reopen = delegate { OpenJournalPeople(); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("信鸽来信", "Pigeon letters"),
                () => SkyIslandJournal.Letters(story.Current)));
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("船员名册", "Crew roster"),
                () => SkyIslandJournal.Crew(story.Current)));
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("带在身上的纪念品", "Keepsakes you carry"),
                () => SkyIslandJournal.Keepsakes(story.Current)));
            choices.Add(BackToJournal());
            presentation.Show(L10n.T("群岛手记 · 来信与人", "Journal · letters and people"),
                SkyIslandJournal.PeopleBrief(story.Current), choices, null,
                SkyIslandUiArt.GetJournalBanner());
        }

        /// <summary>手记子页：岛上的灯 / 群岛之物 / 这一趟。</summary>
        private void OpenJournalIsles()
        {
            if (BlockedByCombat()) return;
            reopen = delegate { OpenJournalIsles(); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("岛上的灯", "Lights on the isles"),
                () => SkyIslandLights.Chapter(story.Current, SkyIslandSession.RegionLabel)));
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("群岛之物 · 用处", "What things are for"),
                () => SkyIslandJournal.Uses()));
            // 进度表退到这里：它是一张表，不该占着首页第一眼。
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("这一趟 · 旅程进度", "This run · journey progress"),
                () => SkyIslandJournal.Overview(story.Current, story.Summary)));
            choices.Add(BackToJournal());
            presentation.Show(L10n.T("群岛手记 · 岛上的事", "Journal · about the isles"),
                SkyIslandJournal.IslesBrief(story.Current), choices, null,
                SkyIslandUiArt.GetJournalBanner());
        }

        /// <summary>子页回到手记首页。回调必须返回首页自己的导语——它会被写进新开的那个面板。</summary>
        private SkyIslandStoryPresentation.Choice BackToJournal()
        {
            return new SkyIslandStoryPresentation.Choice(L10n.T("返回手记", "Back to the journal"), delegate
            {
                OpenJournal();
                return SkyIslandJournal.Brief(story.Current);
            });
        }

        /// <summary>
        /// 内容批次三的推进入口。会话在装配末尾才建服务与搜刮点，本对象比它们早建，所以 owner 在第一次就绪的推进里才创建；
        /// 创建失败只记一次日志并放弃——采集与合成是附加内容，不能拖垮剧情与撤离。
        /// </summary>
        private void TickFieldcraft()
        {
            if (fieldcraft == null)
            {
                if (fieldcraftFailed || !session.IsReady) return;
                try { fieldcraft = new SkyIslandFieldcraft(session, story, root.transform); }
                catch (Exception e)
                {
                    fieldcraftFailed = true;
                    Debug.LogWarning("[SkyIsland] 采集与合成装配失败：" + e.Message);
                    return;
                }
            }
            fieldcraft.Tick();
        }

        /// <summary>
        /// 「点起风晶灯」（<see cref="SkyIslandLights"/>）：七处装置各缺一盏，每一盏都是一封信里的请求；亮了就不再挂这一项。
        /// 灯是本存档的持久事实（写进手记），灯旁暖和、夜风吹不透；十盏凑满之后岛上的夜里不再起风。
        /// 按钮上写着「背包里有几件 / 要几件」；残星瞭台那盏同样要先清掉瞭台上的守卫。
        /// </summary>
        private void LightChoice(List<SkyIslandStoryPresentation.Choice> choices, string key)
        {
            SkyIslandLight light = SkyIslandLights.ForMarker(key);
            if (light == null || SkyIslandLights.Lit(story.Current, light.Id)) return;
            // 残星瞭台那盏同样要先清守卫：没清时不挂，原因进正文（与收录同一句，Hint 自己去重）。
            string overlook = OverlookGuarded(key);
            if (overlook != null) { Hint(overlook); return; }
            Func<int, int> count = null;
            if (fieldcraft != null) count = fieldcraft.CountInPack;
            choices.Add(new SkyIslandStoryPresentation.Choice(SkyIslandLights.ChoiceLabel(light, count), delegate
            {
                if (fieldcraft == null)
                    return L10n.T("工具还没摆开，等群岛就绪再来。", "The tools are not laid out yet — come back once the isles are ready.");
                string guarded = OverlookGuarded(key);
                if (guarded != null) return guarded;
                string message;
                bool lit = fieldcraft.LightLamp(light, out message);
                if (lit) story.LogTiming("light", light.Id);
                return Refreshed(lit, message);
            }));
        }

        /// <summary>
        /// 内容批次四「蛙鸣池复蛙」：镜水寺池边（见闻 F_02）夜里捧一团蛙卵（用掉一把云苔纤维），这一趟里带回蛙鸣池（S1）放生。
        /// 每放一团写进本槽手记（`Frog_n`），近水的云蚋永久少一档；放满三团之后来信、名册与居民台词都有回音。
        /// 白天不挂按钮，「要等夜里」与蛙鸣池进度进正文：纯说明性的占位项不挂（AGENTS §4.14；2026-09-15 第五轮截图里白天挂着说明项）。
        /// </summary>
        private void SpawnChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            SkyIslandGnats swarm = fieldcraft != null ? fieldcraft.Gnats : null;
            if (swarm == null || !swarm.Usable || swarm.CarryingSpawn || SkyIslandMosquitoRules.FrogsComplete(story.Current)) return;
            int fiber = fieldcraft.CountInPack(BossRushItemIds.SkyIslandCloudmossFiber);
            int released = SkyIslandMosquitoRules.FrogsReleased(story.Current);
            if (!swarm.NightNow) { Hint(SkyIslandMosquitoRules.SpawnChoice(false, fiber, released)); return; }
            choices.Add(new SkyIslandStoryPresentation.Choice(
                SkyIslandMosquitoRules.SpawnChoice(true, fiber, released), delegate
                {
                    string message;
                    bool taken = swarm.TakeSpawn(out message);
                    return Refreshed(taken, message);
                }));
        }

        /// <summary>蛙鸣池边「把蛙卵放回去」：只在这一趟捧着蛙卵时挂出；先记手记再放下（写屏障下这趟放不下，蛙卵还捧在手里）。</summary>
        private void ReleaseChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            SkyIslandGnats swarm = fieldcraft != null ? fieldcraft.Gnats : null;
            if (swarm == null || !swarm.CarryingSpawn) return;
            choices.Add(new SkyIslandStoryPresentation.Choice(
                SkyIslandMosquitoRules.ReleaseChoice(SkyIslandMosquitoRules.FrogsReleased(story.Current)), delegate
                {
                    string message;
                    bool released = swarm.ReleaseSpawn(out message);
                    return Refreshed(released, message);
                }));
        }

        /// <summary>
        /// 合成面板：本站**已经会做**的配方（渡口工台最多 5 条；它是列表页，AGENTS §4.14 允许到 6 项，面板布局属性测试按最坏 6 条复算），
        /// 按钮上写着「背包里有几件 / 要几件」；还不会做的不挂按钮，在正文末尾各占一行。
        /// 做成了就重开面板刷新件数；材料不够只回话、不重开。面板同样过战斗门。
        /// </summary>
        private void OpenCrafting(SkyIslandCraftStation station)
        {
            if (BlockedByCombat() || fieldcraft == null) return;
            reopen = delegate { OpenCrafting(station); };
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            var locked = new List<string>();
            List<SkyIslandRecipe> recipes = SkyIslandFieldcraftRules.RecipesFor(station);
            // 头目 / 岛主 R1：星工两件套的减耗按开面板那一刻身上穿的件数写进按钮；Craft 按下时再读一次，两边同一条规则。
            int starworksWorn = fieldcraft.StarworksPiecesWorn();
            for (int i = 0; i < recipes.Count; i++)
            {
                SkyIslandRecipe recipe = recipes[i];
                // 配方随剧情解锁：还不会做的不挂按钮（点了必拒，2026-09-14 审核 F-06），写进正文，告诉玩家要等到什么时候。
                if (!SkyIslandFieldcraftRules.Unlocked(recipe, story.Current))
                {
                    locked.Add(SkyIslandFieldcraftRules.LockedLabel(recipe));
                    continue;
                }
                choices.Add(new SkyIslandStoryPresentation.Choice(SkyIslandFieldcraftRules.RecipeLabel(
                    SkyIslandFieldcraftRules.ForWearer(recipe, starworksWorn), fieldcraft.CountInPack), delegate
                {
                    if (fieldcraft == null) return L10n.T("现在没法做东西。", "Nothing can be made right now.");
                    string message;
                    bool crafted = fieldcraft.Craft(recipe, out message);
                    if (crafted) story.LogTiming("craft", recipe.Id);
                    return Refreshed(crafted, message);
                }));
            }
            presentation.Show(SkyIslandFieldcraftRules.StationName(station), CraftingBody(station, locked), choices, null,
                SkyIslandUiArt.GetScene(StationScene(station)));
        }

        /// <summary>合成面板正文：一句开场白 + 背包里的群岛材料 + 还不会做的配方各一行（要等到什么时候）。</summary>
        private string CraftingBody(SkyIslandCraftStation station, List<string> locked)
        {
            Func<int, int> count = null;
            if (fieldcraft != null) count = fieldcraft.CountInPack;
            string body = SkyIslandFieldcraftRules.StationIntro(station) + "\n\n" + SkyIslandFieldcraftRules.PackSummary(count);
            for (int i = 0; locked != null && i < locked.Count; i++) body += "\n" + locked[i];
            return body;
        }

        /// <summary>合成面板的横幅插图借所在装置的那一张（缺图时 SkyIslandUiArt 退成无插图布局）。</summary>
        private static string StationScene(SkyIslandCraftStation station)
        {
            if (station == SkyIslandCraftStation.Dock) return "Search_A";
            return station == SkyIslandCraftStation.Stove ? "Search_C" : "Search_D";
        }

        /// <summary>归航船名册的四页：第一次翻到某页才写进手记；读过的页照当前存档重新生成（选择变了，话也跟着变）。</summary>
        private List<SkyIslandStoryPresentation.Choice> CrewChoices()
        {
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            for (int i = 0; i < SkyIslandCrew.Count; i++)
            {
                int page = i;
                choices.Add(new SkyIslandStoryPresentation.Choice(SkyIslandCrew.Name(page), delegate
                {
                    string message;
                    if (!SkyIslandCrew.Read(story.Current, page)) story.RecordNote(SkyIslandCrew.NoteId(page), out message);
                    return SkyIslandCrew.Page(page, story.Current);
                }));
            }
            return choices;
        }

        /// <summary>
        /// 信鸽：每趟至多一只，带 <see cref="SkyIslandLetters.NextFor"/> 选出的那封信（只取决于存档：收下之前每趟都是同一封、落在同一处）。
        /// 首帧放下，落地字幕推迟 <see cref="PigeonCaptionDelay"/> 游戏秒。存档不可写（写屏障 / 换槽）时这趟不放：收不下的信不该出现。
        /// </summary>
        private void TickPigeon()
        {
            // 收信之后才修好航标/敲钟，也要在本趟送来应时的信；未收的信不被替换。
            if (pigeon == null && displayedFlags >= 0 && displayedFlags != story.Current.flags)
                RearmPigeonIfStoryLetterWaiting();
            if (!pigeonPlaced)
            {
                pigeonPlaced = true;
                SkyIslandLetter letter = story.CanWrite ? SkyIslandLetters.NextFor(story.Current) : null;
                if (letter != null && PlacePigeon(letter)) pigeonLetter = letter;
            }
            if (pigeon == null || pigeonCaptionAt < 0f || Time.time < pigeonCaptionAt) return;
            pigeonCaptionAt = -1f;
            session.Announce(L10n.T("一只信鸽落在了", "A carrier pigeon has landed on ") + SkyIslandSession.RegionLabel(pigeonLetter.Region) +
                L10n.T("，脚上绑着一封信。", ", a letter tied to its leg."), false);
        }

        /// <summary>落点与纪念物同一套算法：锚点外一个交互间距，方位按信的 id 取稳定散列（交互竞争属性测试逐封复算）。找不到净空这趟就不放。</summary>
        private bool PlacePigeon(SkyIslandLetter letter)
        {
            Transform anchor = root.transform.Find(letter.Anchor);
            Vector3 spot;
            if (anchor == null || !SkyIslandRewardCrate.TryFindCratePosition(root.transform, anchor.position,
                SkyIslandLootTables.StableHash(letter.Id) % 360, SkyIslandRewardCrate.InteractableSeparation,
                GameplayDataSettings.Layers.groundLayerMask.value, out spot)) return false;
            pigeon = SkyIslandStoryInteractable.Create(root.transform, spot, "SkyIslandPigeon_" + letter.Id,
                PigeonTitle(), delegate { ReadLetter(letter); });
            GameObject glow = new GameObject("PigeonGlow");
            glow.transform.SetParent(pigeon.transform, false);
            glow.transform.localPosition = Vector3.up * 2f;
            Light light = glow.AddComponent<Light>(); light.type = LightType.Point; light.color = BossRushUIColors.TextPrimary;
            light.intensity = 1.2f; light.range = 9; light.shadows = LightShadows.None;
            return true;
        }

        /// <summary>信鸽头顶的字：建出来时与岛上切语言时取同一句。</summary>
        private static string PigeonTitle() { return L10n.T("信鸽 · 来信", "Carrier pigeon · a letter"); }

        /// <summary>读信：收下才写进手记；信鸽随即飞走（交互体销毁），面板换成没有选项的同一页并附一句回执。</summary>
        private void ReadLetter(SkyIslandLetter letter)
        {
            if (BlockedByCombat()) return;
            var choices = new List<SkyIslandStoryPresentation.Choice>();
            choices.Add(new SkyIslandStoryPresentation.Choice(L10n.T("收下这封信", "Keep the letter"), delegate
            {
                string message;
                if (!story.RecordNote(letter.Id, out message)) return letter.Body + "\n\n" + message;
                ReleasePigeon();
                // 应时的信同趟连送（CR-2026-09-12-020）：只有「有前置且前置已满足」的信才会紧接着再来一只，
                // 无前置的前 8 封仍是一趟一封。规则收在 SkyIslandLetters.NextSameRaidFor 一处。
                RearmPigeonIfStoryLetterWaiting();
                // 第一封信送到时浮舟捎来风标罗盘：收信不改剧情旗标，纪念品在这里补查一次。
                GrantKeepsakes();
                string kept = letter.Body + "\n\n" + L10n.T("（信收进了群岛手记。信鸽扑了扑翅膀，朝云海飞走了。）",
                    "(The letter goes into your archipelago journal. The pigeon shakes out its wings and flies off over the cloud sea.)");
                presentation.Show(letter.Title, kept, new List<SkyIslandStoryPresentation.Choice>(), null,
                    SkyIslandUiArt.GetScene(letter.Anchor));
                return kept;
            }));
            presentation.Show(letter.Title, letter.Body, choices, null, SkyIslandUiArt.GetScene(letter.Anchor));
        }

        /// <summary>
        /// 纪念品（<see cref="SkyIslandItemRules"/>）：条件满足、手记里还没有发放记录就发一件。
        /// 先准备物品实例，再记手记，最后转移物品；资源缺失不耗掉领取资格，写屏障下也不会发出可重复卖钱的东西。
        /// 旧存档第一次进岛同样补发（航徽、噬风之核按已有旗标，罗盘按已收到的信）。
        /// </summary>
        private void GrantKeepsakes()
        {
            if (!story.CanWrite) return;
            SkyIslandKeepsake[] all = SkyIslandItemRules.Keepsakes;
            for (int i = 0; i < all.Length; i++)
            {
                if (!SkyIslandItemRules.Due(story.Current, all[i])) continue;
                string snapshotError;
                if (!story.RequireAssetSnapshot(all[i].NoteId, out snapshotError))
                {
                    Debug.LogWarning("[SkyIsland] 纪念品发放前无法建立实物快照：" + (snapshotError ?? "unknown"));
                    continue;
                }
                string message;
                if (SkyIslandItems.TryGive(all[i].TypeId, all[i].ToStorage,
                    delegate { return story.RecordNote(all[i].NoteId, out message); },
                    delegate { return story.RemoveNote(all[i].NoteId); })) session.Announce(all[i].Caption, false);
                else Debug.LogWarning("[SkyIsland] 纪念品发放未完成，请检查物品资源与群岛记录：" + all[i].NoteId);
            }
        }

        /// <summary>
        /// 风标罗盘的读数：捧着蛙卵时优先指蛙鸣池；其余时候先指本趟还没收下的信鸽，再指最近的主线目标，再指最近的可选目标
        /// （后两者与官方地图上的圈是同一份清单 <see cref="SkyIslandMapMarkers"/>）；都没有了，带着晴岚风晶时指还缺风晶灯的地方，
        /// 否则指这一趟还没采的风晶簇；连风晶簇都采完了才说没有要找的。
        /// </summary>
        internal string CompassReading(Vector3 from)
        {
            // 内容批次四：捧着蛙卵时先指蛙鸣池——这一趟里送到才算数。
            SkyIslandGnats swarm = fieldcraft != null ? fieldcraft.Gnats : null;
            Transform pool = swarm != null && swarm.CarryingSpawn ? root.transform.Find("Search_S1") : null;
            if (pool != null)
            {
                Vector3 toPool = pool.position - from;
                return SkyIslandItemRules.CompassReading(true, toPool.x, toPool.z, L10n.T("蛙鸣池（把蛙卵放回去）", "Frogsong Pool (release the frogspawn)"));
            }
            if (pigeon != null)
            {
                Vector3 toPigeon = pigeon.transform.position - from;
                return SkyIslandItemRules.CompassReading(true, toPigeon.x, toPigeon.z, L10n.T("信鸽落脚的地方", "where the pigeon landed"));
            }
            string what = L10n.T("当前目标", "your current objective");
            Transform target = NearestMarker(SkyIslandMapMarkers.ObjectiveTargets(story.Current), from);
            if (target == null)
            {
                target = NearestMarker(SkyIslandMapMarkers.SideTargets(story.Current), from);
                what = L10n.T("还没了结的支线", "an unfinished side path");
            }
            // 主线支线都没了：带着晴岚风晶就指还缺风晶灯的地方；否则指这一趟还没采的风晶簇（碎晶是点灯的料）。
            if (target == null && fieldcraft != null && fieldcraft.CountInPack(BossRushItemIds.SkyIslandQinglanWindcrystal) > 0)
            {
                target = NearestMarker(SkyIslandLights.UnlitMarkers(story.Current), from);
                what = L10n.T("还缺一盏风晶灯的地方", "a place still missing its windcrystal lamp");
            }
            Vector3 cluster;
            if (target == null && fieldcraft != null && fieldcraft.TryNearestUnharvested(SkyIslandGatherKind.Crystal, from, out cluster))
            {
                Vector3 toCluster = cluster - from;
                return SkyIslandItemRules.CompassReading(true, toCluster.x, toCluster.z,
                    L10n.T("这一趟还没采的风晶簇", "a wind crystal cluster you have not gathered this trip"));
            }
            if (target == null) return SkyIslandItemRules.CompassReading(false, 0, 0, null);
            Vector3 delta = target.position - from;
            return SkyIslandItemRules.CompassReading(true, delta.x, delta.z, what);
        }

        private Transform NearestMarker(IEnumerable<string> names, Vector3 from)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (string name in names)
            {
                Transform marker = root.transform.Find(name);
                if (marker == null) continue;
                Vector3 delta = marker.position - from;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = marker; }
            }
            return best;
        }

        /// <summary>
        /// 收下一封之后，如果**这一趟**还有一封应时的信（前置已满足），就重新武装信鸽落点。
        ///
        /// 落点与字幕走与首封完全相同的那一条路径（<see cref="TickPigeon"/> → <see cref="PlacePigeon"/>），
        /// 只是把一次性闩 <c>pigeonPlaced</c> 重新打开；字幕同样延后 <see cref="PigeonCaptionDelay"/>，
        /// 免得「信收进了群岛手记」的回执和「又一只信鸽落在…」挤在同一秒。
        /// 存档写不进去时 <c>NextFor</c> 那一层本来就不给信，这里不必再判一次。
        /// </summary>
        private void RearmPigeonIfStoryLetterWaiting()
        {
            if (story == null || !story.CanWrite) return;
            if (SkyIslandLetters.NextSameRaidFor(story.Current) == null) return;
            pigeonPlaced = false;
            pigeonCaptionAt = Time.time + PigeonCaptionDelay;
        }

        private void ReleasePigeon()
        {
            if (pigeon != null) UnityEngine.Object.Destroy(pigeon);
            pigeon = null;
            pigeonLetter = null;
            pigeonCaptionAt = -1f;
        }
        public void Dispose()
        {
            disposed = true;
            DetachBossEvents();
            Hide();
            // reopen 捕获了 marker key、recorded 回调与说话人 Transform，会话结束后一并放开。
            reopen = null;
            presentation.Dispose();
            foreach (GameObject go in feedback) if (go != null) UnityEngine.Object.Destroy(go);
            feedback.Clear();
            // 本趟的增益 Modifier、风灯、营火与采集点都在这里摘掉（离岛失效）。
            if (fieldcraft != null) fieldcraft.Dispose();
            fieldcraft = null;
            ReleasePigeon();
            puzzles.Clear();
        }

    }
}
