using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
        internal int SpawnMarkerCount { get; private set; }
        internal int SpawnBannerCount { get; private set; }
        internal Transform SpawnMarkersRoot { get; private set; }

        /// <summary>Painted standing spots and two-sided workshop signs; all decoration is non-colliding.</summary>
        private void BuildSpawnMarkers()
        {
            SpawnMarkersRoot = new GameObject("Spawn assembly markers").transform;
            SpawnMarkersRoot.SetParent(field, false);
            Material cream = SpawnMarkerMaterial("Spawn cream", new Color(223f/255f, 216f/255f, 183f/255f));
            Material navy = SpawnMarkerMaterial("Spawn workwear blue", new Color(48f/255f, 57f/255f, 80f/255f));
            Material orange = SpawnMarkerMaterial("Spawn warm orange", new Color(203f/255f, 104f/255f, 43f/255f));
            Material yellow = SpawnMarkerMaterial("Spawn helmet yellow", new Color(217f/255f, 169f/255f, 40f/255f));
            SpawnMarkerCount = SpawnBannerCount = 0;
            BuildSpawnTeam(data.collectorSpawns, "Collectors", true, orange, cream, navy, yellow);
            BuildSpawnTeam(data.interrupterSpawns, "Interrupters", false, navy, cream, navy, yellow);
        }

        private Material SpawnMarkerMaterial(string name, Color color)
        {
            Material material = new Material(Load<Material>("RuinSurface")) { name = name };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .2f);
            // Reuse the existing field lifecycle without changing the shared material asset.
            renderMaterials.Add(material, material);
            return material;
        }

        private void BuildSpawnTeam(Spot[] spots, string team, bool collectors, Material teamColor,
            Material cream, Material navy, Material yellow)
        {
            Vector3 centre = Vector3.zero;
            foreach (Spot spot in spots)
            {
                centre += spot.position;
                Transform pad = new GameObject(spot.name + " standing marker").transform;
                pad.SetParent(SpawnMarkersRoot, false);
                pad.localPosition = spot.position + Vector3.up * .025f;
                pad.localRotation = Quaternion.Euler(0, spot.yaw, 0);
                for (int side = 0; side < 16; side++)
                {
                    float angle = side * Mathf.PI * 2f / 16f;
                    float next = (side + 1) * Mathf.PI * 2f / 16f;
                    SpawnPaintStroke(pad, "Painted standing ring",
                        new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)),
                        new Vector3(Mathf.Sin(next), 0, Mathf.Cos(next)), .11f, teamColor);
                }
                // Two chunky boot prints make each of the three positions understandable up close.
                SpawnDecoration(pad, "Left boot print", PrimitiveType.Sphere,
                    new Vector3(-.19f, .005f, -.23f), new Vector3(.23f, .025f, .48f), cream);
                SpawnDecoration(pad, "Right boot print", PrimitiveType.Sphere,
                    new Vector3(.19f, .005f, -.13f), new Vector3(.23f, .025f, .48f), cream);
                SpawnPaintStroke(pad, "Forward arrow stem", new Vector3(0, 0, .2f), new Vector3(0, 0, .68f), .14f, cream);
                SpawnPaintStroke(pad, "Forward arrow left", new Vector3(-.23f, 0, .45f), new Vector3(0, 0, .7f), .14f, cream);
                SpawnPaintStroke(pad, "Forward arrow right", new Vector3(.23f, 0, .45f), new Vector3(0, 0, .7f), .14f, cream);
                SpawnMarkerCount++;
            }
            if (spots.Length == 0) return;
            centre /= spots.Length;
            Vector3 forward = new Vector3(data.size.x * .5f - centre.x, 0, data.size.y * .5f - centre.z).normalized;
            Transform banner = new GameObject(team + " assembly banner").transform;
            banner.SetParent(SpawnMarkersRoot, false);
            banner.localPosition = centre - forward * 3.4f;
            banner.localRotation = Quaternion.LookRotation(forward);
            foreach (float x in new[] { -2.1f, 2.1f })
            {
                SpawnDecoration(banner, "Rounded weighted foot", PrimitiveType.Sphere,
                    new Vector3(x, .11f, 0), new Vector3(.7f, .22f, .65f), navy, true);
                SpawnDecoration(banner, "Chunky sign post", PrimitiveType.Cylinder,
                    new Vector3(x, 1.55f, 0), new Vector3(.18f, 1.5f, .18f), navy, true);
                SpawnDecoration(banner, "Safety collar", PrimitiveType.Cylinder,
                    new Vector3(x, .8f, 0), new Vector3(.23f, .13f, .23f), yellow, true);
                SpawnDecoration(banner, "Round post cap", PrimitiveType.Sphere,
                    new Vector3(x, 3.15f, 0), Vector3.one * .28f, yellow, true);
            }
            SpawnDecoration(banner, "Rounded sign border", PrimitiveType.Sphere,
                new Vector3(0, 2.55f, 0), new Vector3(5.6f, 1.9f, .32f), navy, true);
            SpawnDecoration(banner, "Cream sign face", PrimitiveType.Sphere,
                new Vector3(0, 2.55f, 0), new Vector3(5.3f, 1.66f, .35f), cream);
            SpawnDecoration(banner, "Team colour header", PrimitiveType.Sphere,
                new Vector3(0, 3.22f, 0), new Vector3(3.3f, .24f, .37f), teamColor);
            string title = collectors ? GameText.RuinCollectorBaseEnglish : GameText.RuinInterrupterBaseEnglish;
            string hint = collectors ? GameText.RuinCollectorAssemblyEnglish : GameText.RuinInterrupterAssemblyEnglish;
            BuildSignFace(banner, true, title, hint, teamColor.color);
            BuildSignFace(banner, false, title, hint, teamColor.color);
            SpawnBannerCount++;
        }

        private static void SpawnPaintStroke(Transform parent, string name, Vector3 from, Vector3 to, float width, Material material)
        {
            Transform stroke = SpawnDecoration(parent, name, PrimitiveType.Capsule, (from + to) * .5f,
                new Vector3(width, Vector3.Distance(from, to) * .5f + width * .25f, .025f), material);
            stroke.localRotation = Quaternion.LookRotation(Vector3.up, (to - from).normalized);
        }

        private static Transform SpawnDecoration(Transform parent, string name, PrimitiveType shape,
            Vector3 position, Vector3 scale, Material material, bool outlined = false)
        {
            GameObject item = GameObject.CreatePrimitive(shape);
            item.name = name;
            Collider collider = item.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.layer = outlined ? GameLayers.Outlined : 0;
            Renderer renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = outlined ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return item.transform;
        }

        private static void BuildSignFace(Transform banner, bool front, string title, string hint, Color color)
        {
            GameObject face = new GameObject(front ? "Front localized sign" : "Back localized sign", typeof(RectTransform), typeof(Canvas));
            face.SetActive(false);
            face.transform.SetParent(banner, false);
            face.transform.localPosition = new Vector3(0, 2.55f, front ? .2f : -.2f);
            face.transform.localRotation = Quaternion.Euler(0, front ? 180 : 0, 0);
            face.transform.localScale = Vector3.one * .01f;
            face.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 140);
            Canvas canvas = face.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            MatchHud hud = MatchHud.Instance != null ? MatchHud.Instance : FindAnyObjectByType<MatchHud>();
            Font font = hud != null && hud.overlayFont != null ? hud.overlayFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            SpawnSignLabel(face.transform, "Team base", title, new Vector2(0, 19), new Vector2(440, 58), 43, color, font);
            SpawnSignLabel(face.transform, "Three player assembly", hint, new Vector2(0, -33), new Vector2(430, 34), 23,
                new Color(48f/255f, 57f/255f, 80f/255f), font);
            face.SetActive(true);
        }

        private static void SpawnSignLabel(Transform parent, string name, string english, Vector2 position,
            Vector2 size, int fontSize, Color color, Font font)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = item.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = english;
            item.AddComponent<LocalizedText>().english = english;
        }
    }
}
