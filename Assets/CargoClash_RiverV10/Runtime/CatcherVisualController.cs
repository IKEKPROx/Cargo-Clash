using UnityEngine;

namespace CargoClash.RiverV10
{
    public sealed class CatcherVisualController : MonoBehaviour
    {
        [System.Serializable] public sealed class Slot
        {
            public Transform anchor;
            public GameObject fresh, worn, critical;
            public BoxCollider blocker;
        }
        public Slot[] slots;
        [Tooltip("Demo only. Disable when the host supplies snapshots.")]
        public bool animatePreview;
        public float previewLifetimeSeconds = 60;
        public int OccupiedCount { get; private set; }

        // The host decides occupancy/expiry. Reaching visual age 1 does not independently remove a board.
        public void ApplySnapshot(bool[] occupied, double[] insertedAt, double serverNow, float lifetimeSeconds)
        {
            OccupiedCount=0;
            for(int i=0;i<slots.Length;i++)
            {
                float age=Mathf.Clamp01((float)((serverNow-insertedAt[i])/lifetimeSeconds));
                ApplySlot(i,occupied[i],age);if(occupied[i])OccupiedCount++;
            }
        }
        public void ApplySlot(int index,bool occupied,float normalizedAge)
        {
            var s=slots[index];float age=Mathf.Clamp01(normalizedAge);
            s.fresh.SetActive(occupied && age<.5f);s.worn.SetActive(occupied && age>=.5f && age<.85f);s.critical.SetActive(occupied && age>=.85f);
            if(s.blocker)s.blocker.enabled=occupied;
        }
        public void ShowPreview(int count,float age)
        {
            OccupiedCount=Mathf.Clamp(count,0,slots.Length);
            for(int i=0;i<slots.Length;i++)ApplySlot(i,i<OccupiedCount,Mathf.Clamp01(age-i*.035f));
        }
        private void Update()
        {
            if(!animatePreview)return;
            float t=Time.time%(previewLifetimeSeconds+24);
            OccupiedCount=0;
            for(int i=0;i<slots.Length;i++)
            {
                float age=(t-i*1.5f)/previewLifetimeSeconds;
                bool present=age>=0 && age<1;ApplySlot(i,present,age);if(present)OccupiedCount++;
            }
        }
    }
}
