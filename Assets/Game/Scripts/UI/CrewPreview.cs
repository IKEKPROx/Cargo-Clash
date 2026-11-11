using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The character on the crew page: the picked body type in the picked clothes, holding that side's tool
    /// and turning slowly on a stand. Its own camera draws it into the page's picture, on a layer no other
    /// camera draws, and it only runs while the page is open.
    /// </summary>
    public sealed class CrewPreview : MonoBehaviour
    {
        private static readonly int UpperPoseParam = Animator.StringToHash("UpperPose");
        private const int UpperBodyLayer = 1;
        private const int UpperReadyHammer = 7;
        private const int UpperReadyVacuum = 8;

        public CrewModel[] crews;
        public Transform turntable;
        public float degreesPerSecond = 24f;

        private CrewModel shown;
        private CrewLook look = CrewLook.Default;
        private bool porter = true;
        private bool changed = true;

        public void Show(CrewLook newLook, bool asPorter)
        {
            if (newLook.Equals(look) && asPorter == porter && shown != null) return;
            look = newLook;
            porter = asPorter;
            changed = true;
        }

        private void Update()
        {
            if (changed)
            {
                changed = false;
                foreach (CrewModel crew in crews)
                {
                    bool on = crew.body == look.Body;
                    if (crew.gameObject.activeSelf != on) crew.gameObject.SetActive(on);
                    if (on) shown = crew;
                }
                shown.Dress(look);
                shown.Wear(porter);
                shown.hammer.SetActive(!porter);
                shown.vacuum.SetActive(porter);
            }

            turntable.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
            // Holding the tool the way they would in a match. The upper-body layer starts switched off.
            shown.animator.SetLayerWeight(UpperBodyLayer, 1f);
            shown.animator.SetInteger(UpperPoseParam, porter ? UpperReadyVacuum : UpperReadyHammer);
        }
    }
}
