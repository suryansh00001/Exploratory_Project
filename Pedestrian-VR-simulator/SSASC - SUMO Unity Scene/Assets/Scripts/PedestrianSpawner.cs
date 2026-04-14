using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PedestrianSpawner : MonoBehaviour
{
    public GameObject[] spawnSurfaces;
    public int startPedestrians;
    public int pedestriansPerMinute;

    private float nextActionTime = 0.0f;
    private float period;
    // Reserve ego_ped for the user-controlled subject.
    private int pedestrianCount = 0;

    private GameObject person_prefab;
    private GameObject persons;

    private ForcesVariables vars;


    // Start is called before the first frame update
    void Start()
    {
        person_prefab = Resources.Load("Person_v4") as GameObject;
        persons = GameObject.Find("Persons");

        if (person_prefab == null || persons == null)
        {
            Debug.LogError("PedestrianSpawner is missing Person_v4 prefab or Persons root object.");
            enabled = false;
            return;
        }

        vars = persons.gameObject.GetComponent<ForcesVariables>();
        if (vars == null)
        {
            Debug.LogWarning("ForcesVariables not found on Persons. Using default spawn wall distance.");
        }

        period = pedestriansPerMinute > 0 ? 60f / pedestriansPerMinute : float.MaxValue;

        for (int i = 0; i < startPedestrians; i++)
            SpawnNewPedestrian();
    }

    // Update is called once per frame
    void Update()
    {
        if (person_prefab == null || persons == null || spawnSurfaces == null || spawnSurfaces.Length == 0)
        {
            return;
        }

        if (Time.time > nextActionTime)
        {
            nextActionTime += period;
            SpawnNewPedestrian();
        }
    }

    private void SpawnNewPedestrian()
    {

        Vector3 spawnPoint = getSpawnEndPoint();

        GameObject pedestrian = GameObject.Instantiate(person_prefab, spawnPoint, Quaternion.identity, persons.transform);
        pedestrian.name = "ped_" + pedestrianCount;
        PedestrianController pc = pedestrian.AddComponent<PedestrianController>();
        pc.startPos = spawnPoint;
        pc.endPos = getSpawnEndPoint();

        pedestrianCount++;
    }

    public Vector3 getSpawnEndPoint()
    {
        if (spawnSurfaces == null || spawnSurfaces.Length == 0)
        {
            return Vector3.zero;
        }

        int surfaceIndex = Random.Range(0, spawnSurfaces.Length);
        GameObject chosenOne = spawnSurfaces[surfaceIndex];
        if (chosenOne == null || chosenOne.GetComponent<Collider>() == null)
        {
            return Vector3.zero;
        }

        return SpawnPoint(chosenOne.GetComponent<Collider>().bounds);
    }

    public Vector3 SpawnPoint(Bounds bounds)
    {
        float minDistance = vars != null ? vars.min_distance_to_wall : 0.25f;
        return new Vector3(
            Random.Range(bounds.min.x + minDistance, bounds.max.x - minDistance),
            0.09f,
            Random.Range(bounds.min.z + minDistance, bounds.max.z - minDistance)
        );
    }
}
