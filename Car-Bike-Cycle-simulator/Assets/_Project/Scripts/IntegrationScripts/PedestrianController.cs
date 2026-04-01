using UnityEngine;

/// <summary>
/// Smoothly moves a pedestrian GameObject to SUMO-provided positions each simulation step.
/// Attach automatically by SimulationController when a pedestrian GameObject is instantiated.
/// Physics (gravity + collisions) is fully enabled.
/// </summary>
public class PedestrianController : MonoBehaviour
{
    private Rigidbody rb;

    private Vector3 lastPos;
    private Quaternion lastRot;
    private Vector3 curPos;
    private Quaternion curRot;
    private float lastTime;
    private float curTime;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity    = true;   // ✅ Physics enabled: pedestrians affected by gravity
        rb.isKinematic   = false;
        rb.linearDamping  = 2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;

        curPos  = lastPos  = transform.position;
        curRot  = lastRot  = transform.rotation;
        lastTime = curTime = Time.time;
    }

    /// <summary>
    /// Called by SimulationController each time new SUMO data arrives for this pedestrian.
    /// </summary>
    public void UpdateTarget(Vector3 pos, Quaternion rot)
    {
        lastPos  = curPos;
        lastRot  = curRot;
        lastTime = curTime;

        curPos  = pos;
        curRot  = rot;
        curTime = Time.time;
    }

    private void FixedUpdate()
    {
        float dt = curTime - lastTime;

        if (dt <= 0f)
        {
            rb.linearVelocity  = Vector3.zero;
            // Keep angular + rotation frozen, but let gravity act on y
            rb.MoveRotation(curRot);
            return;
        }

        // Drive horizontal velocity from SUMO positions (preserves y from physics/gravity)
        Vector3 desiredVelocity = (curPos - lastPos) / dt;
        rb.linearVelocity = new Vector3(desiredVelocity.x, rb.linearVelocity.y, desiredVelocity.z);

        // Smoothly blend heading
        Quaternion targetRot = Quaternion.Slerp(transform.rotation, curRot, 10f * Time.fixedDeltaTime);
        rb.MoveRotation(targetRot);

        // ✅ Fixed: use world-space position (transform.position) not local-space
        transform.position = Vector3.Lerp(transform.position, curPos, 0.05f);
    }
}
