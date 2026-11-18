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
    [MenuItem("Tools/Cargo Clash/Apply Visual Refinement")]
    public static void Apply()
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
            var visuals = appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Models/Crew_{body}.fbx")
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(renderer => renderer.name);
            foreach (var visual in visuals)
            {
                var reference = original[visual.name];
                var bindPoses = reference.bones.Select((bone, index) => (bone.name, index))
                    .ToDictionary(bone => bone.name, bone => reference.sharedMesh.bindposes[bone.index]);
                for (var bone = 0; bone < visual.bones.Length; bone++)
                {
                    var expected = bindPoses[visual.bones[bone].name];
                    var actual = visual.sharedMesh.bindposes[bone];
                    for (var element = 0; element < 16; element++)
                        if (Mathf.Abs(expected[element] - actual[element]) > 0.001f)
                            throw new InvalidOperationException($"{visual.name}: bind pose changed for {visual.bones[bone].name}");
                }
                var shapes = Enumerable.Range(0, reference.sharedMesh.blendShapeCount)
                    .Select(reference.sharedMesh.GetBlendShapeName);
                if (!shapes.SequenceEqual(Enumerable.Range(0, visual.sharedMesh.blendShapeCount)
                        .Select(visual.sharedMesh.GetBlendShapeName)))
                    throw new InvalidOperationException($"{visual.name}: face controls changed");
            }

            appearances.Add(body, appearance);
        }

        const string avatarPath = "Assets/Game/Prefabs/Avatar.prefab";
        var avatar = PrefabUtility.LoadPrefabContents(avatarPath);
        ReplaceMeshes(avatar, appearances);
        PrefabUtility.SaveAsPrefabAsset(avatar, avatarPath);
        PrefabUtility.UnloadPrefabContents(avatar);
        foreach (var path in new[] { "Assets/CargoRover/Scenes/CargoRover.unity", "Assets/Game/Scenes/Level01.unity", "Assets/Game/Scenes/LevelRiver.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects()) ReplaceMeshes(root, appearances);
            EditorSceneManager.SaveScene(scene);
        }

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
        Debug.Log("[Refine] Applied all three crew appearances and tools. Original skeletons, clips, controllers and interaction markers retained.");
    }

    private static void ReplaceMeshes(GameObject root, Dictionary<BodyType, GameObject> appearances)
    {
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
            var bones = renderers.Values.SelectMany(renderer => renderer.bones.Append(renderer.rootBone))
                .Distinct().ToDictionary(bone => bone.name);
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
                renderer.sharedMaterials = Materials(visual);
            }
            var tools = crew.GetComponentsInChildren<MeshFilter>(true).ToDictionary(filter => filter.name);
            foreach (var visual in appearances[crew.body].GetComponentsInChildren<MeshFilter>(true)
                         .Where(visual => tools.ContainsKey(visual.name)))
            {
                tools[visual.name].sharedMesh = visual.sharedMesh;
                tools[visual.name].GetComponent<Renderer>().sharedMaterials = Materials(visual.GetComponent<Renderer>());
            }
        }
    }
    private static Material[] Materials(Renderer renderer) => renderer.sharedMaterials.Select(material =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{(material.name == "Char_Team" ? "Team_Orange" : material.name)}.mat")).ToArray();
}
