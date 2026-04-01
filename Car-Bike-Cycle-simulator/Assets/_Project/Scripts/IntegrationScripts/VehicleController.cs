using UnityEngine;

public class VehicleController : MonoBehaviour
{
    private Rigidbody rb;
    private SimulationController simController;
    private GameObject egoVehicle;
    private Rigidbody egoRb;

    private Vector3 lastPos;
    private Quaternion lastRot;
    private Vector3 curPos;
    private Quaternion curRot;
    private float lastTime;
    private float curTime;

    private float curLong, curVert, curLat;
    // set at runtime, after the Inspector value is known
    private float stepLen;
    private float turnThresholdDeg;

    [Header("Ego Interaction")]
    [SerializeField] private bool enableEgoInteraction = true;
    [SerializeField] private float laneInfluenceWidth = 2.2f;
    [SerializeField] private float standstillGap = 2.0f;
    [SerializeField] private float reactionTime = 0.35f;
    [SerializeField] private float maxDeceleration = 8.0f;
    [SerializeField] private float unavoidableCollisionBrakeFactor = 0.3f;
    [SerializeField] private bool stopAfterEgoCollision = true;
    [SerializeField] private float postCrashDamping = 8.0f;

    private bool hasCrashed;

    private const float FadeTime = 0.05f;          // how long to ease out spin

    private Vector3 residualAngularVel;           // ★ keeps turn’s leftover spin
    private float residualTimer;                // ★ fade-out countdown

    private void Start()
    {
        rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
        EnsureCollider();

        rb.isKinematic = false;
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        curPos = lastPos = transform.position;
        curRot = lastRot = transform.rotation;
        lastTime = curTime = Time.time;

        TryResolveEgo();
    }

