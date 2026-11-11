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
public static partial class CargoClashBuilder
{
    private const string Root = "Assets/Game";
    private const string PrefabDir = Root + "/Prefabs";
    private const string SceneryDir = PrefabDir + "/Scenery";
    private const string MaterialDir = Root + "/Materials";
    private const string TextureDir = Root + "/Textures";
    private const string DataDir = Root + "/Data";
    private const string SceneDir = Root + "/Scenes";
    private const string ResourcesDir = Root + "/Resources";
    private const string TitleScenePath = "Assets/CargoRover/Scenes/CargoRover.unity";
    private const string LevelScenePath = SceneDir + "/Level01.unity";
    private const string InputPath = Root + "/Input/CargoClashInput.inputactions";

    private static readonly Dictionary<string, GameObject> sceneryPrefabs = new Dictionary<string, GameObject>();

    // The teams' colours on clothes and tools. A little softer than pure orange and blue, to sit with Astra's palette.
    private static readonly Color TeamOrange = new Color(0.96f, 0.5f, 0.16f);
    private static readonly Color TeamBlue = new Color(0.22f, 0.5f, 0.95f);
    private static Dictionary<string, Material> modelMaterials;

    [MenuItem("Tools/Cargo Clash/Build Game Content")]
    public static void BuildAll()
    {
        modelMaterials = null;
        sceneryPrefabs.Clear();
        EnsureFolders();
        ConfigureProject();

        GameConfig config = LoadOrCreate<GameConfig>(ResourcesDir + "/GameConfig.asset");
        config.orangeMaterial = Mat("Team_Orange", TeamOrange);
        config.blueMaterial = Mat("Team_Blue", TeamBlue);
        config.particleMaterial = ParticleMaterial();
        config.puffMaterial = PuffMaterial();
        config.flashMaterial = FlashMaterial();
        config.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);

