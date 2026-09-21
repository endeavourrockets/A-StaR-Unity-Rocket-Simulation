using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RocketPhysicsChecks
{
    private static int checks;
    private static void Check(bool pass, string description)
    {
        if (!pass) throw new Exception("Physics check failed: " + description);
        checks++;
    }

    [MenuItem("Tools/Rocket/Run Physics Checks")]
    public static void Run()
    {
        checks = 0;
        Vector3 axis = new Vector3(0.1f, 1, 0).normalized;
        RocketPhysics.BodyForces(Vector3.up * 100, Vector3.up, 1.225f, 0.00358f,
            0.505f, 2, out Vector3 axial, out Vector3 normal);
        Check((axial - Vector3.down * 11.0733875f).magnitude < 1e-4f && normal == Vector3.zero,
            "zero-AoA drag equals q A CA and points down");
        RocketPhysics.BodyForces(Vector3.up * 100, axis, 1.225f, 0.00358f,
            0.505f, 2, out axial, out normal);
        Check(Mathf.Abs(Vector3.Dot(normal, axis)) < 1e-5f, "normal force is body-normal");
        Check(Vector3.Dot(axial + normal, Vector3.up * 100) <= 0, "aero removes relative kinetic energy");
        Check(Vector3.Cross(-axis * 0.158f, normal).z > 0, "CP aft of CG restores positive nose-right tilt");
        Check(Vector3.Cross(axis * 0.158f, normal).z < 0, "CP ahead of CG destabilises the same tilt");
        Vector3 arm = Vector3.down * 0.158f;
        Vector3 omega = Vector3.forward;
        RocketPhysics.BodyForces(Vector3.Cross(omega, arm), Vector3.up, 1.225f, 0.00358f,
            0.505f, 2, out Vector3 dampingAxial, out Vector3 dampingNormal);
        Check(Vector3.Dot(Vector3.Cross(arm, dampingAxial + dampingNormal), omega) < 0,
            "CP point velocity produces opposing rotational torque");
        RocketPhysics.BodyForces(Vector3.up * 100, new Vector3(-axis.x, axis.y, 0), 1.225f, 0.00358f,
            0.505f, 2, out Vector3 mirroredAxial, out Vector3 mirroredNormal);
        Check(Mathf.Abs(normal.x + mirroredNormal.x) < 1e-5f && Mathf.Abs(normal.y - mirroredNormal.y) < 1e-5f,
            "opposite angles mirror the force");
        Quaternion rotation = Quaternion.Euler(35, 62, -17);
        RocketPhysics.BodyForces(rotation * (Vector3.up * 100), rotation * axis, 1.225f, 0.00358f,
            0.505f, 2, out Vector3 rotatedAxial, out Vector3 rotatedNormal);
        Check((rotatedAxial - rotation * axial).magnitude < 1e-4f
            && (rotatedNormal - rotation * normal).magnitude < 1e-4f, "world-frame rotation preserves forces");
        RocketPhysics.BodyForces(Vector3.zero, axis, 1.225f, 0.00358f, 0.505f, 2, out axial, out normal);
        Check(axial == Vector3.zero && normal == Vector3.zero, "zero relative airflow gives zero force");
        RocketPhysics.BodyForces(Vector3.down * 100, Vector3.up, 1.225f, 0.00358f, 0.505f, 2, out axial, out normal);
        Check(axial.y > 0 && normal == Vector3.zero, "reverse axial flow opposes descent");
        Check(Mathf.Abs(RocketPhysics.CentreOfGravity(1, 1, 0.5f, 0, 0.5f) - 1f / 3f) < 1e-6,
            "CG follows mass moments rather than linear position interpolation");
        Keyframe[] triangle = { new Keyframe(0, 0), new Keyframe(1, 10), new Keyframe(2, 0) };
        Check(Math.Abs(RocketPhysics.Impulse(triangle, -1, 3) - 10) < 1e-6, "triangle impulse is analytic area");
        Check(Math.Abs(RocketPhysics.Impulse(triangle, 0.5, 1.5) - 7.5) < 1e-6, "partial motor intervals integrate exactly");
        bool rejected = false;
        try { RocketPhysics.ValidateMotor(new[] { new Keyframe(0, 0), new Keyframe(0, 1) }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "duplicate motor times are rejected");

        Keyframe[] h125 = LoadH125();
        RocketPhysics.ValidateMotor(h125);
        double impulse = RocketPhysics.Impulse(h125, 0, 100);
        Check(Math.Abs(impulse - 268.45512525) < 0.001, "H125 source impulse");
        double sum = 0;
        for (double t = 0; t < 3; t += 0.07) sum += RocketPhysics.Impulse(h125, t, t + 0.07);
        Check(Math.Abs(sum - impulse) < 1e-7, "coarse timesteps preserve full impulse including final partial step");

        float a = Flight(h125, 0.02f);
        float b = Flight(h125, 0.01f);
        float c = Flight(h125, 0.005f);
        Check(Mathf.Abs(b - c) < Mathf.Abs(a - b), "apogee converges as physics timestep halves");
        Check(Mathf.Abs(b - c) / c < 0.01f, "last apogee timestep change is below one percent");
        Debug.Log($"PASS: {checks} physics checks. H125 impulse {impulse:F6} N s. Free-flight apogees: dt .02={a:F3}, .01={b:F3}, .005={c:F3} m. These are regression results, not OpenRocket validation.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static Keyframe[] LoadH125()
    {
        List<Keyframe> samples = new List<Keyframe> { new Keyframe(0, 0) };
        string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "CFD Data/Cesaroni_266H125-12A.eng"));
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith(";")) continue;
            string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            samples.Add(new Keyframe(float.Parse(columns[0], CultureInfo.InvariantCulture), float.Parse(columns[1], CultureInfo.InvariantCulture)));
        }
        return samples.ToArray();
    }

    private static float Flight(Keyframe[] samples, float dt)
    {
        float originalDt = Time.fixedDeltaTime;
        Scene scene = SceneManager.CreateScene("Physics check", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            Time.fixedDeltaTime = dt;
            GameObject go = new GameObject("Passive reference");
            SceneManager.MoveGameObjectToScene(go, scene);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            RocketSim sim = go.AddComponent<RocketSim>();
            SerializedObject settings = new SerializedObject(sim);
            settings.FindProperty("thrustCurve").animationCurveValue = new AnimationCurve(samples);
            settings.ApplyModifiedPropertiesWithoutUndo();
            sim.SendMessage("Start");
            Check((rb.inertiaTensor - new Vector3(0.4298f, 0.0005183f, 0.4298f)).magnitude < 1e-6,
                "configured principal moments reach Rigidbody");
            // Wind moving with the body must give zero AoA, irrespective of ground speed.
            settings.Update();
            settings.FindProperty("windVelocity").vector3Value = Vector3.right * 5;
            settings.ApplyModifiedPropertiesWithoutUndo();
            rb.linearVelocity = Vector3.right * 5;
            Check(sim.GetAngleOfAttack() == 0, "AoA uses velocity minus wind");
            rb.linearVelocity = Vector3.zero;
            settings.Update(); settings.FindProperty("windVelocity").vector3Value = Vector3.zero;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PhysicsScene physics = scene.GetPhysicsScene();
            float apogee = 0;
            for (int i = 0; i < Mathf.CeilToInt(30 / dt); i++)
            {
                sim.SendMessage("FixedUpdate");
                physics.Simulate(dt);
                apogee = Mathf.Max(apogee, rb.position.y);
                if (i * dt > 3 && rb.linearVelocity.y < 0) break;
            }
            Check(sim.GetRemainingFuel() == 0 && !sim.IsEngineOn(), "H125 burns all specified propellant and stops");
            Check(Math.Abs(sim.GetDeliveredImpulse() - 268.45512525) < 0.001, "runtime delivers full H125 impulse");
            Check(rb.mass == 0.785f && Mathf.Abs(rb.centerOfMass.y - (1.19f - 0.675f)) < 1e-5,
                "burnout mass and CG match dry endpoints");
            Check(rb.position.x == 0 && rb.position.z == 0 && rb.angularVelocity.sqrMagnitude < 1e-10,
                "vertical passive flight has no lateral force or torque");
            rb.rotation = Quaternion.identity;
            rb.angularVelocity = Vector3.zero;
            rb.AddTorque(Vector3.right * 0.004298f, ForceMode.Impulse);
            physics.Simulate(0.0001f);
            Check(Mathf.Abs(rb.angularVelocity.x - 0.01f) < 1e-4,
                "angular impulse about X uses the transverse moment");
            rb.rotation = Quaternion.identity;
            rb.angularVelocity = Vector3.zero;
            rb.AddTorque(Vector3.up * 0.000005183f, ForceMode.Impulse);
            physics.Simulate(0.0001f);
            Check(Mathf.Abs(rb.angularVelocity.y - 0.01f) < 1e-4,
                "angular impulse about Y uses the roll moment");
            return apogee;
        }
        finally
        {
            Time.fixedDeltaTime = originalDt;
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
