using System;
using System.Text;

namespace BossRush
{
    /// <summary>
    /// COMPAT：敲响归航钟之后系在码头的「归航船」船员名册——四位归来的船员各写一页，内容随本存档的选择变化
    /// （噬风打没打、折翎和解还是战胜、观星镜校没校、钟守和解还是战胜、种植记录交没交）。
    ///
    /// 读过的页写进本槽手记（`discoveredNotes`，id 前缀 <see cref="NotePrefix"/>），复用见闻数组，不加存档字段。
    /// 这是结局之后的内容：给「敲完钟还有什么可做」一个回答，也让第二个存档选另一条路时读到不一样的话。
    /// 纯逻辑、无 Unity 依赖，隔离回归按分支组合逐页核对。
    /// </summary>
    internal static class SkyIslandCrew
    {
        internal const int Count = 4;
        internal const string NotePrefix = "Crew_";

        internal static string NoteId(int index) { return NotePrefix + (index + 1); }

        /// <summary>手记 id → 第几页；不是名册页返回 -1（剧情服务据此只收登记过的 id）。</summary>
        internal static int IndexOf(string id)
        {
            for (int i = 0; i < Count; i++)
                if (string.Equals(NoteId(i), id, StringComparison.Ordinal)) return i;
            return -1;
        }

        internal static bool Read(SkyIslandStoryData data, int index)
        {
            return data != null && data.discoveredNotes != null && index >= 0 && index < Count &&
                Array.IndexOf(data.discoveredNotes, NoteId(index)) >= 0;
        }

        internal static int ReadCount(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < Count; i++) if (Read(data, i)) count++;
            return count;
        }

        internal static string Name(int index)
        {
            switch (index)
            {
                case 0: return L10n.T("老舵手 · 云舵", "Yunduo, the old helmsman");
                case 1: return L10n.T("邮差 · 青鸾", "Qingluan, the post carrier");
                case 2: return L10n.T("工坊学徒 · 小铆", "Xiaomao, the workshop apprentice");
                case 3: return L10n.T("船医 · 念安", "Nian'an, the ship's doctor");
                default: return L10n.T("归来的船员", "A returning crew member");
            }
        }

        internal static string Intro(SkyIslandStoryData data)
        {
            return L10n.T("归航船系在码头，船头挂着一本名册。四位归来的船员各写了一页。（已读 ",
                "The homecoming boat is tied at the dock with a roster hanging at the bow. Each of the four returning crew has written a page. (Read ") +
                ReadCount(data) + "/" + Count + L10n.T("）", ")");
        }

