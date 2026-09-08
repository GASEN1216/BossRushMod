using System.Collections.Generic;

namespace UnityEngine
{
    public class Sprite { }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    }
    public struct Rect
    {
        public float xMin, yMin, xMax, yMax;
        public static Rect MinMaxRect(float x0, float y0, float x1, float y1)
        { return new Rect { xMin = x0, yMin = y0, xMax = x1, yMax = y1 }; }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
}
namespace Duckov.MiniMaps
{
    public class MiniMapSettings
    {
        public class MapEntry { }
        public List<MapEntry> maps;
        public UnityEngine.Sprite combinedSprite;
        public UnityEngine.Vector3 combinedCenter;
        public float combinedSize;
    }
}
