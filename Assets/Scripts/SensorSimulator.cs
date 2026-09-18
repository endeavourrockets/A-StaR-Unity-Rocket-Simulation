using UnityEngine;

/*
 * Generates accelerometer, gyro and barometer readings from the simulated state.
 * Adds noise, bias and a delay. Sample periods are rounded to physics ticks.
 * The accelerometer measures body-frame specific force (acceleration minus gravity).
 * ! PID still reads ground truth. The exported quaternion and gyro bias are also ground truth.
 */

[RequireComponent(typeof(Rigidbody))]
public class SensorSimulator : MonoBehaviour
{
    public enum FlightPhase { Pad = 0, PoweredAscent = 1, Coast = 2, Descent = 3, Landed = 4 }

    [Header("References")]
    [SerializeField] private RocketSim rocketSim;
    [SerializeField] private CanardController canardController;

    [Header("Repeatability")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int randomSeed = 12345;

    [Header("Accelerometer (specific force, body frame)")]
    [SerializeField] private float accelNoiseStdDev = 0.05f;   // m/s^2, 1-sigma white noise
    [SerializeField] private float accelBiasStdDev = 0.1f;     // m/s^2, 1-sigma fixed turn-on bias
    [SerializeField] private float accelSampleRateHz = 50f;    // Hz
    [SerializeField] private float accelDelaySeconds = 0f;     // s

    [Header("Gyroscope (angular rate, body frame)")]
    [SerializeField] private float gyroNoiseStdDev = 0.01f;        // rad/s (~0.5 deg/s), 1-sigma white noise
    [SerializeField] private float gyroBiasDriftStdDev = 0.0005f;  // rad/s per sqrt(s), random-walk drift rate
    [SerializeField] private float gyroBiasMax = 0.05f;            // rad/s, per-axis clamp (~2.9 deg/s)
    [SerializeField] private float gyroSampleRateHz = 50f;         // Hz
    [SerializeField] private float gyroDelaySeconds = 0f;          // s

    [Header("Barometer (pressure -> altitude estimate)")]
    [SerializeField] private float seaLevelPressure = 101325f;  // Pa
    [SerializeField] private float baroScaleHeight = 8500f;     // m — must match RocketSim.GetAirDensity()'s model
    [SerializeField] private float baroNoiseStdDev = 5f;        // Pa, 1-sigma white noise
    [SerializeField] private float baroBiasStdDev = 50f;        // Pa, 1-sigma fixed turn-on bias
    [SerializeField] private float baroSampleRateHz = 20f;      // Hz — slower than IMU
    [SerializeField] private float baroDelaySeconds = 0.02f;    // s

    [Header("Flight Phase Detection")]
    [SerializeField] private int phaseConfirmTicks = 3;               // consecutive ticks required before committing a transition
    [SerializeField] private float descentVelocityThreshold = -0.5f;  // m/s, apogee/descent detection
    [SerializeField] private float landedAltitudeMargin = 0.3f;       // m above launch altitude
    [SerializeField] private float landedSpeedThreshold = 0.15f;      // m/s
    [SerializeField] private float fuelEpsilon = 0.0005f;             // kg

    [Header("Debug")]
    [SerializeField] private bool showDebugGUI = false;
    private Rigidbody rb;

    private Vector3 prevVelocity;
    private Vector3 accelBiasFixed;
    private float baroBiasFixed;
    private Vector3 gyroBias;

    private float simTimeSeconds;
    private float launchAltitude;
    private FlightPhase currentPhase = FlightPhase.Pad;
    private int phaseConfirmCounter;

    private int accelTicksPerSample, gyroTicksPerSample, baroTicksPerSample;
    private int accelTickCounter, gyroTickCounter, baroTickCounter;

    private DelayLine<Vector3> accelDelayLine;
    private struct GyroSample { public Vector3 rate; public Vector3 bias; }
    private DelayLine<GyroSample> gyroDelayLine;
    private DelayLine<float> baroDelayLine;

    private Vector3 latestRawAccel, latestRawGyro, latestGyroBias;
    private float latestPressure, latestAltitudeEstimate;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rocketSim == null)
            Debug.LogError("SensorSimulator: RocketSim reference not assigned in the Inspector.");
        if (canardController == null)
            Debug.LogError("SensorSimulator: CanardController reference not assigned in the Inspector.");

        if (useFixedSeed)
            Random.InitState(randomSeed);

        accelBiasFixed = GaussianRandom.NextGaussian3(accelBiasStdDev);
        baroBiasFixed = GaussianRandom.NextGaussian(0f, baroBiasStdDev);
        gyroBias = Vector3.zero;

