using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

public class SimulationController : MonoBehaviour
{
    private ExchangeData _ExchangeData;
    private GameObject vehiclePrefab;
    private Dictionary<string, GameObject> vehicleObjects = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> pedestrianObjects = new Dictionary<string, GameObject>();
    private string vehicleDataJson = "{}";
    private object vehicleDataLock = new object();
    private string egoVehicleId = "f_0.0";
    public GameObject egoVehicle;
    private GameObject f_1_0;
    private Vector3 previousPosition;
    private Vector3 currentPosition;
    private float long_speed;
    private float distanceAccumulator = 0f;
    private float timeAccumulator = 0f;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    // Diagnostic: track which message types we've seen so we log each only once
    private readonly HashSet<string> _seenMsgTypes = new HashSet<string>();
    public Vector3 egoVehicleInitialPosition = new Vector3(0f, 0f, 0f);
    public Quaternion egoVehicleInitialRotation = Quaternion.Euler(0f, 90f, 0f);

    public string EgoVehicleId => egoVehicleId;

    public GameObject GetEgoVehicleObject()
    {
        if (vehicleObjects.TryGetValue(egoVehicleId, out var ego))
        {
            return ego;
        }

        return null;
    }

    private StreamWriter writer;

    [Header("Unity Step Length (seconds)")]
    public float unityStepLength = 0.10f;

    private float fixedTimeAccum = 0f; // Accumulator for FixedUpdate logging

    // New variables for timestamp offset
    private bool firstTimestampLogged = false;
    private float firstLoggedTime = 0f;

    // ‑‑‑‑ NEW: traffic‑light handling ‑‑‑‑
    [Header("Add all Junction GameObjects")]
    public GameObject junctions;           // drag ‘Junctions’ root here
    private readonly Dictionary<string, GameObject> junctionCache = new();

    [Serializable]
    public class Vehicle
    {
        public string vehicle_id;
        public double[] position;
        public double angle;
        public string type;
        public float long_speed;
        public float vert_speed;
        public float lat_speed;
    }

    [Serializable]
    public class Pedestrian
    {
        public string pedestrian_id;
        public double[] position;
        public double angle;
        public string type;
    }

    [Serializable]
    private class PedestrianWrapper
    {
        public Pedestrian[] pedestrians;
    }

    [Serializable]
    private class VehicleWrapper
    {
        public Vehicle[] vehicles;
    }

    [Serializable]
    public class TrafficLight
    {
        public string junction_id;
        public string state;
    }

    [Serializable]
    private class TrafficLightsWrapper
    {
        public TrafficLight[] lights;
    }

    [System.Serializable]
    public class CarModel
    {
        public string sumoVehicleType;
        public GameObject unityVehiclePrefab;
    }

    [System.Serializable]
    public class PedestrianModel
    {
        public string sumoPedestrianType;
        public GameObject unityPedestrianPrefab;
    }

    [Header("Add Unity Vehicle Prefab (3DModel) according to Sumo Vehicle Type")]
    public List<CarModel> carModelsList = new List<CarModel>();

    [Header("Add Unity Pedestrian Prefab according to Sumo Pedestrian Type")]
    public List<PedestrianModel> pedestrianModelsList = new List<PedestrianModel>();

    [Tooltip("Fallback pedestrian prefab used when no type match is found in the list above. Assign PedestrianCylinder (or any model) here.")]
    public GameObject defaultPedestrianPrefab;

    // ── new fields ─────────────────────────────────────────────
    /// last time we processed a TL message
    private float _lastTlTime = 0f;
    /// minimum seconds between TL updates
    private float tlUpdateInterval = 1f;

    /// cache last seen state per junction
    private Dictionary<string, string> _lastTlState = new();

    /// <summary>Finds (or creates) SUMO2Unity\SUMOData\Results next to the project.</summary>
    /// <summary>Finds (or creates) SUMO2Unity\Results next to the project.</summary>
    private static string LocateOrCreateResultsFolder()
    {
        // projectRoot = folder that *contains* "Assets"
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        DirectoryInfo dir = new DirectoryInfo(projectRoot);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Results");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;                       // walk upward
        }

