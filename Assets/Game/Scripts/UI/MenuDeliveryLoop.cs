using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    [DefaultExecutionOrder(100)]
    public sealed class MenuDeliveryLoop : MonoBehaviour
    {
        public Transform truck;
        public Transform body;
        public Camera camera;
        public TitleFlow flow;
        public CrewModel[] crew;
        public GameObject[] rigRoots;
        public AnimationClip[] clips;
        public Transform steeringWheel;
        public Light cabinLight;
        public Image employeePortrait;
        public Sprite[] portraits;

        private float started;

        public float LoopTime => Mathf.Repeat(Time.unscaledTime - started, 20f);

        private void Awake() => started = Time.unscaledTime;

        private void LateUpdate() => Present(LoopTime);

        public void Present(float time)
        {
            var t = Mathf.Repeat(time, 20f);
            employeePortrait.sprite = portraits[(int)GameSettings.Look.Body];
            for (var i = 0; i < crew.Length; i++) clips[i].SampleAnimation(rigRoots[i], t);
            var grab = Beat(t, 8, 10.4f, 12.8f, 15.8f);
            steeringWheel.localRotation = Quaternion.Euler(63, 0, 0) *
                Quaternion.Euler(0, Mathf.Sin((t - 10.4f) * Mathf.PI * 2) * grab * 12, 0);
            cabinLight.intensity = 0.35f;

            var menuVisible = flow.menuPanel.activeSelf;
            var interior = menuVisible && t >= 8f && t < 14.3f;
            var bedside = menuVisible && t < 8f ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3.2f, 4.8f, t)) : 0f;
            var position = Vector3.Lerp(new Vector3(-12, 7.3f, -15), new Vector3(-8.3f, 7.0f, 0.5f), bedside);
            var focus = Vector3.Lerp(new Vector3(0, 2.3f, 0.2f), new Vector3(-0.45f, 2.70f, 2.50f), bedside);
            var field = Mathf.Lerp(38, 39, bedside);
            if (interior)
            {
                position = new Vector3(0.10f, 3.08f, -3.56f);
                focus = new Vector3(0.10f, 2.70f, -2.0f);
                field = 78;
            }
            var cameraAnchor = interior ? body : truck;
            var worldPosition = cameraAnchor.TransformPoint(position);
            var worldFocus = cameraAnchor.TransformPoint(focus);
            var direction = Quaternion.LookRotation(worldFocus - worldPosition, Vector3.up);
            var space = menuVisible ? (interior ? 0.16f : 0.39f) : 0f;
            worldFocus -= direction * Vector3.right * (Vector3.Distance(worldFocus, worldPosition) * Mathf.Tan(field * Mathf.Deg2Rad * 0.5f) * camera.aspect * space);
            camera.fieldOfView = field;
            camera.transform.SetPositionAndRotation(worldPosition, Quaternion.LookRotation(worldFocus - worldPosition, Vector3.up));
        }

        private static float Beat(float t, float start, float rise, float fall, float end) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, rise, t)) *
            (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fall, end, t)));
    }
}
