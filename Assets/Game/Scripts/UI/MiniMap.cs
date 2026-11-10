using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// The level from above, north at the top. First person can't see the whole field the way the
    /// old camera could, so this shows who's where and what's left.
    /// </summary>
    public sealed class MiniMap : MonoBehaviour
    {
        public RawImage picture;
        public RectTransform dots;
        public Sprite dotSprite;
        public Sprite arrowSprite;

        private readonly List<Image> pool = new List<Image>();
        private int used;

        private void Update()
        {
            LevelLayout layout = LevelLayout.Instance;
            if (layout == null) return;
            if (picture.texture != layout.mapPicture) picture.texture = layout.mapPicture;

            used = 0;
            foreach (CargoItem cargo in CargoItem.All)
            {
                CargoState state = cargo.State.Value;
                if (state == CargoState.Destroyed || state == CargoState.Rescued) continue;
                DrawCargo(layout, cargo);
            }

            PlayerAvatar local = PlayerAvatar.Local;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                if (avatar == local) continue;
                Image dot = Blip(layout, avatar.transform.position, 16f, dotSprite);
                dot.color = avatar.Team.ToColor();
                dot.rectTransform.localRotation = Quaternion.identity;
            }

            if (local != null)
            {
                Image arrow = Blip(layout, local.transform.position, 26f, arrowSprite);
                arrow.color = Color.white;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -local.LookYaw);
            }

            for (int i = used; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        private void DrawCargo(LevelLayout layout, CargoItem cargo)
        {
            float size;
            Color color;
            switch (cargo.Definition.size)
            {
                case CargoSize.Small:
                    size = 9f;
                    color = new Color(0.95f, 0.8f, 0.55f);
                    break;
                case CargoSize.Medium:
                    size = 13f;
                    color = new Color(0.3f, 0.85f, 0.75f);
                    break;
                default:
                    size = 18f;
                    color = new Color(1f, 0.82f, 0.2f);
                    break;
            }
            Image dot = Blip(layout, cargo.transform.position, size, dotSprite);
            dot.color = color;
            dot.rectTransform.localRotation = Quaternion.identity;
        }

        private Image Blip(LevelLayout layout, Vector3 world, float size, Sprite sprite)
        {
            if (used == pool.Count)
            {
                GameObject item = new GameObject("Blip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(dots, false);
                Image created = item.GetComponent<Image>();
                created.raycastTarget = false;
                pool.Add(created);
            }
            Image image = pool[used++];
            if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
            if (image.sprite != sprite) image.sprite = sprite;

            Rect area = layout.mapArea;
            Rect box = dots.rect;
            Vector2 place = new Vector2(
                Mathf.Clamp01(Mathf.InverseLerp(area.xMin, area.xMax, world.x)),
                Mathf.Clamp01(Mathf.InverseLerp(area.yMin, area.yMax, world.z)));
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = new Vector2((place.x - 0.5f) * box.width, (place.y - 0.5f) * box.height);
            rect.sizeDelta = new Vector2(size, size);
            return image;
        }
    }
}
