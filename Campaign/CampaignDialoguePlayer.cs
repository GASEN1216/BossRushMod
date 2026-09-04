// ============================================================================
// CampaignDialoguePlayer.cs - 章节剧情对话播放
// ============================================================================
// **零新对话 UI**：直接复用官方对话框（经 mod 现有 Integration/Dialogue/DialogueManager
// 封装）。官方 DialogueUI 原生带立绘位（portraitSprite → actorPortraitDisplay），
// 而 DialogueActorFactory.Create 已支持传入 portrait，因此让「公告板开口说话」
// 只需要给公告板 GameObject 挂一个 actor，不必自绘任何东西。
//
// 立绘走 CampaignAssetCache（bundle → 开发期 raw PNG → 无立绘）。
// **没有立绘时官方会自动隐藏立绘容器**，对话照常播——因此美术未就绪不阻塞玩法。
//
// 【为什么在关掉公告板面板之后才播】
//   对话要接管输入（BeginDialogueSession 会禁用玩家输入），面板还开着会互相抢，
//   表现为对话点不动或面板点不掉。调用方（CampaignBoardView）先 Close 再调这里。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using NodeCanvas.DialogueTrees;
using UnityEngine;

namespace BossRush
{
    /// <summary>章节剧情对话播放器。</summary>
    internal static class CampaignDialoguePlayer
    {
        /// <summary>对话角色 ID（DialogueTree 引用用，全模块唯一即可）。</summary>
        private const string BrokerActorId = "bossrush_campaign_broker";

        /// <summary>角色名的本地化键。</summary>
        private const string BrokerNameKey = "BossRush_Campaign_Broker_Name";

        /// <summary>承载对话 actor 的常驻 GameObject。</summary>
        private static GameObject _actorHost;

        /// <summary>前冠军的对话角色 ID。</summary>
        private const string ChampionActorId = "bossrush_campaign_champion";

        /// <summary>
        /// 前冠军的角色名键。**复用终章 Boss 的名字键**（CampaignLocalization 已注入
        /// 「冠军之影 / Shadow of the Champion」），让独白抬头与随后 Boss 血条上的名字
        /// 逐字一致；分成两个键会让玩家以为是两个角色。
        /// </summary>
        private const string ChampionNameKey = "BossRush_Campaign_FinalBoss_Name";

        /// <summary>
        /// 承载冠军 actor 的常驻 GameObject。**必须与 _actorHost 分开**：
        /// DialogueActorFactory 的缓存按 GameObject 索引，Create 命中缓存时直接返回，
        /// 完全忽略传入的 actorId / nameKey / portrait。共用一个宿主会让冠军
        /// 顶着中间人的名字和立绘说话。
        /// </summary>
        private static GameObject _championActorHost;

        /// <summary>章节交付后的剧情。fire-and-forget：不阻塞交付流程。</summary>
        internal static void PlayChapterDelivered(CampaignChapterDef def)
        {
            try
            {
                if (def == null) return;
                PlayChapterDeliveredAsync(def).Forget();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 播放交付剧情失败: " + e.Message);
            }
        }

        private static async UniTask PlayChapterDeliveredAsync(CampaignChapterDef def)
        {
            try
            {
                LocalizationHelper.InjectLocalization(
                    BrokerNameKey, L10n.T("中间人", "The Broker"));

                IDialogueActor actor = EnsureActor();
                if (actor == null)
                {
                    // 没有 actor 就退化成一条飘字，不能让玩家完全收不到反馈
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("契约已交付：", "Contract handed in: ")
                        + L10n.T(def.TitleCN, def.TitleEN));
                    return;
                }

                string[][] lines = BuildLinesForChapter(def);
                await DialogueManager.ShowDialogueSequenceBilingual(
                    actor, lines, "BossRush_Campaign_" + def.ChapterId);

                // 剧情播完再解锁线索：先看故事，再拿到"证物"，顺序符合叙事
                CampaignNoteBridge.UnlockClue(def.ClueId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 交付剧情异常: " + e.Message);
            }
        }