    public void UpdateTarget(Vector3 pos, Quaternion rot,
                             float longSpd, float vertSpd, float latSpd)
    {
        if (hasCrashed)
        {
            return;
        }

        lastPos = curPos; lastRot = curRot; lastTime = curTime;
        curPos = pos; curRot = rot; curTime = Time.time;

        curLong = longSpd; curVert = vertSpd; curLat = latSpd;
    }
    void Awake()
    {
        // Look for the first SimulationController in the scene
        simController = FindObjectOfType<SimulationController>();

        if (simController == null)
        {
            Debug.LogError("SimulationController not found!");
            return;
        }

        stepLen = simController.unityStepLength;                 // ← value set in Inspector
        turnThresholdDeg = Mathf.Clamp(stepLen * 40f, 0.25f, 10f);
    }
    private void FixedUpdate()
    {
        if (hasCrashed)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, 0.35f);
            rb.angularVelocity = Vector3.zero;
            return;
        }

        float dt = curTime - lastTime;
        if (dt <= 0f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(curRot);
            return;
        }

        float headingDelta = Quaternion.Angle(lastRot, curRot);   // degrees

        Vector3 plannedLinearVelocity;

        if (headingDelta < turnThresholdDeg)                      // ─ straight
        {
            /* linear vel from local-axis speeds (ultra smooth) */
            Vector3 vLong = curRot * (Vector3.right * curLong);
            Vector3 vLat = curRot * (Vector3.forward * curLat);
            Vector3 vUp = Vector3.up * curVert;
            plannedLinearVelocity = vLong + vLat + vUp;

            /* ① damp residual spin, don’t kill instantly */
            if (residualTimer > 0f)
            {
                residualTimer -= Time.fixedDeltaTime;
                float k = Mathf.Clamp01(residualTimer / FadeTime);
                rb.angularVelocity = residualAngularVel * k;
                if (k <= 0f) rb.MoveRotation(curRot);            // fully aligned
            }
            else
            {
                rb.angularVelocity = Vector3.zero;
                rb.MoveRotation(curRot);
            }
        }
        else                                                      // ─ turning
        {
            plannedLinearVelocity = (curPos - lastPos) / dt;
            residualAngularVel = CalcAngularVel(lastRot, curRot, dt); // ★ store
            rb.angularVelocity = residualAngularVel;
            residualTimer = FadeTime;                          // ★ reset
        }

        rb.linearVelocity = ApplyEgoInteraction(plannedLinearVelocity, Time.fixedDeltaTime);

        /* original ultra-smooth positional blend */
        transform.localPosition =
            Vector3.Lerp(transform.localPosition, curPos, 0.02f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!stopAfterEgoCollision || hasCrashed)
        {
            return;
        }

        if (IsCollisionWithEgo(collision))
        {
            EnterCrashedState();
        }
    }

    private Vector3 ApplyEgoInteraction(Vector3 desiredVelocity, float fixedDt)
    {
        if (!enableEgoInteraction || simController == null)
        {
            return desiredVelocity;
        }

        if (!TryResolveEgo())
        {
            return desiredVelocity;
        }

        Vector3 myForward = (curRot * Vector3.right).normalized;
        Vector3 toEgo = egoVehicle.transform.position - transform.position;

        float longitudinal = Vector3.Dot(toEgo, myForward);
        if (longitudinal <= 0f)
        {
            return desiredVelocity;
        }

        Vector3 lateralVec = toEgo - myForward * longitudinal;
        lateralVec.y = 0f;
        if (lateralVec.magnitude > laneInfluenceWidth)
        {
            return desiredVelocity;
        }

        float desiredAlong = Vector3.Dot(desiredVelocity, myForward);
        float egoAlong = Vector3.Dot(egoRb != null ? egoRb.linearVelocity : Vector3.zero, myForward);
        float closingSpeed = Mathf.Max(0f, desiredAlong - egoAlong);

        float availableDistance = longitudinal - standstillGap;
        if (availableDistance <= 0f)
        {
            return desiredVelocity;
        }

        float brakingDistance = (closingSpeed * closingSpeed) / (2f * Mathf.Max(maxDeceleration, 0.1f));
        float reactionDistance = closingSpeed * Mathf.Max(reactionTime, 0f);
        bool canStopInTime = (brakingDistance + reactionDistance) <= availableDistance;

        float adjustedAlong = desiredAlong;

        if (canStopInTime)
        {
            float safeRelativeSpeed = Mathf.Sqrt(Mathf.Max(0f, 2f * Mathf.Max(maxDeceleration, 0.1f) * availableDistance));
            float cappedAlong = Mathf.Min(desiredAlong, egoAlong + safeRelativeSpeed);
            float maxDelta = Mathf.Max(maxDeceleration, 0.1f) * Mathf.Max(fixedDt, 0.001f);
            adjustedAlong = Mathf.MoveTowards(desiredAlong, cappedAlong, maxDelta);
        }
        else
        {
            float emergencyBrake = Mathf.Max(maxDeceleration, 0.1f) * Mathf.Clamp01(unavoidableCollisionBrakeFactor);
            adjustedAlong = desiredAlong - emergencyBrake * Mathf.Max(fixedDt, 0.001f);
        }

        Vector3 lateralAndVertical = desiredVelocity - myForward * desiredAlong;
        return myForward * adjustedAlong + lateralAndVertical;
    }

    private bool TryResolveEgo()
    {
        if (egoVehicle != null)
        {
            if (egoRb == null)
            {
                egoRb = egoVehicle.GetComponent<Rigidbody>();
            }
            return true;
        }

        if (simController != null)
        {
            egoVehicle = simController.GetEgoVehicleObject();
            if (egoVehicle == null && !string.IsNullOrEmpty(simController.EgoVehicleId))
            {
                egoVehicle = GameObject.Find(simController.EgoVehicleId);
            }
        }

        if (egoVehicle == null)
        {
            return false;
        }

        egoRb = egoVehicle.GetComponent<Rigidbody>();
        return egoRb != null;
    }

    private bool IsCollisionWithEgo(Collision collision)
    {
        if (!TryResolveEgo() || collision == null)
        {
            return false;
        }

        Transform other = collision.transform;
        return other == egoVehicle.transform || other.IsChildOf(egoVehicle.transform) || egoVehicle.transform.IsChildOf(other);
    }

    private void EnterCrashedState()
    {
        hasCrashed = true;

        curLong = 0f;
        curVert = 0f;
        curLat = 0f;

        curPos = transform.position;
        lastPos = curPos;
        curRot = transform.rotation;
        lastRot = curRot;
        lastTime = Time.time;
        curTime = lastTime;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.linearDamping = Mathf.Max(postCrashDamping, rb.linearDamping);
    }

    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        var box = gameObject.AddComponent<BoxCollider>();
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            worldBounds.Encapsulate(renderers[i].bounds);
        }

        box.center = transform.InverseTransformPoint(worldBounds.center);
        Vector3 localSize = transform.InverseTransformVector(worldBounds.size);
        box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
    }

    private static Vector3 CalcAngularVel(Quaternion from, Quaternion to, float dt)
    {
        Quaternion dq = to * Quaternion.Inverse(from);
        dq.ToAngleAxis(out float angDeg, out Vector3 axis);
        if (angDeg > 180f) angDeg -= 360f;
        return axis.normalized * Mathf.Deg2Rad * angDeg / Mathf.Max(dt, 0.0001f);
    }
}
