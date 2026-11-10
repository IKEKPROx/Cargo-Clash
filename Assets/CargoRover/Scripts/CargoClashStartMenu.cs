using UnityEngine;

/// <summary>
/// The stage behind the start screen: the truck drives down an endless road with the camera riding
/// along. When the menus open, the camera swings to the side to make room for them.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class CargoClashStartMenu : MonoBehaviour
{
    [SerializeField] private RoverTerrainAnimator rover;
    [SerializeField] private Transform vehicle;
    [SerializeField] private Camera stageCamera;
    [SerializeField, Min(0.1f)] private float driveSpeed = 5.5f;
    [SerializeField, Min(0.1f)] private float swingSeconds = 1.2f;

    [Header("Camera, measured from the truck")]
    public Vector3 titleCameraPosition = new Vector3(3.5f, 7.6f, -23f);
    public Vector3 titleCameraFocus = new Vector3(0.8f, 4.2f, 0f);
    public Vector3 menuCameraPosition = new Vector3(-8f, 7f, -19f);
    public Vector3 menuCameraFocus = new Vector3(-5f, 3.6f, 0f);

    private bool showingMenus;
    private float swing;

    public void Configure(RoverTerrainAnimator terrainRover, Transform roverRoot, Camera camera)
    {
        rover = terrainRover;
        vehicle = roverRoot;
        stageCamera = camera;
    }

    private void Awake()
    {
        rover.SetRouteAnimation(false);
    }

    private void Update()
    {
        float travel = driveSpeed * Time.deltaTime;
        vehicle.position += Vector3.right * travel;
        // The truck's front is its local -Z, which faces along the road after the 90-degree yaw.
        rover.SetExternalTravel(-travel);
    }

    private void LateUpdate()
    {
        swing = Mathf.MoveTowards(swing, showingMenus ? 1f : 0f, Time.unscaledDeltaTime / swingSeconds);
        float eased = Mathf.SmoothStep(0f, 1f, swing);

        // The camera shares the truck's travel but not its bumps, so the view stays steady.
        Vector3 position = vehicle.position + Vector3.Lerp(titleCameraPosition, menuCameraPosition, eased);
        Vector3 focus = vehicle.position + Vector3.Lerp(titleCameraFocus, menuCameraFocus, eased);
        stageCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
    }

    public void SwingToMenus()
    {
        showingMenus = true;
    }

    /// <summary>Straight to the menu view with no camera move, for coming back from a match.</summary>
    public void JumpToMenus()
    {
        showingMenus = true;
        swing = 1f;
    }
}
