// ============================================================================
// NurseAffinityConfig.cs - 护士"羽织"好感度配置
// ============================================================================
// 护士NPC"羽织"的专属好感度配置。
// 实现所有NPC配置接口：INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, 
//                      INPCGiftContainerConfig
// 好感度系统使用 AffinityManager 统一等级配置。
//   
// 羽织的背景故事：
// 羽织是从J-Lab实验室出来的护士，腿上还留着实验的痕迹。在实验室里，她是唯一会在深夜偷偷给叮当带食物、
// 处理伤口的人。实验室崩溃后逃到鸭科夫，建立战地医疗站帮助冒险者。
// 性格参考星露谷物语海莉：初见高冷，逐渐展现温柔。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 护士"羽织"好感度配置
    /// </summary>
    public class NurseAffinityConfig : INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, INPCGiftContainerConfig, INPCRelationshipDialogueConfig
    {
        /// <summary>共享单例，避免多处 new 实例</summary>
        public static readonly NurseAffinityConfig Instance = new NurseAffinityConfig();

        // ============================================================================
        // 常量定义
        // ============================================================================
        
        /// <summary>护士NPC唯一标识符</summary>
        public const string NPC_ID = "nurse_yuzhi";
        
        /// <summary>每日对话好感度增加值</summary>
        public const int DAILY_CHAT_AFFINITY = 30;
        public const string LEVEL3_REWARD_KEY = "reward_calming_drops";
        public const string LEVEL8_REWARD_KEY = "reward_peace_charm";
        
        /// <summary>治疗折扣解锁等级</summary>
        public const int HEAL_DISCOUNT_UNLOCK_LEVEL = 2;
        
        // ============================================================================
        // 好感度等级配置（使用 AffinityManager 统一配置）
        // ============================================================================
        
        /// <summary>
        /// 获取达到指定等级所需的累计好感度点数
        /// 委托给 AffinityManager 的统一配置
        /// </summary>
        public static int GetPointsRequiredForLevel(int level)
        {
            return AffinityManager.GetPointsRequiredForLevel(level);
        }
        
        // ============================================================================
        // INPCAffinityConfig 实现
        // ============================================================================
        
        public string NpcId => NPC_ID;
        public string DisplayName => L10n.T("羽织", "Yu Zhi");
        public int MaxPoints => AffinityManager.UNIFIED_MAX_POINTS;
        public int PointsPerLevel => 250;
        public int MaxLevel => AffinityManager.UNIFIED_MAX_LEVEL;
        
        // 礼物好感度值配置
        private static bool? cachedTextIsChinese;

        private static void EnsureTextLanguage()
        {
            bool isChinese = L10n.IsChinese;
            if (cachedTextIsChinese == isChinese) return;
            cachedTextIsChinese = isChinese;
            _unlocksByLevel = null;
            _positiveBubbles = null;
            _negativeBubbles = null;
            _normalBubbles = null;
            _healSuccessDialogues = null;
            _fullHPDialogues = null;
            _noMoneyDialogues = null;
            _debuffOnlyDialogues = null;
            _idleBubbles = null;
        }

        private static Dictionary<string, int> _giftValues;
        public Dictionary<string, int> GiftValues
        {
            get
            {
                if (_giftValues == null)
                {
                    _giftValues = new Dictionary<string, int>
                    {
                        { "Liked", 80 },      // 喜欢的物品 +80点
                        { "Disliked", -40 },  // 不喜欢的物品 -40点
                        { "Default", 20 }     // 默认/普通物品 +20点
                    };
                }
                return _giftValues;
            }
        }
        
        // 解锁内容
        private static Dictionary<int, string[]> _unlocksByLevel;
        public Dictionary<int, string[]> UnlocksByLevel
        {
            get
            {
                EnsureTextLanguage();
                if (_unlocksByLevel == null)
                {
                    _unlocksByLevel = new Dictionary<int, string[]>
                    {
                        { 2, new[] { L10n.T("10%治疗折扣", "10% Healing Discount") } },
                        { 3, new[] { L10n.T("羽织的叮嘱", "Yu Zhi's Advice"), L10n.T("安神滴剂 x5", "Calming Drops x5") } },
                        { 4, new[] { L10n.T("20%治疗折扣", "20% Healing Discount") } },
                        { 5, new[] { L10n.T("羽织的回忆（上）", "Yu Zhi's Memories (Part 1)") } },
                        { 6, new[] { L10n.T("25%治疗折扣", "25% Healing Discount") } },
                        { 7, new[] { L10n.T("30%治疗折扣", "30% Healing Discount") } },
                        { 8, new[] { L10n.T("\u7FBD\u7EC7\u7684\u62C5\u5FC3", "Yu Zhi's Worry"), L10n.T("\u5E73\u5B89\u62A4\u8EAB\u7B26", "Peace Charm") } },
                        { 9, new[] { L10n.T("40%治疗折扣", "40% Healing Discount") } },
                        { 10, new[] { L10n.T("\u7FBD\u7EC7\u7684\u544A\u767D", "Yu Zhi's Confession") } }
                    };
                }
                return _unlocksByLevel;
            }
        }
        
        // 折扣配置（治疗折扣）
        private static Dictionary<int, float> _discountsByLevel;
        public Dictionary<int, float> DiscountsByLevel
        {
            get
            {
                if (_discountsByLevel == null)
                {
                    _discountsByLevel = new Dictionary<int, float>
                    {
                        { 2, 0.10f },
                        { 4, 0.20f },
                        { 6, 0.25f },
                        { 7, 0.30f },
                        { 9, 0.40f }
                    };
                }
                return _discountsByLevel;
            }
        }
        
        /// <summary>
        /// 获取指定等级的治疗折扣率
        /// </summary>
        public static float GetHealingDiscountForLevel(int level)
        {
            float discount = 0f;
            foreach (var kvp in Instance.DiscountsByLevel)
            {
                if (level >= kvp.Key && kvp.Value > discount)
                {
                    discount = kvp.Value;
                }
            }
            return discount;
        }
        
        // ============================================================================
        // INPCGiftConfig 实现
        // ============================================================================
        
        public int DailyChatAffinity => DAILY_CHAT_AFFINITY;
        
        // 喜欢的物品列表（TypeID -> 额外加成）
        private static Dictionary<int, int> _positiveItems;
        public Dictionary<int, int> PositiveItems
        {
            get
            {
                if (_positiveItems == null)
                {
                    _positiveItems = new Dictionary<int, int>
                    {
                        // 所有NPC都喜欢的物品
                        { 1254, 500 },      // +500好感度
                        { BossRushItemIds.BirthdayCake, 150 },    // +150好感度
                        { DiamondConfig.TYPE_ID, 80 },  // 钻石 +80好感度
                        { DiamondRingConfig.TYPE_ID, DiamondRingConfig.AFFINITY_BONUS }  // 钻石戒指 +500好感度
                    };
                }
                return _positiveItems;
            }
        }
        
        // 喜欢的物品标签列表
        private static HashSet<string> _positiveTags;
        public HashSet<string> PositiveTags
        {
            get
            {
                if (_positiveTags == null)
                {
                    _positiveTags = new HashSet<string>
                    {
                        "Consumable",       // 消耗品（药草、食物等）
                        "Medic",            // 原版医疗物品
                        "Medical"           // 兼容旧标签写法
                    };
                }
                return _positiveTags;
            }
        }
        
        // 不喜欢的物品列表（TypeID -> 额外惩罚）
        private static Dictionary<int, int> _negativeItems;
        public Dictionary<int, int> NegativeItems
        {
            get
            {
                if (_negativeItems == null)
                {
                    _negativeItems = new Dictionary<int, int>
                    {
                        { BrickStoneConfig.TYPE_ID, 60 },  // 砖石（J-Lab实验的象征）-60好感度
                        { 115, 0 },   // 不喜欢的物品
                        { 878, 0 },   // 不喜欢的物品
                        { 1283, 0 }   // 不喜欢的物品
                    };
                }
                return _negativeItems;
            }
        }
        
        // ============================================================================
        // 正向对话气泡（收到喜欢的礼物，30条）
        // ============================================================================
        
        private static string[] _positiveBubbles;
        public string[] PositiveBubbles
        {
            get
            {
                EnsureTextLanguage();
                if (_positiveBubbles == null)
                {
                    _positiveBubbles = new string[]
                    {
                        // 傲娇反应
                        L10n.T("哼...眼光倒还不错，我收下了。", "Hmph... your taste is decent. I'll take it."),
                        L10n.T("你问过谁了？挑得这么准。", "Who'd you ask? You picked that a little too well."),
                        L10n.T("先收进柜子。别盯着我，我在登记。", "Into the cabinet it goes. Stop staring, I'm logging it."),
                        L10n.T("我拿纸包一层再放。受潮就可惜了。", "I'll wrap it in paper before I put it away. Shame if it got damp."),
                        L10n.T("嗯，能用，也好看。难得两样都占。", "Useful and nice to look at. You don't often get both."),
                        L10n.T("脉搏是快了点。刚从药房跑回来的缘故。", "My pulse is up a little. I just ran back from the dispensary."),
                        
                        // 真情流露
                        L10n.T("……挺好看的。谢谢。", "...It's pretty. Thanks."),
                        L10n.T("这个比以前那些冷冰冰的器械顺眼多了。", "This is far gentler on the eyes than those cold instruments I used to work with."),
                        L10n.T("你从哪儿弄来的？这附近可找不着。", "Where'd you find this? You won't get one anywhere around here."),
                        L10n.T("等我把手洗干净再拿。……好，给我。", "Let me wash my hands first. ...Okay. Give it here."),
                        L10n.T("礼物我收了。你手上这道划口哪来的？先上药。", "I'll take it. Where'd that cut on your hand come from? Ointment first."),
                        L10n.T("这个我很喜欢，是真的。", "I really like this. I mean it."),
                        
                        // 感动反应
                        L10n.T("你左脚有点拖。……礼物我收下，坐下让我看看脚。", "You're dragging your left foot. ...I'll take the gift. Sit down and let me see that foot."),
                        L10n.T("我想把它放在抬眼就能看到的地方。...柜子上层就很好。", "I want to put it somewhere I can see at a glance. ...The top shelf should do nicely."),
                        L10n.T("在实验室的时候，从来没人给过我这种东西。", "Back in the lab, no one ever gave me anything like this."),
                        L10n.T("这东西跟药一点关系都没有。……我还是喜欢。", "This has nothing to do with medicine. ...I still like it."),
                        L10n.T("看到这个，值班的烦心事都忘了。...就一会儿。", "This made me forget about my shift. ...Just for a moment."),
                        L10n.T("消毒水熏的。……真的是消毒水。", "Disinfectant fumes. ...It really is the disinfectant."),
                        
                        // 可爱反应
                        L10n.T("……别碰我手腕，我自己知道快了。", "...Don't reach for my wrist. I know it's racing."),
                        L10n.T("下回想送就送。柜子那层我给你腾着。", "Bring another next time if you like. I'll clear that shelf."),
                        L10n.T("药架正好空了一格，放这儿了。……谢了。", "There's one gap on the medicine shelf. It goes there. ...Thanks."),
                        L10n.T("外面天天打仗，你倒还记得给我带东西。...谢了。", "There's a war on out there and you still remember to bring me stuff. ...Thanks."),
                        L10n.T("这个对心情，比很多药都管用。", "This does more for my mood than half my medicine."),
                        L10n.T("冷脸我见多了，你这么好我反而接不住。", "I'm used to cold faces. Nice ones, I don't know what to do with."),
                        
                        // 深情反应
                        L10n.T("我拿镊子夹着看了一圈，一道划痕都没有。你护得比护自己还好。", "Checked it all over with tweezers. Not a scratch. You looked after it better than you look after yourself."),
                        L10n.T("收下了。……这不算贿赂，治疗费照算。", "Accepted. ...It's not a bribe. Treatment still gets billed."),
                        L10n.T("当初拼命从那地方逃出来，看来是逃对了。", "Guess running from that place was the right call after all."),
                        L10n.T("今天换药换到第九个了。这个来得正是时候。", "That's my ninth dressing today. This came at the right time."),
                        L10n.T("平时骂你的那些话，我收回一句。就一句。", "All the things I snap at you? I'll take one back. Just one."),
                        L10n.T("我刚笑了？...你没看见。", "Did I just smile? ...You didn't see that.")
                    };
                }
                return _positiveBubbles;
            }
        }
        
        // ============================================================================
        // 负向对话气泡（收到讨厌的礼物，20条）
        // ============================================================================
        
        private static string[] _negativeBubbles;
        public string[] NegativeBubbles
        {
            get
            {
                EnsureTextLanguage();
                if (_negativeBubbles == null)
                {
                    _negativeBubbles = new string[]
                    {
                        // 傲娇生气
                        L10n.T("哼，你是故意来气我的吧。", "Hmph. You came here just to annoy me, didn't you?"),
                        L10n.T("你这是看不起我，还是看不起我的眼光？", "Are you looking down on me, or just on my taste?"),
                        L10n.T("这种东西你也拿得出手？你的品味真让人担心。", "You actually thought this was worth giving? Your taste is worrying."),
                        L10n.T("拿开，我不想让这东西进我的柜子。", "Take it away. I don't want this anywhere near my cabinet."),
                        L10n.T("行，我记住了。", "Fine. I'll remember this."),
                        
                        // 委屈反应
                        L10n.T("我还以为...至少你会更懂一点。", "I thought... at least you'd understand me a little better than this."),
                        L10n.T("这让我想起以前那些让人反胃的废料。", "This reminds me of the kind of waste that made me sick just looking at it."),
                        L10n.T("这东西脏成这样，你想让我拿它干什么？", "It's filthy. What exactly do you want me to do with it?"),
                        L10n.T("我很失望。不是装的。", "I'm disappointed. And no, I'm not pretending."),
                        L10n.T("别再送我这种东西了，我不是垃圾桶。", "Don't bring me things like this again. I'm not a trash bin."),
                        
                        // 愤怒反应
                        L10n.T("消毒盘的水刚换过，别往这边凑。拿出去扔了。", "I just refilled the antiseptic tray. Keep that away from it. Throw it out."),
                        L10n.T("外面还有三个等着换药的，我没空陪你闹。", "Three people are waiting on dressings. I don't have time for this."),
                        L10n.T("这东西我戴着手套都不想碰。", "I wouldn't touch that with gloves on."),
                        L10n.T("现在我一点都不想理你。", "Right now, I don't want to talk to you at all."),
                        L10n.T("你下次再送这个，我包扎的时候可不会手下留情。", "Bring me this again and I won't be gentle with the bandages."),
                        
                        // 失望反应
                        L10n.T("我原本还有点期待的...算了。", "I was kind of looking forward to this... never mind."),
                        L10n.T("原来在你眼里，我只配收到这种东西。", "So this is the kind of thing you think suits me."),
                        L10n.T("这种玩意儿，丢进废料堆都没人捡。", "Even the trash pits wouldn't bother keeping something like this."),
                        L10n.T("鸭科夫已经够让人疲惫了，别再拿这种东西给我添堵。", "Duckov is exhausting enough already. Don't add to it by putting this in my hands."),
                        L10n.T("我现在真的不想说话。走开。", "I really don't want to talk right now. Go away.")
                    };
                }
                return _negativeBubbles;
            }
        }
        
        // ============================================================================
        // 普通对话气泡（收到普通礼物，30条）
        // ============================================================================
        
        private static string[] _normalBubbles;
        public string[] NormalBubbles
        {
            get
            {
                EnsureTextLanguage();
                if (_normalBubbles == null)
                {
                    _normalBubbles = new string[]
                    {
                        // 傲娇接受
                        L10n.T("收到了。先登记，回头细看。", "Received. I'll log it and look properly later."),
                        L10n.T("又往我手里塞东西……行，放那边。", "Pressing things into my hands again... fine, over there."),
                        L10n.T("至少不是顺手抓的，这个我看得出来。", "At least you didn't grab it on the way here. I can tell."),
                        L10n.T("收下。我脸上本来就没什么表情，别多想。", "Accepted. My face doesn't do much. Don't read into it."),
                        L10n.T("嗯，能用。放门边那个筐里。", "Mm. Usable. Put it in the basket by the door."),
                        L10n.T("放下吧。下回空着手来也行。", "Put it down. You can come empty-handed next time, you know."),
                        
                        // 平淡接受
                        L10n.T("谢谢。……你袖口那块血是谁的？", "Thanks. ...Whose blood is that on your sleeve?"),
                        L10n.T("嗯，我知道了。", "Mm. Got it."),
                        L10n.T("这个说不定能派上点用场。", "This might actually be useful for something."),
                        L10n.T("收到了，谢谢你。", "Got it. Thanks."),
                        L10n.T("我掂了掂，分量够。拆开说不定有能用的。", "Feels solid. Might be something useful in it if I take it apart."),
                        L10n.T("嗯？这个我以前倒是没见过。", "Hm? I haven't seen this one before."),
                        
                        // 好奇反应
                        L10n.T("有意思...这个东西原本是拿来做什么的？", "Interesting... what was this originally meant for?"),
                        L10n.T("让我看看它有没有药用价值。", "Let me see whether it has any medicinal value."),
                        L10n.T("我先闻闻。……没毒，能留。", "Let me smell it first. ...Not toxic. It stays."),
                        L10n.T("这个...也许能做成药膏或者敷料。", "This... might be turned into an ointment or some kind of dressing."),
                        L10n.T("至少比战场上随手捡来的东西体面得多。", "At least it's far more decent than things scavenged off a battlefield."),
                        L10n.T("实验室发的东西都带编号。这个没有，挺好。", "Everything in the lab came with a serial number. This doesn't. I like that."),
                        
                        // 背景相关
                        L10n.T("你左肩压得低，伤着了？……东西放下，外套脱了我看看。", "You're dropping your left shoulder. Hurt it? ...Put that down. Jacket off, let me see."),
                        L10n.T("我不太会收礼，不知道该说什么。……放这儿吧。", "I'm bad at getting gifts. I never know what to say. ...Leave it here."),
                        L10n.T("这是第几样了？我得给你单开一页。", "How many is this now? I'll need a whole page just for you."),
                        L10n.T("我的柜子里又能多放一样东西了。", "Looks like I'll be making room for one more thing in my cabinet."),
                        L10n.T("我会把它放在安全的地方。", "I'll keep it somewhere safe."),
                        L10n.T("又来了？……嗯，坐吧，水是温的。", "Back again? ...Mm. Sit. The water's warm."),
                        
                        // 可爱反应
                        L10n.T("鸭科夫的日子，有你在的时候好像没那么难熬。", "Days in Duckov don't feel quite so hard when you're around."),
                        L10n.T("嗯，收了。外面风大，回去把领子扣上。", "Mm, got it. It's windy out. Button your collar on the way back."),
                        L10n.T("下次挑的时候看看有没有受潮。……开玩笑的，这个挺好。", "Next time, check it's not damp. ...Kidding. This one's fine."),
                        L10n.T("你送的那些我一样都没扔。抽屉快满了。", "I haven't thrown out a single thing you've brought. The drawer's nearly full."),
                        L10n.T("嗯，我收着。下次别淋着雨跑回来送东西，感冒药比这贵。", "I'll keep it. Next time, wait out the rain. Cold meds cost more than this."),
                        L10n.T("总之...谢谢你。", "Anyway... thank you.")
                    };
                }
                return _normalBubbles;
            }
        }
        
        public bool ShowLoveHeartOnPositive => true;
        public bool ShowBrokenHeartOnNegative => true;
        
        public string[] GetAlreadyGiftedDialogues(GiftReactionType lastReaction)
        {
            if (lastReaction == GiftReactionType.Positive)
            {
                return new string[]
                {
                    L10n.T("今天的已经收了。你手里那份，先别拿出来。", "I already got today's. Whatever's in your hand, keep it there."),
                    L10n.T("够了。再收一份，我今天就没心思配药了。", "That's enough. One more and I won't be able to focus on the prescriptions today."),
                    L10n.T("我没有一直在看今天那份礼物...最多只是看了几眼。", "I haven't been staring at today's gift all day... I only glanced at it a few times."),
                    L10n.T("放下吧……不对，拿回去。今天收过了。", "Put it down... no, take it back. I already got one today."),
                    L10n.T("你今天已经把我哄开心了，别得寸进尺。", "You've already succeeded in cheering me up today. Don't push your luck."),
                    L10n.T("礼物我已经放进柜子最里面了...这样才安全。", "I've already put the gift in the back of the cabinet... that's the safest place for it."),
                    L10n.T("那份我拿酒精擦过一遍了，亮得很。今天不收了。", "I wiped the last one down with alcohol. It's gleaming. No more today."),
                    L10n.T("今天已经收过一份了，真不用每趟都拎东西来。", "You already brought something today. You don't have to haul a gift over every run."),
                    L10n.T("上一份我还没想好摆哪儿。你先别添乱。", "I still haven't figured out where the last one goes. Don't make it harder."),
                    L10n.T("明天再带吧，不然我会开始期待得太明显。", "Bring it tomorrow. Otherwise it'll be too obvious I'm looking forward to it."),
                    L10n.T("今天的份已经到了。剩下的工夫，坐着让我看看你那条腿。", "Today's already here. Spend the rest of your visit sitting still so I can check that leg."),
                    L10n.T("我已经替它留好位置了，你不用再塞第二份给我。", "I've already cleared a place for it. You don't need to push a second gift into my hands."),
                    L10n.T("今天就到这儿。药房还有一堆账没对。", "That's it for today. I've still got a pile of dispensary ledgers to check."),
                    L10n.T("你今天的眼光很好，这点我承认。", "Your taste was good today. I'll admit that much."),
                    L10n.T("一天一份，跟吃药一样，多了没用。", "One a day, like any dose. More doesn't help."),
                    L10n.T("你先把明天也平安带过来，比再送一份更重要。", "What matters more than a second gift is that you make it back safely tomorrow too."),
                    L10n.T("今天这份我收好了，放在药柜第三格。", "Today's gift is put away. Third shelf of the medicine cabinet."),
                    L10n.T("这份先揣回去，明天再拿出来。我等得起。", "Pocket that one. Bring it out tomorrow. I can wait."),
                    L10n.T("再送就得登记两行了。我懒得写，明天吧。", "Another one means two lines in the log. Can't be bothered. Tomorrow."),
                    L10n.T("嗯...谢谢你。今天这一次，就已经够让我睡个好觉了。", "Mm... thank you. Just this one gift today is enough to help me sleep well tonight.")
                };
            }
            else if (lastReaction == GiftReactionType.Negative)
            {
                return new string[]
                {
                    L10n.T("今天别再送了，我现在一看到你就会想起那东西。", "Don't give me anything else today. The moment I see you, I think of that thing again."),
                    L10n.T("上一份的账我还没算，今天就到此为止。", "I still haven't settled the score for the last one. That's enough for today."),
                    L10n.T("你要是再塞给我那种东西，我真会生气。", "If you try to shove something like that into my hands again, I really will get angry."),
                    L10n.T("先停一天，我不想让坏心情跟着我值班。", "Let's stop for a day. I don't want to carry this bad mood through my shift."),
                    L10n.T("你以为再送一份，就能把上一份盖过去吗？", "Do you think another gift will somehow erase the last one?"),
                    L10n.T("上次那东西我连碰都不想再碰。", "I don't even want to touch that last thing again."),
                    L10n.T("你的审美最好今晚先补一补，明天再来。", "Your sense of taste should spend tonight recovering. Come back tomorrow."),
                    L10n.T("也许你不是故意的。可我今天就是笑不出来。", "Maybe you didn't mean it. I still can't smile about it today."),
                    L10n.T("想道歉就拿出点像样的诚意。", "If you want to apologize, bring something that actually feels sincere."),
                    L10n.T("别提礼物了。我去洗个手，你在外面等着。", "Don't mention gifts. I'm going to wash my hands. Wait outside."),
                    L10n.T("你今天就老实一点，别再挑战我的耐心。", "Behave yourself for the rest of the day and stop testing my patience."),
                    L10n.T("再送下去，我怕自己会说更难听的话。", "If you keep going, I'm afraid I'll start saying things even harsher than this."),
                    L10n.T("我以前都没这么烦过，别逼我回想那些东西。", "I haven't been this annoyed in ages. Don't make me think about that stuff again."),
                    L10n.T("今天到这里。我现在手重，别逼我给你换药。", "That's it for today. My hands are heavy right now. Don't make me change your dressings."),
                    L10n.T("你要是真在意我，就别再拿这种东西来试我。", "If you really care about me, don't test me with things like that again."),
                    L10n.T("那东西我已经扔出去了。你别再捡回来。", "I already threw it out. Don't go picking it back up."),
                    L10n.T("我不是不能原谅，只是今天还不想。", "It's not that I can't forgive you. I just don't want to today."),
                    L10n.T("先让我把气消了，明天再说。", "Let me cool down first. We can talk again tomorrow."),
                    L10n.T("你现在最该学的，是什么叫“用心”。", "What you need to learn right now is what it means to be thoughtful."),
                    L10n.T("走吧，明天带着更好的东西，或者更好的态度来。", "Go on. Tomorrow, come back with either something better, or a better attitude.")
                };
            }
            else
            {
                return new string[]
                {
                    L10n.T("今天已经送过了，明天再来吧。", "You already gave me something today. Come back tomorrow."),
                    L10n.T("一天一次就够了，我又不是在清点战利品。", "Once a day is enough. I'm not cataloguing battlefield spoils here."),
                    L10n.T("嗯，今天的已经在登记本上了，第七行。", "Mm. Today's is already in the log. Line seven."),
                    L10n.T("今天的礼物还不错，明天可以再认真一点。", "Today's gift was decent. Tomorrow, you can put in a little more thought."),
                    L10n.T("别送太多，我的柜子也是有极限的。", "Don't bring too much. My cabinet does have limits."),
                    L10n.T("今天已经收过了。我知道你来不只是为了送东西。", "I've already accepted something today. I know you didn't come just to give me gifts."),
                    L10n.T("你今天来得够勤，但礼物一天一份就行。", "You've dropped by plenty today, but one gift a day is enough."),
                    L10n.T("好了，收过了。手放下，别又去揭纱布。", "Alright, I've got one. Hands down. Stop peeling at the gauze."),
                    L10n.T("今天那份还没拆完呢。先去把弹匣压满吧。", "I haven't even unwrapped today's yet. Go load your mags."),
                    L10n.T("今天就不用再送了，陪我说会儿话吧。", "No need to give me anything else today. Just stay and talk with me for a while."),
                    L10n.T("坐下歇两分钟再走。礼物是明天的事。", "Sit for two minutes before you go. Gifts are tomorrow's business."),
                    L10n.T("已经收过了，钱留着，别哪天受伤了又说没预算。", "I've already gotten one. Save your money, so you don't show up injured one day and tell me you're broke."),
                    L10n.T("明天吧。今天这份我还得查查有没有过期。", "Tomorrow. I still have to check today's for an expiry date."),
                    L10n.T("你比那些只会来治伤的人有心，这点我知道。", "You're more thoughtful than the people who only ever come here for treatment. I do know that."),
                    L10n.T("行了，你的好意我心领了。", "That's enough. I appreciate the thought."),
                    L10n.T("今天够了。东西收好，别在路上磕坏了。", "Enough for today. Pack that away so it doesn't get banged up on the road."),
                    L10n.T("礼物留到明天吧，今天你人来了就够了。", "Save the next gift for tomorrow. Today, the fact that you came is enough."),
                    L10n.T("我已经把今天收到的东西整理好了，别让我再重摆第二遍。", "I've already put away what I received today. Don't make me reorganize everything a second time."),
                    L10n.T("今天已经收过了。剩下的明天再拿来，我又不会跑。", "You already brought me something today. Save the rest for tomorrow. I'm not going anywhere."),
                    L10n.T("别送了。转过去，我看看你背上那道口子结痂没有。", "Stop. Turn around. Let me see if that cut on your back has scabbed over.")
                };
            }
        }
        // INPCDialogueConfig 实现
        // ============================================================================
        
        public float DialogueBubbleHeight => 2.5f;
        public float DefaultDialogueDuration => 4f;
        
        /// <summary>
        /// 获取指定类型和等级的对话内容
        /// </summary>
        public string GetDialogue(DialogueCategory category, int level)
        {
            string[] dialogues = GetDialoguesForCategory(category, level);
            if (dialogues != null && dialogues.Length > 0)
            {
                int index = UnityEngine.Random.Range(0, dialogues.Length);
                return dialogues[index];
            }
            return L10n.T("......", "......");
        }
        
        /// <summary>
        /// 获取特殊事件对话
        /// </summary>
        public string GetSpecialDialogue(string eventKey, int level)
        {
            switch (eventKey)
            {
                case "heal_success":
                    return GetRandomHealSuccessDialogue();
                case "heal_full_hp":
                    return GetRandomFullHPDialogue();
                case "heal_no_money":
                    return GetRandomNoMoneyDialogue();
                case "heal_debuff_only":
                    return GetRandomDebuffOnlyDialogue();
                default:
                    return null;
            }
        }

        public string GetRelationshipDialogue(string eventKey, int level)
        {
            switch (eventKey)
            {
                case "dialogue_greeting_married":
                case "dialogue_idle_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("你来了就好。诊疗记录先放一边，我想先看看你。", "You're here. The charts can wait, I want to look at you first."),
                        L10n.T("别总站在门口，我会以为你只是路过。过来一点。", "Don't linger by the door, or I'll think you're only passing by. Come closer."),
                        L10n.T("回来了？走两步我看看。……嗯，没瘸。", "You're back? Walk a few steps for me. ...Good. No limp."),
                        L10n.T("医疗站吵了一天。你一进门，我才想起来还没喝水。", "The clinic's been loud all day. You walk in, and I remember I haven't had any water."),
                        L10n.T("要聊天也行，要安静待着也行。你在这里，我就不嫌麻烦。", "We can talk, or we can sit in silence. As long as you're here, I don't mind."),
                    });
                case "gift_positive_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("又带东西了？先放下。手给我，指甲缝里全是泥。", "Brought something again? Put it down. Hands. There's mud packed under your nails."),
                        L10n.T("我把它放在诊疗记录旁边。值班的时候也能看见。", "I'll keep it beside my charts, where I can see it during my shift."),
                        L10n.T("你挑得很认真。我一眼就看出来了。", "You chose this carefully. I could tell at a glance."),
                        L10n.T("谢谢。你总能把这一天变得没那么累。", "Thank you. You always make the day feel less exhausting."),
                    });
                case "gift_normal_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("收下了。你带回来的，我都记在本子最后一页。", "Got it. Everything you bring home goes on the last page of my notebook."),
                        L10n.T("不用每次都带。真想带，就带点干净纱布回来。", "You don't have to bring something every time. If you must, bring back clean gauze."),
                        L10n.T("嗯。路上还想着我，说明你走得不急。挺好。", "Mm. If you had time to think of me out there, you weren't rushing. Good."),
                        L10n.T("礼物倒在其次，你愿意来见我才更重要。", "The gift is secondary. What matters more is that you came to see me."),
                    });
                case "gift_negative_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("这份礼物不太合适...不过是你送的，我会好好说。", "This gift isn't quite right... but since it's from you, I'll say it gently."),
                        L10n.T("下次别这么敷衍，我会担心你是不是太累了。", "Don't be this careless next time. I'll worry you've been pushing yourself too hard."),
                        L10n.T("这个我用不上。……我没生气，你别那副表情。", "I've got no use for this. ...I'm not angry. Don't make that face."),
                    });
                case "gift_already_positive_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("今天收过了。再拿出来，我就当你发烧了，先量体温。", "Already got one today. Pull out another and I'll assume you've got a fever. Thermometer first."),
                        L10n.T("够了。明天的留到明天，今天先把饭吃完。", "Enough. Tomorrow's can wait for tomorrow. Finish your dinner first."),
                        L10n.T("先欠着，明天我再收。今天想多陪你一会儿。", "Save it for tomorrow. Today I'd rather keep you with me a little longer."),
                    });
                case "gift_already_normal_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("一天一份就够了，不然我会分不清该先看礼物还是先看你。", "One gift a day is enough, or I won't know whether to look at the gift or at you first."),
                        L10n.T("留到明天吧。今天你来过，我已经记下了。", "Save it for tomorrow. You showed up today. That counts."),
                        L10n.T("别急着把好东西都塞给我，慢慢来。", "Don't rush to hand me everything nice all at once. Take it slowly."),
                    });
                case "gift_already_negative_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("今天先到这里吧。你要是真想补偿我，明天认真一点。", "Let's stop here for today. If you really want to make it up to me, try a little harder tomorrow."),
                        L10n.T("先别继续塞礼物了，我怕你越送越慌。", "Don't keep pushing gifts at me. I'm afraid you'll only get more flustered."),
                    });
                case "marriage_chapel_required":
                    return L10n.T("……先把教堂建好。我不想在换药室里答应你。",
                        "...Build the chapel first. I won't say yes in the middle of a clinic.");
                case "marriage_divorce":
                    return L10n.T("好。……伤口我治得了，这个治不了。",
                        "Fine. ...I can treat wounds. Not this.");
                case "marriage_bubble_date":
                    return L10n.T("{date}。这个日子，我记下了。",
                        "{date}. I've written this day down.");
                case "marriage_cheat_first":
                    return L10n.T("戒指给了别人？……这次当我没看见，别让我看见第二次。",
                        "A ring for someone else? ...I didn't see that. Don't make me see it twice.");
                case "marriage_cheat_repeat":
                    return L10n.T("又一次。我不生气了，只是累了。",
                        "Again. I'm not angry anymore. Just tired.");
                case "marriage_follow_refused":
                    return L10n.T("今天我得守着诊所，改天。",
                        "I have to mind the clinic today. Another time.");
                case "ring_reject":
                    return L10n.T("太快了。我可不是谁递枚戒指都点头的人。",
                        "Too fast. I don't nod at every ring that's held out to me.");
                case "ring_cheater":
                    return L10n.T("结了婚还到处递戒指？收回去，别让我替你脸红。",
                        "Married, and still handing out rings? Put it away before I blush for you.");
                case "ring_spouse_repeat":
                    return L10n.T("一枚就够了。……有你在，我不缺戒指。",
                        "One is enough. ...With you here, I don't need more.");
                case "gift_fallback":
                    return L10n.T("收到了，谢谢。",
                        "Received. Thanks.");
                case "gift_already_fallback":
                    return L10n.T("今天的已经收过了，明天再来。",
                        "I've already got today's. Come back tomorrow.");
                case "heal_success_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("好了，伤口我处理过了。接下来轮到你答应我别再乱来。", "There. I've treated your wounds. Now it's your turn to promise me you won't be reckless again."),
                        L10n.T("恢复得不错。纱布别乱揭，痒也忍着。", "Healing nicely. Don't peel the gauze, even if it itches."),
                        L10n.T("治疗结束。别急着走，让我再确认一下你的脸色。", "Treatment's done. Don't rush off. Let me check your color one more time."),
                    });
                case "heal_full_hp_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("你现在没事，我反而更高兴。能别受伤当然最好。", "You're fine right now, and that actually makes me happier. Better not to be hurt at all."),
                        L10n.T("今天不用治疗。能看到你完完整整地站在这里，我就满足了。", "No treatment today. Seeing you here in one piece is enough for me."),
                        L10n.T("检查过了，没有问题。继续保持，让我省点心。", "Checked you over. Nothing wrong. Keep it that way and spare me the worry."),
                    });
                case "heal_no_money_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("钱不够也别硬撑。先回去躺着，伤口别乱碰。", "Short on money? Don't push through it. Go lie down, and leave the wound alone."),
                        L10n.T("这次先去筹钱。我不想看你带着伤硬撑。", "Go scrape together the money this time. I don't want to see you tough it out while injured."),
                        L10n.T("我知道你不想让我为难，所以快去想办法，别让我担心太久。", "I know you don't want to make this hard for me, so go find a way and don't leave me worrying for too long."),
                    });
                case "heal_debuff_only_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("伤不重。可你身上这些不对劲的，我看着不安心。先处理掉。", "The wounds aren't bad. The rest of what's off with you bothers me, though. Let me clear it first."),
                        L10n.T("先别动，我把这些乱七八糟的影响清掉，你再去忙。", "Hold still. I'll clear these messy effects off you before you do anything else."),
                        L10n.T("你总说自己没事，可这些小毛病积起来也会让我心烦。", "You always say you're fine, but little problems piling up still drive me crazy with worry."),
                    });
                default:
                    return null;
            }
        }

        private string GetRandomDialogue(string[] dialogues)
        {
            if (dialogues == null || dialogues.Length == 0)
            {
                return null;
            }

            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        // ============================================================================
        // 每日聊天对话（按好感度等级分组）
        // ============================================================================
        
        private string[] GetDialoguesForCategory(DialogueCategory category, int level)
        {
            if (category == DialogueCategory.Greeting || category == DialogueCategory.Idle)
            {
                return GetChatDialoguesForLevel(level);
            }
            return null;
        }
        
        private string[] GetChatDialoguesForLevel(int level)
        {
            if (level <= 2)
            {
                return new string[]
                {
                    L10n.T("需要治疗就快点，我很忙的。", "Need healing? Hurry up, I'm busy."),
                    L10n.T("又是一个莽撞的冒险者...你们怎么都不怕死呢。", "Another reckless adventurer... aren't you afraid of dying?"),
                    L10n.T("别乱碰我的医疗器具，那些可比你贵多了。", "Don't touch my medical equipment, those are worth more than you."),
                    L10n.T("我不是免费义诊的，别想省钱。", "This isn't a free clinic. Don't try to skimp."),
                    L10n.T("你看起来灰头土脸的...真不讲究。", "Look at you, covered in dust... Have some standards."),
                    L10n.T("哈？你来做什么？受伤了还是纯粹来烦我的？", "Huh? What do you want? Are you hurt or just here to bother me?"),
                    L10n.T("鸭科夫这地方……药晾在外头，半天就落一层灰。", "This place... Leave herbs out to dry and they're dusty in half a day."),
                    L10n.T("我这里不欢迎闲人，有事说事。", "Idlers aren't welcome here, state your business."),
                    L10n.T("你的伤口处理得乱七八糟的...真让人看不下去。", "Your wounds are terribly treated... I can't stand looking at this."),
                    L10n.T("别用那种眼神看我。我只管伤，别的不管。", "Don't look at me like that. I treat wounds. Nothing else."),
                    L10n.T("先把血止住再说，逞强只会给我添工作。", "Stop the bleeding first. Acting tough only gives me more work."),
                    L10n.T("冒险可以，别把命当消耗品。", "Adventure if you want, just don't treat your life like something disposable."),
                    L10n.T("你身上这股火药味...离我的药材远一点。", "That gunpowder smell on you... keep it away from my medicine."),
                    L10n.T("要是想聊天，至少先把鞋底的泥擦干净。", "If you want to chat, at least wipe the mud off your boots first."),
                    L10n.T("我见过太多撑不到第二天的人，所以别跟我说“没事”。", "I've seen too many people who didn't make it to the next day, so don't tell me you're 'fine'."),
                    L10n.T("别皱眉，疼就说疼。我又不会笑你。", "Don't frown. If it hurts, say it hurts. I'm not going to laugh at you."),
                    L10n.T("下次再把绷带缠成这样，我就拆了让你自己重缠。", "Wrap a bandage like that again and I'll unwind it and make you redo it yourself."),
                    L10n.T("你们这些冒险者是不是都觉得内脏是一次性的？", "Do adventurers all think internal organs are single-use?"),
                    L10n.T("这里是医疗站，不是让你炫耀伤口的地方。", "This is a clinic, not a place to show off your wounds."),
                    L10n.T("要不是这地方只有我会治人，我才懒得管你们。", "If I weren't the only one here who could patch people up, I wouldn't bother with you lot.")
                };
            }
            else if (level <= 4)
            {
                return new string[]
                {
                    L10n.T("又受伤了？你打架能不能小心点...", "Hurt again? Can you be more careful when fighting..."),
                    L10n.T("嗯...你今天看起来气色还不错，继续保持。", "Hmm... you look well today, keep it up."),
                    L10n.T("战场上要注意防护，受了重伤我也不一定能治好。", "Be careful on the battlefield, I might not be able to heal severe injuries."),
                    L10n.T("你那条绷带缠反了。过来，我给你重缠。", "You wrapped that bandage backwards. Come here, I'll redo it."),
                    L10n.T("你还活着啊。……嗯，挺好。下一位。", "Still alive, huh. ...Good. Next."),
                    L10n.T("今天鸭科夫倒是难得安静...你也少惹点事。", "Duckov is unusually quiet today... you should cause less trouble too."),
                    L10n.T("别老受伤了，药材很贵。……右胳膊抬一下，我看看。", "Stop getting hurt. Medicine's expensive. ...Lift your right arm. Let me see."),
                    L10n.T("你比其他冒险者稍微靠谱那么一点点。就一点点。", "You're slightly more reliable than other adventurers. Just slightly."),
                    L10n.T("嗯？你要陪我聊天？...随便你吧，反正我也没多忙。", "Hm? You want to chat with me? ...Suit yourself, I'm not too busy anyway."),
                    L10n.T("外伤药膏刚熬了一锅，屋里味道冲，你忍着点。", "Just cooked up a batch of wound salve. It reeks in here. Bear with it."),
                    L10n.T("今天伤得比上次轻，看来你终于学会躲一点了。", "You're hurt less badly than last time. Looks like you've finally learned to dodge a little."),
                    L10n.T("止血粉别撒那么多，一层就够。撒厚了，伤口闷着长不好。", "Go easy on the coagulant powder. One layer's enough. Cake it on and the wound can't breathe."),
                    L10n.T("别总一副无所谓的样子，伤口不会因为嘴硬自己好。", "Stop acting like nothing matters. Wounds don't heal just because you're stubborn."),
                    L10n.T("你来得比我想的早。...咳，我是说今天开门早。", "You came earlier than I expected. ...Ahem, I mean I opened early today."),
                    L10n.T("这几天少熬夜，恢复会快一点。", "Stay up less these next few days. You'll recover faster."),
                    L10n.T("你要是愿意，我可以顺便教你简单包扎。", "If you want, I can teach you some basic bandaging too."),
                    L10n.T("别让自己太累，连呼吸都在发虚。", "Don't wear yourself out. Even your breathing sounds tired."),
                    L10n.T("今天没那么狼狈，至少像个能照顾自己的人了。", "You're not as much of a mess today. You almost look like someone who can take care of themself."),
                    L10n.T("有空就坐会儿吧，我这里比外面安静些。", "If you have time, sit for a while. It's quieter here than outside."),
                    L10n.T("你能平安回来，我这边的药柜都像松了口气。", "When you come back safe, even my medicine cabinet seems to breathe easier.")
                };
            }
            else if (level <= 6)
            {
                return new string[]
                {
                    L10n.T("看到你平安回来真好。来，让我看看有没有受伤。", "It's good to see you back safe. Come, let me check for injuries."),
                    L10n.T("你每次过来，我就……松一口气。外头少一个没人管的伤员。", "Every time you come by, I... breathe easier. One less wounded out there with nobody looking after them."),
                    L10n.T("今天的风很舒服呢...偶尔停下来休息也好。", "The breeze is nice today... it's okay to rest once in a while."),
                    L10n.T("在那里的时候，换药都是排队，没人抬头看你一眼。", "Back there, we queued up for dressings. Nobody even looked up at you."),
                    L10n.T("你想听我的故事吗？...也没什么好说的就是了。", "Want to hear my story? ...Not that there's much to tell."),
                    L10n.T("谢谢你常来。这儿不太平，你来了，我值夜都没那么怕了。", "Thanks for coming by so often. It's not safe here. With you around, the night shifts scare me less."),
                    L10n.T("我给你泡了杯药草茶，对身体好的。快喝了。", "I made you some herbal tea, it's good for your health. Drink up."),
                    L10n.T("你知道吗，鸭科夫的夕阳很美。有时候我会在这里看很久...", "You know, the sunset in Duckov is beautiful. Sometimes I watch it for a long time..."),
                    L10n.T("如果...如果有一天你不来了，我大概会有点不习惯吧。", "If... if you stopped coming one day, I'd probably feel a bit lost."),
                    L10n.T("在那边那几年，我以为再也不会跟谁说心里话了...", "Back there, I figured I'd never tell anyone anything real again..."),
                    L10n.T("你每次站在门口，我都会先看你有没有缺胳膊少腿。", "Every time you appear in the doorway, the first thing I do is check whether you're still in one piece."),
                    L10n.T("刚才称错了一味药。跟你说话分了神。……别笑。", "I just weighed out the wrong herb. Got distracted talking to you. ...Don't laugh."),
                    L10n.T("如果你累了，就在这儿坐着，我不赶你。", "If you're tired, just sit here for a while. I'm not chasing you away."),
                    L10n.T("我开始能分清你的脚步声了...别问我为什么。", "I've started recognizing your footsteps... don't ask me why."),
                    L10n.T("你在的时候，这小医疗站才有点像家。", "When you're here, this little clinic almost feels like home."),
                    L10n.T("你随口说的话，我总是记好几天。", "Stuff you say in passing, I end up remembering for days."),
                    L10n.T("别总说是运气好。你能活着回来，是你自己拼出来的。", "Stop calling it luck. You made it back because you fought for it."),
                    L10n.T("我有时会想，今天该给你准备什么药，才会让你少疼一点。", "Sometimes I wonder what I should prepare for you today so you'd hurt a little less."),
                    L10n.T("谢谢你没有把我当成怪物，只当成羽织。", "Thank you for not treating me like a monster. Just as Yu Zhi."),
                    L10n.T("跟你待久了，我居然开始盘算明天吃什么了。", "Hang around you long enough and I start planning tomorrow's dinner. Weird.")
                };
            }
            else if (level <= 8)
            {
                return new string[]
                {
                    L10n.T("你可算来了。……别多想，药柜我整理了三遍，顺便的。", "You finally showed up. ...Don't read into it. I only reorganized the cabinet three times, in passing."),
                    L10n.T("你身上有血腥味。过来，袖子卷起来。", "You smell like blood. Come here. Roll up your sleeve."),
                    L10n.T("今天我手稳，正好给你换药。别乱动。", "My hands are steady today. Good time to change your dressing. Hold still."),
                    L10n.T("出门前让我看一眼伤口。……行了，去吧。", "Let me look at that wound before you head out. ...Fine. Go."),
                    L10n.T("今天外面炮声停得早。我一直在算你什么时候到。", "The shelling stopped early today. I've been working out when you'd get here."),
                    L10n.T("今天腿好些了，不怎么疼。你回来的时候记得先把泥靴脱在门口。", "My legs aren't acting up today. Leave your muddy boots by the door when you come in."),
                    L10n.T("你上次那道口子，我缝了十一针。我都记着。", "That cut you had last time took eleven stitches. I remember every one."),
                    L10n.T("嗯...你在的时候，我的心情总是特别好。这一定是空气好的关系。", "Hmm... my mood is always better when you're here. Must be the fresh air."),
                    L10n.T("以前我在这鬼地方活一天算一天...现在倒想多活几天了。", "I used to just get through each day here... Now I kind of want a few more of them."),
                    L10n.T("下次受伤了第一个来找我，好吗？...不对，最好是不要受伤。", "Come to me first next time you're hurt, okay? ...No wait, best not to get hurt at all."),
                    L10n.T("你右肩比左肩低一点。背包别老挎一边。", "Your right shoulder sits lower than your left. Stop slinging your pack on one side."),
                    L10n.T("你一靠近，我就闻得出你今天有没有逞强。", "The moment you get close, I can tell whether you've been pushing yourself too hard today."),
                    L10n.T("出门前把水壶灌满。别跟我说来不及。", "Fill your canteen before you head out. Don't tell me there's no time."),
                    L10n.T("你每次来，我先看你走路。今天两条腿一样稳，好。", "Every time you come in, I watch how you walk first. Both legs steady today. Good."),
                    L10n.T("要是外面太冷，就来我这里躲一会儿，我给你热药汤。", "If it's too cold outside, come hide here for a bit. I'll warm some herbal soup for you."),
                    L10n.T("我会记得你喜欢什么，也会记得你怕什么。", "I'll remember what you like, and I'll remember what frightens you too."),
                    L10n.T("有你在的时候，连消毒水的味道都没那么难闻了。", "When you're around, even the smell of disinfectant doesn't feel so harsh."),
                    L10n.T("你要是再晚一点来，我大概真的会去门口看看。", "If you came any later, I probably would have gone to the door to look for you."),
                    L10n.T("别人来治伤，我按规矩办。你来，我手都是抖的。", "Other people come in, I do my job. You come in hurt, and my hands shake."),
                    L10n.T("偶尔也让我照顾你，不只是处理伤口那种。", "Let me take care of you sometimes, and not only in the wound-treating way.")
                };
            }
            else if (level <= 9)
            {
                return new string[]
                {
                    L10n.T("我现在最安心的时刻，就是确认你又平安站在我面前。", "The moment I feel safest now is when I see you standing in front of me alive again."),
                    L10n.T("不管外面怎么样，我都在这儿等你回来。说到做到。", "Whatever happens out there, I'll be here when you get back. Count on it."),
                    L10n.T("你来找我的时候，我连语气都会不自觉软下来。真丢脸。", "Whenever you come to see me, even my voice softens on its own. How embarrassing."),
                    L10n.T("以前我怕跟谁走太近，现在倒怕你走太远。", "I used to be scared of getting close to anyone. Now I'm scared of you going too far."),
                    L10n.T("你今天晚了一个钟头。不是我在数，是药房的钟。", "You're an hour late today. I wasn't counting. The dispensary clock was."),
                    L10n.T("我越来越没办法把你当成普通病人看待了。...这话你就当没听见。", "I'm finding it harder and harder to think of you as just another patient. ...Pretend you didn't hear that."),
                    L10n.T("你受伤的时候，我会比平时更凶一点...因为我会慌。", "When you're hurt, I get harsher than usual... because I panic."),
                    L10n.T("最近配药，我会不自觉按你的体重算剂量。……习惯了。", "Lately when I mix a dose, I catch myself working it out for your weight. ...Habit."),
                    L10n.T("你说话的时候老捂着肋下。那边又磕了？", "You keep holding your side when you talk. Bang your ribs again?"),
                    L10n.T("有些话我明明想说，到了嘴边却又咽回去了。...真不像我。", "There are things I clearly want to say, but I keep swallowing them back. ...That's not like me."),
                    L10n.T("如果你累了，就在这里坐一会儿。我不问，你也不用逞强。", "If you're tired, sit here for a while. I won't ask questions, and you don't need to act tough."),
                    L10n.T("以前换完最后一副药，我就把门闩上。最近，我会留一道缝。", "I used to bolt the door after the last dressing of the day. Lately I leave it open a crack."),
                    L10n.T("这地方这么乱，你还天天往我这儿跑。...傻不傻。", "This place is a mess and you still keep coming to see me. ...Idiot."),
                    L10n.T("你不在的时候，这屋里只剩煮药的声音。", "When you're not here, all I hear is the medicine pot bubbling."),
                    L10n.T("我开始攒干净的布了。攒着做什么……还没想好。", "I've started saving clean cloth. What for... I haven't decided yet."),
                    L10n.T("你咳嗽的声音，我隔着两道门都听得出来。", "I can pick out your cough through two doors."),
                    L10n.T("你站在这里的时候，我会觉得这间医疗站没那么冷。", "When you're standing here, this clinic doesn't feel quite so cold."),
                    L10n.T("要是能早几年认识你就好了...算了，当我没说。", "Wish I'd met you a few years earlier... Forget it, pretend I didn't say that."),
                    L10n.T("你不带伤来的时候，我反倒不知道手该往哪儿放。", "When you come in without a wound, I don't know what to do with my hands."),
                    L10n.T("有你在，我晚上做噩梦都少了。", "With you around, I get fewer nightmares.")
                };
            }
            else // level 10
            {
                return new string[]
                {
                    L10n.T("你还愿意像以前一样来找我...我真的很高兴。", "The fact that you still come to see me like always... it really makes me happy."),
                    L10n.T("现在你一走进来，我第一个想到的已经不是“病人来了”。", "Now when you walk in, my first thought is no longer 'a patient is here.'"),
                    L10n.T("不管你有没有受伤，能见到你，我这一天就会好过很多。", "Whether you're hurt or not, seeing you makes my whole day feel lighter."),
                    L10n.T("外面再乱，你回来先洗手。饭在锅里。", "However bad it gets out there, wash your hands when you're back. Dinner's in the pot."),
                    L10n.T("累了就靠近一点吧。我这里有热药汤，也有位置留给你。", "If you're tired, come a little closer. I've got warm herbal soup, and a place saved for you."),
                    L10n.T("我会继续给你留灯，也会继续看你进门那一步稳不稳。这个习惯，我不打算改。", "I'll keep leaving a light on for you, and I'll keep checking how steady your first step through the door is. That habit, I'm keeping."),
                    L10n.T("以前我觉得自己只配站在灯下替别人止血。现在我也想替自己争一个“以后”。", "I used to think all I deserved was to stand under a lamp and stop other people's bleeding. Now I want to claim a future for myself too."),
                    L10n.T("你总说我救了你。……那几年我连镜子都不照，现在会照了。", "You keep saying I saved you. ...For years I wouldn't look in a mirror. Now I do."),
                    L10n.T("你在的时候，我配药都配得比平时快。", "When you're here, I even mix medicine faster. Don't ask."),
                    L10n.T("想跟你一起看几回日落，别老是在门口说两句就走。", "I'd like to watch a few sunsets with you, instead of the usual quick bye at the door."),
                    L10n.T("外面打成什么样我不管，你回来就行。", "I don't care what's going on out there. Just come back."),
                    L10n.T("药柜整理到一半，我会停下来想，你今天走的是哪条路。", "Halfway through sorting the cabinet, I stop and wonder which road you took today."),
                    L10n.T("如果你不想说话，也没关系。陪我安静待一会儿就很好。", "If you don't feel like talking, that's okay. Just staying quietly with me is more than enough."),
                    L10n.T("我现在想“以后”的时候，里面都有你。...别得意。", "When I think about 'later' now, you're always in it. ...Don't get smug."),
                    L10n.T("在我这儿，背不用挺那么直。坐下，靠着。", "You don't have to hold your back so straight in here. Sit. Lean back."),
                    L10n.T("伤口的疼我止得住。我自己腿上的疼，最近也没那么怕了。", "I can stop the pain in a wound. Lately, the pain in my own legs doesn't scare me as much."),
                    L10n.T("还好你当初走进了这医疗站。认真的。", "I'm glad you walked into this clinic that day. I mean it."),
                    L10n.T("以后……也照常来，好不好？换药也好，不换也好。", "From now on... keep coming by like always, okay? Dressing or no dressing."),
                    L10n.T("要是你愿意，以后别只把这里当医疗站，也把它当成能回来的地方。", "If you'd like, don't think of this place as only a clinic anymore. Think of it as somewhere you can come home to."),
                    L10n.T("以前我只给别人换纱布。前几天，我头一回给自己腿上也换了干净的。", "I used to only change other people's gauze. A few days ago, I put clean gauze on my own legs for the first time.")
                };
            }
        }
        
        // ============================================================================
        // 治疗相关特殊对话
        // ============================================================================
        
        private static string[] _healSuccessDialogues;
        private string GetRandomHealSuccessDialogue()
        {
            EnsureTextLanguage();
            if (_healSuccessDialogues == null)
            {
                _healSuccessDialogues = new string[]
                {
                    L10n.T("好了，都处理好了。出去之后小心点。", "All done. Be careful once you're back out there."),
                    L10n.T("治好了。别再这么莽撞了，知道吗？", "Healed. Don't be so reckless again, okay?"),
                    L10n.T("伤口已经处理好了。别等恶化了才想起过来。", "Your wounds are treated. Don't wait until they get worse before coming to me."),
                    L10n.T("嗯，恢复得不错。记得按时换药，别偷懒。", "Mm, you're recovering well. Change your dressings on time, and don't slack off."),
                    L10n.T("治疗完成。你可真会给我添工作。", "Treatment complete. You really do know how to create work for me."),
                    L10n.T("血止住了，呼吸也稳了。现在别乱跑。", "The bleeding's stopped and your breathing's steady. Don't go running around now."),
                    L10n.T("脏东西都清干净了。你之前到底碰了什么鬼东西……", "All cleaned out. What on earth did you touch...?"),
                    L10n.T("处理好了。真撑不住的时候，要第一时间来找我。", "You're patched up. If you really can't hold out, come find me first."),
                    L10n.T("别皱眉，只是包扎而已，还不至于把你送上手术台。", "Stop frowning. It's just bandaging, not enough to put you on an operating table."),
                    L10n.T("身体不是拿来硬扛的。下次记得早点撤。", "Your body isn't for soaking up damage. Pull out sooner next time."),
                    L10n.T("交给我就行。过去学到的东西，至少医术还算有用。", "Leave it to me. Of all the things from the past, at least the medical training is still useful."),
                    L10n.T("嗯，这次伤势不算重。但下次可不一定还有这么走运。", "Mm, your injuries aren't too serious this time. Next time, you may not be this lucky."),
                    L10n.T("好了，又能活蹦乱跳了。前提是你别再自己找伤。", "There, you can move around again. Assuming you don't go looking for new injuries."),
                    L10n.T("钱我收了。下回别等伤口发白了才来。", "Payment's in. Next time, don't wait till the wound's gone pale."),
                    L10n.T("看到你缓过来，我也能松口气了。……手给我，再摸一下脉。", "Seeing you come around, I can breathe again too. ...Give me your wrist. One more pulse check.")
                };
            }
            return _healSuccessDialogues[UnityEngine.Random.Range(0, _healSuccessDialogues.Length)];
        }
        
        private static string[] _fullHPDialogues;
        private string GetRandomFullHPDialogue()
        {
            EnsureTextLanguage();
            if (_fullHPDialogues == null)
            {
                _fullHPDialogues = new string[]
                {
                    L10n.T("你很健康，不需要我的治疗。这样最好。", "You're healthy. You don't need my treatment. That's for the best."),
                    L10n.T("嗯？状态不错。那你来这儿做什么？", "Hm? You're in good shape. So what are you here for?"),
                    L10n.T("看过了，外伤没有，别的毛病也没有。今天用不着我。", "Looked you over. No wounds, nothing off. You don't need me today."),
                    L10n.T("你身上没什么问题。省得我替你收拾残局。", "There's nothing wrong with you. Saves me from cleaning up another mess."),
                    L10n.T("不用治疗。但既然来了，就先歇一会儿吧。", "No treatment needed. But since you're here, sit down and rest for a bit."),
                    L10n.T("完全没事。看来你今天终于学会小心了。", "You're completely fine. Looks like you've finally learned to be careful today."),
                    L10n.T("一点毛病没有。我这儿最难见到的就是这种。", "Not a scratch on you. That's the rarest thing I see in here."),
                    L10n.T("你现在比我这里大部分病人都健康，这可不容易。", "You're healthier than most of the people who come through here. That's not easy in a place like this."),
                    L10n.T("没有伤势就好。要是只是路过，也别把自己再折腾伤了。", "No injuries is good. Even if you're just passing by, don't go getting yourself hurt afterward."),
                    L10n.T("看来今天轮不到我出手。这样也好。", "Looks like my hands aren't needed today. That's a good thing.")
                };
            }
            return _fullHPDialogues[UnityEngine.Random.Range(0, _fullHPDialogues.Length)];
        }
        
        private static string[] _noMoneyDialogues;
        private string GetRandomNoMoneyDialogue()
        {
            EnsureTextLanguage();
            if (_noMoneyDialogues == null)
            {
                _noMoneyDialogues = new string[]
                {
                    L10n.T("治疗费不够。先去赚点钱再来吧，我给你留位置。", "You don't have enough for treatment. Go earn some first, and I'll keep a place for you."),
                    L10n.T("我也想先替你处理，可药材和止血剂都不是凭空来的。", "I'd like to patch you up first too, but medicine and coagulants don't appear out of nowhere."),
                    L10n.T("钱不够。先拿干净布把伤口压住，别让它再渗。", "Not enough money. Press a clean cloth on that wound for now so it stops seeping."),
                    L10n.T("抱歉，我这边也要进药材。攒够了再来，我不会跑。", "Sorry, I need to restock medicine too. Come back when you've saved enough. I'm not going anywhere."),
                    L10n.T("我也要吃饭，也要给药柜补货，不能总给你赊账。", "I need to eat, and I need to restock the medicine cabinet. I can't keep putting it on credit for you."),
                    L10n.T("你的口袋比你的脸色还糟。先去想办法赚钱。", "Your pockets look worse than your complexion. Go figure out how to earn some money first."),
                    L10n.T("如果我一直免费治疗，这个医疗站撑不了多久。", "If I kept treating people for free, this clinic wouldn't survive very long."),
                    L10n.T("没钱也别在外头硬撑。伤口别沾水，凑够了就回来。", "Even if you're broke, don't push your luck out there. Keep the wound dry, and come back once you've got enough."),
                    L10n.T("账就是账，我对谁都一样。快去赚钱吧。", "A bill's a bill. Same for everyone. Go earn some money."),
                    L10n.T("唉...我是真想帮你，但这批药材是阿稳冒着风险送来的，不能白耗。", "Sigh... I do want to help, but Awen risked a lot to deliver this batch of medicine. I can't just burn through it for nothing.")
                };
            }
            return _noMoneyDialogues[UnityEngine.Random.Range(0, _noMoneyDialogues.Length)];
        }
        
        private static string[] _debuffOnlyDialogues;
        private string GetRandomDebuffOnlyDialogue()
        {
            EnsureTextLanguage();
            if (_debuffOnlyDialogues == null)
            {
                _debuffOnlyDialogues = new string[]
                {
                    L10n.T("你身上带着不干净的东西，先让我处理。", "You're carrying something nasty. Let me deal with that first."),
                    L10n.T("血没掉多少。麻烦的是你身上这些不对劲的地方。", "You've barely lost any blood. The trouble is everything else that's off with you."),
                    L10n.T("没流血不代表没事，身上这些脏东西不清掉，迟早出问题。", "Not bleeding doesn't mean you're fine. Leave that mess on you and it'll bite you sooner or later."),
                    L10n.T("这状态不太对劲...你又碰了什么危险东西？", "That condition doesn't look right... what dangerous thing did you touch this time?"),
                    L10n.T("先站稳，我帮你把这些不舒服压下去。", "Hold still. I'll ease these symptoms for you."),
                    L10n.T("你现在这样去战斗，和把自己往坑里送没区别。", "Going back into a fight like this is no different from throwing yourself into a pit."),
                    L10n.T("别嘴硬，这些状态看着不重，拖久了会更难处理。", "Don't act tough. These effects may not look severe, but they'll get harder to deal with if you drag this out."),
                    L10n.T("呼吸放慢一点，我先把这些乱七八糟的影响清掉。", "Slow your breathing. I'll clear out these messy effects first."),
                    L10n.T("你运气不错，还没发展成更糟的样子。现在处理还来得及。", "You're lucky it hasn't turned into something worse yet. There's still time to deal with it now."),
                    L10n.T("这些不对劲的地方我能处理，但你最好别再把自己弄成这样第二次。", "I can deal with what's wrong with you, but you'd better not end up like this a second time.")
                };
            }
            return _debuffOnlyDialogues[UnityEngine.Random.Range(0, _debuffOnlyDialogues.Length)];
        }
        
        // ============================================================================
        // 闲置自言自语（待机时随机显示）
        // ============================================================================
        
        private static string[] _idleBubbles;
        public string[] IdleBubbles
        {
            get
            {
                EnsureTextLanguage();
                if (_idleBubbles == null)
                {
                    _idleBubbles = new string[]
                    {
                        L10n.T("医疗物资还够用吗...得让阿稳再送一批来。", "Do I have enough medical supplies... need to have Awen deliver another batch."),
                        L10n.T("鸭科夫的伤亡率也太高了吧...", "The casualty rate in Duckov is way too high..."),
                        L10n.T("叮当...你现在过得好吗...", "Dingdang... are you doing well now..."),
                        L10n.T("这把钳子需要消毒...还有那个纱布也快用完了。", "This pair of forceps needs sterilizing... and that gauze is almost used up too."),
                        L10n.T("唉，羽毛又乱了。战场上保持体面真的好难...", "Sigh, my feathers are messy again. It's hard to stay presentable on a battlefield..."),
                        L10n.T("以前的事...不想了。先把今天的药配完。", "The past... stop it. Finish today's prescriptions first."),
                        L10n.T("今天有冒险者会来吗……床单得先铺好。", "Will anyone come in today... I should make up the cots first."),
                        L10n.T("这个方子，止痛那味再少放一点试试……", "This formula... maybe try a little less of the painkiller..."),
                        L10n.T("偶尔也想像普通的鸭子一样...安安静静地生活。", "Sometimes I wish I could live quietly... like an ordinary duck."),
                        L10n.T("阿稳上次带来的药草品质不错，得表扬他一下。", "The herbs Awen brought last time were good quality, should compliment him."),
                        L10n.T("要是外面不打了……我这儿大概只剩看风寒了。", "If the fighting ever stopped... I'd probably be left treating colds."),
                        L10n.T("嗯...今天的天气很适合晾药材呢。", "Hmm... today's weather is perfect for drying herbs."),
                        L10n.T("能进我这门的，我就尽量让他活着走出去。", "Anyone who makes it through my door, I try to send back out alive."),
                        L10n.T("那个之前来治疗的冒险者...后来再也没出现过...希望他平安。", "That adventurer who came for treatment before... never showed up again... hope they're safe."),
                        L10n.T("门口那盆花又开了。这种地方居然也养得活。", "The flower by the door bloomed again. Can't believe anything grows here."),
                        L10n.T("纱布、药酒、镇痛剂...嗯，都齐了。希望今天没人伤得太重。", "Gauze, alcohol, painkillers... mm, all stocked. Hopefully nobody comes in too badly hurt today."),
                        L10n.T("那个人今天还没来……再等一刻钟就关门。", "That one hasn't come by today... I'll give it fifteen more minutes, then close up."),
                        L10n.T("灯再亮一点吧，万一有人半夜来求医，至少能看清他的脸。", "Maybe I should keep the lamp a little brighter. If someone comes for help in the middle of the night, at least I'll be able to see their face."),
                        L10n.T("药能治伤，治不了不想活的人。", "Medicine fixes wounds. It can't fix someone who's stopped trying."),
                        L10n.T("等忙完这一阵子……我也该坐下歇两分钟了。", "Once this rush is over... I should sit down for two minutes myself.")
                    };
                }
                return _idleBubbles;
            }
        }
        
        // ============================================================================
        // INPCGiftContainerConfig 实现
        // ============================================================================
        
        public string ContainerTitleKey => "BossRush_NurseGift_ContainerTitle";
        public string GiftButtonTextKey => "BossRush_NurseGift_GiftButton";
        public string EmptySlotTextKey => "BossRush_NurseGift_EmptySlot";
        public bool UseContainerUI => true;
        
        // ============================================================================
        // INPCShopConfig 实现（护士不开商店，但保留接口兼容性）
        // ============================================================================
        
        // 注意：护士NPC不开设商店，核心功能是治疗服务
    }
}
