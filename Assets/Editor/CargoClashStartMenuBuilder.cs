using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class CargoClashStartMenuBuilder
{
    [MenuItem("Tools/Cargo Rover/Validate Streamed Terrain")]
    public static void ValidateStreamedTerrain()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play mode before validating terrain streaming.");
            return;
        }

        GameObject vehicle = GameObject.Find("Vehicle");
        CargoClashEndlessTerrain streamer = Object.FindAnyObjectByType<CargoClashEndlessTerrain>();
        if (vehicle == null || streamer == null)
        {
            Debug.LogError("Missing rover or terrain streamer.");
            return;
        }

        Vector3 originalPosition = vehicle.transform.position;
        bool passed = true;
        float[] testPositions = { 192f, 320f, 640f, 1024f };
        foreach (float x in testPositions)
        {
            vehicle.transform.position = new Vector3(x, originalPosition.y, 0f);
            streamer.RefreshNow();
            bool rayInside = Physics.Raycast(new Vector3(x + 1f, 6f, 0f), Vector3.down,
                out RaycastHit hit, 15f) && hit.collider.name.StartsWith("TerrainChunk_");
            bool tireContact = Physics.SphereCast(new Vector3(x, 6f, 0f), 1.045f,
                Vector3.down, out RaycastHit tireHit, 15f) &&
                tireHit.collider.name.StartsWith("TerrainChunk_");
            bool hasGround = rayInside && tireContact;
            if (!hasGround)
                Debug.LogError("Stream test failed at X=" + x + ": ray=" +
                    (hit.collider != null ? hit.collider.name : "none") + ", tire=" +
                    (tireHit.collider != null ? tireHit.collider.name : "none"));
            passed &= hasGround;
        }

        vehicle.transform.position = originalPosition;
        streamer.RefreshNow();
        Debug.Log(passed
            ? "Terrain streaming PASS: ground colliders at X=192, 320, 640 and 1024."
            : "Terrain streaming FAIL: missing ground collider beyond the old edge.");
    }

    [MenuItem("Tools/Cargo Rover/Capture Start Menu Preview")]
    public static void CapturePreview()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play mode before capturing the start menu preview.");
            return;
        }

        string path = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "../StartMenuPreview.png"));
        GameObject vehicle = GameObject.Find("Vehicle");
        if (vehicle != null)
        {
            Vector3 position = vehicle.transform.position;
            vehicle.transform.position = new Vector3(0f, position.y, 0f);
            CargoClashEndlessTerrain streamer =
                Object.FindAnyObjectByType<CargoClashEndlessTerrain>();
            if (streamer != null) streamer.RefreshNow();
            RoverTerrainAnimator rig = vehicle.GetComponent<RoverTerrainAnimator>();
            if (rig != null)
            {
                rig.SetExternalTravel(0f);
                rig.RefreshRigNow();
            }
        }
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("Start menu preview captured at " + path);
    }

    [MenuItem("Tools/Cargo Rover/Build Start Menu UI")]
    public static void Build()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Stop Play mode before saving the start menu scene.");
            return;
        }

        GameObject existingMenu = GameObject.Find("StartMenuCanvas");
        if (existingMenu != null)
        {
            ConfigureEndlessTerrain();
            UpgradeWordmark(existingMenu.transform.Find("CargoClashTitle"), "CARGO CLASH", 130,
                new Vector2(1200f, 175f), true);
            Transform existingButton = existingMenu.transform.Find("GoButton");
            UpgradeWordmark(existingButton != null ? existingButton.Find("GoLabel") : null,
                "GO", 57, new Vector2(108f, 64f));
            foreach (Graphic graphic in existingMenu.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.GetComponent<CanvasRenderer>() == null)
                    graphic.gameObject.AddComponent<CanvasRenderer>();
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Cargo Clash start menu typography upgraded and saved.");
            return;
        }

        GameObject vehicle = GameObject.Find("Vehicle");
        Camera camera = Camera.main;
        RoverTerrainAnimator rover = vehicle != null ? vehicle.GetComponent<RoverTerrainAnimator>() : null;
        CargoRoverIntroCamera cinematic = camera != null ? camera.GetComponent<CargoRoverIntroCamera>() : null;
        if (rover == null || cinematic == null)
        {
            Debug.LogError("Open CargoRover.unity before building the start menu.");
            return;
        }

        GameObject canvasObject = new GameObject("StartMenuCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Text title = MakeWordmark("CargoClashTitle", canvasObject.transform,
            "CARGO CLASH", new Vector2(1200f, 175f), 130);
        title.gameObject.AddComponent<CargoClashRuggedText>();
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -72f);
        Shadow titleShadow = title.gameObject.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
        titleShadow.effectDistance = new Vector2(4f, -5f);

        GameObject buttonObject = new GameObject("GoButton", typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(CargoClashButtonFrame), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -210f);
        buttonRect.sizeDelta = new Vector2(232f, 82f);
        CargoClashButtonFrame frame = buttonObject.GetComponent<CargoClashButtonFrame>();
        frame.color = Color.white;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = frame;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.8f, 0.92f, 1f);
        colors.pressedColor = new Color(0.48f, 0.72f, 0.86f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        Text go = MakeWordmark("GoLabel", buttonObject.transform,
            "GO", new Vector2(108f, 64f), 57);
        go.rectTransform.anchorMin = go.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        go.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        go.rectTransform.anchoredPosition = Vector2.zero;
        go.raycastTarget = false;

        GameObject controllerObject = new GameObject("StartMenuController");
        CargoClashStartMenu controller = controllerObject.AddComponent<CargoClashStartMenu>();
        controller.Configure(rover, vehicle.transform, camera, cinematic, canvasObject);
        ConfigureEndlessTerrain();
        UnityEventTools.AddPersistentListener(button.onClick, controller.OnGoPressed);

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Cargo Clash start menu built. Go hands off to the existing cinematic preview.");
    }

    private static void ConfigureEndlessTerrain()
    {
        GameObject environment = GameObject.Find("PreviewEnvironment");
        GameObject vehicle = GameObject.Find("Vehicle");
        Transform terrain = environment != null
            ? environment.transform.Find("TestTerrain_ReplaceWithYourTerrain") : null;
        if (environment == null || vehicle == null || terrain == null)
        {
            Debug.LogError("Cannot configure endless terrain without the rover preview environment.");
            return;
        }

        CargoClashEndlessTerrain streamer = environment.GetComponent<CargoClashEndlessTerrain>();
        if (streamer == null) streamer = environment.AddComponent<CargoClashEndlessTerrain>();
        streamer.Configure(vehicle.transform, terrain.GetComponent<MeshRenderer>(),
            terrain.GetComponent<MeshCollider>());
        EditorUtility.SetDirty(streamer);
    }

    private static Text MakeWordmark(string name, Transform parent,
        string lettering, Vector2 size, int fontSize)
    {
        GameObject objectWithText = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Text), typeof(CargoClashSolidText));
        objectWithText.transform.SetParent(parent, false);
        RectTransform rect = objectWithText.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        Text text = objectWithText.GetComponent<Text>();
        StyleText(text, lettering, fontSize);
        return text;
    }

    private static void UpgradeWordmark(Transform wordmark, string lettering, int fontSize,
        Vector2 size, bool rugged = false)
    {
        if (wordmark == null) return;
        CargoClashAngularText oldText = wordmark.GetComponent<CargoClashAngularText>();
        if (oldText != null) Object.DestroyImmediate(oldText);
        Text text = wordmark.GetComponent<Text>();
        if (text == null) text = wordmark.gameObject.AddComponent<Text>();
        if (wordmark.GetComponent<CargoClashSolidText>() == null)
            wordmark.gameObject.AddComponent<CargoClashSolidText>();
        if (rugged && wordmark.GetComponent<CargoClashRuggedText>() == null)
            wordmark.gameObject.AddComponent<CargoClashRuggedText>();
        wordmark.GetComponent<RectTransform>().sizeDelta = size;
        StyleText(text, lettering, fontSize);
    }

    private static void StyleText(Text text, string lettering, int fontSize)
    {
        text.text = lettering;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontStyle = FontStyle.Bold;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        text.raycastTarget = false;
    }
}
