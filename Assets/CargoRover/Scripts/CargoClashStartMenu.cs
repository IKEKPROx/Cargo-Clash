using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Keeps the terrain-following rover moving under a constant overhead follow shot.
/// The Go event is the hand-off point for a future game flow.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class CargoClashStartMenu : MonoBehaviour
{
    [SerializeField] private RoverTerrainAnimator rover;
    [SerializeField] private Transform vehicle;
    [SerializeField] private Camera menuCamera;
    [SerializeField] private CargoRoverIntroCamera cinematicCamera;
    [SerializeField] private GameObject menuCanvas;
    [SerializeField] private Vector3 fixedCameraPosition = new Vector3(0f, 27f, -18f);
    [SerializeField] private Vector3 fixedCameraFocus = new Vector3(0f, 0.7f, 0f);
    [SerializeField] private float driveStartX = -32f;
    [SerializeField] private float driveZ = 0f;
    [SerializeField, Min(0.1f)] private float driveSpeed = 4.2f;
    [SerializeField] private UnityEvent onGoPressed = new UnityEvent();

    public UnityEvent GoPressed => onGoPressed;

    private bool menuActive = true;
    private bool startingPass = true;
    private Transform fillLight;
    private Vector3 fillLightRestPosition;
    private CargoClashEndlessTerrain endlessTerrain;

    public void Configure(RoverTerrainAnimator terrainRover, Transform roverRoot, Camera camera,
        CargoRoverIntroCamera cinematic, GameObject canvas)
    {
        rover = terrainRover;
        vehicle = roverRoot;
        menuCamera = camera;
        cinematicCamera = cinematic;
        menuCanvas = canvas;
    }

    private void Awake()
    {
        if (rover == null || vehicle == null || menuCamera == null || cinematicCamera == null || menuCanvas == null)
        {
            Debug.LogError("Cargo Clash start menu is missing a scene reference.", this);
            enabled = false;
            return;
        }

        rover.SetRouteAnimation(false);
        endlessTerrain = FindAnyObjectByType<CargoClashEndlessTerrain>();
        cinematicCamera.enabled = false;
        menuCamera.fieldOfView = 56f;
        GameObject fill = GameObject.Find("Fill Light");
        if (fill != null)
        {
            fillLight = fill.transform;
            fillLightRestPosition = fillLight.position;
        }
    }

    private void Update()
    {
        if (!menuActive) return;

        // Wait until the rover rig has captured the separately rooted cargo's rest offsets.
        if (startingPass)
        {
            startingPass = false;
            vehicle.SetPositionAndRotation(new Vector3(driveStartX, vehicle.position.y, driveZ),
                Quaternion.Euler(0f, -90f, 0f));
            rover.SetExternalTravel(0f);
            return;
        }

        float travel = driveSpeed * Time.deltaTime;
        vehicle.position = new Vector3(vehicle.position.x + travel, vehicle.position.y, driveZ);
        // Vehicle front faces local -Z after the 90-degree yaw.
        rover.SetExternalTravel(-travel);
    }

    private void LateUpdate()
    {
        if (!menuActive) return;

        // Camera and focus share the vehicle's translation, never its pitch/roll.
        // This preserves one overhead angle while the terrain moves underneath.
        Vector3 cameraPosition = vehicle.position + fixedCameraPosition;
        Vector3 cameraFocus = vehicle.position + fixedCameraFocus;
        menuCamera.transform.SetPositionAndRotation(cameraPosition,
            Quaternion.LookRotation(cameraFocus - cameraPosition, Vector3.up));
        if (fillLight != null)
            fillLight.position = new Vector3(vehicle.position.x + fillLightRestPosition.x,
                fillLightRestPosition.y, fillLightRestPosition.z);
    }

    public void OnGoPressed()
    {
        if (!menuActive) return;

        menuActive = false;
        menuCanvas.SetActive(false);
        if (fillLight != null) fillLight.position = fillLightRestPosition;
        rover.SetRouteAnimation(true);
        rover.RestartRoute();
        if (endlessTerrain != null) endlessTerrain.RefreshNow();
        rover.RefreshRigNow();
        cinematicCamera.enabled = true;
        onGoPressed.Invoke();
        enabled = false;
    }
}
