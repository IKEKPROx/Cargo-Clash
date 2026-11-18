using System;
using System.Globalization;
using System.IO;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders the start scene's delivery loop at chosen moments without building the game, to check the crew's acting
/// and the shots. The menus are an overlay canvas, so they aren't in these pictures; a built game's -ccShotDir has them.
///
///   Unity -batchmode -quit -projectPath . -executeMethod MenuLoopPreview.Render -previewOut folder
///         [-previewApply] [-previewTimes "0,2.5,9"] [-previewShots "name:time:x,y,z:x,y,z:fov|..."]
///
/// -previewApply runs Tools > Cargo Clash > Apply Menu Presentation first (after new Menu_*.fbx clips).
/// -previewTimes renders the loop's own camera at those times. -previewShots puts the camera at a position and a
/// point to look at, both in the truck body's space (BodyVisual), at that time of the loop, with that field of view.
/// </summary>
public static class MenuLoopPreview
{
    public static void Render()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = Argument(args, "-previewOut") ?? "Temp/MenuLoopPreview";
        Directory.CreateDirectory(output);
        if (Array.IndexOf(args, "-previewApply") >= 0) CargoClashMenuPresentation.Apply();

        EditorSceneManager.OpenScene("Assets/CargoRover/Scenes/CargoRover.unity");
        MenuDeliveryLoop loop = UnityEngine.Object.FindAnyObjectByType<MenuDeliveryLoop>();
        Camera camera = Camera.main;
        int width = int.Parse(Argument(args, "-previewWidth") ?? "1600");
        int height = int.Parse(Argument(args, "-previewHeight") ?? "900");
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        Texture2D picture = new Texture2D(width, height, TextureFormat.RGB24, false);

