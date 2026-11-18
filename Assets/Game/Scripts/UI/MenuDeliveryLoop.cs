using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The twenty-second scene behind the main menu (Tools/blender/make_menu_animation.py): the crew's clips on the
    /// crew page's bodies, and from MenuLoop.json, frame by frame on the same clock, the steering wheel, the truck's
    /// swerve and the pothole, the faces and the camera. Every moment is worked out from the time alone, so any moment
    /// can be shown (MenuLoopPreview renders them without building the game).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class MenuDeliveryLoop : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Face
        {
            public string body;
            public string shape;
            public float[] weights;
        }

        [System.Serializable]
        public sealed class Timeline
        {
            public float seconds;
            public float fps;
            public float[] wheel;
            public float[] yaw;
            public float[] side;
            public float[] roll;
            public float[] heave;
            public float[] pitch;
            public float[] cameraPosition;
            public float[] cameraFocus;
            public float[] cameraField;
            public float[] cameraOnBody;
            public Face[] faces;
        }

        [Tooltip("The truck's root, which travels down the road.")]
        public Transform truck;
        [Tooltip("BodyVisual, which RoverTerrainAnimator bounces over the road.")]
        public Transform body;
        [Tooltip("Menu_Sway, between the body and everything on it: the lean of the swerve and the pothole.")]
        public Transform sway;
        public Camera stageCamera;
        public TitleFlow flow;
        public TextAsset timelineFile;
        [Tooltip("Medium, Short and Tall, as in the timeline's faces.")]
        public CrewModel[] crew;
        public GameObject[] rigRoots;
        public AnimationClip[] clips;
        public Transform steeringWheel;
        [Tooltip("The wheel stands this far up from flat, facing the driver.")]
        public float wheelTilt = 63f;
        [Tooltip("How much of a 16:9 screen's width the main menu covers on the left. The picture is shifted right by half of it.")]
        public float menuShare = 0.33f;

        private Timeline timeline;
        private SkinnedMeshRenderer[] faceRenderers;
        private int[] faceShapes;
        private Quaternion travelTurn;
        private Vector3 appliedSide;
        private bool started;
        private float startTime;
        private float shift;

        public float LoopTime => Mathf.Repeat(Time.unscaledTime - startTime, Length);

        private float Length => Load().seconds;

        private void Awake()
        {
            startTime = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            bool menu = flow.menuPanel.activeSelf;
            shift = Mathf.MoveTowards(shift, menu ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
            Present(LoopTime, Mathf.SmoothStep(0f, 1f, shift));
        }

        public void Present(float time) => Present(time, 1f);

        private void Present(float time, float menuShift)
        {
            Timeline line = Load();
            float frame = Mathf.Repeat(time, line.seconds) * line.fps;

            for (int i = 0; i < clips.Length; i++) clips[i].SampleAnimation(rigRoots[i], Mathf.Repeat(time, line.seconds));
            for (int i = 0; i < faceShapes.Length; i++)
            {
                if (faceShapes[i] >= 0) faceRenderers[i].SetBlendShapeWeight(faceShapes[i], Sample(line.faces[i].weights, frame));
            }
            steeringWheel.localRotation = Quaternion.Euler(wheelTilt, 0f, 0f) * Quaternion.Euler(0f, Sample(line.wheel, frame), 0f);

            // The swerve turns and slides the whole truck; the steady frame is where it would be without it, which
            // the wide shots follow so the swerve shows.
            if (!started)
            {
                travelTurn = truck.rotation;
                appliedSide = Vector3.zero;
                started = true;
            }
            Vector3 steady = truck.position - appliedSide;
            appliedSide = travelTurn * new Vector3(Sample(line.side, frame), 0f, 0f);
            truck.SetPositionAndRotation(steady + appliedSide, travelTurn * Quaternion.Euler(0f, Sample(line.yaw, frame), 0f));
            sway.localPosition = new Vector3(0f, Sample(line.heave, frame), 0f);
            sway.localRotation = Quaternion.Euler(Sample(line.pitch, frame), 0f, Sample(line.roll, frame));

            Vector3 place = Sample3(line.cameraPosition, frame);
            Vector3 focus = Sample3(line.cameraFocus, frame);
            float onBody = Sample(line.cameraOnBody, frame);
            Vector3 position = Vector3.Lerp(steady + travelTurn * place, sway.TransformPoint(place), onBody);
            Vector3 target = Vector3.Lerp(steady + travelTurn * focus, sway.TransformPoint(focus), onBody);
            stageCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            stageCamera.usePhysicalProperties = true;
            stageCamera.gateFit = Camera.GateFitMode.Vertical;
            stageCamera.fieldOfView = Sample(line.cameraField, frame);
            float aspect = stageCamera.pixelHeight > 0 ? (float)stageCamera.pixelWidth / stageCamera.pixelHeight : 16f / 9f;
            float share = menuShare * Mathf.Min(1f, 16f / 9f / aspect);
            stageCamera.lensShift = new Vector2(-share * 0.5f * menuShift, 0f);
        }

        private Timeline Load()
        {
            if (timeline != null) return timeline;
            timeline = JsonUtility.FromJson<Timeline>(timelineFile.text);
            string[] names = { "Medium", "Short", "Tall" };
            faceRenderers = new SkinnedMeshRenderer[timeline.faces.Length];
            faceShapes = new int[timeline.faces.Length];
            for (int i = 0; i < timeline.faces.Length; i++)
            {
                Face face = timeline.faces[i];
                faceRenderers[i] = crew[System.Array.IndexOf(names, face.body)].porter;
                Mesh mesh = faceRenderers[i].sharedMesh;
                faceShapes[i] = -1;
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    if (mesh.GetBlendShapeName(shape).EndsWith(face.shape)) faceShapes[i] = shape;
                }
            }
            return timeline;
        }

        private static float Sample(float[] values, float frame)
        {
            int count = values.Length;
            int first = Mathf.FloorToInt(frame) % count;
            return Mathf.Lerp(values[first], values[(first + 1) % count], frame - Mathf.Floor(frame));
        }

        private static Vector3 Sample3(float[] values, float frame)
        {
            int count = values.Length / 3;
            int first = Mathf.FloorToInt(frame) % count;
            int next = (first + 1) % count;
            float t = frame - Mathf.Floor(frame);
            return Vector3.Lerp(new Vector3(values[first * 3], values[first * 3 + 1], values[first * 3 + 2]),
                new Vector3(values[next * 3], values[next * 3 + 1], values[next * 3 + 2]), t);
        }
    }
}
