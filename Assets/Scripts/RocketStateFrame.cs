// Ground-truth flight state for one timestep, exported as JSON Lines alongside
// SensorFrame (shared `timestamp` clock so the two logs can be joined in Python).
// Kept separate from PlaybackFrame.cs, which is used by the existing CSV playback feature
// (CSVLoader/RocketPlayback) and has a different, fixed field layout.
[System.Serializable]
public class RocketStateFrame
{
    public int timestamp;             // ms, same clock as SensorFrame
    public int state;                 // SensorSimulator.FlightPhase code, ground truth (no hysteresis delay)

    public float[] position;          // world position [x,y,z]
    public float[] velocity;          // world linear velocity [x,y,z]
    public float[] quats;             // orientation [x,y,z,w], ground truth
    public float[] angular_velocity;  // world angular velocity [x,y,z], rad/s

    public float mass;                // kg, current total mass
    public float fuel_remaining;      // kg
    public float altitude;            // m, ground truth
    public float speed;               // m/s, ground truth
    public float angle_of_attack;     // radians

    public float[] canard_angles;     // degrees, 4 elements
    public bool engine_on;
}
