using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>谜题一步里的一个选项（中英成对）。</summary>
    internal sealed class SkyIslandPuzzleOption
    {
        internal string Cn, En;
        internal string Label { get { return L10n.T(Cn, En); } }
    }

    /// <summary>谜题的一步：提问、三个选项、正确答案，以及答错时的提示（第一次）与点破（再错）。</summary>
    internal sealed class SkyIslandPuzzleStep
    {
        internal string PromptCn, PromptEn, HintCn, HintEn, RevealCn, RevealEn;
        internal SkyIslandPuzzleOption[] Options;
        internal int Answer;
        internal string Prompt { get { return L10n.T(PromptCn, PromptEn); } }
        internal string Hint { get { return L10n.T(HintCn, HintEn); } }
        internal string Reveal { get { return L10n.T(RevealCn, RevealEn); } }
    }

    /// <summary>一道秘境谜题：挂在哪个支线物证点上、解开后记下的旗标、标题、场景与线索、步骤、解开后的一句话。</summary>
    internal sealed class SkyIslandPuzzle
    {
        internal string Key;
        /// <summary>解开后由既有支线动作写下的旗标：与 <see cref="SkyIslandStoryRules.TrySearchAction"/> 同一映射，隔离回归逐条核对。</summary>
        internal SkyIslandStoryFlag Flag;
        internal string TitleCn, TitleEn, IntroCn, IntroEn, SolvedCn, SolvedEn;
        internal SkyIslandPuzzleStep[] Steps;
        internal string Title { get { return L10n.T(TitleCn, TitleEn); } }
        internal string Intro { get { return L10n.T(IntroCn, IntroEn); } }
        internal string Solved { get { return L10n.T(SolvedCn, SolvedEn); } }
    }

    internal enum SkyIslandPuzzleOutcome
    {
        /// <summary>这一步答对，进入下一步。</summary>
        Advanced,
        /// <summary>最后一步答对，谜题解开。</summary>
        Solved,
        /// <summary>这一步第一次答错：给提示，停在原步。</summary>
        Hinted,
        /// <summary>这一步再次答错：直接点破答案，停在原步。</summary>
        Revealed,
        /// <summary>选项越界或谜题不存在。</summary>
        Invalid
    }

    /// <summary>
    /// 本趟出击里各谜题解到第几步、这一步错了几次。只活在会话里，离岛清零；
    /// 解开后的结果是既有支线物证旗标（种植记录 / 旧信 / 航路图 / 观星镜），由剧情服务照旧落盘。
    /// </summary>
    internal sealed class SkyIslandPuzzleState
    {
        private readonly Dictionary<string, int> steps = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> misses = new Dictionary<string, int>(StringComparer.Ordinal);

        internal int StepOf(string key)
        {
            int value;
            return key != null && steps.TryGetValue(key, out value) ? value : 0;
        }

        internal int MissesOf(string key)
        {
            int value;
            return key != null && misses.TryGetValue(key, out value) ? value : 0;
        }

        /// <summary>当前停在第几步（从 0 起）；解开之后停在最后一步，供面板照常取提问与选项。</summary>
        internal int CurrentStep(SkyIslandPuzzle puzzle)
        {
            if (puzzle == null || puzzle.Steps == null || puzzle.Steps.Length == 0) return 0;
            return Math.Min(StepOf(puzzle.Key), puzzle.Steps.Length - 1);
        }

        /// <summary>
        /// 选第 <paramref name="option"/> 个选项。**永远不会把人卡死**：答错只给提示、再错直接点破，停在原步可以一直重选；
        /// 答对才前进。<paramref name="feedback"/> 是这一下该对玩家说的话（提示、点破或解开后的一句）。
        /// </summary>
        internal SkyIslandPuzzleOutcome Choose(SkyIslandPuzzle puzzle, int option, out string feedback)
        {
            feedback = null;
            if (puzzle == null || puzzle.Steps == null || puzzle.Steps.Length == 0) return SkyIslandPuzzleOutcome.Invalid;
            int index = CurrentStep(puzzle);
            SkyIslandPuzzleStep step = puzzle.Steps[index];
            if (step.Options == null || option < 0 || option >= step.Options.Length) return SkyIslandPuzzleOutcome.Invalid;
            if (option == step.Answer)
            {
                misses[puzzle.Key] = 0;
                if (index + 1 >= puzzle.Steps.Length)
                {
                    steps[puzzle.Key] = puzzle.Steps.Length;
                    feedback = puzzle.Solved;
                    return SkyIslandPuzzleOutcome.Solved;
                }
                steps[puzzle.Key] = index + 1;
                feedback = L10n.T("对上了。", "That fits.");
                return SkyIslandPuzzleOutcome.Advanced;
            }
            int missed = MissesOf(puzzle.Key) + 1;
            misses[puzzle.Key] = missed;
            if (missed <= 1)
            {
                feedback = step.Hint;
                return SkyIslandPuzzleOutcome.Hinted;
            }
            feedback = step.Reveal;
            return SkyIslandPuzzleOutcome.Revealed;
        }

        internal bool IsSolved(SkyIslandPuzzle puzzle)
        {
            return puzzle != null && puzzle.Steps != null && StepOf(puzzle.Key) >= puzzle.Steps.Length;
        }

        internal void Clear()
        {
            steps.Clear();
            misses.Clear();
        }
    }

    /// <summary>
    /// COMPAT：四座支路岛的秘境小谜题。以前走到物证点按一下「收录见闻 / 物证」就拿到，四座秘境只剩一次按键；
    /// 现在是一段三步的小谜题，线索就写在谜题正文里（再加上本岛的见闻与信鸽来信），答错先提示、再错直接点破，
    /// **不存在解不开的情况**。解开后仍走原来的剧情动作（`SkyIslandStoryRules` 的 Find* / RepairTelescope），不加旗标、不改存档。
    /// 纯逻辑、无 Unity 依赖，隔离回归把每一道题的每一种选法都走一遍。
    /// </summary>
    internal static class SkyIslandPuzzles
    {
        private static readonly SkyIslandPuzzle[] puzzles = Build();

        internal static SkyIslandPuzzle[] All { get { return puzzles; } }

        internal static SkyIslandPuzzle For(string key)
        {
            for (int i = 0; i < puzzles.Length; i++)
                if (string.Equals(puzzles[i].Key, key, StringComparison.Ordinal)) return puzzles[i];
            return null;
        }

        private static SkyIslandPuzzle[] Build()
        {
            return new[]
            {
                Puzzle("Search_S1", SkyIslandStoryFlag.PlantingRecord, "修补种植记录", "Mend the planting record",
                    "池边的纸页泡烂了，三处菜名糊成一片。页脚还剩一行字：『先种能挡风的，再种离不开水的，最后一畦留给归来的人。』照这个顺序把三格补上。",
                    "The pages by the pool are waterlogged and three crop names have run together. One line at the foot of the page survives: 'First plant what breaks the wind, then what cannot live without water, and keep the last bed for those coming home.' Fill the three gaps in that order.",
                    "三格补齐，纸页上的字又连成了句子。", "With all three beds filled in, the words on the page run together into sentences again.",
                    Step("第一格种什么？", "What goes in the first bed?",
                        new[] { Option("水芹", "Water celery"), Option("归航菜", "Homecoming greens"), Option("青麦", "Green wheat") }, 2,
                        "第一格要挡风——哪一样长得高，能替后面的菜挡住风？", "The first bed breaks the wind — which crop grows tall enough to shelter the rest?",
                        "晴禾的笔迹从纸背透出来：第一格是青麦。", "Qinghe's handwriting shows through from the back of the page: the first bed is green wheat."),
                    Step("第二格种什么？", "What goes in the second bed?",
                        new[] { Option("青麦", "Green wheat"), Option("水芹", "Water celery"), Option("归航菜", "Homecoming greens") }, 1,
                        "第二格离不开水——池边哪一样一离水就蔫？", "The second bed cannot live without water — which crop wilts the moment it leaves the pool?",
                        "纸背透出来：第二格是水芹。", "Through the page: the second bed is water celery."),
                    Step("最后一畦留给什么？", "What goes in the last bed?",
                        new[] { Option("归航菜", "Homecoming greens"), Option("青麦", "Green wheat"), Option("水芹", "Water celery") }, 0,
                        "最后一畦是留给归来的人的。", "The last bed is kept for those coming home.",
                        "纸背透出来：最后一畦是归航菜。", "Through the page: the last bed is homecoming greens.")),
                Puzzle("Search_S2", SkyIslandStoryFlag.OldLetter, "拼回旧信", "Piece the letter back together",
                    "旧信被风撕成了三条，倒挂在邮亭的梁上。按句子本来的顺序把纸条一条条取下来，信才读得通。",
                    "The wind tore the old letter into three strips, hung upside down from the post hut's beam. Take them down one at a time in the order the sentences run, or the letter will not make sense.",
                    "三条纸对上了：『封路那天，我们看见了岸上的灯。请别让它熄灭。』", "The three strips line up: 'On the day the lanes closed, we saw the light on shore. Please do not let it go out.'",
                    Step("先取哪一条？", "Which strip comes first?",
                        Strips(), 1,
                        "信是从一个日子讲起的。", "The letter starts by naming a day.",
                        "纸条背面有编号：先取「封路那天，」。", "The strips are numbered on the back: first comes 'On the day the lanes closed,'."),
                    Step("再取哪一条？", "Which strip comes next?",
                        Strips(), 2,
                        "那一天，他们看见了什么？", "What did they see on that day?",
                        "纸条背面有编号：接着是「我们看见了岸上的灯。」", "The strips are numbered on the back: next comes 'we saw the light on shore.'"),
                    Step("最后是哪一条？", "Which strip comes last?",
                        Strips(), 0,
                        "最后一句是一个请求。", "The last line is a request.",
                        "纸条背面有编号：最后是「请别让它熄灭。」", "The strips are numbered on the back: last comes 'Please do not let it go out.'")),
                Puzzle("Search_S3", SkyIslandStoryFlag.RouteChart, "辨认水声", "Read the streams",
                    "洞里有三道水声：一道急，一道缓，一道断断续续。旧航路图的边角写着：『跟着缓的走；缓流到头往右拐；出洞时贴着断续的那道——急的那道会把船冲下云海。』",
                    "Three streams sound in the cave: one fast, one slow, one that comes and goes. The corner of the old route chart says: 'Follow the slow one; where it ends, turn right; leave the cave beside the one that comes and goes — the fast one sweeps boats off into the cloud sea.'",
                    "三道水声都对上了航路图的标记，避风航道一段不差。", "All three streams match the marks on the chart; the sheltered lane is complete, not a stretch missing.",
                    Step("先跟着哪道水声走？", "Which stream do you follow first?",
                        new[] { Option("急流", "The fast stream"), Option("缓流", "The slow stream"), Option("断断续续的那道", "The one that comes and goes") }, 1,
                        "航路图说，先跟着缓的走。", "The chart says to follow the slow one first.",
                        "图上的虚线贴着缓流画。", "The dotted line on the chart hugs the slow stream."),
                    Step("缓流到头，往哪边走？", "Where the slow stream ends, which way?",
                        new[] { Option("往右", "Right"), Option("直走", "Straight on"), Option("往左", "Left") }, 0,
                        "航路图说，缓流到头往右拐。", "The chart says to turn right where the slow stream ends.",
                        "岩壁上有一道旧刻痕，箭头朝右。", "An old scratch on the rock wall points right."),
                    Step("出洞时贴着哪道水声？", "Which stream do you keep beside you on the way out?",
                        new[] { Option("缓流", "The slow stream"), Option("急流", "The fast stream"), Option("断断续续的那道", "The one that comes and goes") }, 2,
                        "急的那道会把船冲下云海。", "The fast one sweeps boats off into the cloud sea.",
                        "断断续续的水声在洞口停了一下，像是在等你。", "The stream that comes and goes pauses at the mouth of the cave, as if waiting for you.")),
                Puzzle("Search_S4", SkyIslandStoryFlag.Telescope, "校准观星镜", "Calibrate the telescope",
                    "观星镜的镜筒上有三道刻环，铭牌上刻着：『外环指星灯，中环指归航星，内环指归航钟。』星灯在西边的工坊，归航星在正北，归航钟庭在瞭台的西北方。",
                    "The telescope barrel carries three graduated rings, and the brass plate reads: 'Outer ring to the star lamp, middle ring to the homing star, inner ring to the Homecoming Bell.' The star lamp is at the workshop to the west, the homing star stands due north, and the Bell Court lies northwest of the overlook.",
                    "三道刻环都对准了，星图重新连成一片。", "All three rings are aligned and the star chart joins up again.",
                    Step("外环转向哪边？", "Where does the outer ring point?",
                        new[] { Option("东", "East"), Option("西", "West"), Option("北", "North") }, 1,
                        "外环指星灯——星灯在工坊那边。", "The outer ring points to the star lamp, over at the workshop.",
                        "外环停在「西」时，镜片里亮起一点暖光。", "When the outer ring stops at 'west', a warm point of light appears in the lens."),
                    Step("中环转向哪边？", "Where does the middle ring point?",
                        new[] { Option("北", "North"), Option("南", "South"), Option("西北", "Northwest") }, 0,
                        "中环指归航星，它从不离开正北。", "The middle ring points to the homing star, which never leaves due north.",
                        "中环停在「北」时，一颗星稳稳落进十字线。", "When the middle ring stops at 'north', a star settles steady in the crosshairs."),
                    Step("内环转向哪边？", "Where does the inner ring point?",
                        new[] { Option("东南", "Southeast"), Option("正南", "Due south"), Option("西北", "Northwest") }, 2,
                        "内环指归航钟——钟庭在瞭台的西北。", "The inner ring points to the Homecoming Bell, northwest of the overlook.",
                        "内环停在「西北」时，远处的钟庭好像轻轻响了一声。", "When the inner ring stops at 'northwest', the distant Bell Court seems to chime once."))
            };
        }

        /// <summary>旧信的三条纸：三步用同一组，顺序固定（纸条就挂在那儿）。</summary>
        private static SkyIslandPuzzleOption[] Strips()
        {
            return new[]
            {
                Option("「请别让它熄灭。」", "'Please do not let it go out.'"),
                Option("「封路那天，」", "'On the day the lanes closed,'"),
                Option("「我们看见了岸上的灯。」", "'we saw the light on shore.'")
            };
        }

        private static SkyIslandPuzzle Puzzle(string key, SkyIslandStoryFlag flag, string titleCn, string titleEn,
            string introCn, string introEn, string solvedCn, string solvedEn, params SkyIslandPuzzleStep[] steps)
        {
            return new SkyIslandPuzzle
            {
                Key = key, Flag = flag, TitleCn = titleCn, TitleEn = titleEn, IntroCn = introCn, IntroEn = introEn,
                SolvedCn = solvedCn, SolvedEn = solvedEn, Steps = steps
            };
        }

        private static SkyIslandPuzzleStep Step(string promptCn, string promptEn, SkyIslandPuzzleOption[] options, int answer,
            string hintCn, string hintEn, string revealCn, string revealEn)
        {
            return new SkyIslandPuzzleStep
            {
                PromptCn = promptCn, PromptEn = promptEn, Options = options, Answer = answer,
                HintCn = hintCn, HintEn = hintEn, RevealCn = revealCn, RevealEn = revealEn
            };
        }

        private static SkyIslandPuzzleOption Option(string cn, string en)
        {
            return new SkyIslandPuzzleOption { Cn = cn, En = en };
        }
    }
}
