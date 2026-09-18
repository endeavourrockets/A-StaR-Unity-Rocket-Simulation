using UnityEngine;

/*
 * Manages four canard transforms and exposes their angles to RocketSim.
 * Canards 0/2 share the pitch command; 1/3 share the yaw command.
 * Angles are in degrees. Disable manual input when using the PID.
 * ! The visual hinge axes and force directions still need to be checked.
 */

public class CanardController : MonoBehaviour
{
    [Header("Canard References")]
    [SerializeField] private Transform canardsParent;       // Assign the "Canards" parent object

    [Header("Control Settings")]
    [SerializeField] private float maxDeflectionAngle = 20f;  // degrees
    [SerializeField] private float deflectionSpeed = 60f;  // degrees per second (manual input rate)

    [Header("Current Deflections (read-only in play mode)")]
    [SerializeField] private float pitchDeflection = 0f;    // nose-up/down  (body X-plane)
    [SerializeField] private float yawDeflection = 0f;    // nose-left/right (body Z-plane)

    [Header("Manual Input")]
    [SerializeField] private bool allowManualInput = false; // Disable when PID is active
    private Transform[] canards;
    private float[] canardAngles;   // exposed to RocketSim for force calculation
    private bool passiveMode;
    private Quaternion[] neutralRotations;

    void Awake()
    {
        canards = new Transform[4];
        canardAngles = new float[4];
        neutralRotations = new Quaternion[4];
        for (int i = 0; i < 4; i++)
        {
            canards[i] = canardsParent.GetChild(i);
            neutralRotations[i] = canards[i].localRotation;
        }
    }

    void Update()
    {
        if (passiveMode) return;
        if (allowManualInput)
            ManageInput();

        ApplyCanardRotations();
    }

    void ManageInput()
    {
        if (Input.GetKey(KeyCode.LeftArrow))
            yawDeflection -= deflectionSpeed * Time.deltaTime;
        else if (Input.GetKey(KeyCode.RightArrow))
            yawDeflection += deflectionSpeed * Time.deltaTime;
        else
            yawDeflection = Mathf.MoveTowards(yawDeflection, 0f, deflectionSpeed * Time.deltaTime);

        if (Input.GetKey(KeyCode.UpArrow))
            pitchDeflection += deflectionSpeed * Time.deltaTime;
        else if (Input.GetKey(KeyCode.DownArrow))
            pitchDeflection -= deflectionSpeed * Time.deltaTime;
        else
            pitchDeflection = Mathf.MoveTowards(pitchDeflection, 0f, deflectionSpeed * Time.deltaTime);

        pitchDeflection = Mathf.Clamp(pitchDeflection, -maxDeflectionAngle, maxDeflectionAngle);
        yawDeflection = Mathf.Clamp(yawDeflection, -maxDeflectionAngle, maxDeflectionAngle);
    }

    // Update visual rotations and the angles read by RocketSim.
    void ApplyCanardRotations()
    {
        if (canards == null || canards.Length < 4) return;
        canards[0].localRotation = Quaternion.Euler(pitchDeflection, 0, 0);
        canardAngles[0] = pitchDeflection;
        canards[1].localRotation = Quaternion.Euler(0, yawDeflection, 0);
        canardAngles[1] = yawDeflection;
        canards[2].localRotation = Quaternion.Euler(pitchDeflection, 0, 0);
        canardAngles[2] = pitchDeflection;
        canards[3].localRotation = Quaternion.Euler(0, yawDeflection, 0);
        canardAngles[3] = yawDeflection;
    }

    public void SetDeflections(float pitch, float yaw)
    {
        if (passiveMode) return;
        pitchDeflection = Mathf.Clamp(pitch, -maxDeflectionAngle, maxDeflectionAngle);
        yawDeflection = Mathf.Clamp(yaw, -maxDeflectionAngle, maxDeflectionAngle);
    }

    // Reset commands; the angles and transforms update on the next Update.
    public void ResetCanards()
    {
        pitchDeflection = 0f;
        yawDeflection = 0f;
    }

    public void SetPassiveMode()
    {
        passiveMode = true;
        allowManualInput = false;
        ResetCanards();
        if (canardAngles == null) return;
        for (int i = 0; i < canardAngles.Length; i++)
        {
            canardAngles[i] = 0f;
            canards[i].localRotation = neutralRotations[i];
        }
    }

    public float[] GetCanardAngles() => canardAngles;
    public Transform[] GetCanardTransforms() => canards;
    public float GetPitchDeflection() => pitchDeflection;
    public float GetYawDeflection() => yawDeflection;
    public float GetCanardsHeight() => 0.9f;
    public float GetMaxDeflection() => maxDeflectionAngle;

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 200, 20), $"Pitch deflection: {pitchDeflection:F2}°");
        GUI.Label(new Rect(10, 30, 200, 20), $"Yaw deflection:   {yawDeflection:F2}°");
        string inputMode = passiveMode ? "Passive (neutral)" : allowManualInput ? "Manual (arrow keys)" : "External";
        GUI.Label(new Rect(10, 50, 300, 20), $"Control mode: {inputMode}");
    }
}
