using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using BossRush;
using Duckov;
using Duckov.Scenes;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions;
    private static int failures;
    private const string SceneId = "BossRush_SkyIsland";
    private static Scene sky;
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Static | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
        .ToDictionary(op => unchecked((ushort)op.Value));

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) { failures++; Console.WriteLine("FAIL " + message); }
    }
    private static void Reset()
    {
        sky = new Scene { handle = 42, isLoaded = true };
        SceneManager.Active = sky;
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.gameObject.scene = sky;
        LevelManager.Instance = new LevelManager { MainCharacter = CharacterMainControl.Main };
        LevelManager.Instance.gameObject.scene = sky;
        LevelManager.AfterInit = LevelManager.LevelInited = true;
        LevelConfig.Instance = new LevelConfig();
        LevelConfig.Instance.gameObject.scene = sky;
        LevelConfig.SaveCharacter = LevelConfig.SpawnTomb = true;
        MultiSceneCore.Instance = new MultiSceneCore();
        MultiSceneCore.Instance.gameObject.scene = sky;
        MultiSceneCore.ActiveSubScene = sky;
        MultiSceneCore.ActiveSubSceneID = SceneId;
        DeadBodyManager.Instance = new DeadBodyManager();
        SceneInfoCollection.Entry = new object();
        SceneLocationsProvider.Provider = new object();
    }
    private static void Reject(Action breakSetup, string label)
    {
        Reset();
        breakSetup();
        string reason;
        Check(!SkyIslandOfficialContract.Verify(sky, SceneId, out reason) && !string.IsNullOrEmpty(reason), label);
    }
    private static void VerifyProductionContract()
    {
        Reset();
        string reason;
        Check(SkyIslandOfficialContract.Verify(sky, SceneId, out reason) && reason == null, "完整独立关卡通过");
        Scene other = new Scene { handle = 7, isLoaded = true };
        Reject(() => SceneManager.Active = other, "活跃场景仍是基地拒绝");
        Reject(() => LevelManager.Instance.gameObject.scene = other, "借用基地 LevelManager 拒绝");
        Reject(() => LevelConfig.Instance.gameObject.scene = other, "借用基地 LevelConfig 拒绝");
        Reject(() => MultiSceneCore.Instance.gameObject.scene = other, "借用基地 MultiSceneCore 拒绝");
        Reject(() => LevelManager.AfterInit = false, "官方尾段尚未完成拒绝");
        Reject(() => LevelManager.Instance.IsBaseLevel = true, "基地规则拒绝");
        Reject(() => LevelManager.Instance.IsRaidMap = false, "非 Raid 不能承担死亡契约");
        Reject(() => LevelConfig.SpawnTomb = false, "缺少官方死亡掉落拒绝");
        Reject(() => LevelConfig.SaveCharacter = false, "角色保存关闭拒绝");
        Reject(() => LevelConfig.Instance.timeOfDayConfig = null, "时间配置空引用拒绝");
        Reject(() => LevelConfig.Instance.startBuffPrefabs = null, "起始 Buff 未装配拒绝");
        Reject(() => LevelManager.Instance.GameCamera.renderCamera = null, "相机缺失拒绝");
        Reject(() => LevelManager.Instance.CharacterCreator = null, "角色创建器缺失拒绝");
        Reject(() => CharacterMainControl.Main.gameObject.scene = other, "搬运旧基地角色拒绝");
        Reject(() => CharacterMainControl.Main.CharacterItem = null, "官方背包未还原拒绝");
        Reject(() => MultiSceneCore.ActiveSubSceneID = "Base", "死亡存档归属基地拒绝");
        Reject(() => MultiSceneCore.ActiveSubScene = other, "子场景实例错误拒绝");
        Reject(() => SceneLocationsProvider.Provider = null, "无出生点提供器拒绝");
        Reject(() => SceneInfoCollection.Entry = null, "未注册官方场景身份拒绝");
        Reject(() => DeadBodyManager.Instance = null, "无官方死亡记录管理器拒绝");
    }

    private static string EntityName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodSpecification)
            return EntityName(reader, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return EntityName(reader, member.Parent) + "." + reader.GetString(member.Name);
        }
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return EntityName(reader, method.GetDeclaringType()) + "." + reader.GetString(method.Name);
        }
        if (handle.Kind == HandleKind.FieldDefinition)
            return reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name);
        if (handle.Kind == HandleKind.TypeReference)
            return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name);
        if (handle.Kind == HandleKind.TypeDefinition)
            return reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name);
        return handle.Kind.ToString();
    }

    private static List<string> ReadReferences(PEReader pe, MetadataReader reader, string typeName, string methodName)
    {
        TypeDefinitionHandle th = reader.TypeDefinitions.First(h => reader.GetString(reader.GetTypeDefinition(h).Name) == typeName);
        TypeDefinition type = reader.GetTypeDefinition(th);
        MethodDefinition method = reader.GetMethodDefinition(type.GetMethods().First(h => reader.GetString(reader.GetMethodDefinition(h).Name) == methodName));
        byte[] bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        List<string> result = new List<string>();
        for (int i = 0; i < bytes.Length;)
        {
            ushort code = bytes[i++];
            if (code == 0xfe) code = (ushort)(0xfe00 | bytes[i++]);
            OpCode op = OpCodesByValue[code];
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + BitConverter.ToInt32(bytes, i) * 4; break;
                default: size = 4; break;
            }
            if (op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineField)
                result.Add(EntityName(reader, MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, i))));
            i += size;
        }
        return result;
    }

    private static void Ordered(List<string> refs, params string[] expected)
    {
        int cursor = 0;
        foreach (string name in expected)
        {
            int index = refs.FindIndex(cursor, candidate => candidate == name);
            Check(index >= 0, "真实 DLL 调用顺序包含 " + name);
            if (index < 0) return;
            cursor = index + 1;
        }
    }

    private static string StateMachine(MetadataReader reader, string prefix)
    {
        return reader.TypeDefinitions.Select(h => reader.GetString(reader.GetTypeDefinition(h).Name))
            .Single(name => name.StartsWith("<" + prefix + ">d__", StringComparison.Ordinal));
    }

    private static void VerifyGameAssembly(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (PEReader pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            Console.WriteLine("只读官方 DLL MVID=" + reader.GetGuid(reader.GetModuleDefinition().Mvid));
            List<string> death = ReadReferences(pe, reader, StateMachine(reader, "CharacterDieTask"), "MoveNext");
            Ordered(death, "LevelManager.get_IsRaidMap", "RaidUtilities.NotifyDead", "LevelConfig.get_SaveCharacter",
                "DeadBodyManager.RecordDeath", "ItemSavesUtilities.SaveAsLastDeadCharacter", "LevelConfig.get_SpawnTomb",
                "InteractableLootbox.CreateFromItem", "CharacterMainControl.DestroyAllItem", "LevelManager.SaveMainCharacter",
                "SavesSystem.CollectSaveData", "SavesSystem.SaveFile", "UniTask.WaitForSeconds",
                "ClosureView.ShowAndReturnTask", "SceneLoader.LoadBaseScene");
            List<string> record = ReadReferences(pe, reader, "DeadBodyManager", "RecordDeath");
            Ordered(record, "RaidUtilities.get_CurrentRaid", "MultiSceneCore.get_ActiveSubSceneID", "ItemTreeData.FromItem", "DeadBodyManager.AppendDeathInfo");
            List<string> shouldSpawn = ReadReferences(pe, reader, "DeadBodyManager", "ShouldSpawnDeadBody");
            Check(shouldSpawn.Contains("Ruleset.get_SpawnDeadBody") && shouldSpawn.Contains("LevelManager.get_IsRaidMap")
                && shouldSpawn.Contains("MultiSceneCore.get_ActiveSubSceneID") && shouldSpawn.Contains("touched"),
                "墓碑仍按官方难度与未触摸的独立子图身份恢复");
            List<string> restore = ReadReferences(pe, reader, StateMachine(reader, "SpawnDeadBody"), "MoveNext");
            Ordered(restore, "ItemTreeData.InstantiateAsync", "InteractableLootbox.CreateFromItem");
            List<string> init = ReadReferences(pe, reader, StateMachine(reader, "InitLevel"), "MoveNext");
            Ordered(init, "LevelManager.CreateMainCharacterAsync", "LevelManager.WaitForOtherInitialization", "LevelManager.HandleRaidInitialization",
                "Health.SetHealth", "LevelManager.CreateMainCharacterMapElement", "CharacterMainControl.SetPosition");
            List<string> create = ReadReferences(pe, reader, StateMachine(reader, "CreateMainCharacterAsync"), "MoveNext");
            Ordered(create, "LevelManager.LoadOrCreateCharacterItemInstance", "CharacterCreator.CreateCharacter",
                "LevelManager.SetControllingCharacter", "CharacterMainControl.SetTeam", "InputManager.SwitchItemAgent");
            List<string> hurt = ReadReferences(pe, reader, "Health", "Hurt");
            Check(hurt.Contains("LevelManager.get_IsRaidMap") && hurt.Contains("Health.get_CanDieIfNotRaidMap"), "非 Raid 的 1 HP 保护仍在官方 Hurt 内");
            List<string> configAwake = ReadReferences(pe, reader, "LevelConfig", "Awake");
            Check(configAwake.Contains("PrefabsData.get_LevelManagerPrefab"), "LevelConfig.Awake 仍使用官方完整服务 prefab");
        }
    }

    // ---- CR-2026-09-08-005：激活前的最小装配合同 ----
    private static UnityEngine.GameObject services, world;
    private static SceneLocationsProvider provider;
    private static SubSceneEntry subScene;

    /// <summary>按真实作者场景装配一份合格的独立关卡：服务根未激活、provider 唯一、出生点与缓存一致。</summary>
    private static void BuildActivationScene()
    {
        services = new UnityEngine.GameObject("SkyIslandLevel") { scene = sky, activeSelf = false };
        world = new UnityEngine.GameObject("SkyIslandWorld") { scene = sky, activeSelf = false };
        services.Attach(new LevelConfig());
        MultiSceneCore core = services.Attach(new MultiSceneCore());
        subScene = new SubSceneEntry { sceneID = SceneId };
        core.SubScenes.Add(subScene);
        UnityEngine.GameObject spawn = world.AddChild("PlayerSpawn", new UnityEngine.Vector3(1, 2, 3));
        world.AddChild("Exit", new UnityEngine.Vector3(9, 0, 9));
        UnityEngine.GameObject nav = world.AddChild("Navigation", new UnityEngine.Vector3(0, 0, 0));
        nav.Attach(new UnityEngine.MeshFilter { gameObject = nav });
        provider = new SceneLocationsProvider { gameObject = new UnityEngine.GameObject { scene = sky } };
        provider.Locations[SkyIslandSceneReferenceBridge.SpawnLocation] = spawn.transform;
        services.Attach(provider);
        subScene.cachedLocations.Add(new SubSceneEntry.Location
        { path = SkyIslandSceneReferenceBridge.SpawnLocation, position = spawn.transform.position });
    }
    private static void RejectActivation(Action breakSetup, string label)
    {
        Reset();
        BuildActivationScene();
        breakSetup();
        string reason;
        Check(!SkyIslandOfficialContract.VerifyBeforeActivation(sky, services, world, out reason)
            && !string.IsNullOrEmpty(reason), label);
    }
    private static void VerifyActivationContract()
    {
        Reset();
        BuildActivationScene();
        string reason;
        Check(SkyIslandOfficialContract.VerifyBeforeActivation(sky, services, world, out reason) && reason == null,
            "合格的最小装配在激活前通过");
        RejectActivation(() => services.activeSelf = true, "服务根已激活拒绝：配置必须先于激活");
        RejectActivation(() => services.scene = new Scene { handle = 7, isLoaded = true }, "服务根不属于本场景拒绝");
        RejectActivation(() => world.scene = new Scene { handle = 7, isLoaded = true }, "地形根不属于本场景拒绝");
        RejectActivation(() => services.Parts.Remove(services.GetComponent<LevelConfig>()), "缺 LevelConfig 拒绝");
        RejectActivation(() => services.GetComponent<LevelConfig>().enabled = false, "LevelConfig 被禁用拒绝");
        RejectActivation(() => services.GetComponent<LevelConfig>().timeOfDayConfig = null, "缺天气配置拒绝");
        RejectActivation(() => services.GetComponent<MultiSceneCore>().enabled = false, "MultiSceneCore 被禁用拒绝");
        RejectActivation(() => services.GetComponent<MultiSceneCore>().SubScenes.Clear(), "缺自身子场景声明拒绝");
        RejectActivation(() => subScene.sceneID = "Base", "子场景身份不是天空岛拒绝");
        RejectActivation(() => subScene.cachedTeleporters = null, "位置表未装配拒绝");
        RejectActivation(() => subScene.cachedLocations.Clear(), "出生记录缺失拒绝");
        RejectActivation(() => subScene.cachedLocations.Add(new SubSceneEntry.Location
        { path = SkyIslandSceneReferenceBridge.SpawnLocation }), "出生记录重复拒绝");
        RejectActivation(() => subScene.cachedLocations[0].position = new UnityEngine.Vector3(50, 0, 0),
            "出生缓存与真实位置不一致拒绝");
        // 报告 CR-2026-09-08-005 的原始复现条件：场景其余部分正确，只是漏装 provider。
        RejectActivation(() => services.Parts.Remove(provider), "缺 SceneLocationsProvider 拒绝");
        RejectActivation(() => provider.enabled = false, "provider 被禁用拒绝");
        RejectActivation(() => services.Attach(new SceneLocationsProvider { gameObject = new UnityEngine.GameObject { scene = sky } }),
            "provider 重复拒绝");
        RejectActivation(() => provider.Locations.Clear(), "provider 缺出生点拒绝");
        RejectActivation(() => world.transform.Children.RemoveAll(child => child.name == "Exit"), "缺返航点拒绝");
        RejectActivation(() => world.transform.Children.RemoveAll(child => child.name == "Navigation"), "缺导航网格拒绝");
        RejectActivation(() => world.transform.Find("Navigation").GetComponent<UnityEngine.MeshFilter>().sharedMesh.isReadable = false,
            "导航网格不可读拒绝");
        RejectActivation(() => world.transform.Find("Navigation").GetComponent<UnityEngine.MeshFilter>().sharedMesh.vertexCount = 4096,
            "导航顶点超出官方 4095 上限拒绝");
        RejectActivation(() => world.transform.Find("Navigation").GetComponent<UnityEngine.MeshFilter>().sharedMesh.vertexCount = 0,
            "空导航网格拒绝");
    }

    private static int Main(string[] args)
    {
        try
        {
            VerifyProductionContract();
            VerifyActivationContract();
            if (args.Length != 1 || !File.Exists(args[0])) throw new FileNotFoundException("必须提供本机 TeamSoda.Duckov.Core.dll；不能把缺依赖算通过");
            VerifyGameAssembly(args[0]);
        }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + e); }
        Console.WriteLine("SkyIslandOfficialContract: " + assertions + " assertions / " + failures + " failures; 未执行 Unity 或玩家存档。");
        return failures == 0 ? 0 : 1;
    }
}
