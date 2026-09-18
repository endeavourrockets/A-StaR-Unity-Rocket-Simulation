using UnityEngine;

/*
 * Keeps the rocket's local up axis close to world up using two PID loops.
 * Reads true attitude and angular velocity from Unity, not SensorSimulator.
 * The error is based on sin(angle), with angular rates in rad/s and commands in degrees.
 * ! Axis mapping and gains still need checking against the canard force model.
 */

[RequireComponent(typeof(Rigidbody))]
public class RocketPIDController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanardController canardController;

    [Header("PID Gains  (tune these)")]
    [SerializeField] private float Kp = 8f;    // proportional gain
    [SerializeField] private float Ki = 0.1f;  // integral gain
    [SerializeField] private float Kd = 2f;    // derivative gain

    [Header("Controller Limits")]
    [SerializeField] private float integralClamp = 10f;  // limit on the integrated sin(angle) error, in seconds
    [SerializeField] private float minSpeedForControl = 5f;  // m/s — no authority at near-zero speed

    [Header("Activation")]
    [SerializeField] private bool pidActive = true;
    [SerializeField] private bool activeOnlyDuringBurn = false; // disables control when fuel is exhausted

    [Header("Debug")]
    [SerializeField] private bool showDebugGUI = true;
    private Rigidbody rb;
    private RocketSim rocketSim;

    private float pitchIntegral = 0f;
    private float yawIntegral = 0f;
    private float dbgPitchError, dbgYawError;
    private float dbgPitchCmd, dbgYawCmd;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rocketSim = GetComponent<RocketSim>();

        if (rocketSim == null)
            Debug.LogError("RocketPIDController: RocketSim not found on this GameObject.");
        if (canardController == null)
            Debug.LogError("RocketPIDController: CanardController reference not assigned in the Inspector.");
    }

    void FixedUpdate()
    {
        if (!pidActive) return;
        if (activeOnlyDuringBurn && rocketSim != null && rocketSim.GetRemainingFuel() <= 0f)
        {
            canardController.ResetCanards();
            return;
        }
        if (rocketSim != null && rocketSim.GetSpeed() < minSpeedForControl)
        {
            ResetIntegrators();
            canardController.ResetCanards();
            return;
        }

        float dt = Time.fixedDeltaTime;

        // Correction axis in world space; its magnitude is sin(angle error).
        Vector3 errorAxis = Vector3.Cross(transform.up, Vector3.up); // world space

        float pitchError = Vector3.Dot(errorAxis, transform.right);   // body X component
        float yawError = Vector3.Dot(errorAxis, transform.forward); // body Z component

        dbgPitchError = pitchError * Mathf.Rad2Deg;
        dbgYawError = yawError * Mathf.Rad2Deg;
        pitchIntegral = Mathf.Clamp(pitchIntegral + pitchError * dt, -integralClamp, integralClamp);
        yawIntegral = Mathf.Clamp(yawIntegral + yawError * dt, -integralClamp, integralClamp);

        // Body-axis rates provide the derivative term without differencing the error.
        Vector3 angVelWorld = rb.angularVelocity; // rad/s, world space
        float pitchRate = -Vector3.Dot(angVelWorld, transform.right);
        float yawRate = -Vector3.Dot(angVelWorld, transform.forward);
        float pitchCmd = Kp * pitchError + Ki * pitchIntegral + Kd * pitchRate;
        float yawCmd = Kp * yawError + Ki * yawIntegral + Kd * yawRate;

        dbgPitchCmd = pitchCmd;
        dbgYawCmd = yawCmd;
        canardController.SetDeflections(pitchCmd, yawCmd);
    }

    private void ResetIntegrators()
    {
        pitchIntegral = 0f;
        yawIntegral = 0f;
    }

    public void EnablePID()
    {
        pidActive = true;
    }

    public void DisablePID()
    {
        pidActive = false;
        ResetIntegrators();
        canardController.ResetCanards();
    }

    void OnGUI()
    {
        if (!showDebugGUI) return;

        int y = 80; // offset below CanardController's HUD rows
        GUI.Label(new Rect(10, y, 300, 20), "--- PID Controller --------");
        GUI.Label(new Rect(10, y + 20, 300, 20), $"Pitch error: {dbgPitchError:+000.0;-000.0}°   cmd: {dbgPitchCmd:+00.0;-00.0}°");
        GUI.Label(new Rect(10, y + 40, 300, 20), $"Yaw error:   {dbgYawError:+000.0;-000.0}°   cmd: {dbgYawCmd:+00.0;-00.0}°");
        GUI.Label(new Rect(10, y + 60, 300, 20), $"Integrals:   P={pitchIntegral:F2}  Y={yawIntegral:F2}");
        GUI.Label(new Rect(10, y + 80, 300, 20), $"Speed:       {(rocketSim != null ? rocketSim.GetSpeed() : 0f):F1} m/s");
        GUI.Label(new Rect(10, y + 100, 300, 20), $"Fuel left:   {(rocketSim != null ? rocketSim.GetRemainingFuel() : 0f):F3} kg");
    }
}