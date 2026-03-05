// ============================================================================
// NurseAffinityConfig.cs - 护士"羽织"好感度配置
// ============================================================================
// 护士NPC"羽织"的专属好感度配置。
// 实现所有NPC配置接口：INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, 
//                      INPCGiftContainerConfig
// 好感度系统使用 AffinityManager 统一等级配置。
//   
// 羽织的背景故事：
// 羽织原本是J-Lab实验室的高级医疗研究员，是唯一一个会在深夜偷偷给叮当带食物、
// 处理伤口的研究员。实验室崩溃后逃到鸭科夫，建立战地医疗站帮助冒险者。
// 性格参考星露谷物语海莉：初见高冷，逐渐展现温柔。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 护士"羽织"好感度配置
    /// </summary>
    public class NurseAffinityConfig : INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, INPCGiftContainerConfig
    {
        private static readonly NurseAffinityConfig Shared = new NurseAffinityConfig();

        // ============================================================================
        // 常量定义
        // ============================================================================
        
        /// <summary>护士NPC唯一标识符</summary>
        public const string NPC_ID = "nurse_yuzhi";
        
        /// <summary>每日对话好感度增加值</summary>
        public const int DAILY_CHAT_AFFINITY = 30;
        
        /// <summary>治疗折扣解锁等级</summary>
        public const int HEAL_DISCOUNT_UNLOCK_LEVEL = 3;
        
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
                if (_unlocksByLevel == null)
                {
                    _unlocksByLevel = new Dictionary<int, string[]>
                    {
                        { 2, new[] { L10n.T("额外对话", "Extra Dialogues") } },
                        { 3, new[] { L10n.T("5%治疗折扣", "5% Healing Discount") } },
                        { 4, new[] { L10n.T("10%治疗折扣", "10% Healing Discount") } },
                        { 5, new[] { L10n.T("羽织的回忆（上）", "Yu Zhi's Memories (Part 1)") } },
                        { 6, new[] { L10n.T("15%治疗折扣", "15% Healing Discount") } },
                        { 7, new[] { L10n.T("羽织的护身符", "Yu Zhi's Talisman") } },
                        { 8, new[] { L10n.T("20%治疗折扣", "20% Healing Discount") } },
                        { 9, new[] { L10n.T("羽织的回忆（下）", "Yu Zhi's Memories (Part 2)") } },
                        { 10, new[] { L10n.T("30%治疗折扣", "30% Healing Discount"), L10n.T("特殊对话", "Special Dialogue") } }
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
                        { 3, 0.05f },   // 5%折扣
                        { 4, 0.10f },   // 10%折扣
                        { 6, 0.15f },   // 15%折扣
                        { 8, 0.20f },   // 20%折扣
                        { 10, 0.30f }   // 30%折扣
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
            foreach (var kvp in Shared.DiscountsByLevel)
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
                        { 500002, 150 },    // +150好感度
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
                        "Medical"           // 医疗物品
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
                if (_positiveBubbles == null)
                {
                    _positiveBubbles = new string[]
                    {
                        // 傲娇反应
                        L10n.T("哼...还、还行吧，我勉强收下了。", "Hmph... it's o-okay, I'll reluctantly accept it."),
                        L10n.T("你...你怎么知道我喜欢这个的！", "H-how did you know I like this!"),
                        L10n.T("别、别误会！我只是觉得扔掉可惜！", "D-don't get the wrong idea! I just think it's a waste to throw away!"),
                        L10n.T("这个...我会好好保管的，别多想！", "This... I'll keep it safe, don't overthink!"),
                        L10n.T("哼，算你有点品位...", "Hmph, you have some taste..."),
                        L10n.T("我才、才没有很开心呢！", "I'm n-not happy at all!"),
                        
                        // 真情流露
                        L10n.T("哇...好漂亮。谢、谢谢你。", "Wow... so pretty. Th-thank you."),
                        L10n.T("这个比J-Lab那些冰冷的器械好看多了...", "This is much prettier than those cold instruments at J-Lab..."),
                        L10n.T("你是第一个...认真给我挑礼物的人。", "You're the first person to... seriously pick a gift for me."),
                        L10n.T("我...我好开心。真的。", "I'm... I'm so happy. Really."),
                        L10n.T("太棒了！...啊不对，我是说还行吧。", "Amazing! ...No wait, I mean it's alright."),
                        L10n.T("嘿嘿，这个我超级喜欢！", "Hehe, I super like this!"),
                        
                        // 感动反应
                        L10n.T("你真懂女孩子的心嘛~ ...我说什么了？", "You really understand a girl's heart~ ...What did I say?"),
                        L10n.T("这个我要放在枕边！...我、我是说放在柜子里！", "I'll put this by my pillow! ...I mean in the cabinet!"),
                        L10n.T("在实验室的时候从来没收到过这么好的东西...", "I never received anything this nice at the lab..."),
                        L10n.T("你...你不觉得给一个护士送这个很奇怪吗？...但我很高兴。", "Don't you think it's weird giving this to a nurse? ...But I'm happy."),
                        L10n.T("这朵花好美...像鸭科夫少有的和平时光。", "This flower is beautiful... like the rare peaceful moments in Duckov."),
                        L10n.T("呜...我眼睛进沙子了，才不是感动哭了！", "Ugh... I got sand in my eyes, I'm not crying from emotion!"),
                        
                        // 可爱反应
                        L10n.T("我的心跳好快...一定是你吓到我了！", "My heart is beating so fast... you must have startled me!"),
                        L10n.T("你、你下次还可以再送...如果你想的话。", "Y-you can give more next time... if you want to."),
                        L10n.T("我会永远记住这份心意的！虽然嘴上不会说！", "I'll always remember this kindness! Even if I won't say it!"),
                        L10n.T("谢谢你让我在这个到处是硝烟的地方...感受到温暖。", "Thank you for making me feel warmth... in this smoke-filled place."),
                        L10n.T("这比任何药都有效...对心情来说。", "This is more effective than any medicine... for my mood."),
                        L10n.T("我在J-Lab见惯了冷漠...你的温柔让我不知所措。", "I was used to coldness at J-Lab... your kindness leaves me flustered."),
                        
                        // 深情反应
                        L10n.T("这个真的很美。就像...算了，不说了。", "This is really beautiful. Just like... never mind."),
                        L10n.T("哼，以后我给你打折多一点吧。...才不是因为这个！", "Hmph, I'll give you a bigger discount from now on. ...Not because of this!"),
                        L10n.T("你让我觉得...当初逃离J-Lab是对的决定。", "You make me feel... that leaving J-Lab was the right decision."),
                        L10n.T("每次收到你的礼物，都是我在鸭科夫最开心的时刻。", "Every time I receive your gift, it's my happiest moment in Duckov."),
                        L10n.T("我虽然嘴巴不饶人...但心里真的很感激你。", "My tongue may be sharp... but I'm truly grateful to you in my heart."),
                        L10n.T("这一刻...我是真心在笑。", "Right now... I'm truly smiling.")
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
                if (_negativeBubbles == null)
                {
                    _negativeBubbles = new string[]
                    {
                        // 傲娇生气
                        L10n.T("哼！你是故意的吧！", "Hmph! You did this on purpose!"),
                        L10n.T("你、你是不是看不起我！", "A-are you looking down on me!"),
                        L10n.T("这种东西也拿得出手？你的品味真让人担忧。", "You can actually give this? Your taste is concerning."),
                        L10n.T("我才不稀罕这种东西！拿走！", "I don't want this kind of thing! Take it away!"),
                        L10n.T("哼，我要记住这笔账的。", "Hmph, I'll remember this."),
                        
                        // 委屈反应
                        L10n.T("呜...我还以为你是个有眼光的人...", "Ugh... I thought you had good taste..."),
                        L10n.T("这让我想起J-Lab那些令人厌恶的实验废料...", "This reminds me of those disgusting lab waste from J-Lab..."),
                        L10n.T("你也觉得一个护士只配收到这种垃圾吗...", "Do you think a nurse only deserves this kind of trash..."),
                        L10n.T("我很失望。真的。", "I'm disappointed. Really."),
                        L10n.T("不要再送我这种东西了，我警告你。", "Don't give me this kind of thing again, I'm warning you."),
                        
                        // 愤怒反应
                        L10n.T("这是什么破烂！你在侮辱我的专业！", "What is this junk! You're insulting my profession!"),
                        L10n.T("你在逗我玩吗！我可没空陪你闹！", "Are you messing with me! I don't have time for this!"),
                        L10n.T("作为一个注重品质的护士，我拒绝接受这个。", "As a nurse who cares about quality, I refuse to accept this."),
                        L10n.T("哼！我不想理你了！", "Hmph! I don't want to talk to you anymore!"),
                        L10n.T("你下次再送这个，我就...我就加倍收你治疗费！", "If you give this again, I'll... I'll double your healing fee!"),
                        
                        // 失望反应
                        L10n.T("我还以为...算了，别说了。", "I thought... forget it, don't say anything."),
                        L10n.T("原来在你眼里我就值这个...", "So this is what I'm worth to you..."),
                        L10n.T("这种东西在J-Lab的垃圾堆里到处都是...", "This kind of thing is everywhere in J-Lab's trash heap..."),
                        L10n.T("我一个人在鸭科夫已经够辛苦了...你就不能体贴一点吗。", "It's already hard enough being alone in Duckov... can't you be more considerate?"),
                        L10n.T("无语。走开。", "Speechless. Go away.")
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
                if (_normalBubbles == null)
                {
                    _normalBubbles = new string[]
                    {
                        // 傲娇接受
                        L10n.T("哼，勉为其难收下吧。", "Hmph, I'll reluctantly accept it."),
                        L10n.T("不、不是我想要，是你非要给的！", "I-it's not that I want it, you insisted!"),
                        L10n.T("算你有心了...虽然一般般。", "At least you're thoughtful... though it's average."),
                        L10n.T("收下了，别指望我说谢谢。", "Accepted, don't expect a thank you."),
                        L10n.T("嗯...还行吧，我见过更好的。", "Hmm... it's okay, I've seen better."),
                        L10n.T("你不用特意送我东西的...但既然送了我就收了。", "You don't have to give me things... but since you did, I'll take it."),
                        
                        // 平淡接受
                        L10n.T("谢谢...我会收好的。", "Thanks... I'll keep it safe."),
                        L10n.T("嗯，知道了。", "Hmm, noted."),
                        L10n.T("这个我可以用在医疗上...大概。", "I might be able to use this for medical purposes... maybe."),
                        L10n.T("收到了，谢谢。", "Received, thanks."),
                        L10n.T("还不错，我喜欢收集各种东西。", "Not bad, I like collecting various things."),
                        L10n.T("嗯？这是什么？我在J-Lab没见过...", "Hm? What's this? Haven't seen it at J-Lab..."),
                        
                        // 好奇反应
                        L10n.T("有意思...这个东西有什么用途？", "Interesting... what's this for?"),
                        L10n.T("让我研究一下有没有药用价值...", "Let me study if this has medicinal value..."),
                        L10n.T("作为护士，我对各种材料都挺好奇的。", "As a nurse, I'm curious about all kinds of materials."),
                        L10n.T("这个...让我想想能不能做成药膏。", "This... let me think if I can make it into ointment."),
                        L10n.T("比战场上捡到的东西体面多了...", "Much more decent than things picked up on the battlefield..."),
                        L10n.T("在J-Lab的时候，从来没人送过东西...", "When I was at J-Lab, no one ever gave anything..."),
                        
                        // 背景相关
                        L10n.T("你...你为什么要对我这么好？...算了别回答了。", "Why... why are you so nice to me? ...Forget it, don't answer."),
                        L10n.T("我不太习惯收礼物...但谢谢你的心意。", "I'm not used to receiving gifts... but thank you for the thought."),
                        L10n.T("嘿嘿...又有新东西了~ 啊不对，我是说还好吧。", "Hehe... something new~ No wait, I mean it's fine."),
                        L10n.T("我的收藏又多了一件。", "My collection has one more item."),
                        L10n.T("我会把它放在安全的地方的。", "I'll put it somewhere safe."),
                        L10n.T("你还记得来看我...我有点高兴。就一点点。", "You remembered to visit me... I'm a little happy. Just a little."),
                        
                        // 可爱反应
                        L10n.T("鸭科夫的日子有你在好像也没那么难熬...", "Days in Duckov don't seem so hard with you around..."),
                        L10n.T("嗯，这份心意我记住了。", "Hmm, I'll remember this gesture."),
                        L10n.T("下次来可以带点更好的...我、我是开玩笑的啦！", "Next time bring something better... I'm just kidding!"),
                        L10n.T("你送的东西我都有保留哦...才不是因为舍不得扔！", "I keep everything you give me... not because I can't bear to throw them away!"),
                        L10n.T("做护士虽然辛苦，但收到礼物还是挺开心的。", "Being a nurse is tough, but receiving gifts is still quite nice."),
                        L10n.T("总之...谢了。", "Anyway... thanks.")
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
                    L10n.T("今天的礼物我已经收到了...还、还不错啦！", "I already received today's gift... it's n-not bad!"),
                    L10n.T("你今天已经送过了！别以为我会更加喜...随便啦！", "You already gave today! Don't think I'll like you mo... whatever!"),
                    L10n.T("我才没有一直在看今天的礼物呢！", "I haven't been looking at today's gift all the time!"),
                    L10n.T("今天的礼物...我很喜欢，但你一天送一次就好了。", "Today's gift... I like it, but once a day is enough."),
                    L10n.T("你今天的表现我很满意~...啊说漏嘴了。", "I'm satisfied with your performance today~... oops, said too much."),
                    L10n.T("今天的礼物我超级喜欢！谢谢你~", "I love today's gift! Thank you~"),
                    L10n.T("嘿嘿，今天的礼物我已经放好了~等等你想干嘛？", "Hehe, I've put away today's gift~wait, what are you trying to do?"),
                    L10n.T("你送的东西让我想起了一些美好的回忆...", "Your gift reminded me of some beautiful memories..."),
                    L10n.T("在J-Lab从来没收到过这么好的东西呢...谢谢。", "Never received anything this nice at J-Lab... thanks."),
                    L10n.T("今天因为你的礼物，我心情特别好~", "My mood is especially good today because of your gift~"),
                    L10n.T("今天的礼物太棒了！明天也要送哦！", "Today's gift is amazing! Give more tomorrow!"),
                    L10n.T("我把今天的礼物放在最珍贵的地方了！", "I put today's gift in the most precious place!"),
                    L10n.T("我今天心情超好~一定是因为天气...才不是因为你的礼物！", "I'm in a super good mood today~must be the weather...not your gift!"),
                    L10n.T("你是我见过最懂我的人...不、不许得意！", "You understand me the best... d-don't get cocky!"),
                    L10n.T("你的礼物比任何药都让人精神百倍呢。", "Your gift is more invigorating than any medicine.")
                };
            }
            else if (lastReaction == GiftReactionType.Negative)
            {
                return new string[]
                {
                    L10n.T("你今天送的什么破烂...我还没消气呢！", "What junk did you give today... I'm still angry!"),
                    L10n.T("哼，上次的账我还没算呢，就别送了！", "Hmph, I haven't settled the last score, so don't bother giving!"),
                    L10n.T("你要是再送那种东西，我真的会生气的！", "If you give that kind of thing again, I'll really get angry!"),
                    L10n.T("今天就不要送了...我怕我控制不住脾气。", "Don't give anything today... I'm afraid I can't control my temper."),
                    L10n.T("哼！你以为送垃圾我就会原谅你吗！", "Hmph! You think I'll forgive you for giving trash!"),
                    L10n.T("上次的礼物我已经扔掉了！...大概。", "I already threw away last time's gift! ...Probably."),
                    L10n.T("你的品味真的需要好好提升一下...", "Your taste really needs to improve..."),
                    L10n.T("我对你很失望...你要拿出诚意来才行。", "I'm disappointed in you... you need to show sincerity."),
                    L10n.T("别送了，我现在看到你就想到那个破烂东西。", "Don't give anything, I think of that junk when I see you now."),
                    L10n.T("你要是真心想道歉，送点像样的来。", "If you really want to apologize, bring something decent."),
                    L10n.T("哼...算了，我大人有大量...但下次注意点！", "Hmph... fine, I'll be the bigger person... but watch it next time!"),
                    L10n.T("你是故意气我的对吧！承认吧！", "You're doing this to annoy me on purpose, right! Admit it!"),
                    L10n.T("作为护士我见过很多让人头疼的东西...你的礼物算其中之一。", "As a nurse I've seen many headache-inducing things... your gift is one of them."),
                    L10n.T("这次就不收了。我需要冷静一下。", "I won't take anything this time. I need to calm down."),
                    L10n.T("你如果带着诚意来的话...我可以考虑原谅你。", "If you come with sincerity... I might consider forgiving you.")
                };
            }
            else
            {
                return new string[]
                {
                    L10n.T("今天已经送过了，明天再来吧。", "You already gave today, come back tomorrow."),
                    L10n.T("一天一次就够了，我又不是贪心的人。", "Once a day is enough, I'm not greedy."),
                    L10n.T("嗯，今天的我收到了。", "Hmm, I got today's."),
                    L10n.T("今天的礼物还行...明天可以更用心一点。", "Today's gift was okay... you can try harder tomorrow."),
                    L10n.T("别送太多了，我柜子都快放不下了。", "Don't give too much, my cabinet is almost full."),
                    L10n.T("今天已经收过了~我知道你来是想聊天的对吧？", "Already received today~I know you came to chat, right?"),
                    L10n.T("你今天来得真勤快...不过一天一份就好啦。", "You're really diligent today... but one gift a day is enough."),
                    L10n.T("好了好了，今天的心意我收到了。", "Okay okay, I received today's gesture."),
                    L10n.T("你每天都送...其实我挺开心的。但一天一次就好。", "You give every day... I'm actually quite happy. But once a day is fine."),
                    L10n.T("今天就不用了，陪我说说话就好。", "No need today, just chat with me."),
                    L10n.T("嗯...今天安安静静地待一会儿也不错。", "Hmm... just quietly spending a moment together is nice too."),
                    L10n.T("已经收过啦，别浪费了。你的钱留着治疗用。", "Already received, don't waste. Save your money for healing."),
                    L10n.T("一天一次刚刚好，太多了我反而不自在。", "Once a day is just right, too much makes me uncomfortable."),
                    L10n.T("呵呵，你比那些只来治伤的冒险者有心多了。", "Hehe, you're much more thoughtful than those adventurers who only come for healing."),
                    L10n.T("行了行了，你的好意我心领了。", "Alright alright, I appreciate your kindness.")
                };
            }
        }
        
        // ============================================================================
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
                    L10n.T("我不是免费义诊的，别想省钱。", "I'm not running a free clinic, don't try to save money."),
                    L10n.T("你看起来灰头土脸的...真不讲究。", "You look all dusty and dirty... how unkempt."),
                    L10n.T("哈？你来做什么？受伤了还是纯粹来烦我的？", "Huh? What do you want? Are you hurt or just here to bother me?"),
                    L10n.T("鸭科夫这地方真是...空气里都是火药味。", "Duckov is really... the air smells like gunpowder."),
                    L10n.T("我这里不欢迎闲人，有事说事。", "Idlers aren't welcome here, state your business."),
                    L10n.T("你的伤口处理得乱七八糟的...真让人看不下去。", "Your wounds are terribly treated... I can't stand looking at this."),
                    L10n.T("别用那种眼神看我。我只是个护士，仅此而已。", "Don't look at me like that. I'm just a nurse, nothing more.")
                };
            }
            else if (level <= 4)
            {
                return new string[]
                {
                    L10n.T("又受伤了？你打架能不能小心点...", "Hurt again? Can you be more careful when fighting..."),
                    L10n.T("嗯...你今天看起来气色还不错，继续保持。", "Hmm... you look well today, keep it up."),
                    L10n.T("战场上要注意防护，受了重伤我也不一定能治好。", "Be careful on the battlefield, I might not be able to heal severe injuries."),
                    L10n.T("那个...我这有多余的绷带，你拿着吧。不、不是特意给你准备的！", "Um... I have extra bandages, take them. I didn't prepare them for you specifically!"),
                    L10n.T("你还活着啊...我是说，你还来啊，真烦。", "You're still alive... I mean, you're here again, so annoying."),
                    L10n.T("今天鸭科夫倒是难得安静...你也少惹点事。", "Duckov is unusually quiet today... you should cause less trouble too."),
                    L10n.T("别老受伤了，药材很贵的你知道吗？...啊我不是在担心你啦！", "Stop getting hurt, do you know how expensive medicine is? ...I'm not worried about you!"),
                    L10n.T("你比其他冒险者稍微靠谱那么一点点。就一点点。", "You're slightly more reliable than other adventurers. Just slightly."),
                    L10n.T("嗯？你要陪我聊天？...随便你吧，反正我也没多忙。", "Hm? You want to chat with me? ...Suit yourself, I'm not too busy anyway."),
                    L10n.T("我之前做了些外伤药膏...你要是需要就拿去。下次小心点。", "I made some wound ointment earlier... take some if you need it. Be more careful next time.")
                };
            }
            else if (level <= 6)
            {
                return new string[]
                {
                    L10n.T("看到你平安回来真好。来，让我看看有没有受伤。", "It's good to see you back safe. Come, let me check for injuries."),
                    L10n.T("你每次过来，我就...松一口气。别误会，只是少了个麻烦患者。", "Every time you come by, I feel... relieved. Don't misunderstand, just one less troublesome patient."),
                    L10n.T("今天的风很舒服呢...偶尔停下来休息也好。", "The breeze is nice today... it's okay to rest once in a while."),
                    L10n.T("我...我以前在J-Lab的时候，从来没有人像你这样关心过我。", "I... when I was at J-Lab, no one ever cared about me like you do."),
                    L10n.T("你想听我的故事吗？...也没什么好说的就是了。", "Want to hear my story? ...Not that there's much to tell."),
                    L10n.T("谢谢你...经常来看我。这里虽然危险，但有你在就觉得安心了一些。", "Thank you... for visiting me often. It's dangerous here, but I feel safer with you around."),
                    L10n.T("我给你泡了杯药草茶，对身体好的。快喝了。", "I made you some herbal tea, it's good for your health. Drink up."),
                    L10n.T("你知道吗，鸭科夫的夕阳很美。有时候我会在这里看很久...", "You know, the sunset in Duckov is beautiful. Sometimes I watch it for a long time..."),
                    L10n.T("如果...如果有一天你不来了，我大概会有点不习惯吧。", "If... if you stopped coming one day, I'd probably feel a bit lost."),
                    L10n.T("在J-Lab的时候，我曾以为自己再也不会对谁敞开心扉了...", "When I was at J-Lab, I thought I'd never open up to anyone again...")
                };
            }
            else if (level <= 8)
            {
                return new string[]
                {
                    L10n.T("你怎么才来看我啊...我、我才没有在等你呢！", "What took you so long... I wasn't waiting for you or anything!"),
                    L10n.T("唔...你身上有血腥味。快过来让我检查，不许拒绝。", "Ugh... you smell like blood. Come here and let me check, no refusing."),
                    L10n.T("我特意给你留了最好的药，别跟别人说哦。", "I saved the best medicine for you, don't tell anyone."),
                    L10n.T("有时候我会想...如果我们是在J-Lab之外认识的就好了。", "Sometimes I think... if only we had met outside of J-Lab."),
                    L10n.T("你今天不来我会担心的...啊不是！我是说我会很清闲的！", "I'd worry if you didn't come today... no wait! I mean I'd be bored!"),
                    L10n.T("给你，这是我特制的恢复药。只有你有哦...才不是偏心！", "Here, this is my special recovery medicine. Only for you... it's not favoritism!"),
                    L10n.T("你受伤的时候...我比谁都着急。所以你要好好保护自己。", "When you're hurt... I worry more than anyone. So please take care of yourself."),
                    L10n.T("嗯...你在的时候，我的心情总是特别好。这一定是空气好的关系。", "Hmm... my mood is always better when you're here. Must be the fresh air."),
                    L10n.T("我以前觉得这个世界没什么值得留恋的...遇到你之后就不一样了。", "I used to think there was nothing worth cherishing in this world... then I met you."),
                    L10n.T("下次受伤了第一个来找我，好吗？...不对，最好是不要受伤。", "Come to me first next time you're hurt, okay? ...No wait, best not to get hurt at all.")
                };
            }
            else // level 9-10
            {
                return new string[]
                {
                    L10n.T("只要你在我身边，我就觉得...以前的一切都值得了。", "As long as you're by my side, I feel... everything before was worth it."),
                    L10n.T("我曾经以为会带着愧疚过一辈子...但是你让我重新看到了希望。", "I once thought I'd live with guilt forever... but you showed me hope again."),
                    L10n.T("你是我在这个冰冷的世界里，最温暖的存在。", "You are the warmest presence in this cold world."),
                    L10n.T("不管发生什么，我都会在这里等你回来。这是我的承诺。", "No matter what happens, I'll be here waiting for you. That's my promise."),
                    L10n.T("如果时光能倒流...我想更早一点遇见你。", "If time could flow backwards... I'd want to meet you sooner."),
                    L10n.T("你让我明白了，即使在最黑暗的地方，也能绽放出光芒。", "You taught me that even in the darkest places, light can bloom."),
                    L10n.T("我的医术可以治好伤口，但你治好了我的心。...好肉麻，当我没说。", "My skills can heal wounds, but you healed my heart. ...So cheesy, forget I said that."),
                    L10n.T("谢谢你出现在我的生命里。认真的。", "Thank you for appearing in my life. I mean it."),
                    L10n.T("以后...我们一直在一起好不好？我会一直守护你的。", "From now on... can we stay together? I'll always protect you."),
                    L10n.T("你看，今天的星空很美呢。像你的眼睛一样...啊我在说什么啊！", "Look, the stars are beautiful tonight. Just like your eyes... what am I saying!")
                };
            }
        }
        
        // ============================================================================
        // 治疗相关特殊对话
        // ============================================================================
        
        private static string[] _healSuccessDialogues;
        private string GetRandomHealSuccessDialogue()
        {
            if (_healSuccessDialogues == null)
            {
                _healSuccessDialogues = new string[]
                {
                    L10n.T("好了，都处理好了。注意安全哦。", "All done. Stay safe out there."),
                    L10n.T("治好了。别再这么莽撞了，知道吗？", "Healed. Don't be so reckless, okay?"),
                    L10n.T("伤口已经处理好了。希望下次见面不是在手术台上。", "Wounds are treated. Hope I don't see you on the operating table next time."),
                    L10n.T("嗯，恢复得不错。记得保持卫生。", "Hmm, recovering well. Remember to stay clean."),
                    L10n.T("治疗完成。你可真是让人操心...", "Treatment complete. You really make me worry..."),
                    L10n.T("好了，满血了。要保重自己知道吗？", "There, full health. Take care of yourself, okay?"),
                    L10n.T("所有的负面状态都清除了。你之前到底经历了什么啊...", "All debuffs cleared. What on earth did you go through..."),
                    L10n.T("治好了~下次受伤了记得第一时间来找我哦。", "All better~Remember to come to me first when you're hurt."),
                    L10n.T("手术很成功...才怪，就是简单包扎而已啦。", "Surgery was successful... just kidding, it's just simple bandaging."),
                    L10n.T("身体是革命的本钱，以后少冒险。", "Health is capital, take fewer risks in the future."),
                    L10n.T("交给我就对了。我的医术可是J-Lab顶级水准。", "Leave it to me. My medical skills are J-Lab's finest."),
                    L10n.T("嗯，这次伤势不算严重。但你也太不小心了。", "Hmm, the injuries aren't serious this time. But you're too careless."),
                    L10n.T("好了，又是崭新的你了。下次给我小心点啊。", "There, good as new. Be more careful next time."),
                    L10n.T("治疗费照收不误哦~开玩笑的，打折了打折了。", "Full price for healing~ Just kidding, I gave you a discount."),
                    L10n.T("每次治好你我都松一口气...因为可以下班了！才不是因为担心你！", "I breathe a sigh of relief every time I heal you... because I can close up! Not because I'm worried about you!")
                };
            }
            return _healSuccessDialogues[UnityEngine.Random.Range(0, _healSuccessDialogues.Length)];
        }
        
        private static string[] _fullHPDialogues;
        private string GetRandomFullHPDialogue()
        {
            if (_fullHPDialogues == null)
            {
                _fullHPDialogues = new string[]
                {
                    L10n.T("你很健康嘛，不需要我的帮助。", "You look healthy, no need for my help."),
                    L10n.T("嗯？满血状态啊...那你来找我干嘛？...想我了？", "Hm? Full health... so why are you here? ...Missed me?"),
                    L10n.T("检查结果：非常健康。不需要治疗。", "Check result: perfectly healthy. No treatment needed."),
                    L10n.T("你身上没有任何伤势呢。该不会是专门来看我的吧~？", "You have no injuries at all. Don't tell me you came just to see me~?"),
                    L10n.T("不用治疗~但既然来了就坐会儿吧。", "No treatment needed~But since you're here, sit for a while."),
                    L10n.T("完全没事嘛。看来你今天很小心，不错不错。", "Completely fine. Looks like you were careful today, good good."),
                    L10n.T("满血满状态！作为护士我很欣慰。", "Full health and no debuffs! As a nurse I'm pleased."),
                    L10n.T("你现在比我的大部分病人都健康...我是该高兴还是难过呢。", "You're healthier than most of my patients now... should I be happy or sad?"),
                    L10n.T("没有伤势就好。那你今天要...聊聊天吗？", "No injuries, that's good. So... want to chat today?"),
                    L10n.T("看来你今天运气不错~不需要我出手！", "Seems your luck is good today~No need for my assistance!")
                };
            }
            return _fullHPDialogues[UnityEngine.Random.Range(0, _fullHPDialogues.Length)];
        }
        
        private static string[] _noMoneyDialogues;
        private string GetRandomNoMoneyDialogue()
        {
            if (_noMoneyDialogues == null)
            {
                _noMoneyDialogues = new string[]
                {
                    L10n.T("治疗费用不够哦，先去赚点钱再来吧~", "Not enough money for healing, go earn some first~"),
                    L10n.T("我也想免费治你...但药材要花钱买的啊。", "I'd love to heal you for free... but medicine costs money."),
                    L10n.T("钱不够呢...你要不先去打打怪赚点钱？", "Not enough money... how about you go fight some monsters first?"),
                    L10n.T("抱歉，我也要进药材的。没钱的话，下次再来吧。", "Sorry, I need to buy medicine supplies too. Come back when you have money."),
                    L10n.T("我也是要吃饭的嘛...赊账就算了吧。", "I need to eat too... no credit, sorry."),
                    L10n.T("你的口袋比你的伤口还空...先去赚钱吧。", "Your pockets are emptier than your wounds... go earn some first."),
                    L10n.T("如果我免费治疗，我的医疗站就要倒闭了呢。", "If I healed for free, my clinic would go bankrupt."),
                    L10n.T("没钱？那就只能自己扛着了...开玩笑的，赚够再来。", "No money? Then tough it out... kidding, come back when you have enough."),
                    L10n.T("身为专业护士，免费是不行的~快去赚钱吧。", "As a professional nurse, free isn't an option~Go earn money."),
                    L10n.T("唉...真想帮你，但阿稳的药材运费可不便宜。", "Sigh... I want to help, but the delivery fee for medicine isn't cheap.")
                };
            }
            return _noMoneyDialogues[UnityEngine.Random.Range(0, _noMoneyDialogues.Length)];
        }
        
        private static string[] _debuffOnlyDialogues;
        private string GetRandomDebuffOnlyDialogue()
        {
            if (_debuffOnlyDialogues == null)
            {
                _debuffOnlyDialogues = new string[]
                {
                    L10n.T("身上有些不好的状态呢，让我帮你处理一下。", "You have some bad status effects, let me take care of those."),
                    L10n.T("血量倒是满的...但你身上的负面效果可不少啊。", "Health is full... but you have quite a few debuffs."),
                    L10n.T("虽然没受伤，但这些debuff不处理可不行哦。", "You're not hurt, but these debuffs need to be dealt with."),
                    L10n.T("奇怪的状态异常呢...一定是碰了什么不该碰的东西吧。", "Strange status abnormalities... you must have touched something you shouldn't have."),
                    L10n.T("看起来你中了不少负面效果...来，我帮你清一清。", "Looks like you've got quite a few debuffs... come, let me clean those up.")
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
                if (_idleBubbles == null)
                {
                    _idleBubbles = new string[]
                    {
                        L10n.T("医疗物资还够用吗...得让阿稳再送一批来。", "Do I have enough medical supplies... need to have Awen deliver another batch."),
                        L10n.T("鸭科夫的伤亡率也太高了吧...", "The casualty rate in Duckov is way too high..."),
                        L10n.T("叮当...你现在过得好吗...", "Dingdang... are you doing well now..."),
                        L10n.T("这把钳子需要消毒...还有那个纱布也快用完了。", "This pair of forceps needs sterilizing... and that gauze is almost used up too."),
                        L10n.T("唉，头发又乱了。战场上保持形象真的好难...", "Sigh, my feathers are messy again. Maintaining appearance on a battlefield is so hard..."),
                        L10n.T("J-Lab的事...不能再想了。这里才是我的现在。", "J-Lab stuff... I need to stop thinking about it. Here is my present."),
                        L10n.T("今天有冒险者会来吗...希望不要太严重的伤势。", "Will any adventurers come today... hopefully no serious injuries."),
                        L10n.T("这个药方的配比好像可以优化一下...", "The ratio of this prescription could probably be optimized..."),
                        L10n.T("偶尔也想像普通的鸭子一样...安安静静地生活。", "Sometimes I wish I could live quietly... like an ordinary duck."),
                        L10n.T("阿稳上次带来的药草品质不错，得表扬他一下。", "The herbs Awen brought last time were good quality, should compliment him."),
                        L10n.T("如果没有战争就好了...大家都不用受伤。", "If only there were no war... no one would have to get hurt."),
                        L10n.T("嗯...今天的天气很适合晾药材呢。", "Hmm... today's weather is perfect for drying herbs."),
                        L10n.T("作为护士，我能做的就是让每一个来找我的人...都能好好活下去。", "As a nurse, all I can do is make sure everyone who comes to me... can live on."),
                        L10n.T("那个之前来治疗的冒险者...后来再也没出现过...希望他平安。", "That adventurer who came for treatment before... never showed up again... hope they're safe."),
                        L10n.T("即使在这么危险的地方，花依然会开。...我也一样。", "Even in such a dangerous place, flowers still bloom. ...So do I.")
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
