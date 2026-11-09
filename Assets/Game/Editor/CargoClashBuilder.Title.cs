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
            new LevelInfo { title = "RIVER FLATS", picture = CardPicture(4) },
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
        BuildTitleMenus(stage);
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
    /// are stretched between them while it drives. The model comes from Tools/blender/make_truck.py.
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

        Material dark = ModelMaterials()["Truck_Dark"];
        Material steel = ModelMaterials()["Truck_Steel"];
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

        // Faces point along the truck's width, so they look out at both sides of it. The camera is on
        // the +X side of the truck, so the chest goes on the far side, with only low parcels in front
        // of it. Heights are the bed floor at 2.0 plus half an item, on top of whatever it stands on.
        Transform load = Group("Load", body);
        Place("Cargo_Large", load, new Vector3(-0.78f, 2.53f, 2.55f), 90f);
        Place("Cargo_Medium", load, new Vector3(-0.85f, 2.46f, 0.55f), 96f);
        Place("Cargo_Medium", load, new Vector3(0.72f, 2.46f, 0.2f), 85f);
        Place("Cargo_Medium", load, new Vector3(0.72f, 2.46f, 1.35f), 92f);
        Place("Cargo_Small", load, new Vector3(-0.85f, 3.22f, 0.55f), 101f);
        Place("Cargo_Small", load, new Vector3(-0.78f, 3.36f, 3.2f), 84f);
        Place("Cargo_Small", load, new Vector3(-0.78f, 3.36f, 1.95f), 97f);
        Place("Cargo_Small", load, new Vector3(0.72f, 3.22f, 0.2f), 83f);
        Place("Cargo_Small", load, new Vector3(0.78f, 2.3f, 2.6f), 92f);
        Place("Cargo_Small", load, new Vector3(0.78f, 2.3f, 3.5f), 100f);

        // Exhaust from the stack behind the cab, and dust thrown up by the back wheels.
        Color smokeDark = new Color(0.72f, 0.73f, 0.76f);
        Color smokeLight = new Color(0.93f, 0.93f, 0.94f);
        Color dustDark = new Color(0.72f, 0.63f, 0.47f);
        Color dustLight = new Color(0.86f, 0.79f, 0.63f);
        Material puff = PuffMaterial();
        AddPuffs(body, "Exhaust", new Vector3(-1.2f, 4.42f, -0.3f), 7f, 24, smokeDark, smokeLight, 0.16f, 0.3f, 1f, 1.7f, puff);
        AddPuffs(body, "DustLeft", new Vector3(-2.36f, 0.25f, 3.7f), 9f, 24, dustDark, dustLight, 0.14f, 0.34f, 0.6f, 1.1f, puff);
        AddPuffs(body, "DustRight", new Vector3(2.36f, 0.25f, 3.7f), 9f, 24, dustDark, dustLight, 0.14f, 0.34f, 0.6f, 1.1f, puff);

        RoverTerrainAnimator rover = root.AddComponent<RoverTerrainAnimator>();
        rover.ConfigureRig(body, pivots, mounts, shocks, feet, arms, axles, differentials);
        return SavePrefab(root, "TitleTruck");
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
    private static readonly Color UiCream = new Color(1f, 0.97f, 0.89f);
    private static readonly Color UiOrange = new Color(1f, 0.6f, 0.11f);
    private static readonly Color UiBlue = new Color(0.2f, 0.56f, 1f);
    private static readonly Color UiGreen = new Color(0.3f, 0.78f, 0.38f);
    private static readonly Color UiGray = new Color(0.56f, 0.61f, 0.72f);
    private static readonly Color UiYellow = new Color(1f, 0.8f, 0.15f);
    private static readonly Vector2 AnchorCentre = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 AnchorTop = new Vector2(0.5f, 1f);
    private static readonly Vector2 AnchorBottom = new Vector2(0.5f, 0f);

    private static Font uiFont;
    private static Sprite uiRound;
    private static Sprite uiSoft;
    private static Sprite uiFace;
    private static Sprite uiCircle;
    private static Sprite uiFrame;
    private static Sprite uiLock;
    private static Sprite uiTape;

    private static void LoadUiAssets()
    {
        uiFont = AssetDatabase.LoadAssetAtPath<Font>(UiDir + "/LilitaOne-Regular.ttf");
        uiRound = UiSprite("UI_Round", 48);
        uiSoft = UiSprite("UI_Soft", 100);
        uiFace = UiSprite("UI_ButtonFace", 48);
        uiCircle = UiSprite("UI_Circle");
        uiFrame = UiSprite("UI_Frame", 48);
        uiLock = UiSprite("UI_Lock");
        uiTape = UiSprite("UI_Hazard", repeat: true);
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

    /// <summary>
    /// A chunky button: dark rim, darker lip along the bottom, and a glossy face that sinks when pressed.
    /// The slot it sits in springs in, and the object with the Button scales itself under the pointer.
    /// </summary>
    private static Button UiButton(Transform parent, string name, string content, Vector2 anchor, Vector2 position, Vector2 size,
        Color color, int fontSize, float delay, out Text label, Sprite icon = null)
    {
        const float rim = 6f;
        const float lipHeight = 10f;
        RectTransform slot = PopSlot(parent, name, anchor, position, size, delay);
        AddSoftShadow(slot);

        Image body = UiImage(slot, "Button", uiRound, UiNavy, 1.4f);
        Fill(body.rectTransform);
        body.raycastTarget = true;

        Color lipColor = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);
        Image lip = UiImage(body.transform, "Lip", uiRound, lipColor, 1.7f);
        Fill(lip.rectTransform, rim, rim, rim, rim);
        Image face = UiImage(body.transform, "Face", uiFace, color, 1.4f);
        Fill(face.rectTransform, rim, rim + lipHeight, rim, rim);

        Button button = body.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = body;
        UiButtonFeel feel = body.gameObject.AddComponent<UiButtonFeel>();
        feel.face = face;
        feel.lip = lip;

        float iconRoom = 0f;
        if (icon != null)
        {
            iconRoom = size.y * 0.92f;
            Image picture = UiImage(face.transform, "Icon", icon, Color.white);
            picture.preserveAspect = true;
            Rect(picture.gameObject, face.transform, new Vector2(0f, 0.5f), new Vector2(iconRoom * 0.56f, 0f), Vector2.one * iconRoom);
        }
        label = UiText(face.transform, "Label", content, fontSize, Color.white, 2.5f);
        Fill(label.rectTransform, 10f + iconRoom, 2f, 10f, 2f);
        return button;
    }

    /// <summary>A rounded panel with a dark edge, returned as the inside to put things on.</summary>
    private static RectTransform UiPanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color fill, float delay, float cornerScale = 1.4f)
    {
        RectTransform slot = PopSlot(parent, name, anchor, position, size, delay);
        AddSoftShadow(slot);
        Image edge = UiImage(slot, "Edge", uiRound, UiNavy, cornerScale);
        Fill(edge.rectTransform);
        Image inside = UiImage(edge.transform, "Inside", uiRound, fill, cornerScale * 1.2f);
        Fill(inside.rectTransform, 8f, 8f, 8f, 8f);
        return inside.rectTransform;
    }

    private static InputField UiCodeInput(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, float delay)
    {
        RectTransform inside = UiPanel(parent, "CodeInput", anchor, position, size, UiCream, delay);
        Text placeholder = UiText(inside, "Placeholder", GameText.CodePlaceholder, 32, new Color(0.56f, 0.57f, 0.62f));
        Fill(placeholder.rectTransform);
        Text value = UiText(inside, "Text", "", 34, UiNavy);
        Fill(value.rectTransform, 10f, 0f, 10f, 0f);
        value.supportRichText = false;
        value.horizontalOverflow = HorizontalWrapMode.Wrap;
        value.verticalOverflow = VerticalWrapMode.Truncate;
        inside.GetComponent<Image>().raycastTarget = true;
        InputField field = inside.gameObject.AddComponent<InputField>();
        field.transition = Selectable.Transition.None;
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.textComponent = value;
        field.placeholder = placeholder;
        field.characterLimit = 32;
        return field;
    }

    // ---- The screens ----

    private static void BuildTitleMenus(CargoClashStartMenu stage)
    {
        LoadUiAssets();
        Canvas canvas = NewCanvas("GameMenuCanvas", 200);
        // The screens are laid out for 1920x1080. This keeps at least that much room in a window of any shape.
        canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        TitleFlow flow = new GameObject("TitleFlow").AddComponent<TitleFlow>();
        flow.stage = stage;
        Sprite logo = UiSprite("UI_Logo", mipmaps: true);

        BuildTitlePanel(canvas.transform, flow, logo);
        BuildMenuPanel(canvas.transform, flow, logo);
        BuildLevelPanel(canvas.transform, flow);
        BuildLobbyPanel(canvas.transform, flow);
        EditorUtility.SetDirty(flow);
    }

    private static void BuildTitlePanel(Transform canvas, TitleFlow flow, Sprite logo)
    {
        RectTransform panel = UiPanelRoot(canvas, "TitlePanel");
        flow.titlePanel = panel.gameObject;

        RectTransform slot = PopSlot(panel, "Logo", AnchorTop, new Vector2(0f, -285f), new Vector2(980f, 544f), 0.1f);
        Image picture = UiImage(slot, "Picture", logo, Color.white);
        Fill(picture.rectTransform);
        picture.gameObject.AddComponent<UiFloat>();

        flow.playButton = UiButton(panel, "Play", GameText.Play, AnchorBottom, new Vector2(0f, 96f), new Vector2(440f, 138f), UiOrange, 84, 0.35f, out _);
        flow.playButton.GetComponent<UiButtonFeel>().pulse = 0.025f;
    }

    private static void BuildMenuPanel(Transform canvas, TitleFlow flow, Sprite logo)
    {
        RectTransform panel = UiPanelRoot(canvas, "MenuPanel");
        flow.menuPanel = panel.gameObject;
        panel.gameObject.SetActive(false);

        const float columnX = 310f;
        Vector2 left = new Vector2(0f, 0.5f);
        Vector2 buttonSize = new Vector2(540f, 108f);

        RectTransform slot = PopSlot(panel, "Logo", new Vector2(0f, 1f), new Vector2(columnX, -165f), new Vector2(540f, 300f), 0f);
        Image picture = UiImage(slot, "Picture", logo, Color.white);
        Fill(picture.rectTransform);
        picture.gameObject.AddComponent<UiFloat>().bobHeight = 5f;

        Sprite parcel = UiSprite("Icon_Parcel");
        Sprite truck = UiSprite("Icon_Truck");
        flow.practiceButton = UiButton(panel, "Practice", GameText.Practice, left, new Vector2(columnX, 130f), buttonSize, UiOrange, 40, 0.05f, out _, parcel);
        flow.createButton = UiButton(panel, "Create", GameText.CreateRoomButton, left, new Vector2(columnX, 8f), buttonSize, UiBlue, 40, 0.1f, out _, truck);
        flow.codeInput = UiCodeInput(panel, left, new Vector2(columnX - 106f, -112f), new Vector2(328f, 100f), 0.15f);
        flow.joinButton = UiButton(panel, "Join", GameText.Join, left, new Vector2(columnX + 170f, -112f), new Vector2(200f, 100f), UiGreen, 40, 0.15f, out _);
        flow.quitButton = UiButton(panel, "Quit", GameText.Quit, left, new Vector2(columnX, -226f), new Vector2(260f, 84f), UiGray, 34, 0.2f, out _);

        flow.statusText = UiText(panel, "Status", "", 30, UiYellow, 2f);
        Rect(flow.statusText.gameObject, panel, left, new Vector2(columnX, -320f), new Vector2(560f, 80f));
        flow.statusText.horizontalOverflow = HorizontalWrapMode.Wrap;

        RectTransform keys = PopSlot(panel, "Keys", AnchorBottom, new Vector2(240f, 56f), new Vector2(1320f, 66f), 0.25f);
        Image back = UiImage(keys, "Back", uiRound, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.82f), 1.8f);
        Fill(back.rectTransform);
        Text hint = UiText(keys, "Hint", GameText.KeysHint, 26, UiCream);
        Fill(hint.rectTransform);
    }

    private static void AddTape(Transform parent, string name, bool top)
    {
        Image tape = UiImage(parent, name, uiTape, Color.white);
        tape.type = Image.Type.Tiled;
        RectTransform rect = tape.rectTransform;
        rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 56f);
    }

    private static void BuildLevelPanel(Transform canvas, TitleFlow flow)
    {
        RectTransform panel = UiPanelRoot(canvas, "LevelPanel");
        flow.levelPanel = panel.gameObject;
        panel.gameObject.SetActive(false);

        Image dim = UiImage(panel, "Dim", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.66f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;
        AddTape(panel, "TapeTop", true);
        AddTape(panel, "TapeBottom", false);

        RectTransform headingSlot = PopSlot(panel, "Heading", AnchorTop, new Vector2(0f, -128f), new Vector2(1100f, 110f), 0f);
        Text heading = UiText(headingSlot, "Text", GameText.ChooseLevel, 92, UiYellow, 4f);
        Fill(heading.rectTransform);

        flow.levelBackButton = UiButton(panel, "Back", GameText.Back, new Vector2(0f, 1f), new Vector2(170f, -128f), new Vector2(250f, 96f), UiBlue, 38, 0f, out _, UiSprite("UI_Back"));

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
            flow.levelCards[i] = UiLevelCard(panel, i, position, size, 0.05f + i * 0.05f);
        }

        flow.levelMessage = UiText(panel, "Message", "", 34, UiCream, 2f);
        Rect(flow.levelMessage.gameObject, panel, AnchorBottom, new Vector2(0f, 94f), new Vector2(1400f, 50f));
    }

    private static LevelCard UiLevelCard(Transform parent, int index, Vector2 position, Vector2 size, float delay)
    {
        // One pixel under the rim, which is 8 thick, so the square corners can't poke out past its rounded ones.
        const float inset = 9f;
        const float stripHeight = 70f;
        RectTransform slot = PopSlot(parent, $"Card_{index + 1}", AnchorCentre, position, size, delay);
        AddSoftShadow(slot, 0.6f);

        Image body = UiImage(slot, "Button", uiRound, UiNavy, 1.5f);
        Fill(body.rectTransform);
        body.raycastTarget = true;
        Button button = body.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = body;
        UiButtonFeel feel = body.gameObject.AddComponent<UiButtonFeel>();
        LevelCard card = body.gameObject.AddComponent<LevelCard>();
        card.button = button;
        card.feel = feel;

        // The picture and the strip below it have square corners. A rim laid over them rounds them off.
        card.picture = UiImage(body.transform, "Picture", null, Color.white);
        Fill(card.picture.rectTransform, inset, inset + stripHeight, inset, inset);
        Image strip = UiImage(body.transform, "Strip", null, UiCream);
        Fill(strip.rectTransform, inset, inset, inset, size.y - inset - stripHeight);
        Image divider = UiImage(body.transform, "Divider", null, UiNavy);
        Fill(divider.rectTransform, inset, inset + stripHeight - 2f, inset, size.y - inset - stripHeight - 2f);
        card.title = UiText(strip.transform, "Title", "", 40, UiNavy);
        Fill(card.title.rectTransform);

        RectTransform cover = UiGroup(body.transform, "LockedCover", AnchorCentre, Vector2.zero, Vector2.zero);
        Fill(cover, inset, inset + stripHeight, inset, inset);
        card.lockedCover = cover.gameObject;
        Image shade = UiImage(cover, "Shade", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.52f));
        Fill(shade.rectTransform);
        Image padlock = UiImage(cover, "Lock", uiLock, Color.white);
        Rect(padlock.gameObject, cover, AnchorCentre, new Vector2(0f, 26f), Vector2.one * 118f);
        Text soon = UiText(cover, "Soon", GameText.ComingSoon, 34, Color.white, 2.5f);
        Rect(soon.gameObject, cover, AnchorCentre, new Vector2(0f, -66f), new Vector2(440f, 44f));

        Image frame = UiImage(body.transform, "Rim", uiFrame, UiNavy, 1.5f);
        Fill(frame.rectTransform);

        RectTransform badge = UiGroup(body.transform, "Badge", new Vector2(0f, 1f), new Vector2(46f, -46f), Vector2.one * 80f);
        Image badgeEdge = UiImage(badge, "Edge", uiCircle, UiNavy);
        Fill(badgeEdge.rectTransform);
        Image badgeInside = UiImage(badge, "Inside", uiCircle, UiYellow);
        Fill(badgeInside.rectTransform, 7f, 7f, 7f, 7f);
        card.number = UiText(badge, "Number", "", 46, UiNavy);
        Fill(card.number.rectTransform, 0f, 2f);
        return card;
    }

    private static void BuildLobbyPanel(Transform canvas, TitleFlow flow)
    {
        RectTransform panel = UiPanelRoot(canvas, "LobbyPanel");
        flow.lobbyPanel = panel.gameObject;
        panel.gameObject.SetActive(false);

        Image dim = UiImage(panel, "Dim", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, 0.5f));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform inside = UiPanel(panel, "Card", AnchorCentre, Vector2.zero, new Vector2(1300f, 930f), UiCream, 0f);

        flow.roomCodeText = UiText(inside, "RoomCode", "", 62, UiNavy);
        Rect(flow.roomCodeText.gameObject, inside, AnchorTop, new Vector2(0f, -64f), new Vector2(1240f, 76f));
        flow.lobbyLevelText = UiText(inside, "Level", "", 36, new Color(0.86f, 0.42f, 0.05f));
        Rect(flow.lobbyLevelText.gameObject, inside, AnchorTop, new Vector2(0f, -128f), new Vector2(1240f, 44f));

        const float columnX = 306f;
        Vector2 buttonSize = new Vector2(590f, 88f);
        flow.orangeList = AddTeamColumn(inside, "Orange", GameText.TeamOrange, UiOrange, new Color(1f, 0.9f, 0.74f), -columnX);
        flow.blueList = AddTeamColumn(inside, "Blue", GameText.TeamBlue, UiBlue, new Color(0.8f, 0.9f, 1f), columnX);

        flow.orangeButton = UiButton(inside, "JoinOrange", GameText.JoinOrange, AnchorCentre, new Vector2(-columnX, -120f), buttonSize, UiOrange, 38, 0.05f, out _);
        flow.blueButton = UiButton(inside, "JoinBlue", GameText.JoinBlue, AnchorCentre, new Vector2(columnX, -120f), buttonSize, UiBlue, 38, 0.05f, out _);
        flow.readyButton = UiButton(inside, "Ready", GameText.Ready, AnchorCentre, new Vector2(-columnX, -218f), buttonSize, UiGreen, 38, 0.1f, out flow.readyLabel);
        flow.fillButton = UiButton(inside, "Fill", GameText.BotsFill(false), AnchorCentre, new Vector2(columnX, -218f), buttonSize, UiGray, 38, 0.1f, out flow.fillLabel);
        flow.startButton = UiButton(inside, "Start", GameText.StartGame, AnchorCentre, new Vector2(-columnX, -316f), buttonSize, UiOrange, 38, 0.15f, out _);
        flow.leaveButton = UiButton(inside, "Leave", GameText.Leave, AnchorCentre, new Vector2(columnX, -316f), buttonSize, UiGray, 38, 0.15f, out _);

        flow.lobbyHint = UiText(inside, "Hint", "", 28, new Color(0.34f, 0.37f, 0.48f));
        Rect(flow.lobbyHint.gameObject, inside, AnchorBottom, new Vector2(0f, 40f), new Vector2(1240f, 36f));
    }

    private static Text AddTeamColumn(RectTransform parent, string name, string heading, Color color, Color tint, float x)
    {
        RectTransform inside = UiPanel(parent, name, AnchorCentre, new Vector2(x, 85f), new Vector2(590f, 300f), tint, 0.03f, 1.6f);
        Image band = UiImage(inside, "Band", uiRound, color, 2f);
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.anchoredPosition = Vector2.zero;
        band.rectTransform.sizeDelta = new Vector2(0f, 68f);
        Text title = UiText(band.transform, "Heading", heading, 42, Color.white, 2.5f);
        Fill(title.rectTransform);
        Text list = UiText(inside, "List", "", 32, UiNavy);
        Fill(list.rectTransform, 20f, 10f, 20f, 78f);
        list.alignment = TextAnchor.UpperCenter;
        return list;
    }
}
