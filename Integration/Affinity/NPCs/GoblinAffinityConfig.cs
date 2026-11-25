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
    public partial class GoblinAffinityConfig : INPCAffinityConfig, INPCGiftConfig, INPCDialogueConfig, INPCShopConfig, INPCGiftContainerConfig, INPCRelationshipDialogueConfig
    {
        /// <summary>Shared singleton to avoid rebuilding config instances.</summary>
        public static readonly GoblinAffinityConfig Instance = new GoblinAffinityConfig();

        // ============================================================================
        // 常量定义
        // ============================================================================
        
        /// <summary>哥布林NPC唯一标识符</summary>
        public const string NPC_ID = "goblin_dingdang";
        
        /// <summary>砖石惩罚值</summary>
        public const int BRICK_STONE_PENALTY = -20;
        
        /// <summary>钻石解锁等级（同时也是商店解锁等级）</summary>
        public const int DIAMOND_UNLOCK_LEVEL = 2;
        
        /// <summary>商店解锁等级（与钻石解锁等级一致）</summary>
        public const int SHOP_UNLOCK_LEVEL = 2;
        
        /// <summary>冷萃液解锁等级</summary>
        public const int COLD_QUENCH_UNLOCK_LEVEL = 4;
        
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
                        // 物品名一律引用各自 Config 的 GetDisplayName()，不要抄字面量：
                        // 这里的「冷淬液」曾被抄成「冷萃液」，玩家在好感面板看到的是错别字，
                        // 而物品本体仍叫「冷淬液」，对不上号。
                        { 2, new[] { L10n.T("商店", "Shop"), DiamondConfig.GetDisplayName() } },
                        { 3, new[] { L10n.T("10%折扣", "10% Discount"), L10n.T("叮当的故事（一）", "Dingdang's Story (I)") } },
                        { 4, new[] { ColdQuenchFluidConfig.GetDisplayName(), TemperingSolventConfig.GetDisplayName() } },
                        { 5, new[] { L10n.T("叮当的故事（二）", "Dingdang's Story (II)") } },
                        { 6, new[] { L10n.T("15%折扣", "15% Discount"), TemperingSolventConfig.GetDisplayName() + L10n.T(" ×2（赠送）", " ×2 (gift)") } },
                        { 7, new[] { DiamondRingConfig.GetDisplayName() } },
                        { 8, new[] { L10n.T("叮当的故事（三）", "Dingdang's Story (III)") } },
                        { 10, new[] { L10n.T("叮当的故事（四）", "Dingdang's Story (IV)"), L10n.T("20%折扣", "20% Discount"), AffixForgeStoneConfig.GetDisplayName() } }
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
                        { BossRushItemIds.BirthdayCake, 150 },    // +150好感度
                        { DiamondConfig.TYPE_ID, 80 },  // 钻石 +80好感度
                        { DiamondRingConfig.TYPE_ID, DiamondRingConfig.AFFINITY_BONUS }  // 钻石戒指 +500好感度
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
                EnsureTextLanguage();
                if (_positiveBubbles == null)
                {
                    _positiveBubbles = new string[]
                    {
                        // 傲娇反应（表面嫌弃实际开心）
                        L10n.T("成色挺正，敲起来声音脆！叮当先收进抽屉，谁也不给看。", "Good metal. Rings clean! Into Dingdang's drawer, and nobody else gets to see it."),
                        L10n.T("别、别误会！叮当只是觉得扔掉可惜！", "D-don't get the wrong idea! Dingdang just thinks it's a waste to throw away!"),
                        L10n.T("你...你怎么知道叮当喜欢这个的！", "H-how did you know Dingdang likes this!"),
                        L10n.T("哼，算你有点眼光...", "Hmph, you have some taste..."),
                        L10n.T("叮当才、才没有很开心呢！", "Dingdang is n-not happy at all!"),
                        L10n.T("放这儿。不对，放这儿。……算了，叮当揣兜里。", "Put it here. No, here. ...Forget it, it's going in Dingdang's pocket."),

                        // 真情流露
                        L10n.T("哇！闪闪发光的！叮当最喜欢了！", "Wow! So shiny! Dingdang loves it!"),
                        L10n.T("这个...比实验室里那些冷冰冰的东西好多了...", "This... is much better than those cold things in the lab..."),
                        L10n.T("等等，叮当先把手上的油擦干净再拿。", "Wait. Dingdang has to wipe the grease off before touching it."),
                        L10n.T("叮当……叮当这会儿手有点抖，锤子都拿不稳了。", "Dingdang... Dingdang's hands are a bit shaky. Can't even hold the hammer straight."),
                        L10n.T("叮当把锤子放下了。这个得用两只手接。", "Dingdang put the hammer down. This one needs both hands."),
                        L10n.T("这个叮当舍不得用。摆架子上，每天看一眼。", "Too nice to use. It goes on Dingdang's shelf. One look a day."),

                        // 活泼反应
                        L10n.T("太棒了太棒了！叮当今天敲铁都要哼歌了！", "Amazing, amazing! Dingdang's going to hum while hammering all day!"),
                        L10n.T("嘿嘿，这个叮当超级喜欢！", "Hehe, Dingdang super likes this!"),
                        L10n.T("哇哦！你从哪儿找来的？叮当翻了好几个垃圾堆都没翻到！", "Whoa! Where'd you find this? Dingdang dug through a bunch of junk piles and never found one!"),
                        L10n.T("叮当先咬一口验验……是真的！不是假货！", "Dingdang's giving it a bite to check... It's real! Not a fake!"),
                        L10n.T("这个比叮当见过的都好看！", "This is prettier than anything Dingdang has ever seen!"),
                        L10n.T("叮当决定了！你今天进门不用敲门。", "Dingdang has decided! You don't have to knock today."),

                        // 背景相关
                        L10n.T("这个能换叮当一整个下午的好心情！", "This is worth a whole afternoon of good mood for Dingdang!"),
                        L10n.T("那些哥布林只会抢叮当的东西，你却送给叮当...", "Those goblins only steal from Dingdang, but you give to Dingdang..."),
                        L10n.T("叮当的宝贝盒子以前是空的。现在有东西了。", "Dingdang's treasure box used to be empty. Not anymore."),
                        L10n.T("你...你不觉得叮当是怪物吗？", "You... don't think Dingdang is a monster?"),
                        L10n.T("叮当认得这个！叮当识字，也识货。", "Dingdang knows what this is! Dingdang can read, and knows good stuff on sight."),
                        L10n.T("别的哥布林管叮当叫怪胎。怪胎今天收到礼物了，气死他们。", "The other goblins call Dingdang a freak. Well, the freak got a present today. Bet that stings."),

                        // 可爱反应
                        L10n.T("呜……叮当鼻子有点酸。是炉灰，是炉灰！", "Ugh... Dingdang's nose stings. It's forge ash. Forge ash!"),
                        L10n.T("叮当的心扑通扑通的...一定是吃坏肚子了！", "Dingdang's heart is pounding... must have eaten something bad!"),
                        L10n.T("你、你下次还可以再送...如果你想的话！", "Y-you can give more next time... if you want!"),
                        L10n.T("叮当把它跟最亮的那颗螺丝摆在一起了。它俩挺配。", "Dingdang put it next to the shiniest screw. They look good together."),
                        L10n.T("哼哼，叮当就知道你会送好东西！", "Hmph, Dingdang knew you'd give something good!"),
                        L10n.T("叮当得给它腾个地方。边角料挪挪……再挪挪。", "Dingdang needs to make room for it. Shove the scrap over... a bit more."),

                        // 笑脸背景相关
                        L10n.T("这次是真的在笑。你分不出来吧？叮当自己分得出来。", "This time it's a real smile. You can't tell, can you? Dingdang can."),
                        L10n.T("叮当要把它挂在锤柄上！走到哪里都带着！", "Dingdang will hang it on the hammer handle! It goes everywhere with Dingdang!"),
                        L10n.T("今晚叮当要抱着它睡……不对，是放在枕头边守着！", "Tonight Dingdang will sleep hugging it... no, keeping it by the pillow to guard it!")
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
                EnsureTextLanguage();
                if (_negativeBubbles == null)
                {
                    _negativeBubbles = new string[]
                    {
                        // 傲娇生气
                        L10n.T("哼！你是故意的对不对！", "Hmph! You did this on purpose, right!"),
                        L10n.T("这玩意儿丢炉子里都嫌冒黑烟。别搁砧子上占地方。", "Even the furnace would choke on this. Get it off the anvil."),
                        L10n.T("你、你是不是看不起叮当！", "A-are you looking down on Dingdang!"),
                        L10n.T("叮当才不稀罕这种东西！", "Dingdang doesn't want this kind of thing!"),
                        L10n.T("哼，叮当要记住这笔账！", "Hmph, Dingdang will remember this!"),
                        
                        // 委屈反应
                        L10n.T("呜……叮当今天特意早早开了门，就等来这个。", "Ugh... Dingdang opened up early today, and this is what shows up."),
                        L10n.T("这个...让叮当想起以前被当实验品的日子...", "This... reminds Dingdang of being treated like a test subject..."),
                        L10n.T("你也觉得叮当只配收到垃圾吗...", "Do you also think Dingdang only deserves trash..."),
                        L10n.T("那些哥布林也是这样对叮当的...", "Those goblins treated Dingdang the same way..."),
                        L10n.T("叮当...叮当不想要这个...", "Dingdang... Dingdang doesn't want this..."),
                        
                        // 愤怒反应
                        L10n.T("这是什么破烂！拿走！", "What is this junk! Take it away!"),
                        L10n.T("你在逗叮当玩吗！", "Are you messing with Dingdang!"),
                        L10n.T("哥布林也是要面子的！你当叮当这儿是废品站？", "Goblins have pride too! You think this place is a scrapyard?"),
                        L10n.T("哼！叮当不想理你了！", "Hmph! Dingdang doesn't want to talk to you anymore!"),
                        L10n.T("叮当生气了！今天的铁都要替你挨揍！", "Dingdang's mad! Today's iron is taking the beating for you!"),
                        
                        // 失望反应
                        L10n.T("叮当还以为...算了...", "Dingdang thought... never mind..."),
                        L10n.T("叮当把它放门口了。门口是放不要的东西的地方。", "Dingdang left it by the door. The door's where unwanted stuff goes."),
                        L10n.T("叮当看了一眼。就一眼，够了。", "Dingdang looked at it once. Once was plenty."),
                        L10n.T("这种东西...叮当在垃圾堆里见多了...", "This kind of thing... Dingdang has seen plenty in the trash..."),
                        L10n.T("下回再送这个，叮当就拿它垫桌脚。", "Bring this again and Dingdang's using it to prop up the table leg."),
                        
                        // 笑脸背景相关
                        L10n.T("叮当很难过……可叮当的脸还是在笑。你看不出来，对吧。", "Dingdang is upset... but Dingdang's face is still smiling. You can't tell, can you?"),
                        L10n.T("这个……叮当碰都不想碰。拿走啦。", "This... Dingdang doesn't even want to touch it. Take it away."),
                        L10n.T("叮当今天不想说话了……锤子也不想敲了。", "Dingdang doesn't feel like talking today... or hammering either.")
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
                EnsureTextLanguage();
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
                        L10n.T("嗯。放那儿吧，叮当忙完再看。", "Mm. Leave it there. Dingdang will look once the work's done."),
                        L10n.T("这个叮当可以用得上。", "Dingdang can use this."),
                        L10n.T("这个叮当收了。不问来路。", "Dingdang will take it. No questions asked."),
                        L10n.T("还不错，叮当喜欢收集东西。", "Not bad, Dingdang likes collecting things."),
                        L10n.T("叮当拿锤子敲了一下。嗯，结实。", "Dingdang gave it a tap with the hammer. Mm. Sturdy."),
                        
                        // 好奇反应
                        L10n.T("这是什么？叮当以前没见过...", "What's this? Dingdang hasn't seen this before..."),
                        L10n.T("嗯？这个东西有什么用？", "Hmm? What's this thing for?"),
                        L10n.T("叮当要研究研究这个...", "Dingdang needs to study this..."),
                        L10n.T("有意思……叮当翻过来看看底下有没有刻字。", "Interesting... Dingdang's flipping it over to check for markings underneath."),
                        L10n.T("这个拆开能有几颗螺丝？叮当数数。", "How many screws if Dingdang takes this apart? Let's count."),
                        L10n.T("叮当估摸着，回炉能出小半块好铁。", "Dingdang reckons it'd melt down into a decent little lump of iron."),

                        // 背景相关
                        L10n.T("比那些哥布林抢来的东西好多了...", "Much better than what those goblins steal..."),
                        L10n.T("有人记得给叮当带东西……叮当不太会应付这种事。", "Someone remembered to bring Dingdang something... Dingdang doesn't know what to do with that."),
                        L10n.T("你...你为什么要对叮当这么好？", "Why... why are you so nice to Dingdang?"),
                        L10n.T("谢……谢谢。这两个字叮当说得还不太顺。", "Th-thanks. Dingdang still isn't great at saying that."),
                        L10n.T("这个叮当会好好珍惜的...才怪！", "Dingdang will treasure this... not!"),
                        L10n.T("实验室里只有领东西，没有收东西。这俩不一样，叮当现在知道了。", "At the lab, things got issued. Nobody ever gave anything. Dingdang knows the difference now."),
                        
                        // 可爱反应
                        L10n.T("嘿嘿...叮当又有新东西了~", "Hehe... Dingdang has something new~"),
                        L10n.T("叮当的架子快放不下了，得再钉一层板子。", "Dingdang's shelf is almost full. Gonna need to nail up another board."),
                        L10n.T("这个叮当要藏在秘密基地里！", "Dingdang will hide this in the secret base!"),
                        L10n.T("叮当今天心情好，不跟你计较。", "Dingdang is in a good mood today, so no grudges."),
                        L10n.T("叮当把它摆工作台上了，干活一抬头就能看见。", "It's going on the workbench. Dingdang can see it every time Dingdang looks up."),
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
                    L10n.T("今天那份叮当已经擦了两遍了。再送，叮当擦不过来。", "Dingdang's already polished today's gift twice. Any more and Dingdang can't keep up."),
                    L10n.T("哼哼，你今天的表现叮当很满意~", "Hmph, Dingdang is satisfied with your performance today~"),
                    
                    // 真情流露
                    L10n.T("今天的礼物叮当超级喜欢！谢谢你~", "Dingdang loves today's gift! Thanks~"),
                    L10n.T("嘿嘿，今天的礼物叮当已经藏在秘密基地了~", "Hehe, Dingdang has hidden today's gift in the secret base~"),
                    L10n.T("今天那份还在叮当兜里揣着，热乎的。明天再说吧。", "Today's gift is still in Dingdang's pocket. Still warm. Tomorrow, okay?"),
                    L10n.T("今天的礼物让叮当想起了...算了，谢谢你！", "Today's gift reminds Dingdang of... never mind, thanks!"),
                    L10n.T("今天那份叮当量过了，刚好塞进宝贝盒子那个空格。", "Dingdang measured today's gift. It fits the empty slot in the treasure box exactly."),
                    
                    // 活泼反应
                    L10n.T("今天的礼物太棒了！明天还要送哦！", "Today's gift is amazing! Give more tomorrow!"),
                    L10n.T("今天的够了！叮当手上还沾着油，别再递了。", "That's enough for today! Dingdang's hands are still greasy. Stop handing things over."),
                    L10n.T("嘿嘿，叮当今天心情超好~", "Hehe, Dingdang is in a super good mood today~"),
                    L10n.T("今天那份挑得准。叮当都怀疑你偷看过叮当的架子。", "Good pick today. Dingdang suspects you've been peeking at Dingdang's shelf."),
                    L10n.T("叮当今天走路都是蹦着的。……你没看见。", "Dingdang's been bouncing around all day. ...You didn't see that.")
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
                    L10n.T("叮当把今天那份翻来覆去看了三遍，还是没看出好在哪。", "Dingdang turned today's gift over three times. Still can't see what's good about it."),
                    L10n.T("那种东西...叮当在垃圾堆里见多了...", "That kind of thing... Dingdang has seen plenty in the trash..."),
                    L10n.T("叮当把它塞砧子底下了。眼不见，心不烦。", "Dingdang shoved it under the anvil. Out of sight, out of mind."),
                    L10n.T("以前的研究员也是这样对叮当的...", "The researchers treated Dingdang the same way..."),
                    
                    // 期待改变
                    L10n.T("明天...明天你会送好东西的对吧？", "Tomorrow... you'll give something good tomorrow, right?"),
                    L10n.T("叮当给你一次机会，明天别再让叮当失望了！", "Dingdang gives you one chance, don't disappoint Dingdang tomorrow!"),
                    L10n.T("哼，这笔叮当记着。明天带对了，就划掉。", "Hmph. Dingdang's writing this one down. Get it right tomorrow and it gets crossed out."),
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
                    L10n.T("叮当的口袋满了！……才怪。不过一天就收一份，这是叮当的规矩。", "Dingdang's pockets are full! ...Not really. But it's one gift a day. Dingdang's rule."),
                    L10n.T("你是不是太闲了？今天已经送过了啦！", "Don't you have anything better to do? You already gave today!"),
                    L10n.T("明天的明天再说。今天叮当还得打三把锄头。", "Tomorrow's gift can wait till tomorrow. Dingdang has three hoes to forge today."),
                    
                    // 平淡反应
                    L10n.T("今天已经收到你的礼物啦~", "Already received your gift today~"),
                    L10n.T("嘿嘿，今天的礼物叮当已经收好了~", "Hehe, Dingdang has kept today's gift~"),
                    L10n.T("今天的收过了。你要是手痒，帮叮当拉两下风箱？", "Already got today's. If your hands are itching for work, give the bellows a couple of pulls?"),
                    L10n.T("今天的份已经收到了，明天再来吧！", "That's today's share! Come back tomorrow!"),
                    L10n.T("你今天送的那个，叮当拿去压图纸了，正好用。", "That thing you brought today? Dingdang's using it to weigh down blueprints. Perfect fit."),
                    
                    // 可爱反应
                    L10n.T("叮当今天已经很满足了！...虽然还想要更多...", "Dingdang is satisfied today! ...though wanting more..."),
                    L10n.T("你今天对叮当很好！叮当会记住的！", "You were nice to Dingdang today! Dingdang will remember!"),
                    L10n.T("嘿嘿，叮当喜欢收礼物~明天还要哦！", "Hehe, Dingdang likes receiving gifts~ More tomorrow!"),
                    L10n.T("叮当把今天的礼物和其他宝贝放在一起了！", "Dingdang put today's gift with other treasures!"),
                    L10n.T("叮当的朋友，掰着指头数，一只手就够了。你算一个。", "Dingdang can count friends on one hand. You're one of them.")
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
            // 哥布林特殊事件：砖石召唤（负面反应）
            if (eventKey == "AfterBrickStone")
            {
                if (level >= 5)
                {
                    return L10n.T("又是假钻石...算了，看在老朋友的份上。", "Another fake diamond... Fine. For an old friend.");
                }
                else
                {
                    return L10n.T("这不是真钻石！叮当生气了！", "That's no real diamond! Dingdang is angry!");
                }
            }
            
            // 哥布林特殊事件：钻石召唤（正面反应）
            if (eventKey == "AfterDiamond")
            {
                if (level >= 5)
                {
                    return L10n.T("真钻石！叮当最喜欢你了！", "Real diamond! Dingdang loves you the most!");
                }
                else
                {
                    return L10n.T("哇！真钻石！叮当好开心！", "Wow! Real diamond! Dingdang is so happy!");
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

        public string GetRelationshipDialogue(string eventKey, int level)
        {
            switch (eventKey)
            {
                case "dialogue_greeting_married":
                case "dialogue_idle_married":
                    return GetMarriedGreetingOrIdleDialogue();
                case "gift_positive_married":
                    return GetMarriedPositiveGiftDialogue();
                case "gift_normal_married":
                    return GetMarriedNormalGiftDialogue();
                case "gift_negative_married":
                    return GetMarriedNegativeGiftDialogue();
                case "gift_already_positive_married":
                    return GetMarriedGiftAlreadyPositiveDialogue();
                case "gift_already_normal_married":
                    return GetMarriedGiftAlreadyNormalDialogue();
                case "gift_already_negative_married":
                    return GetMarriedGiftAlreadyNegativeDialogue();
                default:
                    return GetPersonaRelationshipDialogue(eventKey);
            }
        }

        private string GetMarriedGreetingOrIdleDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("你来啦！叮当刚刚还在想你今天会不会来找叮当！", "You're here! Dingdang was just wondering if you'd come by today!"),
                L10n.T("嘿嘿，叮当的专属客人到场！心情直接变好了！", "Hehe, Dingdang's one and only customer is here! Mood fixed, just like that!"),
                L10n.T("别站那么远嘛，叮当又不会咬你。今天最多只是想黏着你一点。", "Don't stand so far away. Dingdang won't bite you. At most, Dingdang just wants to stick close to you today."),
                L10n.T("你一出现，叮当就懒得搭理别的鸭子了。", "Once you show up, Dingdang can't be bothered with any other duck."),
                L10n.T("听见你的脚步声，叮当连锤子都放下啦。快过来！", "Dingdang heard your footsteps and put the hammer down. Come here!"),
                L10n.T("叮当刚把手边的活收好，就猜你会过来。", "Dingdang had just put the work aside and already guessed you'd come over."),
                L10n.T("今天也让叮当好好看看你，好不好？", "Let Dingdang get a good look at you today too, okay?"),
                L10n.T("你靠近一点，叮当就安心一点。", "The closer you are, the calmer Dingdang feels."),
                L10n.T("有你在，叮当脑子里那盏白灯就没那么亮了。", "With you here, that white light in Dingdang's head isn't so bright."),
                L10n.T("叮当刚刚还在数屋顶木纹，现在改成数你眨了几次眼。", "Dingdang was counting the wood grain on the ceiling just now. Now Dingdang is counting how many times you blink."),
                L10n.T("你来了就好。今天风箱漏气，叮当一个人跟它较劲了半天。", "Good, you're here. The bellows sprang a leak today. Dingdang spent ages fighting with it alone."),
                L10n.T("叮当认得你敲门的声音。三下，第二下最轻。", "Dingdang knows your knock. Three taps, the second one softest."),
                L10n.T("嘘，别笑叮当。刚才还在对着门口发呆，你就来了。", "Shh, don't laugh. Dingdang was staring at the door, and then you walked in."),
                L10n.T("以前叮当最烦等人。等你嘛……叮当先把炉子烧热了等。", "Dingdang used to hate waiting on anyone. For you, Dingdang gets the forge hot first and waits."),
                L10n.T("如果今天哪儿都不去，陪叮当站一会儿也很好。", "If we go nowhere today, just standing here with Dingdang would already be nice."),
                L10n.T("叮当把最软的位置留给你了，谁来也不给。", "Dingdang saved the softest spot for you. Nobody else gets it."),
                L10n.T("你别总让叮当先开口呀……虽然叮当其实很愿意。", "Don't always make Dingdang speak first... even though Dingdang actually doesn't mind."),
                L10n.T("你回头那会儿，叮当正好在看你。……看锤子，叮当在看锤子。", "When you turned around, Dingdang was looking at you. ...At the hammer. Dingdang was looking at the hammer."),
                L10n.T("以前叮当讨厌别人盯着脸看，只有你看过来时不难受。", "Dingdang used to hate people staring at the face. You're the only one who doesn't make it hurt."),
                L10n.T("来吧，今天的第一句好话，叮当想先听你说。", "Come on, Dingdang wants the first nice thing of today to come from you.")
            });
        }

        private string GetMarriedPositiveGiftDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("哇！这是给叮当的？你怎么挑的，正好是叮当缺的那块！", "Whoa! For Dingdang? How'd you know? It's exactly the piece Dingdang was missing!"),
                L10n.T("叮当要把它藏到最安全的地方，谁都不给看！", "Dingdang is going to hide this in the safest place. Nobody else gets to see it!"),
                L10n.T("你送的东西，叮当会一直留着。一直一直留着！", "If it's from you, Dingdang will keep it forever. Forever forever!"),
                L10n.T("嘿嘿，叮当要把它别在围裙上，干活低头就能看见！", "Hehe, Dingdang's pinning it to the apron. One look down while working and there it is!"),
                L10n.T("你怎么这么会哄叮当……再这样叮当会越来越贪心的。", "How are you so good at making Dingdang happy... keep this up and Dingdang will only get greedier for it."),
                L10n.T("叮当刚把边角料扫成堆，最亮那块还没舍得回炉。现在有更亮的了。", "Dingdang just swept the scrap into a pile and couldn't bear to melt the shiniest piece. Now there's something shinier."),
                L10n.T("叮当要给它做个专门的小盒子，还要写上“不许别人碰”。", "Dingdang is making a special little box for this, with 'Hands Off' written on it."),
                L10n.T("嘿嘿，叮当本来还想装一下不在乎的……没忍住。", "Hehe, Dingdang was going to pretend not to care... Couldn't manage it."),
                L10n.T("这东西亮晶晶的。叮当举着它对炉火照了好几遍。", "It sparkles. Dingdang held it up to the forge light again and again."),
                L10n.T("叮当今天可以开心很久很久了，都是你害的。", "Now Dingdang gets to stay happy for a very long time today. That's your fault."),
                L10n.T("叮当要在锤柄上再刻一道。今天值得刻。", "Dingdang's carving another notch in the hammer handle. Today earns one."),
                L10n.T("如果有人敢碰它，叮当就咬人。真的。", "If anyone dares to touch it, Dingdang will bite. Really."),
                L10n.T("你每回都挑得这么准，太犯规了。叮当得把愿望单藏严实点。", "You get it right every single time. That's cheating. Dingdang needs to hide the wish list better."),
                L10n.T("叮当要炫耀一下……不对，只给自己偷偷看。", "Dingdang wants to show it off... no, Dingdang will keep it for private admiring."),
                L10n.T("实验室里只发编号牌，从来不发礼物。", "The lab handed out number tags. Never presents."),
                L10n.T("你一出手就这么厉害，叮当根本招架不住。", "When you go all out like this, Dingdang doesn't stand a chance."),
                L10n.T("叮当现在想亲你一下……咳，先记账！", "Dingdang kind of wants to kiss you right now... ahem, putting that on the tab first!"),
                L10n.T("好喜欢……叮当说的是礼物。也不全是礼物。", "Love it... Dingdang means the gift. Not only the gift."),
                L10n.T("叮当会把它跟最重要的宝贝放一起。那个盒子在哪儿，只有你知道。", "Dingdang's putting it with the most important treasures. Only you know where that box is."),
                L10n.T("今天不许别人惹叮当生气，叮当已经被你宠满了。", "Nobody is allowed to ruin Dingdang's mood today. You've already spoiled Dingdang plenty.")
            });
        }

        private string GetMarriedNormalGiftDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("给叮当的礼物就是好礼物！这是叮当特批的！", "Any gift for Dingdang is a good gift! Dingdang's special approval!"),
                L10n.T("叮当收下啦！人来了，还顺手带了礼物，完美！", "Dingdang accepts! You came by and brought a gift too. Perfect!"),
                L10n.T("嗯嗯，叮当找个铁盒装起来，防潮。", "Mm-hmm. Dingdang's putting it in a tin. Keeps the damp out."),
                L10n.T("只要是你挑的，叮当都会认真看一眼，再认真喜欢一下。", "If you picked it, Dingdang will look at it seriously, then like it seriously too."),
                L10n.T("你每次带东西来，叮当都会觉得今天没白等。", "Whenever you bring something, Dingdang feels the waiting was worth it today."),
                L10n.T("这个好，跟你一样，不吵不闹的，叮当看着就高兴。", "This is nice. Like you, quiet and steady, and it makes Dingdang happy."),
                L10n.T("叮当喜欢这种小东西。挂工具墙上，跟钳子挤一块儿。", "Dingdang likes little things like this. It's going on the tool wall, right next to the pliers."),
                L10n.T("谢谢啦，你今天也很会照顾叮当。", "Thanks. You take good care of Dingdang today too."),
                L10n.T("收到啦~叮当决定今天对你更温柔一点。", "Received~ Dingdang decides to be a little gentler with you today."),
                L10n.T("不贵也没关系，叮当把它摆架子最前排。", "Doesn't matter if it's cheap. It goes on the front of Dingdang's shelf."),
                L10n.T("嗯，叮当闻一下就知道这是“你来过”的味道。", "Mm. One sniff and Dingdang can tell this carries the smell of 'you were here.'"),
                L10n.T("叮当喜欢你边说话边把东西递过来的样子。", "Dingdang likes how you hand things over while you're still talking."),
                L10n.T("这种礼物最适合现在了，叮当会好好用。", "This kind of gift suits today perfectly. Dingdang will use it well."),
                L10n.T("你总会记得给叮当带点什么，真好。", "You always remember to bring something for Dingdang. That's nice."),
                L10n.T("叮当先收着，晚上还要拿出来再看一遍。", "Dingdang will keep it for now, then take it out again tonight for another look."),
                L10n.T("这是今天的小奖励吗？叮当接受。", "Is this today's little reward? Dingdang accepts."),
                L10n.T("普通东西嘛，你送的就不普通。", "It's an ordinary thing. From you, it's not."),
                L10n.T("叮当会把它放到手边，想你的时候就看一眼。", "Dingdang will keep it close at hand and glance at it whenever missing you."),
                L10n.T("谢谢你呀，今天的心情又往上升了一点点。", "Thank you! Dingdang's mood just went up another notch."),
                L10n.T("你送来的东西，叮当才舍不得随便乱放。", "Dingdang would never toss around anything you brought.")
            });
        }

        private string GetMarriedNegativeGiftDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("这个不太对味...不过叮当知道你不是故意的。", "This isn't quite Dingdang's taste... but Dingdang knows you didn't mean it badly."),
                L10n.T("唔，今天挑得有点失手。没关系，下次再来！", "Mm, your pick was a little off today. That's okay, try again next time!"),
                L10n.T("这个嘛……叮当拿它当锤子的靶子，也算没浪费。", "Well... Dingdang can use it for hammer practice. Not a total waste."),
                L10n.T("这次礼物不太行，幸好你的表情很可爱，勉强抵消了。", "This gift isn't really it, but your expression is cute enough to almost make up for it."),
                L10n.T("嗯...这个会让叮当想皱眉，但叮当不想对你皱眉。", "Mm... this makes Dingdang want to frown, but Dingdang doesn't want to frown at you."),
                L10n.T("你是不是太急着来见叮当，才拿错了？", "Were you in too much of a hurry to see Dingdang and grabbed the wrong thing?"),
                L10n.T("这东西留着也行，就当提醒你下次要更懂叮当一点。", "Keeping this is fine. It'll just remind you to understand Dingdang a little better next time."),
                L10n.T("叮当先收下，但要记你一笔“补偿抱抱”。", "Dingdang will take it anyway, but this goes on your 'compensation hug' tab."),
                L10n.T("挑得有点偏。叮当先收着，回头熔了打钉子。", "That pick was a little off. Dingdang will hang onto it, maybe melt it into nails later."),
                L10n.T("唔，不喜欢这个味道...不过喜欢你站在这儿解释。", "Mm, Dingdang doesn't like the feel of this... but does like you standing here trying to explain."),
                L10n.T("这个叮当用不太上。下次先来问叮当嘛。", "Dingdang can't do much with this. Ask Dingdang before picking the next one."),
                L10n.T("叮当会诚实告诉你：下次别选这个啦。", "Dingdang will be honest with you: don't pick this one next time."),
                L10n.T("要不是看在你亲手送来的份上，叮当早就嫌弃出声了。", "If this hadn't come from your own hands, Dingdang would have complained out loud already."),
                L10n.T("没挑中也没关系，你继续学，叮当继续等。", "It's okay that you missed. You keep learning, Dingdang will keep waiting."),
                L10n.T("礼物挑歪了，人没来错。", "Wrong gift. Right person, though."),
                L10n.T("叮当现在有一点点失望，只有一点点，别怕。", "Dingdang is a tiny bit disappointed right now. Just a tiny bit, don't panic."),
                L10n.T("这次不合口味，回头叮当给你列个喜欢清单？", "Not Dingdang's taste this time. Want Dingdang to make you a favorites list later?"),
                L10n.T("别露出那种紧张表情，叮当不会因为一次失手就生气。", "Don't make that nervous face. Dingdang isn't going to get mad over one miss."),
                L10n.T("这回不对。没事，叮当的炉子也不是每回一点就着。", "Not this time. That's fine. Dingdang's forge doesn't light on the first try every time either."),
                L10n.T("不用急着找别的礼物。先听叮当说说喜欢什么嘛。", "No need to rush off for another gift. Let Dingdang tell you what Dingdang likes.")
            });
        }

        private string GetMarriedGiftAlreadyPositiveDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("今天的礼物已经让叮当开心到转圈圈了，明天再来！", "Today's gift already made Dingdang happy enough to spin in circles. Come again tomorrow!"),
                L10n.T("先停一下！再送下去，叮当就要把你抱住不放了！", "Hold it right there! If you keep going, Dingdang might hug you and refuse to let go!"),
                L10n.T("今天这份叮当还没看够呢，下一份明天再拆！", "Dingdang isn't done admiring this one. The next can wait until tomorrow!"),
                L10n.T("不许再加码了！叮当今天已经够飘了，锤子都抡歪两回了。", "No more raising the stakes! Dingdang's already floating today. Swung the hammer crooked twice."),
                L10n.T("今天这份已经是大成功，再送就要溢出来了。", "Today's one was already a huge success. Any more and it'll overflow."),
                L10n.T("叮当要留一点期待到明天，不然今晚会兴奋得睡不着。", "Dingdang needs to save some anticipation for tomorrow or Dingdang won't be able to sleep tonight."),
                L10n.T("够啦够啦，叮当脸都笑酸了。你摸摸！", "Enough, enough! Dingdang's cheeks hurt from smiling. Feel them!"),
                L10n.T("你再送，叮当就会以为自己在做梦。", "If you give Dingdang more, Dingdang will think this is all a dream."),
                L10n.T("今天这一下已经把叮当哄到晕乎乎了，暂停！", "That last one already made Dingdang dizzy with happiness. Pause!"),
                L10n.T("停停停！叮当再高兴下去，耳朵要飞起来了。", "Stop, stop! Any happier and Dingdang's ears will take off."),
                L10n.T("再送，叮当的宝贝盒子就盖不上啦。明天再带嘛。", "Any more and Dingdang's treasure box won't shut. Bring it tomorrow."),
                L10n.T("叮当得先消化一下这份开心，不然会傻笑一整晚。", "Dingdang has to digest all this happiness first, or the silly smile will last all night."),
                L10n.T("你想把今天变成纪念日吗？已经差不多成功了。", "Are you trying to turn today into an anniversary? You're almost succeeding."),
                L10n.T("先别送啦，叮当要腾出手来牵你。", "No more gifts for now. Dingdang needs both hands free to hold yours."),
                L10n.T("再继续的话，叮当会开始怀疑你是不是打算把叮当宠坏。", "If you keep going, Dingdang will start thinking you're trying to spoil Dingdang rotten."),
                L10n.T("今天挑得这么准，不用再挑了。过来歇会儿。", "You nailed it today. No more hunting. Come sit a bit."),
                L10n.T("够啦，叮当要把它搁炉子边上，暖着慢慢看。", "That's plenty. Dingdang's setting it by the forge to keep warm and look at slowly."),
                L10n.T("暂停一下，叮当得先给它找颗合适的钉子挂起来。", "Hold on. Dingdang needs to find a good nail to hang it on first."),
                L10n.T("等一下，叮当还在高兴呢。让叮当先抱你一下！", "Wait, Dingdang is still grinning over this one. Let Dingdang hug you first!"),
                L10n.T("明天再来，叮当想把想你的时间也留一点出来。", "Come again tomorrow. Dingdang wants to leave some room to miss you too.")
            });
        }

        private string GetMarriedGiftAlreadyNormalDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("一天一份就行啦，剩下的留到明天继续宠叮当！", "One gift a day is enough. Save the rest for tomorrow and keep spoiling Dingdang then!"),
                L10n.T("礼物先欠着，今天多陪叮当说说话。", "Put the gift on hold. Spend today talking with Dingdang a little more instead."),
                L10n.T("叮当已经收到了，接下来你的人留下就行！", "Dingdang already got the gift. Now all that's left is for you to stay!"),
                L10n.T("一天一份，说好了的。叮当可舍不得你把东西都送光。", "One a day, remember? Dingdang doesn't want you giving away everything you have."),
                L10n.T("先停一停，炉子要添炭了。你帮叮当递一下？", "Hold on, the forge needs more coal. Pass some to Dingdang?"),
                L10n.T("你坐近一点，比再递一份东西更有用。", "Sit a little closer. That helps more than handing over another gift."),
                L10n.T("东西先收回去嘛。叮当想空着手牵你。", "Put that away for now. Dingdang wants a free hand to hold yours."),
                L10n.T("今天先这样，明天记得继续想叮当。", "Let's leave it here for today. Just remember to keep thinking about Dingdang tomorrow."),
                L10n.T("叮当先把这一份珍藏好，下一份明天再收。", "Dingdang will treasure this one first. The next one can wait until tomorrow."),
                L10n.T("不急不急，慢慢来，叮当喜欢你天天来一点点。", "No rush. Take it slow. Dingdang likes you coming a little bit every day."),
                L10n.T("今天这次已经足够暖了，再多就太贪心啦。", "This was warm enough for one day. Any more would be too greedy."),
                L10n.T("留点机会给明天的叮当，不然明天会空落落的。", "Save a little for tomorrow's Dingdang, or tomorrow will feel empty."),
                L10n.T("先把礼物省下来。叮当打铁，你在旁边坐着就行。", "Save the gift. Dingdang hammers, you just sit nearby."),
                L10n.T("嗯，今天收工啦，接下来轮到你陪聊。", "Mm, gift work is done for today. Now it's your turn to stay and chat."),
                L10n.T("叮当不缺下一份礼物。缺个帮忙扶铁钳的。", "Dingdang doesn't need another gift. Dingdang needs someone to hold the tongs."),
                L10n.T("礼物明天再带。今天陪叮当把这颗螺丝找出来，好不好？", "Bring it tomorrow. Help Dingdang find this missing screw today, will you?"),
                L10n.T("把东西留到明天吧，叮当想多赚一天期待。", "Leave the next thing for tomorrow. Dingdang wants to earn one more day of anticipation."),
                L10n.T("今天先不收啦。手空着的话，摸摸叮当的头嘛。", "No more gifts today. If your hands are free, pat Dingdang's head."),
                L10n.T("叮当已经很满足了，别一次把明天的份也用掉。", "Dingdang is already satisfied. Don't spend tomorrow's share all at once."),
                L10n.T("收好啦！叮当把工具也收一收，咱们说会儿话。", "All put away! Let Dingdang pack up the tools, then we can talk.")
            });
        }

        private string GetMarriedGiftAlreadyNegativeDialogue()
        {
            return GetRandomRelationshipDialogue(new string[]
            {
                L10n.T("今天先别继续试啦，不然叮当怕你越来越紧张。", "Let's stop trying for today, or Dingdang worries you'll only get more nervous."),
                L10n.T("没挑对也没关系，明天再重新来一次！", "It's okay if you missed the mark. Try again tomorrow!"),
                L10n.T("今天的手感不好就先收工，叮当不想看你自责。", "If today just isn't your day, let's call it here. Dingdang doesn't want to watch you blame yourself."),
                L10n.T("别急着补救啦，越慌越容易选错。", "Don't rush to fix it. The more flustered you get, the easier it is to pick wrong again."),
                L10n.T("今天先收起来吧。明天想带什么，先问问叮当就好。", "Put it away for today. Tomorrow, just ask Dingdang what to bring."),
                L10n.T("再试下去只会让你眉毛越皱越紧，叮当不喜欢看。", "If you keep trying now, you'll only frown harder, and Dingdang doesn't like seeing that."),
                L10n.T("没挑对就没挑对嘛，又不是敲坏了宝贝零件。", "So you picked the wrong thing. It's not like you smashed a precious part."),
                L10n.T("没关系，坏手气也会过去的。", "It's okay. Bad luck passes too."),
                L10n.T("先休息一下吧，叮当不想你为了礼物搞得比打仗还累。", "Take a break. Dingdang doesn't want you more exhausted over gifts than after a battle."),
                L10n.T("叮当知道你在努力，所以今天到这里就好。", "Dingdang knows you're trying, so today's enough."),
                L10n.T("再送也不会马上变对，不如明天带着好心情来。", "Sending more right now won't magically make it right. Come back tomorrow in a better mood instead."),
                L10n.T("今天先放过这个礼物话题，陪叮当待会儿就行。", "Let's let the gift topic go for today. Just stay with Dingdang for a bit."),
                L10n.T("别一直翻背包啦，叮当没生气。过来坐。", "Stop rummaging through your bag. Dingdang isn't angry. Come sit down."),
                L10n.T("暂停一下。叮当给你倒杯凉水，炉子边上热。", "Pause. Dingdang will get you some cold water. It's hot by the forge."),
                L10n.T("硬试没用。叮当打铁打歪了，也是先停手，不是接着砸。", "Forcing it won't help. When Dingdang's hammer goes crooked, Dingdang stops. Doesn't keep pounding."),
                L10n.T("今天就不收第二份啦，明天来之前可别忘了吃饭。", "No second gift today. Don't forget to eat before coming over tomorrow."),
                L10n.T("你手心都出汗了。来，拿叮当的抹布擦擦。", "Your palms are sweaty. Here, use Dingdang's rag."),
                L10n.T("叮当宁愿你明天笑着来，也不想看你今天硬撑。", "Dingdang would rather see you come tomorrow with a smile than watch you force it today."),
                L10n.T("好啦，东西收起来。叮当刚修好个小玩意儿，给你看看？", "All right, put it away. Dingdang just fixed a little gadget. Want to see?"),
                L10n.T("不喜欢礼物，又不是不喜欢你。明天记得再来呀。", "Dingdang doesn't like the gift, not you. Come see Dingdang again tomorrow.")
            });
        }

        private string GetRandomRelationshipDialogue(string[] dialogues)
        {
            if (dialogues == null || dialogues.Length == 0)
            {
                return null;
            }

            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
        
        // ============================================================================
        // INPCShopConfig 实现
        // ============================================================================
        
        public bool ShopEnabled => true;
        public int ShopUnlockLevel => SHOP_UNLOCK_LEVEL;  // 2级解锁商店，与钻石解锁等级一致
        public string ShopName => L10n.T("叮当的小店", "Dingdang's Shop");
        
        public List<ShopItemEntry> GetShopItems()
        {
            return new List<ShopItemEntry>
            {
                new ShopItemEntry(DiamondConfig.TYPE_ID, DIAMOND_UNLOCK_LEVEL, 5),
                new ShopItemEntry(ColdQuenchFluidConfig.TYPE_ID, COLD_QUENCH_UNLOCK_LEVEL, 5),
                new ShopItemEntry(TemperingSolventConfig.TYPE_ID, TemperingSolventConfig.SHOP_UNLOCK_LEVEL, TemperingSolventConfig.SHOP_MAX_STOCK),
                // 钻石戒指：7 级解锁，一次 1 枚（库存跟着好感存档，关店重开不再补）。已婚期间不补；
                // 单身时距上次补货满 3 个游戏日补 1 枚，离婚、戒指丢了也还能再求婚（审查 CR-2026-10-18-004）
                new ShopItemEntry(DiamondRingConfig.TYPE_ID, DiamondRingConfig.UNLOCK_LEVEL, 1) { RestockDays = 3, RestockOnlyWhenSingle = true },
                // 词缀熔石：锻造的唯一消耗材料，游戏内 Wiki 承诺「哥布林商店（要好感度）」。
                // 满级好感后才提供有限补货，前期熔石以 BossRush 掉落为主。
                new ShopItemEntry(AffixForgeStoneConfig.TYPE_ID, 10, 5),
                // 冰霜/雷霆套装（500053-500056）：「上头批出库了」。这里是稳定线，运气线是 Boss 专属掉落格
                // （风暴区 Boss 掉雷霆、「???」Boss 掉冰霜，见 LootAndRewardsSetBonusLoot.cs）。
                // 6 级解锁介于冷淬液 4 级与钻戒 7 级之间；库存 1，每个游戏日补一次（剩余库存跟着好感存档）。
                // 掉落黑名单保留不动：它只挡随机奖池，NPC 商店不查它（词缀熔石同款）。
                new ShopItemEntry(FrostThunderSetConfig.FROST_HELMET_ID, FrostThunderSetConfig.SET_PIECE_UNLOCK_LEVEL, FrostThunderSetConfig.SET_PIECE_MAX_STOCK),
                new ShopItemEntry(FrostThunderSetConfig.FROST_ARMOR_ID, FrostThunderSetConfig.SET_PIECE_UNLOCK_LEVEL, FrostThunderSetConfig.SET_PIECE_MAX_STOCK),
                new ShopItemEntry(FrostThunderSetConfig.THUNDER_HELMET_ID, FrostThunderSetConfig.SET_PIECE_UNLOCK_LEVEL, FrostThunderSetConfig.SET_PIECE_MAX_STOCK),
                new ShopItemEntry(FrostThunderSetConfig.THUNDER_ARMOR_ID, FrostThunderSetConfig.SET_PIECE_UNLOCK_LEVEL, FrostThunderSetConfig.SET_PIECE_MAX_STOCK),
                // P0 五把新武器（500048-500052）：与套装同款「稳定线 + 运气线」两条获取途径。
                // 运气线是五个官方 Boss 的专属掉落格（NewWeaponBossDropHandler，原版地图亦可）。
                // 5 级解锁介于冷淬液 4 级与套装件 6 级之间——它们是品质 5，比品质 6 的套装件早一档；
                // 库存 1 与套装件同款，每个游戏日补一次。
                // 掉落黑名单保留不动：它只挡随机奖池，NPC 商店不查它。
                new ShopItemEntry(NewWeaponIds.ViperDaggerTypeId, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock),
                new ShopItemEntry(NewWeaponIds.SummonStaffTypeId, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock),
                new ShopItemEntry(NewWeaponIds.EnergyShieldTypeId, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock),
                new ShopItemEntry(NewWeaponIds.FrostSpearTypeId, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock),
                new ShopItemEntry(NewWeaponIds.ThunderRingTypeId, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock)
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
                    L10n.T("是你啊！叮当正在……正在发呆！才没有在等你！", "It's you! Dingdang was just... just spacing out! Totally not waiting!"),
                    L10n.T("哼哼，叮当最好的朋友来了~", "Hmph, Dingdang's best friend is here~"),
                    L10n.T("你终于来了！叮当正好在门口透气！", "You finally came! Dingdang just happened to be getting fresh air at the door!"),
                    L10n.T("嘿！叮当就知道你会来找叮当！", "Hey! Dingdang knew you'd come find Dingdang!"),
                    L10n.T("叮当今天把门口的台阶扫了。……顺手扫的。", "Dingdang swept the front step today. ...Just happened to."),
                    L10n.T("你来啦~叮当有好多话想跟你说！", "You're here~ Dingdang has so much to tell you!"),
                    L10n.T("快来快来！今天的铁打得特别顺，叮当要你听听这声儿！", "Come, come! The iron's ringing just right today. Dingdang wants you to hear it!"),
                    L10n.T("哼，你怎么才来！叮当都在这站了好久了……因为喜欢站这！", "Hmph, why so late! Dingdang's been standing here forever... because Dingdang likes standing here!"),
                    L10n.T("跑那么快干嘛，鞋底都快磨平了。过来，炉子还热着。", "What's the rush? You'll wear your boots through. Come in, the forge is still hot."),
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
                    L10n.T("来了啊。正好，帮叮当看看这把刀刃磨得直不直。", "There you are. Good timing. Tell Dingdang if this blade's ground straight."),
                    L10n.T("哦~叮当认识的人来了！", "Oh~ Someone Dingdang knows is here!"),
                    L10n.T("你还记得来找叮当，不错不错。", "You remembered to find Dingdang, not bad."),
                    L10n.T("嘿，叮当正想找人说话呢。", "Hey, Dingdang was just looking for someone to talk to."),
                    L10n.T("叮当今天心情还行。别问为什么，问了就不行了。", "Dingdang's in an okay mood today. Don't ask why, or it won't be.")
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
                    L10n.T("哦，是上回那个外头来的。", "Oh. The outsider from last time.")
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
                    L10n.T("白大褂最爱笑眯眯说话。你少来这一套。", "Lab coats always talked with a smile too. Save it."),
                    L10n.T("叮当不想跟陌生人说话。", "Dingdang doesn't want to talk to strangers."),
                    L10n.T("你是那些研究员派来的吗？叮当不会回去的！", "Are you sent by those researchers? Dingdang won't go back!"),
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
                    L10n.T("你老这样，叮当都不知道手往哪儿放了。", "You keep doing this. Dingdang doesn't even know what to do with these hands."),
                    L10n.T("叮当记仇，也记好。这个记在好的那本上。", "Dingdang holds grudges. Holds onto the good stuff too. This goes in the good book."),
                    L10n.T("以前叮当的朋友只有锤子。现在锤子得排第二了。", "Dingdang's only friend used to be the hammer. Now the hammer's in second place."),
                    L10n.T("叮当……叮当先转过去一下。别问。", "Dingdang... Dingdang needs to turn around for a sec. Don't ask.")
                };
            }
            else if (level >= 5)
            {
                dialogues = new string[]
                {
                    L10n.T("谢谢你的礼物！叮当很喜欢！", "Thanks for the gift! Dingdang likes it!"),
                    L10n.T("嘿嘿，这个挑得不赖嘛~", "Hehe, not a bad pick~"),
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
                    L10n.T("还不错...吧。", "Not bad... maybe."),
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
                    L10n.T("陌生人送的东西……叮当得先翻过来看看有没有机关。", "A gift from a stranger... Dingdang's checking it for traps first.")
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
                    L10n.T("你是叮当最好最好的朋友！叮当想敲锣……没有锣，敲锅也行！", "You're Dingdang's best, best friend! Dingdang wants to bang a gong... no gong, a pot will do!"),
                    L10n.T("叮当以前睡觉都抱着锤子。现在敢放下了。", "Dingdang used to sleep holding the hammer. Now Dingdang can put it down."),
                    L10n.T("叮当决定了！你是叮当一辈子的朋友！", "Dingdang has decided! You're Dingdang's friend for life!"),
                    L10n.T("呜……叮当要是能哭，现在就哭了。还好不能。", "Ugh... if Dingdang could cry, it'd be right now. Good thing Dingdang can't."),
                    L10n.T("叮当那块编号牌，一直压在箱底。今天拿出来给你看了。", "Dingdang's number tag has been at the bottom of the chest all this time. Today Dingdang took it out to show you.")
                };
            }
            else if (level >= 7)
            {
                dialogues = new string[]
                {
                    L10n.T("叮当的账本上，你那页的字写得比谁都工整。", "In Dingdang's ledger, your page has the neatest handwriting of all."),
                    L10n.T("哼哼，你在叮当心里的地位又提高了~", "Hmph, your position in Dingdang's heart has risen~"),
                    L10n.T("叮当决定对你更好一点！...才不是因为喜欢你！", "Dingdang decided to be nicer to you! ...not because Dingdang likes you!"),
                    L10n.T("别人进店只看价签。你嘛……叮当把你的记号刻在铁砧边上了。", "Everyone else just reads the price tags. You, though... Dingdang scratched your mark into the side of the anvil."),
                    L10n.T("叮当现在敢让你站在背后看着干活了。这可不是小事。", "Dingdang can work with you standing right behind now. That's a big deal.")
                };
            }
            else if (level >= 3)
            {
                dialogues = new string[]
                {
                    L10n.T("咱们算是熟了！叮当给你一点折扣吧！", "We're pals now! Dingdang will give you a small discount!"),
                    L10n.T("哼，叮当开始有点喜欢你了...一点点！", "Hmph, Dingdang is starting to like you a bit... just a bit!"),
                    L10n.T("你还不错嘛，叮当愿意跟你做朋友。", "You're not bad, Dingdang is willing to be friends with you."),
                    L10n.T("叮当决定信任你一点点了！", "Dingdang decided to trust you a little bit!"),
                    L10n.T("看在你对叮当不错的份上，给你便宜点！", "Since you're nice to Dingdang, Dingdang will make it cheaper!")
                };
            }
            else
            {
                dialogues = new string[]
                {
                    L10n.T("叮当开始觉得你不是坏人了...", "Dingdang is starting to think you're not a bad person..."),
                    L10n.T("哼，叮当勉强承认你还行。", "Hmph, Dingdang reluctantly admits you're okay."),
                    L10n.T("你……你身上没有消毒水味。白大褂身上都有。", "You... don't smell like disinfectant. The lab coats always did."),
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
                    L10n.T("老朋友专属折扣！叮当只对你这样！", "Special discount for an old friend! Dingdang only does this for you!"),
                    L10n.T("嘿嘿，叮当给你最好的价格~", "Hehe, Dingdang gives you the best price~"),
                    L10n.T("叮当的好东西都给你看！别人可看不到！", "Dingdang shows you all the good stuff! Others can't see!"),
                    L10n.T("你是叮当最常来的客人！", "You're Dingdang's best regular!"),
                    L10n.T("这批货叮当一件件敲过，声儿都对。", "Dingdang tapped every piece in this batch. They all ring true.")
                };
            }
            else if (level >= 3)
            {
                dialogues = new string[]
                {
                    L10n.T("随便看，叮当刚擦过一遍架子。", "Look around. Dingdang just wiped down the shelves."),
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
                    L10n.T("下次再来找叮当！……叮当是说，店还开着。", "Come see Dingdang again! ...Dingdang means the shop. The shop's open."),
                    L10n.T("嘿，路上小心！叮当...叮当会想你的...", "Hey, be careful! Dingdang... Dingdang will miss you..."),
                    L10n.T("再见~叮当期待下次见面！", "Bye~ Dingdang looks forward to next time!"),
                    L10n.T("你要走了吗...叮当有点舍不得...", "You're leaving... Dingdang is a bit reluctant..."),
                    L10n.T("下次带好吃的来！叮当喜欢好吃的！", "Bring something tasty next time! Dingdang likes tasty things!"),
                    L10n.T("拜拜~叮当会好好的！你也要好好的！", "Bye~ Dingdang will be fine! You be fine too!"),
                    L10n.T("哼，走吧走吧...叮当才不会想你...", "Hmph, go go... Dingdang won't miss you..."),
                    L10n.T("记得常来看叮当！叮当一个人很无聊的...", "Remember to visit Dingdang often! Dingdang gets bored alone..."),
                    L10n.T("路上要是捡到亮闪闪的铁片，记得给叮当带回来！", "If you find any shiny scrap on the way, bring it back for Dingdang!"),
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
                    L10n.T("下次见...吧。", "See you next time... maybe."),
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
                    L10n.T("以前叮当一个人吃饭，就把锤子摆在对面。", "Dingdang used to eat alone. Put the hammer across the table."),
                    L10n.T("叮当有时候会想，为什么叮当和其他哥布林不一样...", "Dingdang sometimes wonders why Dingdang is different from other goblins..."),
                    L10n.T("嘿嘿，叮当今天收集了很多闪闪发光的东西~", "Hehe, Dingdang collected many shiny things today~"),
                    L10n.T("叮当喜欢这里...比以前待的地方好多了...", "Dingdang likes it here... much better than where Dingdang used to be..."),
                    L10n.T("叮当今天多打了一把小板凳。……多的那把，你坐。", "Dingdang made an extra little stool today. ...The extra one's yours."),
                    L10n.T("叮当在想，那天要是没在路边捡到这把锤子，现在会在哪儿……", "Dingdang was thinking... if Dingdang hadn't found this hammer by the road that day, where would Dingdang be now..."),
                    L10n.T("那些哥布林今天又在外头转悠。叮当把门闩上了……才不是怕。", "Those goblins were hanging around outside again. Dingdang bolted the door... not because Dingdang's scared."),
                    L10n.T("叮当是被造出来的。可锤子是叮当自己捡的，手艺是叮当自己练的。", "Dingdang was made in a lab. But Dingdang found the hammer, and Dingdang learned the trade."),
                    L10n.T("今天风从北边来，炉子好烧。叮当喜欢这种天。", "Wind's from the north today. The forge burns easy. Dingdang likes days like this."),
                    // 笑脸背景相关
                    L10n.T("你问叮当为什么总是笑？...这张脸不是叮当自己选的...", "You ask why Dingdang always smiles? ...Dingdang didn't choose this face..."),
                    L10n.T("叮当的笑脸...是别人刻上去的...叮当想哭也哭不出来...", "Dingdang's smile... someone carved it... Dingdang can't cry, even when Dingdang wants to..."),
                    L10n.T("今天工作台擦了三遍，亮得能照见你的脸，快来看！", "Dingdang wiped the workbench three times. It's so shiny you can see your face in it. Come look!"),
                    L10n.T("叮当在秘密基地又藏了一颗亮晶晶的石头。只告诉你一个人哦。", "Dingdang hid another sparkly stone in the secret base. You're the only one Dingdang will tell."),
                    L10n.T("锤子敲得比以前稳了。叮当觉得，是因为有人在旁边看着。", "Dingdang's hammer swings steadier than before. Dingdang thinks it's because someone is watching.")
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
                    L10n.T("以前的日子...叮当不想回忆...", "The old days... Dingdang doesn't want to remember..."),
                    L10n.T("叮当一个人也可以的...", "Dingdang can manage alone..."),
                    L10n.T("编号……不对，叮当有名字。叮当有名字的。", "Number... no. Dingdang has a name. Dingdang has a name."),
                    L10n.T("叮当要是笨一点，会不会好过些...", "Would Dingdang have it easier if Dingdang were dumber..."),
                    L10n.T("叮当好无聊...", "Dingdang is so bored..."),
                    // 笑脸背景相关
                    L10n.T("叮当在数螺丝……一颗，两颗……哎呀，数乱了。", "Dingdang is counting screws... one, two... oops, lost count."),
                    L10n.T("这块铁片敲了半天，怎么还是歪的……", "Dingdang has hammered this scrap for ages. Why is it still crooked..."),
                    L10n.T("叮当今天不想被人看见……工作台底下比较安全。", "Dingdang doesn't want to be seen today... under the workbench is safer."),
                    L10n.T("没人来的时候，叮当就和锤子说话。锤子不会嫌叮当吵。", "When nobody comes by, Dingdang talks to the hammer. The hammer never says Dingdang is noisy.")
                };
            }
            
            return dialogues[UnityEngine.Random.Range(0, dialogues.Length)];
        }
    }
}
