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
                        { 2, new[] { L10n.T("10%治疗折扣", "10% Healing Discount") } },
                        { 3, new[] { L10n.T("安神滴剂 x5", "Calming Drops x5") } },
                        { 4, new[] { L10n.T("20%治疗折扣", "20% Healing Discount") } },
                        { 5, new[] { L10n.T("羽织的回忆（上）", "Yu Zhi's Memories (Part 1)") } },
                        { 6, new[] { L10n.T("25%治疗折扣", "25% Healing Discount") } },
                        { 7, new[] { L10n.T("30%治疗折扣", "30% Healing Discount") } },
                        { 8, new[] { L10n.T("\u5E73\u5B89\u62A4\u8EAB\u7B26", "Peace Charm") } },
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
                if (_positiveBubbles == null)
                {
                    _positiveBubbles = new string[]
                    {
                        // 傲娇反应
                        L10n.T("哼...眼光倒还不错，我收下了。", "Hmph... your taste is decent. I'll take it."),
                        L10n.T("你...你怎么会挑到我真正喜欢的东西？", "H-how did you manage to pick something I'd really like?"),
                        L10n.T("别误会，我只是觉得扔掉太可惜了。", "Don't get the wrong idea. It would just be a waste to throw this away."),
                        L10n.T("这个...我会好好收着，你别多想。", "This... I'll keep it properly. Don't read too much into it."),
                        L10n.T("哼，至少说明你还是有点品位的。", "Hmph. At least it proves you have some taste."),
                        L10n.T("我、我才没有高兴得忘形。", "I-I'm not so happy that I've lost my composure."),
                        
                        // 真情流露
                        L10n.T("哇...真好看。谢、谢谢你。", "Wow... it's really beautiful. Th-thank you."),
                        L10n.T("这个比J-Lab里那些冷冰冰的器械顺眼多了。", "This is far gentler on the eyes than those cold instruments in J-Lab."),
                        L10n.T("你是第一个...会认真替我挑礼物的人。", "You're the first person to... seriously pick out a gift for me."),
                        L10n.T("我...真的很开心。", "I... really am happy."),
                        L10n.T("太好了——咳，我是说，确实不错。", "This is wonderful— ahem, I mean, it's quite nice."),
                        L10n.T("这个我很喜欢，是真的。", "I really like this. I mean it."),
                        
                        // 感动反应
                        L10n.T("你还真懂怎么让人心软...我刚刚什么都没说。", "You really do know how to soften someone's heart... I didn't say anything just now."),
                        L10n.T("我想把它放在抬眼就能看到的地方。...柜子上层就很好。", "I want to put it somewhere I can see at a glance. ...The top shelf should do nicely."),
                        L10n.T("在实验室的时候，从来没人给过我这种东西。", "Back in the lab, no one ever gave me anything like this."),
                        L10n.T("你不觉得给一个护士送这个有点奇怪吗？...但我很喜欢。", "Don't you think giving this to a nurse is a little strange? ...But I like it a lot."),
                        L10n.T("这东西真温柔...像鸭科夫难得安静下来的时候。", "This feels so gentle... like those rare quiet moments Duckov sometimes has."),
                        L10n.T("只是眼睛有点酸而已，才不是被你感动到了。", "My eyes just sting a little, that's all. It's not because you moved me."),
                        
                        // 可爱反应
                        L10n.T("心跳突然变快了...一定是你吓到我了。", "My heart suddenly sped up... you must have startled me."),
                        L10n.T("你、你下次要是还想送，我也不会拦你。", "I-if you want to bring me something again next time, I won't stop you."),
                        L10n.T("这份心意我会记很久的。嘴上不说，不代表我不在意。", "I'll remember this kindness for a long time. Just because I don't say it doesn't mean I don't care."),
                        L10n.T("谢谢你让我在这片硝烟里，还能觉得心里暖了一下。", "Thank you for making my heart feel a little warmer in the middle of all this smoke and ruin."),
                        L10n.T("这对心情的效果，比很多药都好。", "This works better on the heart than a lot of medicine does."),
                        L10n.T("我在J-Lab看惯了冷脸，反而有点招架不住你的温柔。", "I got used to cold faces in J-Lab, so your kindness catches me off guard."),
                        
                        // 深情反应
                        L10n.T("这个真的很美。像...算了，我不说了。", "This is really beautiful. Like... never mind, I won't say it."),
                        L10n.T("哼，以后给你的折扣可以再多一点。...不是因为被收买了。", "Hmph. I can give you a slightly better discount from now on. ...Not because I've been bribed."),
                        L10n.T("你让我觉得，当初拼命逃离J-Lab，确实是对的。", "You make me feel that struggling to escape J-Lab really was the right choice."),
                        L10n.T("每次收到你的礼物，我这一天都会轻松一点。", "Every time I receive something from you, my whole day feels a little lighter."),
                        L10n.T("我嘴上总是不饶人...但我心里是真的很感激你。", "My mouth is always sharper than it should be... but I truly am grateful to you."),
                        L10n.T("至少这一刻，我是真的在笑。", "At least in this moment, I'm really smiling.")
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
                        L10n.T("哼，你是故意来气我的吧。", "Hmph. You came here just to annoy me, didn't you?"),
                        L10n.T("你这是看不起我，还是看不起我的眼光？", "Are you looking down on me, or just on my taste?"),
                        L10n.T("这种东西你也拿得出手？你的品味真让人担心。", "You actually thought this was worth giving? Your taste is worrying."),
                        L10n.T("拿开，我不想让这东西进我的柜子。", "Take it away. I don't want this anywhere near my cabinet."),
                        L10n.T("行，我记住了。", "Fine. I'll remember this."),
                        
                        // 委屈反应
                        L10n.T("我还以为...至少你会更懂一点。", "I thought... at least you'd understand me a little better than this."),
                        L10n.T("这让我想起J-Lab那些让人反胃的废料。", "This reminds me of the kind of waste in J-Lab that made me sick just looking at it."),
                        L10n.T("你真觉得一个护士会喜欢这种东西？", "Do you really think a nurse would appreciate something like this?"),
                        L10n.T("我很失望。不是装的。", "I'm disappointed. And no, I'm not pretending."),
                        L10n.T("别再送我这种东西了，我不是垃圾桶。", "Don't bring me things like this again. I'm not a trash bin."),
                        
                        // 愤怒反应
                        L10n.T("这是什么破烂？你在侮辱我的专业吗？", "What is this junk? Are you trying to insult my profession?"),
                        L10n.T("你在拿我寻开心？我可没空陪你胡闹。", "Are you making a joke out of me? I don't have time for your nonsense."),
                        L10n.T("作为护士，我拒绝把这种东西收进医疗站。", "As a nurse, I refuse to let something like this into my clinic."),
                        L10n.T("现在我一点都不想理你。", "Right now, I don't want to talk to you at all."),
                        L10n.T("你下次再送这个，我就先给你把治疗费翻倍。", "If you bring me this again, I'm doubling your treatment fee first."),
                        
                        // 失望反应
                        L10n.T("我原本还有点期待的...算了。", "I actually had some expectations... forget it."),
                        L10n.T("原来在你眼里，我只配收到这种东西。", "So this is the kind of thing you think suits me."),
                        L10n.T("这种玩意儿，J-Lab的垃圾区都未必会留。", "Even the trash pits in J-Lab might not have bothered keeping something like this."),
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
                if (_normalBubbles == null)
                {
                    _normalBubbles = new string[]
                    {
                        // 傲娇接受
                        L10n.T("哼，勉为其难收下吧。", "Hmph. I'll accept it, reluctantly."),
                        L10n.T("不、不是我想要，是你非要塞给我的。", "I-it's not that I wanted it. You insisted on giving it to me."),
                        L10n.T("算你有心了...虽然也就那样。", "At least you put some thought into it... even if it's just alright."),
                        L10n.T("收下了，别指望我反应太大。", "I'll take it. Don't expect a big reaction."),
                        L10n.T("嗯...还行吧，至少不算敷衍。", "Mm... it's alright. At least it doesn't feel careless."),
                        L10n.T("你不用特意送我东西的...不过既然送了，我就收下。", "You didn't have to bring me anything... but since you did, I'll accept it."),
                        
                        // 平淡接受
                        L10n.T("谢谢...我会收好的。", "Thanks... I'll keep it safely."),
                        L10n.T("嗯，我知道了。", "Mm. Got it."),
                        L10n.T("这个说不定能派上点用场。", "This might actually be useful for something."),
                        L10n.T("收到了，谢谢你。", "Received. Thank you."),
                        L10n.T("还不错，至少看得出你不是随手乱拿的。", "Not bad. At least it doesn't look like you grabbed it at random."),
                        L10n.T("嗯？这个我以前倒是没见过。", "Hm? I haven't seen this one before."),
                        
                        // 好奇反应
                        L10n.T("有意思...这个东西原本是拿来做什么的？", "Interesting... what was this originally meant for?"),
                        L10n.T("让我看看它有没有药用价值。", "Let me see whether it has any medicinal value."),
                        L10n.T("作为护士，我对各种材料都挺有兴趣。", "As a nurse, I tend to be interested in all sorts of materials."),
                        L10n.T("这个...也许能做成药膏或者敷料。", "This... might be turned into an ointment or some kind of dressing."),
                        L10n.T("至少比战场上随手捡来的东西体面得多。", "At least it's far more decent than things scavenged off a battlefield."),
                        L10n.T("在J-Lab的时候，从来没人会这样给我东西。", "Back in J-Lab, no one ever handed me things like this."),
                        
                        // 背景相关
                        L10n.T("你...为什么总是对我这么好？...算了，你别回答。", "Why... are you always this kind to me? ...Forget it, don't answer that."),
                        L10n.T("我不太习惯收礼物，但谢谢你的心意。", "I'm not very used to receiving gifts, but thank you for the thought."),
                        L10n.T("咳...又有新东西了。我是说，还不错。", "Ahem... something new again. I mean, it's not bad."),
                        L10n.T("我的柜子里又能多放一样东西了。", "Looks like I'll be making room for one more thing in my cabinet."),
                        L10n.T("我会把它放在安全的地方。", "I'll keep it somewhere safe."),
                        L10n.T("你还记得来看我...我有一点高兴。真的只有一点。", "You remembered to come see me... and that makes me a little happy. Just a little."),
                        
                        // 可爱反应
                        L10n.T("鸭科夫的日子，有你在的时候好像没那么难熬。", "Days in Duckov don't feel quite so hard when you're around."),
                        L10n.T("嗯，这份心意我记住了。", "Mm. I'll remember this kindness."),
                        L10n.T("下次来可以再挑仔细一点...我、我是开玩笑的。", "Next time, you can choose even more carefully... I-I'm joking."),
                        L10n.T("你送的东西我都会留着。不是因为舍不得扔，只是...习惯。", "I keep the things you give me. Not because I can't bear to throw them away, it's just... become a habit."),
                        L10n.T("做护士虽然辛苦，但收到礼物的时候，心情会好一点。", "Being a nurse is hard, but getting a gift does make the day feel a little lighter."),
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
                    L10n.T("今天这份礼物我已经收好了...还、还挺喜欢的。", "I've already put today's gift away... and I, I liked it quite a bit."),
                    L10n.T("够了，今天再送我就真的要高兴得藏不住了。", "That's enough. If you give me another one today, I really won't be able to hide how happy I am."),
                    L10n.T("我没有一直在看今天那份礼物...最多只是看了几眼。", "I haven't been staring at today's gift all day... I only glanced at it a few times."),
                    L10n.T("今天这份心意我会记很久，一天一次就够了。", "I'll remember today's kindness for a long time. Once a day is enough."),
                    L10n.T("你今天已经把我哄开心了，别得寸进尺。", "You've already succeeded in cheering me up today. Don't push your luck."),
                    L10n.T("礼物我已经放进柜子最里面了...这样才安全。", "I've already put the gift in the back of the cabinet... that's the safest place for it."),
                    L10n.T("收到这么好的东西，我今天大概都会心情不错。", "After receiving something this nice, I'll probably stay in a good mood all day."),
                    L10n.T("在J-Lab的时候，从来没人会这样对我...所以我才会这么在意。", "Back in J-Lab, no one ever treated me like this... that's why it means so much to me."),
                    L10n.T("今天这份礼物已经够让我惦记一整天了。", "Today's gift is enough to stay on my mind for the rest of the day."),
                    L10n.T("明天再带吧，不然我会开始期待得太明显。", "Bring something tomorrow instead, or I might start looking too obviously expectant."),
                    L10n.T("你送的东西让我想起和平一点的日子...这就已经很好了。", "What you gave me reminds me of gentler days... and that's already more than enough."),
                    L10n.T("我已经替它留好位置了，你不用再塞第二份给我。", "I've already cleared a place for it. You don't need to push a second gift into my hands."),
                    L10n.T("今天就到这里吧，别让我显得太好哄。", "Let's stop here for today. Don't make me look too easy to win over."),
                    L10n.T("你今天的眼光很好，这点我承认。", "Your taste was good today. I'll admit that much."),
                    L10n.T("这份心意比很多药都管用，但一天一次就够了。", "This gesture works better than a lot of medicine, but once a day is enough."),
                    L10n.T("你先把明天也平安带过来，比再送一份更重要。", "What matters more than a second gift is that you make it back safely tomorrow too."),
                    L10n.T("我会把今天这份礼物好好收着，连同你的心意一起。", "I'll keep today's gift carefully, along with the thought behind it."),
                    L10n.T("我今天已经很开心了，剩下的留到明天。", "I'm already happy enough today. Save the rest for tomorrow."),
                    L10n.T("你今天要是再送，我可能真的会舍不得让你走。", "If you give me another gift today, I might really have trouble letting you leave."),
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
                    L10n.T("我知道你未必是故意的，但我今天确实笑不出来。", "I know you may not have meant it, but I really can't smile about it today."),
                    L10n.T("想道歉就拿出点像样的诚意。", "If you want to apologize, bring something that actually feels sincere."),
                    L10n.T("现在先别提礼物，我需要冷静一下。", "Don't bring up gifts right now. I need to calm down first."),
                    L10n.T("你今天就老实一点，别再挑战我的耐心。", "Behave yourself for the rest of the day and stop testing my patience."),
                    L10n.T("再送下去，我怕自己会说更难听的话。", "If you keep going, I'm afraid I'll start saying things even harsher than this."),
                    L10n.T("我在J-Lab都没这么烦过，别逼我回想那些东西。", "Even in J-Lab I wasn't this irritated. Don't push me into remembering that kind of disgust."),
                    L10n.T("今天到这里，别让我把火气迁到治疗费上。", "That's enough for today. Don't make me take this irritation out on your treatment bill."),
                    L10n.T("你要是真在意我，就别再拿这种东西来试我。", "If you really care about me, don't test me with things like that again."),
                    L10n.T("别想靠礼物蒙混过关，问题还摆在这儿。", "Don't think a new gift will smooth this over. The problem is still right here."),
                    L10n.T("我不是不能原谅，只是今天还不想。", "It's not that I can't forgive you. I just don't want to today."),
                    L10n.T("先让我把气消了，明天再说。", "Let me cool down first. We can talk again tomorrow."),
                    L10n.T("你现在最该学会的，是怎么叫“用心”。", "What you need to learn right now is what it means to be thoughtful."),
                    L10n.T("走吧，明天带着更好的东西，或者更好的态度来。", "Go on. Tomorrow, come back with either something better, or a better attitude.")
                };
            }
            else
            {
                return new string[]
                {
                    L10n.T("今天已经送过了，明天再来吧。", "You already gave me something today. Come back tomorrow."),
                    L10n.T("一天一次就够了，我又不是在清点战利品。", "Once a day is enough. I'm not cataloguing battlefield spoils here."),
                    L10n.T("嗯，今天这份我已经收到了。", "Mm. I've already received today's gift."),
                    L10n.T("今天的礼物还不错，明天可以再认真一点。", "Today's gift was decent. Tomorrow, you can put in a little more thought."),
                    L10n.T("别送太多，我的柜子也是有极限的。", "Don't bring too much. My cabinet does have limits."),
                    L10n.T("今天已经收过了。我知道你来不只是为了送东西。", "I've already accepted something today. I know you didn't come just to give me gifts."),
                    L10n.T("你今天来得够勤，但礼物一天一份就行。", "You've been diligent enough today, but one gift per day is enough."),
                    L10n.T("好了，今天的心意我收到了。", "Alright. I've received your thoughtfulness for today."),
                    L10n.T("你天天送，我会不自在的...虽然也不是不高兴。", "If you bring me something every day, I'll start feeling awkward... not unhappy, just awkward."),
                    L10n.T("今天就不用再送了，陪我说会儿话吧。", "No need to give me anything else today. Just stay and talk with me for a while."),
                    L10n.T("安安静静待一会儿，也比多塞一份礼物强。", "Sitting quietly together for a while is better than forcing a second gift on me."),
                    L10n.T("已经收过了，钱留着，别哪天受伤了又说没预算。", "I've already gotten one. Save your money, so you don't show up injured one day and tell me you're broke."),
                    L10n.T("一天一次刚刚好，再多就显得太刻意了。", "Once a day is just right. Any more than that starts to feel forced."),
                    L10n.T("你比那些只会来治伤的人有心，这点我知道。", "You're more thoughtful than the people who only ever come here for treatment. I do know that."),
                    L10n.T("行了，你的好意我心领了。", "That's enough. I appreciate the thought."),
                    L10n.T("今天这份已经够了，再多我反而不知道该摆在哪儿。", "Today's gift is enough. More than that, and I won't even know where to put it."),
                    L10n.T("礼物留到明天吧，今天你人来了就够了。", "Save the next gift for tomorrow. Today, the fact that you came is enough."),
                    L10n.T("我已经把今天收到的东西整理好了，别让我再重摆第二遍。", "I've already put away what I received today. Don't make me reorganize everything a second time."),
                    L10n.T("嗯，今天的额度用完了。把你的好意留到下次。", "Mm. You've used up today's quota. Save your kindness for next time."),
                    L10n.T("比起再送一份，我更想知道你今天有没有好好照顾自己。", "Instead of another gift, I'd rather know whether you've been taking proper care of yourself today.")
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
                        L10n.T("今天平安回来了吗？...嗯，那我就放心了。", "You made it back safely today? ...Good. Then I can relax."),
                        L10n.T("医疗站一直很吵，但你来时，我反而能静下来。", "The clinic is always noisy, but when you arrive, I finally feel calm."),
                        L10n.T("要聊天也行，要安静待着也行。你在这里，我就不嫌麻烦。", "We can talk, or we can sit in silence. As long as you're here, I don't mind."),
                    });
                case "gift_positive_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("又给我准备了礼物？你这样会让我越来越舍不得放你走。", "Another gift for me? At this rate, I won't want to let you go."),
                        L10n.T("我会收好它，就像收好你每次来见我的心意。", "I'll keep it safe, just like I keep every bit of care you bring me."),
                        L10n.T("你挑得很认真。我一眼就看出来了。", "You chose this carefully. I could tell at a glance."),
                        L10n.T("谢谢。你总能把这一天变得没那么累。", "Thank you. You always make the day feel less exhausting."),
                    });
                case "gift_normal_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("我收下了。只要是你带来的，我都会认真对待。", "I'll take it. If it comes from you, I'll treat it seriously."),
                        L10n.T("不用每次都那么费心，但你送来的东西，我都会留下。", "You don't have to fuss every time, but I keep what you bring me."),
                        L10n.T("嗯，我喜欢这种被你惦记着的感觉。", "Mm. I like the feeling of being on your mind."),
                        L10n.T("礼物倒在其次，你愿意来见我才更重要。", "The gift is secondary. What matters more is that you came to see me."),
                    });
                case "gift_negative_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("这份礼物不太合适...不过是你送的，我会好好说。", "This gift isn't quite right... but since it's from you, I'll say it gently."),
                        L10n.T("下次别这么敷衍，我会担心你是不是太累了。", "Don't be this careless next time. I'll worry you've been pushing yourself too hard."),
                        L10n.T("我不喜欢这个，但我更不想让你误会我在生你的气。", "I don't like this, but I don't want you thinking I'm angry with you."),
                    });
                case "gift_already_positive_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("今天这份心意已经够了，再送我会真的脸红。", "Today's thoughtfulness is enough. Any more and I'll actually blush."),
                        L10n.T("我已经很开心了，明天再继续宠我吧。", "I'm already happy enough. You can spoil me again tomorrow."),
                        L10n.T("先欠着，明天我再收。今天想多陪你一会儿。", "Save it for tomorrow. Today I'd rather keep you with me a little longer."),
                    });
                case "gift_already_normal_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("一天一份就够了，不然我会分不清该先看礼物还是先看你。", "One gift a day is enough, or I won't know whether to look at the gift or at you first."),
                        L10n.T("留到明天吧。今天你来过，我已经记下了。", "Save it for tomorrow. You came today, and that's already enough for me to remember."),
                        L10n.T("别急着把好东西都塞给我，慢慢来。", "Don't rush to hand me everything nice all at once. Take it slowly."),
                    });
                case "gift_already_negative_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("今天先到这里吧。你要是真想补偿我，明天认真一点。", "Let's stop here for today. If you really want to make it up to me, try a little harder tomorrow."),
                        L10n.T("先别继续塞礼物了，我怕你越送越慌。", "Don't keep pushing gifts at me. I'm afraid you'll only get more flustered."),
                    });
                case "heal_success_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("好了，伤口我处理过了。接下来轮到你答应我别再乱来。", "There. I've treated your wounds. Now it's your turn to promise me you won't be reckless again."),
                        L10n.T("恢复得不错。你平安一点，我才能少担心一点。", "You're looking better. The safer you are, the less I have to worry."),
                        L10n.T("治疗结束。别急着走，让我再确认一下你的脸色。", "Treatment's done. Don't rush off—let me check your complexion one more time."),
                    });
                case "heal_full_hp_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("你现在没事，我反而更高兴。能别受伤当然最好。", "You're fine right now, and that actually makes me happier. Better not to be hurt at all."),
                        L10n.T("今天不用治疗。能看到你完完整整地站在这里，我就满足了。", "No treatment today. Seeing you here in one piece is enough for me."),
                        L10n.T("检查过了，没有问题。继续保持，让我省点心。", "I've checked you over—no problems. Keep it that way and spare me some worry."),
                    });
                case "heal_no_money_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("钱不够也别逞强。我会替你留着药，但你得先照顾好自己。", "Even if you're short on money, don't act tough. I'll keep the medicine ready, but you need to take care of yourself first."),
                        L10n.T("这次先去筹钱。我不想看你带着伤硬撑。", "Go scrape together the money this time. I don't want to see you tough it out while injured."),
                        L10n.T("我知道你不想让我为难，所以快去想办法，别让我担心太久。", "I know you don't want to make this hard for me, so go find a way and don't leave me worrying for too long."),
                    });
                case "heal_debuff_only_married":
                    return GetRandomDialogue(new string[]
                    {
                        L10n.T("伤倒是不重，但这些异常状态我看着就不安心。先让我处理掉。", "Your injuries aren't serious, but these abnormal effects still worry me. Let me deal with them first."),
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
                    L10n.T("我不是免费义诊的，别想省钱。", "I'm not running a free clinic, don't try to save money."),
                    L10n.T("你看起来灰头土脸的...真不讲究。", "You look all dusty and dirty... how unkempt."),
                    L10n.T("哈？你来做什么？受伤了还是纯粹来烦我的？", "Huh? What do you want? Are you hurt or just here to bother me?"),
                    L10n.T("鸭科夫这地方真是...空气里都是火药味。", "Duckov is really... the air smells like gunpowder."),
                    L10n.T("我这里不欢迎闲人，有事说事。", "Idlers aren't welcome here, state your business."),
                    L10n.T("你的伤口处理得乱七八糟的...真让人看不下去。", "Your wounds are terribly treated... I can't stand looking at this."),
                    L10n.T("别用那种眼神看我。我只是个护士，仅此而已。", "Don't look at me like that. I'm just a nurse, nothing more."),
                    L10n.T("先把血止住再说，逞强只会给我添工作。", "Stop the bleeding first. Acting tough only gives me more work."),
                    L10n.T("冒险可以，别把命当消耗品。", "Adventure if you want, just don't treat your life like something disposable."),
                    L10n.T("你身上这股火药味...离我的药材远一点。", "That gunpowder smell on you... keep it away from my medicine."),
                    L10n.T("要是想聊天，至少先把鞋底的泥擦干净。", "If you want to chat, at least wipe the mud off your boots first."),
                    L10n.T("我见过太多撑不到第二天的人，所以别跟我说“没事”。", "I've seen too many people who didn't make it to the next day, so don't tell me you're 'fine'."),
                    L10n.T("别皱眉，疼就说疼。我又不会笑你。", "Don't frown. If it hurts, say it hurts. I'm not going to laugh at you."),
                    L10n.T("下次再把绷带缠成这样，我就按教学费收费。", "If you wrap your bandages like that again, I'm charging you for a lesson."),
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
                    L10n.T("那个...我这有多余的绷带，你拿着吧。不、不是特意给你准备的！", "Um... I have extra bandages, take them. I didn't prepare them for you specifically!"),
                    L10n.T("你还活着啊...我是说，你还来啊，真烦。", "You're still alive... I mean, you're here again, so annoying."),
                    L10n.T("今天鸭科夫倒是难得安静...你也少惹点事。", "Duckov is unusually quiet today... you should cause less trouble too."),
                    L10n.T("别老受伤了，药材很贵的你知道吗？...啊我不是在担心你啦！", "Stop getting hurt, do you know how expensive medicine is? ...I'm not worried about you!"),
                    L10n.T("你比其他冒险者稍微靠谱那么一点点。就一点点。", "You're slightly more reliable than other adventurers. Just slightly."),
                    L10n.T("嗯？你要陪我聊天？...随便你吧，反正我也没多忙。", "Hm? You want to chat with me? ...Suit yourself, I'm not too busy anyway."),
                    L10n.T("我之前做了些外伤药膏...你要是需要就拿去。下次小心点。", "I made some wound ointment earlier... take some if you need it. Be more careful next time."),
                    L10n.T("今天伤得比上次轻，看来你终于学会躲一点了。", "You're hurt less badly than last time. Looks like you've finally learned to dodge a little."),
                    L10n.T("我给你留了点止血粉...别告诉别人。", "I set aside some coagulant powder for you... don't tell anyone."),
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
                    L10n.T("你每次过来，我就...松一口气。别误会，只是少了个麻烦患者。", "Every time you come by, I feel... relieved. Don't misunderstand, just one less troublesome patient."),
                    L10n.T("今天的风很舒服呢...偶尔停下来休息也好。", "The breeze is nice today... it's okay to rest once in a while."),
                    L10n.T("我...我以前在J-Lab的时候，从来没有人像你这样关心过我。", "I... when I was at J-Lab, no one ever cared about me like you do."),
                    L10n.T("你想听我的故事吗？...也没什么好说的就是了。", "Want to hear my story? ...Not that there's much to tell."),
                    L10n.T("谢谢你...经常来看我。这里虽然危险，但有你在就觉得安心了一些。", "Thank you... for visiting me often. It's dangerous here, but I feel safer with you around."),
                    L10n.T("我给你泡了杯药草茶，对身体好的。快喝了。", "I made you some herbal tea, it's good for your health. Drink up."),
                    L10n.T("你知道吗，鸭科夫的夕阳很美。有时候我会在这里看很久...", "You know, the sunset in Duckov is beautiful. Sometimes I watch it for a long time..."),
                    L10n.T("如果...如果有一天你不来了，我大概会有点不习惯吧。", "If... if you stopped coming one day, I'd probably feel a bit lost."),
                    L10n.T("在J-Lab的时候，我曾以为自己再也不会对谁敞开心扉了...", "When I was at J-Lab, I thought I'd never open up to anyone again..."),
                    L10n.T("你每次站在门口，我都会先看你有没有缺胳膊少腿。", "Every time you appear in the doorway, the first thing I do is check whether you're still in one piece."),
                    L10n.T("有些话我以前谁都不想说，但对你，好像没那么难。", "There are things I never wanted to say to anyone before, but with you... it doesn't feel so hard."),
                    L10n.T("如果你累了，就在这儿坐着，我不赶你。", "If you're tired, just sit here for a while. I'm not chasing you away."),
                    L10n.T("我开始能分清你的脚步声了...别问我为什么。", "I've started recognizing your footsteps... don't ask me why."),
                    L10n.T("你来的时候，这间小小的医疗站就不像避难所了，像家。", "When you come by, this little clinic stops feeling like a shelter and starts feeling like home."),
                    L10n.T("你说话的时候，我会不自觉记很久。", "When you talk, I end up remembering it for a long time without meaning to."),
                    L10n.T("别总说自己运气好，你能活着回来，更多是因为你在努力。", "Stop saying it's just luck. You make it back alive because you're trying hard."),
                    L10n.T("我有时会想，今天该给你准备什么药，才会让你少疼一点。", "Sometimes I wonder what I should prepare for you today so you'd hurt a little less."),
                    L10n.T("谢谢你没有把我当成怪物，只当成羽织。", "Thank you for not treating me like a monster. Just as Yu Zhi."),
                    L10n.T("和你待久了，我好像也学会期待明天了。", "Being around you for long enough has made me start looking forward to tomorrow.")
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
                    L10n.T("下次受伤了第一个来找我，好吗？...不对，最好是不要受伤。", "Come to me first next time you're hurt, okay? ...No wait, best not to get hurt at all."),
                    L10n.T("我把常用的药都按你的习惯重新摆好了...只是顺手而已。", "I've rearranged the common medicine to match your habits... just because it was convenient."),
                    L10n.T("你一靠近，我就闻得出你今天有没有逞强。", "The moment you get close, I can tell whether you've been pushing yourself too hard today."),
                    L10n.T("别让我总担心你，好吗？...至少让我少担心一点。", "Don't make me worry about you all the time, okay? ...At least let me worry a little less."),
                    L10n.T("我知道自己嘴硬，可你每次来，我都是真的开心。", "I know I'm stubborn with words, but every time you come here, I really am happy."),
                    L10n.T("要是外面太冷，就来我这里躲一会儿，我给你热药汤。", "If it's too cold outside, come hide here for a bit. I'll warm some herbal soup for you."),
                    L10n.T("我会记得你喜欢什么，也会记得你怕什么。", "I'll remember what you like, and I'll remember what frightens you too."),
                    L10n.T("有你在的时候，连消毒水的味道都没那么难闻了。", "When you're around, even the smell of disinfectant doesn't feel so harsh."),
                    L10n.T("你要是再晚一点来，我大概真的会去门口看看。", "If you came any later, I probably would have gone to the door to look for you."),
                    L10n.T("别的冒险者来治疗，我会尽责；你来，我会心疼。", "When other adventurers come in, I do my duty. When you come in, I ache for you."),
                    L10n.T("偶尔也让我照顾你，不只是处理伤口那种。", "Let me take care of you sometimes, and not only in the wound-treating way.")
                };
            }
            else if (level <= 9)
            {
                return new string[]
                {
                    L10n.T("我现在最安心的时刻，就是确认你又平安站在我面前。", "The moment I feel safest now is when I see you standing in front of me alive again."),
                    L10n.T("不管发生什么，我都会在这里等你回来。这是我的承诺。", "No matter what happens, I'll be here waiting for you. That's my promise."),
                    L10n.T("你来找我的时候，我连语气都会不自觉软下来。真丢脸。", "Whenever you come to see me, even my voice softens on its own. How embarrassing."),
                    L10n.T("以前我总怕把别人留在心里，现在却怕你离我太远。", "I used to be afraid of keeping anyone in my heart. Now I'm afraid of you being too far away."),
                    L10n.T("如果你再晚一点出现，我大概真的会去门口等你。", "If you took any longer to show up, I'd probably end up waiting for you at the door."),
                    L10n.T("我越来越没办法把你当成普通病人看待了。...这话你就当没听见。", "I'm finding it harder and harder to think of you as just another patient. ...Pretend you didn't hear that."),
                    L10n.T("你受伤的时候，我会比平时更凶一点...因为我会慌。", "When you're hurt, I get harsher than usual... because I panic."),
                    L10n.T("最近我总会先替你多留一份药。只是习惯，不是偏心。", "Lately I keep setting aside extra medicine for you first. It's just a habit, not favoritism."),
                    L10n.T("你一靠近，我就知道你今天有没有好好照顾自己。", "The moment you get close, I can tell whether you've been taking care of yourself today."),
                    L10n.T("有些话我明明想说，到了嘴边却又咽回去了。...真不像我。", "There are things I clearly want to say, but I keep swallowing them back. ...That's not like me."),
                    L10n.T("如果你累了，就在这里坐一会儿。我不问，你也不用逞强。", "If you're tired, sit here for a while. I won't ask questions, and you don't need to act tough."),
                    L10n.T("你总说我在照顾你，其实你也在慢慢把我从过去里拉出来。", "You always say I'm taking care of you, but you've been slowly pulling me out of my past too."),
                    L10n.T("你让我明白了，即使在最黑暗的地方，也还是会有人想靠近光。", "You made me realize that even in the darkest places, people still want to move toward the light."),
                    L10n.T("我有时候会想，如果哪天你不来了，这里会不会突然安静得过分。", "Sometimes I wonder if this place would become unbearably quiet if you stopped coming."),
                    L10n.T("以前我从不去想“以后”，现在却总是不小心想到和你有关的事。", "I used to never think about 'the future,' but now thoughts that include you keep slipping in."),
                    L10n.T("我好像已经记住你的脚步声了。...别笑，这对护士来说很正常。", "I think I've memorized the sound of your footsteps. ...Don't laugh, that's perfectly normal for a nurse."),
                    L10n.T("你站在这里的时候，我会觉得这间医疗站没那么冷。", "When you're standing here, this clinic doesn't feel quite so cold."),
                    L10n.T("如果时光能倒流...我大概会希望更早一点认识你。", "If time could turn back... I'd probably wish I'd met you sooner."),
                    L10n.T("你不用每次都带伤来，我也会想见到你。...啧，我刚刚什么都没说。", "You don't have to show up injured every time for me to want to see you. ...Tch, I didn't say anything."),
                    L10n.T("有你在，我就没那么害怕那些旧伤和旧梦了。", "With you around, I'm less afraid of those old wounds and old nightmares.")
                };
            }
            else // level 10
            {
                return new string[]
                {
                    L10n.T("你还愿意像以前一样来找我...我真的很高兴。", "The fact that you still come to see me like always... it really makes me happy."),
                    L10n.T("现在你一走进来，我第一个想到的已经不是“病人来了”。", "Now when you walk in, my first thought is no longer 'a patient is here.'"),
                    L10n.T("不管你有没有受伤，能见到你，我这一天就会好过很多。", "Whether you're hurt or not, seeing you makes my whole day feel lighter."),
                    L10n.T("外面再乱也没关系，只要你回来，我就觉得这里还是安稳的。", "No matter how chaotic it is outside, when you come back, this place still feels safe."),
                    L10n.T("累了就靠近一点吧。我这里有热药汤，也有位置留给你。", "If you're tired, come a little closer. I've got warm herbal soup, and a place saved for you."),
                    L10n.T("我会继续给你留灯，也会继续替你备药。这个习惯，我不打算改。", "I'll keep a light on for you, and I'll keep setting medicine aside. That's one habit I don't plan to change."),
                    L10n.T("以前我觉得自己只配站在灯下替别人止血。现在我也想替自己争一个“以后”。", "I used to think all I deserved was to stand under a lamp and stop other people's bleeding. Now I want to claim a future for myself too."),
                    L10n.T("你总说我救了你，可如果没有你，我大概还困在J-Lab留下的影子里。", "You always say I saved you, but without you, I'd probably still be trapped in the shadow J-Lab left behind."),
                    L10n.T("你在的时候，连消毒水的味道都像是安静下来了。", "When you're here, even the smell of disinfectant seems to soften."),
                    L10n.T("我想和你一起看很多次日落，而不是每次都在门口匆匆告别。", "I want to watch many sunsets with you, instead of saying hurried goodbyes at the doorway every time."),
                    L10n.T("哪怕外面一直是战场，只要你回来，我这里就是春天。", "Even if the world outside stays a battlefield, when you come back, it becomes spring here."),
                    L10n.T("现在的我，已经会在整理药柜的时候顺便想你今天会不会来。", "These days, when I organize the medicine cabinet, I catch myself wondering whether you'll come by."),
                    L10n.T("如果你不想说话，也没关系。陪我安静待一会儿就很好。", "If you don't feel like talking, that's okay. Just staying quietly with me is more than enough."),
                    L10n.T("我已经开始把“以后”这种词，认真地放进和你有关的想象里了。", "I've started placing words like 'the future' seriously into the thoughts I have that include you."),
                    L10n.T("你不用总表现得很坚强，在我这里，软弱一点也没关系。", "You don't have to act strong all the time. Here with me, it's okay to be a little vulnerable."),
                    L10n.T("我的医术能止住伤口的疼，可你让我没那么害怕疼了。...啧，好肉麻。", "My skills can ease the pain of wounds, but you've made me less afraid of pain itself. ...Tch, that sounded way too cheesy."),
                    L10n.T("谢谢你出现在我的生命里。认真的。", "Thank you for appearing in my life. I mean it."),
                    L10n.T("以后...你也继续来找我，好不好？我会一直在这里等你。", "From now on... keep coming back to me, okay? I'll always be here waiting for you."),
                    L10n.T("要是你愿意，以后别只把这里当医疗站，也把它当成能回来的地方。", "If you'd like, don't think of this place as only a clinic anymore. Think of it as somewhere you can come home to."),
                    L10n.T("有你在，我就敢相信自己也值得被好好对待一次。", "With you here, I can believe that maybe I deserve to be treated gently too.")
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
                    L10n.T("好了，都处理好了。出去之后小心点。", "All done. Be careful once you're back out there."),
                    L10n.T("治好了。别再这么莽撞了，知道吗？", "Healed. Don't be so reckless again, okay?"),
                    L10n.T("伤口已经处理好了。别等恶化了才想起过来。", "Your wounds are treated. Don't wait until they get worse before coming to me."),
                    L10n.T("嗯，恢复得不错。记得按时换药，别偷懒。", "Mm, you're recovering well. Change your dressings on time, and don't slack off."),
                    L10n.T("治疗完成。你可真会给我添工作。", "Treatment complete. You really do know how to create work for me."),
                    L10n.T("血止住了，呼吸也稳了。现在别乱跑。", "The bleeding's stopped and your breathing's steady. Don't go running around now."),
                    L10n.T("所有的负面状态都清干净了。你之前到底碰了什么鬼东西...", "All the debuffs are cleared. What in the world did you get yourself into...?"),
                    L10n.T("处理好了。真撑不住的时候，要第一时间来找我。", "You're patched up. If you really can't hold out, come find me first."),
                    L10n.T("别皱眉，只是包扎而已，还不至于把你送上手术台。", "Stop frowning. It's just bandaging, not enough to put you on an operating table."),
                    L10n.T("身体不是拿来硬扛的。下次记得早点撤。", "Your body isn't meant to be used for stubborn endurance. Next time, pull back sooner."),
                    L10n.T("交给我就行。J-Lab留下来的东西，至少医术还算有用。", "Leave it to me. Of all the things J-Lab left behind, at least the medical training is still useful."),
                    L10n.T("嗯，这次伤势不算重。但下次可不一定还有这么走运。", "Mm, your injuries aren't too serious this time. Next time, you may not be this lucky."),
                    L10n.T("好了，又能活蹦乱跳了。前提是你别再自己找伤。", "There, you can move around again. Assuming you don't go looking for new injuries."),
                    L10n.T("账我还是会记的...不过这次算你便宜点。", "I'm still keeping the bill on record... but I'll go easy on you this time."),
                    L10n.T("看到你缓过来，我也能松口气了。...只是职业习惯。", "Seeing you recover lets me breathe easier too. ...That's just professional instinct.")
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
                    L10n.T("你很健康，不需要我的治疗。这样最好。", "You're healthy. You don't need my treatment. That's for the best."),
                    L10n.T("嗯？状态不错。那你来这儿做什么？", "Hm? You're in good shape. So what are you here for?"),
                    L10n.T("检查结果：没有外伤，也没有异常。今天不用治疗。", "Check result: no external injuries, no abnormalities. No treatment needed today."),
                    L10n.T("你身上没什么问题。省得我替你收拾残局。", "There's nothing wrong with you. Saves me from cleaning up another mess."),
                    L10n.T("不用治疗。但既然来了，就先歇一会儿吧。", "No treatment needed. But since you're here, sit down and rest for a bit."),
                    L10n.T("完全没事。看来你今天终于学会小心了。", "You're completely fine. Looks like you've finally learned to be careful today."),
                    L10n.T("满血满状态。作为护士，我很喜欢看到这种结果。", "Full health, no debuffs. As a nurse, this is exactly what I like to see."),
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
            if (_noMoneyDialogues == null)
            {
                _noMoneyDialogues = new string[]
                {
                    L10n.T("治疗费不够。先去赚点钱再来吧，我给你留位置。", "You don't have enough for treatment. Go earn some first, and I'll keep a place for you."),
                    L10n.T("我也想先替你处理，可药材和止血剂都不是凭空来的。", "I'd like to patch you up first too, but medicine and coagulants don't appear out of nowhere."),
                    L10n.T("钱不够的话，就先别硬撑。去赚一点回来。", "If you don't have enough money, don't try to tough it out. Go earn some and come back."),
                    L10n.T("抱歉，我这边也要进药材。攒够了再来，我不会跑。", "Sorry, I need to restock medicine too. Come back when you've saved enough. I'm not going anywhere."),
                    L10n.T("我也要吃饭，也要给药柜补货，不能总给你赊账。", "I need to eat, and I need to restock the medicine cabinet. I can't keep putting it on credit for you."),
                    L10n.T("你的口袋比你的脸色还糟。先去想办法赚钱。", "Your pockets look worse than your complexion. Go figure out how to earn some money first."),
                    L10n.T("如果我一直免费治疗，这个医疗站撑不了多久。", "If I kept treating people for free, this clinic wouldn't survive very long."),
                    L10n.T("没钱就先别逞强。等攒够了再来，我会在这儿。", "If you're broke, don't act tough. Come back when you've got enough. I'll be here."),
                    L10n.T("身为护士，我得先把账算清。快去赚钱吧。", "As a nurse, I still need to keep the books straight. Go earn some money."),
                    L10n.T("唉...我是真想帮你，但这批药材是阿稳冒着风险送来的，不能白耗。", "Sigh... I do want to help, but Awen risked a lot to deliver this batch of medicine. I can't just burn through it for nothing.")
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
                    L10n.T("身上有负面状态，让我先替你处理一下。", "You've got some negative effects on you. Let me deal with those first."),
                    L10n.T("血量倒是没掉多少，但这些异常状态更麻烦。", "You haven't lost much health, but these abnormal effects are more troublesome."),
                    L10n.T("没受伤不代表没事，这些debuff不清掉迟早要出问题。", "Not being wounded doesn't mean you're fine. If those debuffs stay on you, they'll cause trouble sooner or later."),
                    L10n.T("这状态不太对劲...你又碰了什么危险东西？", "That condition doesn't look right... what dangerous thing did you touch this time?"),
                    L10n.T("先站稳，我帮你把这些异常反应压下去。", "Hold still. I'll suppress these abnormal reactions for you."),
                    L10n.T("你现在这样去战斗，和把自己往坑里送没区别。", "Going back into a fight like this is no different from throwing yourself into a pit."),
                    L10n.T("别嘴硬，这些状态看着不重，拖久了会更难处理。", "Don't act tough. These effects may not look severe, but they'll get harder to deal with if you drag this out."),
                    L10n.T("呼吸放慢一点，我先把这些乱七八糟的影响清掉。", "Slow your breathing. I'll clear out these messy effects first."),
                    L10n.T("你运气不错，还没发展成更糟的样子。现在处理还来得及。", "You're lucky it hasn't turned into something worse yet. There's still time to deal with it now."),
                    L10n.T("这些异常状态我能处理，但你最好别再把自己弄成这样第二次。", "I can handle these abnormal effects, but you'd better not let yourself end up like this again.")
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
                        L10n.T("唉，羽毛又乱了。战场上保持体面真的好难...", "Sigh, my feathers are messy again. It's hard to stay presentable on a battlefield..."),
                        L10n.T("J-Lab的事...不能再想了。这里才是我的现在。", "J-Lab stuff... I need to stop thinking about it. Here is my present."),
                        L10n.T("今天有冒险者会来吗...希望不要太严重的伤势。", "Will any adventurers come today... hopefully no serious injuries."),
                        L10n.T("这个药方的配比好像可以优化一下...", "The ratio of this prescription could probably be optimized..."),
                        L10n.T("偶尔也想像普通的鸭子一样...安安静静地生活。", "Sometimes I wish I could live quietly... like an ordinary duck."),
                        L10n.T("阿稳上次带来的药草品质不错，得表扬他一下。", "The herbs Awen brought last time were good quality, should compliment him."),
                        L10n.T("如果没有战争就好了...大家都不用受伤。", "If only there were no war... no one would have to get hurt."),
                        L10n.T("嗯...今天的天气很适合晾药材呢。", "Hmm... today's weather is perfect for drying herbs."),
                        L10n.T("作为护士，我能做的就是让每一个来找我的人...都能好好活下去。", "As a nurse, all I can do is make sure everyone who comes to me... can live on."),
                        L10n.T("那个之前来治疗的冒险者...后来再也没出现过...希望他平安。", "That adventurer who came for treatment before... never showed up again... hope they're safe."),
                        L10n.T("即使在这么危险的地方，花依然会开。...我也一样。", "Even in such a dangerous place, flowers still bloom. ...So do I."),
                        L10n.T("纱布、药酒、镇痛剂...嗯，都齐了。希望今天没人伤得太重。", "Gauze, alcohol, painkillers... mm, all stocked. Hopefully nobody comes in too badly hurt today."),
                        L10n.T("那个人今天还没来...算了，我才不是在等谁。", "That person hasn't come by today yet... forget it, it's not like I'm waiting for anyone."),
                        L10n.T("灯再亮一点吧，万一有人半夜来求医，至少能看清他的脸。", "Maybe I should keep the lamp a little brighter. If someone comes for help in the middle of the night, at least I'll be able to see their face."),
                        L10n.T("有时候能治的不是药，是有人愿意活下去的念头。", "Sometimes it isn't medicine that heals people, but their desire to keep living."),
                        L10n.T("等忙完这一阵子...或许我也该试着为自己留一点时间。", "Once things calm down... maybe I should try leaving a little time for myself too.")
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
