// ============================================================================
// CollectiblesLocalization.cs - 收集品与收藏册的物品名、描述（DisplayNameRaw 注入，AGENTS.md §4.4）
// ============================================================================
// 描述只写见闻，不讲机制（owner：游玩里不要有 AI 感）。语言在取用时解析，切语言时随
// InjectLocalization_Extra_Integration() 重注入。条目顺序与 CollectibleCatalog 一致，按 Key 对应。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class CollectiblesLocalization
    {
        private sealed class Text
        {
            internal readonly string Key, NameCN, NameEN, DescCN, DescEN;
            internal Text(string key, string nameCN, string nameEN, string descCN, string descEN)
            {
                Key = key; NameCN = nameCN; NameEN = nameEN; DescCN = descCN; DescEN = descEN;
            }
        }

        internal const string BookLocKey = "BossRush_CollectionBook";
        internal const string BookNameCN = "收藏册";
        internal const string BookNameEN = "Collector's Album";
        internal const string BookDescCN = "一本厚厚的册子，每一页都留好了位置。登进去的东西，就归册子了。";
        internal const string BookDescEN = "A thick album with a spot saved on every page. Whatever goes in belongs to the album.";

        private static readonly Text[] Texts =
        {
            new Text("gz_missing_poster", "褪色的寻鸭启事", "Faded Missing-Duck Poster",
                "从电线杆上撕下来的。照片上那只鸭子笑得很开心，联系电话被雨泡没了。",
                "Torn off a utility pole. The duck in the photo is grinning; the phone number washed away in the rain."),
            new Text("gz_bus_pass", "零号区公交月票", "Ground Zero Bus Pass",
                "这一站的车早就不停了，月票还剩二十天没用完。",
                "Buses stopped coming to this stop long ago. The pass still had twenty days left on it."),
            new Text("gz_gas_card", "加油站积分金卡", "Gas Station Gold Card",
                "积分攒到能换一台电饭煲的那天，加油站关门了。",
                "The day the points finally covered a rice cooker, the gas station shut down."),
            new Text("gz_scout_whistle", "童子军口哨", "Scout Whistle",
                "已经吹不响了，挂绳上绣着谁的名字缩写。",
                "It won't make a sound anymore. Someone's initials are stitched into the cord."),
            new Text("gz_waddle_medal", "摇摆竞走铜牌", "Waddle Race Bronze Medal",
                "社区运动会三等奖。项目一栏写着「摇摆竞走」。",
                "Third place at the community games. The event is listed as \"waddle racing.\""),
            new Text("gz_shortcut_map", "手绘近路地图", "Hand-Drawn Shortcut Map",
                "每一条近路都画上了，还标出了哪家的狗最凶。",
                "Every shortcut is drawn in, along with which houses have the meanest dogs."),
            new Text("gz_gilded_doorplate", "镀金门牌「零号」", "Gilded Doorplate \"No. 0\"",
                "整片区的名字，就是照着这块门牌起的。",
                "The whole district was named after this plate."),
            new Text("gz_founders_photo", "第一批居民合影", "Founding Residents' Photo",
                "照片里的鸭子个个站得笔直，后排有一只闭了眼。",
                "Every duck in the photo stands up straight. One in the back row blinked."),
            new Text("wh_clerk_pen", "仓管员的圆珠笔", "Clerk's Ballpoint Pen",
                "笔帽被咬得坑坑洼洼，还能写。",
                "The cap is chewed to bits. It still writes."),
            new Text("wh_container_seal", "集装箱铅封", "Container Seal",
                "号码对不上任何一张货单。",
                "The number doesn't match any manifest."),
            new Text("wh_forklift_keychain", "叉车钥匙扣", "Forklift Keychain",
                "钥匙早丢了，环上只剩一只橡皮小鸭。",
                "The key is long gone. Only the little rubber duck is left on the ring."),
            new Text("wh_stock_clipboard", "盘点夹板", "Stocktake Clipboard",
                "最后一页盘点表只填了一半。",
                "The last inventory sheet is only half filled in."),
            new Text("wh_shelf_plate", "黄铜货架铭牌", "Brass Shelf Plate",
                "「C 区 07 排」。螺丝孔是被撬开的。",
                "\"Zone C, Row 07.\" The screw holes were pried open."),
            new Text("wh_best_clerk_pennant", "年度最佳仓管锦旗", "Clerk of the Year Pennant",
                "锦旗背面有人用铅笔写：明年还是我。",
                "On the back, in pencil: \"Me again next year.\""),
            new Text("wh_antique_weight", "古董秤砣", "Antique Scale Weight",
                "比看上去沉得多，底下刻着一个「准」字。",
                "Far heavier than it looks. The bottom is engraved with the word \"true.\""),
            new Text("wh_smuggled_vase", "走私瓷瓶", "Smuggled Porcelain Vase",
                "藏在一箱螺丝底下。瓶身上画着两只打架的鹅。",
                "Hidden under a crate of screws. Two geese are fighting on its side."),
            new Text("farm_horseshoe", "马蹄铁", "Horseshoe",
                "挂在门上据说能挡霉运。这一块是马自己甩掉的。",
                "Hang it over the door for luck, they say. The horse kicked this one off itself."),
            new Text("farm_straw_duck", "稻草鸭玩偶", "Straw Duck Doll",
                "稻草人旁边总摆一只，给它壮胆。",
                "There's always one next to the scarecrow, to keep its courage up."),
            new Text("farm_pumpkin_ribbon", "最重南瓜绶带", "Heaviest Pumpkin Ribbon",
                "评比第一名。那颗南瓜后来被大家吃了。",
                "First prize. Everyone ate the pumpkin afterward."),
            new Text("farm_kazoo_score", "老吴的卡祖笛乐谱", "Old Wu's Kazoo Score",
                "卡祖笛独奏曲。谱子边上画满了马。",
                "A kazoo solo. The margins are full of little horses."),
            new Text("farm_windmill_model", "风车模型", "Windmill Model",
                "镇上广场那座风车的缩小版，扇叶还能转。",
                "A miniature of the windmill on the town square. The sails still turn."),
            new Text("farm_market_bell", "集市铜铃", "Market Bell",
                "赶集那天一摇，全镇的鸭子都会出门。",
                "Ring it on market day and every duck in town comes out."),
            new Text("farm_pickle_crock", "祖传腌菜坛", "Heirloom Pickle Crock",
                "坛口的封泥上，按着三代人的掌印。",
                "The clay seal on top carries three generations of handprints."),
            new Text("farm_harvest_trophy", "丰收节金穗奖杯", "Harvest Festival Trophy",
                "杯底刻着历年得主，最后一行还空着。",
                "Past winners are engraved on the base. The last line is still blank."),
            new Text("jlab_coffee_mug", "「别放样本」咖啡杯", "\"No Samples\" Coffee Mug",
                "杯子上印着「别把样本放进来」，杯底有一圈干掉的绿色。",
                "Printed on it: \"Do not put samples in here.\" There's a dried green ring at the bottom."),
            new Text("jlab_visitor_badge", "访客胸牌", "Visitor Badge",
                "访客姓名那一栏写着「鸭」。",
                "Under visitor name, it just says \"Duck.\""),
            new Text("jlab_petri_dish", "培养皿标本", "Petri Dish Specimen",
                "封在树脂里的一小团菌落，长成了一只鸭子的样子。",
                "A colony sealed in resin. It grew into the shape of a duck."),
            new Text("jlab_glow_vial", "荧光试剂管", "Glowing Reagent Vial",
                "一直在发光，摇一摇更亮。",
                "It never stops glowing. Shake it and it glows brighter."),
            new Text("jlab_control_jar", "「对照组」标本瓶", "\"Control Group\" Specimen Jar",
                "瓶里泡着一只橡皮鸭，标签上写着「对照组」。",
                "A rubber duck floats inside. The label reads \"control group.\""),
            new Text("jlab_torn_report", "撕掉一半的实验报告", "Half-Torn Lab Report",
                "结论那一页不见了，只剩一句「不要告诉所长」。",
                "The conclusion page is gone. All that's left: \"Don't tell the director.\""),
            new Text("jlab_centrifuge_model", "年会离心机模型", "Centrifuge Desk Model",
                "所里年会发的纪念品，按一下开关就嗡嗡转。",
                "A souvenir from the lab's annual party. Flip the switch and it whirs."),
            new Text("jlab_gold_pen", "首席研究员的金笔", "Lead Researcher's Gold Pen",
                "笔杆上刻着一串公式，最后一个符号被磨平了。",
                "A formula is engraved along the barrel. The last symbol has worn away."),
            new Text("storm_bent_sign", "吹弯的路牌碎片", "Bent Road Sign Shard",
                "上面写着「前方 200 米」，没写前方是什么。",
                "It says \"200 m ahead.\" It doesn't say what."),
            new Text("storm_windproof_lighter", "防风打火机", "Windproof Lighter",
                "里面没油了，开盖那一声还是很好听。",
                "Out of fuel. The click of the lid still sounds great."),
            new Text("storm_glass", "风暴瓶", "Storm Glass",
                "每次起风前，瓶里的晶体都会长成雪花。",
                "Before every storm, the crystals inside grow into snowflakes."),
            new Text("storm_anemometer", "测风杯", "Anemometer Cups",
                "从气象站屋顶拆下来的，三只小杯还在转。",
                "Pulled off the weather station roof. The three little cups still spin."),
            new Text("storm_lightning_rod", "避雷针尖", "Lightning Rod Tip",
                "尖上有一道被雷劈出来的焦痕。",
                "There's a scorch mark where the lightning hit."),
            new Text("storm_weather_log", "气象员日志", "Weather Log",
                "每天只记一行。最后一天写的是「来了」。",
                "One line per day. The last entry reads: \"It's here.\""),
            new Text("storm_eye_photo", "风暴眼照片", "Eye of the Storm Photo",
                "在风暴正中间拍的，那里的天是蓝的。",
                "Taken from the dead center of the storm. The sky there was blue."),
            new Text("storm_barometer", "黄铜气压计", "Brass Barometer",
                "指针停在最低那一格，再也没回去。",
                "The needle sits at the very bottom and never came back up."),
            new Text("z37_eclipse_card", "日蚀观测卡", "Eclipse Viewing Card",
                "透过这片黑胶片看太阳，只剩一个圈。",
                "Look at the sun through the black film and all you see is a ring."),
            new Text("z37_safety_sticker", "「零事故」安全帽贴纸", "\"Zero Accidents\" Hard Hat Sticker",
                "「本季度零事故」，贴纸的边角卷起来了。",
                "\"Zero accidents this quarter.\" The corners are peeling."),
            new Text("z37_petal_specimen", "蚀日花瓣标本", "Eclipse Bloom Petal",
                "夹在书页里压平了，摸上去还是热的。",
                "Pressed flat between pages, and still warm to the touch."),
            new Text("z37_counter", "坏掉的辐射计数器", "Broken Radiation Counter",
                "读数一直在跳，没人知道它在数什么。",
                "The reading keeps jumping. Nobody knows what it's counting."),
            new Text("z37_zone_badge", "第 37 号实验区徽章", "Test Area 37 Badge",
                "一朵被圆环围住的花，下面印着 37。",
                "A flower inside a ring, with 37 printed below."),
            new Text("z37_scorched_eyepiece", "烧灼的望远镜目镜", "Scorched Eyepiece",
                "镜片上烧出一个圈，大小正好是太阳。",
                "A ring is burned into the lens, exactly the size of the sun."),
            new Text("z37_corona_amber", "日冕琥珀", "Corona Amber",
                "琥珀里封着一缕光，关了灯才看得见。",
                "There's a wisp of light trapped inside. You can only see it in the dark."),
            new Text("z37_sun_relief", "残日浮雕", "Waning Sun Relief",
                "一块圆形石雕，一半是太阳，一半是花。",
                "A round stone carving: half sun, half flower."),
            new Text("sky_bronze_windbell", "风铃集铜风铃", "Windchime Market Bell",
                "没有风的时候，它也会轻轻响一下。",
                "Even without wind, it rings softly now and then."),
            new Text("sky_pigeon_band", "信鸽脚环", "Carrier Pigeon Band",
                "环上刻着一个已经回不去的地址。",
                "It's engraved with an address no one can go back to."),
            new Text("sky_greenear_knot", "青穗编结", "Greenear Knot",
                "梯田人家用新收的青穗编的，挂在门口保平安。",
                "Terrace families braid them from the first harvest and hang them by the door for luck."),
            new Text("sky_fortune_tube", "镜水寺签筒", "Mirrorwater Temple Fortune Tube",
                "摇出来的每一支都是「上上」。",
                "Every stick that falls out reads \"great fortune.\""),
            new Text("sky_registry_rubbing", "归航船名册拓片", "Homecoming Registry Rubbing",
                "拓下来的船名，有一半已经认不出了。",
                "Half the ship names on the rubbing can't be read anymore."),
            new Text("sky_starlamp_shard", "星灯灯罩碎片", "Starlamp Shade Shard",
                "拼不回去了，对着光还能看见星图。",
                "It won't fit back together, but hold it to the light and you can see the star chart."),
            new Text("sky_gear_badge", "残星工坊齿轮徽", "Starworks Gear Badge",
                "工坊学徒出师才发，背面刻着师傅的名字。",
                "Given to apprentices on graduation. The master's name is engraved on the back."),
            new Text("sky_bell_model", "钟庭铜钟模型", "Bell Court Model Bell",
                "归航钟的缩小版。敲一下，余音很久才散。",
                "A miniature of the homecoming bell. Strike it and the hum lingers."),
            new Text("tide_bottle_message", "瓶中信", "Message in a Bottle",
                "纸已经泡烂了，只看得清「等我」两个字。",
                "The paper's soaked through. Only \"wait for me\" is still legible."),
            new Text("tide_shanty_record", "渔港号子唱片", "Harbor Shanty Record",
                "唱片划了，放出来只有一句反复的「嘿哟」。",
                "It's scratched. All it plays is one \"heave-ho\" on repeat."),
            new Text("tide_gauge_plate", "潮位站刻度牌", "Tide Gauge Plate",
                "最高那一格的刻线，被人用红漆描过。",
                "Someone traced the highest mark in red paint."),
            new Text("tide_bankbook", "储蓄所铜存折", "Savings Bank Passbook",
                "存折里只有存，没有取。",
                "Deposits only. Not a single withdrawal."),
            new Text("tide_waterworks_award", "第三水厂先进集体奖状", "No. 3 Waterworks Award",
                "合影里站在最边上的，是年轻时的闸伯。",
                "The young duck at the very edge of the photo is Old Zha."),
            new Text("tide_dredger_bell", "挖沙船船铃", "Dredger's Ship Bell",
                "沙老大的手下拿它当饭铃。",
                "The Dredge Boss's crew used it as a dinner bell."),
            new Text("tide_lighthouse_lens", "灯塔透镜碎块", "Lighthouse Lens Fragment",
                "从灯塔大透镜上崩下来的，太阳一照满墙彩虹。",
                "Chipped off the big lighthouse lens. In sunlight it throws rainbows on the wall."),
            new Text("tide_cigar_box", "所长的雪茄盒", "Director's Cigar Box",
                "盒子里没有雪茄，垫着一张潮汐表。",
                "No cigars inside, just a tide table lining the bottom."),
            new Text("ls_enamel_mug", "「安全运行一千天」搪瓷茶缸", "\"1,000 Safe Days\" Enamel Mug",
                "缸底有一圈怎么也洗不掉的茶垢。",
                "There's a tea ring at the bottom that never washes out."),
            new Text("ls_timetable", "站台时刻表", "Platform Timetable",
                "末班车那一行，被人用笔圈了好几圈。",
                "The last-train line has been circled over and over in pen."),
            new Text("ls_signal_flags", "红绿信号旗", "Red and Green Signal Flags",
                "红的那面洗得快发白了。",
                "The red one has faded almost white."),
            new Text("ls_escort_armband", "押运队臂章", "Escort Armband",
                "绣线开了，「押」字只剩一半。",
                "The stitching has come loose. Half the lettering is gone."),
            new Text("ls_train_plate", "「夜行 08 次」列车铭牌", "\"Night Run 08\" Train Plate",
                "铆钉孔里还卡着锈。",
                "There's still rust caught in the rivet holes."),
            new Text("ls_switch_lever", "道岔扳手模型", "Switch Lever Model",
                "司机室里摆着的，扳到哪边全凭心情。",
                "It sat in the driver's cab. Which way it pointed depended on the mood."),
            new Text("ls_headlamp", "车头信号灯", "Locomotive Signal Lamp",
                "一通电就亮，是那种很旧的黄。",
                "Plug it in and it lights up, that old kind of yellow."),
            new Text("ls_brass_punch", "黄铜检票钳", "Brass Ticket Punch",
                "剪出来的孔是一颗星。余响说，这种钳子全线只发过三把。",
                "It punches a star-shaped hole. Yu Xiang says only three were ever issued on the whole line."),
            new Text("arena_cup_stub", "鸭王杯门票存根", "Duck Cup Ticket Stub",
                "看台第 7 排，座位号被汗洇开了。",
                "Stand row 7. The seat number got smudged with sweat."),
            new Text("arena_glowstick", "竞技场荧光棒", "Arena Glow Stick",
                "折过了，还在亮。上面印着一只举拳头的鸭子。",
                "Already snapped, still glowing. A duck raising its fist is printed on it."),
            new Text("arena_ticket_card", "船票打孔纪念卡", "Punched Ticket Card",
                "每打一个孔，就是一趟活着回来。",
                "Every hole punched is one more trip back alive."),
            new Text("arena_scale_rubbing", "龙鳞拓片", "Dragon Scale Rubbing",
                "从龙裔遗族蜕下的鳞上拓的，纹路像一张地图。",
                "Rubbed from a scale the Dragon Descendant shed. The pattern looks like a map."),
            new Text("arena_witch_candle", "幽灵女巫的残烛", "Phantom Witch's Candle Stub",
                "怎么点都点不着，一转身它又亮了。",
                "It never lights when you try. Turn your back and it's burning."),
            new Text("arena_signed_sandbag", "沙暴冠军签名沙袋", "Signed Sandstorm Sandbag",
                "签名是用拳头砸出来的。",
                "The signature was punched in."),
            new Text("arena_hell_counter", "无间炼狱计数牌", "Infinite Hell Counter",
                "翻牌卡在 999，再往上翻就掉了。",
                "The flip counter is stuck at 999. One more flip and it falls off."),
            new Text("arena_belt_buckle", "鸭王杯冠军腰带扣", "Champion Belt Buckle",
                "第一届冠军腰带上掉下来的扣子。",
                "It fell off the very first champion's belt."),
            new Text("arena_soul_orb", "噬魂水晶球", "Soulreaper Crystal Ball",
                "幽灵女巫的水晶球。里面总有一张脸在看你。",
                "The Phantom Witch's crystal ball. There's always a face inside, watching you."),
            new Text("arena_dragon_horn", "焚天龙皇的断角", "Skyburner's Broken Horn",
                "断口还是烫的，握久了手心冒汗。",
                "The break is still hot. Hold it long enough and your palm starts to sweat."),
            new Text("arena_gold_belt", "黑市鸭王杯金腰带", "Black Market Gold Belt",
                "金是真的，宝石是假的，没人敢当面说。",
                "The gold is real, the gems are fake, and nobody dares say so out loud."),
            new Text("pal_awen_badge", "阿稳的旧工牌", "Awen's Old Staff Badge",
                "工号 0001。照片上的阿稳还很年轻，笑得有点僵。",
                "Staff number 0001. Awen looks young in the photo, with a stiff little smile."),
            new Text("pal_daily_first_issue", "日报创刊号", "Daily First Issue",
                "头版标题印错了一个字，后来一直没改。",
                "The front-page headline has a typo that was never fixed."),
            new Text("pal_wish_coin", "许愿台的硬币", "Wishing Fountain Coin",
                "从星愿许愿台里捞出来的，正面被摸得发亮。",
                "Fished out of the Starwish Fountain. The face has been rubbed shiny."),
            new Text("pal_nurse_cap_pin", "羽织的护士帽徽", "Yu Zhi's Nurse Cap Pin",
                "别针有点歪。为了找它，羽织翻遍了整个医务室。",
                "The pin's a little bent. Yu Zhi turned the whole infirmary upside down looking for it."),
            new Text("pal_first_hammer", "叮当的第一把锤子", "Dingdang's First Hammer",
                "锤柄缠了三层布，锤头上全是豁口。",
                "The handle is wrapped in three layers of cloth. The head is covered in nicks."),
            new Text("pal_eggshell_fossil", "遗种巢的蛋壳化石", "Relic Nest Eggshell Fossil",
                "最早那一窝蛋里留下来的一片。",
                "A piece left over from the very first clutch."),
            new Text("pal_chapel_glass", "婚礼教堂的彩窗碎片", "Chapel Stained-Glass Shard",
                "对着光，能看见两只鸭子牵着手。",
                "Hold it to the light and you'll see two ducks holding hands."),
            new Text("pal_jeff_photo", "杰夫的老照片", "Jeff's Old Photo",
                "照片里的杰夫站在一辆卡车前，笑得像换了一个人。",
                "In the photo, Jeff stands in front of a truck, smiling like someone else entirely."),
            new Text("pal_anvil_model", "叮当的祖传铁砧模型", "Dingdang's Heirloom Anvil",
                "照着真铁砧一比十打的，照样能敲。",
                "Forged at one-tenth the size of the real anvil, and it still works."),
            new Text("pal_codex_manuscript", "鸭皇图鉴手稿", "Duckov Codex Manuscript",
                "图鉴第一版的手绘稿，每只 Boss 都画得比本人可爱。",
                "Hand-drawn pages from the Codex's first edition. Every boss looks cuter than in person."),
            new Text("pal_wish_star", "许愿台的许愿星", "Starwish Star",
                "据说许过的愿都存在里面，晃一晃能听见。",
                "They say every wish ever made is kept inside. Shake it and you can hear them."),
            new Text("rare_vault_gold_bar", "储蓄所金库金砖", "Savings Vault Gold Bar",
                "压在沉没金库的最底下，侧面刻着潮闸镇的镇徽。",
                "From the very bottom of the sunken vault. The Tidegate crest is stamped on its side."),
            new Text("rare_gold_rubber_duck", "纯金橡皮鸭", "Solid Gold Rubber Duck",
                "捏一下，会发出很贵的声音。",
                "Squeeze it and it makes a very expensive noise."),
            new Text("rare_qinglan_star_core", "晴岚星核", "Qinglan Star Core",
                "残星工坊最后炼出的一颗，比天上的星还亮。",
                "The last one the Starworks ever forged. Brighter than any star in the sky."),
            new Text("rare_duck_king_scepter", "鸭王权杖", "Duck King's Scepter",
                "和那顶皇冠是一套的，可从没有人见过它们同时出现。",
                "It belongs with that crown, yet no one has ever seen the two together."),
            new Text("rare_dragon_pearl", "龙皇龙珠", "Skyburner's Dragon Pearl",
                "焚天龙皇护了一辈子的珠子，里面的火还没灭。",
                "The pearl the Skyburner guarded all its life. The fire inside hasn't gone out."),
            new Text("rare_first_king_egg", "初代鸭王金蛋", "First Duck King's Golden Egg",
                "传说第一位鸭王就是从这颗蛋里孵出来的。蛋壳是纯金的，里面是空的。",
                "Legend says the first Duck King hatched from this egg. The shell is solid gold. Inside, it's empty."),
        };

        private static Text Find(CollectibleDefinition def)
        {
            if (def == null) return null;
            int index = def.TypeId - CollectibleCatalog.FirstTypeId;
            if (index >= 0 && index < Texts.Length && Texts[index].Key == def.Key) return Texts[index];
            return null;
        }

        internal static string Name(CollectibleDefinition def)
        {
            Text text = Find(def);
            return text == null ? string.Empty : L10n.T(text.NameCN, text.NameEN);
        }

        internal static string Description(CollectibleDefinition def)
        {
            Text text = Find(def);
            return text == null ? string.Empty : L10n.T(text.DescCN, text.DescEN);
        }

        internal static string BookName { get { return L10n.T(BookNameCN, BookNameEN); } }

        /// <summary>注入全部收集品与收藏册的名称、描述（官方描述固定查 DisplayNameRaw + "_Desc"）。</summary>
        internal static void Inject()
        {
            try
            {
                var map = new Dictionary<string, string>();
                for (int i = 0; i < CollectibleCatalog.Count; i++)
                {
                    CollectibleDefinition def = CollectibleCatalog.At(i);
                    Text text = Find(def);
                    if (text == null) continue;
                    map[def.LocKey] = L10n.T(text.NameCN, text.NameEN);
                    map[def.LocKey + "_Desc"] = L10n.T(text.DescCN, text.DescEN);
                }
                map[BookLocKey] = L10n.T(BookNameCN, BookNameEN);
                map[BookLocKey + "_Desc"] = L10n.T(BookDescCN, BookDescEN);
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Collectibles] 本地化注入失败: " + e.Message); }
        }
    }
}
