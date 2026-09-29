// 叮当的婚恋场景专属台词（求婚被拒、离婚、结婚气泡、花心、拒绝跟随等），走 INPCRelationshipDialogueConfig，
// 由 NPCDialogueSystem.GetPersonaLine 读取；没有专属版本的 NPC 落回通用文案。
// 主文件受行数预算限制，原样放到这里（根 AGENTS.md 4.15）。
namespace BossRush
{
    public partial class GoblinAffinityConfig
    {
        private string GetPersonaRelationshipDialogue(string eventKey)
        {
            switch (eventKey)
            {
                case "marriage_chapel_required":
                    return L10n.T("要办就办正式的！先盖间教堂，叮当想在有彩灯的地方点头。",
                        "If we're doing this, we do it properly! Build a chapel first. Dingdang wants fairy lights when Dingdang says yes.");
                case "marriage_divorce":
                    return L10n.T("……叮当不哭。叮当本来也哭不出来。",
                        "...Dingdang won't cry. Dingdang can't anyway.");
                case "marriage_bubble_date":
                    return L10n.T("{date}。叮当把这天刻在锤柄上了！",
                        "{date}. Dingdang carved this day into the hammer handle!");
                case "marriage_cheat_first":
                    return L10n.T("你把戒指给别人了？……叮当这次先不咬你，下不为例！",
                        "You gave a ring to someone else? ...Dingdang won't bite you this once. Never again!");
                case "marriage_cheat_repeat":
                    return L10n.T("又来……叮当的脸还在笑，可叮当是真的难过。",
                        "Again... Dingdang's face is still smiling, but Dingdang is really hurt.");
                case "marriage_follow_refused":
                    return L10n.T("今天叮当想自己溜达，下次吧！",
                        "Dingdang wants to wander alone today. Next time!");
                case "ring_reject":
                    return L10n.T("这个……叮当不敢收。再处处看吧。",
                        "This... Dingdang can't take it yet. Let's see how things go.");
                case "ring_cheater":
                    return L10n.T("你不是有家的人吗？这个叮当不能收。",
                        "Aren't you already spoken for? Dingdang can't take this.");
                case "ring_spouse_repeat":
                    return L10n.T("一枚就够了，叮当收着呢。",
                        "One is enough. Dingdang's keeping it safe.");
                default:
                    return null;
            }
        }
    }
}
