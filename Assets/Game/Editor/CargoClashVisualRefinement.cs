using System;
using System.Collections.Generic;
using System.Linq;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class CargoClashVisualRefinement
{
    private const string AvatarPath = "Assets/Game/Prefabs/Avatar.prefab";
    private static readonly string[] CrewScenes =
        { "Assets/CargoRover/Scenes/CargoRover.unity", "Assets/Game/Scenes/Level01.unity", "Assets/Game/Scenes/LevelRiver.unity",
          "Assets/Game/Scenes/LevelAirport.unity" };

    // Astra's cloth (her V20 on): the two bodies carry a FabricUV, and her Blender materials put a woven normal map on the
    // overalls and the jacket (canvas twill) and on the shirts (cotton knit), at the strengths of her Normal Map nodes.
    // Only the bodies have UVs, so the weave goes on copies that only the bodies use. Hats, tools and the first-person
    // sleeves share the plain ones and have no UVs: a normal map there would read one texel and tilt the whole surface.
    private static readonly (string plain, string fabric, string weave, float strength)[] Fabrics =
    {
        ("Team_Orange", "Crew_Fabric_Orange", "Crew_CanvasTwill_Normal", 0.41f),
        ("Team_Blue", "Crew_Fabric_Blue", "Crew_CanvasTwill_Normal", 0.41f),
        ("Porter_Shirt", "Porter_Shirt_Fabric", "Crew_CottonKnit_Normal", 0.34f),
        ("Wrecker_Shirt", "Wrecker_Shirt_Fabric", "Crew_CottonKnit_Normal", 0.34f),
        ("Wrecker_Jacket", "Wrecker_Jacket_Fabric", "Crew_CanvasTwill_Normal", 0.41f),
    };

    [MenuItem("Tools/Cargo Clash/Apply Visual Refinement")]
    public static void Apply()
    {
        ApplyCrew();

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        pipeline.msaaSampleCount = 4;
        pipeline.mainLightShadowmapResolution = 4096;
        pipeline.shadowDistance = 120;
        foreach (var texture in new[] { "GroundFinish", "AsphaltGrain", "LeafPaint" })
        {
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath($"Assets/CargoClash_RiverV16/Textures/{texture}.png");
            textureImporter.anisoLevel = 8;
            textureImporter.SaveAndReimport();
        }
        EditorUtility.SetDirty(pipeline);
        var surface = Shader.Find("CargoClash/RiverV16/EnvironmentSurface");
        foreach (var name in new[] { "Leaf_Dark", "Leaf_Mid", "Leaf_Light", "Leaf_Yellow", "Pine_Dark", "Pine_Light" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{name}.mat");
            material.shader = surface;
            material.SetFloat("_UseVertexColor", 1);
            material.SetFloat("_LeafSoftness", 1);
            material.SetFloat("_DetailStrength", 0);
            material.SetFloat("_Wetness", 0);
            material.SetFloat("_RainStrength", 0);
            material.SetFloat("_WorldTextureScale", 0);
            EditorUtility.SetDirty(material);
        }
        foreach (var name in new[] { "River10_Grass", "River10_GrassLight", "River10_GrassShade", "River10_GrassOlive", "River10_Soil", "River10_Rock", "River10_RockLight", "River10_RockDark" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/CargoClash_RiverV16/Materials/{name}.mat");
            material.shader = surface;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoClash_RiverV16/Textures/GroundFinish.png"));
            material.SetFloat("_DetailStrength", name.Contains("Rock") ? 0.045f : 0.028f);
            material.SetFloat("_DetailScale", name.Contains("Rock") ? 9f : 18f);
            material.SetFloat("_WorldTextureScale", 0.65f);
            material.SetFloat("_LeafSoftness", 0);
            EditorUtility.SetDirty(material);
        }
        var outline = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Outline.mat");
        outline.SetFloat("_Width", 0.0011f);
        outline.SetColor("_Color", new Color(0.12f, 0.15f, 0.20f));
        EditorUtility.SetDirty(outline);
        ApplyMaterialFinishes();
        AssetDatabase.SaveAssets();
        Debug.Log("[Refine] Applied all three crew appearances and tools. Original skeletons, clips, controllers and interaction markers retained.");
    }

    /// <summary>
    /// The metal and gloss of the fleet's and the crew's materials. The scene builders' model materials (CargoClashBuilder.
    /// ModelMaterials) set their own first values, so a builder that runs after this calls it again (the airport's does).
    /// </summary>
    public static void ApplyMaterialFinishes()
    {
        foreach (var finish in new[] {
            ("Fleet_Navy", 0.16f, 0.62f), ("Fleet_Orange", 0.10f, 0.58f),
            ("Fleet_Cream", 0.06f, 0.54f), ("Fleet_Rubber", 0f, 0.16f),
            ("Fleet_Steel", 0.68f, 0.64f), ("Fleet_Brass", 0.58f, 0.56f),
            ("Crew_Skin", 0f, 0.22f), ("Crew_SkinShade", 0f, 0.22f),
            ("Crew_Glove", 0f, 0.18f), ("Crew_Boot", 0f, 0.22f),
            ("Crew_HatPrimary", 0.04f, 0.56f), ("Crew_Brass", 0.50f, 0.50f),
        })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{finish.Item1}.mat");
            material.SetFloat("_Metallic", finish.Item2);
            material.SetFloat("_Smoothness", finish.Item3);
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Puts Astra's appearance files (Crew_*_Visual.fbx) on every crew in the game: the match avatar, the crew page and
    /// the menu loop in the start scene, and the first-person inspect arms. Only meshes, bone lists and materials change;
    /// skeletons, clips, controllers, markers and props stay. Her files can be copied over the old ones as they are
    /// (keep the .meta files) and this run again. It can also run on its own after Build Game Content.
    /// </summary>
    [MenuItem("Tools/Cargo Clash/Apply Crew Appearance")]
    public static void ApplyCrew()
    {
        var fabrics = PrepareFabrics();
        var appearances = LoadAppearances();
        var avatar = PrefabUtility.LoadPrefabContents(AvatarPath);
        var swapped = ReplaceMeshes(avatar, appearances, fabrics);
        PrefabUtility.SaveAsPrefabAsset(avatar, AvatarPath);
        PrefabUtility.UnloadPrefabContents(avatar);
        Debug.Log($"[Refine] Avatar: {swapped} renderers");
        foreach (var path in CrewScenes)
        {
            var scene = EditorSceneManager.OpenScene(path);
            swapped = scene.GetRootGameObjects().Sum(root => ReplaceMeshes(root, appearances, fabrics));
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Refine] {path}: {swapped} renderers");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>The woven copies of the cloth materials, with her two normal maps. Run again, they follow the plain ones' colours.</summary>
    private static Dictionary<string, Material> PrepareFabrics()
    {
        foreach (var weave in Fabrics.Select(fabric => fabric.weave).Distinct())
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath($"Assets/Game/Textures/{weave}.png");
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        var made = new Dictionary<string, Material>();
        foreach (var (plain, fabric, weave, strength) in Fabrics)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{plain}.mat");
            var path = $"Assets/Game/Materials/{fabric}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            material.CopyPropertiesFromMaterial(source);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Game/Textures/{weave}.png"));
            material.SetFloat("_BumpScale", strength);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            made[plain] = material;
        }

        // The blue team's clothes swap the woven orange for the woven blue, as the plain ones do (CharacterVisual.ApplyTeamColor).
        var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Game/Resources/GameConfig.asset");
        config.orangeFabric = made["Team_Orange"];
        config.blueFabric = made["Team_Blue"];
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        return made;
    }

    /// <summary>Imports her three files and checks they fit the game's skeleton and face sliders before anything is swapped.</summary>
    private static Dictionary<BodyType, GameObject> LoadAppearances()
    {
        var appearances = new Dictionary<BodyType, GameObject>();
        foreach (var body in new[] { BodyType.Short, BodyType.Medium, BodyType.Tall })
        {
            var visualPath = $"Assets/Game/Models/Crew_{body}_Visual.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(visualPath);
            importer.globalScale = 0.84f;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = true;
            importer.optimizeGameObjects = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();

            var appearance = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Models/Crew_{body}.fbx");
            var original = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(renderer => renderer.name);
            // Her file is exported at rest; the game's Crew_*.fbx comes in at its first animated pose, so its skeleton
            // can't be read directly. Its bind poses can: every one of its meshes is bound to the whole skeleton.
            var own = Skeleton(appearance.transform.Find("Armature"));

            // Where each bone was when the game's own meshes were bound (world to bone).
            var known = new Dictionary<string, Matrix4x4>();
            foreach (var renderer in original.Values)
                for (var bone = 0; bone < renderer.bones.Length; bone++)
                    known.TryAdd(renderer.bones[bone].name, renderer.sharedMesh.bindposes[bone] * renderer.transform.worldToLocalMatrix);
            foreach (var visual in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Bind poses are in the mesh's own space, and the game keeps its old mesh objects, so hers must fit those.
                // A bone the game's meshes don't know is held to her own skeleton at rest instead.
                var reference = original[visual.name];
                for (var bone = 0; bone < visual.bones.Length; bone++)
                {
                    var name = visual.bones[bone].name;
                    var expected = known.TryGetValue(name, out var world)
                        ? world * reference.transform.localToWorldMatrix
                        : own[name].worldToLocalMatrix * visual.transform.localToWorldMatrix;
                    if (Differs(expected, visual.sharedMesh.bindposes[bone]))
                        throw new InvalidOperationException($"{body} {visual.name}: bind pose changed for {name}");
                }
                var shapes = Enumerable.Range(0, reference.sharedMesh.blendShapeCount)
                    .Select(reference.sharedMesh.GetBlendShapeName);
                if (!shapes.SequenceEqual(Enumerable.Range(0, visual.sharedMesh.blendShapeCount)
                        .Select(visual.sharedMesh.GetBlendShapeName)))
                    throw new InvalidOperationException($"{body} {visual.name}: face controls changed");
                Debug.Log($"[Refine] {body} {visual.name}: {visual.sharedMesh.triangles.Length / 3} triangles, " +
                          $"{visual.sharedMesh.vertexCount} vertices, {visual.bones.Length} bones, UVs {HasUv(visual.sharedMesh)}");
            }
            appearances.Add(body, appearance);
        }
        return appearances;
    }

    private static bool Differs(Matrix4x4 a, Matrix4x4 b)
    {
        for (var element = 0; element < 16; element++)
            if (Mathf.Abs(a[element] - b[element]) > 0.001f) return true;
        return false;
    }

    private static bool HasUv(Mesh mesh) => mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);

    /// <summary>Every bone under a rig's Armature, by name.</summary>
    private static Dictionary<string, Transform> Skeleton(Transform armature)
    {
        if (armature == null) throw new InvalidOperationException("A crew without an Armature");
        var bones = new Dictionary<string, Transform>();
        var twice = new HashSet<string>();
        foreach (var bone in armature.GetComponentsInChildren<Transform>(true))
            if (!bones.TryAdd(bone.name, bone)) twice.Add(bone.name);
        // A prop's mesh hangs on its bone under the same name (Prop_Notes). No clothes are weighted to those; leave them out
        // so that asking for one fails rather than picking the wrong one.
        foreach (var name in twice) bones.Remove(name);
        return bones;
    }

    private static int ReplaceMeshes(GameObject root, Dictionary<BodyType, GameObject> appearances, Dictionary<string, Material> fabrics)
    {
        var swapped = 0;
        foreach (var rig in root.GetComponentsInChildren<FirstPersonRig>(true))
        {
            foreach (var forearm in new[] { rig.leftArm.forearm, rig.rightArm.forearm })
                forearm.GetComponent<Renderer>().sharedMaterials = new[] {
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Porter_Shirt.mat"),
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Crew_Glove.mat"),
                };
        }
        foreach (var crew in root.GetComponentsInChildren<CrewModel>(true))
        {
            var renderers = crew.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(renderer => renderer.name);
            // The whole skeleton, not only the bones the old meshes used: hers are weighted to more of them.
            var bones = Skeleton(crew.transform.Find("Armature"));
            foreach (var visual in appearances[crew.body].GetComponentsInChildren<SkinnedMeshRenderer>(true)
                         .Where(visual => renderers.ContainsKey(visual.name)))
            {
                var renderer = renderers[visual.name];
                var bounds = renderer.localBounds;
                bounds.Encapsulate(visual.localBounds);
                renderer.sharedMesh = visual.sharedMesh;
                renderer.bones = visual.bones.Select(bone => bones[bone.name]).ToArray();
                renderer.rootBone = bones[visual.rootBone.name];
                renderer.localBounds = bounds;
                renderer.sharedMaterials = Materials(visual, HasUv(visual.sharedMesh) ? fabrics : null);
                if (renderer.bones.Length != renderer.sharedMesh.bindposes.Length)
                    throw new InvalidOperationException($"{crew.name}/{renderer.name}: bones and bind poses don't match");
                swapped++;
            }
            var tools = crew.GetComponentsInChildren<MeshFilter>(true).ToDictionary(filter => filter.name);
            foreach (var visual in appearances[crew.body].GetComponentsInChildren<MeshFilter>(true)
                         .Where(visual => tools.ContainsKey(visual.name)))
            {
                tools[visual.name].sharedMesh = visual.sharedMesh;
                tools[visual.name].GetComponent<Renderer>().sharedMaterials = Materials(visual.GetComponent<Renderer>(), null);
                swapped++;
            }
        }
        return swapped;
    }

    /// <summary>The game's materials for a model's slots, by name. Meshes with her FabricUV get the woven cloth.</summary>
    private static Material[] Materials(Renderer renderer, Dictionary<string, Material> fabrics) => renderer.sharedMaterials.Select(material =>
    {
        var name = material.name == "Char_Team" ? "Team_Orange" : material.name;
        if (fabrics != null && fabrics.TryGetValue(name, out var woven)) return woven;
        return AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{name}.mat");
    }).ToArray();
}
