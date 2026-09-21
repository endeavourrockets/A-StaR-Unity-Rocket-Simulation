using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// Samples the completed physics state before RocketSim queues the next step's forces.
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(RocketSim))]
public class BaselineFlightRecorder : MonoBehaviour
{
    private StreamWriter writer;
    private Rigidbody rb;
    private RocketSim sim;
    private double time;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        sim = GetComponent<RocketSim>();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FlightLogs");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "baseline_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
        writer = new StreamWriter(path);
        writer.WriteLine("time_s,x_m,y_m,z_m,vx_m_s,vy_m_s,vz_m_s,mass_kg,fuel_kg,aoa_rad,previous_step_thrust_n,delivered_impulse_ns,previous_step_aero_x_n,previous_step_aero_y_n,previous_step_aero_z_n");
        Debug.Log("Baseline flight CSV: " + path);
    }

    void FixedUpdate()
    {
        if (writer == null || !sim.enabled) return;
        Vector3 p = rb.position;
        Vector3 v = rb.linearVelocity;
        Vector3 aero = sim.GetLastAeroForce();
        double[] values = { time, p.x, p.y, p.z, v.x, v.y, v.z, rb.mass,
            sim.GetRemainingFuel(), sim.GetAngleOfAttack(), sim.GetLastThrust(),
            sim.GetDeliveredImpulse(), aero.x, aero.y, aero.z };
        writer.WriteLine(string.Join(",", Array.ConvertAll(values, x => x.ToString("R", CultureInfo.InvariantCulture))));
        time += Time.fixedDeltaTime;
    }

    void OnDisable()
    {
        writer?.Dispose();
        writer = null;
    }
}