        /// <summary>第 <paramref name="index"/> 页正文：一段固定的自我介绍 + 随本存档选择变化的几句。</summary>
        internal static string Page(int index, SkyIslandStoryData data)
        {
            if (data == null) data = SkyIslandStoryRules.CreateDefault();
            var text = new StringBuilder();
            switch (index)
            {
                case 0:
                    text.Append(L10n.T("云舵：船是我掌的舵。风灾那年我们在云海底下漂了很久，看不见岸，只听得见风。钟声响起来的时候，我第一个认出了方向。",
                        "Yunduo: I had the helm. The year of the storm we drifted a long time under the cloud sea, no shore in sight, nothing to hear but the wind. When the bell rang, I was the first to know which way was home."));
                    text.Append("\n\n");
                    text.Append(data.StormResolved
                        ? L10n.T("那阵风散了，我在云底下都感觉得到——船身一下子轻了。谢谢你。",
                            "When that wind broke up I felt it even from under the clouds — the hull went light all at once. Thank you.")
                        : L10n.T("可云海上那阵风还没散。下次去栈道，别一个人站在桥心。",
                            "But the wind out on the cloud sea has not broken up yet. Next time you are on the boardwalk, do not stand alone at mid-span."));
                    // 岛上的灯（SkyIslandLights）：十盏都亮了，老舵手在船头数得出来。
                    if (SkyIslandLights.AllLit(data))
                    {
                        text.Append("\n\n");
                        text.Append(L10n.T("回来那一夜，我在船头数过：岛上十盏灯，一盏不少。",
                            "The night we came home I counted from the bow: ten lights on the isles, not one missing."));
                    }
                    break;
                case 1:
                    text.Append(L10n.T("青鸾：我替岛上送了二十年的信。风灾之后，信都寄不出去，只好倒挂在邮亭里等风。",
                        "Qingluan: I carried the islands' letters for twenty years. After the storm nothing could be sent, so the letters hung upside down in the post hut, waiting for a wind."));
                    text.Append("\n\n");
                    if (data.Has(SkyIslandStoryFlag.ZhelingReconciled))
                        text.Append(L10n.T("听说折翎肯坐下来谈了。我把寺里那封回信也带回来了，他接过去的时候，手一直在抖。",
                            "I hear Zheling agreed to sit down and talk. I brought back the reply from the temple too — his hands would not stop shaking when he took it."));
                    else if (data.Has(SkyIslandStoryFlag.ZhelingDefeated))
                        text.Append(L10n.T("路是通了，可寺里没人收信了。我把那封信压在他留下的旧腰牌旁边，风会替我送到的。",
                            "The road is open, but there is no one at the temple to take the letter now. I weighed it down beside the old badge he left; the wind will deliver it for me."));
                    else
                        text.Append(L10n.T("镜水寺那条路还封着吗？写给折翎的那封信，我一直没敢送。",
                            "Is the Mirrorwater Temple road still closed? The letter addressed to Zheling — I never dared deliver it."));
                    if (SkyIslandLights.Lit(data, "Light_S2"))
                    {
                        text.Append("\n\n");
                        text.Append(L10n.T("邮亭里亮了灯，倒挂的信我一封封取下来了——都还朝着家。",
                            "There is a lamp in the post hut now. I took the upside-down letters down one by one — every one still pointing home."));
                    }
                    break;
                case 2:
                    text.Append(L10n.T("小铆：我是工坊的学徒！师傅总说星灯亮了钟庭会回应，我一直不信。",
                        "Xiaomao: I am the workshop apprentice! Master always said the Bell Court answers when the star lamp is lit. I never believed it."));
                    text.Append("\n\n");
                    text.Append(data.Has(SkyIslandStoryFlag.Telescope)
                        ? L10n.T("可观星镜真的对准了——我在船上听见钟庭回应了一声！师傅没骗我。",
                            "But the telescope really is aligned — I heard the Bell Court answer from the boat! Master was not lying.")
                        : L10n.T("星灯是亮了，可瞭台上的观星镜还偏着。你要是有空，替我去校准一下吧？",
                            "The star lamp is lit, but the telescope on the overlook is still off. If you have the time, would you calibrate it for me?"));
                    if (SkyIslandLights.Lit(data, "Light_G"))
                    {
                        text.Append("\n\n");
                        text.Append(L10n.T("工坊的灯是你点的吧？铜环在灯下亮得跟我擦过的一样！",
                            "You lit the workshop lamp, didn't you? The brass rings shine under it just like when I polished them!"));
                    }
                    break;
                case 3:
                    text.Append(L10n.T("念安：我是船医。归航的人身上都有旧伤，最难治的是等人等出来的那种。",
                        "Nian'an: I am the ship's doctor. Everyone who comes home carries old wounds; the hardest to treat is the kind you get from waiting."));
                    text.Append("\n\n");
                    if (data.Has(SkyIslandStoryFlag.BellKeeperReconciled))
                        text.Append(L10n.T("钟守跟我说，这一次钟声不是催人出海，是告诉大家有人在等。我想，这句话比我的药管用。",
                            "The Bell Keeper told me that this time the bell was not sending anyone to sea, but telling everyone someone is waiting. I think those words work better than my medicine."));
                    else if (data.Has(SkyIslandStoryFlag.BellKeeperDefeated))
                        text.Append(L10n.T("守钟装置停下以后，钟守一个人把钟擦了一整夜。我给他手上换了药，他什么也没说。",
                            "After the bell engine stopped, the Bell Keeper polished the bell alone all night. I dressed his hands; he did not say a word."));
                    else
                        text.Append(L10n.T("钟守那边还没松口吗？他不是坏人，只是怕钟一响，又有人回不来。",
                            "Has the Bell Keeper still not given way? He is not a bad man — he is only afraid that if the bell rings, someone else will not come back."));
                    if (SkyIslandLights.Lit(data, "Light_H"))
                    {
                        text.Append("\n\n");
                        text.Append(L10n.T("钟架下那盏灯，守夜的人总算有地方暖手了。",
                            "That lamp under the bell frame — at last the night watch has somewhere to warm their hands."));
                    }
                    if (data.Has(SkyIslandStoryFlag.PlantingDelivered))
                    {
                        text.Append("\n\n");
                        text.Append(L10n.T("晴禾端来的归航菜还温着，船上的人都吃了两碗。",
                            "The homecoming greens Qinghe brought were still warm; everyone on the boat had two bowls."));
                    }
                    break;
                default:
                    text.Append(L10n.T("这一页还是空的。", "This page is still blank."));
                    break;
            }
            return text.ToString();
        }
    }
}
