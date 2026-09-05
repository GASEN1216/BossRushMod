using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

public static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++; Console.WriteLine("PASS " + name);
    }
    private static ModBehaviour Reset(bool follow=false)
    {
        PermanentDuckNpcModule.Invalidate(); PermanentDuckNpcRegistry.Instance=null;
        DuckNpcSpawner.Pending.Clear(); AsyncFixture.Tasks.Clear();
        SceneManager.Current=new Scene {handle=1,name="Base"};
        AffinityManager.Spouse="xiaoman"; AffinityManager.Married=true; AffinityManager.Following=follow;
        CharacterMainControl.Main=new CharacterMainControl();
        CharacterMainControl.Main.transform.position=new Vector3(100,0,100);
        var owner=new ModBehaviour(); ModBehaviour.Instance=owner; return owner;
    }
    public static async Task Main()
    {
        var mod=Reset();
        Check(!mod.Cleanup(300.01f,299f,true,false,false).Destroyed,"scan frame keeps inventory item");
        mod=Reset(); Check(!mod.Cleanup(300.01f,299.9f,true,false,false).Destroyed,"between scans keeps inventory item");
        mod=Reset(); Check(!mod.Cleanup(300.01f,299.9f,false,true,false).Destroyed,"between scans keeps equipped item");
        mod=Reset(); Check(!mod.Cleanup(300.01f,299.9f,true,false,true).Destroyed,"forced wave keeps owned item");
        mod=Reset(); Check(mod.Cleanup(300.01f,299.9f,false,false,false).Destroyed,"expired ground item still reclaimed");
        mod=Reset(); var young=mod.Cleanup(1f,299.9f,false,false,false);
        Check(!young.Destroyed && young.Queries==0,"young candidates preserve scan throttling");
        mod=Reset(); Check(!mod.Cleanup(300.01f,299.9f,false,false,false,true).Destroyed,"high value exemption kept");
        mod=Reset(); Check(!mod.Cleanup(300.01f,299.9f,false,false,true,false,true).Destroyed,"boss exemption kept");

        mod=Reset(); mod.Begin(false); mod.Begin(false);
        Check(DuckNpcSpawner.Pending.Count==1 && mod.HasPending && mod.Placeholder,"cold restore deduplicates while pending");
        var resident=new CharacterMainControl(); DuckNpcSpawner.Pending[0].SetResult(resident);
        await Task.WhenAll(AsyncFixture.Tasks);
        Check(resident.gameObject.Marked && resident.gameObject.Idle && resident.gameObject.Refreshes==1 && !mod.Placeholder && !mod.HasPending,
              "cold completion marks resident removes placeholder refreshes interactions");

        mod=Reset(true); mod.Begin(true);
        CharacterMainControl.Main.transform.position=new Vector3(150,0,150);
        var follower=new CharacterMainControl(); DuckNpcSpawner.Pending[0].SetResult(follower);
        await Task.WhenAll(AsyncFixture.Tasks);
        Check(follower.gameObject.Following && follower.transform.position==CharacterMainControl.Main.transform.position && !mod.HasPending,
              "follow restore starts only after spawn and uses current player position");

        foreach (string invalidation in new[] {"same-name new scene", "divorce", "generation", "building removed", "home requested"})
        {
            mod=Reset(); mod.Begin(false);
            var newer=new CharacterMainControl(); PermanentDuckNpcRegistry.Instance=newer;
            if (invalidation=="same-name new scene") SceneManager.Current=new Scene {handle=2,name="Base"};
            else if (invalidation=="divorce") AffinityManager.Spouse="other";
            else if (invalidation=="generation") PermanentDuckNpcModule.Invalidate();
            else if (invalidation=="building removed") mod.Building=false;
            else mod.Invalidate();
            var stale=new CharacterMainControl(); DuckNpcSpawner.Pending[0].SetResult(stale);
            await Task.WhenAll(AsyncFixture.Tasks);
            Check(stale.gameObject.Destroyed && !newer.gameObject.Destroyed && PermanentDuckNpcRegistry.Instance==newer,
                  invalidation + " discards only its late object");
        }

        mod=Reset(); mod.Begin(false); mod.Invalidate(); mod.Begin(false);
        var current=new CharacterMainControl(); DuckNpcSpawner.Pending[1].SetResult(current);
        var old=new CharacterMainControl(); DuckNpcSpawner.Pending[0].SetResult(old);
        await Task.WhenAll(AsyncFixture.Tasks);
        Check(old.gameObject.Destroyed && !current.gameObject.Destroyed && PermanentDuckNpcRegistry.Instance==current && current.gameObject.Marked,
              "old completion cannot overwrite new generation");

        mod=Reset(); resident=new CharacterMainControl(); PermanentDuckNpcRegistry.Instance=resident; mod.Begin(false);
        await Task.WhenAll(AsyncFixture.Tasks);
        Check(DuckNpcSpawner.Pending.Count==0 && resident.gameObject.Marked && !resident.gameObject.Destroyed,
              "existing resident is reused without spawn");
        Console.WriteLine("RuntimeOwnership regression PASS: " + checks + " checks");
    }
}
