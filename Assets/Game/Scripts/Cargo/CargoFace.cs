using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Cargo has feelings: calm on the ground, cheerful while carried, scared while being smashed,
    /// delighted once it's safe on the truck. Only for show, from replicated state on every computer.
    /// </summary>
    [RequireComponent(typeof(CargoItem))]
    public sealed class CargoFace : MonoBehaviour
    {
        private const float HurtSeconds = 0.6f;

        private static readonly Vector3 EyesCalm = Vector3.one;
        private static readonly Vector3 MouthCalm = Vector3.one;
        private static readonly Vector3 EyesCarried = new Vector3(1f, 1.05f, 1f);
        private static readonly Vector3 MouthCarried = new Vector3(1.35f, 1.8f, 1f);
        private static readonly Vector3 EyesScared = new Vector3(1.25f, 1.35f, 1f);
        private static readonly Vector3 MouthScared = new Vector3(0.9f, 3.2f, 1f);
        private static readonly Vector3 EyesWorried = new Vector3(1.1f, 1.18f, 1f);
        private static readonly Vector3 MouthWorried = new Vector3(0.8f, 1.7f, 1f);
        private static readonly Vector3 EyesHappy = new Vector3(1.08f, 0.25f, 1f);
        private static readonly Vector3 MouthHappy = new Vector3(1.5f, 2.6f, 1f);

        public Transform[] eyes;
        public Transform[] mouths;

        private CargoItem cargo;
        private Vector3 eyeScale = Vector3.one;
        private Vector3 mouthScale = Vector3.one;
        private float nextBlink;
        private float blinkUntil;

        private void Awake()
        {
            cargo = GetComponent<CargoItem>();
            nextBlink = Time.time + Random.Range(0.5f, 5f);
        }

        private void LateUpdate()
        {
            if (!cargo.IsSpawned) return;

            Vector3 eyeTarget = EyesCalm;
            Vector3 mouthTarget = MouthCalm;
            bool scared = false;
            switch (cargo.State.Value)
            {
                case CargoState.Carried:
                    eyeTarget = EyesCarried;
                    mouthTarget = MouthCarried;
                    break;
                case CargoState.Rescued:
                    eyeTarget = EyesHappy;
                    mouthTarget = MouthHappy;
                    break;
                case CargoState.Ground:
                    scared = Time.time - cargo.LastHurtTime < HurtSeconds;
                    if (scared)
                    {
                        eyeTarget = EyesScared;
                        mouthTarget = MouthScared;
                    }
                    else if (cargo.Durability.Value < 1f)
                    {
                        // It remembers.
                        eyeTarget = EyesWorried;
                        mouthTarget = MouthWorried;
                    }
                    break;
            }

            float step = 9f * Time.deltaTime;
            eyeScale = Vector3.MoveTowards(eyeScale, eyeTarget, step);
            mouthScale = Vector3.MoveTowards(mouthScale, mouthTarget, step);

            if (Time.time >= nextBlink)
            {
                blinkUntil = Time.time + 0.1f;
                nextBlink = Time.time + Random.Range(2f, 6f);
            }

            Vector3 eyesNow = eyeScale;
            if (Time.time < blinkUntil && !scared) eyesNow.y *= 0.12f;
            Vector3 mouthNow = mouthScale;
            if (scared) mouthNow.y *= 1f + Mathf.Sin(Time.time * 40f) * 0.15f;

            foreach (Transform eye in eyes) eye.localScale = eyesNow;
            foreach (Transform mouth in mouths) mouth.localScale = mouthNow;
        }
    }
}
