using UnityEngine;
using System.Collections.Generic;

public class ZMQClient : MonoBehaviour {

    private const string EgoPedestrianId = "ego_ped";

    private ZMQRequester zmqRequester;
    GameObject subject;
    [SerializeField] private Transform egoSourceTransform;
    [SerializeField] private bool preferMainCameraAsEgoSource = true;
    [SerializeField] private bool applyInteractionToEgoExport = true;
    [SerializeField] private float pedestrianRepulsionRadius = 1.8f;
    [SerializeField] private float pedestrianRepulsionStrength = 1.3f;
    [SerializeField] private float carRepulsionRadius = 3.5f;
    [SerializeField] private float carRepulsionStrength = 2.2f;
    private readonly int rayCastLayerMask = 1 << 9;
    List<ZMQRequester.Thing> previous_things;
    [SerializeField] private string subjectObjectName = "subject";
    [SerializeField] private string controlledPedestrianId = EgoPedestrianId;

    private static readonly string[] firstPersonRoots =
    {
        "VRTKScripts",
        "VRTKSDK",
        "[VRTK_SDKManager]",
        "Locomotion",
        "VRSimulator"
    };

    void Start () {
        controlledPedestrianId = EgoPedestrianId;
        EnsureFirstPersonRigActive();

        try
        {
            zmqRequester = new ZMQRequester();
            zmqRequester.Start();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to start ZMQ requester: " + ex.Message);
            enabled = false;
        }
	}

    private void EnsureFirstPersonRigActive()
    {
        foreach (string rootName in firstPersonRoots)
        {
            SetSceneObjectActive(rootName, true);
        }
    }

    private void SetSceneObjectActive(string objectName, bool active)
    {
        GameObject target = FindSceneObjectByName(objectName);
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private GameObject FindSceneObjectByName(string objectName)
    {
        GameObject direct = GameObject.Find(objectName);
        if (direct != null)
        {
            return direct;
        }

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject candidate in allObjects)
        {
            if (candidate == null || candidate.name != objectName)
            {
                continue;
            }

            if (candidate.scene.IsValid() && candidate.scene.isLoaded)
            {
                return candidate;
            }
        }

        return null;
    }
	
    void OnDestroy()
    {
        if (zmqRequester != null)
        {
            zmqRequester.Stop();
        }
    }

    private void Update()
    {
        if (zmqRequester == null)
        {
            return;
        }

        GameObject personsObject = GameObject.Find("Persons");
        if (personsObject == null)
        {
            return;
        }

        Transform persons = personsObject.transform;
        Transform subjectTransform = GetSubjectTransform();

        List<ZMQRequester.Thing> things = new List<ZMQRequester.Thing>();
        foreach(Transform t in persons)
        {
            if (t == null)
            {
                continue;
            }

            // Keep ego_ped reserved for the player-controlled subject.
            if (subjectTransform != null && t != subjectTransform && t.gameObject.name == controlledPedestrianId)
            {
                continue;
            }

            AddThingForTransform(things, t);
        }

        if (subjectTransform != null)
        {
            string subjectOutboundName = GetOutboundThingName(subjectTransform);
            bool alreadyIncluded = false;
            foreach (ZMQRequester.Thing thing in things)
            {
                if (thing.name == subjectOutboundName)
                {
                    alreadyIncluded = true;
                    break;
                }
            }

            if (!alreadyIncluded)
            {
                AddThingForTransform(things, subjectTransform);
            }
        }

        previous_things = things;
        zmqRequester.UpdatePersonsList(things);
    }

    private Transform GetSubjectTransform()
    {
        if (egoSourceTransform != null)
        {
            subject = egoSourceTransform.gameObject;
            return egoSourceTransform;
        }

        if (preferMainCameraAsEgoSource && Camera.main != null)
        {
            subject = Camera.main.gameObject;
            return Camera.main.transform;
        }

        SubjectController subjectController = FindObjectOfType<SubjectController>();
        if (subjectController != null)
        {
            subject = subjectController.gameObject;
            return subject.transform;
        }

        if (subject == null)
        {
            subject = GameObject.Find(subjectObjectName);
            if (subject == null)
            {
                subject = GameObject.Find(controlledPedestrianId);
            }
            if (subject == null)
            {
                subject = GameObject.Find("Subject");
            }
        }

        if (subject == null)
        {
            return null;
        }

        return subject.transform;
    }

