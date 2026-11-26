using System.Collections.Generic;
using System.Linq;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The level list and the start scene: a truck on an endless country road, with the title, main menu,
/// level select and lobby on top. The art comes from Tools/blender/make_ui.py and make_ground.py.
/// </summary>
public static partial class CargoClashBuilder
{
    private const string UiDir = Root + "/UI";

    // ---- Levels ----

    /// <summary>
    /// Every level the game shows, in order. A level with no scene is a locked card. To open one, make
    /// its scene in Assets/Game/Scenes and fill in its name here.
    /// </summary>
    private static LevelInfo[] BuildLevelList()
    {
        return new[]
        {
            new LevelInfo { title = "TUTORIAL", scene = "Level01", picture = CardPicture(1) },
            new LevelInfo { title = "FOREST ROAD", picture = CardPicture(2) },
            new LevelInfo { title = "MOUNTAIN PASS", picture = CardPicture(3) },
            new LevelInfo { title = "RIVER FLATS", scene = RiverSceneName, picture = CardPicture(4) },
            new LevelInfo { title = "???", picture = CardPicture(5) },
            new LevelInfo { title = "???", picture = CardPicture(6) },
        };
    }

    private static Sprite CardPicture(int number)
    {
        string name = $"Level{number:00}_Card";
        bool exists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID($"{UiDir}/{name}.png", AssetPathToGUIDOptions.OnlyExistingAssets));
        return exists ? UiSprite(name, mipmaps: true) : null;
    }

    // ---- The scene ----

    private static void BuildTitleScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddDaylight();
        AddCountrySky();

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 40f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.farClipPlane = 620f;
        camera.cullingMask = ~(1 << GameLayers.Preview);
        cameraObject.AddComponent<UniversalAdditionalCameraData>();

        GameObject truck = (GameObject)PrefabUtility.InstantiatePrefab(BuildTitleTruck());
        // In the right-hand lane, heading along +X. The truck is wider than a lane, so it sits a little over the centre line.
        truck.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1.9f), Quaternion.Euler(0f, -90f, 0f));

        CargoClashEndlessTerrain terrain = new GameObject("Countryside").AddComponent<CargoClashEndlessTerrain>();
        terrain.Configure(truck.transform, GroundMat("Title_Ground", "Title_Grass", TextureWrapMode.Repeat),
            CutoutGroundMat("Title_Road", "Title_Road"), CutoutGroundMat("Title_Fields", "Title_Fields"));

        CargoClashStartMenu stage = new GameObject("Stage").AddComponent<CargoClashStartMenu>();
        stage.Configure(truck.GetComponent<RoverTerrainAnimator>(), truck.transform, camera);

        AddTitleScenery(truck.transform);
        CrewPreview preview = BuildCrewPreview(out RenderTexture previewPicture);
        BuildTitleMenus(stage, preview, previewPicture);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        EditorSceneManager.SaveScene(scene, TitleScenePath);
    }

    private static Material SkyMaterial()
    {
        string path = MaterialDir + "/Title_Sky.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Skybox/Panoramic"));
            AssetDatabase.CreateAsset(material, path);
        }
        Texture2D sky = PaintedTexture("Title_Sky", TextureWrapMode.Clamp);
        // Compression would show as bands across such a smooth gradient.
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sky));
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        material.SetTexture("_MainTex", sky);
        material.SetFloat("_Mapping", 1f);
        material.SetFloat("_ImageType", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Ground with holes in it, for laying a road or fields over the grass.</summary>
    private static Material CutoutGroundMat(string name, string textureName)
    {
        Material material = Mat(name, Color.white, 0f);
        material.SetTexture("_BaseMap", PaintedTexture(textureName, TextureWrapMode.Repeat, true));
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Lit, so a puff looks like a little ball instead of a flat dot.</summary>
    private static Material PuffMaterial()
    {
        string path = MaterialDir + "/Title_Puff.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// The truck on the start screen: the same truck before the crash, with a full load. RoverTerrainAnimator
    /// moves the body and each wheel over the bumps, so the wheels are separate and the axles and shocks
    /// are stretched between them while it drives. The model is Astra's (独立建模/运输载具_v07, copied from
    /// 交接/旧接口兼容/TitleTruck.fbx): her two wheels are samples at the origin, copied onto each axle here.
    /// </summary>
    private static GameObject BuildTitleTruck()
    {
        GameObject root = new GameObject("TitleTruck");
        Transform body = Group("BodyVisual", root.transform);
        Transform wheels = Group("Wheels", root.transform);
        Transform suspension = Group("Suspension", root.transform);

        GameObject model = NewStaticModel("TitleTruck", null);
        SetLayerRecursively(model, GameLayers.Outlined);
        FindInModel(model, "Body").SetParent(body, false);
        FindInModel(model, "CargoBed").SetParent(body, false);
        Transform leftWheel = FindInModel(model, "Wheel_L");
        Transform rightWheel = FindInModel(model, "Wheel_R");

        Material dark = ModelMaterials()["Fleet_Dark"];
        Material steel = ModelMaterials()["Fleet_Steel"];
        float[] axleZ = { -2.75f, 0.15f, 2.95f };
        Transform[] pivots = new Transform[6];
        Transform[] mounts = new Transform[6];
        Transform[] shocks = new Transform[6];
        Transform[] feet = new Transform[6];
        Transform[] arms = new Transform[6];
        // In the order the animator expects: front left, front right, middle left and so on.
        for (int i = 0; i < 6; i++)
        {
            bool left = i % 2 == 0;
            float side = left ? -1f : 1f;
            float z = axleZ[i / 2];
            string label = $"{i / 2 + 1}_{(left ? "L" : "R")}";
            pivots[i] = Group("WheelPivot_" + label, wheels, new Vector3(side * 2.36f, 1.05f, z));
            Transform wheel = Object.Instantiate(left ? leftWheel : rightWheel, pivots[i]);
            wheel.name = "Wheel";
            wheel.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            mounts[i] = Group("ShockMount_" + label, body, new Vector3(side * 1.48f, 2.08f, z));
            shocks[i] = Part(PrimitiveType.Cylinder, "Shock_" + label, suspension, Vector3.zero, Vector3.one, steel).transform;
            feet[i] = Part(PrimitiveType.Cylinder, "ShockFoot_" + label, suspension, Vector3.zero, new Vector3(0.17f, 0.08f, 0.17f), dark).transform;
            arms[i] = Part(PrimitiveType.Cube, "TrailingArm_" + label, suspension, Vector3.zero, new Vector3(0.3f, 0.2f, 1f), dark).transform;
        }
        Transform[] axles = new Transform[3];
        Transform[] differentials = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            axles[i] = Part(PrimitiveType.Cylinder, $"Axle_{i + 1}", suspension, Vector3.zero, Vector3.one, dark).transform;
            differentials[i] = Part(PrimitiveType.Sphere, $"Differential_{i + 1}", suspension, Vector3.zero, new Vector3(0.84f, 0.64f, 0.68f), steel).transform;
        }
        Object.DestroyImmediate(model);

        AddTitleLoad(Group("Load", body));

        // Exhaust from the stack behind the cab, and dust thrown up by the back wheels.
        Color smokeDark = new Color(0.72f, 0.73f, 0.76f);
        Color smokeLight = new Color(0.93f, 0.93f, 0.94f);
        Color dustDark = new Color(0.72f, 0.63f, 0.47f);
        Color dustLight = new Color(0.86f, 0.79f, 0.63f);
        Material puff = PuffMaterial();
        // Her exhaust stack runs up the back corner of the cab.
        AddPuffs(body, "Exhaust", new Vector3(-1.25f, 4.05f, -1.15f), 7f, 24, smokeDark, smokeLight, 0.16f, 0.3f, 1f, 1.7f, puff);
        AddPuffs(body, "DustLeft", new Vector3(-2.36f, 0.25f, 3.7f), 9f, 24, dustDark, dustLight, 0.14f, 0.34f, 0.6f, 1.1f, puff);
        AddPuffs(body, "DustRight", new Vector3(2.36f, 0.25f, 3.7f), 9f, 24, dustDark, dustLight, 0.14f, 0.34f, 0.6f, 1.1f, puff);

        RoverTerrainAnimator rover = root.AddComponent<RoverTerrainAnimator>();
        rover.ConfigureRig(body, pivots, mounts, shocks, feet, arms, axles, differentials);
        return SavePrefab(root, "TitleTruck");
    }

    /// <summary>
    /// The full load on the start screen's truck: Astra's four kinds (the same models as in a match, whole and shut), in
    /// stacks along both sides of the bed. Her cargo is made for people, so it's a size small in a bed three metres
    /// wide, and the side walls are a metre high (the floor is at 2.02, the walls' tops at 3.0): the load is drawn
    /// 1.3 times bigger and piled high, with the goose crates on top on the far side, where the camera on the +X side
    /// sees them. Her cases face +Z with their handles along X, so a quarter turn shows their fronts to the camera. Each
    /// stack lists its kinds from the bottom up, each a little twisted.
    /// </summary>
    private static void AddTitleLoad(Transform load)
    {
        const float scale = 1.3f;
        (float x, float z, CargoKind[] stack)[] stacks =
        {
            (-0.8f, 0.15f, new[] { CargoKind.Gold, CargoKind.Gold, CargoKind.Art }),
            (-0.8f, 1.27f, new[] { CargoKind.Cash, CargoKind.Cash, CargoKind.Cash, CargoKind.Art }),
            (-0.82f, 2.39f, new[] { CargoKind.Gold, CargoKind.Cash, CargoKind.Gold, CargoKind.Tobacco, CargoKind.Tobacco }),
            (-0.8f, 3.5f, new[] { CargoKind.Cash, CargoKind.Gold, CargoKind.Art }),
            (0.78f, 0.15f, new[] { CargoKind.Gold, CargoKind.Cash, CargoKind.Gold, CargoKind.Tobacco }),
            (0.78f, 1.27f, new[] { CargoKind.Tobacco, CargoKind.Tobacco, CargoKind.Cash, CargoKind.Gold }),
            (0.78f, 2.39f, new[] { CargoKind.Cash, CargoKind.Gold, CargoKind.Cash, CargoKind.Tobacco }),
            (0.78f, 3.5f, new[] { CargoKind.Gold, CargoKind.Tobacco, CargoKind.Cash, CargoKind.Tobacco }),
        };
        System.Random random = new System.Random(14);
        foreach ((float x, float z, CargoKind[] stack) in stacks)
        {
            float floor = 2.02f;
            foreach (CargoKind kind in stack)
            {
                float yaw = 90f + (float)(random.NextDouble() * 12.0 - 6.0);
                Vector3 nudge = new Vector3((float)(random.NextDouble() - 0.5) * 0.08f, 0f, (float)(random.NextDouble() - 0.5) * 0.08f);
                floor += PlaceTitleCargo(kind, load, new Vector3(x, floor, z) + nudge, yaw, scale);
            }
        }
    }

    /// <summary>A still copy of one of Astra's kinds, shut and undamaged. Returns its height.</summary>
    private static float PlaceTitleCargo(CargoKind kind, Transform parent, Vector3 position, float yaw, float scale)
    {
        GameObject model = NewAstraModel("Cargo_" + kind, parent);
        model.name = "Cargo_" + kind;
        Object.DestroyImmediate(FindInModel(model, "Stage1").gameObject);
        Object.DestroyImmediate(FindInModel(model, "Stage2").gameObject);
        // What's inside a shut case can't be seen, except the goose between the crate's slats.
        if (kind != CargoKind.Art) Object.DestroyImmediate(FindInModel(model, "Slot0").gameObject);
        model.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        model.transform.localScale = Vector3.one * scale;
        SetLayerRecursively(model, GameLayers.Outlined);

        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
        {
            if (first) bounds = part.bounds;
            else bounds.Encapsulate(part.bounds);
            first = false;
        }
        return bounds.max.y - model.transform.position.y;
    }

    // ---- Scenery ----

    private static Transform SceneryBand(string name, Transform truck, float period, float behind, bool onGround = true, bool followSlope = false)
    {
        Transform band = Group(name, null);
        TitleScenery scenery = band.gameObject.AddComponent<TitleScenery>();
        scenery.truck = truck;
        scenery.period = period;
        scenery.behind = behind;
        scenery.onGround = onGround;
        scenery.followSlope = followSlope;
        return band;
    }

    private static float Between(System.Random random, float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());

    /// <summary>
    /// Everything the truck drives past, in bands by distance from the road. The farther a band is,
    /// the wider the view it fills, so the longer it has to be before it repeats.
    /// </summary>
    private static void AddTitleScenery(Transform truck)
    {
        System.Random random = new System.Random(4122);

        // Beside the road. The fence spans and the poles all fit the same 96-metre repeat exactly.
        const float nearStart = -40f;
        const float nearPeriod = 96f;
        const float fenceSpan = 2.4f;
        const float poleSpan = 24f;
        Transform fences = SceneryBand("Fences", truck, nearPeriod, -nearStart, true, true);
        for (int i = 0; i < Mathf.RoundToInt(nearPeriod / fenceSpan); i++)
            Place("Fence_Rail", fences, new Vector3(nearStart + (i + 0.5f) * fenceSpan, 0f, 8.8f), 90f);

        // The wires only join up if every pole is the same height, so these are sunk far enough to clear the highest bump.
        Transform poles = SceneryBand("Poles", truck, nearPeriod, -nearStart, false);
        for (int i = 0; i < Mathf.RoundToInt(nearPeriod / poleSpan); i++)
            Place("UtilityPole", poles, new Vector3(nearStart + i * poleSpan, -1.1f, 19f), -90f);

        Transform roadside = SceneryBand("Roadside", truck, nearPeriod, -nearStart);
        Place("Billboard_Time", roadside, new Vector3(-16f, 0f, 14f), 4f);
        Place("Billboard_Safe", roadside, new Vector3(33f, 0f, 14f), -5f);
        Place("Sign_Warning", roadside, new Vector3(8f, 0f, 7.4f), 12f);
        for (int i = 0; i < 3; i++) Place("TrafficCone", roadside, new Vector3(18f + i * 2.6f, 0f, -6.2f), i * 37f);
        string[] flowers = { "Flowers_White", "Flowers_Yellow", "Flowers_Pink" };
        string[] bushes = { "Bush_Green", "Bush_Dark", "Bush_Flower" };
        string[] rocks = { "Rock_A", "Rock_B", "Rock_C" };
        for (int i = 0; i < 26; i++)
        {
            float x = Between(random, nearStart, nearStart + nearPeriod);
            float z = i % 3 == 0 ? Between(random, 9.6f, 13f) : Between(random, -16f, -8f);
            AddClump(roadside, new Vector3(x, 0f, z), random, flowers[random.Next(flowers.Length)], random.NextDouble() < 0.5);
        }
        for (int i = 0; i < 9; i++)
        {
            float x = Between(random, nearStart, nearStart + nearPeriod);
            Place(bushes[i % bushes.Length], roadside, new Vector3(x, 0f, Between(random, 10f, 13f)), Between(random, 0f, 360f), Between(random, 0.9f, 1.4f));
        }
        for (int i = 0; i < 4; i++)
        {
            float x = Between(random, nearStart, nearStart + nearPeriod);
            Place(rocks[i % rocks.Length], roadside, new Vector3(x, 0f, Between(random, -14f, -8.5f)), Between(random, 0f, 360f), Between(random, 0.7f, 1.1f));
        }

        // The farm and the trees behind the fence, down to where the fields begin.
        const float midStart = -90f;
        const float midPeriod = 192f;
        Transform middle = SceneryBand("Farm and trees", truck, midPeriod, -midStart);
        Place("Barn", middle, new Vector3(12f, 0f, 37f), 180f, 1.1f);
        Place("Silo", middle, new Vector3(24f, 0f, 40f), 0f, 1.1f);
        foreach (Vector3 spot in new[] { new Vector3(3f, 0f, 27f), new Vector3(6.4f, 0f, 24.5f), new Vector3(16f, 0f, 26f), new Vector3(20f, 0f, 29f) })
            Place("HayBale", middle, spot, spot.x * 47f);
        Place("Barn", middle, new Vector3(-62f, 0f, 41f), 170f);
        string[] nearTrees = { "Tree_RoundA", "Tree_RoundB", "Tree_RoundA", "Tree_RoundC", "Tree_Tall", "Tree_Pine" };
        for (int cluster = 0; cluster < 9; cluster++)
        {
            float x = midStart + (cluster + Between(random, 0.1f, 0.9f)) * midPeriod / 9f;
            // Leaves the farm in the clear.
            if (x > -2f && x < 34f) continue;
            float z = Between(random, 20f, 42f);
            int trees = random.Next(2, 6);
            for (int i = 0; i < trees; i++)
            {
                Vector3 spot = new Vector3(x + Between(random, -7f, 7f), 0f, z + Between(random, -5f, 5f));
                Place(nearTrees[random.Next(nearTrees.Length)], middle, spot, Between(random, 0f, 360f), Between(random, 0.95f, 1.5f));
            }
        }

        // Far off: lines of trees between the fields, hills and clouds.
        const float farStart = -240f;
        const float farPeriod = 480f;
        Transform distant = SceneryBand("Distant trees", truck, farPeriod, -farStart);
        string[] farTrees = { "Tree_RoundA", "Tree_RoundB", "Tree_Tall", "Tree_Pine", "Tree_RoundC" };
        for (int line = 0; line < 3; line++)
        {
            float z = 66f + line * 38f;
            for (int i = 0; i < 26; i++)
            {
                float x = farStart + (i + Between(random, 0f, 0.8f)) * farPeriod / 26f;
                if (random.NextDouble() < 0.3) continue;
                Place(farTrees[random.Next(farTrees.Length)], distant, new Vector3(x, 0f, z + Between(random, -4f, 4f)), Between(random, 0f, 360f), Between(random, 1.3f, 2.2f));
            }
        }

        Transform hills = SceneryBand("Hills", truck, farPeriod, -farStart, false);
        Material hillNear = Mat("Level_Hill", new Color(0.36f, 0.57f, 0.38f));
        Material hillFar = Mat("Level_HillFar", new Color(0.55f, 0.71f, 0.69f));
        for (int i = 0; i < 6; i++)
        {
            float x = farStart + i * farPeriod / 6f;
            Part(PrimitiveType.Sphere, "Hill", hills, new Vector3(x, 0f, 215f), new Vector3(Between(random, 120f, 170f), Between(random, 26f, 46f), 80f), hillNear);
            Part(PrimitiveType.Sphere, "HillFar", hills, new Vector3(x + 40f, 0f, 270f), new Vector3(Between(random, 170f, 220f), Between(random, 60f, 96f), 90f), hillFar);
        }

        Transform clouds = SceneryBand("Clouds", truck, farPeriod, -farStart, false);
        for (int i = 0; i < 12; i++)
        {
            float x = farStart + (i + Between(random, 0f, 0.7f)) * farPeriod / 12f;
            Place(i % 2 == 0 ? "Cloud_A" : "Cloud_B", clouds, new Vector3(x, Between(random, 30f, 52f), Between(random, 150f, 205f)), Between(random, -12f, 12f), Between(random, 1.1f, 1.9f));
        }
    }

    // ---- Interface helpers ----

    private static readonly Color UiNavy = new Color(0.09f, 0.12f, 0.24f);
    private static readonly Color UiYellow = new Color(1f, 0.8f, 0.15f);
    // The main menu's plates take the truck's colours (step 25), sRGB as in the mockup.
    private static readonly Color LiveryRim = Html("131A3A");
    private static readonly Color LiveryShadow = Html("0D1330");
    private static readonly Color LiveryCream = Html("FFF0D3");
    private static readonly Color LiveryText = Html("FFF4DE");
    private static readonly Color LiveryOrange = Html("EE7628");
    private static readonly Color LiveryNavy = Html("26346F");
    private static readonly Color LiveryGold = Html("E9B243");
    private static readonly Color LiveryOffPanel = Html("4A557F");
    private static readonly Color LiveryOffTrim = Html("CFC8B8");
    private static readonly Color LiveryOffText = Html("AAB2CF");
    private static readonly Color DimmedPicture = new Color(0.78f, 0.8f, 0.86f);
    private static readonly Color LiveryHealth = Html("5DBB63");
    private static readonly Color LiveryTeamBlue = Html("3B7BE0");
    private static readonly Color LiveryOrangeTint = Html("FFE6CC");
    private static readonly Color LiveryBlueTint = Html("DDE7FB");
    private static readonly Color LiverySection = Html("D9661F");
    private static readonly Color LiverySpeed = Html("4C83E0");
    private static readonly Vector2 AnchorCentre = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 AnchorTop = new Vector2(0.5f, 1f);
    private static readonly Vector2 AnchorBottom = new Vector2(0.5f, 0f);

    private static Font uiFont;
    private static Sprite uiRound;
    private static Sprite uiSoft;
    private static Sprite uiCircle;
    private static Sprite uiFrame;
    private static Sprite uiLock;
    private static Sprite uiPlate;
    private static Sprite uiPlateShade;
    private static Sprite uiPinstripe;
    private static Sprite uiHeadlight;

    private static void LoadUiAssets()
    {
        uiFont = AssetDatabase.LoadAssetAtPath<Font>(UiDir + "/LilitaOne-Regular.ttf");
        uiRound = UiSprite("UI_Round", 48);
        uiSoft = UiSprite("UI_Soft", 100);
        uiCircle = UiSprite("UI_Circle");
        uiFrame = UiSprite("UI_Frame", 48);
        uiLock = UiSprite("UI_Lock");
        uiPlate = UiSprite("UI_Plate", 48, mipmaps: true);
        uiPlateShade = UiSprite("UI_PlateShade", 48, mipmaps: true);
        uiPinstripe = UiSprite("UI_Pinstripe", 24, mipmaps: true);
        uiHeadlight = UiSprite("UI_Headlight", mipmaps: true);
    }

    /// <summary>Import settings for a picture in Assets/Game/UI. `border` is how much of each edge doesn't stretch.</summary>
    private static Sprite UiSprite(string name, int border = 0, bool repeat = false, bool mipmaps = false)
    {
        string path = $"{UiDir}/{name}.png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = Vector4.one * border;
        importer.mipmapEnabled = mipmaps;
        importer.alphaIsTransparency = true;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static RectTransform UiGroup(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        return Rect(new GameObject(name, typeof(RectTransform)), parent, anchor, position, size);
    }

    /// <summary>Stretches to fill the parent, less these margins.</summary>
    private static void Fill(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = AnchorCentre;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static RectTransform UiPanelRoot(Transform parent, string name)
    {
        RectTransform panel = UiGroup(parent, name, AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(panel);
        return panel;
    }

    /// <summary>A sized slot that springs in when shown. Whatever goes inside is free to scale itself.</summary>
    private static RectTransform PopSlot(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float delay)
    {
        RectTransform slot = UiGroup(parent, name, anchor, position, size);
        slot.gameObject.AddComponent<UiPopIn>().delay = delay;
        return slot;
    }

    /// <summary>`cornerScale` above 1 makes the corners of a stretched sprite tighter.</summary>
    private static Image UiImage(Transform parent, string name, Sprite sprite, Color color, float cornerScale = 1f)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerScale;
        }
        return image;
    }

    private static Text UiText(Transform parent, string name, string content, int size, Color color, float outline = 0f)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        item.transform.SetParent(parent, false);
        Text text = item.GetComponent<Text>();
        text.font = uiFont;
        text.text = content;
        item.AddComponent<LocalizedText>().english = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        if (outline > 0f)
        {
            Outline line = item.AddComponent<Outline>();
            line.effectColor = UiNavy;
            line.effectDistance = new Vector2(outline, -outline);
        }
        return text;
    }

    private static void AddSoftShadow(RectTransform slot, float strength = 0.45f)
    {
        Image shadow = UiImage(slot, "Shadow", uiSoft, new Color(0f, 0f, 0f, strength));
        Fill(shadow.rectTransform, -30f, -42f, -30f, -22f);
    }

    // ---- The screens ----

    /// <summary>
    /// Every body type on a stand high above the countryside, where the stage camera never looks and on a
    /// layer it doesn't draw anyway. A camera of its own draws whichever is picked into a picture for the
    /// crew page. The sun lights it like everything else.
    /// </summary>
    private static CrewPreview BuildCrewPreview(out RenderTexture picture)
    {
        string picturePath = UiDir + "/CrewPreview.renderTexture";
        AssetDatabase.DeleteAsset(picturePath);
        picture = new RenderTexture(720, 900, 24, RenderTextureFormat.ARGB32) { name = "CrewPreview", antiAliasing = 4 };
        AssetDatabase.CreateAsset(picture, picturePath);

        GameObject root = new GameObject("CrewPreview");
        root.transform.position = new Vector3(0f, 300f, 0f);
        CrewPreview preview = root.AddComponent<CrewPreview>();
        Transform turntable = Group("Turntable", root.transform);
        preview.turntable = turntable;
        turntable.localRotation = Quaternion.Euler(0f, 200f, 0f);

        Material stand = Mat("Preview_Stand", new Color(0.82f, 0.86f, 0.78f), 0.1f);
        Part(PrimitiveType.Cylinder, "Stand", turntable, new Vector3(0f, -0.04f, 0f), new Vector3(1.5f, 0.04f, 1.5f), stand);

        List<CrewModel> crews = new List<CrewModel>();
        foreach ((BodyType body, float runRate) in CrewBodies)
        {
            CrewModel crew = NewCrewModel(body, runRate, turntable, false);
            crew.gameObject.SetActive(body == BodyType.Medium);
            crews.Add(crew);
        }
        preview.crews = crews.ToArray();

        GameObject cameraObject = new GameObject("PreviewCamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.05f, -4.6f);
        cameraObject.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 28f;
        camera.nearClipPlane = 0.5f;
        camera.farClipPlane = 12f;
        camera.cullingMask = 1 << GameLayers.Preview;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.8f, 0.87f, 0.92f);
        camera.targetTexture = picture;
        UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        SetLayerRecursively(root, GameLayers.Preview);
        root.SetActive(false);
        return preview;
    }

    private static void BuildTitleMenus(CargoClashStartMenu stage, CrewPreview preview, RenderTexture previewPicture)
    {
        LoadUiAssets();
        Canvas canvas = NewCanvas("GameMenuCanvas", 200);
        // The screens are laid out for 1920x1080. This keeps at least that much room in a window of any shape.
        canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        TitleFlow flow = new GameObject("TitleFlow").AddComponent<TitleFlow>();
        flow.stage = stage;
        Sprite logo = UiSprite("UI_Logo", mipmaps: true);

        BuildMenuPanel(canvas.transform, flow, logo);
        BuildLevelPanel(canvas.transform, flow);
        BuildLobbyPanel(canvas.transform, flow);
        BuildCrewPanel(canvas.transform, flow, preview, previewPicture);
        flow.settings = BuildSettingsPanel(canvas.transform);
        EditorUtility.SetDirty(flow);
    }

    // The main menu's column down the left. MenuDeliveryLoop moves the picture behind it over by this much of the width.
    private const float MenuLeft = 84f;
    private const float MenuWidth = 520f;

    /// <summary>
    /// The main menu: the logo, then one column of buttons down the left, in order of how much they're used. Practising
    /// with bots is the big orange one, with Astra's gold case on it; rooms come next; the crew page, settings and quit
    /// are three small buttons with pictures (make_ui.py -- --only menu). The start scene's loop plays to the right, and
    /// the buttons are painted like the truck in it (LiveryPlate).
    /// </summary>
    private static void BuildMenuPanel(Transform canvas, TitleFlow flow, Sprite logo)
    {
        RectTransform panel = UiPanelRoot(canvas, "MenuPanel");
        flow.menuPanel = panel.gameObject;
        panel.gameObject.SetActive(false);
        Vector2 topLeft = new Vector2(0f, 1f);
        float middle = MenuLeft + MenuWidth / 2f;

        RectTransform slot = PopSlot(panel, "Logo", topLeft, new Vector2(middle, -178f), new Vector2(570f, 317f), 0f);
        Image picture = UiImage(slot, "Picture", logo, Color.white);
        Fill(picture.rectTransform);
        picture.gameObject.AddComponent<UiFloat>().bobHeight = 5f;

        flow.practiceButton = LiveryButton(panel, "Practice", GameText.Practice, new Vector2(middle, -414f), new Vector2(MenuWidth, 122f),
            LiveryOrange, 42, 0.05f, UiSprite("Icon_Cargo"), 104f, 128f, 3f);
        flow.createButton = LiveryButton(panel, "Create", GameText.CreateRoomButton, new Vector2(middle, -546f), new Vector2(MenuWidth, 100f),
            LiveryNavy, 38, 0.1f, UiSprite("Icon_Truck"), 86f, 108f, 2f);
        flow.codeInput = LiveryCodeInput(panel, new Vector2(MenuLeft + 166f, -662f), new Vector2(332f, 88f), 0.15f);
        flow.joinButton = LiveryJoinButton(panel, new Vector2(MenuLeft + MenuWidth - 88f, -662f), new Vector2(176f, 88f), 0.15f);

        const float gap = 14f;
        float small = (MenuWidth - gap * 2f) / 3f;
        Vector2 smallSize = new Vector2(small, 136f);
        flow.crewButton = LiverySmallButton(panel, "Crew", GameText.Crew, new Vector2(MenuLeft + small / 2f, -800f), smallSize, 0.2f, UiSprite("Icon_Hat"));
        flow.settingsButton = LiverySmallButton(panel, "Settings", GameText.Settings, new Vector2(MenuLeft + small * 1.5f + gap, -800f), smallSize, 0.22f, UiSprite("Icon_Gear"));
        flow.quitButton = LiverySmallButton(panel, "Quit", GameText.Quit, new Vector2(MenuLeft + small * 2.5f + gap * 2f, -800f), smallSize, 0.24f, UiSprite("Icon_Power"));

        flow.statusText = UiText(panel, "Status", "", 28, UiYellow, 2f);
        Rect(flow.statusText.gameObject, panel, topLeft, new Vector2(middle, -914f), new Vector2(MenuWidth + 40f, 80f));
        flow.statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    // ---- The main menu's plates (step 25) ----
    // Painted like the truck that drives past behind them: a navy rim, a cream trim, a coloured panel with a thin cream
    // pinstripe, and the picture on a gold-ringed lamp like the truck's headlights. Radii are in canvas pixels; UI_Plate is
    // UI_Round's 44-pixel corner with mip maps, so a corner multiplier of 44 / radius gives that radius.

    private const float PlateDrop = 7f;
    private const float CardDrop = 4f;

    private static Color Html(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }

    private static Color Faded(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    private static float Corner(float radius) => 44f / radius;

    private static float PinstripeCorner(float radius) => 20f / radius;

    /// <summary>
    /// A plate's body: a hard navy shadow under it, then the rim, the cream trim and the panel with its shading. The whole
    /// plate sinks onto its shadow when pressed, and its panel, trim and words dim while it can't be used.
    /// </summary>
    private static RectTransform LiveryPlate(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color panelColor,
        float corner, float delay, out Button button, out UiButtonFeel feel, out Image trim, out Image panel, float depth = PlateDrop)
    {
        RectTransform slot = PopSlot(parent, name, anchor, position, size, delay);
        AddSoftShadow(slot, 0.3f);
        Image body = UiImage(slot, "Button", uiPlate, Color.clear, Corner(corner));
        Fill(body.rectTransform);
        body.raycastTarget = true;
        Image drop = UiImage(body.transform, "Drop", uiPlate, LiveryShadow, Corner(corner));
        Fill(drop.rectTransform, 0f, -depth, 0f, depth);
        RectTransform plate = UiGroup(body.transform, "Plate", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(plate);
        Image rim = UiImage(plate, "Rim", uiPlate, LiveryRim, Corner(corner));
        Fill(rim.rectTransform);
        trim = UiImage(plate, "Trim", uiPlate, LiveryCream, Corner(corner - 5f));
        Fill(trim.rectTransform, 5f, 5f, 5f, 5f);
        panel = UiImage(plate, "Panel", uiPlate, panelColor, Corner(corner - 9f));
        Fill(panel.rectTransform, 10f, 10f, 10f, 10f);
        Image shade = UiImage(plate, "Shade", uiPlateShade, Color.white, Corner(corner - 9f));
        Fill(shade.rectTransform, 10f, 10f, 10f, 10f);

        button = body.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = body;
        feel = body.gameObject.AddComponent<UiButtonFeel>();
        feel.sink = plate;
        feel.hoverScale = 1.04f;
        feel.pressDepth = Mathf.Min(5f, depth - 1f);
        return plate;
    }

    /// <summary>
    /// A button's picture on a headlight: a gold ring round a cream lens. The pictures are wide (the case and the truck fill
    /// nine tenths of their width and six tenths of their height), so on the wide buttons they sit across the lamp a little
    /// wider than it, with the ring showing above and below.
    /// </summary>
    private static void LiveryLamp(Transform parent, Vector2 anchor, Vector2 centre, float size, Sprite icon, float iconSize, float iconLift,
        out Image lamp, out Image picture)
    {
        Image under = UiImage(parent, "LampShadow", uiCircle, LiveryRim);
        Rect(under.gameObject, parent, anchor, centre + new Vector2(0f, -4f), Vector2.one * size);
        lamp = UiImage(parent, "Lamp", uiHeadlight, Color.white);
        Rect(lamp.gameObject, parent, anchor, centre, Vector2.one * size);
        picture = UiImage(parent, "Icon", icon, Color.white);
        picture.preserveAspect = true;
        Rect(picture.gameObject, parent, anchor, centre + new Vector2(0f, iconLift), Vector2.one * iconSize);
    }

    /// <summary>A plate's word, with a hard shadow under it. A long one gets smaller rather than wrapping.</summary>
    private static Text LiveryLabel(Transform parent, string content, int fontSize, Color color, Color shadow, float drop)
    {
        Text label = UiText(parent, "Label", content, fontSize, color);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = fontSize - 14;
        label.resizeTextMaxSize = fontSize;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        Shadow cast = label.gameObject.AddComponent<Shadow>();
        cast.effectColor = shadow;
        cast.effectDistance = new Vector2(0f, -drop);
        return label;
    }

    /// <summary>Too low for two lines, so best fit shrinks the word instead of wrapping it.</summary>
    private static void OneLine(Text label, float left, float right, float lift)
    {
        RectTransform line = label.rectTransform;
        line.anchorMin = new Vector2(0f, 0.5f);
        line.anchorMax = new Vector2(1f, 0.5f);
        line.offsetMin = new Vector2(left, -label.fontSize * 0.75f + lift);
        line.offsetMax = new Vector2(-right, label.fontSize * 0.75f + lift);
    }

    /// <summary>A wide plate with a pinstripe, the picture's lamp at the left and the word beside it.</summary>
    private static Button LiveryButton(Transform parent, string name, string content, Vector2 position, Vector2 size, Color panelColor,
        int fontSize, float delay, Sprite icon, float lampSize, float iconSize, float iconLift, Vector2? anchor = null)
    {
        RectTransform plate = LiveryPlate(parent, name, anchor ?? new Vector2(0f, 1f), position, size, panelColor, 24f, delay, out Button button, out UiButtonFeel feel,
            out Image trim, out Image panel);
        Image pinstripe = UiImage(plate, "Pinstripe", uiPinstripe, Faded(LiveryCream, 0.55f), PinstripeCorner(9f));
        Fill(pinstripe.rectTransform, 18f, 18f, 18f, 18f);
        LiveryLamp(plate, new Vector2(0f, 0.5f), new Vector2(19f + lampSize / 2f, 0f), lampSize, icon, iconSize, iconLift,
            out Image lamp, out Image picture);
        Text label = LiveryLabel(plate, content, fontSize, LiveryText, LiveryRim, 3f);
        OneLine(label, 31f + lampSize, 21f, 2f);
        feel.dimmed = new Graphic[] { panel, trim, pinstripe, lamp, picture, label };
        feel.dimmedColors = new[] { LiveryOffPanel, LiveryOffTrim, Faded(LiveryCream, 0.2f), DimmedPicture, DimmedPicture, LiveryOffText };
        return button;
    }

    /// <summary>One of the three small navy plates under the rooms: the picture's lamp over the word.</summary>
    private static Button LiverySmallButton(Transform parent, string name, string content, Vector2 position, Vector2 size, float delay, Sprite icon)
    {
        RectTransform plate = LiveryPlate(parent, name, new Vector2(0f, 1f), position, size, LiveryNavy, 22f, delay, out Button button, out UiButtonFeel feel,
            out Image trim, out Image panel);
        LiveryLamp(plate, new Vector2(0.5f, 1f), new Vector2(0f, -52f), 78f, icon, 72f, 0f, out Image lamp, out Image picture);
        Text label = LiveryLabel(plate, content, 25, LiveryText, LiveryRim, 2f);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 15f);
        rect.sizeDelta = new Vector2(-24f, 30f);
        label.resizeTextMinSize = 17;
        feel.hoverScale = 1.05f;
        feel.dimmed = new Graphic[] { panel, trim, lamp, picture, label };
        feel.dimmedColors = new[] { LiveryOffPanel, LiveryOffTrim, DimmedPicture, DimmedPicture, LiveryOffText };
        return button;
    }

    /// <summary>JOIN: a gold plate, dark until there is a room code to join.</summary>
    private static Button LiveryJoinButton(Transform parent, Vector2 position, Vector2 size, float delay)
    {
        RectTransform plate = LiveryPlate(parent, "Join", new Vector2(0f, 1f), position, size, LiveryGold, 20f, delay, out Button button, out UiButtonFeel feel,
            out Image trim, out Image panel);
        Text label = LiveryLabel(plate, GameText.Join, 34, LiveryRim, Faded(Color.white, 0.4f), 2f);
        OneLine(label, 12f, 12f, 2f);
        feel.dimmed = new Graphic[] { panel, trim, label };
        feel.dimmedColors = new[] { LiveryOffPanel, LiveryOffTrim, LiveryOffText };
        return button;
    }

    /// <summary>A page's title: cream like the plates' words, with a deeper hard shadow.</summary>
    private static Text LiveryHeading(Transform parent, string content, int fontSize)
    {
        Text heading = UiText(parent, "Text", content, fontSize, LiveryText);
        Shadow cast = heading.gameObject.AddComponent<Shadow>();
        cast.effectColor = LiveryShadow;
        cast.effectDistance = new Vector2(0f, -6f);
        return heading;
    }

    /// <summary>BACK, at the top left of a page: a navy plate with the arrow on a small headlight.</summary>
    private static Button LiveryBackButton(Transform parent, Vector2 position, Vector2 size, float delay)
    {
        RectTransform plate = LiveryPlate(parent, "Back", new Vector2(0f, 1f), position, size, LiveryNavy, 22f, delay, out Button button, out UiButtonFeel feel,
            out Image trim, out Image panel);
        float lampSize = size.y - 28f;
        LiveryLamp(plate, new Vector2(0f, 0.5f), new Vector2(19f + lampSize / 2f, 0f), lampSize, UiSprite("UI_Back"), lampSize * 0.62f, 0f,
            out Image lamp, out Image arrow);
        arrow.color = LiveryRim;
        Text label = LiveryLabel(plate, GameText.Back, 38, LiveryText, LiveryRim, 3f);
        OneLine(label, 31f + lampSize, 21f, 2f);
        feel.dimmed = new Graphic[] { panel, trim, lamp, arrow, label };
        feel.dimmedColors = new[] { LiveryOffPanel, LiveryOffTrim, DimmedPicture, Faded(LiveryRim, 0.5f), LiveryOffText };
        return button;
    }

    /// <summary>
    /// A plate that only holds things, cream like the room code box: a navy rim, a cream face and a faint navy line inside it.
    /// Returns the face, whose centre is the plate's, so things are placed on it as on the old UiPanel's inside.
    /// </summary>
    private static RectTransform LiveryCard(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float delay,
        float corner = 20f)
    {
        RectTransform slot = PopSlot(parent, name, anchor, position, size, delay);
        AddSoftShadow(slot, 0.3f);
        // Thinner than a button's, which sits up off the page; the crew page left only 4 pixels between its cards and buttons.
        Image drop = UiImage(slot, "Drop", uiPlate, LiveryShadow, Corner(corner));
        Fill(drop.rectTransform, 0f, -CardDrop, 0f, CardDrop);
        Image rim = UiImage(slot, "Rim", uiPlate, LiveryRim, Corner(corner));
        Fill(rim.rectTransform);
        Image face = UiImage(slot, "Face", uiPlate, LiveryCream, Corner(corner - 5f));
        Fill(face.rectTransform, 5f, 5f, 5f, 5f);
        Image line = UiImage(face.transform, "Pinstripe", uiPinstripe, Faded(LiveryRim, 0.3f), PinstripeCorner(corner - 11f));
        Fill(line.rectTransform, 6f, 6f, 6f, 6f);
        return face.rectTransform;
    }

    /// <summary>A plate with just a word on it, for the pages' buttons.</summary>
    private static Button LiveryTextButton(Transform parent, string name, string content, Vector2 anchor, Vector2 position, Vector2 size,
        Color panelColor, int fontSize, float delay, out Text label)
    {
        RectTransform plate = LiveryPlate(parent, name, anchor, position, size, panelColor, 22f, delay, out Button button,
            out UiButtonFeel feel, out Image trim, out Image panel);
        label = LiveryLabel(plate, content, fontSize, LiveryText, LiveryRim, 3f);
        OneLine(label, 18f, 18f, 2f);
        feel.dimmed = new Graphic[] { panel, trim, label };
        feel.dimmedColors = new[] { LiveryOffPanel, LiveryOffTrim, LiveryOffText };
        return button;
    }

    /// <summary>A small navy plate with a cream arrow, for stepping through hats and glasses. `next` points it right.</summary>
    private static Button LiveryArrowButton(Transform parent, string name, bool next, Vector2 position, Vector2 size, float delay)
    {
        RectTransform plate = LiveryPlate(parent, name, AnchorCentre, position, size, LiveryNavy, 16f, delay, out Button button,
            out UiButtonFeel feel, out Image trim, out Image panel, 5f);
        Image arrow = UiImage(plate, "Arrow", UiSprite("UI_Back"), LiveryText);
        Rect(arrow.gameObject, plate, AnchorCentre, new Vector2(0f, 2f), Vector2.one * (size.y * 0.6f));
        if (next) arrow.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        feel.hoverScale = 1.08f;
        return button;
    }

    /// <summary>A slider: a navy track, an orange fill and a small headlight to drag.</summary>
    private static Slider LiverySlider(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        float trackInset = size.y * 0.25f;
        RectTransform root = UiGroup(parent, name, anchor, position, size);
        Image track = UiImage(root, "Track", uiPlate, LiveryRim, Corner(6.5f));
        Fill(track.rectTransform, 0f, trackInset, 0f, trackInset);
        track.raycastTarget = true;

        RectTransform fillArea = UiGroup(root, "FillArea", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(fillArea, 4f, trackInset + 4f, 4f, trackInset + 4f);
        Image fill = UiImage(fillArea, "Fill", uiPlate, LiveryOrange, Corner(4f));
        Fill(fill.rectTransform);

        RectTransform handleArea = UiGroup(root, "HandleArea", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(handleArea, size.y * 0.5f, 0f, size.y * 0.5f, 0f);
        RectTransform handle = UiGroup(handleArea, "Handle", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(handle);
        handle.sizeDelta = new Vector2(size.y, 0f);
        Image under = UiImage(handle, "Shadow", uiCircle, LiveryRim);
        Fill(under.rectTransform, 0f, -3f, 0f, 3f);
        Image lamp = UiImage(handle, "Lamp", uiHeadlight, Color.white);
        Fill(lamp.rectTransform);
        lamp.raycastTarget = true;

        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle;
        slider.targetGraphic = lamp;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        return slider;
    }

    /// <summary>
    /// A picture framed like a level card: a navy rim, and a cream trim whose round inner corners lie over the picture's square
    /// ones. Returns the group to put the picture in.
    /// </summary>
    private static RectTransform LiveryFrame(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float delay)
    {
        RectTransform slot = PopSlot(parent, name, anchor, position, size, delay);
        AddSoftShadow(slot, 0.45f);
        Image drop = UiImage(slot, "Drop", uiPlate, LiveryShadow, Corner(24f));
        Fill(drop.rectTransform, 0f, -PlateDrop, 0f, PlateDrop);
        Image rim = UiImage(slot, "Rim", uiPlate, LiveryRim, Corner(24f));
        Fill(rim.rectTransform);
        Image trim = UiImage(slot, "Trim", uiPlate, LiveryCream, Corner(19f));
        Fill(trim.rectTransform, 5f, 5f, 5f, 5f);
        RectTransform inside = UiGroup(slot, "Inside", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(inside, 8f, 8f, 8f, 8f);
        Image corners = UiImage(slot, "Corners", uiFrame, LiveryCream, 2.4f);
        Fill(corners.rectTransform, 5f, 5f, 5f, 5f);
        return inside;
    }

    /// <summary>The room code box: a cream plate like the truck's number plate, with a thin navy line inside its rim.</summary>
    private static InputField LiveryCodeInput(Transform parent, Vector2 position, Vector2 size, float delay)
    {
        RectTransform slot = PopSlot(parent, "CodeInput", new Vector2(0f, 1f), position, size, delay);
        AddSoftShadow(slot, 0.3f);
        Image drop = UiImage(slot, "Drop", uiPlate, LiveryShadow, Corner(20f));
        Fill(drop.rectTransform, 0f, -PlateDrop, 0f, PlateDrop);
        Image rim = UiImage(slot, "Rim", uiPlate, LiveryRim, Corner(20f));
        Fill(rim.rectTransform);
        Image plate = UiImage(slot, "Plate", uiPlate, LiveryCream, Corner(15f));
        Fill(plate.rectTransform, 5f, 5f, 5f, 5f);
        plate.raycastTarget = true;
        Image line = UiImage(plate.transform, "Pinstripe", uiPinstripe, Faded(LiveryRim, 0.8f), PinstripeCorner(12f));
        Fill(line.rectTransform, 6f, 6f, 6f, 6f);
        Text placeholder = UiText(plate.transform, "Placeholder", GameText.CodePlaceholder, 32, Faded(LiveryRim, 0.45f));
        Fill(placeholder.rectTransform);
        Text value = UiText(plate.transform, "Text", "", 34, LiveryRim);
        Fill(value.rectTransform, 14f, 0f, 14f, 0f);
        value.supportRichText = false;
        value.horizontalOverflow = HorizontalWrapMode.Wrap;
        value.verticalOverflow = VerticalWrapMode.Truncate;
        InputField field = plate.gameObject.AddComponent<InputField>();
        field.transition = Selectable.Transition.None;
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.textComponent = value;
        field.placeholder = placeholder;
        field.characterLimit = 32;
        return field;
    }

    /// <summary>Builds the main menu again in the open start scene, in place of the one there (Apply Menu Presentation).</summary>
    public static void RebuildMainMenu(TitleFlow flow)
    {
        LoadUiAssets();
        Transform canvas = flow.menuPanel.transform.parent;
        int order = flow.menuPanel.transform.GetSiblingIndex();
        Object.DestroyImmediate(flow.menuPanel);
        foreach (Transform old in canvas.Cast<Transform>().Where(child => child.name == "TitlePanel").ToArray()) Object.DestroyImmediate(old.gameObject);
        BuildMenuPanel(canvas, flow, UiSprite("UI_Logo", mipmaps: true));
        flow.menuPanel.transform.SetSiblingIndex(order);
        flow.menuPanel.SetActive(true);
        EditorUtility.SetDirty(flow);
    }

    /// <summary>Builds the level select again in the open start scene, in place of the one there (Apply Menu Pages).</summary>
    public static void RebuildLevelPage(TitleFlow flow)
    {
        LoadUiAssets();
        Transform canvas = flow.levelPanel.transform.parent;
        int order = flow.levelPanel.transform.GetSiblingIndex();
        Object.DestroyImmediate(flow.levelPanel);
        BuildLevelPanel(canvas, flow);
        flow.levelPanel.transform.SetSiblingIndex(order);
        EditorUtility.SetDirty(flow);
    }

    /// <summary>Builds the lobby again in the open start scene, in place of the one there (Apply Menu Pages).</summary>
    public static void RebuildLobbyPage(TitleFlow flow)
    {
        LoadUiAssets();
        Transform canvas = flow.lobbyPanel.transform.parent;
        int order = flow.lobbyPanel.transform.GetSiblingIndex();
        Object.DestroyImmediate(flow.lobbyPanel);
        BuildLobbyPanel(canvas, flow);
        flow.lobbyPanel.transform.SetSiblingIndex(order);
        EditorUtility.SetDirty(flow);
    }

    /// <summary>
    /// Builds a settings page again where the old one was: the start scene's (TitleFlow.settings) or a match's
    /// (MatchHud.settings), which is darker behind. Returns the new one, for the caller to keep.
    /// </summary>
    public static SettingsPanel RebuildSettingsPage(SettingsPanel old, float dimAlpha)
    {
        LoadUiAssets();
        Transform canvas = old.transform.parent;
        int order = old.transform.GetSiblingIndex();
        Object.DestroyImmediate(old.gameObject);
        SettingsPanel settings = BuildSettingsPanel(canvas, dimAlpha);
        settings.transform.SetSiblingIndex(order);
        return settings;
    }

    /// <summary>Builds the crew page again in the open start scene, in place of the one there (Apply Menu Pages).</summary>
    public static void RebuildCrewPage(TitleFlow flow)
    {
        LoadUiAssets();
        Transform canvas = flow.crewPanel.transform.parent;
        int order = flow.crewPanel.transform.GetSiblingIndex();
        Object.DestroyImmediate(flow.crewPanel);
        RenderTexture picture = AssetDatabase.LoadAssetAtPath<RenderTexture>(UiDir + "/CrewPreview.renderTexture");
        BuildCrewPanel(canvas, flow, flow.preview, picture);
        flow.crewPanel.transform.SetSiblingIndex(order);
        EditorUtility.SetDirty(flow);
    }

    /// <summary>
    /// The level select: six cards, three by two, over the start scene darkened, painted like the main menu (step 26). Each card
    /// is a plate whose cream trim frames the level's picture, with the name on a navy strip and the number on a headlight.
    /// </summary>
    private static void BuildLevelPanel(Transform canvas, TitleFlow flow)
    {
        RectTransform panel = UiPanelRoot(canvas, "LevelPanel");
        flow.levelPanel = panel.gameObject;
        panel.gameObject.SetActive(false);

        Image dim = UiImage(panel, "Dim", null, Faded(LiveryRim, 0.66f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform headingSlot = PopSlot(panel, "Heading", AnchorTop, new Vector2(0f, -128f), new Vector2(1100f, 110f), 0f);
        Text heading = LiveryHeading(headingSlot, GameText.ChooseLevel, 92);
        Fill(heading.rectTransform);

        flow.levelBackButton = LiveryBackButton(panel, new Vector2(170f, -128f), new Vector2(250f, 96f), 0f);

        LevelInfo[] levels = GameConfig.Get().levels;
        flow.levelCards = new LevelCard[levels.Length];
        Vector2 size = new Vector2(480f, 335f);
        const float gapX = 520f;
        const float gapY = 356f;
        const float centreY = -22f;
        for (int i = 0; i < levels.Length; i++)
        {
            int column = i % 3;
            int row = i / 3;
            Vector2 position = new Vector2((column - 1) * gapX, centreY + (0.5f - row) * gapY);
            flow.levelCards[i] = LiveryLevelCard(panel, i, position, size, 0.05f + i * 0.05f);
        }

        flow.levelMessage = UiText(panel, "Message", "", 34, LiveryText);
        Shadow messageShadow = flow.levelMessage.gameObject.AddComponent<Shadow>();
        messageShadow.effectColor = LiveryRim;
        messageShadow.effectDistance = new Vector2(0f, -3f);
        Rect(flow.levelMessage.gameObject, panel, AnchorBottom, new Vector2(0f, 94f), new Vector2(1400f, 50f));
    }

    /// <summary>
    /// One level's card: a plate whose cream trim frames the picture, the name on a navy strip under it, the number on a
    /// headlight over the corner. A locked level's picture is darkened under a padlock. The card sinks when pressed.
    /// </summary>
    private static LevelCard LiveryLevelCard(Transform parent, int index, Vector2 position, Vector2 size, float delay)
    {
        const float stripHeight = 70f;
        // The picture and the strip reach 3 pixels under the cream frame that rounds their corners, so nothing shows between.
        const float inset = 8f;
        RectTransform slot = PopSlot(parent, $"Card_{index + 1}", AnchorCentre, position, size, delay);
        AddSoftShadow(slot, 0.45f);
        Image body = UiImage(slot, "Button", uiPlate, Color.clear, Corner(24f));
        Fill(body.rectTransform);
        body.raycastTarget = true;
        Image drop = UiImage(body.transform, "Drop", uiPlate, LiveryShadow, Corner(24f));
        Fill(drop.rectTransform, 0f, -PlateDrop, 0f, PlateDrop);
        RectTransform plate = UiGroup(body.transform, "Plate", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(plate);
        Image rim = UiImage(plate, "Rim", uiPlate, LiveryRim, Corner(24f));
        Fill(rim.rectTransform);
        Image trim = UiImage(plate, "Trim", uiPlate, LiveryCream, Corner(19f));
        Fill(trim.rectTransform, 5f, 5f, 5f, 5f);

        Button button = body.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = body;
        UiButtonFeel feel = body.gameObject.AddComponent<UiButtonFeel>();
        feel.sink = plate;
        feel.hoverScale = 1.04f;
        feel.pressDepth = 5f;
        LevelCard card = body.gameObject.AddComponent<LevelCard>();
        card.button = button;
        card.feel = feel;

        // A line of the cream trim shows between the picture and the strip.
        card.picture = UiImage(plate, "Picture", null, Color.white);
        Fill(card.picture.rectTransform, inset, inset + stripHeight + 4f, inset, inset);
        Image strip = UiImage(plate, "Strip", null, LiveryNavy);
        Fill(strip.rectTransform, inset, inset, inset, size.y - inset - stripHeight);
        card.title = LiveryLabel(strip.transform, "", 40, LiveryText, LiveryRim, 3f);
        Fill(card.title.rectTransform, 12f, 2f, 12f, 0f);

        RectTransform cover = UiGroup(plate, "LockedCover", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(cover, inset, inset + stripHeight + 4f, inset, inset);
        card.lockedCover = cover.gameObject;
        Image shade = UiImage(cover, "Shade", null, Faded(LiveryRim, 0.55f));
        Fill(shade.rectTransform);
        Image padlock = UiImage(cover, "Lock", uiLock, Color.white);
        Rect(padlock.gameObject, cover, AnchorCentre, new Vector2(0f, 24f), Vector2.one * 110f);
        Text soon = LiveryLabel(cover, GameText.ComingSoon, 34, LiveryText, LiveryRim, 3f);
        Rect(soon.gameObject, cover, AnchorCentre, new Vector2(0f, -62f), new Vector2(440f, 44f));

        // UI_Frame at a corner multiplier of 2.4 is a 5-pixel ring, 18 round outside and 12.5 inside: the trim, over the
        // picture's and the strip's square corners.
        Image corners = UiImage(plate, "Corners", uiFrame, LiveryCream, 2.4f);
        Fill(corners.rectTransform, 5f, 5f, 5f, 5f);

        RectTransform badge = UiGroup(plate, "Badge", new Vector2(0f, 1f), new Vector2(46f, -46f), Vector2.one * 80f);
        Image under = UiImage(badge, "LampShadow", uiCircle, LiveryRim);
        Fill(under.rectTransform, 0f, -4f, 0f, 4f);
        Image lamp = UiImage(badge, "Lamp", uiHeadlight, Color.white);
        Fill(lamp.rectTransform);
        card.number = UiText(badge, "Number", "", 44, LiveryRim);
        Fill(card.number.rectTransform, 0f, 2f);

        feel.dimmed = new Graphic[] { card.picture, strip, card.title, trim, corners };
        feel.dimmedColors = new[] { new Color(0.55f, 0.57f, 0.65f), LiveryOffPanel, LiveryOffText, LiveryOffTrim, LiveryOffTrim };
        return card;
    }

    /// <summary>
    /// The lobby, painted like the menus (step 28): a cream board with the room code and the level, a column for each team (a
    /// navy rim round a panel in the team's tint, its name on a band in the team's colour) and the buttons as plates. Joining
    /// a team is in that team's colour, starting the game is gold, the rest are navy.
    /// </summary>
    private static void BuildLobbyPanel(Transform canvas, TitleFlow flow)
    {
        RectTransform panel = UiPanelRoot(canvas, "LobbyPanel");
        flow.lobbyPanel = panel.gameObject;
        panel.gameObject.SetActive(false);

        Image dim = UiImage(panel, "Dim", null, Faded(LiveryRim, 0.5f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform inside = LiveryCard(panel, "Card", AnchorCentre, Vector2.zero, new Vector2(1300f, 930f), 0f, 26f);

        // Only as wide as the space between the CREW plate and the same space on the right, so a long one (the Chinese
        // "LAN room" words) gets smaller instead of running under the plate. Too low for two lines, so it never wraps.
        flow.roomCodeText = UiText(inside, "RoomCode", "", 62, LiveryRim);
        flow.roomCodeText.resizeTextForBestFit = true;
        flow.roomCodeText.resizeTextMinSize = 36;
        flow.roomCodeText.resizeTextMaxSize = 62;
        flow.roomCodeText.horizontalOverflow = HorizontalWrapMode.Wrap;
        flow.roomCodeText.verticalOverflow = VerticalWrapMode.Truncate;
        Rect(flow.roomCodeText.gameObject, inside, AnchorTop, new Vector2(0f, -64f), new Vector2(800f, 96f));
        flow.lobbyLevelText = UiText(inside, "Level", "", 36, LiverySection);
        Rect(flow.lobbyLevelText.gameObject, inside, AnchorTop, new Vector2(0f, -128f), new Vector2(1240f, 44f));

        const float columnX = 306f;
        Vector2 buttonSize = new Vector2(590f, 88f);
        flow.orangeList = LiveryTeamColumn(inside, "Orange", GameText.TeamOrange, LiveryOrange, LiveryOrangeTint, -columnX);
        flow.blueList = LiveryTeamColumn(inside, "Blue", GameText.TeamBlue, LiveryTeamBlue, LiveryBlueTint, columnX);

        flow.orangeButton = LiveryTextButton(inside, "JoinOrange", GameText.JoinOrange, AnchorCentre, new Vector2(-columnX, -120f), buttonSize, LiveryOrange, 38, 0.05f, out _);
        flow.blueButton = LiveryTextButton(inside, "JoinBlue", GameText.JoinBlue, AnchorCentre, new Vector2(columnX, -120f), buttonSize, LiveryTeamBlue, 38, 0.05f, out _);
        flow.readyButton = LiveryTextButton(inside, "Ready", GameText.Ready, AnchorCentre, new Vector2(-columnX, -218f), buttonSize, LiveryNavy, 38, 0.1f, out flow.readyLabel);
        flow.fillButton = LiveryTextButton(inside, "Fill", GameText.BotsFill(false), AnchorCentre, new Vector2(columnX, -218f), buttonSize, LiveryNavy, 38, 0.1f, out flow.fillLabel);
        flow.startButton = LiveryTextButton(inside, "Start", GameText.StartGame, AnchorCentre, new Vector2(-columnX, -316f), buttonSize, LiveryGold, 38, 0.15f, out Text startLabel);
        // Navy words on the gold plate, like JOIN on the main menu.
        startLabel.color = LiveryRim;
        Shadow startShadow = startLabel.GetComponent<Shadow>();
        startShadow.effectColor = Faded(Color.white, 0.4f);
        startShadow.effectDistance = new Vector2(0f, -2f);
        flow.leaveButton = LiveryTextButton(inside, "Leave", GameText.Leave, AnchorCentre, new Vector2(columnX, -316f), buttonSize, LiveryNavy, 38, 0.15f, out _);

        flow.lobbyHint = UiText(inside, "Hint", "", 28, UiHint);
        Rect(flow.lobbyHint.gameObject, inside, AnchorBottom, new Vector2(0f, 40f), new Vector2(1240f, 36f));
        flow.lobbyCrewButton = LiveryButton(inside, "Crew", GameText.Crew, new Vector2(-520f, -64f), new Vector2(210f, 76f), LiveryNavy, 32, 0.05f,
            UiSprite("Icon_Hat"), 58f, 54f, 0f, AnchorTop);
    }

    /// <summary>
    /// A team's column in the lobby: a navy rim round a panel in the team's tint, the team's name on a band in its colour, and
    /// under it the list of who is on the team.
    /// </summary>
    private static Text LiveryTeamColumn(RectTransform parent, string name, string heading, Color color, Color tint, float x)
    {
        RectTransform slot = PopSlot(parent, name, AnchorCentre, new Vector2(x, 85f), new Vector2(590f, 300f), 0.03f);
        AddSoftShadow(slot, 0.2f);
        Image rim = UiImage(slot, "Rim", uiPlate, LiveryRim, Corner(18f));
        Fill(rim.rectTransform);
        Image inside = UiImage(slot, "Inside", uiPlate, tint, Corner(13f));
        Fill(inside.rectTransform, 5f, 5f, 5f, 5f);
        // The band is rounded like the panel at the top and squared off underneath.
        Image band = UiImage(inside.transform, "Band", uiPlate, color, Corner(13f));
        RectTransform bandRect = band.rectTransform;
        bandRect.anchorMin = new Vector2(0f, 1f);
        bandRect.anchorMax = new Vector2(1f, 1f);
        bandRect.pivot = new Vector2(0.5f, 1f);
        bandRect.anchoredPosition = Vector2.zero;
        bandRect.sizeDelta = new Vector2(0f, 64f);
        Image squared = UiImage(band.transform, "Square", null, color);
        squared.rectTransform.anchorMin = Vector2.zero;
        squared.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        squared.rectTransform.offsetMin = Vector2.zero;
        squared.rectTransform.offsetMax = Vector2.zero;
        Image line = UiImage(inside.transform, "Line", null, LiveryCream);
        line.rectTransform.anchorMin = new Vector2(0f, 1f);
        line.rectTransform.anchorMax = new Vector2(1f, 1f);
        line.rectTransform.pivot = new Vector2(0.5f, 1f);
        line.rectTransform.anchoredPosition = new Vector2(0f, -64f);
        line.rectTransform.sizeDelta = new Vector2(0f, 4f);
        Text title = LiveryLabel(band.transform, heading, 42, LiveryText, LiveryRim, 3f);
        Fill(title.rectTransform, 12f, 2f, 12f, 0f);
        Text list = UiText(inside.transform, "List", "", 32, LiveryRim);
        Fill(list.rectTransform, 20f, 10f, 20f, 78f);
        list.alignment = TextAnchor.UpperCenter;
        return list;
    }

    /// <summary>
    /// The crew page, painted like the main menu (step 27): the character framed like a level card on the left; on the right the
    /// three bodies as navy plates (the picked one turns orange with a gold ring), its numbers, hat and glasses and the face's
    /// sliders on cream plates; the buttons below. Things you press are plates, things that show are cream like the room code.
    /// </summary>
    private static void BuildCrewPanel(Transform canvas, TitleFlow flow, CrewPreview preview, RenderTexture previewPicture)
    {
        RectTransform panel = UiPanelRoot(canvas, "CrewPanel");
        flow.crewPanel = panel.gameObject;
        flow.preview = preview;
        panel.gameObject.SetActive(false);

        Image dim = UiImage(panel, "Dim", null, Faded(LiveryRim, 0.66f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform headingSlot = PopSlot(panel, "Heading", AnchorTop, new Vector2(0f, -116f), new Vector2(1100f, 110f), 0f);
        Text heading = LiveryHeading(headingSlot, GameText.YourCrew, 88);
        Fill(heading.rectTransform);
        flow.crewBackButton = LiveryBackButton(panel, new Vector2(170f, -116f), new Vector2(250f, 96f), 0f);

        // The character.
        RectTransform frame = LiveryFrame(panel, "Preview", AnchorCentre, new Vector2(-500f, 4f), new Vector2(600f, 730f), 0.05f);
        RawImage picture = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        picture.transform.SetParent(frame, false);
        Fill(picture.rectTransform);
        picture.texture = previewPicture;
        picture.raycastTarget = false;
        flow.crewOutfitButton = LiveryTextButton(panel, "Outfit", GameText.SeeWrecker, AnchorCentre, new Vector2(-500f, -428f),
            new Vector2(560f, 84f), LiveryNavy, 32, 0.1f, out flow.crewOutfitLabel);

        // Body types. The picked one's plate turns orange and a gold ring goes round it.
        const float columnX = 330f;
        BodyType[] bodies = { BodyType.Short, BodyType.Medium, BodyType.Tall };
        flow.bodyButtons = new Button[bodies.Length];
        flow.bodyPicked = new GameObject[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            Vector2 position = new Vector2(columnX + (i - 1) * 290f, 318f);
            Button button = LiveryTextButton(panel, "Body" + bodies[i], GameText.BodyName(bodies[i]), AnchorCentre, position,
                new Vector2(266f, 96f), LiveryNavy, 38, 0.05f + i * 0.03f, out Text label);
            RectTransform picked = UiGroup(label.transform.parent, "Picked", AnchorCentre, Vector2.zero, Vector2.zero);
            Fill(picked);
            picked.SetSiblingIndex(label.transform.GetSiblingIndex());
            Image orange = UiImage(picked, "Panel", uiPlate, LiveryOrange, Corner(13f));
            Fill(orange.rectTransform, 10f, 10f, 10f, 10f);
            Image shade = UiImage(picked, "Shade", uiPlateShade, Color.white, Corner(13f));
            Fill(shade.rectTransform, 10f, 10f, 10f, 10f);
            Image ring = UiImage(picked, "Ring", uiPinstripe, LiveryGold, PinstripeCorner(31f));
            Fill(ring.rectTransform, -9f, -9f, -9f, -9f);
            picked.gameObject.SetActive(false);
            flow.bodyButtons[i] = button;
            flow.bodyPicked[i] = picked.gameObject;
        }

        RectTransform stats = LiveryCard(panel, "Stats", AnchorCentre, new Vector2(columnX, 172f), new Vector2(860f, 168f), 0.1f);
        string[] names = { GameText.StatHealth, GameText.StatSpeed, GameText.StatDamage };
        Color[] colors = { LiveryHealth, LiverySpeed, LiveryOrange };
        flow.statFills = new Image[names.Length];
        flow.statValues = new Text[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            float y = 46f - i * 46f;
            Text label = UiText(stats, names[i], names[i], 30, LiveryRim);
            label.alignment = TextAnchor.MiddleLeft;
            Rect(label.gameObject, stats, AnchorCentre, new Vector2(-322f, y), new Vector2(170f, 40f));
            Image track = UiImage(stats, names[i] + "Track", uiPlate, LiveryRim, Corner(13.5f));
            Rect(track.gameObject, stats, AnchorCentre, new Vector2(52f, y), new Vector2(520f, 30f));
            RectTransform area = UiGroup(track.transform, "Area", AnchorCentre, Vector2.zero, Vector2.zero);
            Fill(area, 5f, 5f, 5f, 5f);
            Image fill = UiImage(area, "Fill", uiPlate, colors[i], Corner(9f));
            Fill(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            flow.statFills[i] = fill;
            Text value = UiText(stats, names[i] + "Value", "", 30, LiveryRim);
            value.alignment = TextAnchor.MiddleRight;
            Rect(value.gameObject, stats, AnchorCentre, new Vector2(366f, y), new Vector2(110f, 40f));
            flow.statValues[i] = value;
        }

        flow.bodyBlurb = UiText(panel, "Blurb", "", 30, LiveryText);
        Shadow blurbShadow = flow.bodyBlurb.gameObject.AddComponent<Shadow>();
        blurbShadow.effectColor = LiveryRim;
        blurbShadow.effectDistance = new Vector2(0f, -3f);
        Rect(flow.bodyBlurb.gameObject, panel, AnchorCentre, new Vector2(columnX, 62f), new Vector2(860f, 44f));

        // Hat and glasses.
        LiverySelector(panel, "Hat", GameText.HatHeading, new Vector2(columnX, -12f), 0.15f, out flow.hatBack, out flow.hatNext, out flow.hatLabel);
        LiverySelector(panel, "Glasses", GameText.GlassesHeading, new Vector2(columnX, -104f), 0.18f, out flow.glassesBack, out flow.glassesNext, out flow.glassesLabel);

        // Face: Astra's ten sliders, in two columns.
        RectTransform face = LiveryCard(panel, "Face", AnchorCentre, new Vector2(columnX, -270f), new Vector2(860f, 220f), 0.2f);
        flow.faceSliders = new Slider[CrewLook.FaceSliders.Length];
        for (int i = 0; i < CrewLook.FaceSliders.Length; i++)
        {
            float x = i < 5 ? -212f : 212f;
            float y = 80f - (i % 5) * 40f;
            Text label = UiText(face, "Label" + i, GameText.FaceSliderName(i), 22, LiveryRim);
            label.alignment = TextAnchor.MiddleLeft;
            Rect(label.gameObject, face, AnchorCentre, new Vector2(x - 128f, y), new Vector2(150f, 34f));
            Slider slider = LiverySlider(face, "Slider" + i, AnchorCentre, new Vector2(x + 72f, y), new Vector2(240f, 32f));
            slider.minValue = -1f;
            slider.maxValue = 1f;
            slider.value = 0f;
            flow.faceSliders[i] = slider;
        }

        flow.crewRandomButton = LiveryTextButton(panel, "Random", GameText.RandomLook, AnchorCentre, new Vector2(columnX - 236f, -428f),
            new Vector2(380f, 88f), LiveryNavy, 36, 0.25f, out _);
        flow.crewStartButton = LiveryTextButton(panel, "Start", GameText.StartPractice, AnchorCentre, new Vector2(columnX + 214f, -428f),
            new Vector2(420f, 88f), LiveryOrange, 40, 0.25f, out flow.crewStartLabel);
    }

    /// <summary>A cream row for the hat or the glasses: the heading, an arrow plate each side of the name.</summary>
    private static void LiverySelector(Transform parent, string name, string heading, Vector2 position, float delay, out Button back, out Button next, out Text value)
    {
        RectTransform row = LiveryCard(parent, name, AnchorCentre, position, new Vector2(860f, 80f), delay, 18f);
        Text title = UiText(row, "Heading", heading, 32, LiveryRim);
        title.alignment = TextAnchor.MiddleLeft;
        Rect(title.gameObject, row, AnchorCentre, new Vector2(-322f, 0f), new Vector2(170f, 50f));
        back = LiveryArrowButton(row, "Back", false, new Vector2(-150f, 0f), new Vector2(88f, 62f), delay);
        value = UiText(row, "Value", "", 34, LiveryRim);
        Rect(value.gameObject, row, AnchorCentre, new Vector2(102f, 0f), new Vector2(330f, 50f));
        next = LiveryArrowButton(row, "Next", true, new Vector2(354f, 0f), new Vector2(88f, 62f), delay);
    }

}
