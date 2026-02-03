// ============================================================================
// GoblinAffinityConfig.cs - 哥布林"叮当"好感度配置
// ============================================================================
// 哥布林NPC"叮当"的专属好感度配置。
// 实现所有NPC配置接口：INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, 
//                      INPCShopConfig, INPCGiftContainerConfig
// 好感度系统参考星露谷物语设计，每心250点，最高10心2500点。
//   
// 叮当的背景故事：
// 叮当是J-Lab实验室创造的有智慧的哥布林，因为不够野蛮而被其他哥布林欺负。
// 他的笑脸是实验室强制刻上去的，即使难过也无法哭泣。
// 在Duckov这个危险的鸭子世界中，他渴望找到真正的朋友。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 哥布林"叮当"好感度配置
    /// </summary>
    public class GoblinAffinityConfig : INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, INPCShopConfig, INPCGiftContainerConfig
    {
        // ============================================================================
        // 常量定义
        // ============================================================================
        
        /// <summary>哥布林NPC唯一标识符</summary>
        public const string NPC_ID = "goblin_dingdang";
        
        /// <summary>砖石惩罚值</summary>
        public const int BRICK_STONE_PENALTY = -20;
        
        /// <summary>冷萃液解锁等级</summary>
        public const int COLD_QUENCH_UNLOCK_LEVEL = 2;
        
        /// <summary>每日对话好感度增加值</summary>
        public const int DAILY_CHAT_AFFINITY = 40;
        
        // ============================================================================
        // 好感度等级配置（使用 AffinityManager 统一配置）
        // 所有NPC共用同一套等级点数配置
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
        public string DisplayName => L10n.T("叮当", "Dingdang");
        public int MaxPoints => AffinityManager.UNIFIED_MAX_POINTS;  // 使用统一配置
        public int PointsPerLevel => 250;  // 基础每级点数（参考值，实际使用递增式）
        public int MaxLevel => AffinityManager.UNIFIED_MAX_LEVEL;  // 使用统一配置
        
        // 礼物好感度值配置（简化版：喜欢/不喜欢/一般）
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
                        { "Normal", 20 }      // 一般物品 +20点
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
                        { 2, new[] { L10n.T("冷萃液", "Cold Quench Fluid") } },
                        { 3, new[] { L10n.T("10%折扣", "10% Discount") } },
                        { 5, new[] { L10n.T("叮当的故事（上）", "Dingdang's Story (Part 1)") } },
                        { 6, new[] { L10n.T("15%折扣", "15% Discount") } },
                        { 10, new[] { L10n.T("叮当的故事（下）", "Dingdang's Story (Part 2)"), L10n.T("20%折扣", "20% Discount") } }
                    };
                }
                return _unlocksByLevel;
            }
        }
        
        // 折扣配置
        private static Dictionary<int, float> _discountsByLevel;
        public Dictionary<int, float> DiscountsByLevel
        {
            get
            {
                if (_discountsByLevel == null)
                {
                    _discountsByLevel = new Dictionary<int, float>
                    {
                        { 3, 0.10f },
                        { 6, 0.15f },
                        { 10, 0.20f }
                    };
                }
                return _discountsByLevel;
            }
        }
        
        // ============================================================================
        // INPCGiftConfig 实现
        // ============================================================================
        
        public int DailyChatAffinity => DAILY_CHAT_AFFINITY;
        
        // 喜欢的物品列表（TypeID -> 额外加成，0表示使用默认喜欢值80）
        // 由用户自定义物品ID
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
                        { 500002, 150 }     // +150好感度
                    };
                }
                return _positiveItems;
            }
        }

        // 喜欢的物品标签列表
        // 拥有这些标签的物品都会被视为喜欢的礼物
        private static HashSet<string> _positiveTags;
        public HashSet<string> PositiveTags
        {
            get
            {
                if (_positiveTags == null)
                {
                    _positiveTags = new HashSet<string>
                    {
                        "Formula",           // 配方
                        "Formula_Blueprint"  // 高级工作台蓝图
                    };
                }
                return _positiveTags;
            }
        }
        
        // 不喜欢的物品列表（TypeID -> 额外惩罚，0表示使用默认不喜欢值-40）
        // 由用户自定义物品ID
        private static Dictionary<int, int> _negativeItems;
        public Dictionary<int, int> NegativeItems
        {
            get
            {
                if (_negativeItems == null)
                {
                    _negativeItems = new Dictionary<int, int>
                    {
                        { BrickStoneConfig.TYPE_ID, 0 },  // 砖石（假钻石）- 使用默认不喜欢值
                        { 115, 0 },   // 不喜欢的物品
                        { 878, 0 },   // 不喜欢的物品
                        { 1283, 0 }   // 不喜欢的物品
                    };
                }
                return _negativeItems;
            }
        }
        
        // 正向对话气泡（收到喜欢的礼物 - 高频触发，30条）
        private static string[] _positiveBubbles;
        public string[] PositiveBubbles
        {
            get
            {
                if (_positiveBubbles == null)
                {
                    _positiveBubbles = new string[]
                    {
                        // 傲娇反应（表面嫌弃实际开心）
                        L10n.T("哼...还、还行吧，叮当勉强收下了！", "Hmph... it's o-okay, Dingdang will reluctantly accept it!"),
                        L10n.T("别、别误会！叮当只是觉得扔掉可惜！", "D-don't get the wrong idea! Dingdang just thinks it's a waste to throw away!"),
                        L10n.T("你...你怎么知道叮当喜欢这个的！", "H-how did you know Dingdang likes this!"),
                        L10n.T("哼，算你有点眼光...", "Hmph, you have some taste..."),
                        L10n.T("叮当才、才没有很开心呢！", "Dingdang is n-not happy at all!"),
                        L10n.T("这个...叮当会好好保管的，别多想！", "This... Dingdang will keep it safe, don't overthink!"),
                        
                        // 真情流露
                        L10n.T("哇！闪闪发光的！叮当最喜欢了！", "Wow! So shiny! Dingdang loves it!"),
                        L10n.T("这个...比J-Lab的那些冷冰冰的东西好多了...", "This... is much better than those cold things in J-Lab..."),
                        L10n.T("你是第一个送叮当礼物的人...", "You're the first person to give Dingdang a gift..."),
                        L10n.T("叮当...叮当好开心...", "Dingdang... Dingdang is so happy..."),
                        L10n.T("谢谢你...不像那些哥布林只会欺负叮当...", "Thank you... unlike those goblins who only bully Dingdang..."),
                        L10n.T("你真的...真的很好...", "You're really... really nice..."),
                        
                        // 活泼反应
                        L10n.T("太棒了太棒了！叮当要把它藏起来！", "Amazing! Dingdang will hide it away!"),
                        L10n.T("嘿嘿，这个叮当超级喜欢！", "Hehe, Dingdang super likes this!"),
                        L10n.T("哇哦！你真懂叮当的心！", "Wow! You really understand Dingdang!"),
                        L10n.T("叮当要把这个当宝贝！", "Dingdang will treasure this!"),
                        L10n.T("这个比叮当在实验室见过的都好看！", "This is prettier than anything Dingdang saw in the lab!"),
                        L10n.T("叮当决定了，你是好人！", "Dingdang has decided, you're a good person!"),
                        
                        // 背景相关
                        L10n.T("J-Lab从来没给过叮当这么好的东西...", "J-Lab never gave Dingdang anything this nice..."),
                        L10n.T("那些哥布林只会抢叮当的东西，你却送给叮当...", "Those goblins only steal from Dingdang, but you give to Dingdang..."),
                        L10n.T("叮当在实验室的时候，从来没收到过礼物...", "When Dingdang was in the lab, never received any gifts..."),
                        L10n.T("你...你不觉得叮当是怪物吗？", "You... don't think Dingdang is a monster?"),
                        L10n.T("叮当虽然是哥布林，但叮当有智慧的！谢谢你理解！", "Even though Dingdang is a goblin, Dingdang has intelligence! Thanks for understanding!"),
                        L10n.T("其他哥布林都说叮当是异类...但你不一样...", "Other goblins say Dingdang is a freak... but you're different..."),
                        
                        // 可爱反应
                        L10n.T("呜...叮当眼睛进沙子了，才不是感动哭了！", "Ugh... Dingdang got sand in eyes, not crying from emotion!"),
                        L10n.T("叮当的心扑通扑通的...一定是吃坏肚子了！", "Dingdang's heart is pounding... must have eaten something bad!"),
                        L10n.T("你、你下次还可以再送...如果你想的话！", "Y-you can give more next time... if you want!"),
                        L10n.T("叮当会记住你的好的！虽然叮当不会说出来！", "Dingdang will remember your kindness! Even if Dingdang won't say it!"),
                        L10n.T("哼哼，叮当就知道你会送好东西！", "Hmph, Dingdang knew you'd give something good!"),
                        L10n.T("这个叮当要放在最珍贵的地方！", "Dingdang will put this in the most precious place!"),
                        
                        // 笑脸背景相关
                        L10n.T("叮当真的很开心...这次是真的在笑，不是J-Lab的笑脸...", "Dingdang is really happy... this time it's a real smile, not J-Lab's smile..."),
                        L10n.T("你让叮当感受到了真正的快乐...叮当的笑脸第一次是真心的...", "You made Dingdang feel real happiness... Dingdang's smile is genuine for the first time..."),
                        L10n.T("叮当虽然总是笑...但这次叮当是真的想笑...", "Dingdang always smiles... but this time Dingdang really wants to smile...")
                    };
                }
                return _positiveBubbles;
            }
        }
        
        // 负向对话气泡（收到讨厌的礼物 - 中频触发，20条）
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
                        L10n.T("哼！你是故意的对不对！", "Hmph! You did this on purpose, right!"),
                        L10n.T("叮当讨厌你！...才怪，但叮当真的很生气！", "Dingdang hates you! ...just kidding, but Dingdang is really angry!"),
                        L10n.T("你、你是不是看不起叮当！", "A-are you looking down on Dingdang!"),
                        L10n.T("叮当才不稀罕这种东西！", "Dingdang doesn't want this kind of thing!"),
                        L10n.T("哼，叮当要记住这笔账！", "Hmph, Dingdang will remember this!"),
                        
                        // 委屈反应
                        L10n.T("呜...叮当还以为你是好人...", "Ugh... Dingdang thought you were a good person..."),
                        L10n.T("这个...让叮当想起在J-Lab被当实验品的日子...", "This... reminds Dingdang of being a test subject in J-Lab..."),
                        L10n.T("你也觉得叮当只配收到垃圾吗...", "Do you also think Dingdang only deserves trash..."),
                        L10n.T("那些哥布林也是这样对叮当的...", "Those goblins treated Dingdang the same way..."),
                        L10n.T("叮当...叮当不想要这个...", "Dingdang... Dingdang doesn't want this..."),
                        
                        // 愤怒反应
                        L10n.T("这是什么破烂！拿走！", "What is this junk! Take it away!"),
                        L10n.T("你在逗叮当玩吗！", "Are you messing with Dingdang!"),
                        L10n.T("叮当虽然是哥布林但叮当有尊严的！", "Dingdang may be a goblin but Dingdang has dignity!"),
                        L10n.T("哼！叮当不想理你了！", "Hmph! Dingdang doesn't want to talk to you anymore!"),
                        L10n.T("叮当生气了！后果很严重！", "Dingdang is angry! The consequences are serious!"),
                        
                        // 失望反应
                        L10n.T("叮当还以为...算了...", "Dingdang thought... never mind..."),
                        L10n.T("原来你也是这样看叮当的...", "So you see Dingdang this way too..."),
                        L10n.T("叮当不需要你的施舍！", "Dingdang doesn't need your charity!"),
                        L10n.T("这种东西...叮当在垃圾堆里见多了...", "This kind of thing... Dingdang has seen plenty in the trash..."),
                        L10n.T("你下次要是还送这个，叮当就不理你了！", "If you give this again, Dingdang won't talk to you!"),
                        
                        // 笑脸背景相关
                        L10n.T("叮当很难过...但叮当的脸还是在笑...你看不出来吗...", "Dingdang is sad... but Dingdang's face is still smiling... can't you tell..."),
                        L10n.T("叮当想哭...但J-Lab让叮当哭不出来...", "Dingdang wants to cry... but J-Lab made it so Dingdang can't..."),
                        L10n.T("别被叮当的笑脸骗了...叮当真的很伤心...", "Don't be fooled by Dingdang's smile... Dingdang is really hurt...")
                    };
                }
                return _negativeBubbles;
            }
        }
        
        // 普通对话气泡（收到普通礼物 - 最高频触发，30条）
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
                        L10n.T("哼，叮当就勉为其难收下吧。", "Hmph, Dingdang will reluctantly accept it."),
                        L10n.T("不、不是叮当想要，是你非要给的！", "I-it's not that Dingdang wants it, you insisted on giving!"),
                        L10n.T("算你有心了...虽然一般般。", "At least you're thoughtful... though it's average."),
                        L10n.T("叮当收下了，别指望叮当说谢谢！", "Dingdang accepts it, don't expect Dingdang to say thanks!"),
                        L10n.T("嗯...还行吧，叮当见过更好的。", "Hmm... it's okay, Dingdang has seen better."),
                        L10n.T("你、你不用特意送叮当东西的...", "Y-you don't have to give Dingdang things..."),
                        
                        // 平淡接受
                        L10n.T("谢谢...叮当会收好的。", "Thanks... Dingdang will keep it safe."),
                        L10n.T("嗯，叮当知道了。", "Hmm, Dingdang understands."),
                        L10n.T("这个叮当可以用得上。", "Dingdang can use this."),
                        L10n.T("叮当收到了，谢谢你。", "Dingdang received it, thank you."),
                        L10n.T("还不错，叮当喜欢收集东西。", "Not bad, Dingdang likes collecting things."),
                        L10n.T("叮当会把它放在安全的地方。", "Dingdang will put it somewhere safe."),
                        
                        // 好奇反应
                        L10n.T("这是什么？叮当在J-Lab没见过...", "What's this? Dingdang hasn't seen it in J-Lab..."),
                        L10n.T("嗯？这个东西有什么用？", "Hmm? What's this thing for?"),
                        L10n.T("叮当要研究研究这个...", "Dingdang needs to study this..."),
                        L10n.T("有意思...叮当从没见过这种东西。", "Interesting... Dingdang has never seen this before."),
                        L10n.T("这个...叮当得想想怎么用。", "This... Dingdang needs to think about how to use it."),
                        L10n.T("叮当的智慧告诉叮当，这个有点用处。", "Dingdang's intelligence tells Dingdang this is somewhat useful."),
                        
                        // 背景相关
                        L10n.T("比那些哥布林抢来的东西好多了...", "Much better than what those goblins steal..."),
                        L10n.T("叮当在实验室的时候，从来没人送东西...", "When Dingdang was in the lab, no one ever gave anything..."),
                        L10n.T("你...你为什么要对叮当这么好？", "Why... why are you so nice to Dingdang?"),
                        L10n.T("叮当不习惯收礼物...但谢谢你。", "Dingdang isn't used to receiving gifts... but thanks."),
                        L10n.T("这个叮当会好好珍惜的...才怪！", "Dingdang will treasure this... not!"),
                        L10n.T("叮当虽然是实验品，但也懂得感谢的！", "Even though Dingdang is a test subject, Dingdang knows gratitude!"),
                        
                        // 可爱反应
                        L10n.T("嘿嘿...叮当又有新东西了~", "Hehe... Dingdang has something new~"),
                        L10n.T("叮当的收藏又多了一件！", "Dingdang's collection has grown!"),
                        L10n.T("这个叮当要藏在秘密基地里！", "Dingdang will hide this in the secret base!"),
                        L10n.T("叮当决定原谅你之前的事了！", "Dingdang has decided to forgive you for before!"),
                        L10n.T("你还记得给叮当送东西，叮当有点感动...", "You remembered to give Dingdang something, Dingdang is a bit touched..."),
                        L10n.T("叮当会记住你的！...好的方面！", "Dingdang will remember you! ...in a good way!")
                    };
                }
                return _normalBubbles;
            }
        }
        
        public bool ShowLoveHeartOnPositive => true;
        public bool ShowBrokenHeartOnNegative => true;
        
        public string[] GetAlreadyGiftedDialogues(GiftReactionType lastReaction)
        {
            // 今日已赠送礼物对话（中频触发，每种15条）
            if (lastReaction == GiftReactionType.Positive)
            {
                // 上次送的是喜欢的礼物
                return new string[]
                {
                    // 傲娇开心
                    L10n.T("哼，今天的礼物叮当已经收到了...还、还不错啦！", "Hmph, Dingdang already received today's gift... it's n-not bad!"),
                    L10n.T("你今天已经送过了！别、别以为叮当会更喜欢你！", "You already gave today! D-don't think Dingdang will like you more!"),
                    L10n.T("叮当才没有一直在看今天的礼物呢！", "Dingdang hasn't been looking at today's gift all the time!"),
                    L10n.T("今天的礼物...叮当会好好珍藏的，别多想！", "Today's gift... Dingdang will treasure it, don't overthink!"),
                    L10n.T("哼哼，你今天的表现叮当很满意~", "Hmph, Dingdang is satisfied with your performance today~"),
                    
                    // 真情流露
                    L10n.T("今天的礼物叮当超级喜欢！谢谢你~", "Dingdang loves today's gift! Thanks~"),
                    L10n.T("嘿嘿，今天的礼物叮当已经藏在秘密基地了~", "Hehe, Dingdang has hidden today's gift in the secret base~"),
                    L10n.T("你怎么知道叮当喜欢这个的！叮当好感动...", "How did you know Dingdang likes this! Dingdang is so touched..."),
                    L10n.T("今天的礼物让叮当想起了...算了，谢谢你！", "Today's gift reminds Dingdang of... never mind, thanks!"),
                    L10n.T("叮当在J-Lab从来没收到过这么好的东西...", "Dingdang never received anything this nice in J-Lab..."),
                    
                    // 活泼反应
                    L10n.T("今天的礼物太棒了！明天还要送哦！", "Today's gift is amazing! Give more tomorrow!"),
                    L10n.T("叮当已经把今天的礼物放在最珍贵的地方了！", "Dingdang has put today's gift in the most precious place!"),
                    L10n.T("嘿嘿，叮当今天心情超好~", "Hehe, Dingdang is in a super good mood today~"),
                    L10n.T("你是叮当见过最懂叮当的人！", "You're the person who understands Dingdang the most!"),
                    L10n.T("叮当决定今天对你特别好！...才怪！", "Dingdang decided to be extra nice to you today! ...not!")
                };
            }
            else if (lastReaction == GiftReactionType.Negative)
            {
                // 上次送的是讨厌的礼物
                return new string[]
                {
                    // 傲娇生气
                    L10n.T("哼，今天的礼物叮当还在生气呢！", "Hmph, Dingdang is still angry about today's gift!"),
                    L10n.T("你今天已经惹叮当不高兴了！别再来了！", "You already upset Dingdang today! Don't come again!"),
                    L10n.T("叮当才不想看到你呢...今天的礼物太差了！", "Dingdang doesn't want to see you... today's gift was terrible!"),
                    L10n.T("哼！叮当还没原谅你呢！", "Hmph! Dingdang hasn't forgiven you yet!"),
                    L10n.T("你是不是故意送那种东西气叮当的！", "Did you give that thing on purpose to annoy Dingdang!"),
                    
                    // 委屈反应
                    L10n.T("今天的礼物...让叮当想起被哥布林欺负的日子...", "Today's gift... reminds Dingdang of being bullied by goblins..."),
                    L10n.T("叮当还以为你和其他人不一样...", "Dingdang thought you were different from others..."),
                    L10n.T("那种东西...叮当在垃圾堆里见多了...", "That kind of thing... Dingdang has seen plenty in the trash..."),
                    L10n.T("叮当不需要你的施舍...呜...", "Dingdang doesn't need your charity... ugh..."),
                    L10n.T("J-Lab的研究员也是这样对叮当的...", "The J-Lab researchers treated Dingdang the same way..."),
                    
                    // 期待改变
                    L10n.T("明天...明天你会送好东西的对吧？", "Tomorrow... you'll give something good tomorrow, right?"),
                    L10n.T("叮当给你一次机会，明天别再让叮当失望了！", "Dingdang gives you one chance, don't disappoint Dingdang tomorrow!"),
                    L10n.T("哼，叮当大人有大量，明天再看你表现！", "Hmph, Dingdang is generous, will see your performance tomorrow!"),
                    L10n.T("你下次要是还送那种东西...叮当就真的生气了！", "If you give that kind of thing again... Dingdang will really be angry!"),
                    L10n.T("叮当相信你只是一时糊涂...对吧？", "Dingdang believes you were just confused for a moment... right?")
                };
            }
            else
            {
                // 上次送的是普通礼物
                return new string[]
                {
                    // 傲娇反应
                    L10n.T("今天已经收到礼物了！别、别以为叮当在期待！", "Already received a gift today! D-don't think Dingdang is expecting more!"),
                    L10n.T("哼，你今天已经送过了，叮当记得很清楚！", "Hmph, you already gave today, Dingdang remembers clearly!"),
                    L10n.T("叮当的口袋已经装满了！...才怪，但今天够了！", "Dingdang's pocket is full! ...not really, but enough for today!"),
                    L10n.T("你是不是太闲了？今天已经送过了啦！", "Are you too free? You already gave today!"),
                    L10n.T("叮当才没有在等你明天的礼物呢！", "Dingdang is not waiting for tomorrow's gift!"),
                    
                    // 平淡反应
                    L10n.T("今天已经收到你的礼物啦~", "Already received your gift today~"),
                    L10n.T("嘿嘿，今天的礼物叮当已经收好了~", "Hehe, Dingdang has kept today's gift~"),
                    L10n.T("叮当今天已经收到礼物了，谢谢你！", "Dingdang has received a gift today, thanks!"),
                    L10n.T("今天的份已经收到了，明天再来吧！", "Today's portion is received, come back tomorrow!"),
                    L10n.T("叮当会记住你今天送的东西的~", "Dingdang will remember what you gave today~"),
                    
                    // 可爱反应
                    L10n.T("叮当今天已经很满足了！...虽然还想要更多...", "Dingdang is satisfied today! ...though wanting more..."),
                    L10n.T("你今天对叮当很好！叮当会记住的！", "You were nice to Dingdang today! Dingdang will remember!"),
                    L10n.T("嘿嘿，叮当喜欢收礼物~明天还要哦！", "Hehe, Dingdang likes receiving gifts~ More tomorrow!"),
                    L10n.T("叮当把今天的礼物和其他宝贝放在一起了！", "Dingdang put today's gift with other treasures!"),
                    L10n.T("你是叮当为数不多的朋友之一！", "You're one of Dingdang's few friends!")
                };
            }
        }
        
        // ============================================================================
        // INPCGiftContainerConfig 实现
        // ============================================================================
        // 容器式礼物赠送UI配置
        // 使用单格容器UI替代确认对话框，提供更好的礼物赠送体验
        // ============================================================================
        
        /// <summary>
        /// 容器标题本地化键
        /// <para>显示在容器UI顶部："赠送礼物给叮当"</para>
        /// </summary>
        public string ContainerTitleKey => "BossRush_GoblinGift_ContainerTitle";
        
        /// <summary>
        /// 赠送按钮文本本地化键
        /// <para>显示在赠送按钮上："赠送"</para>
        /// </summary>
        public string GiftButtonTextKey => "BossRush_GoblinGift_GiftButton";
        
        /// <summary>
        /// 空槽位提示文本本地化键
        /// <para>当容器为空时显示："放入礼物"</para>
        /// </summary>
        public string EmptySlotTextKey => "BossRush_GoblinGift_EmptySlot";
        
        /// <summary>
        /// 是否使用容器式UI
        /// <para>true: 使用新的容器式UI</para>
        /// </summary>
        public bool UseContainerUI => true;
        
        // ============================================================================
        // INPCDialogueConfig 实现
        // ============================================================================
        
        public float DialogueBubbleHeight => 2.5f;
        public float DefaultDialogueDuration => 3f;
        
        public string GetDialogue(DialogueCategory category, int level)
        {
            switch (category)
            {
                case DialogueCategory.Greeting:
                    return GetGreetingDialogue(level);
                case DialogueCategory.AfterGift:
                    return GetAfterGiftDialogue(level);
                case DialogueCategory.LevelUp:
                    return GetLevelUpDialogue(level);
                case DialogueCategory.Shopping:
                    return GetShoppingDialogue(level);
                case DialogueCategory.AlreadyGifted:
                    return L10n.T("今天已经收到礼物了~", "Already received a gift today~");
                case DialogueCategory.Farewell:
                    return GetFarewellDialogue(level);
                case DialogueCategory.Idle:
                    return GetIdleDialogue(level);
                default:
                    return "";
            }
        }
        
        public string GetSpecialDialogue(string eventKey, int level)
        {
            // 哥布林特殊事件：砖石召唤
            if (eventKey == "AfterBrickStone")
            {
                if (level >= 5)
                {
                    return L10n.T("又是假钻石...算了，看在老朋友的份上。", "Fake diamond again... Fine, for old friend's sake.");
                }
                else
                {
                    return L10n.T("这不是真钻石！叮当生气了！", "This is not real diamond! Dingdang is angry!");
                }
            }
            
            // 5级故事：返回触发标识（由调用方触发大对话系统）
            if (eventKey == "Story_Level5")
            {
                return "TRIGGER_STORY_5";
            }
            
            // 10级故事：返回触发标识（由调用方触发大对话系统）
            if (eventKey == "Story_Level10")
            {
                return "TRIGGER_STORY_10";
            }
            
            return null;
        }
        
        // ============================================================================
        // INPCShopConfig 实现
        // ============================================================================
        
        public bool ShopEnabled => true;
        public int ShopUnlockLevel => COLD_QUENCH_UNLOCK_LEVEL;
        public string ShopName => L10n.T("叮当的小店", "Dingdang's Shop");
        
        public List<ShopItemEntry> GetShopItems()
        {
            return new List<ShopItemEntry>
            {
                new ShopItemEntry(ColdQuenchFluidConfig.TYPE_ID, COLD_QUENCH_UNLOCK_LEVEL, 5)
            };
        }
        
        public float GetDiscountForLevel(int level)
        {
            float discount = 0f;
            if (DiscountsByLevel != null)
            {
                foreach (var kvp in DiscountsByLevel)
                {
                    if (level >= kvp.Key && kvp.Value > discount)
                    {
                        discount = kvp.Value;
                    }
                }
            }
            return discount;
        }
        
        // ============================================================================
        // 私有方法 - 对话内容（根据好感度等级返回不同对话）
        // ============================================================================
        
        /// <summary>
        /// 问候对话（高频触发，每个等级段10条）
        /// </summary>
        private string GetGreetingDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 8)
            {
                // 高好感度 - 亲密朋友
                dialogues = new string[]
                {
                    L10n.T("是你啊！叮当...叮当才没有在等你呢！", "It's you! Dingdang... Dingdang wasn't waiting for you!"),
                    L10n.T("哼哼，叮当最好的朋友来了~", "Hmph, Dingdang's best friend is here~"),
                    L10n.T("你终于来了！叮当...叮当只是刚好在这里！", "You finally came! Dingdang... Dingdang just happened to be here!"),
                    L10n.T("嘿！叮当就知道你会来找叮当！", "Hey! Dingdang knew you'd come find Dingdang!"),
                    L10n.T("叮当今天心情很好...才不是因为看到你！", "Dingdang is in a good mood today... not because of seeing you!"),
                    L10n.T("你来啦~叮当有好多话想跟你说！", "You're here~ Dingdang has so much to tell you!"),
                    L10n.T("叮当的好朋友！快来快来！", "Dingdang's good friend! Come come!"),
                    L10n.T("哼，你怎么才来！叮当等了...才没有等！", "Hmph, why so late! Dingdang was waiting... was not waiting!"),
                    L10n.T("是叮当最喜欢的人类！...别、别误会！", "It's Dingdang's favorite human! ...d-don't misunderstand!"),
                    L10n.T("太好了你来了！叮当有东西要给你看！", "Great you're here! Dingdang has something to show you!")
                };
            }
            else if (level >= 5)
            {
                // 中高好感度 - 熟悉的朋友
                dialogues = new string[]
                {
                    L10n.T("哦，是你啊。叮当...还挺高兴看到你的。", "Oh, it's you. Dingdang... is quite happy to see you."),
                    L10n.T("嘿！好久不见！叮当有点想...算了！", "Hey! Long time no see! Dingdang kinda missed... never mind!"),
                    L10n.T("你来了啊，叮当正好有点无聊。", "You're here, Dingdang was just a bit bored."),
                    L10n.T("哼，叮当就知道你会来。", "Hmph, Dingdang knew you'd come."),
                    L10n.T("是你啊~叮当今天愿意跟你聊聊。", "It's you~ Dingdang is willing to chat today."),
                    L10n.T("来了啊，叮当刚在想你会不会来。", "You came, Dingdang was just wondering if you'd come."),
                    L10n.T("哦~叮当认识的人来了！", "Oh~ Someone Dingdang knows is here!"),
                    L10n.T("你还记得来找叮当，不错不错。", "You remembered to find Dingdang, not bad."),
                    L10n.T("嘿，叮当正想找人说话呢。", "Hey, Dingdang was just looking for someone to talk to."),
                    L10n.T("是你啊，叮当今天心情还行。", "It's you, Dingdang is in an okay mood today.")
                };
            }
            else if (level >= 2)
            {
                // 中低好感度 - 认识但不熟
                dialogues = new string[]
                {
                    L10n.T("哦，是你啊。有什么事？", "Oh, it's you. What's up?"),
                    L10n.T("嗯？你又来了？", "Hmm? You're here again?"),
                    L10n.T("叮当记得你...大概。", "Dingdang remembers you... probably."),
                    L10n.T("你是那个...算了，有什么事？", "You're that... never mind, what do you want?"),
                    L10n.T("哼，叮当很忙的，有事快说。", "Hmph, Dingdang is busy, speak quickly."),
                    L10n.T("又是你啊，叮当没什么好说的。", "It's you again, Dingdang has nothing to say."),
                    L10n.T("嗯...叮当见过你。", "Hmm... Dingdang has seen you before."),
                    L10n.T("你来找叮当干嘛？", "Why are you looking for Dingdang?"),
                    L10n.T("叮当不太想说话...但你来了就说吧。", "Dingdang doesn't want to talk... but since you're here, speak."),
                    L10n.T("哦，是之前那个人类。", "Oh, it's that human from before.")
                };
            }
            else
            {
                // 低好感度 - 陌生人
                dialogues = new string[]
                {
                    L10n.T("嗯？你是谁？叮当不认识你。", "Hmm? Who are you? Dingdang doesn't know you."),
                    L10n.T("你...你想干什么？叮当可不好欺负！", "What... what do you want? Dingdang is not easy to bully!"),
                    L10n.T("别、别靠近叮当！叮当警告你！", "D-don't come near Dingdang! Dingdang warns you!"),
                    L10n.T("又是人类...你们都一样...", "Another human... you're all the same..."),
                    L10n.T("叮当不想跟陌生人说话。", "Dingdang doesn't want to talk to strangers."),
                    L10n.T("你是J-Lab的人吗？叮当不会回去的！", "Are you from J-Lab? Dingdang won't go back!"),
                    L10n.T("哼，叮当才不怕你！", "Hmph, Dingdang is not afraid of you!"),
                    L10n.T("你想要什么？叮当什么都没有。", "What do you want? Dingdang has nothing."),
                    L10n.T("叮当...叮当不是普通的哥布林...", "Dingdang... Dingdang is not an ordinary goblin..."),
                    L10n.T("走开！叮当不需要任何人！", "Go away! Dingdang doesn't need anyone!")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 收到礼物后对话（由礼物系统触发，这里是通用感谢）
        /// </summary>
        private string GetAfterGiftDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 8)
            {
                dialogues = new string[]
                {
                    L10n.T("谢谢你...叮当真的很开心...", "Thank you... Dingdang is really happy..."),
                    L10n.T("你对叮当太好了...叮当不知道该怎么报答...", "You're too good to Dingdang... Dingdang doesn't know how to repay..."),
                    L10n.T("叮当会永远记住你的好的！", "Dingdang will always remember your kindness!"),
                    L10n.T("有你真好...叮当从来没有这样的朋友...", "It's great to have you... Dingdang never had a friend like this..."),
                    L10n.T("叮当...叮当好感动...", "Dingdang... Dingdang is so touched...")
                };
            }
            else if (level >= 5)
            {
                dialogues = new string[]
                {
                    L10n.T("谢谢你的礼物！叮当很喜欢！", "Thanks for the gift! Dingdang likes it!"),
                    L10n.T("嘿嘿，你还挺懂叮当的~", "Hehe, you understand Dingdang quite well~"),
                    L10n.T("叮当收到了！谢谢~", "Dingdang received it! Thanks~"),
                    L10n.T("你对叮当真好~", "You're so nice to Dingdang~"),
                    L10n.T("叮当会好好保管的！", "Dingdang will keep it safe!")
                };
            }
            else if (level >= 2)
            {
                dialogues = new string[]
                {
                    L10n.T("嗯...谢谢。", "Hmm... thanks."),
                    L10n.T("叮当收到了。", "Dingdang received it."),
                    L10n.T("还不错...吧。", "Not bad... I guess."),
                    L10n.T("你为什么要送叮当东西？", "Why are you giving Dingdang things?"),
                    L10n.T("叮当...叮当会收下的。", "Dingdang... Dingdang will accept it.")
                };
            }
            else
            {
                dialogues = new string[]
                {
                    L10n.T("这是给叮当的？...为什么？", "This is for Dingdang? ...why?"),
                    L10n.T("你...你想要什么？", "What... what do you want?"),
                    L10n.T("叮当不需要你的施舍！...但叮当会收下。", "Dingdang doesn't need your charity! ...but Dingdang will take it."),
                    L10n.T("哼...叮当勉强收下了。", "Hmph... Dingdang reluctantly accepts."),
                    L10n.T("你是第一个送叮当东西的人...", "You're the first person to give Dingdang something...")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 等级提升对话（低频触发，每个等级段5条）
        /// </summary>
        private string GetLevelUpDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 10)
            {
                dialogues = new string[]
                {
                    L10n.T("你是叮当最好最好的朋友！叮当...叮当好开心！", "You're Dingdang's best best friend! Dingdang... Dingdang is so happy!"),
                    L10n.T("叮当从来没有这么信任过一个人...谢谢你...", "Dingdang has never trusted anyone this much... thank you..."),
                    L10n.T("叮当决定了！你是叮当一辈子的朋友！", "Dingdang has decided! You're Dingdang's friend for life!"),
                    L10n.T("呜...叮当太感动了...才、才没有哭！", "Ugh... Dingdang is so touched... n-not crying!"),
                    L10n.T("叮当会把最好的东西都给你！因为你是叮当最重要的人！", "Dingdang will give you the best things! Because you're Dingdang's most important person!")
                };
            }
            else if (level >= 7)
            {
                dialogues = new string[]
                {
                    L10n.T("叮当越来越喜欢你了！给你更多折扣！", "Dingdang likes you more and more! More discount for you!"),
                    L10n.T("哼哼，你在叮当心里的地位又提高了~", "Hmph, your position in Dingdang's heart has risen~"),
                    L10n.T("叮当决定对你更好一点！...才不是因为喜欢你！", "Dingdang decided to be nicer to you! ...not because Dingdang likes you!"),
                    L10n.T("你是叮当见过最好的人类！", "You're the best human Dingdang has ever met!"),
                    L10n.T("叮当要给你特别的优惠！因为你特别！", "Dingdang will give you special deals! Because you're special!")
                };
            }
            else if (level >= 3)
            {
                dialogues = new string[]
                {
                    L10n.T("我们的关系更好了！叮当给你一点折扣吧！", "Our relationship is better! Dingdang will give you a small discount!"),
                    L10n.T("哼，叮当开始有点喜欢你了...一点点！", "Hmph, Dingdang is starting to like you a bit... just a bit!"),
                    L10n.T("你还不错嘛，叮当愿意跟你做朋友。", "You're not bad, Dingdang is willing to be friends with you."),
                    L10n.T("叮当决定信任你一点点了！", "Dingdang decided to trust you a little bit!"),
                    L10n.T("看在你对叮当不错的份上，给你便宜点！", "Since you're nice to Dingdang, I'll give you a discount!")
                };
            }
            else
            {
                dialogues = new string[]
                {
                    L10n.T("叮当开始觉得你不是坏人了...", "Dingdang is starting to think you're not a bad person..."),
                    L10n.T("哼，叮当勉强承认你还行。", "Hmph, Dingdang reluctantly admits you're okay."),
                    L10n.T("你...你好像跟其他人类不一样...", "You... you seem different from other humans..."),
                    L10n.T("叮当愿意再给你一次机会。", "Dingdang is willing to give you another chance."),
                    L10n.T("也许...叮当可以稍微相信你一点。", "Maybe... Dingdang can trust you a little bit.")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 购物对话（中频触发）
        /// </summary>
        private string GetShoppingDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 7)
            {
                dialogues = new string[]
                {
                    L10n.T("老朋友专属折扣！叮当只对你这样！", "Special discount for old friend! Dingdang only does this for you!"),
                    L10n.T("嘿嘿，叮当给你最好的价格~", "Hehe, Dingdang gives you the best price~"),
                    L10n.T("叮当的好东西都给你看！别人可看不到！", "Dingdang shows you all the good stuff! Others can't see!"),
                    L10n.T("你是VIP！叮当的VIP！", "You're a VIP! Dingdang's VIP!"),
                    L10n.T("叮当把最好的都留给你了~", "Dingdang saved the best for you~")
                };
            }
            else if (level >= 3)
            {
                dialogues = new string[]
                {
                    L10n.T("给你便宜一点~叮当今天心情好！", "A little cheaper for you~ Dingdang is in a good mood today!"),
                    L10n.T("看看叮当的好东西！保证你喜欢！", "Check out Dingdang's good stuff! Guaranteed you'll like it!"),
                    L10n.T("叮当的东西都是好东西！不骗你！", "Dingdang's stuff is all good stuff! Not lying!"),
                    L10n.T("嘿嘿，叮当给你打个折~", "Hehe, Dingdang will give you a discount~"),
                    L10n.T("叮当的店欢迎你~", "Dingdang's shop welcomes you~")
                };
            }
            else
            {
                dialogues = new string[]
                {
                    L10n.T("看看叮当的好东西！", "Check out Dingdang's good stuff!"),
                    L10n.T("叮当的东西很好的！不信你看！", "Dingdang's stuff is great! See for yourself!"),
                    L10n.T("要买东西吗？叮当有很多好东西！", "Want to buy something? Dingdang has lots of good stuff!"),
                    L10n.T("叮当不会骗你的...大概。", "Dingdang won't cheat you... probably."),
                    L10n.T("这些都是叮当收集的宝贝！", "These are all treasures Dingdang collected!")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 告别对话（中频触发）
        /// 分为三个等级段：低好感度(<3)、中好感度(3-6)、高好感度(≥7)
        /// </summary>
        private string GetFarewellDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 7)
            {
                // 高好感度 - 亲密朋友，依依不舍
                dialogues = new string[]
                {
                    L10n.T("下次再来找叮当玩！叮当会等你的！...才不会！", "Come play with Dingdang again! Dingdang will wait! ...will not!"),
                    L10n.T("嘿，路上小心！叮当...叮当会想你的...", "Hey, be careful! Dingdang... Dingdang will miss you..."),
                    L10n.T("再见~叮当期待下次见面！", "Bye~ Dingdang looks forward to next time!"),
                    L10n.T("你要走了吗...叮当有点舍不得...", "You're leaving... Dingdang is a bit reluctant..."),
                    L10n.T("下次带好吃的来！叮当喜欢好吃的！", "Bring something tasty next time! Dingdang likes tasty things!"),
                    L10n.T("拜拜~叮当会好好的！你也要好好的！", "Bye~ Dingdang will be fine! You be fine too!"),
                    L10n.T("哼，走吧走吧...叮当才不会想你...", "Hmph, go go... Dingdang won't miss you..."),
                    L10n.T("记得常来看叮当！叮当一个人很无聊的...", "Remember to visit Dingdang often! Dingdang is lonely alone..."),
                    L10n.T("你是叮当最好的朋友！下次一定要来！", "You're Dingdang's best friend! Come again for sure!"),
                    L10n.T("叮当会把你的位置留着的！快点回来！", "Dingdang will save your spot! Come back soon!")
                };
            }
            else if (level >= 3)
            {
                // 中好感度 - 熟悉的朋友，有些不舍
                dialogues = new string[]
                {
                    L10n.T("再见啦~下次再来找叮当！", "Bye~ Come find Dingdang again!"),
                    L10n.T("嗯，路上小心。", "Hmm, be careful on the way."),
                    L10n.T("下次见~叮当会记得你的。", "See you next time~ Dingdang will remember you."),
                    L10n.T("走好~有空再来玩！", "Take care~ Come play again when you're free!"),
                    L10n.T("哼，走吧，叮当要继续忙了。", "Hmph, go on, Dingdang needs to get back to work."),
                    L10n.T("再见！叮当今天挺开心的~", "Goodbye! Dingdang was quite happy today~"),
                    L10n.T("下次来的时候带点好东西！", "Bring something good next time!"),
                    L10n.T("叮当会在这里的，下次再见！", "Dingdang will be here, see you next time!")
                };
            }
            else
            {
                // 低好感度 - 陌生人，冷淡
                dialogues = new string[]
                {
                    L10n.T("再见~", "Goodbye~"),
                    L10n.T("哦，你要走了。", "Oh, you're leaving."),
                    L10n.T("嗯，再见。", "Hmm, goodbye."),
                    L10n.T("走好。", "Take care."),
                    L10n.T("下次见...吧。", "See you next time... I guess."),
                    L10n.T("哼，走吧。", "Hmph, go."),
                    L10n.T("叮当要继续忙了。", "Dingdang needs to get back to work."),
                    L10n.T("再见，陌生人。", "Goodbye, stranger.")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        /// <summary>
        /// 闲聊对话（低频触发，用于待机时随机显示）
        /// </summary>
        private string GetIdleDialogue(int level)
        {
            string[] dialogues;
            
            if (level >= 5)
            {
                // 高好感度闲聊 - 分享心事（包含笑脸背景）
                dialogues = new string[]
                {
                    L10n.T("叮当在等你呢~...才、才没有！", "Dingdang is waiting for you~ ...n-not!"),
                    L10n.T("你知道吗，叮当以前在J-Lab很孤单...", "You know, Dingdang was very lonely in J-Lab..."),
                    L10n.T("叮当有时候会想，为什么叮当和其他哥布林不一样...", "Dingdang sometimes wonders why Dingdang is different from other goblins..."),
                    L10n.T("嘿嘿，叮当今天收集了很多闪闪发光的东西~", "Hehe, Dingdang collected many shiny things today~"),
                    L10n.T("叮当喜欢这里...比J-Lab好多了...", "Dingdang likes it here... much better than J-Lab..."),
                    L10n.T("你是叮当第一个真正的朋友...", "You're Dingdang's first real friend..."),
                    L10n.T("叮当在想，如果叮当没有被创造出来会怎样...", "Dingdang is thinking, what if Dingdang was never created..."),
                    L10n.T("那些哥布林还是会欺负叮当...但有你在叮当不怕！", "Those goblins still bully Dully... but with you Dingdang is not afraid!"),
                    L10n.T("叮当的智慧是J-Lab给的...但叮当的心是自己的！", "Dingdang's intelligence is from J-Lab... but Dingdang's heart is Dingdang's own!"),
                    L10n.T("嘿，你有没有觉得今天的天气很好~", "Hey, don't you think the weather is nice today~"),
                    // 笑脸背景相关
                    L10n.T("你问叮当为什么总是笑？...这是J-Lab做的...", "You ask why Dingdang always smiles? ...J-Lab did this..."),
                    L10n.T("叮当的笑脸...是实验室刻上去的...叮当想哭也哭不出来...", "Dingdang's smile... was carved by the lab... Dingdang can't cry even if wanting to..."),
                    L10n.T("有时候叮当很难过...但脸上还是在笑...这种感觉很奇怪...", "Sometimes Dingdang is sad... but still smiling... it feels strange..."),
                    L10n.T("你是第一个问叮当为什么笑的人...其他人都以为叮当很开心...", "You're the first to ask why Dingdang smiles... others think Dingdang is happy..."),
                    L10n.T("J-Lab说笑脸的哥布林更容易被接受...但叮当还是被欺负...", "J-Lab said a smiling goblin is more acceptable... but Dingdang was still bullied...")
                };
            }
            else
            {
                // 低好感度闲聊 - 自言自语（包含笑脸背景）
                dialogues = new string[]
                {
                    L10n.T("...", "..."),
                    L10n.T("叮当在想事情...别打扰叮当。", "Dingdang is thinking... don't disturb Dingdang."),
                    L10n.T("哼，叮当才不需要朋友...", "Hmph, Dingdang doesn't need friends..."),
                    L10n.T("那些哥布林又在远处看叮当了...", "Those goblins are watching Dingdang from afar again..."),
                    L10n.T("叮当不是怪物...叮当只是不一样...", "Dingdang is not a monster... Dingdang is just different..."),
                    L10n.T("J-Lab的日子...叮当不想回忆...", "The days in J-Lab... Dingdang doesn't want to remember..."),
                    L10n.T("叮当一个人也可以的...", "Dingdang can be alone..."),
                    L10n.T("为什么大家都不理解叮当...", "Why doesn't anyone understand Dingdang..."),
                    L10n.T("叮当的智慧是诅咒还是祝福呢...", "Is Dingdang's intelligence a curse or a blessing..."),
                    L10n.T("叮当好无聊...", "Dingdang is so bored..."),
                    // 笑脸背景相关
                    L10n.T("叮当的脸...为什么总是在笑...", "Dingdang's face... why is it always smiling..."),
                    L10n.T("叮当明明很难过...但脸却在笑...", "Dingdang is clearly sad... but the face is smiling..."),
                    L10n.T("这张笑脸...是J-Lab的杰作...", "This smile... is J-Lab's masterpiece..."),
                    L10n.T("叮当想哭...但叮当哭不出来...", "Dingdang wants to cry... but Dingdang can't...")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
    }
}
