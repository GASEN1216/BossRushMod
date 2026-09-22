// ============================================================================
// CampaignDialoguePlayer.cs - 章节剧情对话播放
// ============================================================================
// **零新对话 UI**：直接复用官方对话框（经 mod 现有 Integration/Dialogue/DialogueManager
// 封装）。交付台词的说话人是官方 Jeff：名字直接用官方键 Character_Jeff（各语言都有），
// 不给立绘——**没有立绘时官方会自动隐藏立绘容器**，对话照常播。终章独白的冠军之影
// 仍用 CampaignAssetCache 的冠军立绘（bundle → 开发期 raw PNG → 无立绘）。
//
// 【为什么在官方完成面板关掉之后才播】
//   对话要接管输入（BeginDialogueSession 会禁用玩家输入），官方 QuestCompletePanel 还开着会互相抢。
//   调用方（CampaignOfficialQuestClient）等 BossRushUI.IsOfficialHudHidden() 为假再调这里。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using System.Threading;
using NodeCanvas.DialogueTrees;
using UnityEngine;

namespace BossRush
{
    /// <summary>章节剧情对话播放器。</summary>
    internal static class CampaignDialoguePlayer
    {
        /// <summary>杰夫的对话角色 ID（DialogueTree 引用用，全模块唯一即可）。</summary>
        private const string JeffActorId = "bossrush_campaign_jeff";

        /// <summary>说话人名字直接用官方 Jeff 的本地化键（「杰夫 / Jeff」），不另注册。</summary>
        private const string JeffNameKey = "Character_Jeff";

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
        /// 顶着杰夫的名字说话。
        /// </summary>
        private static GameObject _championActorHost;
        private static int _playbackGeneration;
        private static CancellationTokenSource _playbackCancellation;