        // Not found – create it next to the project
        string fallback = Path.Combine(projectRoot, "Results");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private void Start()
    {
        vehiclePrefab = Resources.Load("EloraGold") as GameObject;

        if (vehiclePrefab == null)
        {
            Debug.LogError("Vehicle prefab 'EloraGold' not found in Resources.");
            return;
        }

        // Warn if no fallback assigned
        if (defaultPedestrianPrefab == null)
            Debug.LogWarning("SimulationController: No defaultPedestrianPrefab assigned. Pedestrians without a type match will be skipped.");

        // Warn if any list entry is missing a prefab
        foreach (var pm in pedestrianModelsList)
        {
            if (pm.unityPedestrianPrefab == null)
                Debug.LogWarning($"PedestrianModel entry for type '{pm.sumoPedestrianType}' has no prefab assigned.");
        }

        _ExchangeData = GetComponent<ExchangeData>();
        if (_ExchangeData == null)
        {
            _ExchangeData = gameObject.AddComponent<ExchangeData>();
        }

        SumoRequesterStart();
        //StartCoroutine(FindGameObjectAfterDelay(1.0f));

        // 3) open log file in SUMOData folder
        string sumoDataDir = LocateOrCreateResultsFolder();
        string logPath = Path.Combine(sumoDataDir, "vehicle_data_report.txt");
        writer = new StreamWriter(logPath, append: false, Encoding.UTF8);
        writer.WriteLine("timestep_time;vehicle_id;vehicle_x;vehicle_y;vehicle_z");
    }

    public void SumoRequesterStart()
    {
        if (egoVehicle == null)
        {
            Debug.LogError("Ego vehicle GameObject is not assigned.");
            return;
        }

        Vector3 initialPosition = egoVehicleInitialPosition;
        Quaternion initialRotation = egoVehicleInitialRotation;
        egoVehicle = GameObject.Instantiate(egoVehicle, initialPosition, initialRotation);
        egoVehicle.name = egoVehicleId;
        vehicleObjects.Add(egoVehicleId, egoVehicle);
    }

