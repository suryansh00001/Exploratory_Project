using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.XR;
using UnityEngine.SpatialTracking;
using VRTK;

public class ZMQClient : MonoBehaviour {

    private const string EgoPedestrianId = "ego_ped";

    private ZMQRequester zmqRequester;
    GameObject subject;
    [SerializeField] private Transform egoSourceTransform;
    [SerializeField] private bool preferMainCameraAsEgoSource = true;
    [SerializeField] private bool forceSteamVRAsDefault = true;
    [SerializeField] private int steamVRWarmupFrames = 12;
    [SerializeField] private bool logVRReadability = true;
    [SerializeField] private int vrDiagnosticFrames = 24;
    [SerializeField] private bool attemptSteamVRInitProbe = true;
    [SerializeField] private bool forceEgoPedestrianViewWhenNoSteamVR = true;
    [SerializeField] private float egoPedestrianEyeHeight = 1.55f;
    [SerializeField] private bool lockLoadedVRRigToEgoPedestrian = true;
    [SerializeField] private bool useOpenXRNodeTrackingFallback = true;
    [SerializeField] private bool mapOpenXRHandsToSceneControllers = true;
    [SerializeField] private string leftControllerObjectName = "LeftController";
    [SerializeField] private string rightControllerObjectName = "RightController";
    [SerializeField] private bool enableVRTKTouchpadDebugLogs = true;
    [SerializeField] private float vrtkTouchpadAxisLogInterval = 0.2f;
    [SerializeField] private bool enableZMQPayloadLogs = false;
    [SerializeField] private int zmqPayloadLogEveryNMessages = 300;
    [SerializeField] private bool enableZMQTrafficSummaryLogs = true;
    [SerializeField] private float zmqTrafficSummaryIntervalSeconds = 3f;
    [SerializeField] private bool enableZMQParsedDataObjectLogs;
    [SerializeField] private bool enableOpenXREgoLocomotion = true;
    [SerializeField] private float openXRMoveSpeed = 1.9f;
    [SerializeField] private float openXRTurnSpeed = 90f;
    [SerializeField] private bool openXRSnapTurn = true;
    [SerializeField] private float openXRSnapTurnDegrees = 30f;
    [SerializeField] private float openXRSnapTurnThreshold = 0.4f;
    [SerializeField] private float openXRMoveDeadzone = 0.05f;
    [SerializeField] private bool preferViveTrackpadInput = true;
    [SerializeField] private bool requireViveTrackpadTouchForMove = true;
    [SerializeField] private bool requireViveTrackpadTouchForTurn = true;
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
        "Locomotion"
    };

    private bool steamVRProbeAttempted;
    private Transform cachedLeftControllerTransform;
    private Transform cachedRightControllerTransform;
    private bool openXRHeadBaselineCaptured;
    private float openXRHeadBaselineLocalY;
    private bool openXRSnapTurnReady = true;
    private bool touchpadDebugLoggersAttached;
    private bool touchpadDebugAttachSuccessLogged;
    private float nextTouchpadDebugRetryLogTime;

    private static readonly string[] touchpadDebugTargets =
    {
        "LeftController",
        "RightController",
        "LeftTouchpadControl",
        "RightTouchpadControl"
    };

    void Start () {
        controlledPedestrianId = EgoPedestrianId;
        EnsureEgoPedestrianAnchorExists();
        if (forceSteamVRAsDefault)
        {
            preferMainCameraAsEgoSource = false;
        }
        DisableTrackedPoseDriverOnLights();
        if (forceSteamVRAsDefault)
        {
            ProbeSteamVRInitialization();
        }
        EnsureFirstPersonRigActive();
        EnsureSingleAudioListener();
        LogVRReadability("Start");
        if (logVRReadability)
        {
            StartCoroutine(LogVRReadabilityFrames());
        }

        TryAttachVRTKTouchpadDebuggers();

        try
        {
            zmqRequester = new ZMQRequester();
            zmqRequester.LogReceivedPayloads = enableZMQPayloadLogs;
            zmqRequester.PayloadLogEveryNMessages = Mathf.Max(1, zmqPayloadLogEveryNMessages);
            zmqRequester.LogReceiveTrafficSummary = enableZMQTrafficSummaryLogs;
            zmqRequester.ReceiveTrafficSummaryIntervalSeconds = Mathf.Max(0.5f, zmqTrafficSummaryIntervalSeconds);
            zmqRequester.LogParsedDataObject = enableZMQParsedDataObjectLogs;
            zmqRequester.Start();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to start ZMQ requester: " + ex.Message);
            enabled = false;
        }
	}

    private void EnsureEgoPedestrianAnchorExists()
    {
        if (string.IsNullOrEmpty(controlledPedestrianId))
        {
            return;
        }

        GameObject existing = GameObject.Find(controlledPedestrianId);
        if (existing != null)
        {
            return;
        }

        GameObject personsRoot = GameObject.Find("Persons");
        if (personsRoot == null)
        {
            return;
        }

        Vector3 spawnPosition = Vector3.zero;
        Transform referenceTransform = FindSceneObjectByName("BodyPhysics") != null
            ? FindSceneObjectByName("BodyPhysics").transform
            : (Camera.main != null ? Camera.main.transform : null);

        if (referenceTransform != null)
        {
            spawnPosition = referenceTransform.position;
        }
        spawnPosition.y = 0.09f;

        GameObject prefab = Resources.Load<GameObject>("Person_v4");
        GameObject created;

        if (prefab != null)
        {
            created = GameObject.Instantiate(prefab, spawnPosition, Quaternion.identity, personsRoot.transform);
        }
        else
        {
            created = new GameObject(controlledPedestrianId);
            created.transform.SetParent(personsRoot.transform, false);
            created.transform.position = spawnPosition;
        }

        created.name = controlledPedestrianId;

        PedestrianController pedestrianController = created.GetComponent<PedestrianController>();
        if (pedestrianController != null)
        {
            pedestrianController.enabled = false;
        }

        Debug.LogWarning("[VR-DIAG] Created local ego pedestrian anchor: " + controlledPedestrianId);
    }

    private void EnsureSingleAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (listeners == null || listeners.Length <= 1)
        {
            return;
        }

        AudioListener keep = null;
        if (Camera.main != null)
        {
            keep = Camera.main.GetComponent<AudioListener>();
        }
        if (keep == null)
        {
            keep = listeners[0];
        }

        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener != null && listener != keep)
            {
                listener.enabled = false;
            }
        }
    }

    private void EnsureFirstPersonRigActive()
    {
        bool preferSteamVR = forceSteamVRAsDefault && IsSteamVRUsable();

        foreach (string rootName in firstPersonRoots)
        {
            bool shouldBeActive = rootName != "[VRTK_SDKManager]" || preferSteamVR;
            SetSceneObjectActive(rootName, shouldBeActive);
        }

        SetSceneObjectActive("SteamVRSDK", preferSteamVR);
        SetSceneObjectActive("VRSimulator", !preferSteamVR);

        ForcePreferredSDKSetup(preferSteamVR);

        bool steamVRReady = HasActiveSteamVRHeadset();
        SetSceneObjectActive("VRSimulator", !steamVRReady);
        bool hasLoadedRigCamera = TryGetLoadedHeadsetCamera(out _, out _);

        if (hasLoadedRigCamera)
        {
            DisableNonSteamVRCameras();
            AlignLoadedVRRigToEgoPedestrian();
        }
        else
        {
            EnableRootOverviewCamera();
            AlignLoadedVRRigToEgoPedestrian();
            AlignRootOverviewCameraToEgoPedestrian();
        }

        EnforceSingleActiveCamera();

        StartCoroutine(ReapplyRigSelectionForFrames(preferSteamVR));
    }

    private IEnumerator ReapplyRigSelectionForFrames(bool preferSteamVR)
    {
        int framesToRun = Mathf.Max(1, steamVRWarmupFrames);
        for (int i = 0; i < framesToRun; i++)
        {
            // Let VRTK finish its own auto-load pass, then enforce our preferred setup.
            yield return null;

            ForcePreferredSDKSetup(preferSteamVR);

            bool steamVRReady = HasActiveSteamVRHeadset();
            SetSceneObjectActive("VRSimulator", !steamVRReady);
            bool hasLoadedRigCamera = TryGetLoadedHeadsetCamera(out _, out _);

            if (hasLoadedRigCamera)
            {
                DisableNonSteamVRCameras();
                AlignLoadedVRRigToEgoPedestrian();
            }
            else
            {
                EnableRootOverviewCamera();
                AlignLoadedVRRigToEgoPedestrian();
                AlignRootOverviewCameraToEgoPedestrian();
            }

            EnforceSingleActiveCamera();

            LogVRReadability("Warmup frame " + i);
        }
    }

    private IEnumerator LogVRReadabilityFrames()
    {
        int frames = Mathf.Max(1, vrDiagnosticFrames);
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            LogVRReadability("Diag frame " + i);
        }
    }

    private void ForcePreferredSDKSetup(bool useSteamVR)
    {
        if (!useSteamVR)
        {
            // OpenXR flow in Unity 6 does not rely on VRTK setup loading through legacy OpenVR device names.
            return;
        }

        VRTK_SDKManager manager = VRTK_SDKManager.instance;
        if (manager == null)
        {
            return;
        }

        VRTK_SDKSetup[] allSetups = VRTK_SDKManager.GetAllSDKSetups();
        if (allSetups == null || allSetups.Length == 0)
        {
            return;
        }

        List<VRTK_SDKSetup> orderedSetups = new List<VRTK_SDKSetup>();
        VRTK_SDKSetup preferredSetup = null;

        for (int i = 0; i < allSetups.Length; i++)
        {
            VRTK_SDKSetup setup = allSetups[i];
            if (setup == null || !setup.isValid || !setup.isActiveAndEnabled)
            {
                continue;
            }

            orderedSetups.Add(setup);

            bool isSteamVRSetup = IsSteamVRSetup(setup);
            bool isPreferred = useSteamVR ? isSteamVRSetup : !isSteamVRSetup;
            if (preferredSetup == null && isPreferred)
            {
                preferredSetup = setup;
            }
        }

        if (preferredSetup == null)
        {
            return;
        }

        if (manager.loadedSetup == preferredSetup)
        {
            return;
        }

        orderedSetups.Remove(preferredSetup);
        orderedSetups.Insert(0, preferredSetup);
        manager.TryLoadSDKSetup(0, true, orderedSetups.ToArray());
    }

    private bool IsSteamVRSetup(VRTK_SDKSetup setup)
    {
        return IsSteamVRSDKInfo(setup.systemSDKInfo)
            || IsSteamVRSDKInfo(setup.boundariesSDKInfo)
            || IsSteamVRSDKInfo(setup.headsetSDKInfo)
            || IsSteamVRSDKInfo(setup.controllerSDKInfo);
    }

    private bool IsSteamVRSDKInfo(VRTK_SDKInfo info)
    {
        if (info == null)
        {
            return false;
        }

        string typeName = info.type != null ? info.type.Name : string.Empty;
        string prettyName = (info.description != null && info.description.prettyName != null) ? info.description.prettyName : string.Empty;
        string vrDeviceName = (info.description != null && info.description.vrDeviceName != null) ? info.description.vrDeviceName : string.Empty;

        return typeName.IndexOf("SteamVR", StringComparison.OrdinalIgnoreCase) >= 0
            || prettyName.IndexOf("SteamVR", StringComparison.OrdinalIgnoreCase) >= 0
            || vrDeviceName.Equals("OpenVR", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSteamVRUsable()
    {
        try
        {
            return SteamVR.active || SteamVR.usingNativeSupport;
        }
        catch
        {
            return false;
        }
    }

    private void DisableNonSteamVRCameras()
    {
        if (!TryGetLoadedHeadsetCamera(out VRTK_SDKSetup loadedSetup, out Camera headsetCamera))
        {
            return;
        }

        Transform loadedRigRoot = loadedSetup != null ? loadedSetup.transform : null;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null)
            {
                continue;
            }

            if (camera == headsetCamera)
            {
                continue;
            }

            if (loadedRigRoot != null && camera.transform.IsChildOf(loadedRigRoot))
            {
                continue;
            }

            if (camera.transform.parent == null && camera.gameObject.name == "Camera")
            {
                camera.gameObject.SetActive(false);
            }
        }
    }

    private void EnableRootOverviewCamera()
    {
        GameObject rootCamera = FindSceneObjectByName("Camera");
        if (rootCamera == null)
        {
            return;
        }

        if (!rootCamera.activeSelf)
        {
            rootCamera.SetActive(true);
        }

        Camera camera = rootCamera.GetComponent<Camera>();
        if (camera != null)
        {
            camera.enabled = true;
        }
    }

    private void AlignRootOverviewCameraToEgoPedestrian()
    {
        if (!forceEgoPedestrianViewWhenNoSteamVR)
        {
            return;
        }

        GameObject rootCamera = FindSceneObjectByName("Camera");
        if (rootCamera == null)
        {
            return;
        }

        Camera overviewCamera = rootCamera.GetComponent<Camera>();
        if (overviewCamera == null || !overviewCamera.enabled || !rootCamera.activeInHierarchy)
        {
            return;
        }

        Transform egoTransform = FindEgoPedestrianTransform();
        if (egoTransform == null)
        {
            return;
        }

        rootCamera.transform.position = egoTransform.position + Vector3.up * egoPedestrianEyeHeight;
        rootCamera.transform.rotation = Quaternion.Euler(0f, egoTransform.rotation.eulerAngles.y, 0f);
    }

    private void ApplyOpenXRNodeTrackingFallback()
    {
        if (!useOpenXRNodeTrackingFallback)
        {
            return;
        }

        if (TryGetLoadedHeadsetCamera(out _, out _))
        {
            openXRHeadBaselineCaptured = false;
            return;
        }

        if (!HasRunningXRDisplay())
        {
            return;
        }

        GameObject rootCameraObject = FindRootOverviewCameraObject();
        if (rootCameraObject == null)
        {
            return;
        }

        Camera fallbackCamera = rootCameraObject.GetComponent<Camera>();
        if (fallbackCamera == null || !fallbackCamera.enabled || !fallbackCamera.gameObject.activeInHierarchy)
        {
            return;
        }

        Transform egoTransform = FindEgoPedestrianTransform();
        if (egoTransform == null)
        {
            return;
        }

        if (!TryGetXRNodePose(XRNode.Head, out Vector3 headLocalPosition, out Quaternion headLocalRotation, out bool headTracked) || !headTracked)
        {
            return;
        }

        if (!openXRHeadBaselineCaptured)
        {
            openXRHeadBaselineCaptured = true;
            openXRHeadBaselineLocalY = headLocalPosition.y;
        }

        Quaternion worldYaw = Quaternion.Euler(0f, egoTransform.rotation.eulerAngles.y, 0f);
        Vector3 baseWorldPosition = egoTransform.position + Vector3.up * egoPedestrianEyeHeight;
        Vector3 calibratedHeadLocalPosition = headLocalPosition;
        calibratedHeadLocalPosition.y -= openXRHeadBaselineLocalY;

        fallbackCamera.transform.position = baseWorldPosition + worldYaw * calibratedHeadLocalPosition;
        fallbackCamera.transform.rotation = worldYaw * headLocalRotation;

        if (mapOpenXRHandsToSceneControllers)
        {
            Transform leftController = ResolveSceneControllerTransform(true);
            Transform rightController = ResolveSceneControllerTransform(false);
            ApplyOpenXRControllerNodePose(XRNode.LeftHand, leftController, baseWorldPosition, worldYaw);
            ApplyOpenXRControllerNodePose(XRNode.RightHand, rightController, baseWorldPosition, worldYaw);
        }
    }

    private void ApplyOpenXREgoLocomotion()
    {
        if (!enableOpenXREgoLocomotion)
        {
            return;
        }

        if (!HasRunningXRDisplay())
        {
            return;
        }

        Transform egoTransform = FindEgoPedestrianTransform();
        if (egoTransform == null)
        {
            return;
        }

        TryGetOpenXRAxisForLocomotion(XRNode.LeftHand, requireViveTrackpadTouchForMove, out Vector2 leftAxis);
        TryGetOpenXRAxisForLocomotion(XRNode.RightHand, requireViveTrackpadTouchForMove, out Vector2 rightAxis);

        float moveDeadzoneSqr = openXRMoveDeadzone * openXRMoveDeadzone;
        Vector2 moveAxis = leftAxis.sqrMagnitude >= moveDeadzoneSqr ? leftAxis : rightAxis;

        Camera drivingCamera = null;
        GameObject rootCameraObject = FindRootOverviewCameraObject();
        if (rootCameraObject != null)
        {
            drivingCamera = rootCameraObject.GetComponent<Camera>();
        }

        Vector3 planarForward = (drivingCamera != null ? drivingCamera.transform.forward : egoTransform.forward);
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude < 0.0001f)
        {
            planarForward = Vector3.forward;
        }
        planarForward.Normalize();

        Vector3 planarRight = (drivingCamera != null ? drivingCamera.transform.right : egoTransform.right);
        planarRight.y = 0f;
        if (planarRight.sqrMagnitude < 0.0001f)
        {
            planarRight = Vector3.Cross(Vector3.up, planarForward);
        }
        planarRight.Normalize();

        if (moveAxis.sqrMagnitude >= moveDeadzoneSqr)
        {
            Vector3 motion = planarForward * moveAxis.y + planarRight * moveAxis.x;
            if (motion.sqrMagnitude > 1f)
            {
                motion.Normalize();
            }

            egoTransform.position += motion * openXRMoveSpeed * Time.deltaTime;
        }

        TryGetOpenXRAxisForLocomotion(XRNode.RightHand, requireViveTrackpadTouchForTurn, out Vector2 turnAxis);
        float turnInput = Mathf.Abs(turnAxis.x) >= openXRMoveDeadzone ? turnAxis.x : 0f;

        if (Mathf.Abs(turnInput) < openXRMoveDeadzone)
        {
            openXRSnapTurnReady = true;
            return;
        }

        if (openXRSnapTurn)
        {
            if (Mathf.Abs(turnInput) >= openXRSnapTurnThreshold)
            {
                if (openXRSnapTurnReady)
                {
                    float step = Mathf.Sign(turnInput) * openXRSnapTurnDegrees;
                    egoTransform.rotation = Quaternion.Euler(0f, egoTransform.rotation.eulerAngles.y + step, 0f);
                    openXRSnapTurnReady = false;
                }
            }
            else if (Mathf.Abs(turnInput) <= (openXRSnapTurnThreshold * 0.5f))
            {
                openXRSnapTurnReady = true;
            }
        }
        else if (Mathf.Abs(turnInput) >= openXRMoveDeadzone)
        {
            float yawDelta = turnInput * openXRTurnSpeed * Time.deltaTime;
            egoTransform.rotation = Quaternion.Euler(0f, egoTransform.rotation.eulerAngles.y + yawDelta, 0f);
        }
    }

    private bool TryGetPrimary2DAxis(XRNode node, out Vector2 axis)
    {
        axis = Vector2.zero;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            return false;
        }

        return TryGetPrimary2DAxis(device, out axis);
    }

    private bool TryGetOpenXRAxisForLocomotion(XRNode node, bool requireTouch, out Vector2 axis)
    {
        axis = Vector2.zero;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            return false;
        }

        if (preferViveTrackpadInput && TryGetViveTrackpadAxis(device, requireTouch, out axis))
        {
            return true;
        }

        return TryGetPrimary2DAxis(device, out axis);
    }

    private bool TryGetPrimary2DAxis(InputDevice device, out Vector2 axis)
    {
        axis = Vector2.zero;

        if (TryGet2DAxisUsage(device, CommonUsages.primary2DAxis, out axis))
        {
            return true;
        }

        if (TryGet2DAxisUsage(device, CommonUsages.secondary2DAxis, out axis))
        {
            return true;
        }

        List<InputFeatureUsage> usages = new List<InputFeatureUsage>();
        if (!device.TryGetFeatureUsages(usages) || usages.Count == 0)
        {
            return false;
        }

        bool found = false;
        float bestMagnitude = -1f;

        for (int i = 0; i < usages.Count; i++)
        {
            InputFeatureUsage usage = usages[i];
            if (usage.type != typeof(Vector2))
            {
                continue;
            }

            InputFeatureUsage<Vector2> vector2Usage = new InputFeatureUsage<Vector2>(usage.name);
            if (!device.TryGetFeatureValue(vector2Usage, out Vector2 value))
            {
                continue;
            }

            float magnitude = value.sqrMagnitude;
            if (!found || magnitude > bestMagnitude)
            {
                axis = value;
                bestMagnitude = magnitude;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetViveTrackpadAxis(InputDevice device, bool requireTouch, out Vector2 axis)
    {
        axis = Vector2.zero;

        InputFeatureUsage<Vector2> trackpadUsage = new InputFeatureUsage<Vector2>("trackpad");
        if (!device.TryGetFeatureValue(trackpadUsage, out Vector2 trackpadAxis))
        {
            return false;
        }

        bool touched = TryGetBoolUsage(device, new InputFeatureUsage<bool>("trackpadTouched"), out bool rawTouched) && rawTouched;
        bool clicked = TryGetBoolUsage(device, new InputFeatureUsage<bool>("trackpadClicked"), out bool rawClicked) && rawClicked;

        if (!touched && device.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out bool commonTouched))
        {
            touched = commonTouched;
        }

        if (!clicked && device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool commonClicked))
        {
            clicked = commonClicked;
        }

        if (requireTouch && !touched && !clicked)
        {
            axis = Vector2.zero;
            return true;
        }

        axis = trackpadAxis;
        return true;
    }

    private bool TryGet2DAxisUsage(InputDevice device, InputFeatureUsage<Vector2> usage, out Vector2 value)
    {
        if (device.TryGetFeatureValue(usage, out value))
        {
            return true;
        }

        value = Vector2.zero;
        return false;
    }

    private bool TryGetBoolUsage(InputDevice device, InputFeatureUsage<bool> usage, out bool value)
    {
        if (device.TryGetFeatureValue(usage, out value))
        {
            return true;
        }

        value = false;
        return false;
    }

    private void ApplyOpenXRControllerNodePose(XRNode node, Transform controllerTransform, Vector3 baseWorldPosition, Quaternion worldYaw)
    {
        if (controllerTransform == null)
        {
            return;
        }

        if (!TryGetXRNodePose(node, out Vector3 localPosition, out Quaternion localRotation, out bool tracked) || !tracked)
        {
            return;
        }

        localPosition.y -= openXRHeadBaselineLocalY;
        controllerTransform.position = baseWorldPosition + worldYaw * localPosition;
        controllerTransform.rotation = worldYaw * localRotation;

        if (!controllerTransform.gameObject.activeSelf)
        {
            controllerTransform.gameObject.SetActive(true);
        }
    }

    private Transform ResolveSceneControllerTransform(bool leftHand)
    {
        if (leftHand)
        {
            if (cachedLeftControllerTransform == null)
            {
                cachedLeftControllerTransform = FindSceneControllerTransform(leftControllerObjectName, "LeftHand");
            }

            return cachedLeftControllerTransform;
        }

        if (cachedRightControllerTransform == null)
        {
            cachedRightControllerTransform = FindSceneControllerTransform(rightControllerObjectName, "RightHand");
        }

        return cachedRightControllerTransform;
    }

    private Transform FindSceneControllerTransform(string primaryName, string fallbackName)
    {
        if (!string.IsNullOrEmpty(primaryName))
        {
            GameObject named = FindSceneObjectByName(primaryName);
            if (named != null)
            {
                return named.transform;
            }
        }

        if (!string.IsNullOrEmpty(fallbackName))
        {
            GameObject fallback = FindSceneObjectByName(fallbackName);
            if (fallback != null)
            {
                return fallback.transform;
            }
        }

        return null;
    }

    private bool TryGetXRNodePose(XRNode node, out Vector3 localPosition, out Quaternion localRotation, out bool tracked)
    {
        localPosition = Vector3.zero;
        localRotation = Quaternion.identity;
        tracked = false;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
        {
            return false;
        }

        bool hasPosition = false;
        bool hasRotation = false;

        if (node == XRNode.Head)
        {
            hasPosition = device.TryGetFeatureValue(CommonUsages.centerEyePosition, out localPosition);
            hasRotation = device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out localRotation);
        }

        if (!hasPosition)
        {
            hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out localPosition);
        }

        if (!hasRotation)
        {
            hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out localRotation);
        }

        bool hasTrackedFlag = device.TryGetFeatureValue(CommonUsages.isTracked, out bool trackedFlag);
        tracked = hasTrackedFlag ? trackedFlag : (hasPosition || hasRotation);

        return hasPosition || hasRotation;
    }

    private void EnforceSingleActiveCamera()
    {
        Camera targetCamera = null;
        Transform targetRoot = null;

        if (TryGetLoadedHeadsetCamera(out _, out Camera loadedHeadsetCamera))
        {
            targetCamera = loadedHeadsetCamera;
            targetRoot = loadedHeadsetCamera.transform.root;
        }
        else
        {
            GameObject rootCameraObject = FindRootOverviewCameraObject();
            if (rootCameraObject != null)
            {
                targetCamera = rootCameraObject.GetComponent<Camera>();
                if (targetCamera != null)
                {
                    targetRoot = targetCamera.transform.root;
                }
            }
        }

        if (targetCamera == null)
        {
            return;
        }

        if (!targetCamera.gameObject.activeSelf)
        {
            targetCamera.gameObject.SetActive(true);
        }
        targetCamera.enabled = true;
        targetCamera.tag = "MainCamera";

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == targetCamera)
            {
                continue;
            }

            if (targetRoot != null && camera.transform.root == targetRoot)
            {
                continue;
            }

            camera.enabled = false;

            if (camera.gameObject.activeSelf && (camera.transform.parent == null || camera.gameObject.name == "Camera"))
            {
                camera.gameObject.SetActive(false);
            }
        }
    }

    private GameObject FindRootOverviewCameraObject()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null)
            {
                continue;
            }

            if (camera.transform.parent == null && camera.gameObject.name == "Camera")
            {
                return camera.gameObject;
            }
        }

        return FindSceneObjectByName("Camera");
    }

    private void AlignLoadedVRRigToEgoPedestrian()
    {
        if (!lockLoadedVRRigToEgoPedestrian)
        {
            return;
        }

        Transform egoTransform = FindEgoPedestrianTransform();
        if (egoTransform == null)
        {
            return;
        }

        VRTK_SDKSetup loadedSetup = VRTK_SDKManager.GetLoadedSDKSetup();
        if (loadedSetup == null || loadedSetup.transform == null || !loadedSetup.gameObject.activeInHierarchy)
        {
            return;
        }

        Transform rigTransform = loadedSetup.transform;
        Vector3 alignedPosition = rigTransform.position;
        alignedPosition.x = egoTransform.position.x;
        alignedPosition.y = egoTransform.position.y;
        alignedPosition.z = egoTransform.position.z;
        rigTransform.position = alignedPosition;

        // Keep headset yaw free when true SteamVR tracking is live; otherwise use ego heading.
        if (!HasActiveSteamVRHeadset())
        {
            rigTransform.rotation = Quaternion.Euler(0f, egoTransform.rotation.eulerAngles.y, 0f);
        }
    }

    private bool TryGetLoadedHeadsetCamera(out VRTK_SDKSetup loadedSetup, out Camera headsetCamera)
    {
        loadedSetup = VRTK_SDKManager.GetLoadedSDKSetup();
        headsetCamera = null;

        if (loadedSetup == null || loadedSetup.actualHeadset == null || !loadedSetup.actualHeadset.activeInHierarchy)
        {
            return false;
        }

        Camera candidate = loadedSetup.actualHeadset.GetComponentInChildren<Camera>(true);
        if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
        {
            return false;
        }

        headsetCamera = candidate;
        return true;
    }

    private Transform FindEgoPedestrianTransform()
    {
        SubjectController subjectController = FindFirstObjectByType<SubjectController>();
        if (subjectController != null)
        {
            return subjectController.transform;
        }

        if (!string.IsNullOrEmpty(controlledPedestrianId))
        {
            GameObject egoById = GameObject.Find(controlledPedestrianId);
            if (egoById != null)
            {
                return egoById.transform;
            }
        }

        if (!string.IsNullOrEmpty(subjectObjectName))
        {
            GameObject subjectByName = GameObject.Find(subjectObjectName);
            if (subjectByName != null)
            {
                return subjectByName.transform;
            }
        }

        GameObject fallbackSubject = GameObject.Find("subject");
        if (fallbackSubject != null)
        {
            return fallbackSubject.transform;
        }

        GameObject egoByLiteralName = GameObject.Find(EgoPedestrianId);
        if (egoByLiteralName != null)
        {
            return egoByLiteralName.transform;
        }

        GameObject personsObject = GameObject.Find("Persons");
        if (personsObject != null)
        {
            Transform personsTransform = personsObject.transform;
            for (int i = 0; i < personsTransform.childCount; i++)
            {
                Transform child = personsTransform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (child.name == EgoPedestrianId || child.name == controlledPedestrianId || child.name == subjectObjectName)
                {
                    return child;
                }
            }
        }

        GameObject bodyPhysics = FindSceneObjectByName("BodyPhysics");
        if (bodyPhysics != null)
        {
            return bodyPhysics.transform;
        }

        GameObject vrtkScripts = FindSceneObjectByName("VRTKScripts");
        if (vrtkScripts != null)
        {
            return vrtkScripts.transform;
        }

        GameObject locomotion = FindSceneObjectByName("Locomotion");
        if (locomotion != null)
        {
            return locomotion.transform;
        }

        GameObject objectControl = FindSceneObjectByName("Object Control");
        if (objectControl != null)
        {
            return objectControl.transform;
        }

        if (subject != null && subject.GetComponent<Camera>() == null)
        {
            return subject.transform;
        }

        return null;
    }

    private bool HasActiveSteamVRHeadset()
    {
        VRTK_SDKSetup loadedSetup = VRTK_SDKManager.GetLoadedSDKSetup();
        if (loadedSetup == null || !IsSteamVRSetup(loadedSetup))
        {
            return false;
        }

        if (loadedSetup.actualHeadset == null || !loadedSetup.actualHeadset.activeInHierarchy)
        {
            return false;
        }

        Camera headsetCamera = loadedSetup.actualHeadset.GetComponentInChildren<Camera>(true);
        return headsetCamera != null && headsetCamera.enabled;
    }

    private void LogVRReadability(string phase)
    {
        if (!logVRReadability)
        {
            return;
        }

        VRTK_SDKSetup loadedSetup = VRTK_SDKManager.GetLoadedSDKSetup();
        bool hasLoadedSetup = loadedSetup != null;
        bool loadedIsSteamVR = hasLoadedSetup && IsSteamVRSetup(loadedSetup);

        GameObject headset = hasLoadedSetup ? loadedSetup.actualHeadset : null;
        bool headsetActive = headset != null && headset.activeInHierarchy;
        Camera headsetCamera = headset != null ? headset.GetComponentInChildren<Camera>(true) : null;
        bool headsetCameraActive = headsetCamera != null && headsetCamera.enabled && headsetCamera.gameObject.activeInHierarchy;

        bool hasSteamVRHeadset = HasActiveSteamVRHeadset();
        bool hasOpenXRHeadTracking = HasTrackedOpenXRHead();
        bool xrRunning = HasRunningXRDisplay();

        string loadedSetupName = hasLoadedSetup ? loadedSetup.name : "<none>";

        bool steamVRActive = SteamVR.active;
        bool steamVRNativeSupport = SteamVR.usingNativeSupport;

        string summary = string.Format(
            "[VR-DIAG] {0} | readable={1} xrRunning={2} steamVR.active={3} nativeSupport={4} loadedSetup={5} loadedIsSteamVR={6} headsetObj={7} headsetActive={8} headsetCam={9} headsetCamActive={10} xrDisplays={11} xrInputs={12} activeCameras={13} xrNodes={14} sdkSetups={15}",
            phase,
            hasSteamVRHeadset || hasOpenXRHeadTracking,
            xrRunning,
            steamVRActive,
            steamVRNativeSupport,
            loadedSetupName,
            loadedIsSteamVR,
            headset != null ? headset.name : "<none>",
            headsetActive,
            headsetCamera != null ? headsetCamera.name : "<none>",
            headsetCameraActive,
            BuildXRDisplaySummary(),
            BuildXRInputSummary(),
            BuildActiveCameraSummary(),
            BuildXRNodeTrackingSummary(),
            BuildSDKSetupSummary());

        if (hasSteamVRHeadset || hasOpenXRHeadTracking)
        {
            Debug.Log(summary);
        }
        else
        {
            Debug.LogWarning(summary);
        }
    }

    private string BuildXRDisplaySummary()
    {
        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);

        if (displays.Count == 0)
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < displays.Count; i++)
        {
            XRDisplaySubsystem display = displays[i];
            if (display == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(",");
            }

            builder.Append(display.SubsystemDescriptor != null ? display.SubsystemDescriptor.id : "unknown");
            builder.Append(":");
            builder.Append(display.running ? "running" : "stopped");
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private string BuildXRInputSummary()
    {
        List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputs);

        if (inputs.Count == 0)
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < inputs.Count; i++)
        {
            XRInputSubsystem input = inputs[i];
            if (input == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(",");
            }

            builder.Append(input.SubsystemDescriptor != null ? input.SubsystemDescriptor.id : "unknown");
            builder.Append(":");
            builder.Append(input.running ? "running" : "stopped");
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private string BuildActiveCameraSummary()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        StringBuilder builder = new StringBuilder();

        int added = 0;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(",");
            }

            builder.Append(camera.name);
            added++;
            if (added >= 5)
            {
                break;
            }
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private string BuildSDKSetupSummary()
    {
        VRTK_SDKSetup[] setups = VRTK_SDKManager.GetAllSDKSetups();
        if (setups == null || setups.Length == 0)
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < setups.Length; i++)
        {
            VRTK_SDKSetup setup = setups[i];
            if (setup == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(",");
            }

            builder.Append(setup.name);
            builder.Append("(active=");
            builder.Append(setup.gameObject.activeInHierarchy ? "1" : "0");
            builder.Append(",valid=");
            builder.Append(setup.isValid ? "1" : "0");
            builder.Append(",steam=");
            builder.Append(IsSteamVRSetup(setup) ? "1" : "0");
            builder.Append(")");
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private string BuildXRNodeTrackingSummary()
    {
        bool hasHeadPose = TryGetXRNodePose(XRNode.Head, out _, out _, out bool headTracked);
        bool hasLeftPose = TryGetXRNodePose(XRNode.LeftHand, out _, out _, out bool leftTracked);
        bool hasRightPose = TryGetXRNodePose(XRNode.RightHand, out _, out _, out bool rightTracked);

        return string.Format(
            "head={0},left={1},right={2}",
            (hasHeadPose && headTracked) ? "1" : "0",
            (hasLeftPose && leftTracked) ? "1" : "0",
            (hasRightPose && rightTracked) ? "1" : "0");
    }

    private void ProbeSteamVRInitialization()
    {
        if (!attemptSteamVRInitProbe || steamVRProbeAttempted)
        {
            return;
        }

        steamVRProbeAttempted = true;
        try
        {
            SteamVR instance = SteamVR.instance;
            if (instance != null)
            {
                Debug.Log("[VR-DIAG] SteamVR probe connected: " + instance.hmd_TrackingSystemName + " " + instance.hmd_SerialNumber);
            }
            else
            {
                Debug.LogWarning("[VR-DIAG] SteamVR probe failed: SteamVR.instance is null.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VR-DIAG] SteamVR probe exception: " + ex.Message);
        }
    }

    private bool IsSteamVRCamera(Camera camera, GameObject steamVRRoot)
    {
        if (camera.GetComponentInParent<SteamVR_Camera>() != null)
        {
            return true;
        }

        return steamVRRoot != null && camera.transform.IsChildOf(steamVRRoot.transform);
    }

    private bool HasRunningXRDisplay()
    {
        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            XRDisplaySubsystem display = displays[i];
            if (display != null && display.running)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTrackedOpenXRHead()
    {
        if (!HasRunningXRDisplay())
        {
            return false;
        }

        return TryGetXRNodePose(XRNode.Head, out _, out _, out bool tracked) && tracked;
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
        touchpadDebugLoggersAttached = false;
        touchpadDebugAttachSuccessLogged = false;
        if (zmqRequester != null)
        {
            zmqRequester.Stop();
        }
    }

    private void TryAttachVRTKTouchpadDebuggers()
    {
        if (!enableVRTKTouchpadDebugLogs)
        {
            return;
        }

        int attachedCount = 0;
        List<string> missingTargets = new List<string>();

        for (int i = 0; i < touchpadDebugTargets.Length; i++)
        {
            string objectName = touchpadDebugTargets[i];
            GameObject target = FindSceneObjectByName(objectName);
            if (target == null)
            {
                missingTargets.Add(objectName);
                continue;
            }

            VRTKTouchpadDebugLogger logger = target.GetComponent<VRTKTouchpadDebugLogger>();
            if (logger == null)
            {
                logger = target.AddComponent<VRTKTouchpadDebugLogger>();
            }

            logger.ConfigureForRuntime(objectName, vrtkTouchpadAxisLogInterval);
            attachedCount++;
        }

        bool hasControllers = FindSceneObjectByName(leftControllerObjectName) != null && FindSceneObjectByName(rightControllerObjectName) != null;
        touchpadDebugLoggersAttached = hasControllers && attachedCount > 0;

        if (touchpadDebugLoggersAttached)
        {
            if (!touchpadDebugAttachSuccessLogged)
            {
                Debug.Log("[VRTK-TP-DEBUG] Logger components attached targets=" + attachedCount + " hasControllers=" + hasControllers);
                touchpadDebugAttachSuccessLogged = true;
            }
            return;
        }

        touchpadDebugAttachSuccessLogged = false;

        if (Time.unscaledTime >= nextTouchpadDebugRetryLogTime)
        {
            string missingLabel = missingTargets.Count > 0 ? string.Join(",", missingTargets.ToArray()) : "none";
            Debug.LogWarning("[VRTK-TP-DEBUG] Waiting for touchpad debug targets. attached=" + attachedCount + " hasControllers=" + hasControllers + " missing=" + missingLabel);
            nextTouchpadDebugRetryLogTime = Time.unscaledTime + 3f;
        }
    }

    private void DisableTrackedPoseDriverOnLights()
    {
        TrackedPoseDriver[] drivers = FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < drivers.Length; i++)
        {
            TrackedPoseDriver driver = drivers[i];
            if (driver == null)
            {
                continue;
            }

            bool isCameraDriver = driver.GetComponent<Camera>() != null;
            bool isLightDriver = driver.GetComponent<Light>() != null || driver.gameObject.name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isCameraDriver && isLightDriver && driver.enabled)
            {
                driver.enabled = false;
                Debug.LogWarning("[VR-DIAG] Disabled TrackedPoseDriver on non-camera object: " + driver.gameObject.name);
            }
        }
    }

    private void Update()
    {
        if (enableVRTKTouchpadDebugLogs && !touchpadDebugLoggersAttached)
        {
            TryAttachVRTKTouchpadDebuggers();
        }

        bool steamVRReady = HasActiveSteamVRHeadset();
        SetSceneObjectActive("VRSimulator", !steamVRReady);
        bool hasLoadedRigCamera = TryGetLoadedHeadsetCamera(out _, out _);

        if (hasLoadedRigCamera)
        {
            DisableNonSteamVRCameras();
            AlignLoadedVRRigToEgoPedestrian();
        }
        else
        {
            ApplyOpenXREgoLocomotion();
            EnableRootOverviewCamera();
            AlignLoadedVRRigToEgoPedestrian();
            AlignRootOverviewCameraToEgoPedestrian();
        }

        ApplyOpenXRNodeTrackingFallback();

        EnforceSingleActiveCamera();

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

    private void LateUpdate()
    {
        bool hasLoadedRigCamera = TryGetLoadedHeadsetCamera(out _, out _);
        AlignLoadedVRRigToEgoPedestrian();

        if (!hasLoadedRigCamera)
        {
            AlignRootOverviewCameraToEgoPedestrian();
        }

        ApplyOpenXRNodeTrackingFallback();

        EnforceSingleActiveCamera();
    }

    private Transform GetSubjectTransform()
    {
        if (egoSourceTransform != null)
        {
            subject = egoSourceTransform.gameObject;
            return egoSourceTransform;
        }

        VRTK_SDKSetup loadedSetup = VRTK_SDKManager.GetLoadedSDKSetup();
        if (loadedSetup != null && loadedSetup.actualHeadset != null && loadedSetup.actualHeadset.activeInHierarchy)
        {
            subject = loadedSetup.actualHeadset;
            return loadedSetup.actualHeadset.transform;
        }

        if (preferMainCameraAsEgoSource && Camera.main != null)
        {
            subject = Camera.main.gameObject;
            return Camera.main.transform;
        }

        SubjectController subjectController = FindFirstObjectByType<SubjectController>();
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
