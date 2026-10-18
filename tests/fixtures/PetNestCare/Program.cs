using System;
using System.Linq;
using System.Reflection;
using BossRush;
using ItemStatsSystem;
using Saves;
using UnityEngine;
using Object = UnityEngine.Object;

class Program
{
    static int checks,failures;
    static Inventory Bag{get{return CharacterMainControl.Main.CharacterItem.Inventory;}}
    static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    static void Case(string name,Action body){try{Reset();body();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
    internal static Item Food(int type,int count)
    {
        var go=new GameObject();var item=go.AddComponent<Item>();item.TypeID=type;item.StackCount=count;go.AddComponent<Duckov.ItemUsage.FoodDrink>();item.Variables["original"]="keep";return item;
    }
    static CharacterMainControl Character()
    {
        var go=new GameObject();var character=go.AddComponent<CharacterMainControl>();character.Health=go.AddComponent<Health>();character.Health.Character=character;character.CharacterItem=go.AddComponent<Item>();character.CharacterItem.Inventory=new Inventory();return character;
    }
    static void Reset()
    {
        if(CharacterMainControl.Main!=null)Object.Destroy(CharacterMainControl.Main.gameObject);
        CharacterMainControl.Main=Character();PetNestCompanionRuntime.CompanionCharacter=null;PetNestCompanionRuntime.ThrowRefresh=false;PetNestBaseIdleSpawner.ActiveCount=0;
        PetNestService.Pet=new PetNestPetRecord();PetNestService.Pending=false;PetNestPersistenceAccess.Active=PetNestPersistenceAccess.RejectCommit=false;
        SavesSystem.Cache.Clear();SavesSystem.Disk.Clear();SavesSystem.CurrentSlot=1;SavesSystem.IsSaving=false;SavesSystem.FailAt=SavesSystem.Attempts=0;
        PetNestSaveCoordinator.Reset();BossRushSaveFileThrottle.ResetStaticCaches();Time.frameCount++;LevelManager.Instance.IsBaseLevel=false;L10n.IsChinese=true;
        Duckov.UI.DialogueBubbles.DialogueBubblesManager.Lines.Clear();
    }
    static int FoodCount(){return Bag.Content.Where(x=>x!=null).Sum(x=>x.StackCount);}
    static void Add(Item food){Bag.AddItem(food);}
    static bool Allowed(PetNestCareOptionBase option){return(bool)typeof(PetNestCareOptionBase).GetMethod("IsInteractable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(option,null);}
    static void Complete(PetNestCareOptionBase option){Check(Allowed(option),"formal option must be enabled");typeof(PetNestCareOptionBase).GetMethod("OnTimeOut",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(option,null);}
    static PetNestCareOptionBase[] Options(CharacterMainControl pet)
    {
        var host=pet.gameObject.AddComponent<InteractableBase>();var attach=typeof(PetNestCareService).GetMethod("AttachOptions",BindingFlags.Static|BindingFlags.NonPublic);
        if(attach.GetParameters().Length==3)attach.Invoke(null,new object[]{host,"pet",pet});else attach.Invoke(null,new object[]{host,"pet"});
        return host.Group.Cast<PetNestCareOptionBase>().ToArray();
    }
    static void Main()
    {
        Case("successful feed persists food and growth together",()=>{
            Add(Food(42,2));CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");PetNestSaveCoordinator.Captures=0;
            var pet=Character();PetNestCareService.TryFeed("pet",pet);Check(PetNestService.Pet.exp==20&&FoodCount()==1,"feed consumes exactly one and grants 20 exp");
            Check(PetNestSaveCoordinator.HostDestroy(),"formal host-destroy flush succeeds");var saved=(PetNestPetRecord)SavesSystem.Disk["Pet"];
            Check(saved.exp==20&&saved.lastFeedUtcTicks>0&&(int)SavesSystem.Disk["MainCharacterItemData"]==1&&PetNestSaveCoordinator.Captures>0,"growth, cooldown and consumed food share snapshot");
            Check(Math.Abs(pet.Health.CurrentHealth-60)<.01,"feed heals its actual companion");
        });
        Case("partial-stack notification failure restores original",()=>{
            var food=Food(42,3);Add(food);food.ThrowAfterCount=true;PetNestCareService.TryFeed("pet",Character());
            Check(FoodCount()==3&&PetNestService.Pet.exp==0&&PetNestService.Pet.lastFeedUtcTicks==0,"notification failure loses neither food nor grants growth");
            Check(ReferenceEquals(Bag.GetItemAt(0),food)&&food.Variables["original"]=="keep","original object and variables remain");
        });
        Case("whole-item notification failure restores same object",()=>{
            var food=Food(42,1);Add(food);Bag.ThrowAfterRemove=true;PetNestCareService.TryFeed("pet",Character());
            Check(FoodCount()==1&&ReferenceEquals(Bag.GetItemAt(0),food)&&food.Variables["original"]=="keep"&&PetNestService.Pet.exp==0,"original final item restored after notification");
        });
        Case("pre-mutation failure cannot create food",()=>{
            var food=Food(42,3);Add(food);food.ThrowBeforeCount=true;PetNestCareService.TryFeed("pet",Character());Check(FoodCount()==3&&PetNestService.Pet.exp==0,"failed pre-mutation writes do not add food");
        });
        Case("rejected pet transaction rolls back exact food",()=>{
            var food=Food(42,1);Add(food);PetNestPersistenceAccess.RejectCommit=true;PetNestCareService.TryFeed("pet",Character());
            Check(FoodCount()==1&&ReferenceEquals(Bag.GetItemAt(0),food)&&food.Variables["original"]=="keep"&&PetNestService.Pet.exp==0,"rejected growth restores original item");
            Check(!PetNestPersistenceAccess.IsTransactionActive,"rejected commit releases candidate transaction");
        });
        Case("save failure keeps accepted growth and pending asset obligation",()=>{
            Add(Food(42,2));CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");SavesSystem.FailAt=1;PetNestCareService.TryFeed("pet",Character());
            Check(FoodCount()==1&&PetNestService.Pet.exp==20&&SavesSystem.Disk.Count==0,"IO failure never refunds accepted consumption");
            SavesSystem.FailAt=0;Check(PetNestSaveCoordinator.HostDestroy(),"asset save retry succeeds");Check((int)SavesSystem.Disk["MainCharacterItemData"]==1&&((PetNestPetRecord)SavesSystem.Disk["Pet"]).exp==20,"retry retains matching consumption and growth");
        });
        Case("post-commit presentation failure does not refund",()=>{
            Add(Food(42,1));PetNestService.Pet.exp=90;PetNestCompanionRuntime.ThrowRefresh=true;PetNestCareService.TryFeed("pet",Character());
            Check(FoodCount()==0&&PetNestService.Pet.level==2&&PetNestService.Pet.exp==10,"refresh exception cannot refund a committed meal");
        });
        Case("base feed pat and behavior target their attached companion",()=>{
            var pet=Character();Add(Food(42,2));var options=Options(pet);Check(options.Length==3&&options.All(Allowed),"base options enabled without raid companion owner");
            Complete(options.OfType<PetNestFeedOption>().Single());Check(PetNestService.Pet.exp==20&&pet.Health.CurrentHealth==60,"base feed applies to base pet");Check(options.OfType<PetNestFeedOption>().Single().InteractName.Contains("10"),"feed option shows the cooldown right after feeding");
            Complete(options.OfType<PetNestPatOption>().Single());Check(Duckov.UI.DialogueBubbles.DialogueBubblesManager.LastTarget==pet.transform,"base pet receives interaction bubble");
            Complete(options.OfType<PetNestBehaviourOption>().Single());Check(PetNestService.Pet.behaviour==1,"base behavior stored for next raid");
            SavesSystem.CurrentSlot=2;Check(options.All(x=>!Allowed(x)),"old options cannot mutate another save slot");
            SavesSystem.CurrentSlot=1;Object.Destroy(pet.gameObject);Check(options.All(x=>!Allowed(x)),"destroyed character revokes interaction");
        });
        Case("existing option names follow official localization reinjection",()=>{
            var pet=Character();PetNestCompanionRuntime.CompanionCharacter=pet;var options=Options(pet);PetNestCareService.InjectOptionLocalization(PetNestBehaviour.Follow);
            foreach(bool chinese in new[]{false,true,false}){
                L10n.IsChinese=chinese;PetNestLocalization.Inject();Check(options.OfType<PetNestFeedOption>().Single().InteractName.StartsWith(chinese?"喂食":"Feed")&&(chinese||!options.OfType<PetNestFeedOption>().Single().InteractName.Contains("喂食")),"feed name changes language without respawn");
                Check(options.OfType<PetNestPatOption>().Single().InteractName==(chinese?"摸摸":"Pet"),"pat name changes language without respawn");
                Check(options.OfType<PetNestBehaviourOption>().Single().InteractName.Contains(chinese?"跟随":"Follow"),"behavior name preserves current state in new language");
            }
        });
        Case("base option names refresh on the same instance",()=>{
            var pet=Character();PetNestBaseIdleSpawner.ActiveCount=1;var options=Options(pet);
            Complete(options.OfType<PetNestBehaviourOption>().Single());
            L10n.IsChinese=false;PetNestLocalization.Inject();
            Check(PetNestCompanionRuntime.CompanionCharacter==null&&options.OfType<PetNestBehaviourOption>().Single().InteractName.Contains("Aggressive"),"base owner uses current pet behavior in new language");
            Check(options.OfType<PetNestFeedOption>().Single().InteractName.StartsWith("Feed")&&options.OfType<PetNestPatOption>().Single().InteractName=="Pet","same base care options refresh without raid owner or respawn");
        });
        Case("settled historical risk stays frozen while in-flight Rough becomes safe",()=>{
            var historical=new PetNestExpeditionRecord{riskTier=1,deathRate=.06f,settled=true};Check(PetNestExpeditionService.EffectiveDeathRate(historical)==.06f,"settled historical risk stays 6 percent");
            var traveling=new PetNestExpeditionRecord{riskTier=1,deathRate=.06f};Check(PetNestExpeditionService.EffectiveDeathRate(traveling)==0f,"existing in-flight Rough is protected");
        });
        Console.WriteLine("PetNestCare "+checks+" assertions, "+failures+" failed cases");if(failures!=0)Environment.Exit(1);
    }
}
