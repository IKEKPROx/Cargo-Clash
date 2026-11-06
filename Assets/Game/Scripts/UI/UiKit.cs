using UnityEngine;

namespace CargoClash
{
    /// <summary>Small UI helpers that need no art assets.</summary>
    public static class UiKit
    {
        private static Sprite triangle;

        /// <summary>An upward-pointing triangle, drawn once at runtime.</summary>
        public static Sprite TriangleSprite()
        {
            if (triangle != null) return triangle;

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                float halfWidth = (1f - (y + 0.5f) / size) * size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Abs(x + 0.5f - size * 0.5f);
                    float alpha = Mathf.Clamp01(halfWidth - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            triangle = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return triangle;
        }
    }
}