        /// <summary>
        /// 终章开战前的冠军独白。由 CampaignFinalBoss 在武装决战后、生成 Boss 前 await：
        /// 玩家点完最后一句，Boss 才现身。
        ///
        /// 【为什么必须是独立的一段序列】DialogueManager.ShowDialogueSequenceBilingual
        /// 的归属是**每段一个 actor**（整段所有台词都发给同一个 actor）。
        /// 要换说话人只能再开一段，不能逐句换。
        /// </summary>
        internal static async UniTask PlayFinalBossPrologueAsync()
        {
            try
            {
                // 防御式注入：CampaignFinalBoss 也注同一个键，但那一步在生成 Boss 时才跑，
                // 晚于本独白。缺了它抬头会显示 *BossRush_Campaign_FinalBoss_Name*。
                LocalizationHelper.InjectLocalization(
                    ChampionNameKey, L10n.T("冠军之影", "Shadow of the Champion"));

                IDialogueActor actor = EnsureChampionActor();
                if (actor == null)
                {
                    // 独白可以没有，但不能把开战流程卡住
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("召唤石上浮出一道影子。", "A silhouette surfaces on the altar stone."));
                    return;
                }

                await DialogueManager.ShowDialogueSequenceBilingual(
                    actor, BuildFinalBossPrologueLines(), "BossRush_Campaign_FinalBossPrologue");
            }
            catch (Exception e)
            {
                // 对话中途抛异常会把输入禁用令牌留在 DisableInput 状态（玩家卡住不能动），
                // 强制收场是既有 NPC 对话的同款兜底。
                DialogueManager.ForceEndDialogue();
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战独白异常: " + e.Message);
            }
        }

        /// <summary>
        /// 终章开战前的冠军独白。四句：出局 → 三年白找 → 找到不挤人的册子 → 开打。
        ///
        /// 【数字必须与既有线索对得上】三十二格 / 第三十三个（clue_ch1、ch1 与 ch6 台词）、
        ///   三面旗（clue_ch3）、十一次（clue_ch4、ch4 台词）、三年（clue_ch2、ch6 台词）。
        ///   改动这里之前先对一遍 CampaignLocalization.InjectClueKeys。
        ///
        /// 【不承诺 Mode H 未实现的机制】名人堂只读、冠军不可招募，
        ///   所以他只说「不会掉」和「不写名字」，不说玩家能在哪里查到他。
        /// </summary>
        private static string[][] BuildFinalBossPrologueLines()
        {
            return new string[][]
            {
                new string[] {
                    "别找名字。第三十三个人进来那天，最下面那行自己走了。我跟着走的。",
                    "Don't look for a name. The day the thirty-third walked in, the bottom line walked out. I went with it." },
                new string[] {
                    "我当掉了战甲。挂过三面旗。给自己写的悬赏令改了十一次数字。三年，没有一处写得下我。",
                    "I pawned the armor. Flew three banners. Rewrote my own bounty eleven times. Three years, and not one of them had room for me." },
                new string[] {
                    "后来我找到一本不挤人的册子。写进去的一条都不会掉。代价是那一页上不写名字。",
                    "Then I found a book that evicts nobody. Nothing written in it ever falls out. The price is that the page carries no name." },
                new string[] {
                    "你来刮碑上那一行。刮不动了。动手吧——影子不认字，它只认打赢过它的。",
                    "You came to scrape that line off the plaque. It won't come off. Draw. A silhouette can't read. It only remembers who beat it." },
            };
        }