    void Update()
    {
        try
        {
            string data = CollectVehicleData();
            lock (vehicleDataLock)
            {
                vehicleDataJson = data;
            }

            while (mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception in Update(): {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void FixedUpdate()
    {
        // Only log if we have started and not stopped recording
        if (!RecordingManager.startRecordingFromZero)
        {
            return;
        }

        fixedTimeAccum += Time.fixedDeltaTime;
        if (fixedTimeAccum >= unityStepLength - 0.002)
        {
            float currentTime = Time.fixedTime;

            // If this is the first timestamp we log, record it as the start
            if (!firstTimestampLogged)
            {
                firstLoggedTime = currentTime;
                firstTimestampLogged = true;
            }

            // Log time adjusted by first logged time
            float logTime = currentTime - firstLoggedTime;
            LogVehicleData(logTime);
            fixedTimeAccum = 0f;
        }

    }

    private void LogVehicleData(float relativeLogTime)
    {
        foreach (var kvp in vehicleObjects)
        {
            string vehicleId = kvp.Key;
            GameObject vehicleObj = kvp.Value;
            Vector3 pos = vehicleObj.transform.position;
            writer.WriteLine($"{relativeLogTime:F3};{vehicleId};{pos.x:F2};{pos.y:F2};{pos.z:F2}");
        }
    }

    private void OnDestroy()
    {
        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }
    }

    public void EnqueueMainThreadAction(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    public string CollectVehicleData()
    {
        if (!vehicleObjects.ContainsKey(egoVehicleId))
        {
            UnityEngine.Debug.LogWarning("Ego vehicle not found. Sending empty JSON.");
            return "{}";
        }

        GameObject egoVehicle = vehicleObjects[egoVehicleId];
        long_speed = egoVehicle.GetComponent<Rigidbody>().linearVelocity.magnitude;

        Vector3 position = egoVehicle.transform.position;
        float unroundangle = egoVehicle.transform.rotation.eulerAngles.y;
        double angle = Math.Round(unroundangle, 2);
        double x = Math.Round(position.x, 2);
        double y = Math.Round(position.z, 2);
        double z = Math.Round(position.y, 2);
        string type = "ego";

        float vertical_speed = (float)Math.Round(egoVehicle.GetComponent<Rigidbody>().linearVelocity.y, 2);
        float lateral_speed = (float)Math.Round(egoVehicle.GetComponent<Rigidbody>().linearVelocity.z, 2);

        Vehicle egoVehicleData = new Vehicle();
        egoVehicleData.vehicle_id = egoVehicleId;
        egoVehicleData.position = new double[] { x, y, z };
        egoVehicleData.angle = angle;
        egoVehicleData.type = type;
        egoVehicleData.long_speed = (float)Math.Round(long_speed, 2);
        egoVehicleData.vert_speed = vertical_speed;
        egoVehicleData.lat_speed = lateral_speed;

        string jsonData = JsonHelper.ToJson(new Vehicle[] { egoVehicleData });
        return jsonData;
    }

    public string GetVehicleDataJson()
    {
        lock (vehicleDataLock)
        {
            return vehicleDataJson;
        }
    }

    public void HandleMessage(string message)
    {
        CommonMessage common = JsonUtility.FromJson<CommonMessage>(message);

        if (common == null || string.IsNullOrEmpty(common.type))
        {
            // Log the raw message snippet to diagnose bad JSON
            Debug.LogError($"[SUMO] Bad/untyped message. Raw (first 300 chars): {message.Substring(0, Mathf.Min(300, message.Length))}");
            return;
        }

        // ── Diagnostic: log each new message type the first time we see it ──
        if (_seenMsgTypes.Add(common.type))
            Debug.Log($"[SUMO] First-seen message type: '{common.type}'");

        if (common.type == "command")
        {
            if (common.command == "START_RECORDING")
            {
                RecordingManager.startRecordingFromZero = true;
                RecordingManager.recordingStartTime = Time.time;
                Debug.Log("Received START_RECORDING command from SUMO. Starting logs from zero now.");

                // Reset offset logging variables when we start recording
                firstTimestampLogged = false;
                firstLoggedTime = 0f;
            }
            else if (common.command == "STOP_RECORDING")
            {
                // Stop recording
                RecordingManager.startRecordingFromZero = false;
                Debug.Log("Received STOP_RECORDING command from SUMO. Stopping logs.");

                // Remove all surrounding cars except ego
                var nonEgoKeys = vehicleObjects.Keys.Where(k => k != egoVehicleId).ToList();
                foreach (var vid in nonEgoKeys)
                {
                    GameObject obj = vehicleObjects[vid];
                    Destroy(obj);
                    vehicleObjects.Remove(vid);
                }

                // Remove all pedestrians
                foreach (var pid in pedestrianObjects.Keys.ToList())
                {
                    Destroy(pedestrianObjects[pid]);
                }
                pedestrianObjects.Clear();
            }

            return; // No further vehicle parsing needed
        }
        else if (common.type == "vehicles")
        {
            VehicleWrapper wrapper = JsonUtility.FromJson<VehicleWrapper>(message);
            Vehicle[] vehicleArray = wrapper.vehicles;
            List<Vehicle> vehiclesData = vehicleArray != null ? vehicleArray.ToList() : new List<Vehicle>();

            HashSet<string> incomingVehicleIds = new HashSet<string>(vehiclesData.Select(v => v.vehicle_id));
            var vehiclesToRemove = vehicleObjects.Keys.Where(id => !incomingVehicleIds.Contains(id) && id != egoVehicleId).ToList();

            foreach (var id in vehiclesToRemove)
            {
                GameObject vehicleToDestroy = vehicleObjects[id];
                GameObject.Destroy(vehicleToDestroy);
                vehicleObjects.Remove(id);
            }

            foreach (var vehicle in vehiclesData)
            {
                Vector3 newPosition = new Vector3((float)vehicle.position[0], (float)vehicle.position[2], (float)vehicle.position[1]);
                Quaternion newRotation = Quaternion.Euler(0, (float)vehicle.angle - 90f, 0);
                float vehicleSpeed = vehicle.long_speed;
                float vehiclevertical_speed = vehicle.vert_speed;
                float vehiclelateral_speed = vehicle.lat_speed;

                if (vehicle.vehicle_id == egoVehicleId)
                {
                    continue;
                }

                if (vehicleObjects.ContainsKey(vehicle.vehicle_id))
                {
                    GameObject existingVehicle = vehicleObjects[vehicle.vehicle_id];
                    VehicleController vehicleController = existingVehicle.GetComponent<VehicleController>();
                    if (vehicleController != null)
                    {
                        vehicleController.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    }
                }
                else
                {
                    GameObject prefabToInstantiate = vehiclePrefab;
                    foreach (CarModel carModel in carModelsList)
                    {
                        if (carModel.sumoVehicleType == vehicle.type)
                        {
                            prefabToInstantiate = carModel.unityVehiclePrefab;
                            break;
                        }
                    }

                    GameObject newVehicle = GameObject.Instantiate(prefabToInstantiate, newPosition, newRotation);
                    newVehicle.name = vehicle.vehicle_id;
                    VehicleController vc = newVehicle.GetComponent<VehicleController>();
                    if (vc == null)
                    {
                        vc = newVehicle.AddComponent<VehicleController>();
                    }

                    vc.UpdateTarget(newPosition, newRotation, vehicleSpeed, vehiclevertical_speed, vehiclelateral_speed);
                    vehicleObjects.Add(vehicle.vehicle_id, newVehicle);
                }
            }

        }
        else if (common.type == "pedestrians")
        {
            PedestrianWrapper wrapper = JsonUtility.FromJson<PedestrianWrapper>(message);
            Pedestrian[] pedArray = wrapper?.pedestrians;
            if (pedArray == null) return;

            // Remove pedestrians no longer present in SUMO
            HashSet<string> incomingPedIds = new HashSet<string>(pedArray.Select(p => p.pedestrian_id));
            var pedsToRemove = pedestrianObjects.Keys.Where(id => !incomingPedIds.Contains(id)).ToList();
            foreach (var pid in pedsToRemove)
            {
                Destroy(pedestrianObjects[pid]);
                pedestrianObjects.Remove(pid);
            }

            foreach (var ped in pedArray)
            {
                // SUMO coords → Unity: (x, height, z) → (x, z, y)
                Vector3 newPos = new Vector3(
                    (float)ped.position[0],
                    ped.position.Length > 2 ? (float)ped.position[2] : 0f,
                    (float)ped.position[1]);
                Quaternion newRot = Quaternion.Euler(0, (float)ped.angle - 90f, 0);

                if (pedestrianObjects.ContainsKey(ped.pedestrian_id))
                {
                    // Update existing pedestrian
                    GameObject existingPed = pedestrianObjects[ped.pedestrian_id];
                    PedestrianController pc = existingPed.GetComponent<PedestrianController>();
                    if (pc != null)
                    {
                        pc.UpdateTarget(newPos, newRot);
                    }
                }
                else
                {
                    // ── Pick prefab: match by type, or fall back to defaultPedestrianPrefab ──
                    GameObject prefabToUse = null;

                    // Log the exact type string SUMO sent (helps diagnose mismatches)
                    Debug.Log($"[PedSpawn] id={ped.pedestrian_id} type='{ped.type}' — looking for prefab match...");

                    foreach (var pm in pedestrianModelsList)
                    {
                        if (pm.sumoPedestrianType == ped.type && pm.unityPedestrianPrefab != null)
                        {
                            prefabToUse = pm.unityPedestrianPrefab;
                            break;
                        }
                    }

                    if (prefabToUse == null)
                    {
                        prefabToUse = defaultPedestrianPrefab;
                    }

                    // ── Fallback: create a primitive capsule so pedestrians are NEVER invisible ──
                    GameObject newPed;
                    if (prefabToUse == null)
                    {
                        Debug.LogWarning($"[PedSpawn] No prefab for type '{ped.type}'. " +
                            "Creating a primitive Capsule. Assign 'defaultPedestrianPrefab' or add a " +
                            $"pedestrianModelsList entry with sumoPedestrianType='{ped.type}' to use a real model.");

                        newPed = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        newPed.transform.position = newPos;
                        newPed.transform.rotation = newRot;
                        // Give the capsule a distinct colour so it's easy to spot
                        var rend = newPed.GetComponent<Renderer>();
                        if (rend != null)
                        {
                            rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            rend.material.color = new Color(1f, 0.4f, 0f); // orange
                        }
                    }
                    else
                    {
                        newPed = Instantiate(prefabToUse, newPos, newRot);
                    }

                    newPed.name = ped.pedestrian_id;

                    PedestrianController pc = newPed.GetComponent<PedestrianController>();
                    if (pc == null)
                    {
                        pc = newPed.AddComponent<PedestrianController>();
                    }
                    pc.UpdateTarget(newPos, newRot);

                    pedestrianObjects.Add(ped.pedestrian_id, newPed);
                    Debug.Log($"[PedSpawn] Spawned '{ped.pedestrian_id}' (type='{ped.type}') " +
                        $"prefab={(prefabToUse != null ? prefabToUse.name : "PrimitiveCapsule")} at {newPos}");
                }
            }
        }
        else if (common.type == "trafficlights")
        {
            // 2) parse wrapper
            var wrapper = JsonUtility.FromJson<TrafficLightsWrapper>(message);

            foreach (var tl in wrapper.lights)
            {
                // only repaint if state actually changed
                if (!_lastTlState.TryGetValue(tl.junction_id, out var prev)
                 || prev != tl.state)
                {
                    ChangeTrafficStatus(tl.junction_id, tl.state);
                    _lastTlState[tl.junction_id] = tl.state;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[SUMO] Unknown message type: '{common.type}'. " +
                $"Raw (first 300 chars): {message.Substring(0, Mathf.Min(300, message.Length))}");
        }
    }

    public void EnqueueOnMainThread(string message)
    {
        EnqueueMainThreadAction(() => HandleMessage(message));
    }

    private void ChangeTrafficStatus(string junctionID, string state)
    {
        // find & cache the J4 GameObject exactly as before
        if (!junctionCache.TryGetValue(junctionID, out GameObject junctionGO))
        {
            var t = junctions.transform.Find(junctionID);
            if (t == null) { Debug.LogWarning($"Junction {junctionID} not found"); return; }
            junctionGO = t.gameObject;
            junctionCache[junctionID] = junctionGO;
        }

        // now for each character in the state string
        for (int i = 0; i < state.Length; i++)
        {
            // look for the child named "Head0", "Head1", etc.
            var headTransform = junctionGO.transform.Find($"Head{i}");
            if (headTransform == null)
            {
                Debug.LogWarning($"  Head{i} not found under {junctionID}");
                continue;
            }
            SetSignalState(state[i], headTransform.gameObject);
        }
    }


    private void SetSignalState(char c, GameObject head)
    {
        // look for your three meshes under each head
        var green = FindChildRecursive(head.transform, "green_light");
        var yellow = FindChildRecursive(head.transform, "yellow_light");
        var red = FindChildRecursive(head.transform, "red_light");
        if (green) green.SetActive(c == 'G' || c == 'g');
        if (yellow) yellow.SetActive(c == 'y' || c == 'Y');
        if (red) red.SetActive(!(c == 'G' || c == 'g' || c == 'y' || c == 'Y'));
    }

    private GameObject FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child.gameObject;
            var found = FindChildRecursive(child, name);
            if (found) return found;
        }
        return null;
    }


    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            string newJson = "{ \"vehicles\": " + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.vehicles;
        }

        public static string ToJson<T>(T[] array)
        {
            Wrapper<T> wrapper = new Wrapper<T> { vehicles = array };
            return JsonUtility.ToJson(wrapper);
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] vehicles;
        }
    }
}