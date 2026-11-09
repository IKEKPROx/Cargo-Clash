using System;
using UnityEngine;

namespace CargoClash
{
    /// <summary>One slot in the level list. A level with no scene yet shows as a locked card.</summary>
    [Serializable]
    public sealed class LevelInfo
    {
        public string title;
        [Tooltip("The scene to load, which must be in Build Settings. Leave it empty and the card stays locked.")]
        public string scene;
        public Sprite picture;

        public bool IsReady => !string.IsNullOrEmpty(scene);
    }
}