        prevVelocity = rb.linearVelocity;
        launchAltitude = rocketSim != null ? rocketSim.GetAltitude() : transform.position.y;

        accelTicksPerSample = ComputeTicksPerSample(accelSampleRateHz);
        gyroTicksPerSample = ComputeTicksPerSample(gyroSampleRateHz);
        baroTicksPerSample = ComputeTicksPerSample(baroSampleRateHz);
        // Fill delay buffers with initial readings until sampled history is available.
        Vector3 warmAccel = transform.InverseTransformDirection(Vector3.up * (rocketSim != null ? rocketSim.GetGravity() : 9.81f));
        int accelDelaySamples = Mathf.RoundToInt(accelDelaySeconds / (accelTicksPerSample * Time.fixedDeltaTime));
        accelDelayLine = new DelayLine<Vector3>(accelDelaySamples, warmAccel);
        latestRawAccel = warmAccel;

        GyroSample warmGyro = new GyroSample { rate = Vector3.zero, bias = Vector3.zero };
        int gyroDelaySamples = Mathf.RoundToInt(gyroDelaySeconds / (gyroTicksPerSample * Time.fixedDeltaTime));
        gyroDelayLine = new DelayLine<GyroSample>(gyroDelaySamples, warmGyro);
        latestRawGyro = warmGyro.rate;
        latestGyroBias = warmGyro.bias;