    private string GetOutboundThingName(Transform t)
    {
        if (subject != null && t.gameObject == subject)
        {
            return controlledPedestrianId;
        }

        return t.gameObject.name;
    }

    private void AddThingForTransform(List<ZMQRequester.Thing> things, Transform t)
    {
        string outgoingName = GetOutboundThingName(t);
        Vector3 outboundPosition = GetOutboundPosition(t, outgoingName);

        RaycastHit hit;
        string hit_id = "";
        string hit_lane = "";
        bool hit_pedWalk = false;
        bool changedArea = false;

        if (Physics.Raycast(outboundPosition, Vector3.down, out hit, Mathf.Infinity, rayCastLayerMask))
        {
            GameObject hit_go = hit.collider.gameObject;
            NetworkData networkData = hit_go.GetComponent<NetworkData>();
            if (networkData != null)
            {
                hit_id = networkData.id;
                hit_pedWalk = networkData.pedWalk;
            }
            hit_lane = hit_go.name;
        }

        if (previous_things != null)
        {
            foreach (ZMQRequester.Thing old in previous_things)
            {
                if (old.name == outgoingName && old.edge != hit_id)
                {
                    changedArea = true;
                    break;
                }
            }
        }

        ZMQRequester.Thing th = new ZMQRequester.Thing(outgoingName, outboundPosition.x, outboundPosition.z, t.rotation.eulerAngles.y, hit_id, hit_lane, hit_pedWalk, changedArea);
        things.Add(th);
    }

    private Vector3 GetOutboundPosition(Transform sourceTransform, string outgoingName)
    {
        Vector3 outboundPosition = sourceTransform.position;

        if (!applyInteractionToEgoExport || outgoingName != controlledPedestrianId)
        {
            return outboundPosition;
        }

        Vector3 repulsion = Vector3.zero;
        GameObject personsObject = GameObject.Find("Persons");
        if (personsObject != null)
        {
            repulsion += ComputeRepulsionFromRoot(personsObject.transform, sourceTransform, outboundPosition, pedestrianRepulsionRadius, pedestrianRepulsionStrength);
        }

        GameObject carsObject = GameObject.Find("Cars");
        if (carsObject != null)
        {
            repulsion += ComputeRepulsionFromRoot(carsObject.transform, sourceTransform, outboundPosition, carRepulsionRadius, carRepulsionStrength);
        }

        repulsion.y = 0f;
        return outboundPosition + repulsion * Time.deltaTime;
    }

    private Vector3 ComputeRepulsionFromRoot(Transform root, Transform sourceTransform, Vector3 position, float radius, float strength)
    {
        if (root == null || radius <= 0f || strength <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 totalRepulsion = Vector3.zero;
        float radiusSqr = radius * radius;

        foreach (Transform child in root)
        {
            if (child == null || child == sourceTransform)
            {
                continue;
            }

            Vector3 offset = position - child.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr < 0.0001f || distanceSqr > radiusSqr)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSqr);
            float proximity = 1f - (distance / radius);
            totalRepulsion += offset.normalized * (proximity * strength);
        }

        return totalRepulsion;
    }

    private void Update2()
    {
        subject = GameObject.Find("subject");

        if (subject == null)
            return;

        RaycastHit hit;
        string hit_id = "";
        string hit_lane = "";
        bool hit_pedWalk = false;

        if (Physics.Raycast(subject.transform.position, subject.transform.TransformDirection(Vector3.down), out hit, Mathf.Infinity, rayCastLayerMask))
        {
            GameObject hit_go = hit.collider.gameObject;
            hit_id = hit_go.GetComponent<NetworkData>().id;
            hit_lane = hit_go.name;
            hit_pedWalk = hit_go.GetComponent<NetworkData>().pedWalk;
        }
        
        zmqRequester.UpdateSubject(subject.transform.position.x, subject.transform.position.z, subject.transform.rotation.eulerAngles.y, hit_id, hit_lane, hit_pedWalk);
    }
}