        internal static void InvalidatePlayback()
        {
            _playbackGeneration++;
            CancellationTokenSource previous = _playbackCancellation;
            _playbackCancellation = null;
            if (previous == null) return;
            try { previous.Cancel(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 取消征程对话失败: " + e.Message);
            }
            finally { previous.Dispose(); }
        }

        private static CancellationToken PlaybackToken()
        {
            if (_playbackCancellation == null) _playbackCancellation = new CancellationTokenSource();
            return _playbackCancellation.Token;
        }

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
            int generation = _playbackGeneration;
            try
            {
                IDialogueActor actor = EnsureActor();
                if (actor == null)
                {
                    // 没有 actor 就退化成一条飘字，不能让玩家完全收不到反馈
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("契约已交付：", "Contract handed in: ")
                        + L10n.T(def.TitleCN, def.TitleEN));
                    CampaignNoteBridge.UnlockClue(def.ClueId);
                    return;
                }

                string[][] lines = BuildLinesForChapter(def);
                await DialogueManager.ShowDialogueSequenceBilingual(
                    actor, lines, "BossRush_Campaign_" + def.ChapterId, PlaybackToken());

                // 剧情播完再解锁线索：先看故事，再拿到"证物"，顺序符合叙事
                if (generation == _playbackGeneration) CampaignNoteBridge.UnlockClue(def.ClueId);
            }
            catch (OperationCanceledException) { }
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
                        L10n.T("报名石上浮出一道影子。", "A silhouette surfaces on the sign-up stone."));
                    return;
                }

                await DialogueManager.ShowDialogueSequenceBilingual(
                    actor, BuildFinalBossPrologueLines(), "BossRush_Campaign_FinalBossPrologue", PlaybackToken());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                // 共享对话管理器按 owner 收尾；这里不能关闭随后开始的其他对话。
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战独白异常: " + e.Message);
            }
        }

        /// <summary>
        /// 终章开战前的冠军独白。四句：这甲不是赢来的 → 上一个人立完名字没走 → 册上只印影子 → 开打。
        ///
        /// 【数字必须与既有线索对得上】六行（clue_ch1）、十一个签过名的（clue_ch5）、八个头目（clue_ch3）、
        ///   三个悬赏（clue_ch4）、第五波（clue_ch5）。改动这里之前先对一遍 CampaignLocalization.InjectClueKeys。
        /// </summary>
        private static string[][] BuildFinalBossPrologueLines()
        {
            return new string[][]
            {
                new string[] {
                    "这套甲我没赢来，是上一个人脱下来的。",
                    "I didn't win this armor. The last one took it off and left it." },
                new string[] {
                    "他也是来立名字的。立完了，没走。",
                    "He came to sign his name too. Signed it, and never left." },
                new string[] {
                    "册上不写我，只印一道影子。你要那一页，从我这儿过。",
                    "The ledger doesn't carry my name. Just a silhouette. You want that page, you come through me." },
                new string[] {
                    "来吧，别留手。",
                    "Come on. Don't hold back." },
            };
        }

        /// <summary>
        /// 章节交付台词：杰夫说的。每章三句：这一行收了 → 下一行要什么 → 解锁的东西怎么用。
        /// 终章三句收束。写法：第一人称、逗号句号、三句以内、不用填充词（教程 §18）。
        /// </summary>
        private static string[][] BuildLinesForChapter(CampaignChapterDef def)
        {
            switch (def.ChapterId)
            {
                case "ch1":
                    return new string[][]
                    {
                        new string[] {
                            "账房收了，第一行算我们的。",
                            "The bookkeeper took it. Line one is ours." },
                        new string[] {
                            "册子上还要填一样东西，粮。",
                            "The ledger wants one more thing. Food." },
                        new string[] {
                            "基地后头那块菜地我让人腾出来了，去建设面板把它建起来，下一章要用。",
                            "I had the plot out back cleared. Build the garden from the construction panel. You'll need it next chapter." }
                    };
                case "ch2":
                    return new string[][]
                    {
                        new string[] {
                            "第二行写了。空手打到第五波，账房抬了下头。",
                            "Line two's written. Bare-handed to wave five. The bookkeeper actually looked up." },
                        new string[] {
                            "战利品别锁在箱子里，摆到基地的枪械展示架或者假人上，看着自己打回来的东西，人扛得住更多。",
                            "Stop locking your trophies in a crate. Put them on the weapon rack or on a dummy. Looking at what you won keeps you standing longer." },
                        new string[] {
                            "下一章账房要看门面，先摆一件，再来找我。",
                            "The next line is about the look of the place. Put one up, then come see me." }
                    };
                case "ch3":
                    return new string[][]
                    {
                        new string[] {
                            "三行了。那块地记我们名下。",
                            "Three lines. That ground's on our name now." },
                        new string[] {
                            "来人看了架子，两眼就走了。两眼就够。",
                            "Their man looked at the rack twice and left. Twice is plenty." },
                        new string[] {
                            "点唱机我加了两首，出击前听一听。第四行要赏金。",
                            "I added two tracks to the jukebox. Play one before you head out. Line four wants bounty money." }
                    };
                case "ch4":
                    return new string[][]
                    {
                        new string[] {
                            "四行了，赏金到账。入册的手续费我垫了。",
                            "Four lines, bounties paid. I covered the listing fee." },
                        new string[] {
                            "剩一行，疫区那场。签过的人没回来过，所以一直空着。",
                            "One line left. The quarantine match. Nobody who signed it came back, so it stayed blank." },
                        new string[] {
                            "去之前先吃一顿。菜地有收成就做一份出击餐。",
                            "Eat before you go. If the garden's in, make yourself a raid meal." }
                    };
                case "ch5":
                    return new string[][]
                    {
                        new string[] {
                            "六行满了。账房把册子翻到我们那一页，让你签。",
                            "Six lines full. The bookkeeper turned to our page for your name." },
                        new string[] {
                            "签之前有一场。守擂的那个穿着历任冠军留下的甲，不露脸，册上只印一道影子。",
                            "There's a bout first. The one holding the ring wears the old champions' armor. No face. The ledger just prints a silhouette." },
                        new string[] {
                            "报名石会立在竞技场里等你，别先点路牌。按住它他就来。",
                            "A sign-up stone will be waiting for you in the arena. Don't touch the sign first. Hold the stone and he comes." }
                    };
                case "ch6":
                    return new string[][]
                    {
                        new string[] {
                            "名字写上去了，上了墨，擦不掉。",
                            "The name's on the page. In ink. Won't rub out." },
                        new string[] {
                            "那套甲还挂在擂台上，等下一个不打算回家的人来穿。你有地方回。",
                            "The armor still hangs in the ring, waiting on the next one who isn't planning to go home. You've got somewhere to go home to." },
                        new string[] {
                            "菜地该收了，架子上再摆一件。回去吃饭吧。",
                            "The garden's ready to pick. Put one more piece on the rack. Go eat." }
                    };
                default:
                    return new string[][]
                    {
                        new string[] {
                            "这一行收了。钱你拿好。",
                            "That line's in. Take your money." }
                    };
            }
        }

        /// <summary>
        /// 幂等创建杰夫的对话 actor。挂在一个常驻空物体上，
        /// 不挂官方 Jeff 实例——他会随场景销毁，actor 缓存会留下死引用。
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

                // 杰夫没有 Mod 立绘：传 null，官方自动隐藏立绘位
                return DialogueActorFactory.Create(
                    _actorHost, JeffActorId, JeffNameKey, null, null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 对话 actor 创建失败: " + e.Message);
                return null;
            }
        }

        /// <summary>幂等创建冠军对话 actor。宿主与杰夫分开，理由见 _championActorHost。</summary>
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
            InvalidatePlayback();
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
