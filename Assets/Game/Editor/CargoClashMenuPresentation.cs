using System;
using System.Linq;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CargoClashMenuPresentation
{
    private static readonly Color Navy = new Color32(41, 56, 76, 255);
    private static readonly Color Cream = new Color32(255, 240, 210, 255);
    private static readonly Color Orange = new Color32(245, 139, 38, 255);
    private static readonly Color Blue = new Color32(52, 127, 195, 255);
    private static readonly Color Gold = new Color32(203, 169, 95, 255);
    private static readonly Color Ink = new Color32(139, 121, 102, 255);

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
        var scene = EditorSceneManager.OpenScene("Assets/CargoRover/Scenes/CargoRover.unity");
        var flow = Object.FindAnyObjectByType<TitleFlow>();
        var truck = Object.FindAnyObjectByType<RoverTerrainAnimator>().transform;
        var bodyRoot = truck.Find("BodyVisual");
        foreach (var child in bodyRoot.Cast<Transform>().Where(child => child.name.StartsWith("Menu_")).ToArray())
            Object.DestroyImmediate(child.gameObject);
        foreach (var old in flow.stage.GetComponents<MenuDeliveryLoop>()) Object.DestroyImmediate(old);
        var loop = flow.stage.gameObject.AddComponent<MenuDeliveryLoop>();
        loop.truck = truck;
        loop.body = bodyRoot;
        loop.camera = Camera.main;
        loop.flow = flow;
        loop.camera.nearClipPlane = 0.025f;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Models/MenuTruck.fbx");
        var parts = model.GetComponentsInChildren<MeshFilter>().ToDictionary(part => part.name);
        bodyRoot.Find("Body").GetComponent<MeshFilter>().sharedMesh = parts["Body"].sharedMesh;
        bodyRoot.Find("Body").GetComponent<Renderer>().sharedMaterials = Materials(parts["Body"].GetComponent<Renderer>());
        foreach (var name in new[] { "CabInterior", "CabSeats", "SteeringWheel" })
        {
            var item = Object.Instantiate(parts[name].gameObject, bodyRoot);
            item.name = "Menu_" + name;
            item.GetComponent<Renderer>().sharedMaterials = Materials(parts[name].GetComponent<Renderer>());
            if (name == "SteeringWheel") loop.steeringWheel = item.transform;
        }
        loop.steeringWheel.localPosition = new Vector3(0.55f, 2.64f, -2.55f);
        var actors = new GameObject("Menu_Crew").transform;
        actors.SetParent(bodyRoot, false);
        loop.crew = new CrewModel[3];
        loop.rigRoots = new GameObject[3];
        loop.clips = new AnimationClip[3];
        var types = new[] { BodyType.Medium, BodyType.Short, BodyType.Tall };
        var places = new[] { new Vector3(0.55f, 1.7800743f, -2.15f), new Vector3(-0.30f, 1.889099f, -2.20f), new Vector3(-0.80f, 1.9084f, 2.73f) };
        for (var i = 0; i < types.Length; i++)
        {
            var actor = Object.Instantiate(flow.preview.crews.Single(crew => crew.body == types[i]), actors);
            actor.name = "Menu_" + types[i];
            actor.gameObject.SetActive(true);
            actor.transform.SetLocalPositionAndRotation(places[i], Quaternion.Euler(0, 180, 0));
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
            loop.clips[i] = AssetDatabase.LoadAllAssetsAtPath($"Assets/Game/Models/Menu_{types[i]}.fbx")
                .OfType<AnimationClip>().Single(clip => clip.name == "MenuLoop");
        }
        var sleeper = loop.crew[2].porter;
        sleeper.SetBlendShapeWeight(Enumerable.Range(0, sleeper.sharedMesh.blendShapeCount)
            .Single(index => sleeper.sharedMesh.GetBlendShapeName(index).EndsWith("EyeSquint")), 90);
        var loadRoot = bodyRoot.Find("Load");
        foreach (Transform load in loadRoot)
            load.gameObject.SetActive(!(load.localPosition.x < -0.2f && load.localPosition.z > 0.7f));
        foreach (var stack in loadRoot.Cast<Transform>().Where(load => load.gameObject.activeSelf)
                     .GroupBy(load => (load.localPosition.x < 0, Mathf.RoundToInt((load.localPosition.z - 0.15f) / 1.12f))))
        {
            var floor = 2.02f;
            foreach (var load in stack.OrderBy(load => load.localPosition.y))
                floor += PlaceCargo(load, new Vector3(stack.Key.Item1 ? -0.80f : 0.78f, floor, 0.15f + stack.Key.Item2 * 1.12f), 90);
        }
        var cargoSeat = new GameObject("Menu_CargoSeat").transform;
        cargoSeat.SetParent(bodyRoot, false);
        var gold = loadRoot.Cast<Transform>().First(load => load.name.Contains("Gold"));
        var cash = loadRoot.Cast<Transform>().First(load => load.name.Contains("Cash"));
        var seat = Object.Instantiate(gold, cargoSeat);
        seat.gameObject.SetActive(true);
        var seatHeight = PlaceCargo(seat, new Vector3(-0.8f, 2.02f, 2.78f), 90);
        var seated = loop.crew[2].transform.localPosition;
        seated.y = 2.02f + seatHeight + 0.125f - 0.6015345f;
        loop.crew[2].transform.localPosition = seated;
        var backHeight = 2.02f;
        for (var i = 0; i < 4; i++)
        {
            var support = Object.Instantiate(cash, cargoSeat);
            support.gameObject.SetActive(true);
            backHeight += PlaceCargo(support, new Vector3(-0.80f, backHeight, 3.38f), 90);
        }
        var light = new GameObject("Menu_CabinLight").AddComponent<Light>();
        light.transform.SetParent(bodyRoot, false);
        light.transform.localPosition = new Vector3(0, 3.12f, -2.95f);
        light.type = LightType.Point;
        light.range = 2.5f;
        light.color = new Color(1f, 0.88f, 0.68f);
        light.shadows = LightShadows.None;
        loop.cabinLight = light;
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
        foreach (var particles in bodyRoot.GetComponentsInChildren<ParticleSystem>())
        {
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.42f, 0.15f), new GradientAlphaKey(0, 1) });
            color.color = fade;
        }

        Restyle(flow);
        loop.employeePortrait = flow.crewButton.GetComponent<UiButtonFeel>().face.transform.Find("Badge_Portrait").GetComponent<Image>();
        loop.portraits = new[] { BodyType.Short, BodyType.Medium, BodyType.Tall }.Select(body =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Game/UI/MenuPortrait_{body}.png")).ToArray();
        loop.Present(0);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Menu] Installed the original truck interior, three original crew rigs, a 20-second acting loop and dispatch UI.");
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

    private static void Restyle(TitleFlow flow)
    {
        foreach (var old in flow.menuPanel.transform.parent.Cast<Transform>().Where(child => child.name == "TitlePanel").ToArray())
            Object.DestroyImmediate(old.gameObject);
        flow.menuPanel.SetActive(true);
        foreach (var old in flow.menuPanel.transform.Cast<Transform>().Where(child => child.name == "Presentation").ToArray()) Object.DestroyImmediate(old.gameObject);
        var logo = flow.menuPanel.transform.Find("Logo");
        Place((RectTransform)logo, 340, 944, 490, 272);
        foreach (var floating in logo.GetComponentsInChildren<UiFloat>()) Object.DestroyImmediate(floating);
        var presentation = new GameObject("Presentation", typeof(RectTransform)).GetComponent<RectTransform>();
        presentation.SetParent(flow.menuPanel.transform, false);
        Stretch(presentation, 0, 0, 0, 0);
        presentation.SetAsFirstSibling();
        Box(presentation, "Shadow", 346, 480, 622, 720, new Color(0.05f, 0.08f, 0.12f, 0.24f));
        Box(presentation, "Board", 340, 495, 604, 724, Navy);
        Box(presentation, "Edge", 340, 495, 572, 660, Gold);
        Box(presentation, "Paper", 340, 495, 566, 654, Cream);
        Box(presentation, "Clip shadow", 340, 824, 226, 51, Ink);
        Box(presentation, "Steel clip", 340, 832, 220, 50, new Color32(200, 195, 177, 255));
        Box(presentation, "Clip glint", 340, 847, 198, 5, new Color32(246, 241, 220, 255));
        Box(presentation, "Clip neck", 340, 865, 72, 50, new Color32(200, 195, 177, 255));
        foreach (var x in new[] { 80f, 600f })
            foreach (var y in new[] { 790f, 205f })
            {
                var bolt = Box(presentation, "Rivet", x, y, 17, 17, Ink);
                bolt.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/UI_Circle.png");
                Box(presentation, "Screw slot", x, y + 1, 9, 2, Cream).sprite = null;
            }
        Label(presentation, "DISPATCH DESK", 340, 760, 420, 50, 42, Navy, true);
        Box(presentation, "Header line", 340, 720, 490, 2, new Color32(212, 191, 152, 255)).sprite = null;
        Label(presentation, "CARGO CO. / READY FOR DISPATCH", 340, 694, 500, 25, 17, Ink);
        StyleButton(flow.practiceButton, 340, 604, 510, 124, Orange, "START PRACTICE", 43, true);
        var practiceFace = flow.practiceButton.GetComponent<UiButtonFeel>().face.transform;
        var primary = practiceFace.Find("Label").GetComponent<Text>();
        Stretch(primary.rectTransform, 108, 40, 15, 12);
        foreach (var old in practiceFace.Cast<Transform>().Where(child => child.name == "Warm up with the crew").ToArray()) Object.DestroyImmediate(old.gameObject);
        Label(practiceFace, "Warm up with the crew", 296, 27, 330, 32, 23, Color.white);
        StyleButton(flow.createButton, 340, 476, 510, 96, Blue, "CREATE ROOM", 36, true);
        StyleButton(flow.joinButton, 507, 367, 176, 78, Navy, "JOIN", 32, false);
        var inputSlot = (RectTransform)flow.codeInput.transform.parent.parent;
        Place(inputSlot, 250, 367, 330, 78);
        flow.codeInput.GetComponent<Image>().color = new Color32(255, 247, 228, 255);
        flow.codeInput.transform.parent.GetComponent<Image>().color = new Color32(183, 162, 125, 255);
        Stretch((RectTransform)flow.codeInput.transform, 2, 5, 2, 2);
        flow.codeInput.textComponent.color = Navy;
        flow.codeInput.textComponent.fontSize = 28;
        var placeholder = (Text)flow.codeInput.placeholder;
        placeholder.color = Ink;
        placeholder.fontSize = 27;
        Box(presentation, "Footer line", 340, 302, 490, 2, new Color32(212, 191, 152, 255)).sprite = null;
        StyleButton(flow.settingsButton, 216, 252, 220, 66, Cream, "SETTINGS", 27, false, Navy);
        StyleButton(flow.quitButton, 465, 252, 220, 66, Cream, "QUIT", 27, false, Navy);
        StyleButton(flow.crewButton, 1690, 987, 360, 100, Cream, "CREW PROFILE", 29, false, Navy);
        var badge = flow.crewButton.GetComponent<UiButtonFeel>().face.transform;
        foreach (var old in badge.Cast<Transform>().Where(child => child.name == "PLAYER 1" || child.name.StartsWith("Badge_")).ToArray()) Object.DestroyImmediate(old.gameObject);
        Label(badge, "PLAYER 1", 210, 71, 200, 24, 18, Ink);
        Stretch(badge.Find("Label").GetComponent<RectTransform>(), 100, 9, 30, 30);
        Box(badge, "Badge_Back", 50, 46, 82, 80, Navy);
        var portrait = Box(badge, "Badge_Portrait", 50, 48, 82, 82, Color.white);
        portrait.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/MenuPortrait_Medium.png");
        portrait.type = Image.Type.Simple;
        portrait.preserveAspect = true;
        var arrow = Box(badge, "Badge_Arrow", 336, 46, 23, 23, Navy);
        arrow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/UI_Back.png");
        arrow.transform.localRotation = Quaternion.Euler(0, 0, 180);
        Place(flow.statusText.rectTransform, 340, 103, 550, 62);
        flow.statusText.color = Navy;
        flow.statusText.fontSize = 25;
        flow.menuPanel.transform.Find("Keys").gameObject.SetActive(false);

    }

    private static void StyleButton(Button button, float x, float y, float width, float height, Color color, string text, int size, bool icon, Color? ink = null)
    {
        Place((RectTransform)button.transform.parent, x, y, width, height);
        var feel = button.GetComponent<UiButtonFeel>();
        feel.hoverScale = 1.025f;
        feel.pressDepth = 4;
        feel.pulse = 0;
        button.GetComponent<Image>().color = color * new Color(0.66f, 0.66f, 0.66f, 1);
        feel.face.color = color;
        feel.lip.color = color * new Color(0.77f, 0.77f, 0.77f, 1);
        Stretch(feel.face.rectTransform, 2, 7, 2, 2);
        Stretch(feel.lip.rectTransform, 2, 2, 2, 2);
        var label = feel.face.transform.Find("Label").GetComponent<Text>();
        label.text = text;
        label.GetComponent<LocalizedText>().english = text;
        label.fontSize = size;
        label.color = ink ?? Color.white;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = size - 4;
        label.resizeTextMaxSize = size;
        foreach (var outline in label.GetComponents<Outline>()) Object.DestroyImmediate(outline);
        Stretch(label.rectTransform, icon ? 110 : 12, 0, 12, 0);
        foreach (var picture in feel.face.GetComponentsInChildren<Image>().Where(image => image.name == "Icon"))
        {
            picture.gameObject.SetActive(icon);
            Place(picture.rectTransform, 57, (height - 9) * 0.5f, 94, 94);
        }
    }

    private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/UI_Round.png");
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1.5f;
        image.color = color;
        image.raycastTarget = false;
        Place(image.rectTransform, x, y, width, height);
        return image;
    }

    private static Text Label(Transform parent, string words, float x, float y, float width, float height, int size, Color color, bool display = false)
    {
        var label = new GameObject(words, typeof(RectTransform), typeof(Text), typeof(LocalizedText)).GetComponent<Text>();
        label.transform.SetParent(parent, false);
        label.font = display ? AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/UI/LilitaOne-Regular.ttf") : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.color = color;
        label.text = words;
        label.GetComponent<LocalizedText>().english = words;
        label.GetComponent<LocalizedText>().boldChinese = display;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        Place(label.rectTransform, x, y, width, height);
        return label;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