        /// <summary>
        /// 章节交付台词。每章两句：中间人对上一件物证的评述 + 指向下一章的钩子。
        /// 终章三句收束全案。
        ///
        /// 写法约束（照 mod 既有文案的调性，见 docs 的文风分析）：
        ///   - 中间人只给事实和下一条线索，从不安慰玩家；
        ///   - 短句，最多两个逗号；不用抒情词，情绪靠留白；
        ///   - 每段收在一个不解释的转折上；
        ///   - 数字精确到荒诞（三十二格、十一次、十七遍）。
        /// </summary>
        private static string[][] BuildLinesForChapter(CampaignChapterDef def)
        {
            switch (def.ChapterId)
            {
                case "ch1":
                    return new string[][]
                    {
                        new string[] {
                            "两张碑拓，我对过了。三十二个名字，一个不多。他不是被谁抹掉的——是排队排出去的。",
                            "I compared the two rubbings. Thirty-two names, exactly. Nobody erased him. He was queued out." },
                        new string[] {
                            "掉出名单的第二天，他把整套战甲当了。去白手起家那边问问，当铺认得那身货。",
                            "The day after he dropped off the list, he pawned his whole kit. Ask around in Bootstrap — the pawnshop knows that gear." }
                    };
                case "ch2":
                    return new string[][]
                    {
                        new string[] {
                            "当票是真的。他不是缺钱，他是想从零再打一遍，重新排进那三十二格。",
                            "The ticket's real. He wasn't short of money. He wanted to climb back into those thirty-two slots from nothing." },
                        new string[] {
                            "没排进去。后来他开始挨个阵营挂旗——只要有人肯把他名字重新写下来就行。",
                            "He didn't make it. After that he started flying every faction's banner, hoping someone would write his name down again." }
                    };
                case "ch3":
                    return new string[][]
                    {
                        new string[] {
                            "三个头目都说他是自己人，三个都叫不出他名字。查无此人，就是这么来的。",
                            "Three bosses all claim him. Not one can say his name. That's how a man becomes 'no such person.'" },
                        new string[] {
                            "所以他自己写了。猎杀名单上有张没发出去的悬赏令，目标栏是他自己的名字。",
                            "So he wrote it himself. There's an unposted bounty notice on the kill list — the target field is his own name." }
                    };
                case "ch4":
                    return new string[][]
                    {
                        new string[] {
                            "赏金改了十一次，改到没人敢接。他要的不是有人来杀他，是那张纸上必须写清他叫什么。",
                            "The sum was rewritten eleven times, up to where nobody would take it. He didn't want a killer. He wanted his name spelled out on something." },
                        new string[] {
                            "最后一封信从疫区寄出。他大概听说了：污染能把一只鸭改成别的东西。",
                            "His last letter came from the quarantine. He'd heard what contamination does — it turns a duck into something else." }
                    };
                case "ch5":
                    return new string[][]
                    {
                        new string[] {
                            "十七遍「我还认得自己吗」。他改错了地方——污染改的是你是什么，不是你叫什么。",
                            "Seventeen times: \"Do I still recognize myself?\" He aimed at the wrong thing. Contamination changes what you are, not what you're called." },
                        new string[] {
                            "他现在在竞技场。不是作为选手回来的。你自己去看吧，我不陪你。",
                            "He's at the arena now. Not as a fighter. Go see for yourself. I'm not coming." }
                    };
                case "ch6":
                    return new string[][]
                    {
                        new string[] {
                            "看清楚了？名人堂进一个挤一个，Boss 图鉴不挤人。写进去的，一条都不会掉。",
                            "Now you see it. The Hall of Fame swaps one in for one out. The bestiary evicts nobody — once you're written in, you stay." },
                        new string[] {
                            "他找了三年一个不会被挤掉的位置。找到了。代价是那个位置上不写名字。",
                            "He spent three years looking for a slot that couldn't be taken from him. He found one. The price is that the slot carries no name." },
                        new string[] {
                            "碑上那一行现在归别人了，没人去刮。案子到此为止，钱你拿好。",
                            "That line on the plaque belongs to someone else now. Nobody scrapes it off. Case closed. Take your money." }
                    };
                default:
                    return new string[][]
                    {
                        new string[] {
                            "契约完成。钱你拿好。",
                            "Contract complete. Take your money." }
                    };
            }
        }

        /// <summary>
        /// 幂等创建对话 actor。挂在一个常驻空物体上，
        /// 不挂公告板实例——公告板会随场景销毁，actor 缓存会留下死引用。
        /// </summary>
        private static IDialogueActor EnsureActor()
        {
            try
            {
                if (_actorHost == null)
                {
                    _actorHost = new GameObject("BossRushCampaignDialogueActor");
                    UnityEngine.Object.DontDestroyOnLoad(_actorHost);
                }

                Sprite portrait = CampaignAssetCache.GetBrokerPortrait();
                return DialogueActorFactory.Create(
                    _actorHost, BrokerActorId, BrokerNameKey, null, portrait);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 对话 actor 创建失败: " + e.Message);
                return null;
            }
        }

        /// <summary>幂等创建冠军对话 actor。宿主与中间人分开，理由见 _championActorHost。</summary>
        private static IDialogueActor EnsureChampionActor()
        {
            try
            {
                if (_championActorHost == null)
                {
                    _championActorHost = new GameObject("BossRushCampaignChampionActor");
                    UnityEngine.Object.DontDestroyOnLoad(_championActorHost);
                }

                Sprite portrait = CampaignAssetCache.GetChampionPortrait();
                return DialogueActorFactory.Create(
                    _championActorHost, ChampionActorId, ChampionNameKey, null, portrait);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 冠军对话 actor 创建失败: " + e.Message);
                return null;
            }
        }

        /// <summary>宿主销毁时的静态缓存复位。</summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                if (_actorHost != null)
                {
                    UnityEngine.Object.Destroy(_actorHost);
                    _actorHost = null;
                }
                if (_championActorHost != null)
                {
                    UnityEngine.Object.Destroy(_championActorHost);
                    _championActorHost = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 复位对话播放器缓存失败: " + e.Message);
            }
        }
    }
}
