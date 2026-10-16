// Native UI presentation is isolated. Both the production waiting button and
// the shared ApplyButtonDepth method are extracted unchanged by run.py.
using System;
namespace Duckov.MiniMaps {}
namespace Duckov.Scenes {}
namespace Duckov.Utilities {}
namespace Cysharp.Threading.Tasks {}
namespace ItemStatsSystem {}
namespace UnityEngine.AI {}
namespace UnityEngine.SceneManagement {}
namespace TMPro { internal enum TextAlignmentOptions {Center} }
namespace UnityEngine
{
    internal struct Vector2 {internal float x,y;internal Vector2(float a,float b){x=a;y=b;}}
    internal struct Color {internal float a;internal Color(float alpha){a=alpha;}}
    internal sealed class Sprite {internal Vector2 border=new Vector2(8,8);}
}
namespace UnityEngine.UI
{
    internal class Graphic {}
    internal sealed class Image:Graphic {internal enum Type {Sliced}internal UnityEngine.Sprite sprite=new UnityEngine.Sprite();internal Type type=Type.Sliced;}
    internal sealed class Button {internal Graphic targetGraphic=new Image();internal Action Click;}
}
namespace BossRush
{
    internal enum BossRushUISkinPart {Button,Card,Panel}
    internal static class BossRushUISkin {internal static UnityEngine.Sprite GetInjected(BossRushUISkinPart part){return null;}}
    internal static class BossRushUIColors {internal static readonly UnityEngine.Color Accent=new UnityEngine.Color(1),AccentFill=new UnityEngine.Color(1),TextPrimary=new UnityEngine.Color(1);}
    internal static partial class BossRushUIDepth
    {
        internal static int Calls;
        private static void ApplyDepth(UnityEngine.UI.Image image,int radius,BossRushUISkinPart part,float alpha){Calls++;}
    }
    internal static class ZombieModeUIHelper
    {
        internal static UnityEngine.UI.Button Last;
        internal static UnityEngine.GameObject CreateModalSurface(string name,UnityEngine.Transform parent,UnityEngine.Vector2 size,UnityEngine.Color accent){return new UnityEngine.GameObject();}
        internal static void CreateText(string name,UnityEngine.Transform parent,string text,int font,UnityEngine.Vector2 a,UnityEngine.Vector2 b,UnityEngine.Vector2 c,UnityEngine.Vector2 d,TMPro.TextAlignmentOptions align,UnityEngine.Color color){}
        internal static UnityEngine.UI.Button CreateButton(string name,UnityEngine.Transform parent,string text,UnityEngine.Vector2 anchor,UnityEngine.Vector2 position,UnityEngine.Vector2 size,UnityEngine.Color color,int font,UnityEngine.Vector2 textSize,Action click,bool enabled){return Last=new UnityEngine.UI.Button {Click=click};}
    }
}
