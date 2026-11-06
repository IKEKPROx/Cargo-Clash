using System.Collections.Generic;
using CargoClash;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Generates the first-version prefabs, data and scenes. Everything it makes is a normal asset that
/// can be edited by hand afterwards, but running it again overwrites those edits.
/// </summary>
public static class CargoClashBuilder
{
    private const string Root = "Assets/Game";
    private const string PrefabDir = Root + "/Prefabs";
    private const string MaterialDir = Root + "/Materials";
    private const string DataDir = Root + "/Data";
    private const string SceneDir = Root + "/Scenes";
    private const string ResourcesDir = Root + "/Resources";
    private const string TitleScenePath = "Assets/CargoRover/Scenes/CargoRover.unity";
    private const string LevelScenePath = SceneDir + "/Level01.unity";
    private const string InputPath = Root + "/Input/CargoClashInput.inputactions";

    private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.94f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.45f, 0.82f);
    private static readonly Color AccentColor = new Color(1f, 0.56f, 0.13f);

    [MenuItem("Tools/Cargo Clash/Build Game Content")]
    public static void BuildAll()
    {
        EnsureFolders();
        ConfigureProject();

        GameConfig config = LoadOrCreate<GameConfig>(ResourcesDir + "/GameConfig.asset");
        config.orangeMaterial = Mat("Team_Orange", new Color(1f, 0.5f, 0.1f));
        config.blueMaterial = Mat("Team_Blue", new Color(0.2f, 0.52f, 1f));
        config.particleMaterial = ParticleMaterial();
        config.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);

        config.cargo = BuildCargo();
        config.avatarPrefab = BuildAvatar();
        config.directorPrefab = BuildNetworkPrefab<MatchDirector>("MatchDirector");
        config.rosterPrefab = BuildNetworkPrefab<SessionRoster>("SessionRoster");
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        BuildNetworkRig();
        BuildLevelScene();
        BuildTitleScene();

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(TitleScenePath, true),
            new EditorBuildSettingsScene(LevelScenePath, true),
        };
        AssetDatabase.SaveAssets();
        ReportNetworkHashes(config);
        Debug.Log("Cargo Clash content built.");
    }

    private static void EnsureFolders()
    {
        foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes", "Resources", "Input" })
        {
            if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
        }
    }

    private static void ConfigureProject()
    {
        PlayerSettings.runInBackground = true;
        PlayerSettings.companyName = "COMP4122 Cargo Clash Team";
        PlayerSettings.productName = "Cargo Clash";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        EditorBuildSettings.RemoveConfigObject("com.unity.input.settings.actions");

        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");
        layers.GetArrayElementAtIndex(GameLayers.Player).stringValue = "Player";
        layers.GetArrayElementAtIndex(GameLayers.Cargo).stringValue = "Cargo";
        layers.GetArrayElementAtIndex(GameLayers.Level).stringValue = "Level";
        tags.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ReportNetworkHashes(GameConfig config)
    {
        List<GameObject> prefabs = new List<GameObject> { config.avatarPrefab, config.directorPrefab, config.rosterPrefab };
        foreach (CargoDefinition definition in config.cargo) prefabs.Add(definition.prefab);
        foreach (GameObject prefab in prefabs)
        {
            SerializedObject networkObject = new SerializedObject(prefab.GetComponent<NetworkObject>());
            uint hash = networkObject.FindProperty("GlobalObjectIdHash").uintValue;
            Debug.Log($"[Builder] {prefab.name} GlobalObjectIdHash={hash}");
            if (hash == 0) Debug.LogError($"[Builder] {prefab.name} has no network hash.");
        }
    }

    // ---- Assets ----

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static Material Mat(string name, Color color, float smoothness = 0.2f)
    {
        string path = MaterialDir + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material ZoneMaterial()
    {
        string path = MaterialDir + "/TruckZone.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.15f, 0.28f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material ParticleMaterial()
    {
        string path = MaterialDir + "/Particle.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static PhysicsMaterial BouncyMaterial()
    {
        string path = MaterialDir + "/CargoBounce.physicMaterial";
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial("CargoBounce");
            AssetDatabase.CreateAsset(material, path);
        }
        material.bounciness = 0.35f;
        material.dynamicFriction = 0.6f;
        material.staticFriction = 0.6f;
        material.bounceCombine = PhysicsMaterialCombine.Maximum;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- Primitive helpers ----

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, Vector3 rotation = default, bool keepCollider = false, int layer = 0)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localEulerAngles = rotation;
        part.transform.localScale = scale;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        part.layer = layer;
        if (!keepCollider) Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    private static Transform Group(string name, Transform parent, Vector3 position = default)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        group.transform.localPosition = position;
        return group.transform;
    }

    private static GameObject SavePrefab(GameObject source, string name)
    {
        string path = PrefabDir + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
        Object.DestroyImmediate(source);
        // Reimport so NetworkObject.OnValidate records the prefab's network hash.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // ---- Network prefabs ----

    private static GameObject BuildNetworkPrefab<T>(string name) where T : NetworkBehaviour
    {
        GameObject root = new GameObject(name);
        root.AddComponent<NetworkObject>();
        root.AddComponent<T>();
        return SavePrefab(root, name);
    }

    private static void BuildNetworkRig()
    {
        GameObject root = new GameObject("NetworkRig");
        NetworkManager manager = root.AddComponent<NetworkManager>();
        UnityTransport transport = root.AddComponent<UnityTransport>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = true,
            ConnectionApproval = false,
            ForceSamePrefabs = true,
            TickRate = 30,
        };
        root.AddComponent<RoomService>();
        root.AddComponent<GameAudio>();
        root.AddComponent<AudioListener>();
        PrefabUtility.SaveAsPrefabAsset(root, ResourcesDir + "/NetworkRig.prefab");
        Object.DestroyImmediate(root);
    }

    private static GameObject BuildAvatar()
    {
        Material black = Mat("Eye_Black", new Color(0.04f, 0.04f, 0.05f));
        Material white = Mat("Eye_White", Color.white);
        Material orange = Mat("Team_Orange", new Color(1f, 0.5f, 0.1f));
        Material shoes = Mat("Shoes", new Color(0.13f, 0.13f, 0.16f));
        Material hardHat = Mat("Hat_Collector", new Color(1f, 0.86f, 0.1f), 0.5f);
        Material darkHat = Mat("Hat_Interrupter", new Color(0.12f, 0.1f, 0.14f));
        Material red = Mat("Hat_Band", new Color(0.86f, 0.14f, 0.16f));
        Material wood = Mat("Hammer_Handle", new Color(0.55f, 0.35f, 0.18f));
        Material steel = Mat("Hammer_Head", new Color(0.45f, 0.47f, 0.52f), 0.6f);

        GameObject root = new GameObject("Avatar");
        root.layer = GameLayers.Player;
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.85f, 0f);
        controller.height = 1.7f;
        controller.radius = 0.45f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.05f;

        root.AddComponent<NetworkObject>();
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.SyncRotAngleX = false;
        networkTransform.SyncRotAngleZ = false;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
        networkTransform.Interpolate = true;

        root.AddComponent<PlayerAvatar>();
        CharacterVisual visual = root.AddComponent<CharacterVisual>();

        Transform pivot = Group("Pivot", root.transform);
        GameObject body = Part(PrimitiveType.Sphere, "Body", pivot, new Vector3(0f, 0.72f, 0f), new Vector3(1f, 1.2f, 0.95f), orange);
        GameObject handLeft = Part(PrimitiveType.Sphere, "HandL", pivot, new Vector3(-0.55f, 0.72f, 0.1f), Vector3.one * 0.26f, orange);
        GameObject handRight = Part(PrimitiveType.Sphere, "HandR", pivot, new Vector3(0.55f, 0.72f, 0.1f), Vector3.one * 0.26f, orange);
        Part(PrimitiveType.Sphere, "FootL", pivot, new Vector3(-0.22f, 0.1f, 0.05f), new Vector3(0.32f, 0.2f, 0.44f), shoes);
        Part(PrimitiveType.Sphere, "FootR", pivot, new Vector3(0.22f, 0.1f, 0.05f), new Vector3(0.32f, 0.2f, 0.44f), shoes);
        Part(PrimitiveType.Sphere, "EyeL", pivot, new Vector3(-0.2f, 1f, 0.38f), Vector3.one * 0.24f, white);
        Part(PrimitiveType.Sphere, "EyeR", pivot, new Vector3(0.2f, 1f, 0.38f), Vector3.one * 0.24f, white);
        Part(PrimitiveType.Sphere, "PupilL", pivot, new Vector3(-0.2f, 1f, 0.48f), Vector3.one * 0.12f, black);
        Part(PrimitiveType.Sphere, "PupilR", pivot, new Vector3(0.2f, 1f, 0.48f), Vector3.one * 0.12f, black);

        Transform hatCollector = Group("HatCollector", pivot);
        Part(PrimitiveType.Sphere, "Dome", hatCollector, new Vector3(0f, 1.3f, 0f), new Vector3(0.72f, 0.42f, 0.72f), hardHat);
        Part(PrimitiveType.Cylinder, "Brim", hatCollector, new Vector3(0f, 1.2f, 0.06f), new Vector3(0.86f, 0.025f, 0.9f), hardHat);

        Transform hatInterrupter = Group("HatInterrupter", pivot);
        Part(PrimitiveType.Cylinder, "Beanie", hatInterrupter, new Vector3(0f, 1.33f, 0f), new Vector3(0.68f, 0.14f, 0.68f), darkHat);
        Part(PrimitiveType.Cylinder, "Band", hatInterrupter, new Vector3(0f, 1.22f, 0f), new Vector3(0.72f, 0.05f, 0.72f), red);
        Part(PrimitiveType.Cube, "HornL", hatInterrupter, new Vector3(-0.25f, 1.52f, 0f), new Vector3(0.1f, 0.26f, 0.1f), red, new Vector3(0f, 0f, 22f));
        Part(PrimitiveType.Cube, "HornR", hatInterrupter, new Vector3(0.25f, 1.52f, 0f), new Vector3(0.1f, 0.26f, 0.1f), red, new Vector3(0f, 0f, -22f));
        hatInterrupter.gameObject.SetActive(false);

        Transform hammer = Group("Hammer", pivot, new Vector3(0.6f, 0.78f, 0.15f));
        Part(PrimitiveType.Cylinder, "Handle", hammer, new Vector3(0f, 0.4f, 0f), new Vector3(0.08f, 0.42f, 0.08f), wood);
        Part(PrimitiveType.Cube, "Head", hammer, new Vector3(0f, 0.85f, 0f), new Vector3(0.46f, 0.3f, 0.3f), steel);
        hammer.gameObject.SetActive(false);

        visual.pivot = pivot;
        visual.teamRenderers = new Renderer[]
        {
            body.GetComponent<Renderer>(), handLeft.GetComponent<Renderer>(), handRight.GetComponent<Renderer>(),
        };
        visual.allRenderers = pivot.GetComponentsInChildren<Renderer>(true);
        visual.collectorHat = hatCollector.gameObject;
        visual.interrupterHat = hatInterrupter.gameObject;
        visual.hammer = hammer;

        SetLayerRecursively(root, GameLayers.Player);
        return SavePrefab(root, "Avatar");
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }

    private static CargoDefinition[] BuildCargo()
    {
        CargoDefinition small = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Small.asset");
        small.size = CargoSize.Small;
        small.value = 10;
        small.smashSeconds = 2.5f;
        small.carrySpeedMultiplier = 0.9f;
        small.requiredCarriers = 1;
        small.fullCount = 12;

        CargoDefinition medium = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Medium.asset");
        medium.size = CargoSize.Medium;
        medium.value = 30;
        medium.smashSeconds = 7f;
        medium.carrySpeedMultiplier = 0.7f;
        medium.requiredCarriers = 1;
        medium.fullCount = 6;

        CargoDefinition large = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Large.asset");
        large.size = CargoSize.Large;
        large.value = 100;
        large.smashSeconds = 16f;
        large.carrySpeedMultiplier = 0.5f;
        large.requiredCarriers = 2;
        large.fullCount = 2;

        small.prefab = BuildSmallCargo(small);
        medium.prefab = BuildMediumCargo(medium);
        large.prefab = BuildLargeCargo(large);
        foreach (CargoDefinition definition in new[] { small, medium, large }) EditorUtility.SetDirty(definition);
        return new[] { small, medium, large };
    }

    private static GameObject BuildSmallCargo(CargoDefinition definition)
    {
        Material box = Mat("Cargo_Cardboard", new Color(0.82f, 0.66f, 0.42f));
        Material tape = Mat("Cargo_Tape", new Color(0.95f, 0.9f, 0.7f));
        Vector3 half = new Vector3(0.3f, 0.3f, 0.3f);
        GameObject root = CargoRoot("Cargo_Small", half, 1f, definition, out Transform visual);
        Part(PrimitiveType.Cube, "Box", visual, Vector3.zero, half * 2f, box);
        Part(PrimitiveType.Cube, "Tape", visual, new Vector3(0f, half.y + 0.005f, 0f), new Vector3(0.14f, 0.01f, half.z * 2.02f), tape);
        AddFace(visual, half);
        return SavePrefab(root, "Cargo_Small");
    }

    private static GameObject BuildMediumCargo(CargoDefinition definition)
    {
        Material crate = Mat("Cargo_Crate", new Color(0.2f, 0.66f, 0.56f));
        Material band = Mat("Cargo_Band", new Color(0.1f, 0.3f, 0.3f));
        Vector3 half = new Vector3(0.52f, 0.46f, 0.46f);
        GameObject root = CargoRoot("Cargo_Medium", half, 3f, definition, out Transform visual);
        Part(PrimitiveType.Cube, "Crate", visual, Vector3.zero, half * 2f, crate);
        Part(PrimitiveType.Cube, "BandL", visual, new Vector3(-0.3f, 0f, 0f), new Vector3(0.12f, half.y * 2.03f, half.z * 2.03f), band);
        Part(PrimitiveType.Cube, "BandR", visual, new Vector3(0.3f, 0f, 0f), new Vector3(0.12f, half.y * 2.03f, half.z * 2.03f), band);
        AddFace(visual, half);
        return SavePrefab(root, "Cargo_Medium");
    }

    private static GameObject BuildLargeCargo(CargoDefinition definition)
    {
        const float scale = 0.72f;
        Vector3 half = new Vector3(1.16f, 0.53f, 0.56f);
        GameObject root = CargoRoot("Cargo_Large", half, 8f, definition, out Transform visual);

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CargoRover/Prefabs/CargoBox.prefab");
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.transform.SetParent(visual, false);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = new Vector3(0f, -half.y, 0f);
        AddFace(visual, half);
        return SavePrefab(root, "Cargo_Large");
    }

    private static GameObject CargoRoot(string name, Vector3 half, float mass, CargoDefinition definition, out Transform visual)
    {
        GameObject root = new GameObject(name);
        root.layer = GameLayers.Cargo;
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = half * 2f;
        collider.sharedMaterial = BouncyMaterial();
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = 0.1f;
        body.angularDamping = 0.4f;

        root.AddComponent<NetworkObject>();
        NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
        root.AddComponent<NetworkRigidbody>();

        visual = Group("Visual", root.transform);
        CargoItem item = root.AddComponent<CargoItem>();
        SerializedObject serialized = new SerializedObject(item);
        serialized.FindProperty("definition").objectReferenceValue = definition;
        serialized.FindProperty("visual").objectReferenceValue = visual;
        serialized.FindProperty("halfExtents").vector3Value = half;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static void AddFace(Transform visual, Vector3 half)
    {
        Material white = Mat("Eye_White", Color.white);
        Material black = Mat("Eye_Black", new Color(0.04f, 0.04f, 0.05f));
        float eyeSize = Mathf.Min(half.x, half.y) * 0.42f;
        float eyeX = Mathf.Min(half.x * 0.42f, 0.3f);
        float faceZ = -half.z;
        float eyeY = half.y * 0.2f;
        Part(PrimitiveType.Sphere, "EyeL", visual, new Vector3(-eyeX, eyeY, faceZ), Vector3.one * eyeSize, white);
        Part(PrimitiveType.Sphere, "EyeR", visual, new Vector3(eyeX, eyeY, faceZ), Vector3.one * eyeSize, white);
        Part(PrimitiveType.Sphere, "PupilL", visual, new Vector3(-eyeX, eyeY, faceZ - eyeSize * 0.4f), Vector3.one * eyeSize * 0.5f, black);
        Part(PrimitiveType.Sphere, "PupilR", visual, new Vector3(eyeX, eyeY, faceZ - eyeSize * 0.4f), Vector3.one * eyeSize * 0.5f, black);
        Part(PrimitiveType.Cube, "Mouth", visual, new Vector3(0f, -half.y * 0.32f, faceZ), new Vector3(eyeSize * 1.3f, eyeSize * 0.3f, 0.02f), black);
    }

    // ---- Level ----

    private static GameObject BuildCrashedTruck()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CargoRover/Prefabs/Vehicle.prefab");
        GameObject root = new GameObject("TruckCrashed");

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        Object.DestroyImmediate(model.GetComponent<RoverTerrainAnimator>());
        model.transform.SetParent(root.transform, false);
        // One side has dropped into the ditch, so the bed tips toward the road and stays easy to see.
        model.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        model.transform.localEulerAngles = new Vector3(0f, 0f, -9f);
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic);

        Transform bed = Group("BedOrigin", model.transform, new Vector3(0f, 2.05f, 1.66f));
        bed.name = "BedOrigin";

        Transform colliders = Group("Colliders", root.transform);
        AddBoxCollider(colliders, "Cab", new Vector3(0f, 2.3f, -2.5f), new Vector3(3.4f, 2.8f, 3.6f));
        AddBoxCollider(colliders, "Bed", new Vector3(0f, 2.2f, 1.66f), new Vector3(3.9f, 2.2f, 5f));
        AddBoxCollider(colliders, "Chassis", new Vector3(0f, 1f, 0f), new Vector3(5.4f, 2f, 8.6f));

        Group("Zone", root.transform);
        return SavePrefab(root, "TruckCrashed");
    }

    private static void AddBoxCollider(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject item = new GameObject(name);
        item.transform.SetParent(parent, false);
        item.layer = GameLayers.Level;
        BoxCollider box = item.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    private static void BuildLevelScene()
    {
        Material grass = Mat("Level_Grass", new Color(0.42f, 0.68f, 0.3f));
        Material grassFar = Mat("Level_GrassFar", new Color(0.34f, 0.56f, 0.27f));
        Material road = Mat("Level_Road", new Color(0.3f, 0.31f, 0.34f));
        Material dirt = Mat("Level_Shoulder", new Color(0.66f, 0.56f, 0.38f));
        Material paint = Mat("Level_Paint", new Color(0.95f, 0.95f, 0.88f));
        Material skid = Mat("Level_Skid", new Color(0.13f, 0.13f, 0.14f));
        Material trunk = Mat("Tree_Trunk", new Color(0.42f, 0.28f, 0.16f));
        Material leaf = Mat("Tree_Leaf", new Color(0.2f, 0.52f, 0.24f));
        Material leafLight = Mat("Tree_LeafLight", new Color(0.3f, 0.62f, 0.26f));
        Material rock = Mat("Level_Rock", new Color(0.55f, 0.56f, 0.58f));
        Material fence = Mat("Level_Fence", new Color(0.78f, 0.62f, 0.4f));
        Material cone = Mat("Level_Cone", new Color(1f, 0.45f, 0.1f));
        Material zone = ZoneMaterial();

        GameObject truckPrefab = BuildCrashedTruck();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.66f, 0.69f, 0.74f);
        RenderSettings.skybox = null;

        GameObject sun = new GameObject("Sun");
        sun.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.color = new Color(1f, 0.97f, 0.9f);
        light.shadows = LightShadows.Soft;

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.55f, 0.75f, 0.9f);
        camera.farClipPlane = 220f;
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        FollowCamera follow = cameraObject.AddComponent<FollowCamera>();
        follow.fallbackFocus = new Vector3(0f, 0f, -20f);
        cameraObject.transform.position = follow.fallbackFocus + follow.offset;
        cameraObject.transform.rotation = Quaternion.LookRotation(-follow.offset);

        Transform environment = Group("Environment", null);
        Part(PrimitiveType.Cube, "Ground", environment, new Vector3(0f, -0.5f, 0f), new Vector3(46f, 1f, 98f), grass, default, true, GameLayers.Level);
        Part(PrimitiveType.Cube, "GroundFar", environment, new Vector3(0f, -0.55f, 0f), new Vector3(260f, 1f, 300f), grassFar);
        Part(PrimitiveType.Cube, "Road", environment, new Vector3(0f, 0.01f, 0f), new Vector3(10f, 0.02f, 98f), road);
        Part(PrimitiveType.Cube, "ShoulderL", environment, new Vector3(-5.8f, 0.006f, 0f), new Vector3(1.6f, 0.012f, 98f), dirt);
        Part(PrimitiveType.Cube, "ShoulderR", environment, new Vector3(5.8f, 0.006f, 0f), new Vector3(1.6f, 0.012f, 98f), dirt);
        for (int z = -46; z <= 46; z += 6)
            Part(PrimitiveType.Cube, "Dash", environment, new Vector3(0f, 0.024f, z), new Vector3(0.28f, 0.006f, 2.8f), paint);
        Part(PrimitiveType.Cube, "SkidA", environment, new Vector3(2.8f, 0.026f, -22f), new Vector3(0.5f, 0.006f, 15f), skid, new Vector3(0f, 15f, 0f));
        Part(PrimitiveType.Cube, "SkidB", environment, new Vector3(5.2f, 0.026f, -21.5f), new Vector3(0.5f, 0.006f, 15f), skid, new Vector3(0f, 15f, 0f));

        List<Vector4> obstacles = new List<Vector4>();
        Transform props = Group("Props", environment);
        AddWalls(environment);
        AddTrees(props, trunk, leaf, leafLight, obstacles);
        AddRocks(props, rock, obstacles);
        AddFences(props, fence, obstacles);

        Vector3 truckPosition = new Vector3(11f, 0f, -33f);
        const float truckYaw = 10f;
        GameObject truck = (GameObject)PrefabUtility.InstantiatePrefab(truckPrefab);
        truck.name = "Truck";
        truck.transform.SetPositionAndRotation(truckPosition, Quaternion.Euler(0f, truckYaw, 0f));
        obstacles.Add(new Vector4(truckPosition.x, 0f, truckPosition.z, 7f));

        Transform zoneTransform = truck.transform.Find("Zone");
        Vector2 zoneHalf = new Vector2(5f, 7f);
        Part(PrimitiveType.Cube, "ZoneFloor", zoneTransform, new Vector3(0f, 0.05f, 0f), new Vector3(zoneHalf.x * 2f, 0.01f, zoneHalf.y * 2f), zone);
        for (int i = 0; i < 3; i++)
        {
            Part(PrimitiveType.Cube, "ConeBase", props, new Vector3(4.6f, 0.03f, -28f + i * 4f), new Vector3(0.7f, 0.06f, 0.7f), cone);
            Part(PrimitiveType.Cylinder, "Cone", props, new Vector3(4.6f, 0.34f, -28f + i * 4f), new Vector3(0.34f, 0.32f, 0.34f), cone);
        }

        Transform layoutRoot = Group("Layout", null);
        LevelLayout layout = layoutRoot.gameObject.AddComponent<LevelLayout>();
        layout.collectorSpawns = Spots(layoutRoot, "CollectorSpawn", Quaternion.identity,
            new Vector3(2.5f, 0f, -36f), new Vector3(2.5f, 0f, -32f), new Vector3(2.5f, 0f, -28f));
        layout.interrupterSpawns = Spots(layoutRoot, "InterrupterSpawn", Quaternion.Euler(0f, 180f, 0f),
            new Vector3(-4f, 0f, 45f), new Vector3(0f, 0f, 45f), new Vector3(4f, 0f, 45f));

        System.Random random = new System.Random(4122);
        List<Vector3> taken = new List<Vector3>();
        layout.smallSpots = ScatterSpots(layoutRoot, "SmallSpot", 12, -14f, 4.5f, -44f, -13f, 0.6f, 2.2f, random, obstacles, taken);
        layout.mediumSpots = ScatterSpots(layoutRoot, "MediumSpot", 6, -17f, 17f, -9f, 8f, 0.8f, 5f, random, obstacles, taken);
        layout.largeSpots = Spots(layoutRoot, "LargeSpot", Quaternion.identity,
            new Vector3(-15f, 0.7f, 12f), new Vector3(16f, 0.7f, 16f));
        layout.truckZone = zoneTransform;
        layout.truckZoneHalfSize = zoneHalf;
        layout.bedOrigin = truck.transform.Find("Model/BedOrigin");
        layout.bedHalfSize = new Vector2(1.35f, 2f);

        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << GameLayers.Level;
        surface.BuildNavMesh();
        string navPath = SceneDir + "/Level01_NavMesh.asset";
        AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);

        BuildHud();

        GameObject events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        events.transform.SetAsLastSibling();

        EditorSceneManager.SaveScene(scene, LevelScenePath);
    }

    private static void AddWalls(Transform parent)
    {
        Transform walls = Group("Walls", parent);
        AddWall(walls, new Vector3(-23.5f, 3f, 0f), new Vector3(2f, 7f, 100f));
        AddWall(walls, new Vector3(23.5f, 3f, 0f), new Vector3(2f, 7f, 100f));
        AddWall(walls, new Vector3(0f, 3f, -50f), new Vector3(50f, 7f, 2f));
        AddWall(walls, new Vector3(0f, 3f, 50f), new Vector3(50f, 7f, 2f));
    }

    private static void AddWall(Transform parent, Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject("Wall");
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = position;
        wall.layer = GameLayers.Level;
        wall.AddComponent<BoxCollider>().size = size;
    }

    private static void AddTrees(Transform parent, Material trunk, Material leaf, Material leafLight, List<Vector4> obstacles)
    {
        Vector3[] positions =
        {
            new Vector3(-20f, 0f, -42f), new Vector3(-18.5f, 0f, -31f), new Vector3(-20.5f, 0f, -18f),
            new Vector3(-19f, 0f, -4f), new Vector3(-20.5f, 0f, 24f), new Vector3(-18f, 0f, 37f),
            new Vector3(20f, 0f, -44f), new Vector3(20.5f, 0f, -18f), new Vector3(19.5f, 0f, -5f),
            new Vector3(20.5f, 0f, 30f), new Vector3(18.5f, 0f, 42f), new Vector3(-10f, 0f, 30f),
            new Vector3(11f, 0f, 36f), new Vector3(-9f, 0f, -36f),
            new Vector3(-30f, 0f, -20f), new Vector3(-34f, 0f, 10f), new Vector3(-29f, 0f, 34f),
            new Vector3(30f, 0f, -24f), new Vector3(34f, 0f, 6f), new Vector3(29f, 0f, 30f),
        };
        for (int i = 0; i < positions.Length; i++)
        {
            Transform tree = Group("Tree", parent, positions[i]);
            float size = 0.9f + 0.45f * ((i * 37) % 10) / 10f;
            Part(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 1.2f * size, 0f), new Vector3(0.6f, 1.2f, 0.6f) * size, trunk);
            Part(PrimitiveType.Sphere, "Crown", tree, new Vector3(0f, 3.3f * size, 0f), Vector3.one * 3.6f * size, i % 2 == 0 ? leaf : leafLight);
            Part(PrimitiveType.Sphere, "CrownTop", tree, new Vector3(0.3f, 4.8f * size, 0.2f), Vector3.one * 2.2f * size, i % 2 == 0 ? leafLight : leaf);
            if (Mathf.Abs(positions[i].x) < 23f)
            {
                GameObject blocker = new GameObject("Blocker");
                blocker.transform.SetParent(tree, false);
                blocker.layer = GameLayers.Level;
                CapsuleCollider capsule = blocker.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 1.5f, 0f);
                capsule.height = 3f;
                capsule.radius = 0.7f * size;
                obstacles.Add(new Vector4(positions[i].x, 0f, positions[i].z, 1.6f));
            }
        }
    }

    private static void AddRocks(Transform parent, Material rock, List<Vector4> obstacles)
    {
        Vector4[] rocks =
        {
            new Vector4(-8f, 0f, 21f, 1.6f), new Vector4(8.5f, 0f, 26f, 1.9f), new Vector4(-12f, 0f, -23f, 1.5f),
            new Vector4(12f, 0f, 2f, 1.7f), new Vector4(-10f, 0f, -2f, 1.4f), new Vector4(9f, 0f, -12f, 1.3f),
        };
        foreach (Vector4 entry in rocks)
        {
            Vector3 position = new Vector3(entry.x, entry.w * 0.25f, entry.z);
            Vector3 scale = new Vector3(entry.w * 1.7f, entry.w, entry.w * 1.4f);
            Part(PrimitiveType.Sphere, "Rock", parent, position, scale, rock, new Vector3(0f, entry.x * 13f, 8f), true, GameLayers.Level);
            obstacles.Add(new Vector4(entry.x, 0f, entry.z, entry.w + 0.6f));
        }
    }

    private static void AddFences(Transform parent, Material fence, List<Vector4> obstacles)
    {
        AddFence(parent, fence, new Vector3(-7.5f, 0f, 30f), 12f, obstacles);
        AddFence(parent, fence, new Vector3(7.5f, 0f, 11f), 10f, obstacles);
        AddFence(parent, fence, new Vector3(-7.5f, 0f, -14f), 8f, obstacles);
    }

    private static void AddFence(Transform parent, Material fence, Vector3 center, float length, List<Vector4> obstacles)
    {
        Transform item = Group("Fence", parent, center);
        for (float z = -length * 0.5f; z <= length * 0.5f; z += 2f)
            Part(PrimitiveType.Cube, "Post", item, new Vector3(0f, 0.55f, z), new Vector3(0.18f, 1.1f, 0.18f), fence);
        Part(PrimitiveType.Cube, "RailTop", item, new Vector3(0f, 0.85f, 0f), new Vector3(0.1f, 0.14f, length), fence);
        Part(PrimitiveType.Cube, "RailLow", item, new Vector3(0f, 0.45f, 0f), new Vector3(0.1f, 0.14f, length), fence);

        GameObject blocker = new GameObject("Blocker");
        blocker.transform.SetParent(item, false);
        blocker.layer = GameLayers.Level;
        BoxCollider box = blocker.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(0.4f, 1.2f, length);
        for (float z = -length * 0.5f; z <= length * 0.5f; z += 2f)
            obstacles.Add(new Vector4(center.x, 0f, center.z + z, 1.5f));
    }

    private static Transform[] Spots(Transform parent, string prefix, Quaternion rotation, params Vector3[] positions)
    {
        Transform[] result = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Transform spot = Group($"{prefix}{i + 1}", parent, positions[i]);
            spot.rotation = rotation;
            result[i] = spot;
        }
        return result;
    }

    private static Transform[] ScatterSpots(Transform parent, string prefix, int count, float minX, float maxX, float minZ, float maxZ,
        float height, float spacing, System.Random random, List<Vector4> obstacles, List<Vector3> taken)
    {
        Transform[] result = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = Vector3.zero;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                position = new Vector3(
                    Mathf.Lerp(minX, maxX, (float)random.NextDouble()),
                    height,
                    Mathf.Lerp(minZ, maxZ, (float)random.NextDouble()));
                if (IsClear(position, spacing, obstacles, taken)) break;
            }
            taken.Add(position);
            Transform spot = Group($"{prefix}{i + 1}", parent, position);
            spot.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            result[i] = spot;
        }
        return result;
    }

    private static bool IsClear(Vector3 position, float spacing, List<Vector4> obstacles, List<Vector3> taken)
    {
        foreach (Vector4 obstacle in obstacles)
        {
            float dx = position.x - obstacle.x;
            float dz = position.z - obstacle.z;
            if (dx * dx + dz * dz < obstacle.w * obstacle.w) return false;
        }
        foreach (Vector3 other in taken)
        {
            if ((other - position).sqrMagnitude < spacing * spacing) return false;
        }
        return true;
    }

    // ---- UI helpers ----

    private static Canvas NewCanvas(string name, int order)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static RectTransform Rect(GameObject item, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Font BuiltinFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    private static Text Label(Transform parent, string name, string content, int fontSize, Color color, Vector2 anchor,
        Vector2 position, Vector2 size, TextAnchor alignment = TextAnchor.MiddleCenter, bool heavy = false, bool outline = false)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        Rect(item, parent, anchor, position, size);
        Text text = item.GetComponent<Text>();
        text.text = content;
        text.font = BuiltinFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        if (heavy) item.AddComponent<CargoClashSolidText>();
        else text.fontStyle = FontStyle.Bold;
        if (outline)
        {
            Outline line = item.AddComponent<Outline>();
            line.effectColor = new Color(0f, 0f, 0f, 0.9f);
            line.effectDistance = new Vector2(2f, -2f);
        }
        return text;
    }

    private static Image Panel(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Rect(item, parent, anchor, position, size);
        Image image = item.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Button Btn(Transform parent, string name, string content, Vector2 anchor, Vector2 position, Vector2 size,
        Color color, out Text label)
    {
        Image background = Panel(parent, name, color, anchor, position, size);
        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        button.colors = colors;
        label = Label(background.transform, "Label", content, 26, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        return button;
    }

    private static InputField CodeInput(Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        Image background = Panel(parent, "CodeInput", new Color(0.93f, 0.94f, 0.96f), anchor, position, size);
        Text placeholder = Label(background.transform, "Placeholder", "Room code", 24, new Color(0.45f, 0.48f, 0.53f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Text value = Label(background.transform, "Text", "", 26, new Color(0.08f, 0.1f, 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        value.supportRichText = false;
        value.horizontalOverflow = HorizontalWrapMode.Wrap;
        value.verticalOverflow = VerticalWrapMode.Truncate;
        InputField field = background.gameObject.AddComponent<InputField>();
        field.textComponent = value;
        field.placeholder = placeholder;
        field.characterLimit = 32;
        return field;
    }

    // ---- HUD ----

    private static void BuildHud()
    {
        Canvas canvas = NewCanvas("HudCanvas", 10);
        MatchHud hud = canvas.gameObject.AddComponent<MatchHud>();
        Transform root = canvas.transform;
        Vector2 top = new Vector2(0.5f, 1f);
        Vector2 bottomLeft = new Vector2(0f, 0f);
        Vector2 centre = new Vector2(0.5f, 0.5f);

        GameObject overlay = new GameObject("Overlays", typeof(RectTransform));
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.SetParent(root, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        hud.overlayRoot = overlayRect;
        hud.overlayFont = BuiltinFont();

        Panel(root, "TopBar", new Color(0.05f, 0.06f, 0.09f, 0.72f), top, new Vector2(0f, -62f), new Vector2(760f, 124f));
        hud.halfText = Label(root, "Half", "HALF 1", 22, new Color(0.8f, 0.85f, 0.9f), top, new Vector2(0f, -24f), new Vector2(200f, 30f));
        hud.timerText = Label(root, "Timer", "3:00", 64, Color.white, top, new Vector2(0f, -72f), new Vector2(240f, 80f), TextAnchor.MiddleCenter, true);
        hud.orangeScoreText = Label(root, "OrangeScore", "0", 62, Team.Orange.ToColor(), top, new Vector2(-250f, -56f), new Vector2(190f, 76f), TextAnchor.MiddleCenter, true);
        hud.blueScoreText = Label(root, "BlueScore", "0", 62, Team.Blue.ToColor(), top, new Vector2(250f, -56f), new Vector2(190f, 76f), TextAnchor.MiddleCenter, true);
        hud.orangeRoleText = Label(root, "OrangeRole", "COLLECTORS", 19, Team.Orange.ToColor(), top, new Vector2(-250f, -105f), new Vector2(220f, 26f));
        hud.blueRoleText = Label(root, "BlueRole", "INTERRUPTERS", 19, Team.Blue.ToColor(), top, new Vector2(250f, -105f), new Vector2(220f, 26f));

        hud.bannerText = Label(root, "Banner", "", 50, Color.white, centre, new Vector2(0f, 250f), new Vector2(1400f, 240f), TextAnchor.MiddleCenter, true, true);

        Panel(root, "RamBack", new Color(0.05f, 0.06f, 0.09f, 0.8f), bottomLeft, new Vector2(170f, 70f), new Vector2(280f, 46f));
        Image ramFill = Panel(root, "RamFill", Color.green, bottomLeft, new Vector2(170f, 70f), new Vector2(268f, 34f));
        ramFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        ramFill.type = Image.Type.Filled;
        ramFill.fillMethod = Image.FillMethod.Horizontal;
        hud.ramFill = ramFill;
        hud.ramLabel = Label(root, "RamLabel", "RAM READY", 22, Color.black, bottomLeft, new Vector2(170f, 70f), new Vector2(268f, 34f));
        Label(root, "KeysHint", GameText.KeysHint, 20, new Color(1f, 1f, 1f, 0.85f), new Vector2(0.5f, 0f), new Vector2(120f, 34f), new Vector2(1300f, 30f), TextAnchor.MiddleCenter, false, true);

        Image results = Panel(root, "ResultsPanel", PanelColor, centre, Vector2.zero, new Vector2(880f, 620f));
        Transform panel = results.transform;
        Vector2 panelTop = new Vector2(0.5f, 1f);
        hud.resultsPanel = results.gameObject;
        hud.resultsHeading = Label(panel, "Heading", "ORANGE WINS!", 72, AccentColor, panelTop, new Vector2(0f, -90f), new Vector2(820f, 100f), TextAnchor.MiddleCenter, true);
        hud.resultsScore = Label(panel, "Score", "0 : 0", 58, Color.white, panelTop, new Vector2(0f, -190f), new Vector2(820f, 80f), TextAnchor.MiddleCenter, true);
        hud.resultsTitles = Label(panel, "Titles", "", 30, new Color(1f, 0.93f, 0.6f), panelTop, new Vector2(0f, -330f), new Vector2(820f, 170f));
        hud.resultsWaiting = Label(panel, "Waiting", "Waiting for the host...", 24, new Color(0.8f, 0.85f, 0.9f), new Vector2(0.5f, 0f), new Vector2(-170f, 70f), new Vector2(380f, 50f));
        hud.againButton = Btn(panel, "Again", "PLAY AGAIN", new Vector2(0.5f, 0f), new Vector2(-170f, 70f), new Vector2(340f, 72f), ButtonColor, out hud.againLabel);
        hud.leaveButton = Btn(panel, "Leave", "MAIN MENU", new Vector2(0.5f, 0f), new Vector2(190f, 70f), new Vector2(320f, 72f), new Color(0.3f, 0.33f, 0.4f), out _);

        Image pause = Panel(root, "PausePanel", PanelColor, centre, Vector2.zero, new Vector2(560f, 300f));
        hud.pausePanel = pause.gameObject;
        Label(pause.transform, "Heading", "PAUSE MENU", 44, Color.white, panelTop, new Vector2(0f, -60f), new Vector2(500f, 60f), TextAnchor.MiddleCenter, true);
        hud.pauseResumeButton = Btn(pause.transform, "Resume", "KEEP PLAYING", centre, new Vector2(0f, -10f), new Vector2(400f, 64f), ButtonColor, out _);
        hud.pauseLeaveButton = Btn(pause.transform, "LeaveMatch", "LEAVE MATCH", centre, new Vector2(0f, -90f), new Vector2(400f, 64f), new Color(0.3f, 0.33f, 0.4f), out _);
    }

    // ---- Title ----

    private static void BuildTitleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);

        foreach (string stale in new[] { "GameMenuCanvas", "TitleFlow" })
        {
            GameObject found = GameObject.Find(stale);
            if (found != null) Object.DestroyImmediate(found);
        }

        // The preview terrain mesh was removed to keep the repo small. Chunks are generated in Play.
        GameObject previewTerrain = GameObject.Find("TestTerrain_ReplaceWithYourTerrain");
        if (previewTerrain != null)
        {
            previewTerrain.GetComponent<MeshFilter>().sharedMesh = null;
            previewTerrain.GetComponent<MeshCollider>().sharedMesh = null;
        }

        Canvas canvas = NewCanvas("GameMenuCanvas", 200);
        TitleFlow flow = new GameObject("TitleFlow").AddComponent<TitleFlow>();
        flow.startMenu = Object.FindAnyObjectByType<CargoClashStartMenu>();
        Vector2 centre = new Vector2(0.5f, 0.5f);

        Image menu = Panel(canvas.transform, "MenuPanel", PanelColor, centre, new Vector2(-540f, 0f), new Vector2(620f, 640f));
        flow.menuPanel = menu.gameObject;
        Transform menuRoot = menu.transform;
        Label(menuRoot, "Heading", "CARGO CLASH", 74, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(580f, 100f), TextAnchor.MiddleCenter, true);
        flow.practiceButton = Btn(menuRoot, "Practice", "PRACTICE WITH BOTS", centre, new Vector2(0f, 110f), new Vector2(480f, 76f), AccentColor, out _);
        flow.createButton = Btn(menuRoot, "Create", "CREATE ROOM", centre, new Vector2(0f, 15f), new Vector2(480f, 76f), ButtonColor, out _);
        flow.codeInput = CodeInput(menuRoot, centre, new Vector2(-100f, -85f), new Vector2(280f, 72f));
        flow.joinButton = Btn(menuRoot, "Join", "JOIN", centre, new Vector2(145f, -85f), new Vector2(190f, 72f), ButtonColor, out _);
        flow.statusText = Label(menuRoot, "Status", "", 24, new Color(1f, 0.85f, 0.45f), centre, new Vector2(0f, -190f), new Vector2(560f, 90f));
        flow.statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Label(menuRoot, "Keys", "WASD move   E pick up / drop   SPACE ram   F smash", 19, new Color(0.8f, 0.85f, 0.9f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(580f, 30f));

        Image lobby = Panel(canvas.transform, "LobbyPanel", PanelColor, centre, Vector2.zero, new Vector2(1040f, 720f));
        flow.lobbyPanel = lobby.gameObject;
        Transform lobbyRoot = lobby.transform;
        flow.roomCodeText = Label(lobbyRoot, "RoomCode", "ROOM CODE: ------", 48, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(980f, 70f), TextAnchor.MiddleCenter, true);

        Vector2 columnOrange = new Vector2(-250f, 135f);
        Vector2 columnBlue = new Vector2(250f, 135f);
        Label(lobbyRoot, "OrangeHeading", "ORANGE", 36, Team.Orange.ToColor(), centre, columnOrange + new Vector2(0f, 90f), new Vector2(440f, 50f), TextAnchor.MiddleCenter, true);
        Label(lobbyRoot, "BlueHeading", "BLUE", 36, Team.Blue.ToColor(), centre, columnBlue + new Vector2(0f, 90f), new Vector2(440f, 50f), TextAnchor.MiddleCenter, true);
        flow.orangeList = Label(lobbyRoot, "OrangeList", "", 26, Color.white, centre, columnOrange + new Vector2(0f, -10f), new Vector2(440f, 150f), TextAnchor.UpperCenter);
        flow.blueList = Label(lobbyRoot, "BlueList", "", 26, Color.white, centre, columnBlue + new Vector2(0f, -10f), new Vector2(440f, 150f), TextAnchor.UpperCenter);
        flow.orangeButton = Btn(lobbyRoot, "JoinOrange", "JOIN ORANGE", centre, new Vector2(-250f, -35f), new Vector2(380f, 64f), new Color(0.82f, 0.42f, 0.08f), out _);
        flow.blueButton = Btn(lobbyRoot, "JoinBlue", "JOIN BLUE", centre, new Vector2(250f, -35f), new Vector2(380f, 64f), new Color(0.14f, 0.42f, 0.82f), out _);

        flow.readyButton = Btn(lobbyRoot, "Ready", "READY", centre, new Vector2(-250f, -125f), new Vector2(380f, 72f), new Color(0.16f, 0.62f, 0.34f), out flow.readyLabel);
        flow.fillButton = Btn(lobbyRoot, "Fill", "BOTS FILL TEAMS: OFF", centre, new Vector2(250f, -125f), new Vector2(380f, 72f), new Color(0.3f, 0.33f, 0.4f), out flow.fillLabel);
        flow.startButton = Btn(lobbyRoot, "Start", "START GAME", centre, new Vector2(-250f, -215f), new Vector2(380f, 72f), AccentColor, out _);
        flow.leaveButton = Btn(lobbyRoot, "Leave", "LEAVE", centre, new Vector2(250f, -215f), new Vector2(380f, 72f), new Color(0.3f, 0.33f, 0.4f), out _);
        flow.lobbyHint = Label(lobbyRoot, "Hint", "", 22, new Color(0.8f, 0.85f, 0.9f), new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(980f, 34f));

        menu.gameObject.SetActive(false);
        lobby.gameObject.SetActive(false);

        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
