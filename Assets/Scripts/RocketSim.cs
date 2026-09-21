using System;
using UnityEngine;

/*
 * Local +Y points towards the nose. All distances and forces use SI units.
 * CG and CP are measured from the nose; the model pivot is at the base.
 * This is a fixed-coefficient, small-angle model, not OpenRocket's full aero model.
 */
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public class RocketSim : MonoBehaviour
{
    [Header("Mass Properties")]
    [SerializeField] private float dryMass = 0.785f; // Includes the empty motor, kg
    [SerializeField] private float startFuelMass = 0.138f;
    [SerializeField] private float fuelMass = 0.138f;
    [SerializeField] private Vector3 mOfInertia = new Vector3(0.4298f, 5.183e-4f, 0.4298f);
    // Principal moments about CG, kg m^2. Held constant until full/empty data are available.

    [Header("Geometry")]
    [SerializeField] private float rocketLength = 1.19f;
    [SerializeField] private float rocketDiameter = 0.0675f;
    [SerializeField] private float cOfPressure = 0.916f;
    [SerializeField] private float cOfGravity = 0.758f;
    [SerializeField] private float cOfGravityEmpty = 0.675f;
    [SerializeField] private float cOfGravityFull = 0.758f;

    [Header("Aerodynamic Coefficients")]
    [SerializeField] private float cAxial = 0.505f; // Zero-AoA CD used as a constant CA approximation
    [SerializeField] private float cNormalSlope = 2f; // per radian; needs whole-rocket reference data
    [SerializeField] private float referenceArea = 0.00358f; // m^2

    [Header("Thrust Curve")]
    [SerializeField] private AnimationCurve thrustCurve; // N versus seconds; piecewise-linear samples

    [Header("Environment")]
    [SerializeField] private Vector3 windVelocity = Vector3.zero; // Air motion in world coordinates, m/s
    [SerializeField] private float seaLevelDensity = 1.225f; // kg/m^3
    [SerializeField] private float densityScaleHeight = 8500f; // m
    [SerializeField] private float gravity = 9.81f;

    [Header("Control")]
    [SerializeField] private bool engineOn = true;
    [SerializeField] private float startingAngle = 0f; // degrees from initial orientation
    [SerializeField] private bool passiveFlight = true; // Neutral canards; PID and manual actuation disabled
    [SerializeField] private CanardController canardController;

    private Rigidbody rb;
    private Keyframe[] motorSamples;
    private double totalImpulse;
    private double deliveredImpulse;
    private double motorTime;
    private float lastThrust;
    private Vector3 lastAeroForce;
    private bool warnedLargeAngle;
    private float canardsHeight = 0.9f;
    private float canardArea = 0.0001f; // Legacy control model; not validated

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // Gravity is applied below, exactly once.
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        transform.rotation *= Quaternion.Euler(0, 0, startingAngle);
        fuelMass = startFuelMass;
        motorTime = 0;
        deliveredImpulse = 0;
        if (dryMass <= 0 || startFuelMass < 0 || referenceArea <= 0 || densityScaleHeight <= 0
            || mOfInertia.x <= 0 || mOfInertia.y <= 0 || mOfInertia.z <= 0)
        {
            Debug.LogError("RocketSim: mass, area, scale height and principal moments must be positive.", this);
            enabled = false;
            return;
        }
        if (startFuelMass > 0)
        {
            float fuelCG = ((dryMass + startFuelMass) * cOfGravityFull - dryMass * cOfGravityEmpty) / startFuelMass;
            if (fuelCG < 0 || fuelCG > rocketLength)
                Debug.LogWarning($"RocketSim: full/empty CG values imply a propellant centre {fuelCG:F3} m from the nose, outside the {rocketLength:F3} m body. Verify the mass and CG inputs.", this);
        }
        motorSamples = thrustCurve != null ? thrustCurve.keys : new Keyframe[0];
        if (engineOn || motorSamples.Length >= 2)
        {
            try
            {
                RocketPhysics.ValidateMotor(motorSamples);
                totalImpulse = RocketPhysics.Impulse(motorSamples, 0, motorSamples[motorSamples.Length - 1].time);
                if (totalImpulse <= 0 || startFuelMass <= 0) throw new ArgumentException("Motor needs positive impulse and propellant mass.");
                Debug.Log($"RocketSim motor: {totalImpulse:F3} N s, {motorSamples[motorSamples.Length - 1].time:F3} s; effective Isp {totalImpulse / (startFuelMass * 9.80665):F2} s.", this);
            }
            catch (ArgumentException e)
            {
                Debug.LogError("RocketSim: " + e.Message, this);
                enabled = false;
                return;
            }
        }
        if (passiveFlight)
        {
            RocketPIDController pid = GetComponent<RocketPIDController>();
            if (pid != null) pid.enabled = false;
            if (canardController != null) canardController.SetPassiveMode();
        }
        UpdateMass();
    }

    void FixedUpdate()
    {
        // Burn the interval's propellant before setting the mass used for this physics step.
        // This is a first-order mass approximation; check convergence by halving dt.
        lastThrust = StepMotor(Time.fixedDeltaTime);
        UpdateMass();
        rb.AddForce(Vector3.down * gravity * rb.mass);
        rb.AddForce(transform.up * lastThrust);
        ApplyAeroForces();
    }

    void UpdateMass()
    {
        rb.mass = dryMass + fuelMass;
        cOfGravity = RocketPhysics.CentreOfGravity(dryMass, startFuelMass, fuelMass,
            cOfGravityEmpty, cOfGravityFull);
        rb.centerOfMass = new Vector3(0, rocketLength - cOfGravity, 0);
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.inertiaTensor = mOfInertia; // Local X/Z transverse, local Y roll. Values still need verification.
    }

    float StepMotor(float dt)
    {
        if (!engineOn || totalImpulse <= 0) return 0;
        double impulse = RocketPhysics.Impulse(motorSamples, motorTime, motorTime + dt);
        motorTime += dt;
        deliveredImpulse += impulse;
        fuelMass = startFuelMass * (float)Math.Max(0, 1 - deliveredImpulse / totalImpulse);
        if (motorTime >= motorSamples[motorSamples.Length - 1].time)
        {
            fuelMass = 0;
            engineOn = false;
        }
        return (float)(impulse / dt);
    }

    void ApplyAeroForces()
    {
        Vector3 cp = transform.TransformPoint(new Vector3(0, rocketLength - cOfPressure, 0));
        // Point velocity includes rotation. This supplies approximate damping at the lumped CP.
        Vector3 relVelocity = rb.GetPointVelocity(cp) - windVelocity;
        RocketPhysics.BodyForces(relVelocity, transform.up, GetAirDensity(), referenceArea,
            cAxial, cNormalSlope, out Vector3 axialForce, out Vector3 normalForce);
        lastAeroForce = axialForce + normalForce;
        rb.AddForceAtPosition(lastAeroForce, cp);
        if (!warnedLargeAngle && relVelocity.sqrMagnitude > 1 && GetAngleOfAttack() > 20 * Mathf.Deg2Rad)
        {
            Debug.LogWarning("RocketSim: AoA exceeds 20 degrees; the small-angle aero model is outside its intended range.", this);
            warnedLargeAngle = true;
        }
        if (!passiveFlight) ApplyLegacyCanardForces(relVelocity);
    }

    // Retained for later controller work. Do not use for the passive reference comparison.
    void ApplyLegacyCanardForces(Vector3 relVelocity)
    {
        if (canardController == null || !canardController.isActiveAndEnabled) return;
        float[] canardAngles = canardController.GetCanardAngles();
        if (canardAngles == null || canardAngles.Length < 4) return;
        float dynamicPressure = 0.5f * GetAirDensity() * relVelocity.sqrMagnitude;
        Vector3 canardForce;
        float canardTempCoeff = cNormalSlope * canardArea * dynamicPressure;
        if (Mathf.Abs(canardAngles[0]) > 0.01f)
        {
            //Debug.Log("Canard lift");
            Vector3 normalDir = Quaternion.AngleAxis(canardAngles[0], transform.forward) * transform.right; // normal in world coords
            canardForce = normalDir * canardTempCoeff * Mathf.Sin(canardAngles[0] * Mathf.Deg2Rad);
            rb.AddForceAtPosition(canardForce, transform.TransformPoint(new Vector3(rocketDiameter / 2f, canardsHeight, 0)));
        }
        if (Mathf.Abs(canardAngles[1]) > 0.01f)
        {
            Vector3 normalDir = Quaternion.AngleAxis(canardAngles[1], transform.right) * transform.forward; // normal in world coords
            //Vector3 normalDir = -canardTransforms[1].right;
            canardForce = normalDir * canardTempCoeff * Mathf.Sin(canardAngles[1] * Mathf.Deg2Rad);
            rb.AddForceAtPosition(canardForce, transform.TransformPoint(new Vector3(0, canardsHeight, rocketDiameter / 2f)));
        }
        if (Mathf.Abs(canardAngles[2]) > 0.01f)
        {
            Vector3 normalDir = Quaternion.AngleAxis(-canardAngles[2], transform.forward) * transform.right; // normal in world coords
            //Vector3 normalDir = -canardTransforms[2].up;
            canardForce = normalDir * canardTempCoeff * Mathf.Sin(canardAngles[2] * Mathf.Deg2Rad);
            rb.AddForceAtPosition(canardForce, transform.TransformPoint(new Vector3(-rocketDiameter / 2f, canardsHeight, 0)));
        }
        if (Mathf.Abs(canardAngles[3]) > 0.01f)
        {
            Vector3 normalDir = Quaternion.AngleAxis(-canardAngles[3], transform.right) * transform.forward; // normal in world coords
            //Vector3 normalDir = canardTransforms[3].right;
            canardForce = normalDir * canardTempCoeff * Mathf.Sin(canardAngles[3] * Mathf.Deg2Rad);
            rb.AddForceAtPosition(canardForce, transform.TransformPoint(new Vector3(0, canardsHeight, -rocketDiameter / 2f)));
        }

    }

    public void IgniteEngine() { engineOn = totalImpulse > 0 && fuelMass > 0; }
    public void ShutdownEngine() { engineOn = false; }
    public float GetRemainingFuel() => fuelMass;
    public float GetAngleOfAttack()
    {
        Vector3 velocity = rb.linearVelocity - windVelocity;
        return velocity.sqrMagnitude < 1e-8f ? 0f : Vector3.Angle(transform.up, velocity) * Mathf.Deg2Rad;
    }
    public float GetAirDensity() => seaLevelDensity * Mathf.Exp(-GetAltitude() / densityScaleHeight);
    public float GetSpeed() => rb.linearVelocity.magnitude;
    public float GetAltitude() => transform.position.y;
    public bool IsEngineOn() => engineOn;
    public float GetGravity() => gravity;
    public float GetLastThrust() => lastThrust;
    public Vector3 GetLastAeroForce() => lastAeroForce;
    public double GetDeliveredImpulse() => deliveredImpulse;

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || rb == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.TransformPoint(new Vector3(0, rocketLength - cOfPressure, 0)), 0.02f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(rb.worldCenterOfMass, 0.02f);
    }
}
