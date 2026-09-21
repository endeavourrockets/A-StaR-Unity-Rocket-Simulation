using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

public static class CSVLoader
{
    public static List<PlaybackFrame> LoadStates(string csvText)
    {
        var states = new List<PlaybackFrame>();
        var lines = csvText.Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split(',');

            float t = float.Parse(cols[0]);

            float px = float.Parse(cols[1]);
            float py = float.Parse(cols[3]);
            float pz = float.Parse(cols[2]);

            float qx = float.Parse(cols[7]);
            float qy = float.Parse(cols[8]);
            float qz = float.Parse(cols[9]);
            float qw = float.Parse(cols[10]);

            float a1 = float.Parse(cols[14]);
            float a2 = float.Parse(cols[15]);
            float a3 = float.Parse(cols[16]);
            float a4 = float.Parse(cols[17]);

            

            Vector3 pos = new Vector3(px, py, pz);
            Quaternion rot = new Quaternion(qx, qy, qz, qw) * Quaternion.Euler(90, 0, 0); // Prefab rocket points horizontally. Adjusting to point upwards.
            float[] c_angles = new float[] {a1, a2, a3, a4};

            states.Add(new PlaybackFrame(t, pos, rot, c_angles));
        }

        return states;
    }
}