using System;
using System.Collections.Generic;
using System.IO;
using CargoClash;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Astra's four kinds of cargo (独立建模/互动货物_v08, through Tools/blender/make_astra_cargo.py), what's in them, and
/// the inspect clips (Tools/blender/cargo_actions.py, written into the crew's FBX with the rest of their clips).
/// </summary>
public static partial class CargoClashBuilder
{
    private const string InspectDataPath = Root + "/Models/CargoInspect.json";

    /// <summary>
    /// Level 1 keeps the design doc's three sizes and the 500 they add up to. The two small kinds share the small
    /// spots; the goose crate is the large one that takes two, held by its side handles.
    /// </summary>
    private static readonly (CargoKind kind, CargoSize size, int value, float smash, float speed, int carriers, int count, Color chip, string sound)[] CargoKinds =
    {
        (CargoKind.Tobacco, CargoSize.Small, 5, 2f, 0.9f, 1, 6, new Color(0.95f, 0.9f, 0.76f), "crack"),
        (CargoKind.Cash, CargoSize.Small, 15, 3f, 0.9f, 1, 6, new Color(0.71f, 0.31f, 0.25f), "coins"),
        (CargoKind.Gold, CargoSize.Medium, 30, 7f, 0.7f, 1, 6, new Color(0.94f, 0.71f, 0.3f), "coins"),
        (CargoKind.Art, CargoSize.Large, 100, 16f, 0.5f, 2, 2, new Color(0.21f, 0.5f, 0.51f), "crack"),
    };

    [Serializable]
    private class InspectData
    {
        public int fps;
        public InspectClipData[] clips;
        public EyeData[] eyes;
        public float crateDistance;
        public float coCarryHandleHeight;
        public float coCarryReach;
        public float handleOut;
        public float handleAt;
    }

    [Serializable]
    private class InspectClipData
    {
        public string name;
        public string kind;
        public float seconds;
        public float drop;
        public float land;
        public float @out;
        public float start;
        public float back;
        public float shut;
        public float lift;
        public float caught;
        public float notes;
        public float puff;
        public float flick;
        public float lidOpen;
    }

    [Serializable]
    private class EyeData
    {
        public string body;
        public float x;
        public float y;
        public float z;
    }

    private static InspectData inspectData;

    private static InspectData LoadInspectData()
    {
        if (inspectData != null) return inspectData;
        inspectData = JsonUtility.FromJson<InspectData>(File.ReadAllText(InspectDataPath));
        if (inspectData.clips == null || inspectData.clips.Length == 0) Debug.LogError($"[Builder] {InspectDataPath} has no clips.");
        return inspectData;
    }

    /// <summary>A point in her units (x right of the person's left, y back, z up) as it comes out in Unity's model space.</summary>
    private static Vector3 FromBlender(float x, float y, float z) => new Vector3(-x, z, -y);

    private static CargoDefinition[] BuildCargo()
    {
        InspectData data = LoadInspectData();
        List<CargoDefinition> made = new List<CargoDefinition>();
        foreach ((CargoKind kind, CargoSize size, int value, float smash, float speed, int carriers, int count, Color chip, string sound) in CargoKinds)
        {
            string name = "Cargo_" + kind;
            CargoDefinition definition = LoadOrCreate<CargoDefinition>(DataDir + "/" + name + ".asset");
            definition.kind = kind;
            definition.size = size;
            definition.value = value;
            definition.smashSeconds = smash;
            definition.carrySpeedMultiplier = speed;
            definition.requiredCarriers = carriers;
            definition.fullCount = count;
            definition.chipColor = chip;
            definition.breakSound = sound;

            InspectClipData clip = Array.Find(data.clips, c => c.kind == kind.ToString());
            definition.inspectState = clip != null ? clip.name : null;
            definition.inspectOnGround = kind == CargoKind.Art;
            definition.inspectDistance = data.crateDistance;
            definition.inspect = clip == null ? default : new InspectTimes
            {
                seconds = clip.seconds, drop = clip.drop, land = clip.land, @out = clip.@out, start = clip.start, back = clip.back,
                shut = clip.shut, lift = clip.lift, caught = clip.caught, notes = clip.notes, puff = clip.puff, flick = clip.flick,
                lidOpen = clip.lidOpen,
            };

            definition.piecesPrefab = BuildAstraPieces(kind);
            definition.prefab = BuildAstraCargo(kind, definition, data);
            EditorUtility.SetDirty(definition);
            made.Add(definition);
        }
        return made.ToArray();
    }