        float warmPressure = seaLevelPressure * Mathf.Exp(-launchAltitude / baroScaleHeight);
        int baroDelaySamples = Mathf.RoundToInt(baroDelaySeconds / (baroTicksPerSample * Time.fixedDeltaTime));
        baroDelayLine = new DelayLine<float>(baroDelaySamples, warmPressure);
        latestPressure = warmPressure;
        latestAltitudeEstimate = launchAltitude;
    }

    private int ComputeTicksPerSample(float sampleRateHz)
    {
        return Mathf.Max(1, Mathf.RoundToInt(1f / (sampleRateHz * Time.fixedDeltaTime)));
    }

    void FixedUpdate()
    {
        if (rocketSim == null) return;

        simTimeSeconds += Time.fixedDeltaTime;
        UpdateFlightPhase();
        SampleAccelerometer();
        SampleGyro();
        SampleBarometer();

        prevVelocity = rb.linearVelocity;
    }

    private void SampleAccelerometer()
    {
        if (++accelTickCounter < accelTicksPerSample) return;
        accelTickCounter = 0;
        // Velocity difference over one physics tick; this includes sampling lag.
        Vector3 worldAccel = (rb.linearVelocity - prevVelocity) / Time.fixedDeltaTime;
        // Remove gravity: a supported stationary sensor reads +g, free fall reads zero.
        Vector3 specificForceWorld = worldAccel + Vector3.up * rocketSim.GetGravity();
        Vector3 specificForceBody = transform.InverseTransformDirection(specificForceWorld);

        Vector3 noisy = specificForceBody + accelBiasFixed + GaussianRandom.NextGaussian3(accelNoiseStdDev);
        latestRawAccel = accelDelayLine.PushAndRead(noisy);
    }

    private void SampleGyro()
    {
        if (++gyroTickCounter < gyroTicksPerSample) return;
        gyroTickCounter = 0;

        float dt = gyroTicksPerSample * Time.fixedDeltaTime;
        gyroBias += GaussianRandom.NextGaussian3(gyroBiasDriftStdDev * Mathf.Sqrt(dt)); // Wiener process
        gyroBias.x = Mathf.Clamp(gyroBias.x, -gyroBiasMax, gyroBiasMax);
        gyroBias.y = Mathf.Clamp(gyroBias.y, -gyroBiasMax, gyroBiasMax);
        gyroBias.z = Mathf.Clamp(gyroBias.z, -gyroBiasMax, gyroBiasMax);

        Vector3 trueRateBody = transform.InverseTransformDirection(rb.angularVelocity);
        Vector3 noisy = trueRateBody + gyroBias + GaussianRandom.NextGaussian3(gyroNoiseStdDev);
        // Delay the bias alongside the reading so the logged values correspond.
        GyroSample delayed = gyroDelayLine.PushAndRead(new GyroSample { rate = noisy, bias = gyroBias });
        latestRawGyro = delayed.rate;
        latestGyroBias = delayed.bias;
    }

    private void SampleBarometer()
    {
        if (++baroTickCounter < baroTicksPerSample) return;
        baroTickCounter = 0;

        float trueAltitude = rocketSim.GetAltitude();
        float truePressure = seaLevelPressure * Mathf.Exp(-trueAltitude / baroScaleHeight);
        float noisyPressure = truePressure + baroBiasFixed + GaussianRandom.NextGaussian(0f, baroNoiseStdDev);

        latestPressure = baroDelayLine.PushAndRead(noisyPressure);
        latestAltitudeEstimate = -baroScaleHeight * Mathf.Log(Mathf.Max(latestPressure, 1f) / seaLevelPressure);
    }

    private FlightPhase DetermineCandidatePhase()
    {
        bool engineOn = rocketSim.IsEngineOn();
        float fuel = rocketSim.GetRemainingFuel();
        float altitude = rocketSim.GetAltitude();
        float speed = rocketSim.GetSpeed();
        float vy = rb.linearVelocity.y;

        switch (currentPhase)
        {
            case FlightPhase.Pad:
                return (engineOn && fuel > fuelEpsilon) ? FlightPhase.PoweredAscent : FlightPhase.Pad;

            case FlightPhase.PoweredAscent:
                if (!engineOn || fuel <= fuelEpsilon) return FlightPhase.Coast;
                if (vy < descentVelocityThreshold) return FlightPhase.Descent; // thrust insufficient, already falling
                return FlightPhase.PoweredAscent;

            case FlightPhase.Coast:
                return (vy < descentVelocityThreshold) ? FlightPhase.Descent : FlightPhase.Coast;

            case FlightPhase.Descent:
                return (altitude <= launchAltitude + landedAltitudeMargin && speed < landedSpeedThreshold)
                    ? FlightPhase.Landed : FlightPhase.Descent;

            default: // Landed is terminal
                return FlightPhase.Landed;
        }
    }

    private void UpdateFlightPhase()
    {
        FlightPhase candidate = DetermineCandidatePhase();
        if (candidate == currentPhase) { phaseConfirmCounter = 0; return; }

        if (++phaseConfirmCounter >= phaseConfirmTicks)
        {
            currentPhase = candidate;
            phaseConfirmCounter = 0;
        }
    }

    public SensorFrame GetLatestReading()
    {
        float[] rawCanardAngles = canardController != null ? canardController.GetCanardAngles() : new float[4];
        float[] servo = new float[4];
        for (int i = 0; i < 4 && i < rawCanardAngles.Length; i++)
            servo[i] = rawCanardAngles[i];

        Quaternion q = transform.rotation;

        return new SensorFrame
        {
            timestamp = GetTimestampMillis(),
            state = (int)currentPhase,
            raw_accel = new float[] { latestRawAccel.x, latestRawAccel.y, latestRawAccel.z },
            raw_gyro = new float[] { latestRawGyro.x, latestRawGyro.y, latestRawGyro.z },
            pressure = latestPressure,
            altitude = latestAltitudeEstimate,
            quats = new float[] { q.x, q.y, q.z, q.w },
            servo = servo,
            gyro_bias = new float[] { latestGyroBias.x, latestGyroBias.y, latestGyroBias.z },
        };
    }

    public FlightPhase GetFlightPhase() => currentPhase;
    public int GetTimestampMillis() => Mathf.RoundToInt(simTimeSeconds * 1000f);
    public Vector3 GetRawAccel() => latestRawAccel;
    public Vector3 GetRawGyro() => latestRawGyro;
    public Vector3 GetGyroBias() => latestGyroBias;
    public float GetPressure() => latestPressure;
    public float GetEstimatedAltitude() => latestAltitudeEstimate;

    void OnGUI()
    {
        if (!showDebugGUI) return;

        int y = 200; // offset below CanardController/RocketPIDController HUD rows
        GUI.Label(new Rect(10, y, 320, 20), "--- Sensor Simulator --------");
        GUI.Label(new Rect(10, y + 20, 320, 20), $"Phase: {currentPhase}   t={simTimeSeconds:F2}s");
        GUI.Label(new Rect(10, y + 40, 320, 20), $"Accel (body): {latestRawAccel.x:F2}, {latestRawAccel.y:F2}, {latestRawAccel.z:F2} m/s^2");
        GUI.Label(new Rect(10, y + 60, 320, 20), $"Gyro (body):  {latestRawGyro.x:F3}, {latestRawGyro.y:F3}, {latestRawGyro.z:F3} rad/s");
        GUI.Label(new Rect(10, y + 80, 320, 20), $"Gyro bias:    {latestGyroBias.x:F4}, {latestGyroBias.y:F4}, {latestGyroBias.z:F4} rad/s");
        GUI.Label(new Rect(10, y + 100, 320, 20), $"Baro: {latestPressure:F1} Pa   alt(est)={latestAltitudeEstimate:F2} m");
    }
}
