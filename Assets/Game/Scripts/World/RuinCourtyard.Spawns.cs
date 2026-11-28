using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
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
