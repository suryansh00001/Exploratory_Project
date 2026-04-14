using AsyncIO;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using System.Collections;

public class ZMQRequester : RunableThread {

    private Thing subject = new Thing("ego_ped", 0, 0, 0, "", "", false);
    private GameObject person_prefab;
    private GameObject car_prefab;
    private GameObject persons;
    private GameObject cars;
    private GameObject egoPed;

    private readonly Dictionary<string, Vector3> lastCarSyncPosition = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> carPhysicsUntil = new Dictionary<string, float>();
    private const float CarCollisionDistancePadding = 0.2f;
    private const float CarPhysicsHandoffSeconds = 0.8f;
    private const float EgoImpulseStrength = 3.5f;

    private List<Thing> personsList = new List<Thing>();
    private PersonsData personsData = new PersonsData();
    private readonly System.Diagnostics.Stopwatch receiveStopwatch = System.Diagnostics.Stopwatch.StartNew();
    private long lastReceiveSummaryLogMs;
    private int receivedMessageCount;

    public bool LogReceivedPayloads { get; set; }
    public int PayloadLogEveryNMessages { get; set; } = 300;
    public bool LogReceiveTrafficSummary { get; set; } = true;
    public float ReceiveTrafficSummaryIntervalSeconds { get; set; } = 3f;
    public bool LogParsedDataObject { get; set; }

    [Serializable]
    public  class Thing
    {
        public string name;
        public double x;
        public double y;
        public double angle;
        public string edge;
        public string lane;
        public bool pedWalk;
        public bool changedArea;

        public Thing(string name, double x, double y, double angle, string edge, string lane, bool pedWalk, bool changedArea = false)
        {
            this.name = name;
            this.x = x;
            this.y = y;
            this.angle = angle;
            this.edge = edge;
            this.lane = lane;
            this.pedWalk = pedWalk;
            this.changedArea = changedArea;
        }
    }

    [Serializable]
    public class Data
    {
        public Thing[] persons;
        public Thing[] vehicles;
    }

    [Serializable]
    public class PersonsData
    {
        public Thing[] persons;
    }

    public void UpdateSubject(double x, double y, double angle, string edge, string lane, bool pedWalk)
    {
        subject.x = x;
        subject.y = y;
        subject.angle = angle;
        subject.edge = edge;
        subject.lane = lane;
        subject.pedWalk = pedWalk;
    }

    public void UpdatePersonsList(List<Thing> things)
    {
        personsList = things;
        personsData.persons = things.ToArray();
    }

    private List<string> getNamesOfThings()
    {
        List<string> ret = new List<string>();

        if (cars == null)
        {
            cars = GameObject.Find("Cars");
        }

        if (cars == null)
        {
            return ret;
        }

        //foreach (Transform child in persons.transform)
            //ret.Add(child.name);

        foreach (Transform child in cars.transform)
            ret.Add(child.name);

        return ret;
    }

    private IEnumerator HandleMessage(string message)
    {
        if (cars == null)
        {
            cars = GameObject.Find("Cars");
        }
        if (persons == null)
        {
            persons = GameObject.Find("Persons");
        }
        if (car_prefab == null)
        {
            car_prefab = Resources.Load("Car_v2") as GameObject;
        }
        if (person_prefab == null)
        {
            person_prefab = Resources.Load("Person_v4") as GameObject;
        }
        if (egoPed == null)
        {
            egoPed = GameObject.Find("ego_ped");
        }

        Data data = JsonUtility.FromJson<Data>(message);
        if (data == null)
        {
            yield break;
        }
        if (LogParsedDataObject)
        {
            UnityEngine.Debug.Log(data);
        }

        List<string> things = getNamesOfThings();

        /*
        foreach (Thing p in data.persons)
        {
            GameObject go = GameObject.Find(p.name);
            SubjectController sc;

            if (go == null)
            {
                go = GameObject.Instantiate(person_prefab, new Vector3((float)p.x, 0.09f, (float)p.y), Quaternion.identity, persons.transform);
                go.name = p.name;

                if(p.name == "subject")
                {
                    //go.AddComponent(typeof(SubjectController));
                }
            }
            else if (p.name != "subject")
            {
                go.transform.position = new Vector3((float)p.x, 0.09f, (float)p.y);
                go.transform.rotation = Quaternion.Euler(0, (float)p.angle, 0);
            }

            things.Remove(p.name);
        }
        */
        

        foreach (Thing p in data.vehicles)
        {
            GameObject go = GameObject.Find(p.name);

            if (go == null)
            {
                if (car_prefab == null || cars == null)
                {
                    continue;
                }

                go = GameObject.Instantiate(car_prefab, new Vector3(0, 0, 0), Quaternion.identity, cars.transform);
                go.name = p.name;
            }

            EnsureCarPhysics(go);

            Vector3 syncPosition = new Vector3((float)p.x, 0f, (float)p.y);
            bool inPhysicsHandoff = carPhysicsUntil.ContainsKey(p.name) && carPhysicsUntil[p.name] > Time.time;

            if (!inPhysicsHandoff)
            {
                go.transform.position = syncPosition;
                go.transform.rotation = Quaternion.Euler(0, (float)p.angle, 0);
            }

            HandleCarEgoCollision(go, p.name, syncPosition);

            lastCarSyncPosition[p.name] = syncPosition;

            things.Remove(p.name);
        }

        foreach (string name in things)
        {
            lastCarSyncPosition.Remove(name);
            carPhysicsUntil.Remove(name);
            GameObject.Destroy(GameObject.Find(name));
        }

        yield return null;
    }

