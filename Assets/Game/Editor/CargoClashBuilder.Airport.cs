using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CargoClash;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The airport level: the team's Map3 de_airport (玩法与设计说明.md and 接入说明.md in 独立建模/Map3 de_airport), with
/// Astra's refinement of it (模型精修04: more detail, and the cargo laid out for the game's four kinds). Her
/// AirportEnvironment.fbx, AirportPalette.json, AirportDistrictHorizon.png and AirportCargoLayout.json, the teammate's
/// backdrop shader, music and materials, are in Assets/Game/Models/Airport. This builds the playable scene around them,
/// the way the teammate's own generator (CargoClashAirportScene) did on main, but on this branch's match setup: our HUD,
/// cargo, crew and camera, Astra's opening layout, the warehouse queue on the infeed benches, the bots' map, the minimap
/// and the level card.
///
/// Run on its own with Tools > Cargo Clash > Build Airport Level (-executeMethod CargoClashBuilder.BuildAirportLevel),
/// which touches nothing but the airport, the director's rules, the level list and Build Settings. Then Apply Crew
/// Appearance and Apply Menu Pages, as for the other levels.
/// </summary>
public static partial class CargoClashBuilder
{
    public const string AirportSceneName = "LevelAirport";
    private const string AirportScenePath = SceneDir + "/" + AirportSceneName + ".unity";
    private const string AirportDir = Root + "/Models/Airport";
    private const string AirportModel = AirportDir + "/AirportEnvironment.fbx";
    private const string AirportPalettePath = AirportDir + "/AirportPalette.json";
    private const string AirportLayoutPath = AirportDir + "/AirportCargoLayout.json";
    private const string AirportHorizonPath = AirportDir + "/AirportDistrictHorizon.png";
    private const string AirportMusicPath = AirportDir + "/AirportMatchMusic.wav";
    private const string AirportInterludePath = AirportDir + "/AirportInterlude.wav";
    private const string AirportPanorama = "Airport District Panorama";
    private const string AirportAnchor = "CC_AirportAnchor_";
    private const string AirportMapPath = TextureDir + "/Airport_Map.png";
    private const string AirportCardName = "Card_Airport";
    // The match music is 60 seconds; its fast part starts here, for the last 15.
    private const float AirportSprintStart = 45f;

    [Serializable] private sealed class AirportPaletteFile { public AirportPaletteEntry[] entries; }
    [Serializable] private sealed class AirportPaletteEntry { public string name; public float r, g, b, a; }
    [Serializable] private sealed class AirportLayoutFile { public AirportOpening[] opening; public AirportQueued[] dispatch; public AirportOnBelt[] beltInProgress; }
    [Serializable] private sealed class AirportOpening { public string kind; public string environmentAnchor; public float yawBlenderDegrees; }
    [Serializable] private sealed class AirportQueued { public int order; public string kind; public float[] rackBlenderXYZ; }
    [Serializable] private sealed class AirportOnBelt { public int order; public string kind; public float[] blenderXYZ; }

    /// <summary>
    /// The teammate's list of what people stand on or bump into. Everything else in the model is only looked at (and
    /// anything named CC_AirportCollision_ is only stood on).
    /// </summary>
    private static readonly string[] AirportSolidPrefixes =
    {
        "CC_AirportCollision_", "LowerFloor_", "UpperFloor_", "Helipad_Deck", "Stair_UpperThreshold", "ParkingRamp_Deck",
        "Stair_ReturnLanding", "Column_Concrete", "Airfield_Site", "Airport_ServiceRoad",
        "Airport_TransferApron", "West_ServiceApron", "WestApron_ServiceRoadLink", "East_ServiceApron",
        "Baggage_SortingApron", "Warehouse_LoadingDock", "Warehouse_DockApron", "Airside_Taxiway",
        "MainWarehouse_Wall", "MainWarehouse_Roof", "Dispatch_Throat", "Dispatch_Jamb",
        "ServiceBooth_Wall", "GroundPower_Cabinet", "LadderRoom_", "IronDoor_Leaf", "VentDuct_HollowGalvanizedShell",
        "Upper_Perimeter_Rail", "Shaft_SideGuard_Rail", "Stair_Handrail", "Stair_LandingOuter_Rail",
        "Stair_LandingInner_Rail", "Stair_LandingNorth_Rail", "Stair_LandingSouth_Rail", "Airport_PerimeterFence_Rail",
    };

