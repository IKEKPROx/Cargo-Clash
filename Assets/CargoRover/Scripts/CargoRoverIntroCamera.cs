using UnityEngine;

/// <summary>
/// A cinematic, route-synchronised camera move: high establishing view, curved descent,
/// then a settled side view of the cargo bed. The focus stays on the body, not the boxes,
/// so a future cargo-release effect cannot pull the camera away.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class CargoRoverIntroCamera : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private RoverTerrainAnimator rover;
    [SerializeField] private Transform vehicleRoot;
    [SerializeField] private Transform bodyAnchor;

    [Header("Camera positions relative to the vehicle")]
    [SerializeField] private Vector3 aerial = new Vector3(25f, 28f, -22f);
    [SerializeField] private Vector3 sweep = new Vector3(20f, 20f, -13f);
    [SerializeField] private Vector3 approach = new Vector3(11f, 9f, -2f);
    [SerializeField] private Vector3 cargoSide = new Vector3(8.6f, 7.3f, 2.2f);

    [Header("Focus points relative to the body")]
    [SerializeField] private Vector3 aerialFocus = new Vector3(0f, 1.4f, 0f);
    [SerializeField] private Vector3 sweepFocus = new Vector3(0f, 1.9f, 0.3f);
    [SerializeField] private Vector3 approachFocus = new Vector3(0f, 2.8f, 1.1f);
    [SerializeField] private Vector3 cargoFocus = new Vector3(0f, 3.7f, 1.65f);

    [Header("Lens and settling")]
    [SerializeField, Range(35f, 75f)] private float startingFieldOfView = 58f;
    [SerializeField, Range(25f, 60f)] private float endingFieldOfView = 40f;
    [SerializeField, Range(0.01f, 1f)] private float positionLag = 0.18f;
    [SerializeField, Range(0.01f, 1f)] private float rotationLag = 0.13f;

    private Camera sceneCamera;
    private bool placed;

    public void Configure(RoverTerrainAnimator roverRig, Transform root, Transform body)
    {
        rover = roverRig;
        vehicleRoot = root;
        bodyAnchor = body;
    }

    private void Awake()
    {
        sceneCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        placed = false;
    }

    private void LateUpdate()
    {
        if (rover == null || vehicleRoot == null || bodyAnchor == null) return;

        // Smooth the overall timing, but keep movement continuous through both middle keys.
        float progress = rover.RouteProgress;
        float cinematicTime = progress * progress * (3f - 2f * progress);
        Vector3 offset = Spline(aerial, sweep, approach, cargoSide, cinematicTime);
        Vector3 focusOffset = Spline(aerialFocus, sweepFocus, approachFocus, cargoFocus,
            cinematicTime);
        Vector3 desiredPosition = vehicleRoot.TransformPoint(offset);
        Vector3 desiredFocus = bodyAnchor.TransformPoint(focusOffset);

        if (!placed)
        {
            transform.position = desiredPosition;
            transform.rotation = Quaternion.LookRotation(desiredFocus - desiredPosition,
                Vector3.up);
            placed = true;
        }
        else
        {
            float positionBlend = 1f - Mathf.Exp(-Time.deltaTime / positionLag);
            float rotationBlend = 1f - Mathf.Exp(-Time.deltaTime / rotationLag);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
            Quaternion aim = Quaternion.LookRotation(desiredFocus - transform.position,
                Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, aim, rotationBlend);
        }

        sceneCamera.fieldOfView = Mathf.Lerp(startingFieldOfView, endingFieldOfView,
            cinematicTime);
    }

    private static Vector3 Spline(Vector3 first, Vector3 second, Vector3 third,
        Vector3 fourth, float progress)
    {
        float scaled = Mathf.Clamp01(progress) * 3f;
        int segment = Mathf.Min(2, Mathf.FloorToInt(scaled));
        float t = scaled - segment;

        if (segment == 0) return CatmullRom(first, first, second, third, t);
        if (segment == 1) return CatmullRom(first, second, third, fourth, t);
        return CatmullRom(second, third, fourth, fourth, t);
    }

    private static Vector3 CatmullRom(Vector3 previous, Vector3 from, Vector3 to,
        Vector3 next, float t)
    {
        return 0.5f * ((2f * from) + (-previous + to) * t +
            (2f * previous - 5f * from + 4f * to - next) * t * t +
            (-previous + 3f * from - 3f * to + next) * t * t * t);
    }
}
