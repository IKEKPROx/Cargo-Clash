using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CargoRoverBuilder
{
    private const string RootFolder = "Assets/CargoRover";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string MeshFolder = RootFolder + "/Meshes";
    private const string PrefabFolder = RootFolder + "/Prefabs";
    private const string SceneFolder = RootFolder + "/Scenes";

    [MenuItem("Tools/Cargo Rover/Regenerate Preview Terrain")]
    public static void RegeneratePreviewTerrain()
    {
        Mesh mesh = GetPreviewTerrainMesh();
        MeshCollider terrainCollider = GameObject.Find("TestTerrain_ReplaceWithYourTerrain")?.GetComponent<MeshCollider>();
        if (terrainCollider != null)
        {
            terrainCollider.sharedMesh = null;
            terrainCollider.sharedMesh = mesh;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Cargo Rover preview terrain regenerated with stronger rolling hills.");
    }

    [MenuItem("Tools/Cargo Rover/Refit Windshield")]
    public static void RefitWindshield()
    {
        string prefabPath = PrefabFolder + "/Vehicle.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform cabin = prefabRoot.transform.Find("BodyVisual/Cabin");
            SetWindshieldPartPosition(cabin, "Windshield", new Vector3(0f, 2.57f, -3.4f));
            SetWindshieldPartPosition(cabin, "WindshieldFrame_Top", new Vector3(0f, 3.15f, -3.12f));
            SetWindshieldPartPosition(cabin, "WindshieldFrame_Bottom", new Vector3(0f, 1.99f, -3.68f));
            SetWindshieldPartPosition(cabin, "WindshieldFrame_L", new Vector3(-1.27f, 2.57f, -3.4f));
            SetWindshieldPartPosition(cabin, "WindshieldFrame_R", new Vector3(1.27f, 2.57f, -3.4f));
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Cargo Rover windshield refitted to cabin front slope.");
    }

    private static void SetWindshieldPartPosition(Transform cabin, string partName, Vector3 position)
    {
        Transform part = cabin.Find(partName);
        if (part == null)
        {
            throw new System.InvalidOperationException("Missing windshield part: " + partName);
        }

        part.localPosition = position;
    }

    [MenuItem("Tools/Cargo Rover/Build Final Model")]
    public static void BuildFinalModel()
    {
        EnsureFolders();
        RenderPipelineAsset pipelineAsset =
            AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipelineAsset != null)
        {
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
        }

        Material teal = GetMaterial("Cabin_Teal", new Color(0.035f, 0.43f, 0.62f));
        Material orange = GetMaterial("Cargo_Orange", new Color(1.0f, 0.31f, 0.025f));
        Material charcoal = GetMaterial("Chassis_Charcoal", new Color(0.075f, 0.085f, 0.105f));
        Material rubber = GetMaterial("Tire_Rubber", new Color(0.025f, 0.028f, 0.035f));
        Material tread = GetMaterial("Tire_Tread", new Color(0.038f, 0.041f, 0.048f));
        Material glass = GetMaterial("Windshield_Dark", new Color(0.025f, 0.055f, 0.085f), 0.7f);
        Material light = GetMaterial("Lamp_Warm", new Color(1.0f, 0.82f, 0.48f), 0.25f, true);
        Material ground = GetMaterial("Ground_Gray", new Color(0.22f, 0.23f, 0.25f));

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject vehicleSource = BuildVehicle(teal, orange, charcoal, rubber, tread, glass, light);
        GameObject cargoSource = BuildCargoBox(orange, charcoal);

        string vehiclePrefabPath = PrefabFolder + "/Vehicle.prefab";
        string cargoPrefabPath = PrefabFolder + "/CargoBox.prefab";
        PrefabUtility.SaveAsPrefabAsset(vehicleSource, vehiclePrefabPath);
        PrefabUtility.SaveAsPrefabAsset(cargoSource, cargoPrefabPath);
        Object.DestroyImmediate(vehicleSource);
        Object.DestroyImmediate(cargoSource);

        GameObject vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(vehiclePrefabPath);
        GameObject cargoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cargoPrefabPath);

        GameObject vehicle = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab);
        vehicle.name = "Vehicle";
        vehicle.transform.position = Vector3.zero;

        GameObject cargoA = (GameObject)PrefabUtility.InstantiatePrefab(cargoPrefab);
        cargoA.name = "CargoBox_A";
        cargoA.transform.position = new Vector3(0f, 2.0f, 0.85f);

        GameObject cargoB = (GameObject)PrefabUtility.InstantiatePrefab(cargoPrefab);
        cargoB.name = "CargoBox_B";
        cargoB.transform.position = new Vector3(0f, 2.0f, 2.52f);

        vehicle.GetComponent<RoverTerrainAnimator>()?.SetCargo(cargoA.transform, cargoB.transform);

        BuildPreviewEnvironment(ground, vehicle);

        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
            SceneFolder + "/CargoRover.unity"
        );
        CargoClashStartMenuBuilder.Build();

        Selection.activeGameObject = vehicle;
        SceneView.lastActiveSceneView?.FrameSelected();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Cargo Rover built with terrain-following preview. Vehicle and cargo are independent prefab instances. Rigidbody count: " +
                  Object.FindObjectsByType<Rigidbody>().Length);
    }

    private static GameObject BuildVehicle(
        Material teal,
        Material orange,
        Material charcoal,
        Material rubber,
        Material tread,
        Material glass,
        Material light)
    {
        GameObject vehicle = new GameObject("Vehicle");

        Transform bodyVisual = NewGroup("BodyVisual", vehicle.transform);
        Transform chassis = NewGroup("Chassis", bodyVisual);
        const float wheelCenterY = 1.05f;
        // Unity's built-in cylinder has a 0.5-unit radius, so a scale of 2.0 gives a 1-unit wheel radius.
        const float wheelRadius = 2.0f;
        const float wheelOffsetX = 2.36f;
        const float chassisY = 1.5f;
        Box("FrameRail_L", chassis, new Vector3(-1.25f, chassisY, 0.1f), new Vector3(0.3f, 0.42f, 8.35f), charcoal);
        Box("FrameRail_R", chassis, new Vector3(1.25f, chassisY, 0.1f), new Vector3(0.3f, 0.42f, 8.35f), charcoal);

        float[] crossMemberZ = { -3.25f, -1.45f, 0.55f, 2.25f, 3.65f };
        for (int i = 0; i < crossMemberZ.Length; i++)
        {
            Box("Crossmember_" + (i + 1), chassis, new Vector3(0f, chassisY, crossMemberZ[i]),
                new Vector3(2.8f, 0.24f, 0.3f), charcoal);
        }

        Box("FrontBumper", chassis, new Vector3(0f, 1.68f, -4.08f), new Vector3(3.7f, 0.4f, 0.38f), charcoal);
        Box("FrontBumperMount_L", chassis, new Vector3(-1.1f, 1.5f, -3.82f), new Vector3(0.3f, 0.3f, 0.72f), charcoal);
        Box("FrontBumperMount_R", chassis, new Vector3(1.1f, 1.5f, -3.82f), new Vector3(0.3f, 0.3f, 0.72f), charcoal);
        Box("FrontSkidPlate", chassis, new Vector3(0f, 1.23f, -3.72f), new Vector3(2.65f, 0.16f, 1.05f),
            charcoal, new Vector3(12f, 0f, 0f));

        Box("CabMount_Front_L", chassis, new Vector3(-1.25f, 1.8f, -2.85f), new Vector3(0.52f, 0.24f, 0.55f), charcoal);
        Box("CabMount_Front_R", chassis, new Vector3(1.25f, 1.8f, -2.85f), new Vector3(0.52f, 0.24f, 0.55f), charcoal);
        Box("CabMount_Rear_L", chassis, new Vector3(-1.25f, 1.8f, -0.85f), new Vector3(0.52f, 0.24f, 0.55f), charcoal);
        Box("CabMount_Rear_R", chassis, new Vector3(1.25f, 1.8f, -0.85f), new Vector3(0.52f, 0.24f, 0.55f), charcoal);

        float[] axleZ = { -2.75f, 0.15f, 2.95f };
        Transform suspension = NewGroup("Suspension", vehicle.transform);
        Transform axles = NewGroup("Axles", suspension);
        Transform wheels = NewGroup("Wheels", vehicle.transform);
        Transform[] wheelPivots = new Transform[6];
        Transform[] shockMounts = new Transform[6];
        Transform[] shocks = new Transform[6];
        Transform[] shockFeet = new Transform[6];
        Transform[] trailingArms = new Transform[6];
        Transform[] axleVisuals = new Transform[3];
        Transform[] differentialVisuals = new Transform[3];

        for (int axleIndex = 0; axleIndex < axleZ.Length; axleIndex++)
        {
            float z = axleZ[axleIndex];
            axleVisuals[axleIndex] = Cylinder("Axle_" + (axleIndex + 1), axles,
                new Vector3(0f, wheelCenterY, z), new Vector3(0.16f, 2.42f, 0.16f),
                new Vector3(0f, 0f, 90f), charcoal).transform;
            differentialVisuals[axleIndex] = Box("Differential_" + (axleIndex + 1), axles,
                new Vector3(0f, wheelCenterY, z), new Vector3(0.72f, 0.52f, 0.62f), charcoal).transform;

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                string suffix = side < 0 ? "L" : "R";
                int wheelIndex = axleIndex * 2 + sideIndex;

                trailingArms[wheelIndex] = Box("TrailingArm_" + (axleIndex + 1) + "_" + suffix, suspension,
                    new Vector3(side * 1.58f, 1.17f, z + (axleIndex == 0 ? 0.38f : -0.38f)),
                    new Vector3(0.3f, 0.2f, 1.0f), charcoal).transform;
                shocks[wheelIndex] = Cylinder("Shock_" + (axleIndex + 1) + "_" + suffix, suspension,
                    new Vector3(side * 1.48f, 1.65f, z), new Vector3(0.13f, 0.48f, 0.13f),
                    Vector3.zero, orange).transform;
                shockFeet[wheelIndex] = Cylinder("ShockLower_" + (axleIndex + 1) + "_" + suffix, suspension,
                    new Vector3(side * 1.48f, 1.12f, z), new Vector3(0.17f, 0.08f, 0.17f),
                    Vector3.zero, charcoal).transform;
                shockMounts[wheelIndex] = Box("ShockMount_" + (axleIndex + 1) + "_" + suffix, chassis,
                    new Vector3(side * 1.48f, 2.08f, z), new Vector3(0.34f, 0.18f, 0.34f), charcoal).transform;

                Transform wheelPivot = NewGroup("WheelPivot_" + (axleIndex + 1) + "_" + suffix, wheels);
                wheelPivot.localPosition = new Vector3(side * wheelOffsetX, wheelCenterY, z);
                wheelPivots[wheelIndex] = wheelPivot;
                Cylinder("Wheel_" + (axleIndex + 1) + "_" + suffix, wheelPivot,
                    Vector3.zero, new Vector3(wheelRadius, 0.38f, wheelRadius),
                    new Vector3(0f, 0f, 90f), rubber);

                Cylinder("HubRing_" + (axleIndex + 1) + "_" + suffix, wheelPivot,
                    new Vector3(side * 0.41f, 0f, 0f), new Vector3(1.12f, 0.08f, 1.12f),
                    new Vector3(0f, 0f, 90f), orange);
                Cylinder("HubCap_" + (axleIndex + 1) + "_" + suffix, wheelPivot,
                    new Vector3(side * 0.5f, 0f, 0f), new Vector3(0.42f, 0.055f, 0.42f),
                    new Vector3(0f, 0f, 90f), charcoal);

                // Two shallow staggered rows follow the tire circumference like molded tread blocks.
                for (int row = 0; row < 2; row++)
                {
                    for (int treadIndex = 0; treadIndex < 16; treadIndex++)
                    {
                        float angle = treadIndex * 22.5f + row * 11.25f;
                        float radians = angle * Mathf.Deg2Rad;
                        float rowOffset = row == 0 ? -0.19f : 0.19f;
                        Box("Tread_" + (axleIndex + 1) + "_" + suffix + "_" + row + "_" + (treadIndex + 1), wheelPivot,
                            new Vector3(rowOffset, Mathf.Cos(radians) * 1.015f,
                                Mathf.Sin(radians) * 1.015f),
                            new Vector3(0.38f, 0.065f, 0.27f), tread, new Vector3(angle, 0f, 0f));
                    }
                }

                for (int lugIndex = 0; lugIndex < 6; lugIndex++)
                {
                    float angle = lugIndex * 60f;
                    float radians = angle * Mathf.Deg2Rad;
                    Cylinder("Lug_" + (axleIndex + 1) + "_" + suffix + "_" + (lugIndex + 1), wheelPivot,
                        new Vector3(side * 0.57f, Mathf.Cos(radians) * 0.4f,
                            Mathf.Sin(radians) * 0.4f),
                        new Vector3(0.12f, 0.035f, 0.12f), new Vector3(0f, 0f, 90f), charcoal);
                }
            }
        }

        Transform cabin = NewGroup("Cabin", bodyVisual);
        GameObject cabinBody = new GameObject("CabinBody");
        cabinBody.transform.SetParent(cabin, false);
        MeshFilter cabinFilter = cabinBody.AddComponent<MeshFilter>();
        cabinFilter.sharedMesh = GetCabinWedgeMesh();
        cabinBody.AddComponent<MeshRenderer>().sharedMaterial = teal;

        // Match the cabin wedge's front slope so the glass and frame sit against the body.
        Box("Windshield", cabin, new Vector3(0f, 2.57f, -3.4f), new Vector3(2.52f, 1.3f, 0.07f),
            glass, new Vector3(26f, 0f, 0f));
        Box("WindshieldFrame_Top", cabin, new Vector3(0f, 3.15f, -3.12f), new Vector3(2.62f, 0.1f, 0.12f), charcoal,
            new Vector3(26f, 0f, 0f));
        Box("WindshieldFrame_Bottom", cabin, new Vector3(0f, 1.99f, -3.68f), new Vector3(2.62f, 0.1f, 0.12f), charcoal,
            new Vector3(26f, 0f, 0f));
        Box("WindshieldFrame_L", cabin, new Vector3(-1.27f, 2.57f, -3.4f), new Vector3(0.1f, 1.34f, 0.12f), charcoal,
            new Vector3(26f, 0f, 0f));
        Box("WindshieldFrame_R", cabin, new Vector3(1.27f, 2.57f, -3.4f), new Vector3(0.1f, 1.34f, 0.12f), charcoal,
            new Vector3(26f, 0f, 0f));

        Box("UnderCabSill", cabin, new Vector3(0f, 1.88f, -2.15f), new Vector3(2.9f, 0.26f, 2.72f), teal);
        Box("FrontFascia", cabin, new Vector3(0f, 1.76f, -3.82f), new Vector3(3.08f, 0.3f, 0.2f), teal);
        Box("LowerGrille", cabin, new Vector3(0f, 1.56f, -4.28f), new Vector3(1.5f, 0.24f, 0.1f), charcoal);
        Box("SideStep_L", cabin, new Vector3(-1.72f, 1.72f, -0.72f), new Vector3(0.36f, 0.14f, 1.15f), charcoal);
        Box("SideStep_R", cabin, new Vector3(1.72f, 1.72f, -0.72f), new Vector3(0.36f, 0.14f, 1.15f), charcoal);

        Transform frontLights = NewGroup("FrontLights", cabin);
        Box("HeadlightHousing_L", frontLights, new Vector3(-1.05f, 1.92f, -4.13f), new Vector3(0.56f, 0.5f, 0.3f), charcoal);
        Box("HeadlightHousing_R", frontLights, new Vector3(1.05f, 1.92f, -4.13f), new Vector3(0.56f, 0.5f, 0.3f), charcoal);
        Box("Headlight_L", frontLights, new Vector3(-1.05f, 1.92f, -4.3f), new Vector3(0.34f, 0.28f, 0.07f), light);
        Box("Headlight_R", frontLights, new Vector3(1.05f, 1.92f, -4.3f), new Vector3(0.34f, 0.28f, 0.07f), light);

        Transform roofLights = NewGroup("RoofLightBar", cabin);
        Box("RoofCap", roofLights, new Vector3(0f, 3.34f, -1.82f), new Vector3(2.62f, 0.14f, 1.42f), teal);
        Box("LightBarBase", roofLights, new Vector3(0f, 3.48f, -1.95f), new Vector3(1.86f, 0.14f, 0.44f), charcoal);
        for (int i = 0; i < 3; i++)
        {
            float lampX = -0.65f + i * 0.65f;
            Box("RoofLampHousing_" + (i + 1), roofLights, new Vector3(lampX, 3.66f, -2.1f),
                new Vector3(0.46f, 0.4f, 0.3f), charcoal);
            Box("RoofLamp_" + (i + 1), roofLights, new Vector3(lampX, 3.66f, -2.27f),
                new Vector3(0.28f, 0.23f, 0.06f), light);
        }

        Transform bed = NewGroup("CargoBed", bodyVisual);
        Box("BedFloor", bed, new Vector3(0f, 1.88f, 1.66f), new Vector3(3.76f, 0.22f, 4.88f), charcoal);
        Box("BedRail_L", bed, new Vector3(-1.78f, 2.54f, 1.66f), new Vector3(0.2f, 1.1f, 4.88f), orange);
        Box("BedRail_R", bed, new Vector3(1.78f, 2.54f, 1.66f), new Vector3(0.2f, 1.1f, 4.88f), orange);
        Box("BedRail_Rear", bed, new Vector3(0f, 2.54f, 4.01f), new Vector3(3.76f, 1.1f, 0.18f), orange);
        Box("BedSupport_Front", bed, new Vector3(0f, 2.54f, -0.7f), new Vector3(3.76f, 1.1f, 0.26f), orange);
        Box("BedSupport_Rear", bed, new Vector3(0f, 1.7f, 3.45f), new Vector3(3.0f, 0.25f, 0.3f), charcoal);

        vehicle.AddComponent<RoverTerrainAnimator>().ConfigureRig(bodyVisual, wheelPivots,
            shockMounts, shocks, shockFeet, trailingArms, axleVisuals, differentialVisuals);
        return vehicle;
    }

    private static GameObject BuildCargoBox(Material orange, Material charcoal)
    {
        GameObject root = new GameObject("CargoBox");
        Transform shell = NewGroup("Shell", root.transform);
        Box("Body", shell, new Vector3(0f, 0.62f, 0f), new Vector3(3.1f, 1.18f, 1.42f), orange);
        Box("Lid", shell, new Vector3(0f, 1.28f, 0f), new Vector3(3.18f, 0.18f, 1.5f), orange);

        Transform straps = NewGroup("LockingStraps", root.transform);
        Box("Strap_L_Top", straps, new Vector3(-0.92f, 1.4f, 0f), new Vector3(0.18f, 0.08f, 1.55f), charcoal);
        Box("Strap_R_Top", straps, new Vector3(0.92f, 1.4f, 0f), new Vector3(0.18f, 0.08f, 1.55f), charcoal);
        Box("Strap_L_Front", straps, new Vector3(-0.92f, 0.72f, -0.74f), new Vector3(0.18f, 1.34f, 0.08f), charcoal);
        Box("Strap_R_Front", straps, new Vector3(0.92f, 0.72f, -0.74f), new Vector3(0.18f, 1.34f, 0.08f), charcoal);

        Transform guards = NewGroup("CornerGuards", root.transform);
        float[] xs = { -1.5f, 1.5f };
        float[] zs = { -0.66f, 0.66f };
        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                Box("Guard", guards, new Vector3(x, 0.72f, z), new Vector3(0.22f, 1.36f, 0.18f), charcoal);
            }
        }

        Box("InsetPanel_Left", root.transform, new Vector3(-1.57f, 0.68f, 0f), new Vector3(0.05f, 0.48f, 0.68f), charcoal);
        Box("InsetPanel_Right", root.transform, new Vector3(1.57f, 0.68f, 0f), new Vector3(0.05f, 0.48f, 0.68f), charcoal);

        return root;
    }

    private static void BuildPreviewEnvironment(Material ground, GameObject vehicle)
    {
        GameObject environment = new GameObject("PreviewEnvironment");
        GameObject terrain = new GameObject("TestTerrain_ReplaceWithYourTerrain");
        terrain.transform.SetParent(environment.transform, false);
        Mesh terrainMesh = GetPreviewTerrainMesh();
        terrain.AddComponent<MeshFilter>().sharedMesh = terrainMesh;
        terrain.AddComponent<MeshRenderer>().sharedMaterial = ground;
        terrain.AddComponent<MeshCollider>().sharedMesh = terrainMesh;

        GameObject lightObject = new GameObject("Directional Light");
        lightObject.transform.SetParent(environment.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        Light directionalLight = lightObject.AddComponent<Light>();
        directionalLight.type = LightType.Directional;
        directionalLight.intensity = 1.35f;
        directionalLight.shadows = LightShadows.Soft;

        GameObject fillObject = new GameObject("Fill Light");
        fillObject.transform.SetParent(environment.transform, false);
        fillObject.transform.position = new Vector3(-5f, 5f, -2f);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 18f;
        fill.intensity = 650f;
        fill.color = new Color(0.55f, 0.72f, 1f);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(environment.transform, false);
        cameraObject.transform.position = new Vector3(11.6f, 5.7f, -14.4f);
        cameraObject.transform.LookAt(new Vector3(0f, 1.75f, 0.1f));
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        camera.fieldOfView = 38f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
        cameraObject.AddComponent<CargoRoverIntroCamera>().Configure(
            vehicle.GetComponent<RoverTerrainAnimator>(), vehicle.transform,
            vehicle.transform.Find("BodyVisual"));
    }

    private static Mesh GetPreviewTerrainMesh()
    {
        const string path = MeshFolder + "/PreviewTerrain.asset";
        const int xSegments = 384;
        const int zSegments = 256;
        const float width = 384f;
        const float length = 256f;
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Mesh mesh = new Mesh { name = "PreviewTerrain" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        Vector3[] vertices = new Vector3[(xSegments + 1) * (zSegments + 1)];
        int[] triangles = new int[xSegments * zSegments * 6];

        for (int z = 0; z <= zSegments; z++)
        {
            float worldZ = -length * 0.5f + length * z / zSegments;
            for (int x = 0; x <= xSegments; x++)
            {
                float worldX = -width * 0.5f + width * x / xSegments;
                float height = CargoClashEndlessTerrain.HeightAt(worldX, worldZ);
                vertices[z * (xSegments + 1) + x] = new Vector3(worldX, height, worldZ);
            }
        }

        int triangle = 0;
        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                int a = z * (xSegments + 1) + x;
                int b = a + 1;
                int c = a + xSegments + 1;
                int d = c + 1;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Mesh GetCabinWedgeMesh()
    {
        string path = MeshFolder + "/CabinWedge.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Mesh mesh = new Mesh { name = "CabinWedge" };
        mesh.vertices = new[]
        {
            new Vector3(-1.52f, 1.75f, -3.78f), new Vector3(1.52f, 1.75f, -3.78f),
            new Vector3(-1.72f, 1.75f, -0.72f), new Vector3(1.72f, 1.75f, -0.72f),
            new Vector3(-1.3f, 3.3f, -3.03f), new Vector3(1.3f, 3.3f, -3.03f),
            new Vector3(-1.5f, 3.3f, -0.82f), new Vector3(1.5f, 3.3f, -0.82f)
        };
        mesh.triangles = new[]
        {
            0, 4, 1, 1, 4, 5,
            2, 3, 6, 3, 7, 6,
            0, 2, 4, 2, 6, 4,
            1, 5, 3, 3, 5, 7,
            4, 6, 5, 5, 6, 7,
            0, 1, 2, 1, 3, 2
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, Vector3 rotation = default)
    {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = name;
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.localPosition = position;
        gameObject.transform.localEulerAngles = rotation;
        gameObject.transform.localScale = scale;
        gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        RemoveCollider(gameObject);
        return gameObject;
    }

    private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale,
        Vector3 rotation, Material material)
    {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        gameObject.name = name;
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.localPosition = position;
        gameObject.transform.localEulerAngles = rotation;
        gameObject.transform.localScale = scale;
        gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        RemoveCollider(gameObject);
        return gameObject;
    }

    private static void RemoveCollider(GameObject gameObject)
    {
        Collider collider = gameObject.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }
    }

    private static Transform NewGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static Material GetMaterial(string name, Color color, float smoothness = 0.25f, bool emissive = false)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = GraphicsSettings.defaultRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        shader ??= Shader.Find("Standard");

        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

        if (emissive && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "CargoRover");
        EnsureFolder(RootFolder, "Materials");
        EnsureFolder(RootFolder, "Meshes");
        EnsureFolder(RootFolder, "Prefabs");
        EnsureFolder(RootFolder, "Scenes");
        EnsureFolder(RootFolder, "Scripts");
        EnsureFolder("Assets", "Editor");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