    /// <summary>
    /// Astra's new things standing in the play area (精修04), which people shouldn't walk through: a box round each. Signs
    /// over heads, markings, the duct's marker (over its mouth) and the ladder's cage (people walk into it) stay open.
    /// </summary>
    private static readonly string[] AirportPropBoxes =
    {
        "B03_LD3RollerDolly", "B03_TransferTrayCart", "B03_BaggageHandlingDetails", "BaggageDolly_Platform",
        "Airport04_B03TagStation", "B03_ControlStation", "H01_RampEntry", "H01_DeliverySign",
    };

    /// <summary>Her equipment by the truck on the upper deck (the windsock mast and the cabinets): its own shape.</summary>
    private static readonly string[] AirportPropMeshes = { "Airport04_H01Equip" };

    [MenuItem("Tools/Cargo Clash/Build Airport Level")]
    public static void BuildAirportLevel()
    {
        modelMaterials = null;
        AddAirportRulesToDirector();
        GameConfig config = LoadOrCreate<GameConfig>(ResourcesDir + "/GameConfig.asset");
        BuildAirportScene(config.cargo);
        config = LoadOrCreate<GameConfig>(ResourcesDir + "/GameConfig.asset");
        config.levels = BuildLevelList();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        UpdateBuildScenes(config);
        // The camera rig and the helicopter remake the model materials with their first finishes; put the refined ones back.
        CargoClashVisualRefinement.ApplyMaterialFinishes();
        Debug.Log("[Builder] Airport level built. Run Apply Crew Appearance and Apply Menu Pages next.");
    }

    /// <summary>The director's network object carries each level's rules; the airport's join the river's.</summary>
    private static void AddAirportRulesToDirector()
    {
        string path = PrefabDir + "/MatchDirector.prefab";
        GameObject director = PrefabUtility.LoadPrefabContents(path);
        if (director.GetComponent<AirportMatch>() == null) director.AddComponent<AirportMatch>();
        PrefabUtility.SaveAsPrefabAsset(director, path);
        PrefabUtility.UnloadPrefabContents(director);
    }

    // ---- The scene ----

