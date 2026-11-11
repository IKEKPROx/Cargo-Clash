using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// One of Astra's body types (Tools/blender/make_crew.py): a skeleton with the porters' and the wreckers'
    /// clothes on it, every hat and pair of glasses for each, and the face sliders. It dresses itself in a
    /// player's CrewLook. The character in a match (CharacterVisual) and the preview on the crew page
    /// (CrewPreview) both use it, so they always look the same.
    /// </summary>
    public sealed class CrewModel : MonoBehaviour
    {
        public BodyType body;
        public Animator animator;
        [Tooltip("How many times faster than the clip the Run animation plays at full speed. Shorter legs take quicker steps.")]
        public float runRate = 2.8f;

        [Header("Clothes")]
        [Tooltip("The transport company's work clothes, worn while Collecting.")]
        public SkinnedMeshRenderer porter;
        [Tooltip("The rival crew's work clothes, worn while Interrupting.")]
        public SkinnedMeshRenderer wrecker;
        [Tooltip("In CrewLook.Hats order, fitted to each set of clothes' head.")]
        public GameObject[] porterHats;
        public GameObject[] wreckerHats;
        [Tooltip("In CrewLook.GlassesStyles order.")]
        public SkinnedMeshRenderer[] porterGlasses;
        public SkinnedMeshRenderer[] wreckerGlasses;
        [Tooltip("Your sleeves in first person, in each set of clothes.")]
        public Material porterSleeve;
        public Material wreckerSleeve;

        [Header("Gear")]
        public GameObject hammer;
        public GameObject vacuum;
        public GameObject dizzyStars;
        public Transform handLeft;
        public Transform handRight;
        public Transform starsBone;
        public Renderer[] renderers;

        private bool wearing;
        private bool wearsPorter;

        /// <summary>Puts on the porters' clothes, or the wreckers'.</summary>
        public void Wear(bool asPorter)
        {
            if (wearing && wearsPorter == asPorter) return;
            wearing = true;
            wearsPorter = asPorter;
            porter.gameObject.SetActive(asPorter);
            wrecker.gameObject.SetActive(!asPorter);
        }

        /// <summary>The hat, glasses and face from a look, on both sets of clothes so the swap at half-time keeps them.</summary>
        public void Dress(CrewLook look)
        {
            for (int i = 0; i < porterHats.Length; i++)
            {
                bool on = look.Hat == i + 1;
                porterHats[i].SetActive(on);
                wreckerHats[i].SetActive(on);
            }
            for (int i = 0; i < porterGlasses.Length; i++)
            {
                bool on = look.Glasses == i + 1;
                porterGlasses[i].gameObject.SetActive(on);
                wreckerGlasses[i].gameObject.SetActive(on);
            }

            for (int slider = 0; slider < CrewLook.FaceSliders.Length; slider++)
            {
                string name = CrewLook.FaceSliders[slider];
                float weight = look.FaceValue(slider) * 100f;
                SetShape(porter, name, weight);
                SetShape(wrecker, name, weight);
                if (name != "EyeSpacing" && name != "EyeSize") continue;
                // Her glasses widen and grow with the eyes, so they keep sitting on them.
                foreach (SkinnedMeshRenderer glasses in porterGlasses) SetShape(glasses, name, weight);
                foreach (SkinnedMeshRenderer glasses in wreckerGlasses) SetShape(glasses, name, weight);
            }
        }

        private static void SetShape(SkinnedMeshRenderer renderer, string shape, float weight)
        {
            Mesh mesh = renderer.sharedMesh;
            int index = mesh.GetBlendShapeIndex(shape);
            if (index < 0)
            {
                // The FBX importer sometimes puts the mesh's name in front.
                for (int i = 0; i < mesh.blendShapeCount && index < 0; i++)
                {
                    if (mesh.GetBlendShapeName(i).EndsWith(shape)) index = i;
                }
            }
            if (index >= 0) renderer.SetBlendShapeWeight(index, weight);
        }
    }
}