    /// <summary>Her model, at the people's scale, in a cargo object: its collider covers the case and its side handles.</summary>
    private static GameObject BuildAstraCargo(CargoKind kind, CargoDefinition definition, InspectData data)
    {
        string name = "Cargo_" + kind;
        GameObject model = NewAstraModel(name, null);

        // Her case's size and its side handles, measured off the whole model at the game's scale.
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool first = true;
        Transform stage0 = FindInModel(model, "Stage0");
        foreach (Renderer part in stage0.GetComponentsInChildren<Renderer>(true))
        {
            if (first) bounds = part.bounds;
            else bounds.Encapsulate(part.bounds);
            first = false;
        }
        Vector3 half = bounds.extents;
        float bottom = bounds.min.y - model.transform.position.y;

        if (definition.requiredCarriers > 1)
        {
            // Lifted so its handle bars are where make_crew.co_carry_values has the carriers' fists.
            float handleBar = definition.size == CargoSize.Large ? data.handleAt * half.y * 2f : 0f;
            definition.coCarryHeight = data.coCarryHandleHeight - handleBar + half.y;
            float barOut = half.x - 0.014f * CrewScale;
            definition.coCarryReach = barOut + data.coCarryReach;
        }
        else
        {
            definition.coCarryHeight = 0f;
            definition.coCarryReach = 0f;
        }

        GameObject root = CargoRoot(name, half, kind == CargoKind.Art ? 9f : kind == CargoKind.Gold ? 4f : 1.5f, definition, out Transform visual);
        model.transform.SetParent(visual, false);
        model.name = "Model";
        // Her origin is the middle of the case's bottom; this object's is the middle of the collider.
        model.transform.localPosition = new Vector3(0f, -half.y - bottom, 0f) + new Vector3(-(bounds.center.x - model.transform.position.x), 0f, -(bounds.center.z - model.transform.position.z));

        GameObject[] stages = new GameObject[3];
        Transform[] lids = new Transform[3];
        GameObject[] slots = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            stages[i] = FindInModel(model, "Stage" + i).gameObject;
            lids[i] = FindInModel(model, "Lid_Pivot" + i);
            slots[i] = FindInModel(model, "Slot" + i).gameObject;
            stages[i].SetActive(i == 0);
        }
        SerializedObject item = new SerializedObject(root.GetComponent<CargoItem>());
        SetObjects(item.FindProperty("damageBodies"), stages);
        SetObjects(item.FindProperty("lids"), lids);
        SetObjects(item.FindProperty("slots"), slots);
        item.ApplyModifiedPropertiesWithoutUndo();

