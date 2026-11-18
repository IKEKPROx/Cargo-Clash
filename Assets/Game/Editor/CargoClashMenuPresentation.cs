using System.Linq;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class CargoClashMenuPresentation
{
    private const string TitleScene = "Assets/CargoRover/Scenes/CargoRover.unity";
    // Shared with Tools/blender/make_menu_truck.py and make_menu_animation.py, in the truck body's space.
    private static readonly Vector3 Wheel = new Vector3(0.62f, 2.64f, -2.55f);
    private static readonly (BodyType body, Vector3 place, float yaw)[] Actors =
    {
        (BodyType.Medium, new Vector3(0.62f, 1.78f, -2.15f), 180f),
        (BodyType.Short, new Vector3(-0.40f, 1.889f, -2.20f), 180f),
        (BodyType.Tall, new Vector3(-0.80f, 1.749f, 1.02f), 0f),
    };

    /// <summary>
    /// The start scene's loop and its main menu: the truck with the cab you can see into (MenuTruck.fbx), the crew in it
    /// playing Menu_*.fbx, MenuLoop.json for everything else on the clock, and the menu's buttons. Run it after the
    /// Blender scripts; it can run again (it takes out what it put in first).
    /// </summary>
    [MenuItem("Tools/Cargo Clash/Apply Menu Presentation")]
    public static void Apply()
    {
        foreach (var body in new[] { "Medium", "Short", "Tall" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath($"Assets/Game/Models/Menu_{body}.fbx");
            importer.globalScale = 0.84f;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = false;
            importer.importLights = false;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips) { clip.name = "MenuLoop"; clip.loopTime = true; }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
        var truckImporter = (ModelImporter)AssetImporter.GetAtPath("Assets/Game/Models/MenuTruck.fbx");
        truckImporter.importAnimation = false;
        truckImporter.importCameras = false;
        truckImporter.importLights = false;
        truckImporter.meshCompression = ModelImporterMeshCompression.Off;
        truckImporter.SaveAndReimport();
        AssetDatabase.ImportAsset("Assets/Game/Models/MenuLoop.json", ImportAssetOptions.ForceUpdate);

        var scene = EditorSceneManager.OpenScene(TitleScene);
        var flow = Object.FindAnyObjectByType<TitleFlow>();
        var truck = Object.FindAnyObjectByType<RoverTerrainAnimator>().transform;
        // The menu's truck is its own: the cab is opened up and the body's parts are moved under Menu_Sway.
        if (PrefabUtility.IsPartOfPrefabInstance(truck.gameObject))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(truck.gameObject), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var bodyVisual = truck.Find("BodyVisual");

        // Everything on the body goes under one more transform, which the loop leans and jolts on top of the road's bumps.
        var sway = bodyVisual.Find("Menu_Sway");
        if (sway == null)
        {
            sway = new GameObject("Menu_Sway").transform;
            sway.SetParent(bodyVisual, false);
        }
        foreach (var child in bodyVisual.Cast<Transform>().Where(child => child != sway).ToArray()) child.SetParent(sway, true);
        sway.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var child in sway.Cast<Transform>().Where(child => child.name.StartsWith("Menu_")).ToArray())
            Object.DestroyImmediate(child.gameObject);
        foreach (var old in flow.stage.GetComponents<MenuDeliveryLoop>()) Object.DestroyImmediate(old);

        var loop = flow.stage.gameObject.AddComponent<MenuDeliveryLoop>();
        loop.truck = truck;
        loop.body = bodyVisual;
        loop.sway = sway;
        loop.flow = flow;
        loop.stageCamera = Camera.main;
        loop.stageCamera.nearClipPlane = 0.1f;
        loop.timelineFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Models/MenuLoop.json");

        // The truck, opened at its windows, with the cab's inside.
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Models/MenuTruck.fbx");
        var parts = model.GetComponentsInChildren<MeshFilter>().ToDictionary(part => part.name);
        sway.Find("Body").GetComponent<MeshFilter>().sharedMesh = parts["Body"].sharedMesh;
        sway.Find("Body").GetComponent<Renderer>().sharedMaterials = Materials(parts["Body"].GetComponent<Renderer>());
        foreach (var name in new[] { "CabInterior", "CabSeats", "SteeringWheel" })
        {
            var item = Object.Instantiate(parts[name].gameObject, sway);
            item.name = "Menu_" + name;
            item.GetComponent<Renderer>().sharedMaterials = Materials(parts[name].GetComponent<Renderer>());
            item.layer = GameLayers.Outlined;
            if (name == "SteeringWheel") loop.steeringWheel = item.transform;
        }
        loop.steeringWheel.localPosition = Wheel;

        // The crew: the crew page's bodies in the porters' clothes, played by the loop rather than their Animator.
        var actors = new GameObject("Menu_Crew").transform;
        actors.SetParent(sway, false);
        loop.crew = new CrewModel[Actors.Length];
        loop.rigRoots = new GameObject[Actors.Length];
        loop.clips = new AnimationClip[Actors.Length];
        for (var i = 0; i < Actors.Length; i++)
        {
            var actor = Object.Instantiate(flow.preview.crews.Single(crew => crew.body == Actors[i].body), actors);
            actor.name = "Menu_" + Actors[i].body;
            actor.gameObject.SetActive(true);
            actor.transform.SetLocalPositionAndRotation(Actors[i].place, Quaternion.Euler(0, Actors[i].yaw, 0));
            actor.transform.localScale = Vector3.one;
            actor.animator.enabled = false;
            actor.Wear(true);
            actor.Dress(CrewLook.Default);
            actor.hammer.SetActive(false);
            actor.vacuum.SetActive(false);
            actor.dizzyStars.SetActive(false);
            actor.ShowProp(-1);
            foreach (var transform in actor.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = GameLayers.Player;
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;
            loop.crew[i] = actor;
            loop.rigRoots[i] = actor.transform.Find("Armature").gameObject;
            loop.clips[i] = AssetDatabase.LoadAllAssetsAtPath($"Assets/Game/Models/Menu_{Actors[i].body}.fbx")
                .OfType<AnimationClip>().Single(clip => clip.name == "MenuLoop");
        }

        // The load: stacks down both sides, with room on the camera's side for the napper's long case, his back
        // against the stack behind the cab.
        var loadRoot = sway.Find("Load");
        foreach (Transform load in loadRoot) load.gameObject.SetActive(true);
        // On the camera's side only the stack behind the cab stays, and the bottom case of the next one, his bed.
        var napStack = loadRoot.Cast<Transform>().Where(load => load.localPosition.x < -0.2f && load.localPosition.z > 0.7f)
            .OrderBy(load => Mathf.Abs(load.localPosition.z - 1.27f)).ThenBy(load => load.localPosition.y).ToArray();
        foreach (var load in napStack.Skip(1)) load.gameObject.SetActive(false);
        foreach (var stack in loadRoot.Cast<Transform>().Where(load => load.gameObject.activeSelf)
                     .GroupBy(load => (load.localPosition.x < 0, Mathf.RoundToInt((load.localPosition.z - 0.15f) / 1.12f))))
        {
            var floor = 2.02f;
            foreach (var load in stack.OrderBy(load => load.localPosition.y))
                floor += PlaceCargo(load, new Vector3(stack.Key.Item1 ? -0.80f : 0.78f, floor, 0.15f + stack.Key.Item2 * 1.12f), 90);
        }

        // A soft light under the cab's roof, so faces seen through the windscreen aren't in the roof's shadow.
        var light = new GameObject("Menu_CabinLight").AddComponent<Light>();
        light.transform.SetParent(sway, false);
        light.transform.localPosition = new Vector3(0.05f, 3.32f, -3.15f);
        light.type = LightType.Point;
        light.range = 3.2f;
        light.intensity = 0.9f;
        light.color = new Color(1f, 0.93f, 0.82f);
        light.shadows = LightShadows.None;

        var puff = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Title_Puff.mat");
        puff.SetFloat("_Surface", 1);
        puff.SetFloat("_Blend", 0);
        puff.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        puff.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        puff.SetFloat("_ZWrite", 0);
        puff.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        puff.SetOverrideTag("RenderType", "Transparent");
        puff.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(puff);
        foreach (var particles in sway.GetComponentsInChildren<ParticleSystem>())
        {
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.42f, 0.15f), new GradientAlphaKey(0, 1) });
            color.color = fade;
        }

        CargoClashBuilder.RebuildMainMenu(flow);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Menu] The truck's cab, the crew's loop and the main menu are in the start scene.");
    }

    private static Material[] Materials(Renderer renderer) => renderer.sharedMaterials.Select(material =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/Game/Materials/{material.name}.mat")).ToArray();

    private static float PlaceCargo(Transform cargo, Vector3 bottom, float yaw)
    {
        cargo.localRotation = Quaternion.Euler(-90, yaw, 0);
        var corners = cargo.GetComponentsInChildren<MeshFilter>().SelectMany(mesh =>
            Enumerable.Range(0, 8).Select(index => cargo.parent.InverseTransformPoint(mesh.transform.TransformPoint(
                mesh.sharedMesh.bounds.center + Vector3.Scale(mesh.sharedMesh.bounds.extents,
                    new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1)))))).ToArray();
        var min = corners.Aggregate(Vector3.Min);
        var max = corners.Aggregate(Vector3.Max);
        cargo.localPosition += bottom - new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
        return max.y - min.y;
    }

}