        Camera ui = null;
        if (Array.IndexOf(args, "-previewUi") >= 0)
        {
            // The menus are an overlay canvas, which a camera doesn't draw. For the pictures it goes in front of a camera
            // of its own, drawn over the stage without clearing it, and without the lens shift that moves the stage over.
            ui = new GameObject("PreviewUiCamera").AddComponent<Camera>();
            ui.cullingMask = 1 << 5;
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.backgroundColor = new Color(0f, 0f, 0f, 0f);
            ui.nearClipPlane = 0.1f;
            ui.farClipPlane = 10f;
            ui.transform.position = new Vector3(0f, -500f, 0f);
            Canvas canvas = GameObject.Find("GameMenuCanvas").GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = ui;
            canvas.planeDistance = 1f;
            foreach (Transform part in canvas.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 5;
        }

        void Shoot(string name)
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = null;
            if (ui != null)
            {
                // The menu on its own, on clear, laid over the stage here.
                Canvas.ForceUpdateCanvases();
                RenderTexture layer = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                ui.targetTexture = layer;
                ui.Render();
                ui.targetTexture = null;
                Texture2D menu = new Texture2D(width, height, TextureFormat.RGBA32, false);
                RenderTexture.active = layer;
                menu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(layer);
                Color[] under = picture.GetPixels();
                Color[] over = menu.GetPixels();
                for (int i = 0; i < under.Length; i++) under[i] = Color.Lerp(under[i], new Color(over[i].r, over[i].g, over[i].b), over[i].a);
                picture.SetPixels(under);
                picture.Apply();
                UnityEngine.Object.DestroyImmediate(menu);
            }
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), picture.EncodeToPNG());
            Debug.Log($"[Preview] {name}");
        }

        if (Array.IndexOf(args, "-previewDump") >= 0)
        {
            // Where everything on the truck is, in the body's space, for placing the crew and the cameras.
            foreach (Renderer renderer in loop.body.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = new Bounds(loop.body.InverseTransformPoint(renderer.bounds.center), Vector3.zero);
                Vector3 extents = renderer.bounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    local.Encapsulate(loop.body.InverseTransformPoint(renderer.bounds.center + Vector3.Scale(extents, offset)));
                }
                string path = renderer.name;
                for (Transform parent = renderer.transform.parent; parent != null && parent != loop.body; parent = parent.parent) path = parent.name + "/" + path;
                Debug.Log($"[Dump] {path} active={renderer.gameObject.activeInHierarchy} min={local.min.ToString("F2")} max={local.max.ToString("F2")}");
            }
        }

        // Without a player loop the skinned meshes keep the pose they were drawn with first, unless told otherwise.
        foreach (SkinnedMeshRenderer skin in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            skin.forceMatrixRecalculationPerRender = true;
        // The first picture after opening the scene comes out in flat colours; take one to throw away.
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = null;

        string probe = Argument(args, "-previewProbe");
        if (probe != null)
        {
            // "Part:ymin,ymax:zmin,zmax": the x range of that part's vertices inside the window, in the body's space.
            foreach (string item in probe.Split('|'))
            {
                string[] parts = item.Split(':');
                Vector2 ys = Pair(parts[1]);
                Vector2 zs = Pair(parts[2]);
                MeshFilter filter = loop.body.Find(parts[0]).GetComponent<MeshFilter>();
                float low = float.MaxValue, high = float.MinValue;
                int count = 0;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = loop.body.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (point.y < ys.x || point.y > ys.y || point.z < zs.x || point.z > zs.y) continue;
                    low = Mathf.Min(low, point.x);
                    high = Mathf.Max(high, point.x);
                    count++;
                }
                Debug.Log($"[Probe] {item} vertices={count} x={low:F2}..{high:F2}");
            }
        }

        string times = Argument(args, "-previewTimes");
        if (times != null)
        {
            foreach (string item in times.Split(','))
            {
                float time = float.Parse(item, CultureInfo.InvariantCulture);
                loop.Present(time);
                Shoot($"loop_{time:00.00}");
                if (Array.IndexOf(args, "-previewBones") >= 0)
                {
                    foreach (CrewModel actor in loop.crew)
                    {
                        string line = "";
                        foreach (string bone in new[] { "Hips", "Head", "LeftHand", "RightHand", "LeftLowerArm", "RightLowerArm" })
                        {
                            Transform found = null;
                            foreach (Transform t in actor.GetComponentsInChildren<Transform>(true)) if (t.name == bone) found = t;
                            line += $" {bone}={loop.sway.InverseTransformPoint(found.position).ToString("F2")}";
                            if (bone == "Head") line += $" headFwd={loop.sway.InverseTransformDirection(found.forward).ToString("F2")}";
                        }
                        Debug.Log($"[Bones] {time:F2} {actor.name}{line}");
                    }
                    foreach (AnimationClip clip in loop.clips) Debug.Log($"[Clip] {clip.name} length={clip.length} rate={clip.frameRate}");
                }
                if (Array.IndexOf(args, "-previewFaces") < 0) continue;
                foreach (CrewModel actor in loop.crew)
                {
                    Mesh mesh = actor.porter.sharedMesh;
                    string weights = "";
                    for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                        weights += $" {mesh.GetBlendShapeName(shape)}={actor.porter.GetBlendShapeWeight(shape):F0}";
                    Debug.Log($"[Faces] {time:F2} {actor.name} mesh={mesh.name}{weights}");
                }
            }
        }

        string shots = Argument(args, "-previewShots");
        if (shots != null)
        {
            foreach (string shot in shots.Split('|'))
            {
                string[] parts = shot.Split(':');
                loop.Present(float.Parse(parts[1], CultureInfo.InvariantCulture));
                Vector3 position = loop.body.TransformPoint(Vector(parts[2]));
                Vector3 focus = loop.body.TransformPoint(Vector(parts[3]));
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
                camera.fieldOfView = float.Parse(parts[4], CultureInfo.InvariantCulture);
                Shoot(parts[0]);
            }
        }
    }

    private static string Argument(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Vector2 Pair(string text)
    {
        string[] values = text.Split(',');
        return new Vector2(float.Parse(values[0], CultureInfo.InvariantCulture), float.Parse(values[1], CultureInfo.InvariantCulture));
    }

    private static Vector3 Vector(string text)
    {
        string[] values = text.Split(',');
        return new Vector3(float.Parse(values[0], CultureInfo.InvariantCulture),
            float.Parse(values[1], CultureInfo.InvariantCulture), float.Parse(values[2], CultureInfo.InvariantCulture));
    }
}