        SetLayerRecursively(root, GameLayers.Cargo);
        return SavePrefab(root, name);
    }

    private static void SetObjects(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>What's left when it breaks: each of her pieces gets its own collider and physics, and lands on the level.</summary>
    private static GameObject BuildAstraPieces(CargoKind kind)
    {
        string name = $"Cargo_{kind}_Pieces";
        GameObject root = NewAstraModel(name, null);
        root.name = name;
        PhysicsMaterial bounce = BouncyMaterial();
        foreach (MeshFilter part in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds bounds = part.sharedMesh.bounds;
            BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.03f);
            box.sharedMaterial = bounce;
            Rigidbody body = part.gameObject.AddComponent<Rigidbody>();
            Vector3 size = Vector3.Scale(bounds.size, part.transform.lossyScale);
            body.mass = Mathf.Clamp(size.x * size.y * size.z * 8f, 0.08f, 2f);
            body.linearDamping = 0.15f;
            body.angularDamping = 0.35f;
        }
        root.AddComponent<CargoPieces>();
        SetLayerRecursively(root, GameLayers.Debris);
        return SavePrefab(root, name);
    }

    /// <summary>One of her models (Tools/blender/make_astra_cargo.py), at the people's scale and in the project's materials.</summary>
    private static GameObject NewAstraModel(string modelName, Transform parent)
    {
        string path = Root + "/Models/" + modelName + ".fbx";
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (!Mathf.Approximately(importer.globalScale, CrewScale) || importer.importAnimation)
        {
            importer.globalScale = CrewScale;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.SaveAndReimport();
        }
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
        ApplyMaterials(model);
        return model;
    }

    /// <summary>
    /// Her palette (build_cargo.py PALETTE): sRGB colour, roughness and metallic. Metal is held back a little: the game's
    /// flat ambient light has nothing for full metal to reflect, and it goes dark.
    /// </summary>
    private static void AddAstraCargoMaterials(Dictionary<string, Material> materials)
    {
        (string name, string hex, float roughness, float metallic)[] palette =
        {
            ("Ink", "172B33", 0.48f, 0.1f), ("Teal", "367F83", 0.55f, 0.05f), ("TealLight", "62ABA5", 0.55f, 0.05f),
            ("Wood", "B78252", 0.68f, 0f), ("WoodCut", "E7BE83", 0.7f, 0f), ("Gold", "EFB64C", 0.28f, 0.78f),
            ("GoldLight", "FFE2A0", 0.25f, 0.62f), ("Steel", "7D9B9E", 0.4f, 0.6f), ("Silver", "CFDBD5", 0.35f, 0.55f),
            ("Red", "B54F40", 0.5f, 0.05f), ("RedLight", "E27B59", 0.55f, 0.02f), ("Velvet", "63476D", 0.9f, 0f),
            ("Foam", "233A3C", 0.95f, 0f), ("Ivory", "F4E6C3", 0.37f, 0f), ("Porcelain", "E7F0D7", 0.24f, 0.05f),
            ("Green", "50896D", 0.7f, 0f), ("Paper", "DBE1B2", 0.8f, 0f), ("PaperEdge", "AFB992", 0.9f, 0f),
            ("Filter", "C29154", 0.88f, 0f), ("Ash", "686A67", 0.9f, 0f), ("Ember", "F77832", 0.45f, 0f),
            ("Smoke", "C5D1CA", 1f, 0f), ("Stage", "E8DED0", 0.8f, 0f),
        };
        foreach ((string name, string hex, float roughness, float metallic) in palette)
        {
            string full = "Cargo08_" + name;
            materials[full] = FleetMat(full, hex, roughness, Mathf.Min(metallic, 0.45f));
        }
        materials["Cargo08_Ember"].EnableKeyword("_EMISSION");
        materials["Cargo08_Ember"].SetColor("_EmissionColor", new Color(0.97f, 0.47f, 0.2f) * 1.2f);
        materials["Cargo08_NotePrint"] = NoteMaterial();
    }

    /// <summary>Her play-money print, on both sides: the thrown notes tumble.</summary>
    private static Material NoteMaterial()
    {
        string texturePath = TextureDir + "/Cargo08_Note.png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.SaveAndReimport();
        Material material = Mat("Cargo08_NotePrint", Color.white, 0.15f);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetFloat("_Cull", 0f);
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// What the crew model carries for the cargo clips: the markers the real cargo follows, the props in the hands,
    /// and on the medium body the arms alone for first person (cargo_actions.py).
    /// </summary>
    private static void SetUpCrewCargo(CrewModel crew, GameObject model)
    {
        crew.caseMarker = FindInModel(model, "Marker_Case");
        crew.lidMarker = FindInModel(model, "Marker_Lid");
        crew.props = new GameObject[4];
        foreach (CargoKind kind in (CargoKind[])Enum.GetValues(typeof(CargoKind)))
        {
            GameObject prop = FindInModel(model, "Prop_" + kind).gameObject;
            prop.SetActive(false);
            crew.props[(int)kind] = prop;
        }
        crew.notes = FindInModel(model, "Prop_Notes").gameObject;
        crew.notes.SetActive(false);
        Transform arms = null;
        foreach (Transform item in model.GetComponentsInChildren<Transform>(true))
        {
            if (item.name == "FP_Arms") arms = item;
        }
        if (arms != null)
        {
            crew.firstPersonArms = arms.GetComponent<SkinnedMeshRenderer>();
            // Only the first-person rig shows these; on a body they'd sit on top of its own sleeves.
            arms.gameObject.SetActive(false);
        }
        foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true))
        {
            if (part.name.StartsWith("Prop_")) part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    /// <summary>
    /// The medium body again, for the first-person inspect: only the shirt's arms, the gloves and the props show, on the
    /// hands camera's layer, without shadows. FirstPersonRig plays its clip in step with your body's.
    /// </summary>
    private static void BuildInspectArms(FirstPersonRig rig)
    {
        CrewModel arms = NewCrewModel(BodyType.Medium, 2.8f, rig.transform, false);
        arms.gameObject.name = "Inspect Arms";
        foreach (Renderer part in arms.GetComponentsInChildren<Renderer>(true))
        {
            bool keep = part.name == "FP_Arms" || part.name == "Gloves" || part.name.StartsWith("Prop_");
            if (!keep)
            {
                Object.DestroyImmediate(part.gameObject);
                continue;
            }
            part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            part.receiveShadows = false;
            if (part is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        arms.renderers = arms.GetComponentsInChildren<Renderer>(true);
        arms.firstPersonArms.gameObject.SetActive(true);
        SetLayerRecursively(arms.gameObject, GameLayers.ViewModel);
        arms.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        rig.inspectArms = arms;

        InspectData data = LoadInspectData();
        EyeData eyes = Array.Find(data.eyes, e => e.body == "Medium");
        if (eyes != null) rig.inspectEyes = FromBlender(eyes.x, eyes.y, eyes.z);
    }
}
