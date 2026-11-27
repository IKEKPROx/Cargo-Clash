using System.Collections.Generic;
using System.IO;
using System.Linq;
using CargoClash;
using CargoClash.RiverV10;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The rainy river level, Astra's 独立建模/雨天河流关卡_v16. Her Unity package's models, materials and shaders are copied
/// into Assets/CargoClash_RiverV16, Assets/CargoClash_RiverV10 and Assets/CargoClash_ImpactedBedV12 with her GUIDs, so a
/// newer delivery of hers imports over them. This builds the playable scene around them: collision, deep water that only
/// the fords cross, the truck's bed for the helicopter, the catcher and the planks, spawns, the bots' map, rain, the
/// minimap and the level card. Her own preview builder and her merged meshes aren't used.
/// </summary>
public static partial class CargoClashBuilder
{
    public const string RiverSceneName = "LevelRiver";
    private const string RiverScenePath = SceneDir + "/" + RiverSceneName + ".unity";
    private const string RiverArt = "Assets/CargoClash_RiverV16";
    private const string RiverShared = "Assets/CargoClash_RiverV10";
    private const string RiverEnvironmentModel = RiverArt + "/Models/River16_Environment.fbx";
    private const string RiverWaterModel = RiverArt + "/Models/River16_Water.fbx";
    private const string RiverPlankModel = RiverArt + "/Models/River16_PickupPlank.fbx";
    private const string RiverCollisionModel = RiverShared + "/Models/River10_CollisionProposals.fbx";
    private const string RiverTruckPrefab = "Assets/CargoClash_ImpactedBedV12/Prefabs/River12_TruckPlaced.prefab";
    private const string RiverWeatherPrefab = RiverArt + "/Prefabs/RiverV10_LocalWeather.prefab";
    private const string RiverFlowAsset = RiverArt + "/Data/RiverFlowPath.asset";
    private const string RiverTreeLods = RiverArt + "/Data/tree_lod_bindings.json";
    private const string RiverMapPath = TextureDir + "/River_Map.png";
    // RIVER FLATS's card on the level select.
    private const string RiverCardName = "Card_River";
    private const float RiverHalfSeconds = 180f;

    /// <summary>Inside her boundary fence, whose posts stand at x ±29.6 and z -20.5 and 24.5.</summary>
    private static readonly Rect RiverField = new Rect(-30.3f, -21.2f, 60.6f, 46.4f);

    /// <summary>
    /// Her two fords (Ford_West_Short and Ford_East_Long in River_16_DetailedLandscape.blend; only their markers are in the
    /// FBX), where the riverbed is shallow enough to wade: short in the west, long in the east.
    /// </summary>
    private static readonly Vector3 FordWestSize = new Vector3(3.8f, 4f, 5.4f);
    private static readonly Vector3 FordEastSize = new Vector3(3.7f, 4f, 9.3f);

    [System.Serializable] private class TreeLodPart { public string high; public string low; }
    [System.Serializable] private class TreeLodEntry { public string root; public TreeLodPart[] parts; }
    [System.Serializable] private class TreeLodManifest { public TreeLodEntry[] trees; }

    // ---- Her files ----

    /// <summary>
    /// No cameras or lights from her files, and every material she names mapped onto her material of that name. The big
    /// environment isn't kept readable: the player bakes its rock colliders at build time (bakeCollisionMeshes).
    /// </summary>
    private static void PrepareRiverModel(string path, bool readable)
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
        bool changed = importer.importCameras || importer.importLights || importer.isReadable != readable || importer.importAnimation;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.isReadable = readable;

