// 共享 GPU 复制调用替身；只观测资源归属与调用参数，不模拟像素或 GPU 性能。
using System;

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { R8G8B8A8_UNorm }
    public enum TextureCreationFlags { None }
}

namespace UnityEngine.Rendering
{
    public enum CopyTextureSupport { None, Basic }
}

namespace UnityEngine
{
    public enum FilterMode { Point, Bilinear }
    public static class SystemInfo { public static Rendering.CopyTextureSupport copyTextureSupport; }
    public partial class Texture2D
    {
        public Experimental.Rendering.GraphicsFormat graphicsFormat;
        public int mipmapCount = 1, anisoLevel;
        public FilterMode filterMode;
        public Texture2D CopiedFrom;
        public Texture2D(int width, int height, Experimental.Rendering.GraphicsFormat format, int mipCount,
            Experimental.Rendering.TextureCreationFlags flags)
        { this.width = width; this.height = height; graphicsFormat = format; mipmapCount = mipCount; }
    }
    public static class Graphics
    {
        public static bool FailCopy;
        public static Texture2D LastTarget;
        public static void CopyTexture(Texture2D source, Texture2D target)
        {
            LastTarget = target;
            if (FailCopy) throw new InvalidOperationException("GPU copy failed");
            target.CopiedFrom = source;
        }
    }
}
