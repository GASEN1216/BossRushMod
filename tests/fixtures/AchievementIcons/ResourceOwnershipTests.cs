using System;
using System.IO;
using System.Linq;
using BossRush;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static class ResourceOwnershipTests
{
    private static int assertions;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Item icon: " + message);
        assertions++;
    }

    internal static void Run(string directory)
    {
        ModBehaviour.ModPath = directory;
        Directory.CreateDirectory(Path.Combine(directory,"Assets","Items"));
        string path = "Assets/Items/test.png";
        File.WriteAllBytes(Path.Combine(directory,path),new byte[]{1});
        Sprite.CreationMode=0;
        int before = Texture2D.DecodeCalls;
        Sprite sprite=ItemFactory.GetSpriteFromFile(path);
        Check(sprite != null && !sprite.texture.isReadable,"pixels released after decode");
        Check(ReferenceEquals(sprite,ItemFactory.GetSpriteFromFile(path.Replace('/','\\'))),"separator aliases share cache");
        for (int i=0;i<30;i++) Check(ReferenceEquals(sprite,ItemFactory.GetSpriteFromFile(path)),"repeated item configuration shares icon");
        Check(Texture2D.DecodeCalls==before+1,"one decode for repeated configuration");
        UObject.Destroy(sprite);
        UObject.Destroy(sprite.texture);
        sprite=ItemFactory.GetSpriteFromFile(path);
        Check(sprite!=null,"destroyed cached sprite reloads");
        ItemFactory.Shutdown(); ItemFactory.Shutdown();
        Check(sprite.destroyCalls==1 && sprite.texture.destroyCalls==1,"owned native objects released once");
        foreach (int failure in new[]{0,1,2,3})
        {
            Sprite.CreationMode=failure==2 ? 1 : failure==3 ? 2 : 0;
            File.WriteAllBytes(Path.Combine(directory,path),new byte[]{(byte)(failure==0 ? 0 : failure==1 ? 2 : 1)});
            int objects=UObject.Created.Count;
            Check(ItemFactory.GetSpriteFromFile(path)==null,"decode/create failure returns null");
            Check(UObject.Created.Skip(objects).All(o=>o.destroyed && o.destroyCalls==1),"failure frees every partial allocation");
            objects=UObject.Created.Count;
            Check(RawImageLoader.LoadSprite(Path.Combine(directory,path), "raw") == null, "shared raw fallback failure returns null");
            Check(UObject.Created.Skip(objects).All(o=>o.destroyed && o.destroyCalls==1), "shared raw helper frees every partial allocation");
        }
        Sprite.CreationMode=0;
        ItemFactory.Shutdown();
        File.WriteAllBytes(Path.Combine(directory,"map.png"),new byte[]{1});
        Sprite map=MapThumbnailCache.GetOrLoad(directory,"map.png");
        Check(map!=null && !map.texture.isReadable,"map releases CPU pixels");
        Check(ReferenceEquals(map,MapThumbnailCache.GetOrLoad(directory,"map.png")),"map cache shares sprite");
        UObject.Destroy(map);
        Sprite replacement=MapThumbnailCache.GetOrLoad(directory,"map.png");
        Check(replacement!=null && !ReferenceEquals(map,replacement),"destroyed map cache reloads");
        MapThumbnailCache.ResetStaticCaches();MapThumbnailCache.ResetStaticCaches();
        Check(map.texture.destroyCalls==1 && replacement.texture.destroyCalls==1 && replacement.destroyCalls==1,"map releases textures and sprite exactly once");
        Console.WriteLine("ResourceOwnership: "+assertions+" PASS (production methods, Unity doubles)");
    }
}