    private static void BuildAirportScene(CargoDefinition[] cargo)
    {
        PrepareAirportAssets();
        Dictionary<string, Material> palette = BuildAirportMaterials();
        AirportLayoutFile plan = JsonUtility.FromJson<AirportLayoutFile>(File.ReadAllText(AirportLayoutPath));
        PlayerSettings.bakeCollisionMeshes = true;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddDaylight();
        AddCountrySky();
        BuildPlayerCamera();
        Camera camera = Object.FindAnyObjectByType<FollowCamera>().view;

        Transform level = Group("Airport", null);
        GameObject environment = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AirportModel), level);
        PrefabUtility.UnpackPrefabInstance(environment, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        environment.name = "Airport Environment";
        SetUpAirportEnvironment(environment, palette);

        Dictionary<string, Transform> anchors = environment.GetComponentsInChildren<Transform>(true)
            .Where(item => item.name.StartsWith(AirportAnchor, StringComparison.Ordinal))
            .ToDictionary(item => item.name.Substring(AirportAnchor.Length), item => item);
        Transform Mark(string key) => anchors.TryGetValue(key, out Transform found)
            ? found : throw new InvalidOperationException("The airport model has no anchor " + AirportAnchor + key);

        AirportFacility facility = WireAirportFacility(level, environment, Mark);
        facility.dispatchOrder = plan.dispatch.OrderBy(entry => entry.order).Select(entry => ParseKind(entry.kind)).ToArray();
        facility.queueDisplays = AddWarehouseQueue(level, plan);

        Transform bed = AddAirportTruck(Mark, out Transform truck);
        LevelLayout layout = BuildAirportLayout(cargo, plan, truck, Mark);

        BakeAirportNavMesh(facility);
        AddAirportLinks(level, facility);
        CheckAirportLayout(layout, facility, cargo);

        BuildExtraction(bed);
        SetAirportHelicopterPath(Mark);
        miniMapSize = new Vector2(318f, 222f);
        BuildHud();
        miniMapSize = DefaultMiniMapSize;
        GameObject events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
        events.transform.SetAsLastSibling();

        ReportAirportSize(environment);
        EditorSceneManager.SaveScene(scene, AirportScenePath);

        layout.mapPicture = RenderAirportMap(layout.mapArea);
        RenderAirportCard(environment, layout, plan, Mark);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, AirportScenePath);
    }

    private static CargoKind ParseKind(string kind) => (CargoKind)Enum.Parse(typeof(CargoKind), kind);

    /// <summary>Import settings: no cameras or lights from the model, the music decompressed for exact seeking, the panorama wide.</summary>
    private static void PrepareAirportAssets()
    {
        ModelImporter model = (ModelImporter)AssetImporter.GetAtPath(AirportModel);
        if (model.importCameras || model.importLights || model.isReadable || model.importAnimation)
        {
            model.importCameras = false;
            model.importLights = false;
            model.isReadable = false;
            model.importAnimation = false;
            model.SaveAndReimport();
        }

        AudioImporter music = (AudioImporter)AssetImporter.GetAtPath(AirportMusicPath);
        AudioImporterSampleSettings settings = music.defaultSampleSettings;
        // The music jumps to its fast part when a half is short, which needs exact seeking.
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.8f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        music.defaultSampleSettings = settings;
        music.SaveAndReimport();

        TextureImporter horizon = (TextureImporter)AssetImporter.GetAtPath(AirportHorizonPath);
        horizon.maxTextureSize = 4096;
        horizon.alphaIsTransparency = true;
        horizon.wrapModeU = TextureWrapMode.Repeat;
        horizon.wrapModeV = TextureWrapMode.Clamp;
        horizon.SaveAndReimport();
    }

    /// <summary>One URP/Lit material per colour in her palette, and the painted panorama on the teammate's backdrop shader.</summary>
    private static Dictionary<string, Material> BuildAirportMaterials()
    {
        string folder = AirportDir + "/Materials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(AirportDir, "Materials");
        AirportPaletteFile palette = JsonUtility.FromJson<AirportPaletteFile>(File.ReadAllText(AirportPalettePath));
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Dictionary<string, Material> made = new Dictionary<string, Material>();
        foreach (AirportPaletteEntry entry in palette.entries)
        {
            string path = $"{folder}/{entry.name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit) { name = entry.name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (entry.name == AirportPanorama)
            {
                material.shader = AssetDatabase.LoadAssetAtPath<Shader>(AirportDir + "/AirportDistrictBackdrop.shader");
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AirportHorizonPath);
            }
            else
            {
                material.shader = lit;
                material.SetColor("_BaseColor", new Color(entry.r, entry.g, entry.b, entry.a));
                material.SetFloat("_Smoothness", 0.28f);
            }
            EditorUtility.SetDirty(material);
            made[entry.name] = material;
        }
        AssetDatabase.SaveAssets();
        return made;
    }

    /// <summary>Her palette on every part, collision on what's solid, and no shadows from the sky, the clouds or the horizon.</summary>
    private static void SetUpAirportEnvironment(GameObject root, Dictionary<string, Material> palette)
    {
        int solid = 0;
        int props = 0;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            GameObject part = filter.gameObject;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(material => material != null && palette.TryGetValue(material.name, out Material mine) ? mine : material)
                    .ToArray();
                if (part.name.StartsWith("CC_AirportCollision_", StringComparison.Ordinal)) renderer.enabled = false;
                if (part.name.StartsWith("SkyCloud_Surface_", StringComparison.Ordinal) || part.name.StartsWith("AirportHorizon_", StringComparison.Ordinal))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            if (AirportSolidPrefixes.Any(prefix => part.name.StartsWith(prefix, StringComparison.Ordinal))
                || AirportPropMeshes.Any(prefix => part.name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                part.layer = GameLayers.Level;
                part.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                solid++;
            }
            else if (AirportPropBoxes.Any(prefix => part.name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                // A box round the prop, in its own space. A cart's raised platform still stops a person: they're taller.
                Bounds local = filter.sharedMesh.bounds;
                part.layer = GameLayers.Level;
                BoxCollider box = part.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size;
                props++;
            }
        }
        Debug.Log($"[Builder] Airport: {solid} solid parts, {props} props boxed in.");
    }

    // ---- Facilities ----

    /// <summary>The teammate's wiring (CargoClashAirportScene.ConfigureFacilities): the belt, the door on a hinge, the ladder and the duct.</summary>
    private static AirportFacility WireAirportFacility(Transform level, GameObject environment, Func<string, Transform> mark)
    {
        Transform[] parts = environment.GetComponentsInChildren<Transform>(true);
        AirportFacility facility = level.gameObject.AddComponent<AirportFacility>();
        facility.beltIn = mark("BeltIn");
        facility.beltOut = mark("BeltOut");
        facility.beltFloor = parts.Single(part => part.name == "CC_AirportCollision_FlushBelt").GetComponent<Collider>();
        facility.beltTreads = parts.Where(part => part.name.StartsWith("Belt_Tread", StringComparison.Ordinal)).ToArray();

        facility.doorCenter = mark("DoorCenter");
        facility.doorSwing = mark("DoorSwing");
        Transform hinge = Group("Iron Door Hinge", level);
        hinge.position = mark("DoorHinge").position;
        facility.doorHinge = hinge;
        facility.doorLeaf = parts.Single(part => part.name == "IronDoor_Leaf");
        facility.doorLeaf.SetParent(hinge, true);
        foreach (Transform handle in parts.Where(part => part.name.StartsWith("IronDoor_Handle", StringComparison.Ordinal)))
            handle.SetParent(facility.doorLeaf, true);

        facility.ladderLow = mark("LadderLow");
        facility.ladderShaft = mark("LadderShaft");
        facility.ladderHigh = mark("LadderHigh");
        facility.shaftLanding = mark("ShaftLanding");
        facility.unsafeDrop = mark("UnsafeDrop");
        facility.ductUpper = mark("DuctUpper");
        facility.ductLower = mark("DuctLower");
        facility.ductWaypoints = new[] { "DuctUpper", "DuctLip", "DuctBendIn", "DuctVerticalTop", "DuctVerticalBottom", "DuctBendOut",
            "DuctExitLip", "DuctLower" }.Select(mark).ToArray();
        return facility;
    }

    /// <summary>
    /// What the warehouse will send out, waiting on the infeed benches behind B03's mouth (her rack positions): a still,
    /// shut copy of each case, in order. AirportFacility hides each as its piece leaves.
    /// </summary>
    private static GameObject[] AddWarehouseQueue(Transform level, AirportLayoutFile plan)
    {
        Transform queue = Group("Warehouse Queue", level);
        List<GameObject> shown = new List<GameObject>();
        foreach (AirportQueued entry in plan.dispatch.OrderBy(item => item.order))
        {
            float[] p = entry.rackBlenderXYZ;
            GameObject display = PlaceStillCargo(ParseKind(entry.kind), queue, FromBlender(p[0], p[1], p[2]), 0f);
            display.name = $"Queued_{entry.order:00}_{entry.kind}";
            shown.Add(display);
        }
        return shown.ToArray();
    }

    /// <summary>A shut, undamaged copy of one of Astra's kinds standing on this point, only to look at.</summary>
    private static GameObject PlaceStillCargo(CargoKind kind, Transform parent, Vector3 bottom, float yaw)
    {
        GameObject model = NewAstraModel("Cargo_" + kind, parent);
        model.name = "Cargo_" + kind;
        Object.DestroyImmediate(FindInModel(model, "Stage1").gameObject);
        Object.DestroyImmediate(FindInModel(model, "Stage2").gameObject);
        if (kind != CargoKind.Art) Object.DestroyImmediate(FindInModel(model, "Slot0").gameObject);
        model.transform.SetPositionAndRotation(bottom, Quaternion.Euler(0f, yaw, 0f));
        SetLayerRecursively(model, GameLayers.Outlined);
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>()) part.shadowCastingMode = ShadowCastingMode.On;
        return model;
    }

    // ---- The truck ----

    /// <summary>
    /// Astra's crashed truck, broken down level on the round pad beside H01 with its bed toward the deck, and the wheel that
    /// came off lying on the pad. Returns its bed for the helicopter.
    /// </summary>
    private static Transform AddAirportTruck(Func<string, Transform> mark, out Transform truck)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/TruckCrashed.prefab");
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "Truck at H01";
        truck = instance.transform;
        Transform model = truck.Find("Model");
        model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Transform bed = truck.Find("Model/CargoBed");
        Vector3 bedDirection = Vector3.ProjectOnPlane(bed.position - truck.position, Vector3.up);
        Vector3 rearDirection = Vector3.ProjectOnPlane(mark("TruckRear").position - mark("Truck").position, Vector3.up);
        truck.SetPositionAndRotation(mark("Truck").position, Quaternion.FromToRotation(bedDirection, rearDirection));
        Transform looseWheel = truck.Find("LooseWheel");
        Vector3 wheel = looseWheel.position;
        wheel.y = mark("TruckLooseWheel").position.y;
        looseWheel.position = wheel;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        PrefabUtility.RecordPrefabInstancePropertyModifications(looseWheel);
        return bed;
    }

    // ---- Spawns and cargo ----

    /// <summary>
    /// Collectors start south of B03 and Interrupters in the ladder room's ground floor, both facing the truck. Each kind
    /// starts on Astra's one spot for it (her opening layout: a carton by the belt's end, cash by the west ladder, gold in
    /// the east aisle, the goose crate on the flat before the ramp); the rest come out of the warehouse.
    /// </summary>
    private static LevelLayout BuildAirportLayout(CargoDefinition[] cargo, AirportLayoutFile plan, Transform truck, Func<string, Transform> mark)
    {
        Transform layoutRoot = Group("Layout", null);
        LevelLayout layout = layoutRoot.gameObject.AddComponent<LevelLayout>();
        Vector3 rear = mark("TruckRear").position;
        Quaternion Facing(Vector3 from) => Quaternion.LookRotation(Vector3.ProjectOnPlane(rear - from, Vector3.up));
        layout.collectorSpawns = AnchorSpots(layoutRoot, "CollectorSpawn", mark, "Collector_00", "Collector_01", "Collector_02", Facing);
        layout.interrupterSpawns = AnchorSpots(layoutRoot, "InterrupterSpawn", mark, "Interrupter_00", "Interrupter_01", "Interrupter_02", Facing);
        layout.smallSpots = new Transform[0];
        layout.mediumSpots = new Transform[0];
        layout.largeSpots = new Transform[0];

        List<KindSpots> kinds = new List<KindSpots>();
        foreach (AirportOpening opening in plan.opening)
        {
            CargoKind kind = ParseKind(opening.kind);
            Transform anchor = mark(opening.environmentAnchor.Substring(AirportAnchor.Length));
            Transform spot = Group($"Spot_{kind}", layoutRoot);
            // Her anchors are the middle of each case's collider; a Blender turn about up is the opposite turn in Unity.
            spot.SetPositionAndRotation(anchor.position + Vector3.up * 0.03f, Quaternion.Euler(0f, -opening.yawBlenderDegrees, 0f));
            kinds.Add(new KindSpots { kind = kind, spots = new[] { spot } });
        }
        layout.kindSpots = kinds.ToArray();
        foreach (CargoKind kind in new[] { CargoKind.Tobacco, CargoKind.Cash, CargoKind.Gold, CargoKind.Art })
            if (!kinds.Any(entry => entry.kind == kind)) Debug.LogError($"[Builder] Airport: no opening spot for {kind}.");

        // H01: the delivery area on the upper deck, counting only at the deck's height.
        Transform zone = Group("H01 Delivery Area", layoutRoot);
        zone.position = mark("H01").position;
        layout.truckZone = zone;
        layout.truckZoneHalfSize = new Vector2(3.65f, 3.65f);
        layout.truckZoneHalfHeight = 1.65f;
        layout.bedOrigin = truck.Find("Model/BedOrigin");
        layout.bedHalfSize = new Vector2(1.25f, 1.95f);

        Vector3 a = mark("MapA").position;
        Vector3 b = mark("MapB").position;
        layout.mapArea = UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        // North (the warehouse) at the top, as in her drawings.
        layout.mapUpsideDown = true;

        layout.matchMusic = AssetDatabase.LoadAssetAtPath<AudioClip>(AirportMusicPath);
        layout.matchInterlude = AssetDatabase.LoadAssetAtPath<AudioClip>(AirportInterludePath);
        layout.matchMusicSprintStart = AirportSprintStart;
        return layout;
    }

    private static Transform[] AnchorSpots(Transform parent, string name, Func<string, Transform> mark, string first, string second, string third,
        Func<Vector3, Quaternion> facing)
    {
        string[] keys = { first, second, third };
        Transform[] spots = new Transform[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            Vector3 at = mark(keys[i]).position + Vector3.up * 0.05f;
            spots[i] = Group($"{name}{i + 1}", parent);
            spots[i].SetPositionAndRotation(at, facing(at));
        }
        return spots;
    }

    // ---- The bots' map and checks ----

    /// <summary>
    /// The bots' map, baked with the iron door open: the doorway is on it, and a bot that finds the door shut presses E
    /// (BotBrain.OpenDoorIfShut).
    /// </summary>
    private static void BakeAirportNavMesh(AirportFacility facility)
    {
        Collider door = facility.doorLeaf.GetComponent<Collider>();
        door.enabled = false;
        Physics.SyncTransforms();
        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << GameLayers.Level;
        surface.BuildNavMesh();
        string navPath = SceneDir + "/" + AirportSceneName + "_NavMesh.asset";
        AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        door.enabled = true;
        Physics.SyncTransforms();
    }

    /// <summary>
    /// The ladder (both ways) and the duct (down only) as links on the bots' map, costed about as long as they take, so a
    /// bot's path takes them when they're quicker. Walking into either starts it, the same as for a person (BotBrain.Steer).
    /// </summary>
    private static void AddAirportLinks(Transform level, AirportFacility facility)
    {
        // The same routes AirportFacility follows (it works them out when the level starts).
        GameConfig config = LoadOrCreate<GameConfig>(ResourcesDir + "/GameConfig.asset");
        Vector3 shaft = facility.ladderShaft.position;
        Vector3 shaftTop = new Vector3(shaft.x, facility.ladderHigh.position.y, shaft.z);
        float ladder = Vector3.Distance(facility.ladderLow.position, shaft) + Vector3.Distance(shaft, shaftTop)
            + Vector3.Distance(shaftTop, facility.ladderHigh.position);
        float duct = 0f;
        for (int i = 1; i < facility.ductWaypoints.Length; i++)
            duct += Vector3.Distance(facility.ductWaypoints[i - 1].position, facility.ductWaypoints[i].position);

        Transform links = Group("Bot Links", level);
        AddLink(links, "Ladder", facility.ladderLow.position, facility.ladderHigh.position, true, ladder / config.airportLadderSpeed);
        AddLink(links, "Duct", facility.ductUpper.position, facility.ductLower.position, false, duct / config.airportDuctSpeed);

        void AddLink(Transform parent, string name, Vector3 from, Vector3 to, bool bothWays, float seconds)
        {
            // Point links on the map's own surface: an edge link's corners have to land on the map too, and the ladder's
            // top landing is narrow.
            if (NavMesh.SamplePosition(from, out NavMeshHit onFrom, 1.2f, NavMesh.AllAreas)) from = onFrom.position;
            if (NavMesh.SamplePosition(to, out NavMeshHit onTo, 1.2f, NavMesh.AllAreas)) to = onTo.position;
            NavMeshLink link = new GameObject(name + " Link").AddComponent<NavMeshLink>();
            link.transform.SetParent(parent, true);
            link.startPoint = from;
            link.endPoint = to;
            link.width = 0f;
            // Their own area, so a bot carrying a crate with someone can leave them out (BotBrain.Repath).
            link.area = AirportFacility.TraversalArea;
            link.bidirectional = bothWays;
            link.autoUpdate = false;
            // A link costs its straight length; a bot walks about 5.5 m a second.
            link.costModifier = Mathf.Max(1f, seconds * 5.5f / Vector3.Distance(from, to));
        }
    }

    /// <summary>
    /// The teammate's checks (CargoClashAirportScene.VerifyAndBuild), on our layout: H01 only counts up on the deck, the belt
    /// runs out of the warehouse and its floor spans it, the duct drops straight down between walls, and every spawn,
    /// opening spot and the belt's end can walk to H01. Problems are logged as errors, so a build run shows them.
    /// </summary>
    private static void CheckAirportLayout(LevelLayout layout, AirportFacility facility, CargoDefinition[] cargo)
    {
        Physics.SyncTransforms();
        List<string> problems = new List<string>();
        Vector3 target = layout.truckZone.position;
        if (!layout.InTruckZone(target) || layout.InTruckZone(new Vector3(target.x, 0.4f, target.z)))
            problems.Add("H01 must count on the upper deck and not on the floor below");

        Vector3 along = facility.beltOut.position - facility.beltIn.position;
        if (along.magnitude < 25f) problems.Add($"B03 is only {along.magnitude:0.0} m long");
        Vector3 middle = Vector3.Lerp(facility.beltIn.position, facility.beltOut.position, 0.3f);
        Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
        foreach (float offset in new[] { -2.4f, 0f, 2.4f })
        {
            Vector3 point = middle + side * offset;
            if (!Physics.Raycast(point + Vector3.up * 0.55f, Vector3.down, out RaycastHit floor, 0.63f, GameLayers.LevelMask)
                || floor.collider != facility.beltFloor)
                problems.Add($"the belt's floor doesn't span it at {offset} m");
        }

        Vector3 drop = facility.ductWaypoints[3].position - facility.ductWaypoints[4].position;
        if (drop.y < 2.4f || Vector3.ProjectOnPlane(drop, Vector3.up).sqrMagnitude > 0.0025f) problems.Add("the duct's chute isn't a straight drop");
        if (facility.dispatchOrder.Length == 0 || facility.dispatchOrder.Length != facility.queueDisplays.Length)
            problems.Add("the warehouse queue is empty or doesn't match its display");

        NavMeshTriangulation map = NavMesh.CalculateTriangulation();
        Debug.Log($"[Builder] Airport bots' map: {map.vertices.Length} vertices, {map.indices.Length / 3} triangles.");
        List<(string name, Vector3 at)> starts = new List<(string, Vector3)>();
        starts.AddRange(layout.collectorSpawns.Select(spot => (spot.name, spot.position)));
        starts.AddRange(layout.interrupterSpawns.Select(spot => (spot.name, spot.position)));
        starts.AddRange(layout.kindSpots.SelectMany(kind => kind.spots).Select(spot => (spot.name, spot.position)));
        starts.Add(("belt's end", facility.beltOut.position));
        starts.Add(("warehouse mouth", facility.beltIn.position + along.normalized * 1.5f));
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.SamplePosition(target, out NavMeshHit end, 2f, NavMesh.AllAreas) || Mathf.Abs(end.position.y - target.y) > 0.6f)
            problems.Add($"H01 isn't on the bots' map ({target})");
        foreach ((string name, Vector3 at) in starts)
        {
            if (!NavMesh.SamplePosition(at, out NavMeshHit from, 2f, NavMesh.AllAreas))
            {
                problems.Add($"{name} is off the bots' map");
                continue;
            }
            if (!NavMesh.CalculatePath(from.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                problems.Add($"{name} can't walk to H01 ({path.status})");
            else Debug.Log($"[Builder] Airport: {name} walks to H01 in {PathLength(path):0} m.");
        }
        // The goose crate goes up the ramp: two people can't take it up the ladder.
        int rampOnly = NavMesh.AllAreas & ~(1 << AirportFacility.TraversalArea);
        foreach (Transform spot in layout.kindSpots.Where(kind => kind.kind == CargoKind.Art).SelectMany(kind => kind.spots))
        {
            if (!NavMesh.SamplePosition(spot.position, out NavMeshHit from, 2f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(from.position, end.position, rampOnly, path) || path.status != NavMeshPathStatus.PathComplete)
                problems.Add($"{spot.name} can't be carried to H01 without the ladder");
            else Debug.Log($"[Builder] Airport: the goose crate goes up the ramp to H01 in {PathLength(path):0} m.");
        }
        foreach (KindSpots kind in layout.kindSpots)
        {
            CargoItem item = cargo.First(definition => definition.kind == kind.kind).prefab.GetComponent<CargoItem>();
            foreach (Transform spot in kind.spots)
            {
                if (!Physics.Raycast(spot.position, Vector3.down, out RaycastHit floor, 1.5f, GameLayers.LevelMask))
                    problems.Add($"{spot.name} has no floor under it");
                else if (Mathf.Abs(spot.position.y - item.HalfExtents.y - floor.point.y) > 0.12f)
                    problems.Add($"{spot.name} stands {spot.position.y - item.HalfExtents.y - floor.point.y:0.00} m off its floor");
                if (Physics.CheckBox(spot.position + Vector3.up * 0.05f, item.HalfExtents * 0.9f, spot.rotation, GameLayers.LevelMask, QueryTriggerInteraction.Ignore))
                    problems.Add($"{spot.name} starts inside something solid");
            }
        }
        // The ladder and the duct are on the bots' map: up from the ladder's foot, and down from the duct's mouth.
        foreach ((string name, Vector3 from, Vector3 to) in new[]
        {
            ("the ladder", facility.ladderLow.position, facility.ladderHigh.position),
            ("the duct", facility.ductUpper.position, facility.ductLower.position),
        })
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 1.2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, 1.2f, NavMesh.AllAreas))
            {
                problems.Add($"{name}'s ends aren't on the bots' map");
                continue;
            }
            bool found = NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            float walked = found ? PathLength(path) : float.PositiveInfinity;
            Debug.Log($"[Builder] Airport: through {name} the bots' path is {walked:0} m with {path.corners.Length} corners " +
                      $"(ends {from} -> map {a.position}, {to} -> map {b.position}; corners {string.Join(" ", path.corners.Select(c => c.ToString("F1")))}).");
            if (!found || walked > Vector3.Distance(from, to) + 4f) problems.Add($"the bots' map doesn't take {name} ({walked:0} m)");
        }
        if (problems.Count == 0) Debug.Log("[Builder] Airport checks passed: H01 on the deck, B03, the duct, the queue, the bots' ladder and duct, and every start reaches H01.");
        else foreach (string problem in problems) Debug.LogError("[Builder] Airport: " + problem);
    }

    private static float PathLength(NavMeshPath path)
    {
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return length;
    }

    /// <summary>
    /// The teammate's flight: the helicopter comes in along the deck's long axis from the far side and leaves the same way,
    /// watched from over the deck.
    /// </summary>
    private static void SetAirportHelicopterPath(Func<string, Transform> mark)
    {
        HelicopterExtraction extraction = Object.FindAnyObjectByType<HelicopterExtraction>();
        float escape = Mathf.Sign(mark("H01").position.x - mark("TruckRear").position.x);
        extraction.leaveDirection = Vector3.right * escape;
        extraction.pathStart = new Vector3(160f * escape, 2f, 4f);
        extraction.pathControlA = new Vector3(85f * escape, 0f, 3f);
        extraction.pathControlB = new Vector3(25f * escape, 0f, 2f);
        extraction.shotOffset = new Vector3(21f * escape, 13f, -14f);
    }

    private static void ReportAirportSize(GameObject environment)
    {
        long triangles = 0;
        MeshFilter[] filters = environment.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
            if (filter.sharedMesh != null)
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) triangles += filter.sharedMesh.GetIndexCount(i) / 3;
        Debug.Log($"[Builder] Airport scenery: {filters.Length} meshes, {triangles} triangles.");
    }

    // ---- Pictures ----

    /// <summary>The minimap: the map area from straight above, the warehouse (north) at the top. No clouds.</summary>
    private static Texture2D RenderAirportMap(Rect area)
    {
        List<Renderer> hidden = HideAirportSky();
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;
        int height = 460;
        int width = Mathf.RoundToInt(height * area.width / area.height);
        // In batch mode the first picture taken in a newly built scene comes out in the wrong colours; the second is right.
        Texture2D picture = null;
        for (int take = 0; take < 2; take++)
        {
            GameObject cameraObject = new GameObject("MapCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = area.height * 0.5f;
            camera.aspect = area.width / area.height;
            camera.transform.SetPositionAndRotation(new Vector3(area.center.x, 80f, area.center.y), Quaternion.LookRotation(Vector3.down, Vector3.back));
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 140f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.66f, 0.66f);
            camera.cullingMask = ~((1 << GameLayers.ViewModel) | (1 << GameLayers.Preview));
            picture = RenderToPng(camera, width, height, AirportMapPath);
            Object.DestroyImmediate(cameraObject);
        }
        RenderSettings.fog = fog;
        foreach (Renderer renderer in hidden) renderer.enabled = true;

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AirportMapPath);
        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AirportMapPath);
    }

    private static List<Renderer> HideAirportSky()
    {
        List<Renderer> hidden = new List<Renderer>();
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!renderer.enabled || !(renderer.name.StartsWith("SkyCloud", StringComparison.Ordinal) || renderer.name.StartsWith("AirportHorizon_PaintedDistrict", StringComparison.Ordinal))) continue;
            renderer.enabled = false;
            hidden.Add(renderer);
        }
        return hidden;
    }

    /// <summary>
    /// The level select card: the freight area from the south-west over the apron, the way her overview is drawn, with the
    /// opening cases on the floor and three on their way down B03 (her beltInProgress), only for the picture.
    /// </summary>
    private static void RenderAirportCard(GameObject environment, LevelLayout layout, AirportLayoutFile plan, Func<string, Transform> mark)
    {
        Transform props = Group("CardProps", null);
        foreach (KindSpots kind in layout.kindSpots)
            foreach (Transform spot in kind.spots)
            {
                CargoItem item = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Cargo_" + kind.kind + ".prefab").GetComponent<CargoItem>();
                PlaceStillCargo(kind.kind, props, spot.position - Vector3.up * (item.HalfExtents.y + 0.03f), spot.rotation.eulerAngles.y);
            }
        if (plan.beltInProgress != null)
            foreach (AirportOnBelt onBelt in plan.beltInProgress)
                PlaceStillCargo(ParseKind(onBelt.kind), props, FromBlender(onBelt.blenderXYZ[0], onBelt.blenderXYZ[1], onBelt.blenderXYZ[2]), 0f);

        GameObject cameraObject = new GameObject("CardCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 34f;
        camera.aspect = 928f / 500f;
        Vector3 look = FromBlender(17f, 14f, 2.5f);
        camera.transform.position = FromBlender(-6f, -30f, 27f);
        camera.transform.LookAt(look, Vector3.up);
        camera.nearClipPlane = 0.5f;
        camera.farClipPlane = 700f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = ~((1 << GameLayers.ViewModel) | (1 << GameLayers.Preview));
        RenderToPng(camera, 928, 500, $"{UiDir}/{AirportCardName}.png");
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(props.gameObject);
        UiSprite(AirportCardName, mipmaps: true);
    }
}
