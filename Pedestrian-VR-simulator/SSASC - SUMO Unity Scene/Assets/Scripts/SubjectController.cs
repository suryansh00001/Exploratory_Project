using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRTK;

public class SubjectController : MonoBehaviour
{
    private const string EgoPedestrianId = "ego_ped";

    [Header("Keyboard fallback (non-VR)")]
    public bool allowKeyboardFallback = false;
    public float moveSpeed = 2.5f;
    public float turnSpeed = 120f;
    public float eyeHeight = 1.55f;
    public string controlledPedestrianId = EgoPedestrianId;

    [Header("Interaction")]
    public bool enableInteractionForces = true;
    public float pedestrianRepulsionRadius = 1.8f;
    public float pedestrianRepulsionStrength = 1.3f;
    public float carRepulsionRadius = 3.5f;
    public float carRepulsionStrength = 2.2f;

    private float keyboardYaw;
    private Camera fallbackCamera;
    private Transform personsRoot;
    private Transform carsRoot;

    // Start is called before the first frame update
    void Start()
    {
        controlledPedestrianId = EgoPedestrianId;
        gameObject.name = controlledPedestrianId;

        keyboardYaw = transform.eulerAngles.y;
        if (allowKeyboardFallback)
        {
            fallbackCamera = ResolveOrCreateFallbackCamera();
        }

        Transform avatar = gameObject.transform.Find("lpMaleG");
        if (avatar != null)
        {
            avatar.gameObject.layer = 8;
        }

        GameObject rightHand = GameObject.Find("RightHand");
        if (rightHand != null)
        {
            rightHand.SetActive(false);
        }

        GameObject leftHand = GameObject.Find("LeftHand");
        if (leftHand != null)
        {
            leftHand.SetActive(false);
        }
    }

    // Update is called once per frame
    void Update()
    {
        GameObject headset = GetTrackedHeadset();
        if (headset != null)
        {
            Vector3 desiredPosition = new Vector3(headset.transform.position.x, transform.position.y, headset.transform.position.z);
            transform.position = ApplyInteractionRepulsion(desiredPosition, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, headset.transform.rotation.eulerAngles.y, 0);
            return;
        }

        if (!allowKeyboardFallback)
        {
            return;
        }

        HandleKeyboardMovement();
        UpdateFallbackCamera();
    }

    private GameObject GetTrackedHeadset()
    {
        if (VRTK_SDKManager.GetLoadedSDKSetup() == null)
        {
            return null;
        }

        GameObject headset = VRTK_SDKManager.GetLoadedSDKSetup().actualHeadset;
        if (headset == null || !headset.activeInHierarchy)
        {
            return null;
        }

        return headset;
    }

    private void HandleKeyboardMovement()
    {
        float turnInput = 0f;

        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow))
        {
            turnInput = -1f;
        }
        else if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightArrow))
        {
            turnInput = 1f;
        }

        keyboardYaw += turnInput * turnSpeed * Time.deltaTime;
        Quaternion yawRotation = Quaternion.Euler(0f, keyboardYaw, 0f);

        float forwardAxis = Input.GetAxisRaw("Vertical");
        float strafeAxis = Input.GetAxisRaw("Horizontal");

        Vector3 moveDirection = (yawRotation * Vector3.forward * forwardAxis) + (yawRotation * Vector3.right * strafeAxis);
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        Vector3 desiredPosition = transform.position + moveDirection * moveSpeed * Time.deltaTime;
        transform.position = ApplyInteractionRepulsion(desiredPosition, Time.deltaTime);
        transform.rotation = yawRotation;
    }

    private Vector3 ApplyInteractionRepulsion(Vector3 desiredPosition, float deltaTime)
    {
        if (!enableInteractionForces)
        {
            return desiredPosition;
        }

        EnsureInteractionRoots();

        Vector3 repulsion = Vector3.zero;
        repulsion += ComputeRepulsionFromRoot(personsRoot, desiredPosition, pedestrianRepulsionRadius, pedestrianRepulsionStrength);
        repulsion += ComputeRepulsionFromRoot(carsRoot, desiredPosition, carRepulsionRadius, carRepulsionStrength);
        repulsion.y = 0f;

        return desiredPosition + repulsion * deltaTime;
    }

    private void EnsureInteractionRoots()
    {
        if (personsRoot == null)
        {
            GameObject persons = GameObject.Find("Persons");
            if (persons != null)
            {
                personsRoot = persons.transform;
            }
        }

        if (carsRoot == null)
        {
            GameObject cars = GameObject.Find("Cars");
            if (cars != null)
            {
                carsRoot = cars.transform;
            }
        }
    }

    private Vector3 ComputeRepulsionFromRoot(Transform root, Vector3 desiredPosition, float radius, float strength)
    {
        if (root == null || radius <= 0f || strength <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 totalRepulsion = Vector3.zero;
        float radiusSqr = radius * radius;

        foreach (Transform child in root)
        {
            if (child == null || child == transform)
            {
                continue;
            }

            Vector3 offset = desiredPosition - child.position;
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

    private void UpdateFallbackCamera()
    {
        if (fallbackCamera == null)
        {
            fallbackCamera = ResolveOrCreateFallbackCamera();
        }

        if (fallbackCamera == null)
        {
            return;
        }

        Transform cameraTransform = fallbackCamera.transform;
        cameraTransform.position = transform.position + Vector3.up * eyeHeight;
        cameraTransform.rotation = Quaternion.Euler(0f, keyboardYaw, 0f);

        if (!fallbackCamera.gameObject.activeSelf)
        {
            fallbackCamera.gameObject.SetActive(true);
        }
        fallbackCamera.tag = "MainCamera";
    }

    private Camera ResolveOrCreateFallbackCamera()
    {
        Camera main = Camera.main;
        if (main != null)
        {
            return main;
        }

        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allCameras.Length; i++)
        {
            if (allCameras[i] != null)
            {
                return allCameras[i];
            }
        }

        GameObject cameraObject = new GameObject("DesktopFallbackCamera");
        Camera createdCamera = cameraObject.AddComponent<Camera>();
        if (FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
        {
            cameraObject.AddComponent<AudioListener>();
        }
        createdCamera.clearFlags = CameraClearFlags.Skybox;
        createdCamera.nearClipPlane = 0.05f;
        createdCamera.farClipPlane = 1000f;
        createdCamera.fieldOfView = 60f;
        cameraObject.tag = "MainCamera";
        return createdCamera;
    }

}