        Dictionary<AssetImporter.SourceAssetIdentifier, Object> mapped = importer.GetExternalObjectMap();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is Material embedded)) continue;
            Material hers = FindRiverMaterial(embedded.name);
            if (hers == null) continue;
            AssetImporter.SourceAssetIdentifier id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name);
            if (mapped.TryGetValue(id, out Object current) && current == hers) continue;
            importer.AddRemap(id, hers);
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    private static Material FindRiverMaterial(string name)
    {
        foreach (string folder in new[] { RiverArt + "/Materials", "Assets/CargoClash_ImpactedBedV12/Materials" })
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{name}.mat");
            if (material != null) return material;
        }
        return null;
    }

    // ---- The plank ----

    /// <summary>
    /// Her loose plank (River16_PickupPlank): carried like cargo from the huts to the catcher. It lies flat, as in her huts,
    /// with its length along Z. It's worth nothing and can't be smashed or rescued.
    /// </summary>
    private static CargoDefinition BuildPlank()
    {
        PrepareRiverModel(RiverPlankModel, false);
        CargoDefinition definition = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Plank.asset");
        definition.kind = CargoKind.Plank;
        definition.size = CargoSize.Small;
        definition.value = 0;
        definition.smashSeconds = 1f;
        definition.carrySpeedMultiplier = 0.85f;
        definition.requiredCarriers = 1;
        definition.fullCount = 0;
        definition.coCarryHeight = 0f;
        definition.coCarryReach = 0f;
        definition.inspectState = "";
        definition.inspectOnGround = false;
        definition.inspect = default;
        definition.piecesPrefab = null;
        definition.chipColor = new Color(0.72f, 0.55f, 0.36f);
        definition.breakSound = "crack";

        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RiverPlankModel));
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Object.DestroyImmediate(model.transform.Find("GripSocket")?.gameObject);
        Bounds bounds = RendererBounds(model);
        GameObject root = CargoRoot("Plank", bounds.extents, 2f, definition, out Transform visual);
        model.name = "Model";
        model.transform.SetParent(visual, false);
        model.transform.localPosition = -bounds.center;

        SerializedObject item = new SerializedObject(root.GetComponent<CargoItem>());
        SetObjects(item.FindProperty("damageBodies"), new Object[] { model });
        SetObjects(item.FindProperty("lids"), new Object[0]);
        SetObjects(item.FindProperty("slots"), new Object[0]);
        item.ApplyModifiedPropertiesWithoutUndo();
        SetLayerRecursively(root, GameLayers.Cargo);
        definition.prefab = SavePrefab(root, "Plank");
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    // ---- The scene ----

    private static void BuildRiverScene(CargoDefinition[] cargo)
    {
        PrepareRiverModel(RiverEnvironmentModel, false);
        PrepareRiverModel(RiverWaterModel, false);
        PrepareRiverModel(RiverCollisionModel, true);
        PlayerSettings.bakeCollisionMeshes = true;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddOvercast();
        BuildPlayerCamera();
        Camera camera = Object.FindAnyObjectByType<FollowCamera>().view;
        // Her water reads the depth behind it to fade into the banks.
        UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RiverSky;
        camera.farClipPlane = 260f;

        GameObject levelObject = new GameObject("RiverLevel");
        RiverLevel level = levelObject.AddComponent<RiverLevel>();

        GameObject environment = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiverEnvironmentModel), levelObject.transform);
        environment.name = "Environment";
        GameObject water = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiverWaterModel), levelObject.transform);
        water.name = "Water";
        foreach (Renderer renderer in water.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
        AddTreeLods(environment);

        Transform collision = Group("Collision", levelObject.transform);
        AddRiverColliders(environment, collision);
        Transform truckRoot = AddRiverTruck(levelObject.transform, collision, out Transform bedPivot, out Transform zone, out Vector2 zoneHalf);

        // The water: where it runs, the fords, and the walls of deep water between them.
        RiverFlowPath flow = AssetDatabase.LoadAssetAtPath<RiverFlowPath>(RiverFlowAsset);
        level.flowPath = flow;
        level.flowRoot = levelObject.transform;
        level.fords = new[]
        {
            new Bounds(Marker(environment, "Ford_West").position, FordWestSize),
            new Bounds(Marker(environment, "Ford_East").position, FordEastSize),
        };
        level.field = RiverField;
        Transform walls = Group("PlayerWalls", levelObject.transform);
        AddDeepWater(walls, flow, levelObject.transform, level.fords);
        AddFieldEdge(walls);

        level.catcher = SetUpCatcher(environment, collision);
        level.plankSpots = PlankSpots(environment, levelObject.transform);

        // Her references component, which the rain reads the river from.
        RiverLevelReferences references = levelObject.AddComponent<RiverLevelReferences>();
        references.flowPath = flow;
        references.catcher = level.catcher;
        references.recoveryBed = bedPivot;
        references.westFord = Marker(environment, "Ford_West");
        references.eastFord = Marker(environment, "Ford_East");
        references.downstreamCatcher = level.catcher.transform;
        references.suggestedHalfSeconds = (int)RiverHalfSeconds;
        AddRain(camera, references, levelObject, water);

        LevelLayout layout = BuildRiverLayout(environment, cargo, zone, zoneHalf, bedPivot);

        BakeRiverNavMesh(level);
        CheckRiverSpawns(layout);

        BuildExtraction(bedPivot);
        SetRiverHelicopterPath(bedPivot);
        miniMapSize = new Vector2(300f, 236f);
        BuildHud();
        miniMapSize = DefaultMiniMapSize;
        GameObject events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
        events.transform.SetAsLastSibling();

        ReportRiverSize(environment);
        EditorSceneManager.SaveScene(scene, RiverScenePath);

        layout.mapPicture = RenderRiverMap(camera);
        RenderRiverCard();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, RiverScenePath);
    }

    private static readonly Color RiverSky = new Color(0.48f, 0.56f, 0.62f);

    /// <summary>
    /// Her overcast light (River16PreviewBuilder.SetupLights). No fog, as in her preview: her three shaders work the fog
    /// out from the pixel's depth-buffer value instead of its clip-space depth (RiverWeatherCommon.hlsl,
    /// ComputeFogFactor(clip.z) in the fragment stage), so with fog on, everything of hers turns the fog's colour.
    /// </summary>
    private static void AddOvercast()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.68f, 0.75f, 0.79f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.55f, 0.47f);
        RenderSettings.ambientGroundColor = new Color(0.27f, 0.28f, 0.23f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        GameObject sun = new GameObject("Overcast key");
        sun.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.94f, 0.84f);
        light.intensity = 0.55f;
        light.shadows = LightShadows.Soft;
        light.shadowBias = 0.03f;
        light.shadowNormalBias = 0.08f;
        RenderSettings.sun = light;
    }

    private static Transform Marker(GameObject environment, string name)
    {
        Transform found = environment.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (found == null) throw new System.Exception($"[Builder] Astra's river has no {name}.");
        return found;
    }

    /// <summary>
    /// Her near trees go to her far trees' meshes when small on screen (tree_lod_bindings.json), and every tree is culled
    /// when tiny. Leaves keep her imported normals.
    /// </summary>
    private static void AddTreeLods(GameObject environment)
    {
        TreeLodManifest manifest = JsonUtility.FromJson<TreeLodManifest>(File.ReadAllText(RiverTreeLods));
        Dictionary<string, TreeLodEntry> bindings = manifest.trees.ToDictionary(e => e.root);
        Dictionary<string, MeshFilter> byName = new Dictionary<string, MeshFilter>();
        foreach (MeshFilter filter in environment.GetComponentsInChildren<MeshFilter>(true)) byName[filter.name] = filter;

        int trees = 0;
        int detailed = 0;
        foreach (Transform root in environment.GetComponentsInChildren<Transform>(true))
        {
            if (root.GetComponent<MeshFilter>() != null) continue;
            if (!root.name.StartsWith("Tree_") && !root.name.StartsWith("DistantTree") && !root.name.StartsWith("V13_Woodland_")) continue;
            MeshRenderer[] high = root.GetComponentsInChildren<MeshRenderer>();
            if (high.Length == 0) continue;
            LODGroup group = root.gameObject.AddComponent<LODGroup>();
            List<Renderer> low = new List<Renderer>();
            if (bindings.TryGetValue(root.name, out TreeLodEntry binding))
            {
                foreach (TreeLodPart part in binding.parts)
                {
                    if (!byName.TryGetValue(part.high, out MeshFilter near) || !byName.TryGetValue(part.low, out MeshFilter far)) continue;
                    GameObject lod = new GameObject(near.name + "_LOD1");
                    lod.transform.SetParent(root, false);
                    lod.transform.localPosition = near.transform.localPosition;
                    lod.transform.localRotation = near.transform.localRotation;
                    lod.transform.localScale = near.transform.localScale;
                    lod.AddComponent<MeshFilter>().sharedMesh = far.sharedMesh;
                    MeshRenderer renderer = lod.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = near.GetComponent<MeshRenderer>().sharedMaterials;
                    low.Add(renderer);
                }
            }
            if (low.Count == high.Length)
            {
                group.SetLODs(new[] { new LOD(0.24f, high), new LOD(0.025f, low.ToArray()) });
                detailed++;
            }
            else group.SetLODs(new[] { new LOD(0.02f, high) });
            group.RecalculateBounds();
            trees++;
        }
        Debug.Log($"[Builder] River trees: {trees}, {detailed} with a far mesh.");
    }

    /// <summary>
    /// Her collision proposal (River10_CollisionProposals: the ground and the huts' walls), her rocks, boulders, buckled
    /// road and the ground beyond the fence as mesh colliders, and boxes for the fences and the near trees' trunks.
    /// All on the Level layer, as separate objects, so her models stay untouched.
    /// </summary>
    private static void AddRiverColliders(GameObject environment, Transform collision)
    {
        GameObject proposals = AssetDatabase.LoadAssetAtPath<GameObject>(RiverCollisionModel);
        foreach (MeshFilter filter in proposals.GetComponentsInChildren<MeshFilter>(true))
            MeshColliderCopy(collision, filter, GameLayers.Level);

        int rocks = 0;
        int boxes = 0;
        foreach (MeshFilter filter in environment.GetComponentsInChildren<MeshFilter>(true))
        {
            string name = filter.name;
            if (name.StartsWith("RockCluster") || name.StartsWith("ImpactBoulder") || name.StartsWith("NaturalRelayBoulder") ||
                name.StartsWith("BuckledAsphalt") || name == "BeyondFence_Terrain")
            {
                MeshColliderCopy(collision, filter, GameLayers.Level);
                rocks++;
            }
            else if (name.StartsWith("BoundaryFence_") || name.StartsWith("RoadFence_Post") || name.StartsWith("RoadFence_Rail"))
            {
                GameObject box = new GameObject("Box_" + name);
                box.layer = GameLayers.Level;
                box.transform.SetParent(collision, false);
                box.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                box.transform.localScale = filter.transform.lossyScale;
                BoxCollider collider = box.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center;
                collider.size = filter.sharedMesh.bounds.size;
                boxes++;
            }
            else if (name.StartsWith("SlateTile") || name.StartsWith("RidgeCap"))
            {
                // The roofs only keep the rain out of the huts.
                MeshColliderCopy(collision, filter, GameLayers.RainOnly);
            }
        }

        // Her near trees have no collision. A trunk you can walk through looks wrong at eye height.
        foreach (Transform tree in environment.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tree_") && t.GetComponent<MeshFilter>() == null))
        {
            GameObject trunk = new GameObject("Trunk_" + tree.name);
            trunk.layer = GameLayers.Level;
            trunk.transform.SetParent(collision, false);
            trunk.transform.position = tree.position + Vector3.up * 1.5f;
            CapsuleCollider capsule = trunk.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 3f;
        }
        Debug.Log($"[Builder] River collision: {rocks} rock and ground meshes, {boxes} fence boxes.");
    }

    private static void MeshColliderCopy(Transform parent, MeshFilter filter, int layer)
    {
        GameObject copy = new GameObject("Mesh_" + filter.name);
        copy.layer = layer;
        copy.transform.SetParent(parent, false);
        copy.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
        copy.transform.localScale = filter.transform.lossyScale;
        copy.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
    }

    /// <summary>
    /// Her rain-crash truck with the dented bed (River12_TruckPlaced, V12). Her bed keeps Blender's axes, so the
    /// helicopter lifts a pivot in Unity's: up is up and the rear is +Z, as on the tutorial's truck, with her bed inside
    /// it. The colliders are the tutorial truck's, in the same frame (her truck keeps the old one's measurements).
    /// </summary>
    private static Transform AddRiverTruck(Transform parent, Transform collision, out Transform bedPivot, out Transform zone, out Vector2 zoneHalf)
    {
        GameObject truck = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiverTruckPrefab), parent);
        PrefabUtility.UnpackPrefabInstance(truck, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        truck.name = "TruckModel";
        SetLayerRecursively(truck, GameLayers.Outlined);

        Transform bed = truck.transform.Find("CargoBed");
        Transform[] anchors = { bed.Find("LiftAnchor_FL"), bed.Find("LiftAnchor_FR"), bed.Find("LiftAnchor_RL"), bed.Find("LiftAnchor_RR") };
        Vector3 front = (anchors[0].position + anchors[1].position - anchors[2].position - anchors[3].position) * 0.5f;
        front.y = 0f;
        Vector3 rear = -front.normalized;

        // The truck's own frame, at its root, for the colliders and the delivery zone.
        Transform frame = Group("Truck", parent);
        frame.SetPositionAndRotation(truck.transform.position, Quaternion.LookRotation(rear, Vector3.up));
        truck.transform.SetParent(frame, true);

        bedPivot = Group("BedPivot", frame);
        bedPivot.SetPositionAndRotation(bed.position, Quaternion.LookRotation(rear, Vector3.up));
        bed.SetParent(bedPivot, true);
        Transform bedOrigin = Group("BedOrigin", bedPivot);
        bedOrigin.localPosition = new Vector3(0f, 0.17f, 0f);

        Transform colliders = Group("Colliders", frame);
        AddBoxCollider(colliders, "Cab", new Vector3(0f, 2.3f, -2.55f), new Vector3(3.4f, 3.1f, 3.7f));
        AddBoxCollider(colliders, "Bed", new Vector3(0f, 2.1f, 1.69f), new Vector3(4f, 2f, 5.1f));
        AddBoxCollider(colliders, "Chassis", new Vector3(0f, 1f, 0f), new Vector3(5.8f, 2f, 8.7f));
        // Nobody walks on the truck: its top is left out of the bots' map, the ground round it isn't.
        colliders.gameObject.layer = GameLayers.Level;
        NavMeshModifierVolume roof = colliders.gameObject.AddComponent<NavMeshModifierVolume>();
        roof.center = new Vector3(0f, 3.4f, 0f);
        roof.size = new Vector3(7f, 3f, 10f);
        roof.area = 1;
        Transform loose = truck.transform.Find("LooseWheel");
        if (loose != null)
        {
            GameObject wheel = new GameObject("LooseWheel");
            wheel.layer = GameLayers.Level;
            wheel.transform.SetParent(collision, false);
            wheel.transform.position = loose.position;
            wheel.AddComponent<SphereCollider>().radius = 0.8f;
        }

        AddPuffs(frame, "EngineSmoke", new Vector3(-0.4f, 2.2f, -4.5f), 6f, 40, new Color(0.42f, 0.42f, 0.44f), new Color(0.68f, 0.68f, 0.7f), 0.4f, 0.8f, 1.1f, 1.8f);

        zone = Group("Zone", frame);
        zoneHalf = new Vector2(5f, 7f);
        Part(PrimitiveType.Cube, "ZoneFloor", zone, new Vector3(0f, 0.16f, 0f), new Vector3(zoneHalf.x * 2f, 0.01f, zoneHalf.y * 2f), ZoneMaterial());

        return frame;
    }

    /// <summary>
    /// Deep water stops people (PlayerBlock walls along the river) everywhere but the two fords. Cargo floats on through,
    /// and the bots' map leaves the water out.
    /// </summary>
    private static void AddDeepWater(Transform walls, RiverFlowPath flow, Transform root, Bounds[] fords)
    {
        Transform deep = Group("DeepWater", walls);

        int count = 0;
        for (int i = 0; i + 1 < flow.centres.Length; i++)
        {
            Vector3 a = root.TransformPoint(flow.centres[i]);
            Vector3 b = root.TransformPoint(flow.centres[i + 1]);
            Vector3 middle = (a + b) * 0.5f;
            if (InAnyFord(middle, fords)) continue;
            Vector3 along = b - a;
            along.y = 0f;
            float halfWidth = (flow.halfWidths[i] + flow.halfWidths[i + 1]) * 0.5f;
            GameObject wall = new GameObject("Deep" + i);
            wall.layer = GameLayers.PlayerBlock;
            wall.transform.SetParent(deep, false);
            wall.transform.SetPositionAndRotation(new Vector3(middle.x, middle.y + 0.9f, middle.z), Quaternion.LookRotation(along.normalized, Vector3.up));
            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(0.5f, halfWidth * 2f - 0.3f), 4f, along.magnitude + 0.12f);
            // The bots' map leaves the riverbed here out. The wall itself isn't in the map; the volume has to be on a
            // layer the map is made from, or it's ignored.
            GameObject hole = new GameObject("NotWalkable");
            hole.layer = GameLayers.Level;
            hole.transform.SetParent(wall.transform, false);
            NavMeshModifierVolume volume = hole.AddComponent<NavMeshModifierVolume>();
            volume.center = Vector3.zero;
            volume.size = box.size;
            volume.area = 1;
            count++;
        }
        Debug.Log($"[Builder] River deep water: {count} walls.");
    }

    private static bool InAnyFord(Vector3 point, Bounds[] fords)
    {
        foreach (Bounds ford in fords)
            if (point.x >= ford.min.x && point.x <= ford.max.x && point.z >= ford.min.z && point.z <= ford.max.z) return true;
        return false;
    }

    /// <summary>People stay inside the fence, even where the river and road leave the field. Cargo isn't held back.</summary>
    private static void AddFieldEdge(Transform walls)
    {
        Transform edge = Group("FieldEdge", walls);
        Rect field = RiverField;
        void Wall(string name, Vector3 center, Vector3 size)
        {
            GameObject wall = new GameObject(name);
            wall.layer = GameLayers.PlayerBlock;
            wall.transform.SetParent(edge, false);
            wall.transform.position = center;
            wall.AddComponent<BoxCollider>().size = size;
        }
        Wall("West", new Vector3(field.xMax + 0.5f, 2f, field.center.y), new Vector3(1f, 8f, field.height + 2f));
        Wall("East", new Vector3(field.xMin - 0.5f, 2f, field.center.y), new Vector3(1f, 8f, field.height + 2f));
        Wall("North", new Vector3(field.center.x, 2f, field.yMin - 0.5f), new Vector3(field.width + 2f, 8f, 1f));
        Wall("South", new Vector3(field.center.x, 2f, field.yMax + 0.5f), new Vector3(field.width + 2f, 8f, 1f));
    }

    /// <summary>
    /// Her catcher downstream (Downstream_Catcher_BUILT): ten slots, each with a fresh, a soaked and a cracked board. Her
    /// controller shows the right one; the blocker that stops cargo is a separate box per slot, on whenever a plank is in.
    /// </summary>
    private static CatcherVisualController SetUpCatcher(GameObject environment, Transform collision)
    {
        Transform catcher = Marker(environment, "Downstream_Catcher_BUILT");
        CatcherVisualController controller = catcher.gameObject.AddComponent<CatcherVisualController>();
        Transform blockers = Group("CatcherBoards", collision);
        Transform[] anchors = catcher.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("CatcherSlot_")).OrderBy(t => t.name).ToArray();
        List<CatcherVisualController.Slot> slots = new List<CatcherVisualController.Slot>();
        foreach (Transform anchor in anchors)
        {
            Transform fresh = anchor.Cast<Transform>().First(t => t.name.EndsWith("_Fresh"));
            Transform worn = anchor.Cast<Transform>().First(t => t.name.EndsWith("_Worn"));
            Transform critical = anchor.Cast<Transform>().First(t => t.name.EndsWith("_Critical"));
            MeshFilter board = fresh.GetComponentsInChildren<MeshFilter>(true).First(f => f.name.StartsWith("Board_Fresh"));
            GameObject blocker = new GameObject("Board_" + anchor.name);
            blocker.layer = GameLayers.Level;
            blocker.transform.SetParent(blockers, false);
            blocker.transform.SetPositionAndRotation(board.transform.position, board.transform.rotation);
            blocker.transform.localScale = board.transform.lossyScale;
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.center = board.sharedMesh.bounds.center;
            // Down to the riverbed and up out of the water, so nothing slips under or over it.
            box.size = board.sharedMesh.bounds.size;
            box.enabled = false;
            slots.Add(new CatcherVisualController.Slot { anchor = anchor, fresh = fresh.gameObject, worn = worn.gameObject, critical = critical.gameObject, blocker = box });
            fresh.gameObject.SetActive(false);
            worn.gameObject.SetActive(false);
            critical.gameObject.SetActive(false);
        }
        controller.slots = slots.ToArray();
        controller.animatePreview = false;
        Debug.Log($"[Builder] River catcher: {slots.Count} slots.");
        return controller;
    }

    /// <summary>Her plank spots in the huts, stacked flat. Her marker is at one end of a plank; this is its middle.</summary>
    private static Transform[] PlankSpots(GameObject environment, Transform parent)
    {
        Transform group = Group("PlankSpots", parent);
        List<Transform> spots = new List<Transform>();
        foreach (Transform marker in environment.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("WoodSpawn_")).OrderBy(t => t.name))
        {
            Vector3 forward = marker.forward;
            forward.y = 0f;
            forward.Normalize();
            Transform spot = Group(marker.name, group);
            spot.SetPositionAndRotation(marker.position + forward * 0.975f + Vector3.up * 0.03f, Quaternion.LookRotation(forward, Vector3.up));
            spots.Add(spot);
        }
        return spots.ToArray();
    }

    /// <summary>
    /// Her rain (RiverV10_LocalWeather), following the game camera: drops around wherever you look from, splashing on the
    /// ground, the roofs and the water, and ripples on the river and the puddles.
    /// </summary>
    private static void AddRain(Camera camera, RiverLevelReferences references, GameObject levelObject, GameObject water)
    {
        GameObject weatherObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiverWeatherPrefab));
        weatherObject.name = "Rain";
        RiverWeather weather = weatherObject.GetComponent<RiverWeather>();
        weather.targetCamera = camera;
        weather.precipitationAnchor = null;
        weather.level = references;
        weather.groundLayers = (1 << GameLayers.Level) | (1 << GameLayers.RainOnly);
        weather.rippleSurfaces = levelObject.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.sharedMaterials.Any(m => m != null && (m.shader.name.EndsWith("FlowWater") || m.name.Contains("Puddle")))).ToArray();
        Debug.Log($"[Builder] River rain: {weather.rippleSurfaces.Length} rippling surfaces.");
    }

    /// <summary>
    /// Spawns, cargo spots and the truck. Collectors start beside the truck on the south bank, Interrupters across the river
    /// on the north field. Every kind of cargo starts on her spots for it (42 at three a side), lifted by its half height.
    /// </summary>
    private static LevelLayout BuildRiverLayout(GameObject environment, CargoDefinition[] cargo, Transform zone, Vector2 zoneHalf, Transform bedPivot)
    {
        Transform layoutRoot = Group("Layout", null);
        LevelLayout layout = layoutRoot.gameObject.AddComponent<LevelLayout>();
        layout.halfSeconds = RiverHalfSeconds;
        Quaternion north = Quaternion.LookRotation(Vector3.back);
        Quaternion south = Quaternion.LookRotation(Vector3.forward);
        layout.collectorSpawns = GroundSpots(layoutRoot, "CollectorSpawn", north,
            new Vector3(6.5f, 0f, 9f), new Vector3(8.5f, 0f, 11f), new Vector3(10.5f, 0f, 9f));
        layout.interrupterSpawns = GroundSpots(layoutRoot, "InterrupterSpawn", south,
            new Vector3(6f, 0f, -17.5f), new Vector3(8f, 0f, -18.5f), new Vector3(10f, 0f, -17.5f));
        layout.smallSpots = new Transform[0];
        layout.mediumSpots = new Transform[0];
        layout.largeSpots = new Transform[0];

        List<KindSpots> kinds = new List<KindSpots>();
        foreach (CargoKind kind in new[] { CargoKind.Tobacco, CargoKind.Cash, CargoKind.Gold, CargoKind.Art })
        {
            CargoDefinition definition = cargo.First(d => d.kind == kind);
            float lift = definition.prefab.GetComponent<CargoItem>().HalfExtents.y + 0.03f;
            List<Transform> spots = new List<Transform>();
            foreach (Transform marker in environment.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith($"Spawn_{kind}_")).OrderBy(t => t.name))
            {
                // Her markers keep Blender's axes: their Y (Unity's up vector here) is the way the case faces.
                Vector3 facing = marker.up;
                facing.y = 0f;
                Transform spot = Group(marker.name.Replace("Spawn_", "Spot_"), layoutRoot);
                spot.SetPositionAndRotation(marker.position + Vector3.up * lift,
                    facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity);
                spots.Add(spot);
            }
            kinds.Add(new KindSpots { kind = kind, spots = spots.ToArray() });
        }
        layout.kindSpots = kinds.ToArray();

        layout.truckZone = zone;
        layout.truckZoneHalfSize = zoneHalf;
        layout.bedOrigin = bedPivot.Find("BedOrigin");
        layout.bedHalfSize = new Vector2(1.25f, 1.95f);
        layout.mapArea = new Rect(-30f, -21f, 60f, 46f);
        layout.mapUpsideDown = true;
        return layout;
    }

    /// <summary>Spots standing on the ground, wherever its height is.</summary>
    private static Transform[] GroundSpots(Transform parent, string name, Quaternion rotation, params Vector3[] positions)
    {
        Physics.SyncTransforms();
        Transform[] spots = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 at = positions[i];
            if (Physics.Raycast(new Vector3(at.x, 30f, at.z), Vector3.down, out RaycastHit hit, 60f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore))
                at.y = hit.point.y + 0.05f;
            spots[i] = Group($"{name}{i + 1}", parent);
            spots[i].SetPositionAndRotation(at, rotation);
        }
        return spots;
    }

    private static void BakeRiverNavMesh(RiverLevel level)
    {
        Physics.SyncTransforms();
        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << GameLayers.Level;
        surface.BuildNavMesh();
        string navPath = SceneDir + "/" + RiverSceneName + "_NavMesh.asset";
        AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
    }

    /// <summary>Every spawn is on the bots' map, and they can walk from each side to the truck, the far side and the huts.</summary>
    private static void CheckRiverSpawns(LevelLayout layout)
    {
        NavMeshTriangulation map = NavMesh.CalculateTriangulation();
        Debug.Log($"[Builder] River bots' map: {map.vertices.Length} vertices, {map.indices.Length / 3} triangles.");
        List<string> problems = new List<string>();

        // Nowhere in the deep water is on the map: the bots wade across at the fords like everyone else.
        RiverLevel river = Object.FindAnyObjectByType<RiverLevel>();
        int wet = 0;
        foreach (Vector3 centre in river.flowPath.centres)
        {
            Vector3 point = river.flowRoot.TransformPoint(centre);
            // Next to a ford the sample can reach the ford's own wading floor.
            bool besideFord = river.fords.Any(f => { Bounds near = f; near.Expand(new Vector3(2f, 0f, 2f)); return near.Contains(new Vector3(point.x, f.center.y, point.z)); });
            Rect inside = new Rect(river.field.xMin + 1f, river.field.yMin + 1f, river.field.width - 2f, river.field.height - 2f);
            if (besideFord || !inside.Contains(new Vector2(point.x, point.z))) continue;
            if (NavMesh.SamplePosition(point + Vector3.down * 0.3f, out NavMeshHit hit, 0.8f, NavMesh.AllAreas) && river.OverWater(hit.position, out float surface, out _, out _) && hit.position.y < surface)
            {
                wet++;
                Debug.Log($"[Builder] River: the bots' map reaches into deep water at {hit.position}");
            }
        }
        if (wet > 0) problems.Add($"{wet} spots in the deep water are on the bots' map");
        foreach (Transform spot in layout.collectorSpawns.Concat(layout.interrupterSpawns).Concat(layout.kindSpots.SelectMany(k => k.spots)))
        {
            if (!NavMesh.SamplePosition(spot.position, out NavMeshHit near, 4f, NavMesh.AllAreas))
            {
                problems.Add($"{spot.name} is off the map");
                continue;
            }
            // Where a bot carrying it would head for, as BotBrain does.
            Vector3 truck = layout.ClosestTruckZonePoint(spot.position);
            NavMeshPath path = new NavMeshPath();
            bool sampled = NavMesh.SamplePosition(truck, out NavMeshHit toTruck, 4f, NavMesh.AllAreas);
            bool calculated = sampled && NavMesh.CalculatePath(near.position, toTruck.position, NavMesh.AllAreas, path);
            // The nearest point can be on top of the rocks by the truck; ending in the delivery zone beside them is enough.
            bool arrives = calculated && path.corners.Length > 0 &&
                (path.status == NavMeshPathStatus.PathComplete || layout.InTruckZone(path.corners[path.corners.Length - 1]));
            if (!arrives)
                problems.Add($"{spot.name} can't walk to the truck (from {near.position} to {(sampled ? toTruck.position.ToString() : "nowhere")}, {path.status}, {path.corners.Length} corners{(path.corners.Length > 0 ? " ending " + path.corners[path.corners.Length - 1] : "")})");
        }
        if (problems.Count == 0) Debug.Log("[Builder] River spawns and cargo spots all reach the truck.");
        else Debug.LogWarning("[Builder] River layout: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The helicopter comes in over the road from the south, lifts the bed and leaves north over the river, watched from
    /// over the road to the south-east. The tutorial's settings are tuned for its own field.
    /// </summary>
    private static void SetRiverHelicopterPath(Transform bedPivot)
    {
        HelicopterExtraction extraction = Object.FindAnyObjectByType<HelicopterExtraction>();
        extraction.bedClips = new[]
        {
            bedPivot.InverseTransformPoint(bedPivot.Find("CargoBed/LiftAnchor_FL").position),
            bedPivot.InverseTransformPoint(bedPivot.Find("CargoBed/LiftAnchor_FR").position),
            bedPivot.InverseTransformPoint(bedPivot.Find("CargoBed/LiftAnchor_RL").position),
            bedPivot.InverseTransformPoint(bedPivot.Find("CargoBed/LiftAnchor_RR").position),
        };
        extraction.pathStart = new Vector3(-30f, 4f, 150f);
        extraction.pathControlA = new Vector3(-22f, 2f, 85f);
        extraction.pathControlB = new Vector3(-6f, 1f, 32f);
        extraction.leaveDirection = new Vector3(0.25f, 0f, -1f);
        extraction.shotOffset = new Vector3(-16f, 9f, 17f);
        extraction.shotLookHeight = 5.5f;
        Debug.Log($"[Builder] River bed clips: {string.Join(" ", extraction.bedClips.Select(c => c.ToString("F2")))}");
    }

    private static void ReportRiverSize(GameObject environment)
    {
        long triangles = 0;
        MeshFilter[] filters = environment.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
            if (filter.sharedMesh != null) triangles += filter.sharedMesh.GetIndexCount(0) / 3;
        Debug.Log($"[Builder] River scenery: {filters.Length} meshes, about {triangles} triangles in their first parts.");
    }

    // ---- Pictures ----

    /// <summary>The minimap: the field from straight above, -Z at the top as in her drawings (LevelLayout.mapUpsideDown).</summary>
    private static Texture2D RenderRiverMap(Camera gameCamera)
    {
        Rect area = new Rect(-30f, -21f, 60f, 46f);
        GameObject cameraObject = new GameObject("MapCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = area.height * 0.5f;
        camera.aspect = area.width / area.height;
        camera.transform.SetPositionAndRotation(new Vector3(area.center.x, 60f, area.center.y), Quaternion.LookRotation(Vector3.down, Vector3.back));
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 120f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RiverSky;
        camera.cullingMask = ~((1 << GameLayers.ViewModel) | (1 << GameLayers.Preview));
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;
        Texture2D picture = RenderToPng(camera, 600, 460, RiverMapPath);
        RenderSettings.fog = fog;
        Object.DestroyImmediate(cameraObject);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(RiverMapPath);
        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RiverMapPath);
    }

    /// <summary>The level select card: her hero view of the whole river, from high over the road.</summary>
    private static void RenderRiverCard()
    {
        GameObject cameraObject = new GameObject("CardCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 17f;
        camera.aspect = 928f / 500f;
        // Her "Hero" view (River16PreviewBuilder.SetView): from (12, 57, 65) toward the middle of the river.
        camera.transform.position = new Vector3(12f, 57f, 65f);
        camera.transform.LookAt(new Vector3(0f, 0f, 0.7f), Vector3.up);
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 250f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RiverSky;
        camera.cullingMask = ~((1 << GameLayers.ViewModel) | (1 << GameLayers.Preview));
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;
        RenderToPng(camera, 928, 500, $"{UiDir}/{RiverCardName}.png");
        RenderSettings.fog = fog;
        Object.DestroyImmediate(cameraObject);
    }

    private static Texture2D RenderToPng(Camera camera, int width, int height, string path)
    {
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        target.Create();
        UniversalAdditionalCameraData data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.requiresDepthOption = CameraOverrideOption.On;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        target.Release();
        Object.DestroyImmediate(target);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