    private void EnsureCarPhysics(GameObject car)
    {
        if (car == null)
        {
            return;
        }

        Rigidbody rb = car.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = car.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (!carPhysicsUntil.ContainsKey(car.name) || carPhysicsUntil[car.name] <= Time.time)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Collider col = car.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider box = car.AddComponent<BoxCollider>();
            box.isTrigger = false;
        }
    }

    private void EnsureEgoPhysics()
    {
        if (egoPed == null)
        {
            return;
        }

        Collider col = egoPed.GetComponent<Collider>();
        if (col == null)
        {
            CapsuleCollider capsule = egoPed.AddComponent<CapsuleCollider>();
            capsule.height = 1.75f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
        }

        Rigidbody rb = egoPed.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = egoPed.AddComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void HandleCarEgoCollision(GameObject car, string carName, Vector3 syncPosition)
    {
        if (car == null)
        {
            return;
        }

        if (egoPed == null)
        {
            egoPed = GameObject.Find("ego_ped");
            if (egoPed == null)
            {
                return;
            }
        }

        EnsureEgoPhysics();

        float distance = Vector3.Distance(new Vector3(car.transform.position.x, 0f, car.transform.position.z), new Vector3(egoPed.transform.position.x, 0f, egoPed.transform.position.z));

        float collisionDistance = 1.0f + CarCollisionDistancePadding;
        Collider carCollider = car.GetComponent<Collider>();
        Collider egoCollider = egoPed.GetComponent<Collider>();
        if (carCollider != null && egoCollider != null)
        {
            float carRadius = Mathf.Max(carCollider.bounds.extents.x, carCollider.bounds.extents.z);
            float egoRadius = Mathf.Max(egoCollider.bounds.extents.x, egoCollider.bounds.extents.z);
            collisionDistance = carRadius + egoRadius + CarCollisionDistancePadding;
        }

        if (distance > collisionDistance)
        {
            if (carPhysicsUntil.ContainsKey(carName) && carPhysicsUntil[carName] <= Time.time)
            {
                Rigidbody rb = car.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            return;
        }

        Rigidbody carRb = car.GetComponent<Rigidbody>();
        if (carRb == null)
        {
            return;
        }

        carRb.isKinematic = false;
        carRb.useGravity = false;

        Vector3 lastPos;
        if (lastCarSyncPosition.TryGetValue(carName, out lastPos))
        {
            float dt = Mathf.Max(Time.deltaTime, 0.01f);
            Vector3 estimatedVelocity = (syncPosition - lastPos) / dt;
            estimatedVelocity.y = 0f;
            carRb.linearVelocity = estimatedVelocity;
        }

        carPhysicsUntil[carName] = Time.time + CarPhysicsHandoffSeconds;

        Rigidbody egoRb = egoPed.GetComponent<Rigidbody>();
        if (egoRb != null)
        {
            Vector3 push = egoPed.transform.position - car.transform.position;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f)
            {
                push = -car.transform.forward;
            }
            egoRb.AddForce(push.normalized * EgoImpulseStrength, ForceMode.VelocityChange);
        }
    }

    protected override void Run()
    {
        try
        {
            ForceDotNet.Force();
            using (RequestSocket client = new RequestSocket())
            {
                client.Connect("tcp://localhost:5555");

                while (running)
                {
                    //Debug.Log("Sending Hello");
                    //client.SendFrame("subject: " + subject.x + "; " + subject.y);
                    //Debug.Log("Sent " + JsonUtility.ToJson(personsData));
                    client.SendFrame(JsonUtility.ToJson(personsData));
                    

                    string message = null;
                    bool gotMessage = false;
                    while (running)
                    {
                        gotMessage = client.TryReceiveFrameString(out message);
                        if (gotMessage) break;
                    }

                    if (gotMessage)
                    {
                        LogReceiveDiagnostics(message);
                        UnityMainThreadDispatcher.Instance().Enqueue(HandleMessage(message));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ZMQ requester stopped: " + ex.Message);
        }

        NetMQConfig.Cleanup();
    }

    private void LogReceiveDiagnostics(string message)
    {
        receivedMessageCount++;

        int payloadLogEvery = Math.Max(1, PayloadLogEveryNMessages);
        if (LogReceivedPayloads && (receivedMessageCount % payloadLogEvery == 0))
        {
            UnityEngine.Debug.Log("[ZMQ] Received #" + receivedMessageCount + " bytes=" + (message != null ? message.Length : 0) + " payload=" + message);
        }

        if (!LogReceiveTrafficSummary)
        {
            return;
        }

        float intervalSeconds = Mathf.Max(0.5f, ReceiveTrafficSummaryIntervalSeconds);
        long elapsedMs = receiveStopwatch.ElapsedMilliseconds;
        if (elapsedMs - lastReceiveSummaryLogMs >= (long)(intervalSeconds * 1000f))
        {
            UnityEngine.Debug.Log("[ZMQ] Receive traffic messages=" + receivedMessageCount + " lastBytes=" + (message != null ? message.Length : 0));
            lastReceiveSummaryLogMs = elapsedMs;
        }
    }

    protected /*override*/ void Run2()
    {
        ForceDotNet.Force(); 
        using (RequestSocket client = new RequestSocket())
        {
            client.Connect("tcp://localhost:5555");

            while (running)
            {
                //Debug.Log("Sending Hello");
                //client.SendFrame("subject: " + subject.x + "; " + subject.y);
                client.SendFrame(JsonUtility.ToJson(subject));
                Debug.Log("Sent " + JsonUtility.ToJson(subject));

                string message = null;
                bool gotMessage = false;
                while (running)
                {
                    gotMessage = client.TryReceiveFrameString(out message);
                    if (gotMessage) break;
                }

                if (gotMessage)
                {
                    Debug.Log("Received " + message);
                    UnityMainThreadDispatcher.Instance().Enqueue(HandleMessage(message));
                }
            }

        }

        NetMQConfig.Cleanup();
    }
}
