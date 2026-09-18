using System;
using UnityEngine;

// SI units. Velocity is the rocket's motion relative to the air, not incoming airflow.
public static class RocketPhysics
{
    public static void BodyForces(Vector3 airVelocity, Vector3 axis, float density,
        float area, float axialCoefficient, float normalSlope,
        out Vector3 axialForce, out Vector3 normalForce)
    {
        axis = axis.normalized;
        float axialSpeed = Vector3.Dot(airVelocity, axis);
        Vector3 transverseVelocity = airVelocity - axialSpeed * axis;
        float qArea = 0.5f * density * airVelocity.sqrMagnitude * area;
        axialForce = -axis * Mathf.Sign(axialSpeed) * axialCoefficient * qArea;
        if (Mathf.Abs(axialSpeed) < 1e-6f) axialForce = Vector3.zero;

        // Small-angle CN = CNalpha * alpha. Opposes motion across the body axis.
        // The bounded continuation beyond small angles is not a validated stall model.
        float alpha = Mathf.Atan2(transverseVelocity.magnitude, Mathf.Abs(axialSpeed));
        normalForce = -transverseVelocity.normalized * normalSlope * alpha * qArea;
    }

    public static float CentreOfGravity(float dryMass, float initialFuel, float fuel,
        float emptyCG, float fullCG)
    {
        if (initialFuel <= 0f) return emptyCG;
        // Infer a fixed propellant centroid from the full and empty mass moments.
        float fuelCG = ((dryMass + initialFuel) * fullCG - dryMass * emptyCG) / initialFuel;
        return (dryMass * emptyCG + fuel * fuelCG) / (dryMass + fuel);
    }

    public static void ValidateMotor(Keyframe[] keys)
    {
        if (keys == null || keys.Length < 2) throw new ArgumentException("Motor needs at least two samples.");
        if (keys[0].time != 0f) throw new ArgumentException("Motor curve must start at t=0.");
        for (int i = 0; i < keys.Length; i++)
        {
            if (float.IsNaN(keys[i].time) || float.IsInfinity(keys[i].time)
                || float.IsNaN(keys[i].value) || float.IsInfinity(keys[i].value)
                || keys[i].value < 0f)
                throw new ArgumentException("Motor samples must be finite and thrust nonnegative.");
            if (i > 0 && keys[i].time <= keys[i - 1].time)
                throw new ArgumentException("Motor timestamps must strictly increase.");
        }
        if (keys[keys.Length - 1].value != 0f)
            throw new ArgumentException("Motor curve must end at zero thrust.");
    }

    // Integrate the piecewise-linear source samples, including partial timesteps.
    // AnimationCurve tangents are deliberately ignored, so the importer and solver agree.
    public static double Impulse(Keyframe[] keys, double start, double end)
    {
        double impulse = 0;
        for (int i = 1; i < keys.Length; i++)
        {
            double left = Math.Max(start, keys[i - 1].time);
            double right = Math.Min(end, keys[i].time);
            if (right <= left) continue;
            double slope = (keys[i].value - keys[i - 1].value) / (double)(keys[i].time - keys[i - 1].time);
            double a = keys[i - 1].value + slope * (left - keys[i - 1].time);
            double b = keys[i - 1].value + slope * (right - keys[i - 1].time);
            impulse += 0.5 * (a + b) * (right - left);
        }
        return impulse;
    }
}
