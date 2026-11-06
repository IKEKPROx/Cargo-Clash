using UnityEngine;

/// <summary>
/// Animation-oriented six-wheel ground following. No Rigidbody or WheelCollider is required.
/// Wheel order is front-left, front-right, middle-left, middle-right, rear-left, rear-right.
/// </summary>
public sealed class RoverTerrainAnimator : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Transform bodyVisual;
    [SerializeField] private Transform[] wheelPivots = new Transform[6];
    [SerializeField] private Transform[] shockMounts = new Transform[6];
    [SerializeField] private Transform[] shocks = new Transform[6];
    [SerializeField] private Transform[] shockFeet = new Transform[6];
    [SerializeField] private Transform[] trailingArms = new Transform[6];
    [SerializeField] private Transform[] axles = new Transform[3];
    [SerializeField] private Transform[] differentials = new Transform[3];
    [SerializeField] private Transform[] cargoBoxes = new Transform[2];

    [Header("Terrain and suspension")]
    [SerializeField] private LayerMask terrainLayers = ~0;
    [SerializeField, Min(0.1f)] private float wheelRadius = 1f;
    [SerializeField, Range(0.1f, 0.8f)] private float suspensionTravel = 0.38f;
    [SerializeField, Range(0.05f, 0.8f)] private float bodyResponse = 0.22f;
    [SerializeField, Range(0f, 12f)] private float maxTiltDegrees = 7f;

    [Header("Preview route (world Z)")]
    [SerializeField] private bool animateRoute = true;
    [SerializeField] private float routeStartZ = 5f;
    [SerializeField] private float routeEndZ = -5f;
    [SerializeField, Min(0f)] private float speed = 1.2f;

    private readonly Vector3[] restWheelPositions = new Vector3[6];
    private readonly Vector3[] cargoLocalPositions = new Vector3[2];
    private readonly Quaternion[] cargoLocalRotations = new Quaternion[2];
    private readonly float[] sampledWheelY = new float[6];
    private readonly RaycastHit[] groundHits = new RaycastHit[32];
    private float restWheelY;
    private float bodyYVelocity;
    private float pitchVelocity;
    private float rollVelocity;
    private float pitch;
    private float roll;
    private float spinDegrees;
    private float externalTravel;
    private bool initialized;

    public float RouteProgress => !animateRoute || Mathf.Approximately(routeStartZ, routeEndZ)
        ? 0f : Mathf.InverseLerp(routeStartZ, routeEndZ, transform.position.z);

    public void SetRouteAnimation(bool active)
    {
        animateRoute = active;
        externalTravel = 0f;
    }

    /// <summary>Signed forward distance supplied by the fixed-camera menu path.</summary>
    public void SetExternalTravel(float signedDistance)
    {
        externalTravel = signedDistance;
    }

    public void RestartRoute()
    {
        transform.SetPositionAndRotation(new Vector3(0f, transform.position.y, routeStartZ),
            Quaternion.identity);
        if (initialized) UpdateRig(0f, 0f);
    }

    /// <summary>Synchronize wheels and separate cargo immediately after a deliberate teleport.</summary>
    public void RefreshRigNow()
    {
        if (initialized) UpdateRig(0f, 0f);
    }

    public void ConfigureRig(Transform body, Transform[] wheels, Transform[] mounts,
        Transform[] shockBodies, Transform[] feet, Transform[] arms,
        Transform[] axleVisuals, Transform[] differentialVisuals)
    {
        bodyVisual = body;
        wheelPivots = wheels;
        shockMounts = mounts;
        shocks = shockBodies;
        shockFeet = feet;
        trailingArms = arms;
        axles = axleVisuals;
        differentials = differentialVisuals;
    }

    public void SetCargo(Transform first, Transform second)
    {
        cargoBoxes = new[] { first, second };
    }

    private void Start()
    {
        if (bodyVisual == null || wheelPivots == null || wheelPivots.Length != 6)
        {
            Debug.LogError("Rover terrain animator is missing its wheel rig.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < 6; i++)
        {
            if (wheelPivots[i] == null)
            {
                Debug.LogError("Rover terrain animator has an unassigned wheel pivot.", this);
                enabled = false;
                return;
            }
            restWheelPositions[i] = wheelPivots[i].localPosition;
        }
        restWheelY = restWheelPositions[0].y;

        for (int i = 0; i < cargoLocalPositions.Length; i++)
        {
            if (cargoBoxes == null || i >= cargoBoxes.Length || cargoBoxes[i] == null) continue;
            cargoLocalPositions[i] = transform.InverseTransformPoint(cargoBoxes[i].position);
            cargoLocalRotations[i] = Quaternion.Inverse(transform.rotation) * cargoBoxes[i].rotation;
        }

        if (animateRoute)
        {
            Vector3 start = transform.position;
            start.z = routeStartZ;
            transform.position = start;
        }

        initialized = true;
        UpdateRig(0f, 0f);
    }

    private void Update()
    {
        if (!initialized) return;

        float travel = externalTravel;
        externalTravel = 0f;
        if (animateRoute && speed > 0f)
        {
            float nextZ = Mathf.MoveTowards(transform.position.z, routeEndZ, speed * Time.deltaTime);
            travel = nextZ - transform.position.z;
            Vector3 next = transform.position;
            next.z = nextZ;
            transform.position = next;
        }

        UpdateRig(Time.deltaTime, travel);
    }

    private void UpdateRig(float deltaTime, float travel)
    {
        float sum = 0f;
        float left = 0f;
        float right = 0f;

        for (int i = 0; i < 6; i++)
        {
            Vector3 rest = restWheelPositions[i];
            float targetY = float.NegativeInfinity;
            // Sweep the tire's actual rolling volume, including the shallow tread.
            // The visual rig has no wheel colliders, so only ground mesh colliders
            // are eligible; decorative rover parts cannot become false contacts.
            Vector3 wheelPoint = transform.TransformPoint(new Vector3(rest.x, 0f, rest.z));
            Vector3 origin = new Vector3(wheelPoint.x, transform.position.y + 5f, wheelPoint.z);
            int hits = Physics.SphereCastNonAlloc(origin, wheelRadius + 0.045f,
                Vector3.down, groundHits, 12f, terrainLayers, QueryTriggerInteraction.Ignore);
            float shortestDistance = float.PositiveInfinity;
            for (int hitIndex = 0; hitIndex < hits; hitIndex++)
            {
                Collider ground = groundHits[hitIndex].collider;
                if (!(ground is MeshCollider) && !(ground is TerrainCollider)) continue;
                shortestDistance = Mathf.Min(shortestDistance, groundHits[hitIndex].distance);
            }
            if (!float.IsPositiveInfinity(shortestDistance))
                targetY = origin.y - shortestDistance - transform.position.y;
            if (float.IsNegativeInfinity(targetY)) targetY = rest.y;

            sampledWheelY[i] = targetY;
            sum += targetY;
            if (i % 2 == 0) left += targetY;
            else right += targetY;
        }

        float targetBodyY = sum / 6f - restWheelY;
        for (int i = 0; i < 6; i++)
            targetBodyY = Mathf.Max(targetBodyY,
                sampledWheelY[i] - restWheelY - suspensionTravel);
        float front = (sampledWheelY[0] + sampledWheelY[1]) * 0.5f;
        float rear = (sampledWheelY[4] + sampledWheelY[5]) * 0.5f;
        float axleSpan = restWheelPositions[4].z - restWheelPositions[0].z;
        float trackWidth = restWheelPositions[1].x - restWheelPositions[0].x;
        float targetPitch = Mathf.Clamp(Mathf.Atan2(front - rear, axleSpan) * Mathf.Rad2Deg,
            -maxTiltDegrees, maxTiltDegrees);
        float targetRoll = Mathf.Clamp(Mathf.Atan2(right - left, trackWidth * 3f) * Mathf.Rad2Deg,
            -maxTiltDegrees, maxTiltDegrees);

        if (deltaTime <= 0f)
        {
            bodyVisual.localPosition = new Vector3(0f, targetBodyY, 0f);
            pitch = targetPitch;
            roll = targetRoll;
        }
        else
        {
            float nextBodyY = Mathf.SmoothDamp(bodyVisual.localPosition.y, targetBodyY,
                ref bodyYVelocity, bodyResponse, Mathf.Infinity, deltaTime);
            bodyVisual.localPosition = new Vector3(0f, nextBodyY, 0f);
            pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity,
                bodyResponse, Mathf.Infinity, deltaTime);
            roll = Mathf.SmoothDampAngle(roll, targetRoll, ref rollVelocity,
                bodyResponse, Mathf.Infinity, deltaTime);
        }
        bodyVisual.localRotation = Quaternion.Euler(pitch, 0f, roll);

        spinDegrees += travel / wheelRadius * Mathf.Rad2Deg;
        for (int i = 0; i < 6; i++)
        {
            Vector3 rest = restWheelPositions[i];
            // Contact is authoritative in both directions. Smoothing the wheel
            // downward leaves it visibly floating over a receding slope.
            float contactY = sampledWheelY[i];
            float nextY = contactY;

            wheelPivots[i].localPosition = new Vector3(rest.x, nextY, rest.z);
            wheelPivots[i].localRotation = Quaternion.Euler(spinDegrees, 0f, 0f);

            int side = i % 2 == 0 ? -1 : 1;
            Vector3 lower = wheelPivots[i].position +
                transform.TransformVector(new Vector3(-side * 0.88f, 0.07f, 0f));
            if (shockMounts != null && shocks != null && shockFeet != null &&
                i < shockMounts.Length && i < shocks.Length && i < shockFeet.Length &&
                shockMounts[i] != null && shocks[i] != null && shockFeet[i] != null)
            {
                Vector3 upper = shockMounts[i].position;
                Vector3 extension = upper - lower;
                shocks[i].position = (upper + lower) * 0.5f;
                shocks[i].rotation = Quaternion.FromToRotation(Vector3.up, extension);
                shocks[i].localScale = new Vector3(0.13f, extension.magnitude * 0.5f, 0.13f);
                shockFeet[i].position = lower;
            }

            if (trailingArms != null && i < trailingArms.Length && trailingArms[i] != null)
            {
                float zOffset = i < 2 ? 0.38f : -0.38f;
                trailingArms[i].position = wheelPivots[i].position +
                    transform.TransformVector(new Vector3(-side * 0.78f, 0.12f, zOffset));
            }
        }

        for (int axle = 0; axle < 3; axle++)
        {
            if (axles == null || differentials == null ||
                axle >= axles.Length || axle >= differentials.Length ||
                axles[axle] == null || differentials[axle] == null) continue;

            Vector3 start = wheelPivots[axle * 2].position;
            Vector3 end = wheelPivots[axle * 2 + 1].position;
            Vector3 span = end - start;
            Vector3 midpoint = (start + end) * 0.5f;
            axles[axle].position = midpoint;
            axles[axle].rotation = Quaternion.FromToRotation(Vector3.up, span);
            axles[axle].localScale = new Vector3(0.16f, span.magnitude * 0.5f, 0.16f);
            differentials[axle].position = midpoint;
        }

        for (int i = 0; i < cargoLocalPositions.Length; i++)
        {
            if (cargoBoxes == null || i >= cargoBoxes.Length || cargoBoxes[i] == null) continue;
            cargoBoxes[i].position = bodyVisual.TransformPoint(cargoLocalPositions[i]);
            cargoBoxes[i].rotation = bodyVisual.rotation * cargoLocalRotations[i];
        }
    }
}
