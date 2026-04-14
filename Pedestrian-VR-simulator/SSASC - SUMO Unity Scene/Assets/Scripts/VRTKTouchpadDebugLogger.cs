using UnityEngine;
using VRTK;

public class VRTKTouchpadDebugLogger : MonoBehaviour
{
    [SerializeField] private string debugLabel = "";
    [SerializeField] private bool logAxisContinuously = true;
    [SerializeField] private float axisLogInterval = 0.2f;

    private VRTK_ControllerEvents controllerEvents;
    private VRTK_TouchpadControl touchpadControl;
    private float nextAxisLogTime;

    public void ConfigureForRuntime(string label, float axisInterval)
    {
        debugLabel = label;
        axisLogInterval = Mathf.Max(0.02f, axisInterval);
    }

    private void OnEnable()
    {
        ResolveComponents();
        Subscribe();
        Debug.Log($"{Prefix()} enabled. controllerEvents={(controllerEvents != null)} touchpadControl={(touchpadControl != null)}");
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (!logAxisContinuously || controllerEvents == null)
        {
            return;
        }

        if (Time.unscaledTime < nextAxisLogTime)
        {
            return;
        }

        nextAxisLogTime = Time.unscaledTime + Mathf.Max(0.02f, axisLogInterval);
        Debug.Log($"{Prefix()} state: touched={controllerEvents.touchpadTouched} pressed={controllerEvents.touchpadPressed} axis={controllerEvents.GetTouchpadAxis()} twoAxis={controllerEvents.GetTouchpadTwoAxis()}");
    }

    private void ResolveComponents()
    {
        controllerEvents = GetComponent<VRTK_ControllerEvents>();
        if (controllerEvents == null)
        {
            controllerEvents = GetComponentInParent<VRTK_ControllerEvents>();
        }
        if (controllerEvents == null)
        {
            controllerEvents = GetComponentInChildren<VRTK_ControllerEvents>(true);
        }

        touchpadControl = GetComponent<VRTK_TouchpadControl>();
        if (touchpadControl == null)
        {
            touchpadControl = GetComponentInParent<VRTK_TouchpadControl>();
        }
        if (touchpadControl == null)
        {
            touchpadControl = GetComponentInChildren<VRTK_TouchpadControl>(true);
        }
    }

    private void Subscribe()
    {
        if (controllerEvents != null)
        {
            controllerEvents.TouchpadTouchStart += OnTouchpadTouchStart;
            controllerEvents.TouchpadTouchEnd += OnTouchpadTouchEnd;
            controllerEvents.TouchpadPressed += OnTouchpadPressed;
            controllerEvents.TouchpadReleased += OnTouchpadReleased;
            controllerEvents.TouchpadAxisChanged += OnTouchpadAxisChanged;
            controllerEvents.TouchpadTwoAxisChanged += OnTouchpadTwoAxisChanged;
        }

        if (touchpadControl != null)
        {
            touchpadControl.XAxisChanged += OnObjectControlXAxisChanged;
            touchpadControl.YAxisChanged += OnObjectControlYAxisChanged;
        }
    }

    private void Unsubscribe()
    {
        if (controllerEvents != null)
        {
            controllerEvents.TouchpadTouchStart -= OnTouchpadTouchStart;
            controllerEvents.TouchpadTouchEnd -= OnTouchpadTouchEnd;
            controllerEvents.TouchpadPressed -= OnTouchpadPressed;
            controllerEvents.TouchpadReleased -= OnTouchpadReleased;
            controllerEvents.TouchpadAxisChanged -= OnTouchpadAxisChanged;
            controllerEvents.TouchpadTwoAxisChanged -= OnTouchpadTwoAxisChanged;
        }

        if (touchpadControl != null)
        {
            touchpadControl.XAxisChanged -= OnObjectControlXAxisChanged;
            touchpadControl.YAxisChanged -= OnObjectControlYAxisChanged;
        }
    }

    private void OnTouchpadTouchStart(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadTouchStart axis={e.touchpadAxis} pressure={e.buttonPressure:0.000}");
    }

    private void OnTouchpadTouchEnd(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadTouchEnd axis={e.touchpadAxis} pressure={e.buttonPressure:0.000}");
    }

    private void OnTouchpadPressed(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadPressed axis={e.touchpadAxis} pressure={e.buttonPressure:0.000}");
    }

    private void OnTouchpadReleased(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadReleased axis={e.touchpadAxis} pressure={e.buttonPressure:0.000}");
    }

    private void OnTouchpadAxisChanged(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadAxisChanged axis={e.touchpadAxis} angle={e.touchpadAngle:0.0}");
    }

    private void OnTouchpadTwoAxisChanged(object sender, ControllerInteractionEventArgs e)
    {
        Debug.Log($"{Prefix()} TouchpadTwoAxisChanged axis={e.touchpadTwoAxis} angle={e.touchpadTwoAngle:0.0}");
    }

    private void OnObjectControlXAxisChanged(object sender, ObjectControlEventArgs e)
    {
        Debug.Log($"{Prefix()} ObjectControl XAxisChanged axis={e.axis:0.000} deadzone={e.deadzone:0.000} direction={e.axisDirection}");
    }

    private void OnObjectControlYAxisChanged(object sender, ObjectControlEventArgs e)
    {
        Debug.Log($"{Prefix()} ObjectControl YAxisChanged axis={e.axis:0.000} deadzone={e.deadzone:0.000} direction={e.axisDirection}");
    }

    private string Prefix()
    {
        string label = string.IsNullOrEmpty(debugLabel) ? gameObject.name : debugLabel;
        return $"[VRTK-TP-DEBUG][{label}]";
    }
}