        config.cargo = BuildCargo();
        config.avatarPrefab = BuildAvatar();
        config.directorPrefab = BuildNetworkPrefab<MatchDirector>("MatchDirector");
        config.rosterPrefab = BuildNetworkPrefab<SessionRoster>("SessionRoster");
        config.levels = BuildLevelList();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        AddOutlines();
        BuildNetworkRig();
        BuildLevelScene();
        BuildTitleScene();

        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(TitleScenePath, true) };
        foreach (LevelInfo level in config.levels)
        {
            if (level.IsReady) scenes.Add(new EditorBuildSettingsScene($"{SceneDir}/{level.scene}.unity", true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        ReportNetworkHashes(config);
        Debug.Log("Cargo Clash content built.");
    }

    private static void EnsureFolders()
    {
        foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes", "Resources", "Input", "Models", "Textures", "UI" })
        {
            if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
        }
        if (!AssetDatabase.IsValidFolder(SceneryDir)) AssetDatabase.CreateFolder(PrefabDir, "Scenery");
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
        layers.GetArrayElementAtIndex(GameLayers.Outlined).stringValue = "Outlined";
        layers.GetArrayElementAtIndex(GameLayers.ViewModel).stringValue = "ViewModel";
        layers.GetArrayElementAtIndex(GameLayers.Debris).stringValue = "Debris";
        layers.GetArrayElementAtIndex(GameLayers.Preview).stringValue = "Preview";
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

    private static Material Mat(string name, Color color, float smoothness = 0.2f, float metallic = 0f)
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
        material.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// One of Astra's vehicle colours, as her palette.json gives it: an sRGB hex colour, roughness and
    /// metallic. URP's smoothness is one minus her roughness.
    /// </summary>
    private static Material FleetMat(string name, string hex, float roughness, float metallic)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return Mat(name, color, 1f - roughness, metallic);
    }

    /// <summary>A painted texture from Tools/blender/make_ground.py, with its import settings.</summary>
    private static Texture2D PaintedTexture(string name, TextureWrapMode wrap, bool hasAlpha = false)
    {
        string path = TextureDir + "/" + name + ".png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.maxTextureSize = 4096;
        importer.wrapMode = wrap;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = hasAlpha;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material GroundMat(string name, string textureName, TextureWrapMode wrap)
    {
        Material material = Mat(name, Color.white, 0f);
        material.SetTexture("_BaseMap", PaintedTexture(textureName, wrap));
        return material;
    }

    private static Material ZoneMaterial()
    {
        Material material = TransparentMat("TruckZone", Color.white);
        material.SetTexture("_BaseMap", PaintedTexture("TruckZone", TextureWrapMode.Clamp, true));
        return material;
    }

    private static Material TransparentMat(string name, Color color)
    {
        string path = MaterialDir + "/" + name + ".mat";
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
        material.SetColor("_BaseColor", color);
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

    /// <summary>The comic star that flashes where a hit lands. See-through, and tinted by each particle's colour.</summary>
    private static Material FlashMaterial()
    {
        string path = MaterialDir + "/Fx_Flash.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", UiSprite("UI_Pow").texture);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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

    /// <summary>
    /// Astra's people come in at this size, so the medium one stands about as tall as the old worker.
    /// It has to match GAME_SCALE in Tools/blender/make_crew.py, which sizes cargo held in the arms by it.
    /// </summary>
    private const float CrewScale = 0.84f;

    /// <summary>Each body type and how fast its legs go round: shorter legs take quicker steps.</summary>
    private static readonly (BodyType body, float runRate)[] CrewBodies =
    {
        (BodyType.Short, 3.6f), (BodyType.Medium, 2.8f), (BodyType.Tall, 2.45f),
    };

    /// <summary>
    /// Astra's three body types from Tools/blender/make_crew.py, each with both sets of clothes, its own
    /// skeleton and clips. CharacterVisual switches on the one the host gave this player.
    /// </summary>
    private static GameObject BuildAvatar()
    {
        GameObject root = new GameObject("Avatar");
        root.layer = GameLayers.Player;
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.85f, 0f);
        controller.height = 1.7f;
        controller.radius = 0.45f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.05f;
        // The default ignores very small moves, which stops a slow carrier at high frame rates.
        controller.minMoveDistance = 0f;

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
        visual.pivot = pivot;
        List<CrewModel> crews = new List<CrewModel>();
        foreach ((BodyType body, float runRate) in CrewBodies)
        {
            CrewModel crew = NewCrewModel(body, runRate, pivot, true);
            crew.gameObject.SetActive(body == BodyType.Medium);
            crews.Add(crew);
        }
        visual.crews = crews.ToArray();

        SetLayerRecursively(root, GameLayers.Player);
        return SavePrefab(root, "Avatar");
    }

    /// <summary>
    /// One body type, dressed in the default look. The first time (for the avatar) it sets up the import and
    /// builds the Animator; the crew page's preview then reuses that Animator.
    /// </summary>
    private static CrewModel NewCrewModel(BodyType body, float runRate, Transform parent, bool firstTime)
    {
        string modelPath = $"{Root}/Models/Crew_{body}.fbx";
        if (firstTime) CargoClashCharacterAnimator.ConfigureImport(modelPath, CrewScale, true);
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), parent);
        model.name = "Crew_" + body;
        ApplyMaterials(model);

        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = firstTime
            ? CargoClashCharacterAnimator.BuildController(modelPath, model.transform, "Crew_" + body)
            : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{Root}/Animation/Crew_{body}.controller");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Dictionary<string, Material> materials = ModelMaterials();
        CrewModel crew = model.AddComponent<CrewModel>();
        crew.body = body;
        crew.animator = animator;
        crew.runRate = runRate;
        crew.porter = FindInModel(model, "Porter").GetComponent<SkinnedMeshRenderer>();
        crew.wrecker = FindInModel(model, "Wrecker").GetComponent<SkinnedMeshRenderer>();
        crew.porterHats = new GameObject[CrewLook.Hats.Length];
        crew.wreckerHats = new GameObject[CrewLook.Hats.Length];
        for (int i = 0; i < CrewLook.Hats.Length; i++)
        {
            crew.porterHats[i] = FindInModel(model, "Porter_Hat_" + CrewLook.Hats[i]).gameObject;
            crew.wreckerHats[i] = FindInModel(model, "Wrecker_Hat_" + CrewLook.Hats[i]).gameObject;
        }
        crew.porterGlasses = new SkinnedMeshRenderer[CrewLook.GlassesStyles.Length];
        crew.wreckerGlasses = new SkinnedMeshRenderer[CrewLook.GlassesStyles.Length];
        for (int i = 0; i < CrewLook.GlassesStyles.Length; i++)
        {
            crew.porterGlasses[i] = FindInModel(model, "Porter_Glasses_" + CrewLook.GlassesStyles[i]).GetComponent<SkinnedMeshRenderer>();
            crew.wreckerGlasses[i] = FindInModel(model, "Wrecker_Glasses_" + CrewLook.GlassesStyles[i]).GetComponent<SkinnedMeshRenderer>();
        }
        crew.porterSleeve = materials["Porter_Shirt"];
        // The medium wrecker wears a vest over a shirt with rolled sleeves; the others wear jackets.
        crew.wreckerSleeve = body == BodyType.Medium ? materials["Wrecker_Shirt"] : materials["Wrecker_Jacket"];
        crew.hammer = FindInModel(model, "Hammer").gameObject;
        crew.vacuum = FindInModel(model, "Vacuum").gameObject;
        crew.dizzyStars = FindInModel(model, "DizzyStars").gameObject;
        crew.handLeft = FindInModel(model, "LeftHand");
        crew.handRight = FindInModel(model, "RightHand");
        crew.starsBone = FindInModel(model, "Stars");
        crew.renderers = model.GetComponentsInChildren<Renderer>(true);

        crew.Dress(CrewLook.Default);
        crew.Wear(true);
        crew.hammer.SetActive(false);
        crew.vacuum.SetActive(false);
        crew.dizzyStars.SetActive(false);
        return crew;
    }

    private static Transform FindInModel(GameObject model, string partName)
    {
        foreach (Transform item in model.GetComponentsInChildren<Transform>(true))
        {
            if (item.name == partName) return item;
        }
        Debug.LogError($"[Builder] The model has no part called {partName}.");
        return null;
    }

    /// <summary>The project's materials for every Blender model, by the name the model's script gives them.</summary>
    private static Dictionary<string, Material> ModelMaterials() => modelMaterials ??= new Dictionary<string, Material>
    {
        { "Char_Team", Mat("Team_Orange", TeamOrange) },
        { "Char_Band", Mat("Char_Band", new Color(0.97f, 0.97f, 0.9f), 0.5f) },
        { "Char_Pants", Mat("Char_Pants", new Color(0.2f, 0.24f, 0.34f)) },
        { "Char_Belt", Mat("Char_Belt", new Color(0.2f, 0.13f, 0.08f)) },
        { "Char_Boot", Mat("Char_Boot", new Color(0.47f, 0.28f, 0.14f), 0.3f) },
        { "Char_Sole", Mat("Char_Sole", new Color(0.1f, 0.09f, 0.09f)) },
        { "Char_Skin", Mat("Char_Skin", new Color(1f, 0.8f, 0.62f), 0.25f) },
        { "Char_Cheek", Mat("Char_Cheek", new Color(0.98f, 0.6f, 0.52f), 0.25f) },
        { "Char_Hair", Mat("Char_Hair", new Color(0.16f, 0.11f, 0.09f)) },
        { "Char_EyeWhite", Mat("Eye_White", Color.white, 0.5f) },
        { "Char_Pupil", Mat("Eye_Black", new Color(0.04f, 0.04f, 0.05f), 0.6f) },
        { "Char_Glove", Mat("Char_Glove", new Color(0.97f, 0.95f, 0.88f)) },
        { "Char_Star", Mat("Char_Star", new Color(1f, 0.85f, 0.1f), 0.6f) },
        { "Hat_Collector", Mat("Hat_Collector", new Color(1f, 0.86f, 0.1f), 0.5f) },
        { "Hat_Interrupter", Mat("Hat_Interrupter", new Color(0.15f, 0.13f, 0.18f), 0.45f) },
        { "Hat_Band", Mat("Hat_Band", new Color(0.86f, 0.14f, 0.16f)) },
        { "Hat_Horn", Mat("Hat_Horn", new Color(0.95f, 0.9f, 0.76f)) },
        { "Hammer_Handle", Mat("Hammer_Handle", new Color(0.55f, 0.35f, 0.18f)) },
        { "Hammer_Head", Mat("Hammer_Head", new Color(0.5f, 0.52f, 0.57f), 0.6f) },

        // Astra's crew, tools and first-person sleeves (Tools/blender/make_crew.py, make_hands.py).
        { "Porter_Shirt", Mat("Porter_Shirt", new Color(0.18f, 0.26f, 0.36f), 0.15f) },
        { "Wrecker_Jacket", Mat("Wrecker_Jacket", new Color(0.29f, 0.29f, 0.3f), 0.2f) },
        { "Wrecker_Shirt", Mat("Wrecker_Shirt", new Color(0.82f, 0.8f, 0.74f), 0.15f) },
        { "Crew_Skin", Mat("Crew_Skin", new Color(0.93f, 0.74f, 0.57f), 0.3f) },
        { "Crew_SkinShade", Mat("Crew_SkinShade", new Color(0.82f, 0.6f, 0.45f), 0.3f) },
        { "Crew_Hair", Mat("Crew_Hair", new Color(0.3f, 0.21f, 0.16f), 0.25f) },
        { "Crew_EyeWhite", Mat("Crew_EyeWhite", new Color(0.97f, 0.95f, 0.9f), 0.5f) },
        { "Crew_Dark", Mat("Crew_Dark", new Color(0.13f, 0.12f, 0.12f), 0.4f) },
        { "Crew_Mouth", Mat("Crew_Mouth", new Color(0.48f, 0.26f, 0.21f), 0.3f) },
        { "Crew_Glove", Mat("Crew_Glove", new Color(0.78f, 0.62f, 0.42f), 0.25f) },
        { "Crew_Boot", Mat("Crew_Boot", new Color(0.36f, 0.32f, 0.27f), 0.25f) },
        { "Crew_Brass", Mat("Crew_Brass", new Color(0.75f, 0.64f, 0.42f), 0.55f) },
        { "Crew_Light", Mat("Crew_Light", new Color(0.86f, 0.82f, 0.71f), 0.2f) },
        { "Crew_HatPrimary", Mat("Crew_HatPrimary", new Color(0.95f, 0.72f, 0.25f), 0.45f) },
        { "Crew_HatTrim", Mat("Crew_HatTrim", new Color(0.66f, 0.49f, 0.2f), 0.4f) },
        { "Crew_Lamp", Mat("Crew_Lamp", new Color(1f, 0.96f, 0.78f), 0.8f) },
        { "Crew_Beacon", Mat("Crew_Beacon", new Color(1f, 0.6f, 0.18f), 0.7f) },
        { "Crew_LensBlue", Mat("Crew_LensBlue", new Color(0.24f, 0.56f, 0.64f), 0.85f) },
        { "Crew_LensAmber", Mat("Crew_LensAmber", new Color(0.74f, 0.47f, 0.18f), 0.85f) },
        { "Hammer_Grip", Mat("Hammer_Grip", new Color(0.7f, 0.54f, 0.34f), 0.2f) },
        { "Vacuum_Housing", Mat("Vacuum_Housing", new Color(0.97f, 0.72f, 0.2f), 0.45f) },
        { "Vacuum_Red", Mat("Vacuum_Red", new Color(0.82f, 0.32f, 0.24f), 0.4f) },
        { "Vacuum_Steel", Mat("Vacuum_Steel", new Color(0.55f, 0.62f, 0.62f), 0.6f) },
        { "Vacuum_Glass", Mat("Vacuum_Glass", new Color(0.3f, 0.66f, 0.74f), 0.85f) },
        { "Vacuum_Ivory", Mat("Vacuum_Ivory", new Color(0.97f, 0.89f, 0.76f), 0.3f) },
        { "FP_Sleeve", Mat("Porter_Shirt", new Color(0.18f, 0.26f, 0.36f), 0.15f) },

        // Astra's truck, its cargo bed and the helicopter (运输载具_v07, 交接/palette.json).
        { "Fleet_Navy", FleetMat("Fleet_Navy", "303950", 0.46f, 0.12f) },
        { "Fleet_Orange", FleetMat("Fleet_Orange", "CB682B", 0.46f, 0.08f) },
        { "Fleet_Cream", FleetMat("Fleet_Cream", "DFD8B7", 0.46f, 0.05f) },
        { "Fleet_Gold", FleetMat("Fleet_Gold", "D9A928", 0.42f, 0.12f) },
        { "Fleet_Dark", FleetMat("Fleet_Dark", "353934", 0.58f, 0.12f) },
        { "Fleet_Rubber", FleetMat("Fleet_Rubber", "24292C", 0.76f, 0f) },
        { "Fleet_Steel", FleetMat("Fleet_Steel", "849393", 0.33f, 0.6f) },
        { "Fleet_Brass", FleetMat("Fleet_Brass", "AC8E52", 0.36f, 0.55f) },
        { "Fleet_Glass", FleetMat("Fleet_Glass", "284B58", 0.18f, 0.3f) },
        { "Fleet_GlassGlint", FleetMat("Fleet_GlassGlint", "8DB1B8", 0.25f, 0.24f) },
        { "Fleet_Lamp", FleetMat("Fleet_Lamp", "FFF1BE", 0.26f, 0.12f) },
        { "Fleet_Red", FleetMat("Fleet_Red", "B9503E", 0.4f, 0.12f) },
        { "Fleet_Grille", FleetMat("Fleet_Grille", "182932", 0.65f, 0.05f) },

        { "Heli_Yellow", Mat("Heli_Yellow", new Color(1f, 0.76f, 0.1f), 0.45f) },
        { "Heli_White", Mat("Heli_White", new Color(0.95f, 0.94f, 0.9f), 0.35f) },
        { "Heli_Dark", Mat("Heli_Dark", new Color(0.13f, 0.14f, 0.17f), 0.3f) },
        { "Heli_Steel", Mat("Heli_Steel", new Color(0.55f, 0.57f, 0.62f), 0.6f) },
        { "Heli_Glass", Mat("Heli_Glass", new Color(0.5f, 0.8f, 0.95f), 0.9f) },
        { "Heli_Red", Mat("Heli_Red", new Color(0.88f, 0.16f, 0.14f), 0.4f) },

        { "Eye_White", Mat("Eye_White", Color.white, 0.5f) },
        { "Eye_Black", Mat("Eye_Black", new Color(0.04f, 0.04f, 0.05f), 0.6f) },
        { "Cargo_Cardboard", Mat("Cargo_Cardboard", new Color(0.82f, 0.66f, 0.42f)) },
        { "Cargo_CardboardDark", Mat("Cargo_CardboardDark", new Color(0.66f, 0.5f, 0.3f)) },
        { "Cargo_Tape", Mat("Cargo_Tape", new Color(0.95f, 0.9f, 0.7f), 0.5f) },
        { "Cargo_Label", Mat("Cargo_Label", new Color(1f, 1f, 0.97f), 0.3f) },
        { "Cargo_Sticker", Mat("Cargo_Sticker", new Color(0.9f, 0.2f, 0.18f), 0.4f) },
        { "Cargo_Crate", Mat("Cargo_Crate", new Color(0.2f, 0.66f, 0.56f), 0.3f) },
        { "Cargo_CrateFrame", Mat("Cargo_CrateFrame", new Color(0.1f, 0.42f, 0.38f)) },
        { "Cargo_Steel", Mat("Cargo_Steel", new Color(0.66f, 0.68f, 0.72f), 0.6f) },
        { "Cargo_Chest", Mat("Cargo_Chest", new Color(0.47f, 0.26f, 0.72f), 0.45f) },
        { "Cargo_ChestDark", Mat("Cargo_ChestDark", new Color(0.3f, 0.14f, 0.5f), 0.4f) },
        { "Cargo_Gold", Mat("Cargo_Gold", new Color(1f, 0.8f, 0.2f), 0.65f) },
        { "Cargo_Inside", Mat("Cargo_Inside", new Color(0.16f, 0.11f, 0.1f)) },
        { "Cargo_Straw", Mat("Cargo_Straw", new Color(0.93f, 0.77f, 0.36f), 0.05f) },

        { "Truck_Blue", Mat("Truck_Blue", new Color(0.2f, 0.47f, 0.84f), 0.5f) },
        { "Truck_BlueDark", Mat("Truck_BlueDark", new Color(0.13f, 0.31f, 0.58f), 0.45f) },
        { "Truck_Orange", Mat("Truck_Orange", new Color(0.98f, 0.48f, 0.15f), 0.4f) },
        { "Truck_OrangeDark", Mat("Truck_OrangeDark", new Color(0.77f, 0.33f, 0.1f), 0.4f) },
        { "Truck_Dark", Mat("Truck_Dark", new Color(0.19f, 0.21f, 0.23f), 0.3f) },
        { "Truck_Tire", Mat("Truck_Tire", new Color(0.14f, 0.14f, 0.15f), 0.15f) },
        { "Truck_Steel", Mat("Truck_Steel", new Color(0.68f, 0.69f, 0.72f), 0.65f) },
        { "Truck_Glass", Mat("Truck_Glass", new Color(0.58f, 0.8f, 0.91f), 0.9f) },
        { "Truck_White", Mat("Truck_White", new Color(0.96f, 0.96f, 0.94f), 0.4f) },
        { "Truck_Amber", Mat("Truck_Amber", new Color(1f, 0.77f, 0.19f), 0.6f) },
        { "Truck_Red", Mat("Truck_Red", new Color(0.88f, 0.19f, 0.19f), 0.5f) },
        { "Truck_Light", Mat("Truck_Light", new Color(1f, 0.96f, 0.8f), 0.7f) },
        { "Cone_Orange", Mat("Cone_Orange", new Color(1f, 0.5f, 0.12f), 0.3f) },
        { "Cone_Band", Mat("Cone_Band", new Color(0.97f, 0.97f, 0.93f), 0.3f) },
        { "Cone_Base", Mat("Cone_Base", new Color(0.18f, 0.18f, 0.2f)) },

        { "Tree_Bark", Mat("Tree_Bark", new Color(0.45f, 0.31f, 0.19f)) },
        { "Leaf_Dark", Mat("Leaf_Dark", new Color(0.17f, 0.43f, 0.22f), 0.1f) },
        { "Leaf_Mid", Mat("Leaf_Mid", new Color(0.25f, 0.56f, 0.25f), 0.1f) },
        { "Leaf_Light", Mat("Leaf_Light", new Color(0.4f, 0.68f, 0.27f), 0.1f) },
        { "Leaf_Yellow", Mat("Leaf_Yellow", new Color(0.66f, 0.77f, 0.27f), 0.1f) },
        { "Pine_Dark", Mat("Pine_Dark", new Color(0.13f, 0.39f, 0.28f), 0.1f) },
        { "Pine_Light", Mat("Pine_Light", new Color(0.21f, 0.5f, 0.32f), 0.1f) },
        { "Rock_Base", Mat("Rock_Base", new Color(0.47f, 0.48f, 0.52f), 0.1f) },
        { "Rock_Light", Mat("Rock_Light", new Color(0.6f, 0.61f, 0.63f), 0.1f) },
        { "Rock_Moss", Mat("Rock_Moss", new Color(0.42f, 0.59f, 0.27f), 0.05f) },
        { "Fence_White", Mat("Fence_White", new Color(0.95f, 0.93f, 0.86f)) },
        { "Fence_Cap", Mat("Fence_Cap", new Color(0.36f, 0.44f, 0.53f)) },
        { "Guard_Steel", Mat("Guard_Steel", new Color(0.74f, 0.76f, 0.79f), 0.6f) },
        { "Guard_Groove", Mat("Guard_Groove", new Color(0.48f, 0.5f, 0.54f), 0.5f) },
        { "Guard_Post", Mat("Guard_Post", new Color(0.32f, 0.34f, 0.38f), 0.4f) },
        { "Barrier_Frame", Mat("Barrier_Frame", new Color(0.2f, 0.21f, 0.24f), 0.4f) },
        { "Sign_Yellow", Mat("Sign_Yellow", new Color(1f, 0.82f, 0.1f), 0.4f) },
        { "Sign_Black", Mat("Sign_Black", new Color(0.08f, 0.08f, 0.09f), 0.4f) },
        { "Pole_Wood", Mat("Pole_Wood", new Color(0.36f, 0.26f, 0.18f)) },
        { "Pole_Wire", Mat("Pole_Wire", new Color(0.07f, 0.07f, 0.08f)) },
        { "Pole_Insulator", Mat("Pole_Insulator", new Color(0.85f, 0.86f, 0.8f), 0.6f) },
        { "Hay", Mat("Hay", new Color(0.88f, 0.73f, 0.37f), 0.05f) },
        { "Hay_Dark", Mat("Hay_Dark", new Color(0.67f, 0.51f, 0.23f), 0.05f) },
        { "Hay_Twine", Mat("Hay_Twine", new Color(0.4f, 0.25f, 0.12f), 0.05f) },
        { "Barn_Red", Mat("Barn_Red", new Color(0.7f, 0.17f, 0.14f)) },
        { "Barn_RedDark", Mat("Barn_RedDark", new Color(0.5f, 0.11f, 0.1f)) },
        { "Barn_Trim", Mat("Barn_Trim", new Color(0.95f, 0.93f, 0.87f)) },
        { "Barn_Roof", Mat("Barn_Roof", new Color(0.3f, 0.33f, 0.38f)) },
        { "Barn_Hole", Mat("Barn_Hole", new Color(0.12f, 0.12f, 0.15f)) },
        { "Barn_Stone", Mat("Barn_Stone", new Color(0.55f, 0.54f, 0.52f), 0.1f) },
        { "Silo_Steel", Mat("Silo_Steel", new Color(0.75f, 0.78f, 0.81f), 0.5f) },
        { "Silo_Band", Mat("Silo_Band", new Color(0.5f, 0.52f, 0.56f), 0.5f) },
        { "Silo_Cap", Mat("Silo_Cap", new Color(0.6f, 0.2f, 0.16f), 0.4f) },
        { "Board_White", Mat("Board_White", new Color(0.97f, 0.96f, 0.9f)) },
        { "Board_Frame", Mat("Board_Frame", new Color(0.13f, 0.31f, 0.58f)) },
        { "Board_Text", Mat("Board_Text", new Color(0.1f, 0.16f, 0.32f)) },
        { "Board_Accent", Mat("Board_Accent", new Color(0.98f, 0.48f, 0.15f)) },
        { "Board_Wood", Mat("Board_Wood", new Color(0.42f, 0.3f, 0.2f)) },
        { "Tuft_Light", Mat("Tuft_Light", new Color(0.46f, 0.7f, 0.28f), 0.05f) },
        { "Tuft_Dark", Mat("Tuft_Dark", new Color(0.29f, 0.54f, 0.24f), 0.05f) },
        { "Flower_White", Mat("Flower_White", new Color(0.98f, 0.98f, 0.94f), 0.05f) },
        { "Flower_Yellow", Mat("Flower_Yellow", new Color(1f, 0.83f, 0.2f), 0.05f) },
        { "Flower_Pink", Mat("Flower_Pink", new Color(0.97f, 0.56f, 0.72f), 0.05f) },
        { "Dirt_Top", Mat("Dirt_Top", new Color(0.5f, 0.37f, 0.24f), 0.05f) },
        { "Dirt_Dark", Mat("Dirt_Dark", new Color(0.36f, 0.25f, 0.16f), 0.05f) },
        { "Cloud_White", Mat("Level_Cloud", Color.white, 0f) },
    };

    /// <summary>Swaps the materials imported with a model for the project's own, matched by name.</summary>
    private static void ApplyMaterials(GameObject model)
    {
        Dictionary<string, Material> materials = ModelMaterials();
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = part.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && materials.TryGetValue(slots[i].name, out Material replacement)) slots[i] = replacement;
                else Debug.LogError($"[Builder] {model.name} part {part.name} has an unknown material {slots[i]?.name}.");
            }
            part.sharedMaterials = slots;
        }
    }

    /// <summary>A copy of a model with no animation, with the project's materials on it.</summary>
    private static GameObject NewStaticModel(string modelName, Transform parent)
    {
        string path = Root + "/Models/" + modelName + ".fbx";
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.SaveAndReimport();

        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
        ApplyMaterials(model);
        return model;
    }

    /// <summary>
    /// The prefab of a scenery model, made the first time it is needed. Editing the prefab changes
    /// every copy in the level. Small things get no outline and no shadow, and a part whose name
    /// starts with Plain gets no outline.
    /// </summary>
    private static GameObject SceneryPrefab(string modelName)
    {
        if (sceneryPrefabs.TryGetValue(modelName, out GameObject prefab)) return prefab;

        bool small = modelName.StartsWith("Tuft") || modelName.StartsWith("Flowers") || modelName.StartsWith("Cloud") || modelName == "Debris_Bits";
        GameObject copy = NewStaticModel(modelName, null);
        copy.name = modelName;
        foreach (Transform part in copy.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = small || part.name.StartsWith("Plain") ? 0 : GameLayers.Outlined;
            if (small && part.TryGetComponent(out MeshRenderer renderer)) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        prefab = PrefabUtility.SaveAsPrefabAsset(copy, SceneryDir + "/" + modelName + ".prefab");
        Object.DestroyImmediate(copy);
        sceneryPrefabs[modelName] = prefab;
        return prefab;
    }

    private static GameObject Place(string modelName, Transform parent, Vector3 position, float yaw = 0f, float scale = 1f)
    {
        GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(SceneryPrefab(modelName), parent);
        item.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        item.transform.localScale = Vector3.one * scale;
        return item;
    }

    /// <summary>
    /// Everything on the chosen layers gets drawn a second time with the outline material. To outline
    /// more of the game, add its layer to the mask here.
    /// </summary>
    private static void AddOutlines()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/Outline.shader");
        string materialPath = MaterialDir + "/Outline.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        // Nothing in the game reads these, and copying them takes time every frame on the lab's PCs.
        foreach (string path in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipeline);
        }

        foreach (string path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
        {
            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            SerializedObject serialized = new SerializedObject(data);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");

            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                Object existing = features.GetArrayElementAtIndex(i).objectReferenceValue;
                // Ambient occlusion goes too. It comes out as speckles on the hands, which a second camera
                // draws, and it's slow. It has to be removed, not switched off: switched off, its shaders
                // get left out of the build, and then it fails every frame when the game creates it.
                bool remove = existing != null && (existing.name == "CharacterOutline" || existing.name == "ModelOutline" || existing is ScreenSpaceAmbientOcclusion);
                if (!remove) continue;
                int size = features.arraySize;
                features.DeleteArrayElementAtIndex(i);
                if (features.arraySize == size) features.DeleteArrayElementAtIndex(i);
                map.DeleteArrayElementAtIndex(i);
                AssetDatabase.RemoveObjectFromAsset(existing);
                Object.DestroyImmediate(existing, true);
            }

            RenderObjects feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = "ModelOutline";
            feature.settings.passTag = "ModelOutline";
            feature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
            feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            feature.settings.filterSettings.LayerMask = (1 << GameLayers.Player) | (1 << GameLayers.Cargo) | (1 << GameLayers.Outlined) |
                (1 << GameLayers.ViewModel) | (1 << GameLayers.Debris) | (1 << GameLayers.Preview);
            feature.settings.overrideMaterial = material;
            feature.settings.overrideMaterialPassIndex = 0;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }
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
        small.chipColor = new Color(0.82f, 0.66f, 0.42f);
        small.breakSound = "crack";

        CargoDefinition medium = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Medium.asset");
        medium.size = CargoSize.Medium;
        medium.value = 30;
        medium.smashSeconds = 7f;
        medium.carrySpeedMultiplier = 0.7f;
        medium.requiredCarriers = 1;
        medium.fullCount = 6;
        medium.chipColor = new Color(0.2f, 0.66f, 0.56f);
        medium.breakSound = "crack";

        CargoDefinition large = LoadOrCreate<CargoDefinition>(DataDir + "/Cargo_Large.asset");
        large.size = CargoSize.Large;
        large.value = 100;
        large.smashSeconds = 16f;
        large.carrySpeedMultiplier = 0.5f;
        large.requiredCarriers = 2;
        large.fullCount = 2;
        large.chipColor = new Color(0.47f, 0.26f, 0.72f);
        large.breakSound = "coins";

        small.piecesPrefab = BuildCargoPieces("Cargo_Small");
        medium.piecesPrefab = BuildCargoPieces("Cargo_Medium");
        large.piecesPrefab = BuildCargoPieces("Cargo_Large");
        small.prefab = BuildCargoPrefab("Cargo_Small", new Vector3(0.3f, 0.3f, 0.3f), 1f, small);
        medium.prefab = BuildCargoPrefab("Cargo_Medium", new Vector3(0.52f, 0.46f, 0.46f), 3f, medium);
        large.prefab = BuildCargoPrefab("Cargo_Large", new Vector3(1.16f, 0.53f, 0.56f), 8f, large);
        foreach (CargoDefinition definition in new[] { small, medium, large }) EditorUtility.SetDirty(definition);
        return new[] { small, medium, large };
    }

    /// <summary>The models come from Tools/blender/make_cargo.py and fit these box colliders.</summary>
    private static GameObject BuildCargoPrefab(string name, Vector3 half, float mass, CargoDefinition definition)
    {
        GameObject root = CargoRoot(name, half, mass, definition, out Transform visual);
        GameObject model = NewStaticModel(name, visual);
        model.name = "Model";

        List<Transform> eyes = new List<Transform>();
        List<Transform> mouths = new List<Transform>();
        foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
        {
            if (part.name.StartsWith("Eye_")) eyes.Add(part);
            if (part.name.StartsWith("Mouth_")) mouths.Add(part);
        }
        CargoFace face = root.AddComponent<CargoFace>();
        face.eyes = eyes.ToArray();
        face.mouths = mouths.ToArray();

        // The bodies it changes into as it gets hit. The face stays on, so it can watch.
        GameObject damaged = NewStaticModel(name + "_Damaged", visual);
        damaged.name = "Damaged";
        GameObject dented = damaged.transform.Find("Body_D1").gameObject;
        GameObject wrecked = damaged.transform.Find("Body_D2").gameObject;
        dented.SetActive(false);
        wrecked.SetActive(false);
        SerializedObject item = new SerializedObject(root.GetComponent<CargoItem>());
        SerializedProperty bodies = item.FindProperty("damageBodies");
        bodies.arraySize = 3;
        bodies.GetArrayElementAtIndex(0).objectReferenceValue = model.transform.Find("Body").gameObject;
        bodies.GetArrayElementAtIndex(1).objectReferenceValue = dented;
        bodies.GetArrayElementAtIndex(2).objectReferenceValue = wrecked;
        item.ApplyModifiedPropertiesWithoutUndo();

        SetLayerRecursively(root, GameLayers.Cargo);
        return SavePrefab(root, name);
    }

    /// <summary>What's left when it breaks: each piece gets its own collider and physics, and lands on the level.</summary>
    private static GameObject BuildCargoPieces(string name)
    {
        GameObject root = NewStaticModel(name + "_Pieces", null);
        root.name = name + "_Pieces";
        PhysicsMaterial bounce = BouncyMaterial();
        foreach (MeshFilter part in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds bounds = part.sharedMesh.bounds;
            BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.04f);
            box.sharedMaterial = bounce;
            Rigidbody body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(bounds.size.x * bounds.size.y * bounds.size.z * 8f, 0.1f, 2f);
            body.linearDamping = 0.15f;
            body.angularDamping = 0.35f;
        }
        root.AddComponent<CargoPieces>();
        SetLayerRecursively(root, GameLayers.Debris);
        return SavePrefab(root, name + "_Pieces");
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

    // ---- Helicopter ----

    /// <summary>
    /// Astra's helicopter (独立建模/运输载具_v07, copied from 交接/旧接口兼容/Helicopter.fbx). Materials are
    /// matched by name. Its winch, hook and the hook's eye are where her lifting motion expects them.
    /// </summary>
    private static GameObject BuildHelicopter()
    {
        GameObject root = NewStaticModel("Helicopter", null);
        root.name = "Helicopter";
        SetLayerRecursively(root, GameLayers.Outlined);

        // A faint disc makes the spinning blades read as a blur, even in a still frame.
        Transform mainRotor = root.transform.Find("MainRotor");
        Part(PrimitiveType.Cylinder, "RotorBlur", mainRotor, new Vector3(0f, 0.08f, 0f), new Vector3(9.6f, 0.004f, 9.6f),
            TransparentMat("Heli_RotorBlur", new Color(0.1f, 0.11f, 0.13f, 0.14f))).GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        // HelicopterExtraction stretches these thin cylinders every frame, and puts a clip on each sling's end.
        Material cable = Mat("Heli_Cable", new Color(0.09f, 0.09f, 0.1f), 0.2f);
        Part(PrimitiveType.Cylinder, "Cable", root.transform, Vector3.zero, Vector3.one * 0.1f, cable).SetActive(false);
        for (int i = 1; i <= 4; i++)
            Part(PrimitiveType.Cylinder, "Sling" + i, root.transform, Vector3.zero, Vector3.one * 0.1f, cable).SetActive(false);
        Material clipColour = ModelMaterials()["Fleet_Orange"];
        for (int i = 1; i <= 4; i++)
            Part(PrimitiveType.Cube, "SlingClip" + i, root.transform, Vector3.zero, new Vector3(0.11f, 0.17f, 0.12f), clipColour).SetActive(false);

        Debug.Log($"[Builder] Helicopter root scale={root.transform.localScale} mainRotor rotation={mainRotor.localRotation.eulerAngles} scale={mainRotor.localScale} winch={root.transform.Find("Winch").localPosition}");
        return SavePrefab(root, "Helicopter");
    }

    private static void BuildExtraction(Transform bed)
    {
        GameObject prefab = BuildHelicopter();
        Transform root = Group("Extraction", null);
        GameObject helicopter = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        helicopter.name = "Helicopter";

        HelicopterExtraction extraction = root.gameObject.AddComponent<HelicopterExtraction>();
        extraction.helicopter = helicopter.transform;
        extraction.mainRotor = helicopter.transform.Find("MainRotor");
        extraction.tailRotor = helicopter.transform.Find("TailRotor");
        extraction.winch = helicopter.transform.Find("Winch");
        extraction.hook = helicopter.transform.Find("Hook");
        extraction.cable = helicopter.transform.Find("Cable");
        extraction.slings = new[]
        {
            helicopter.transform.Find("Sling1"), helicopter.transform.Find("Sling2"),
            helicopter.transform.Find("Sling3"), helicopter.transform.Find("Sling4"),
        };
        extraction.slingClips = new[]
        {
            helicopter.transform.Find("SlingClip1"), helicopter.transform.Find("SlingClip2"),
            helicopter.transform.Find("SlingClip3"), helicopter.transform.Find("SlingClip4"),
        };
        extraction.bed = bed;
        helicopter.SetActive(false);
    }

    // ---- Level ----

    /// <summary>
    /// Astra's crashed truck (独立建模/运输载具_v07, copied from 交接/旧接口兼容/Truck.fbx): a front wheel
    /// off and lying on the ground, the windscreen cracked. Its CargoBed is a separate object, with the
    /// helicopter's four lifting eyes on it.
    /// </summary>
    private static GameObject BuildCrashedTruck()
    {
        GameObject root = new GameObject("TruckCrashed");
        GameObject model = NewStaticModel("Truck", root.transform);
        model.name = "Model";
        SetLayerRecursively(model, GameLayers.Outlined);
        // One side has dropped into the ditch, so the bed tips toward the road and stays easy to see.
        model.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        model.transform.localEulerAngles = new Vector3(0f, 0f, -9f);

        // The wheel that came off lies on the ground, so it doesn't lean with the truck.
        model.transform.Find("LooseWheel").SetParent(root.transform, false);

        // Her bed's floor is at 2.02, so cargo lands just above it.
        Transform bed = Group("BedOrigin", model.transform, new Vector3(0f, 2.05f, 1.66f));
        bed.name = "BedOrigin";
        // The engine sits under the cab, so the smoke comes up out of the grille.
        AddPuffs(model.transform, "EngineSmoke", new Vector3(-0.4f, 1.9f, -4.5f), 6f, 40, new Color(0.42f, 0.42f, 0.44f), new Color(0.68f, 0.68f, 0.7f), 0.4f, 0.8f, 1.1f, 1.8f);

        Transform colliders = Group("Colliders", root.transform);
        AddBoxCollider(colliders, "Cab", new Vector3(0f, 2.4f, -2.55f), new Vector3(3.4f, 3.1f, 3.7f));
        AddBoxCollider(colliders, "Bed", new Vector3(0f, 2.1f, 1.69f), new Vector3(4f, 2f, 5.1f));
        AddBoxCollider(colliders, "Chassis", new Vector3(0f, 1f, 0f), new Vector3(5.8f, 2f, 8.7f));

        Group("Zone", root.transform);
        return SavePrefab(root, "TruckCrashed");
    }

    /// <summary>Round puffs of smoke or dust, left hanging in the air. They run on their own, on every computer.</summary>
    private static void AddPuffs(Transform parent, string name, Vector3 position, float rate, int maxParticles, Color darkest, Color lightest,
        float smallest, float largest, float shortestLife, float longestLife, Material material = null)
    {
        GameObject smoke = new GameObject(name);
        smoke.transform.SetParent(parent, false);
        smoke.transform.localPosition = position;
        ParticleSystem system = smoke.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(shortestLife, longestLife);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(smallest, largest);
        main.startColor = new ParticleSystem.MinMaxGradient(darkest, lightest);
        main.gravityModifier = -0.12f;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);

        ParticleSystemRenderer renderer = smoke.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        renderer.sharedMaterial = material != null ? material : ParticleMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        Material zone = ZoneMaterial();

        GameObject truckPrefab = BuildCrashedTruck();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddDaylight();
        AddCountrySky();

        BuildPlayerCamera();

        Transform environment = Group("Environment", null);
        AddGround(environment);

        List<Vector4> obstacles = new List<Vector4>();
        Transform props = Group("Props", environment);
        AddWalls(environment);
        AddBorder(environment);
        AddTrees(props, obstacles);
        AddRocks(props, obstacles);
        AddFences(props, obstacles);
        AddSigns(props);
        AddCrashSite(props);
        AddDecor(environment, obstacles);
        AddOutskirts(environment);
        AddBackdrop(environment);

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
            Place("TrafficCone", props, new Vector3(4.6f, 0f, -28f + i * 4f), i * 37f);

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
        // Her bed is 3.16 m across inside, so the outer rows of cargo stay off its walls.
        layout.bedHalfSize = new Vector2(1.25f, 1.95f);
        layout.mapArea = new Rect(-23f, -49f, 46f, 98f);
        layout.mapPicture = PaintedTexture("Level01_Ground", TextureWrapMode.Clamp);

        GameObject navigation = new GameObject("Navigation");
        NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << GameLayers.Level;
        surface.BuildNavMesh();
        string navPath = SceneDir + "/Level01_NavMesh.asset";
        AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);

        BuildExtraction(truck.transform.Find("Model/CargoBed"));
        BuildHud();

        GameObject events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        events.transform.SetAsLastSibling();

        ReportSceneSize(environment);
        EditorSceneManager.SaveScene(scene, LevelScenePath);
    }

    /// <summary>
    /// The main camera, with a second camera stacked on it that only draws the hands and whatever
    /// they're holding. That one clears the depth first, so they never poke into a wall.
    /// </summary>
    private static void BuildPlayerCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.08f;
        camera.farClipPlane = 640f;
        camera.cullingMask = ~(1 << GameLayers.ViewModel);
        UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        // Fences and wires shimmer as you walk past them at eye level.
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        GameObject handsObject = new GameObject("Hands Camera");
        handsObject.transform.SetParent(cameraObject.transform, false);
        Camera handsCamera = handsObject.AddComponent<Camera>();
        handsCamera.cullingMask = 1 << GameLayers.ViewModel;
        handsCamera.nearClipPlane = 0.03f;
        handsCamera.farClipPlane = 8f;
        UniversalAdditionalCameraData handsData = handsObject.AddComponent<UniversalAdditionalCameraData>();
        handsData.renderType = CameraRenderType.Overlay;
        handsData.renderShadows = false;
        cameraData.cameraStack.Add(handsCamera);

        FollowCamera follow = cameraObject.AddComponent<FollowCamera>();
        follow.view = camera;
        follow.handsView = handsCamera;
        follow.fallbackFocus = new Vector3(0f, 0f, -20f);
        camera.fieldOfView = follow.overviewFov;
        handsCamera.fieldOfView = follow.firstPersonFov;
        cameraObject.transform.position = follow.fallbackFocus + follow.offset;
        cameraObject.transform.rotation = Quaternion.LookRotation(-follow.offset);

        BuildFirstPersonRig(cameraObject.transform);
    }

    /// <summary>The model comes from Tools/blender/make_hands.py. FirstPersonRig poses its parts in code.</summary>
    private static void BuildFirstPersonRig(Transform camera)
    {
        GameObject rigObject = new GameObject("First Person Rig");
        rigObject.transform.SetParent(camera, false);
        FirstPersonRig rig = rigObject.AddComponent<FirstPersonRig>();

        GameObject model = NewStaticModel("FirstPersonHands", rigObject.transform);
        model.name = "Hands";
        rig.content = model;
        rig.rightArm = BuildFirstPersonArm(model, "R");
        rig.leftArm = BuildFirstPersonArm(model, "L");
        rig.hammer = FindInModel(model, "Hammer");
        rig.vacuum = FindInModel(model, "Vacuum");
        rig.sleeves = new Renderer[]
        {
            rig.rightArm.upperArm.GetComponent<Renderer>(), rig.rightArm.forearm.GetComponent<Renderer>(),
            rig.leftArm.upperArm.GetComponent<Renderer>(), rig.leftArm.forearm.GetComponent<Renderer>(),
        };

        SetLayerRecursively(model, GameLayers.ViewModel);
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true))
        {
            part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            part.receiveShadows = false;
        }
    }

    private static FirstPersonRig.ArmParts BuildFirstPersonArm(GameObject model, string side)
    {
        Transform hand = Group("Hand_" + side, model.transform);
        return new FirstPersonRig.ArmParts
        {
            upperArm = FindInModel(model, "UpperArm_" + side),
            forearm = FindInModel(model, "Forearm_" + side),
            hand = hand,
            relaxed = HandPart(model, $"Hand_{side}_Relaxed", hand),
            open = HandPart(model, $"Hand_{side}_Open", hand),
            fist = HandPart(model, $"Hand_{side}_Fist", hand),
            grip = HandPart(model, $"Hand_{side}_Grip", hand),
        };
    }

    private static GameObject HandPart(GameObject model, string partName, Transform hand)
    {
        Transform part = FindInModel(model, partName);
        part.SetParent(hand, false);
        return part.gameObject;
    }

    /// <summary>A painted sky, with the far distance fading into the bottom of it.</summary>
    private static void AddCountrySky()
    {
        RenderSettings.skybox = SkyMaterial();
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.8f, 0.91f, 0.96f);
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 320f;
    }

    private static void AddDaylight()
    {
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
    }

    /// <summary>How heavy the scenery is. Worth watching, because the instructor plays on a lab PC.</summary>
    private static void ReportSceneSize(Transform environment)
    {
        MeshFilter[] meshes = environment.GetComponentsInChildren<MeshFilter>(true);
        long triangles = 0;
        foreach (MeshFilter mesh in meshes)
        {
            if (mesh.sharedMesh != null) triangles += mesh.sharedMesh.triangles.Length / 3;
        }
        Debug.Log($"[Builder] Level01 scenery: {meshes.Length} meshes, {triangles} triangles.");
    }

    /// <summary>
    /// The collider everything stands on, and separately the painted pictures of the ground from
    /// Tools/blender/make_ground.py: the play area, the land around it, and the road carrying on past both ends.
    /// </summary>
    private static void AddGround(Transform parent)
    {
        GameObject ground = new GameObject("Ground");
        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        ground.layer = GameLayers.Level;
        ground.AddComponent<BoxCollider>().size = new Vector3(46f, 1f, 98f);

        GroundQuad("Level01_GroundPaint", parent, new Rect(-23f, -49f, 46f, 98f), 0f,
            GroundMat("Level_Ground", "Level01_Ground", TextureWrapMode.Clamp), Vector2.zero, Vector2.one);
        GroundQuad("Level01_GroundFar", parent, new Rect(-250f, -200f, 400f, 400f), -0.05f,
            GroundMat("Level_GroundFar", "Level01_Far", TextureWrapMode.Clamp), Vector2.zero, Vector2.one);

        // Past the painted land there's plain grass, out to where the haze hides everything.
        GroundQuad("Level01_GroundHorizon", parent, new Rect(-900f, -900f, 1800f, 1800f), -0.12f,
            Mat("Level_GroundHorizon", new Color(0.42f, 0.62f, 0.3f), 0f), Vector2.zero, Vector2.one);

        Material road = GroundMat("Level_RoadFar", "Level01_RoadTile", TextureWrapMode.Repeat);
        AddRoadStrip("Level01_RoadNorth", parent, 49f, 200f, road);
        AddRoadStrip("Level01_RoadSouth", parent, -200f, -49f, road);
    }

    /// <summary>The texture repeats every 6 metres with a dash in its middle, lined up with the dashes in the play area.</summary>
    private static void AddRoadStrip(string name, Transform parent, float fromZ, float toZ, Material road)
    {
        Vector2 uvFrom = new Vector2(0f, (fromZ + 46f) / 6f + 0.5f);
        Vector2 uvTo = new Vector2(1f, (toZ + 46f) / 6f + 0.5f);
        GroundQuad(name, parent, new Rect(-10f, fromZ, 20f, toZ - fromZ), -0.02f, road, uvFrom, uvTo);
    }

    private static void GroundQuad(string name, Transform parent, Rect area, float height, Material material, Vector2 uvFrom, Vector2 uvTo)
    {
        Mesh mesh = new Mesh { name = name };
        mesh.vertices = new[]
        {
            new Vector3(area.xMin, 0f, area.yMin), new Vector3(area.xMax, 0f, area.yMin),
            new Vector3(area.xMin, 0f, area.yMax), new Vector3(area.xMax, 0f, area.yMax),
        };
        mesh.uv = new[]
        {
            uvFrom, new Vector2(uvTo.x, uvFrom.y), new Vector2(uvFrom.x, uvTo.y), uvTo,
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();
        string path = Root + "/Models/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        GameObject quad = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, height, 0f);
        quad.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// A white fence just outside the walls on all four sides, so the edge of the map can be seen.
    /// The road is closed off with barriers at both ends.
    /// </summary>
    private static void AddBorder(Transform parent)
    {
        Transform border = Group("Border", parent);
        const float sideX = 22.75f;
        const float endZ = 49.25f;
        const float roadGap = 6.9f;
        AddFenceRun(border, new Vector3(-sideX, 0f, -endZ), new Vector3(-sideX, 0f, endZ), 1, true);
        AddFenceRun(border, new Vector3(sideX, 0f, -endZ), new Vector3(sideX, 0f, endZ), 2, true);
        foreach (float z in new[] { -endZ, endZ })
        {
            AddFenceRun(border, new Vector3(-sideX, 0f, z), new Vector3(-roadGap, 0f, z), 3, false);
            AddFenceRun(border, new Vector3(roadGap, 0f, z), new Vector3(sideX, 0f, z), 4, false);
            Place("Barrier", border, new Vector3(-2.9f, 0f, z));
            Place("Barrier", border, new Vector3(2.9f, 0f, z));
            Place("TrafficCone", border, new Vector3(-6.3f, 0f, z), 20f);
            Place("TrafficCone", border, new Vector3(6.3f, 0f, z), 75f);
        }
    }

    /// <summary>A straight fence from one point to another, stretched a little so the posts fit exactly.</summary>
    private static void AddFenceRun(Transform parent, Vector3 start, Vector3 end, int seed, bool someLoose)
    {
        const float modelSpan = 2.4f;
        Transform run = Group("Fence", parent);
        Vector3 along = end - start;
        int spans = Mathf.Max(1, Mathf.RoundToInt(along.magnitude / modelSpan));
        float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
        System.Random random = new System.Random(seed);
        for (int i = 0; i < spans; i++)
        {
            bool loose = someLoose && random.NextDouble() < 0.1;
            Vector3 centre = start + along * ((i + 0.5f) / spans);
            float lean = (float)(random.NextDouble() - 0.5) * 2.5f;
            GameObject span = Place(loose ? "Fence_Loose" : "Fence_Rail", run, centre, yaw + lean);
            span.transform.localScale = new Vector3(1f, 1f, along.magnitude / spans / modelSpan);
        }
        Place("Fence_Post", run, start, yaw);
    }

    private static void AddSigns(Transform parent)
    {
        foreach (Vector3 spot in new[] { new Vector3(7.8f, 0f, 34f), new Vector3(-7.8f, 0f, 13.5f) })
        {
            Transform sign = Group("Sign", parent, spot);
            Place("Sign_Warning", sign, Vector3.zero);
            AddCapsuleBlocker(sign, 0.25f, 3f);
        }
    }

    /// <summary>
    /// What the crash left behind: the guardrail the truck went through, a bit of its cargo box, and
    /// ploughed-up soil. The tyre marks and torn ground are painted in Level01_Ground.png.
    /// </summary>
    private static void AddCrashSite(Transform parent)
    {
        Transform crash = Group("CrashSite", parent);
        const float railX = 7.4f;
        Place("Guard_Rail", crash, new Vector3(railX, 0f, -16f));
        Place("Guard_Bent", crash, new Vector3(railX, 0f, -20f));
        AddBoxBlocker(crash, new Vector3(railX, 0.6f, -16f), new Vector3(0.4f, 1.2f, 4f), 0f);
        AddBoxBlocker(crash, new Vector3(railX + 1f, 0.6f, -19.9f), new Vector3(0.4f, 1.2f, 4.3f), -27.8f);

        Place("Debris_Panel", crash, new Vector3(4.2f, 0f, -14.5f), 38f);
        Place("Debris_Bits", crash, new Vector3(6.1f, 0f, -24.5f), 0f);
        Place("Debris_Bits", crash, new Vector3(2.6f, 0f, -11.5f), 130f, 0.8f);
        Place("DirtMound", crash, new Vector3(13.3f, 0f, -36.8f), 24f, 0.72f);
        Place("DirtMound", crash, new Vector3(6.9f, 0f, -27.9f), 110f, 0.55f);
    }

    /// <summary>Grass tufts and flowers in small clumps. They have no colliders, so people walk through them.</summary>
    private static void AddDecor(Transform parent, List<Vector4> obstacles)
    {
        Transform decor = Group("Decor", parent);
        System.Random random = new System.Random(77);
        string[] flowers = { "Flowers_White", "Flowers_Yellow", "Flowers_Pink" };
        const int clumps = 74;
        int placed = 0;
        for (int attempt = 0; attempt < 2000 && placed < clumps; attempt++)
        {
            float x = Mathf.Lerp(-22f, 22f, (float)random.NextDouble());
            float z = Mathf.Lerp(-48.5f, 48.5f, (float)random.NextDouble());
            bool onRoad = Mathf.Abs(x) < 7.6f;
            bool nearTruck = Mathf.Abs(x - 11f) < 7.5f && Mathf.Abs(z + 33f) < 9f;
            if (onRoad || nearTruck || !IsClear(new Vector3(x, 0f, z), 0f, obstacles, new List<Vector3>())) continue;
            placed++;
            AddClump(decor, new Vector3(x, 0f, z), random, flowers[random.Next(flowers.Length)], random.NextDouble() < 0.42);
        }
    }

    private static void AddClump(Transform parent, Vector3 centre, System.Random random, string flower, bool withFlowers)
    {
        int count = random.Next(2, 5);
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = centre + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 1.8f;
            string model = withFlowers && i % 2 == 0 ? flower : (i % 2 == 0 ? "Tuft_A" : "Tuft_B");
            Place(model, parent, spot, (float)random.NextDouble() * 360f, 0.85f + (float)random.NextDouble() * 0.5f);
        }
    }

    /// <summary>
    /// Everything past the fence: a farm, billboards, utility poles, bushes and trees. Nothing here
    /// can be reached. Tall things stay out of OutskirtsIsFree's no-go areas.
    /// </summary>
    private static void AddOutskirts(Transform parent)
    {
        Transform outskirts = Group("Outskirts", parent);
        Transform farm = Group("Farm", outskirts);
        Place("Barn", farm, new Vector3(-35f, 0f, 19f), -28f);
        Place("Silo", farm, new Vector3(-43f, 0f, 31f));
        Vector3[] westHay = { new Vector3(-29.5f, 0f, 3f), new Vector3(-32f, 0f, 0.2f), new Vector3(-28.7f, 0f, -1.5f), new Vector3(-34f, 0f, 5f) };
        Vector3[] eastHay = { new Vector3(30f, 0f, -6f), new Vector3(33f, 0f, -3.5f), new Vector3(35f, 0f, 37f), new Vector3(28.5f, 0f, 41f) };
        foreach (Vector3 spot in westHay) Place("HayBale", farm, spot, spot.x * 47f);
        foreach (Vector3 spot in eastHay) Place("HayBale", farm, spot, spot.z * 53f);

        // This one stands where the helicopter shot can read it, in the gap to the left of the trees.
        Place("Billboard_Time", outskirts, new Vector3(-33f, 0f, -52f), -65f);
        Place("Billboard_Safe", outskirts, new Vector3(31f, 0f, 19f), 22f);
        Transform poles = Group("Poles", outskirts);
        for (float z = 102f; z >= -130f; z -= 24f) Place("UtilityPole", poles, new Vector3(-26.5f, 0f, z));

        List<Vector3> taken = new List<Vector3>
        {
            new Vector3(-35f, 0f, 19f), new Vector3(-43f, 0f, 31f), new Vector3(-33f, 0f, -52f), new Vector3(-27f, 0f, -48f), new Vector3(31f, 0f, 19f),
        };
        foreach (Vector3 spot in westHay) taken.Add(spot);
        foreach (Vector3 spot in eastHay) taken.Add(spot);

        Transform trees = Group("Trees", outskirts);
        System.Random random = new System.Random(2026);
        string[] nearTrees = { "Tree_RoundA", "Tree_RoundB", "Tree_RoundA", "Tree_RoundC", "Tree_Tall", "Tree_Pine" };
        ScatterOutside(trees, nearTrees, 30, -52f, 52f, -78f, 78f, 8f, 0.9f, 1.4f, random, taken);
        string[] farTrees = { "Tree_RoundA", "Tree_RoundB", "Tree_Tall", "Tree_Pine", "Tree_Pine" };
        ScatterOutside(trees, farTrees, 34, -118f, 95f, -145f, 145f, 12f, 1f, 1.6f, random, taken);

        // A line of tall trees along a farm track, for the view behind the helicopter.
        for (float z = -135f; z <= 135f; z += 12.5f)
        {
            float x = -92f + (float)random.NextDouble() * 3f;
            Place("Tree_Tall", trees, new Vector3(x, 0f, z + (float)random.NextDouble() * 4f), (float)random.NextDouble() * 360f, 1.2f + (float)random.NextDouble() * 0.5f);
        }

        Transform bushes = Group("Bushes", outskirts);
        string[] bushModels = { "Bush_Green", "Bush_Dark", "Bush_Flower" };
        for (int i = 0; i < 46; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float x = side * Mathf.Lerp(23.8f, 32f, Mathf.Pow((float)random.NextDouble(), 1.6f));
            float z = Mathf.Lerp(-49f, 49f, (float)random.NextDouble());
            Place(bushModels[i % bushModels.Length], bushes, new Vector3(x, 0f, z), (float)random.NextDouble() * 360f, 0.9f + (float)random.NextDouble() * 0.7f);
        }

        Transform grass = Group("Grass", outskirts);
        string[] flowers = { "Flowers_White", "Flowers_Yellow", "Flowers_Pink" };
        for (int i = 0; i < 70; i++)
        {
            float x = Mathf.Lerp(-48f, 48f, (float)random.NextDouble());
            float z = Mathf.Lerp(-66f, 72f, (float)random.NextDouble());
            if (Mathf.Abs(x) < 23.5f && Mathf.Abs(z) < 50f || Mathf.Abs(x) < 10.5f) continue;
            AddClump(grass, new Vector3(x, 0f, z), random, flowers[random.Next(flowers.Length)], random.NextDouble() < 0.35);
        }
    }

    private static void ScatterOutside(Transform parent, string[] models, int count, float minX, float maxX, float minZ, float maxZ,
        float spacing, float minScale, float maxScale, System.Random random, List<Vector3> taken)
    {
        int placed = 0;
        for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
        {
            Vector3 spot = new Vector3(Mathf.Lerp(minX, maxX, (float)random.NextDouble()), 0f, Mathf.Lerp(minZ, maxZ, (float)random.NextDouble()));
            if (!OutskirtsIsFree(spot) || !IsClear(spot, spacing, new List<Vector4>(), taken)) continue;
            taken.Add(spot);
            float scale = Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
            Place(models[placed % models.Length], parent, spot, (float)random.NextDouble() * 360f, scale);
            placed++;
        }
    }

    private static bool OutskirtsIsFree(Vector3 spot)
    {
        float absX = Mathf.Abs(spot.x);
        if (absX < 26f && Mathf.Abs(spot.z) < 53f) return false;
        if (absX < 12f) return false;
        // The game camera looks north from the south, so a tall tree here would hide the south end of the map.
        if (spot.z < -49f && spot.z > -64f && absX < 40f) return false;
        // The helicopter shot films from this side.
        if (spot.x > 24f && spot.x < 70f && spot.z > -90f && spot.z < -14f) return false;
        return true;
    }

    private static void AddCapsuleBlocker(Transform parent, float radius, float height)
    {
        GameObject blocker = new GameObject("Blocker");
        blocker.transform.SetParent(parent, false);
        blocker.layer = GameLayers.Level;
        CapsuleCollider capsule = blocker.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        capsule.height = height;
        capsule.radius = radius;
    }

    private static void AddBoxBlocker(Transform parent, Vector3 position, Vector3 size, float yaw)
    {
        GameObject blocker = new GameObject("Blocker");
        blocker.transform.SetParent(parent, false);
        blocker.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        blocker.layer = GameLayers.Level;
        blocker.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>Hills and clouds on the far horizon, all the way round, because in first person you can look anywhere.</summary>
    private static void AddBackdrop(Transform parent)
    {
        Material near = Mat("Level_Hill", new Color(0.36f, 0.57f, 0.38f));
        Material far = Mat("Level_HillFar", new Color(0.55f, 0.71f, 0.69f));
        Transform backdrop = Group("Backdrop", parent);

        // x, z, width, height
        Vector4[] nearHills =
        {
            new Vector4(-118f, 0f, -115f, 0f), new Vector4(-122f, 0f, -35f, 0f), new Vector4(-116f, 0f, 45f, 0f), new Vector4(-124f, 0f, 120f, 0f),
        };
        float[] nearSizes = { 120f, 38f, 140f, 46f, 125f, 36f, 135f, 50f };
        for (int i = 0; i < nearHills.Length; i++)
            Part(PrimitiveType.Sphere, "Hill", backdrop, new Vector3(nearHills[i].x, 0f, nearHills[i].z), new Vector3(70f, nearSizes[i * 2 + 1], nearSizes[i * 2]), near);

        Vector4[] farHills =
        {
            new Vector4(-172f, 0f, -135f, 0f), new Vector4(-178f, 0f, -55f, 0f), new Vector4(-170f, 0f, 30f, 0f), new Vector4(-176f, 0f, 115f, 0f),
        };
        float[] farSizes = { 150f, 66f, 145f, 58f, 165f, 78f, 150f, 62f };
        for (int i = 0; i < farHills.Length; i++)
            Part(PrimitiveType.Sphere, "HillFar", backdrop, new Vector3(farHills[i].x, 0f, farHills[i].z), new Vector3(90f, farSizes[i * 2 + 1], farSizes[i * 2]), far);

        Vector3[] clouds =
        {
            new Vector3(-150f, 42f, -95f), new Vector3(-165f, 50f, -30f), new Vector3(-150f, 40f, 30f), new Vector3(-170f, 54f, 95f),
            new Vector3(-130f, 56f, 62f),
        };
        for (int i = 0; i < clouds.Length; i++)
            Place(i % 2 == 0 ? "Cloud_A" : "Cloud_B", backdrop, clouds[i], 90f + i * 9f, 0.8f + i * 0.08f);

        // The other three sides. The helicopter shot looks west, and it flies in from the north above these.
        System.Random random = new System.Random(808);
        for (float angle = 35f; angle <= 325f; angle += 21f)
        {
            float wobble = Between(random, -7f, 7f);
            Vector3 direction = Quaternion.Euler(0f, angle + wobble, 0f) * Vector3.forward;
            Vector3 nearSpot = direction * Between(random, 235f, 265f);
            float facing = angle + wobble;
            Part(PrimitiveType.Sphere, "Hill", backdrop, nearSpot, new Vector3(Between(random, 120f, 170f), Between(random, 30f, 48f), 70f), near, new Vector3(0f, facing, 0f));
            Vector3 farSpot = direction * Between(random, 305f, 340f);
            Part(PrimitiveType.Sphere, "HillFar", backdrop, farSpot, new Vector3(Between(random, 170f, 220f), Between(random, 62f, 92f), 90f), far, new Vector3(0f, facing, 0f));
        }
        for (float angle = 20f; angle <= 340f; angle += 34f)
        {
            Vector3 direction = Quaternion.Euler(0f, angle + Between(random, -9f, 9f), 0f) * Vector3.forward;
            Vector3 spot = direction * Between(random, 180f, 250f);
            spot.y = Between(random, 42f, 72f);
            Place(random.NextDouble() < 0.5 ? "Cloud_A" : "Cloud_B", backdrop, spot, Between(random, 0f, 360f), Between(random, 0.9f, 1.6f));
        }
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

    private static void AddTrees(Transform parent, List<Vector4> obstacles)
    {
        string[] models =
        {
            "Tree_RoundA", "Tree_RoundB", "Tree_Tall", "Tree_RoundA", "Tree_RoundC", "Tree_Pine",
            "Tree_RoundB", "Tree_RoundA", "Tree_Tall", "Tree_RoundB", "Tree_Pine", "Tree_RoundA",
            "Tree_RoundB", "Tree_RoundA",
            "Tree_Tall", "Tree_RoundB", "Tree_Pine", "Tree_RoundA", "Tree_RoundC", "Tree_Tall",
        };
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
            Place(models[i], tree, Vector3.zero, i * 73f, size);
            if (Mathf.Abs(positions[i].x) < 23f)
            {
                AddCapsuleBlocker(tree, 0.7f * size, 3f);
                obstacles.Add(new Vector4(positions[i].x, 0f, positions[i].z, 1.6f));
            }
        }
    }

    private static void AddRocks(Transform parent, List<Vector4> obstacles)
    {
        string[] models = { "Rock_A", "Rock_B", "Rock_C" };
        Vector4[] rocks =
        {
            new Vector4(-8f, 0f, 21f, 1.6f), new Vector4(8.5f, 0f, 26f, 1.9f), new Vector4(-12f, 0f, -23f, 1.5f),
            new Vector4(12f, 0f, 2f, 1.7f), new Vector4(-10f, 0f, -2f, 1.4f), new Vector4(9f, 0f, -12f, 1.3f),
        };
        for (int i = 0; i < rocks.Length; i++)
        {
            Vector4 entry = rocks[i];
            Transform rock = Group("Rock", parent, new Vector3(entry.x, 0f, entry.z));
            Place(models[i % models.Length], rock, Vector3.zero, entry.x * 13f, entry.w);

            GameObject blocker = new GameObject("Blocker");
            blocker.transform.SetParent(rock, false);
            blocker.layer = GameLayers.Level;
            SphereCollider sphere = blocker.AddComponent<SphereCollider>();
            sphere.center = new Vector3(0f, entry.w * 0.25f, 0f);
            sphere.radius = entry.w * 0.85f;
            obstacles.Add(new Vector4(entry.x, 0f, entry.z, entry.w + 0.6f));
        }
    }

    private static void AddFences(Transform parent, List<Vector4> obstacles)
    {
        AddFence(parent, new Vector3(-7.5f, 0f, 30f), 12f, obstacles);
        AddFence(parent, new Vector3(7.5f, 0f, 11f), 10f, obstacles);
        AddFence(parent, new Vector3(-7.5f, 0f, -14f), 8f, obstacles);
    }

    private static void AddFence(Transform parent, Vector3 center, float length, List<Vector4> obstacles)
    {
        Transform item = Group("Fence", parent, center);
        AddFenceRun(item, new Vector3(0f, 0f, -length * 0.5f), new Vector3(0f, 0f, length * 0.5f), Mathf.RoundToInt(center.z), true);

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

    // ---- HUD ----

    private static void BuildHud()
    {
        LoadUiAssets();
        Canvas canvas = NewCanvas("HudCanvas", 10);
        MatchHud hud = canvas.gameObject.AddComponent<MatchHud>();
        Transform root = canvas.transform;

        RectTransform overlay = UiPanelRoot(root, "Overlays");
        hud.overlayRoot = overlay;
        hud.overlayFont = uiFont;
        hud.roundSprite = uiRound;
        hud.diamondSprite = UiSprite("UI_Diamond");
        hud.chevronSprite = UiSprite("UI_Chevron");

        hud.speedLines = FullScreenImage(root, "SpeedLines", UiSprite("UI_SpeedLines"));
        hud.vignette = FullScreenImage(root, "Vignette", UiSprite("UI_Vignette"));

        BuildScoreboard(root, hud);

        hud.bannerText = UiText(root, "Banner", "", 54, UiCream, 3.5f);
        Rect(hud.bannerText.gameObject, root, AnchorCentre, new Vector2(0f, 330f), new Vector2(1500f, 90f));

        RectTransform play = UiPanelRoot(root, "PlayHud");
        hud.playHud = play.gameObject;
        BuildCrosshair(play, hud);
        BuildRamChip(play, hud);
        BuildHealthChip(play, hud);
        // To the left of the map, where it's clear of the scoreboard.
        hud.feedRoot = UiGroup(play, "Feed", Vector2.one, new Vector2(-226f, -56f), new Vector2(10f, 10f));
        hud.streakText = UiText(play, "Streak", "", 72, new Color(1f, 0.82f, 0.25f), 4f);
        Rect(hud.streakText.gameObject, play, AnchorCentre, new Vector2(0f, 220f), new Vector2(1200f, 100f));
        BuildMiniMap(play);
        BuildRoleChip(play, hud);

        hud.hitArrow = UiImage(root, "HitArrow", hud.chevronSprite, Color.clear);
        Rect(hud.hitArrow.gameObject, root, AnchorCentre, Vector2.zero, new Vector2(120f, 120f));
        hud.hitArrow.enabled = false;

        hud.dizzyStars = UiGroup(root, "DizzyStars", AnchorCentre, new Vector2(0f, 190f), new Vector2(10f, 10f));
        Sprite star = UiSprite("UI_Star");
        for (int i = 0; i < 5; i++)
        {
            Image item = UiImage(hud.dizzyStars, "Star", star, Color.white);
            Rect(item.gameObject, hud.dizzyStars, AnchorCentre, Vector2.zero, new Vector2(84f, 84f));
        }
        hud.dizzyStars.gameObject.SetActive(false);

        BuildIntroCard(root, hud);
        BuildResults(root, hud);
        BuildPause(root, hud);

        Image fade = FullScreenImage(root, "ScreenFade", null);
        fade.color = new Color(0f, 0f, 0f, 0f);
        hud.screenFade = fade;
    }

    private static Image FullScreenImage(Transform parent, string name, Sprite sprite)
    {
        Image image = UiImage(parent, name, sprite, Color.clear);
        Fill(image.rectTransform);
        image.enabled = false;
        return image;
    }

    private static void BuildScoreboard(Transform root, MatchHud hud)
    {
        RectTransform board = UiGroup(root, "Scoreboard", AnchorTop, new Vector2(0f, -72f), new Vector2(860f, 124f));
        hud.orangeScoreText = ScorePlate(board, "Orange", UiOrange, -286f, out hud.orangeRoleText, out hud.orangeYou);
        hud.blueScoreText = ScorePlate(board, "Blue", UiBlue, 286f, out hud.blueRoleText, out hud.blueYou);

        RectTransform timer = UiGroup(board, "Timer", AnchorCentre, new Vector2(0f, 0f), new Vector2(280f, 124f));
        AddSoftShadow(timer, 0.4f);
        Image back = UiImage(timer, "Back", uiRound, UiNavy, 1.5f);
        Fill(back.rectTransform);
        hud.halfText = UiText(timer, "Half", "", 26, UiYellow);
        Rect(hud.halfText.gameObject, timer, AnchorTop, new Vector2(0f, -27f), new Vector2(260f, 30f));
        hud.timerText = UiText(timer, "Time", "", 74, Color.white);
        Rect(hud.timerText.gameObject, timer, AnchorCentre, new Vector2(0f, -15f), new Vector2(270f, 84f));
    }

    private static Text ScorePlate(Transform board, string name, Color color, float x, out Text role, out GameObject you)
    {
        RectTransform plate = UiGroup(board, name, AnchorCentre, new Vector2(x, 4f), new Vector2(320f, 106f));
        AddSoftShadow(plate, 0.4f);
        Image edge = UiImage(plate, "Edge", uiRound, UiNavy, 1.6f);
        Fill(edge.rectTransform);
        Image face = UiImage(edge.transform, "Face", uiFace, color, 1.6f);
        Fill(face.rectTransform, 6f, 6f, 6f, 6f);

        Text score = UiText(face.transform, "Score", "0", 66, Color.white, 3f);
        Rect(score.gameObject, face.transform, AnchorCentre, new Vector2(0f, 12f), new Vector2(300f, 70f));
        role = UiText(face.transform, "Role", "", 23, Color.white, 2f);
        Rect(role.gameObject, face.transform, AnchorBottom, new Vector2(0f, 19f), new Vector2(300f, 28f));

        RectTransform tag = UiGroup(board, name + "You", AnchorCentre, new Vector2(x, -66f), new Vector2(84f, 36f));
        Image tagBack = UiImage(tag, "Back", uiRound, UiNavy, 3f);
        Fill(tagBack.rectTransform);
        Text tagText = UiText(tag, "Text", GameText.You, 24, UiYellow);
        Fill(tagText.rectTransform);
        you = tag.gameObject;
        you.SetActive(false);
        return score;
    }

    private static void BuildCrosshair(RectTransform parent, MatchHud hud)
    {
        hud.crosshairRing = UiImage(parent, "Ring", UiSprite("UI_Ring"), Color.white);
        Rect(hud.crosshairRing.gameObject, parent, AnchorCentre, Vector2.zero, new Vector2(76f, 76f));
        hud.crosshairRing.enabled = false;
        hud.crosshairDot = UiImage(parent, "Dot", UiSprite("UI_Dot"), Color.white);
        Rect(hud.crosshairDot.gameObject, parent, AnchorCentre, Vector2.zero, new Vector2(26f, 26f));

        RectTransform bar = UiGroup(parent, "TargetBar", AnchorCentre, new Vector2(0f, -68f), new Vector2(236f, 24f));
        hud.targetBar = bar.gameObject;
        Image track = UiImage(bar, "Track", uiRound, UiNavy, 5f);
        Fill(track.rectTransform);
        hud.targetFill = UiImage(bar, "Fill", uiRound, UiGreen, 7f);
        Fill(hud.targetFill.rectTransform, 5f, 5f, 5f, 5f);

        hud.promptA = BuildPrompt(parent, "PromptA", new Vector2(0f, -118f));
        hud.promptB = BuildPrompt(parent, "PromptB", new Vector2(0f, -182f));
    }

    private static HudPrompt BuildPrompt(Transform parent, string name, Vector2 position)
    {
        RectTransform slot = UiGroup(parent, name, AnchorCentre, position, new Vector2(300f, 56f));
        HudPrompt prompt = slot.gameObject.AddComponent<HudPrompt>();
        Image back = UiImage(slot, "Back", uiRound, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.88f), 2.4f);
        Rect(back.gameObject, slot, AnchorCentre, Vector2.zero, new Vector2(300f, 56f));
        prompt.back = back.rectTransform;
        Image key = UiImage(slot, "Key", uiFace, UiCream, 3f);
        Rect(key.gameObject, slot, AnchorCentre, Vector2.zero, new Vector2(42f, 42f));
        prompt.keyBack = key.rectTransform;
        prompt.keyText = UiText(key.transform, "KeyText", "", 26, UiNavy);
        Fill(prompt.keyText.rectTransform);
        prompt.label = UiText(slot, "Label", "", 28, UiCream);
        Rect(prompt.label.gameObject, slot, AnchorCentre, Vector2.zero, new Vector2(200f, 56f));
        return prompt;
    }

    private static void BuildRamChip(Transform parent, MatchHud hud)
    {
        Vector2 size = new Vector2(330f, 74f);
        RectTransform chip = UiGroup(parent, "Ram", Vector2.zero, new Vector2(30f + size.x * 0.5f, 30f + size.y * 0.5f), size);
        AddSoftShadow(chip, 0.4f);
        Image edge = UiImage(chip, "Edge", uiRound, UiNavy, 2f);
        Fill(edge.rectTransform);
        Image track = UiImage(edge.transform, "Track", uiRound, new Color(0.2f, 0.24f, 0.38f), 2.4f);
        Fill(track.rectTransform, 7f, 7f, 7f, 7f);
        hud.ramFill = UiImage(track.transform, "Fill", uiFace, UiGreen, 2.4f);
        Fill(hud.ramFill.rectTransform);

        Image key = UiImage(chip, "Key", uiFace, UiCream, 3f);
        Rect(key.gameObject, chip, new Vector2(0f, 0.5f), new Vector2(68f, 1f), new Vector2(104f, 48f));
        Text keyText = UiText(key.transform, "KeyText", GameText.KeyRam, 25, UiNavy);
        Fill(keyText.rectTransform);
        hud.ramLabel = UiText(chip, "Label", "", 32, Color.white, 2.5f);
        Rect(hud.ramLabel.gameObject, chip, AnchorCentre, new Vector2(56f, 0f), new Vector2(200f, 50f));
    }

    private static void BuildHealthChip(Transform parent, MatchHud hud)
    {
        Vector2 size = new Vector2(330f, 60f);
        RectTransform chip = UiGroup(parent, "Health", Vector2.zero, new Vector2(30f + size.x * 0.5f, 30f + 74f + 12f + size.y * 0.5f), size);
        AddSoftShadow(chip, 0.4f);
        Image edge = UiImage(chip, "Edge", uiRound, UiNavy, 2f);
        Fill(edge.rectTransform);
        Image track = UiImage(edge.transform, "Track", uiRound, new Color(0.2f, 0.24f, 0.38f), 2.4f);
        Fill(track.rectTransform, 7f, 7f, 7f, 7f);
        hud.healthFill = UiImage(track.transform, "Fill", uiFace, UiGreen, 2.4f);
        Fill(hud.healthFill.rectTransform);

        Image key = UiImage(chip, "Key", uiFace, UiCream, 3f);
        Rect(key.gameObject, chip, new Vector2(0f, 0.5f), new Vector2(50f, 1f), new Vector2(68f, 40f));
        Text keyText = UiText(key.transform, "KeyText", GameText.HealthLabel, 25, UiNavy);
        Fill(keyText.rectTransform);
        hud.healthLabel = UiText(chip, "Label", "", 32, Color.white, 2.5f);
        Rect(hud.healthLabel.gameObject, chip, AnchorCentre, new Vector2(30f, 0f), new Vector2(200f, 50f));
    }

    private static void BuildMiniMap(Transform parent)
    {
        // The same shape as the level: 46 metres across and 98 long.
        const float inset = 9f;
        Vector2 size = new Vector2(176f, 356f);
        RectTransform map = UiGroup(parent, "Map", Vector2.one, new Vector2(-30f - size.x * 0.5f, -30f - size.y * 0.5f), size);
        AddSoftShadow(map, 0.4f);

        GameObject pictureObject = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        pictureObject.transform.SetParent(map, false);
        RawImage picture = pictureObject.GetComponent<RawImage>();
        picture.raycastTarget = false;
        Fill(picture.rectTransform, inset, inset, inset, inset);

        RectTransform dots = UiGroup(map, "Dots", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(dots, inset, inset, inset, inset);

        Image rim = UiImage(map, "Rim", uiFrame, UiNavy, 2f);
        Fill(rim.rectTransform);

        MiniMap miniMap = map.gameObject.AddComponent<MiniMap>();
        miniMap.picture = picture;
        miniMap.dots = dots;
        miniMap.dotSprite = UiSprite("UI_Dot");
        miniMap.arrowSprite = UiSprite("UI_Chevron");
    }

    private static void BuildRoleChip(Transform parent, MatchHud hud)
    {
        Vector2 size = new Vector2(462f, 54f);
        RectTransform chip = UiGroup(parent, "RoleChip", new Vector2(0f, 1f), new Vector2(30f + size.x * 0.5f, -30f - size.y * 0.5f), size);
        AddSoftShadow(chip, 0.35f);
        Image edge = UiImage(chip, "Edge", uiRound, UiNavy, 2.4f);
        Fill(edge.rectTransform);
        hud.roleChipBack = UiImage(edge.transform, "Face", uiFace, UiOrange, 2.8f);
        Fill(hud.roleChipBack.rectTransform, 5f, 5f, 5f, 5f);
        hud.roleChip = UiText(chip, "Text", "", 25, Color.white, 2f);
        Fill(hud.roleChip.rectTransform, 12f, 2f, 12f, 2f);
    }

    private static void BuildIntroCard(Transform root, MatchHud hud)
    {
        RectTransform card = UiGroup(root, "IntroCard", AnchorCentre, new Vector2(0f, 60f), new Vector2(1180f, 380f));
        card.gameObject.AddComponent<UiPopIn>();
        hud.introCard = card.gameObject;
        AddSoftShadow(card, 0.5f);
        Image edge = UiImage(card, "Edge", uiRound, UiNavy, 1.4f);
        Fill(edge.rectTransform);
        Image inside = UiImage(edge.transform, "Inside", uiRound, UiCream, 1.7f);
        Fill(inside.rectTransform, 8f, 8f, 8f, 8f);

        hud.introSwap = UiText(card, "Swap", GameText.SidesSwapped, 32, new Color(0.86f, 0.42f, 0.05f));
        Rect(hud.introSwap.gameObject, card, AnchorTop, new Vector2(0f, -40f), new Vector2(1100f, 40f));
        hud.introHeading = UiText(card, "Heading", "", 82, UiOrange, 3.5f);
        Rect(hud.introHeading.gameObject, card, AnchorTop, new Vector2(0f, -106f), new Vector2(1100f, 90f));
        hud.introGoal = UiText(card, "Goal", "", 44, UiNavy);
        Rect(hud.introGoal.gameObject, card, AnchorTop, new Vector2(0f, -182f), new Vector2(1100f, 56f));
        Text keys = UiText(card, "Keys", GameText.KeysHint, 23, new Color(0.34f, 0.37f, 0.48f));
        Rect(keys.gameObject, card, AnchorBottom, new Vector2(0f, 42f), new Vector2(1140f, 34f));

        RectTransform count = UiGroup(card, "Count", AnchorTop, new Vector2(0f, -272f), new Vector2(84f, 84f));
        Image countBack = UiImage(count, "Back", uiCircle, UiNavy);
        Fill(countBack.rectTransform);
        hud.introCount = UiText(count, "Number", "", 56, UiYellow);
        Fill(hud.introCount.rectTransform, 0f, 2f);
    }

    private static void BuildResults(Transform root, MatchHud hud)
    {
        RectTransform panel = UiPanelRoot(root, "Results");
        hud.resultsPanel = panel.gameObject;
        Image dim = UiImage(panel, "Dim", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.5f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform inside = UiPanel(panel, "Card", AnchorCentre, Vector2.zero, new Vector2(1160f, 780f), UiCream, 0f);
        hud.resultsHeading = UiText(inside, "Heading", "", 96, UiOrange, 4f);
        Rect(hud.resultsHeading.gameObject, inside, AnchorTop, new Vector2(0f, -100f), new Vector2(1080f, 110f));
        hud.resultsScore = UiText(inside, "Score", "", 78, UiNavy);
        Rect(hud.resultsScore.gameObject, inside, AnchorTop, new Vector2(0f, -212f), new Vector2(1080f, 90f));
        hud.resultsTitles = UiText(inside, "Titles", "", 32, new Color(0.34f, 0.37f, 0.48f));
        Rect(hud.resultsTitles.gameObject, inside, AnchorCentre, new Vector2(0f, -30f), new Vector2(1080f, 220f));

        hud.resultsWaiting = UiText(inside, "Waiting", GameText.WaitingForHost, 34, new Color(0.34f, 0.37f, 0.48f));
        Rect(hud.resultsWaiting.gameObject, inside, AnchorBottom, new Vector2(-270f, 92f), new Vector2(480f, 60f));
        hud.againButton = UiButton(inside, "Again", GameText.PlayAgain, AnchorBottom, new Vector2(-270f, 92f), new Vector2(500f, 112f), UiOrange, 44, 0.1f, out hud.againLabel);
        hud.leaveButton = UiButton(inside, "Leave", GameText.MainMenu, AnchorBottom, new Vector2(270f, 92f), new Vector2(500f, 112f), UiGray, 44, 0.15f, out _);
    }

    private static void BuildPause(Transform root, MatchHud hud)
    {
        RectTransform panel = UiPanelRoot(root, "Pause");
        hud.pausePanel = panel.gameObject;
        Image dim = UiImage(panel, "Dim", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.5f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform inside = UiPanel(panel, "Card", AnchorCentre, Vector2.zero, new Vector2(820f, 640f), UiCream, 0f);
        Text heading = UiText(inside, "Heading", GameText.Paused, 76, UiNavy);
        Rect(heading.gameObject, inside, AnchorTop, new Vector2(0f, -68f), new Vector2(760f, 90f));

        Text speed = UiText(inside, "SpeedLabel", GameText.LookSpeed, 34, new Color(0.86f, 0.42f, 0.05f));
        Rect(speed.gameObject, inside, AnchorTop, new Vector2(0f, -146f), new Vector2(760f, 44f));
        hud.lookSpeedSlider = UiSlider(inside, "LookSpeed", AnchorTop, new Vector2(0f, -196f), new Vector2(620f, 52f));
        Text hint = UiText(inside, "Hint", GameText.ViewHint, 26, new Color(0.34f, 0.37f, 0.48f));
        Rect(hint.gameObject, inside, AnchorTop, new Vector2(0f, -252f), new Vector2(780f, 36f));

        hud.pauseResumeButton = UiButton(inside, "Resume", GameText.KeepPlaying, AnchorBottom, new Vector2(0f, 186f), new Vector2(560f, 104f), UiOrange, 42, 0.05f, out _);
        hud.pauseLeaveButton = UiButton(inside, "LeaveMatch", GameText.LeaveMatch, AnchorBottom, new Vector2(0f, 66f), new Vector2(560f, 92f), UiGray, 38, 0.1f, out _);
    }

    private static Slider UiSlider(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        float trackInset = size.y * 0.27f;
        RectTransform root = UiGroup(parent, name, anchor, position, size);
        Image track = UiImage(root, "Track", uiRound, UiNavy, 4f);
        Fill(track.rectTransform, 0f, trackInset, 0f, trackInset);
        track.raycastTarget = true;

        RectTransform fillArea = UiGroup(root, "FillArea", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(fillArea, 6f, trackInset + 5f, 6f, trackInset + 5f);
        Image fill = UiImage(fillArea, "Fill", uiRound, UiOrange, 6f);
        Fill(fill.rectTransform);

        RectTransform handleArea = UiGroup(root, "HandleArea", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(handleArea, size.y * 0.5f, 0f, size.y * 0.5f, 0f);
        Image handleEdge = UiImage(handleArea, "Handle", uiCircle, UiNavy);
        handleEdge.raycastTarget = true;
        Fill(handleEdge.rectTransform);
        handleEdge.rectTransform.sizeDelta = new Vector2(size.y, 0f);
        Image handleFace = UiImage(handleEdge.transform, "Face", uiCircle, UiYellow);
        Fill(handleFace.rectTransform, 7f, 7f, 7f, 7f);

        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleEdge.rectTransform;
        slider.targetGraphic = handleEdge;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        return slider;
    }
}
