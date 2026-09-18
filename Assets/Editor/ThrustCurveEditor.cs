using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Globalization;

[CustomEditor(typeof(ThrustCurveAsset))]
public class ThrustCurveEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ThrustCurveAsset asset = (ThrustCurveAsset)target;

        if (GUILayout.Button("Import CSV"))
        {
            if (asset.csvFile == null)
            {
                Debug.LogError("No CSV file assigned.");
                return;
            }

            string[] lines = asset.csvFile.text.Split('\n');
            List<Keyframe> keys = new List<Keyframe>();

            try
            {
                // Two columns: seconds and newtons. Accept whitespace, comma or semicolon.
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) throw new FormatException("Expected two columns: time (s), thrust (N).");
                    keys.Add(new Keyframe(float.Parse(parts[0], CultureInfo.InvariantCulture),
                        float.Parse(parts[1], CultureInfo.InvariantCulture)));
                }
                if (keys.Count > 0 && keys[0].time > 0) keys.Insert(0, new Keyframe(0, 0));
                RocketPhysics.ValidateMotor(keys.ToArray());
                AnimationCurve curve = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < keys.Count; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                Undo.RecordObject(asset, "Import thrust curve");
                asset.curve = curve;
                EditorUtility.SetDirty(asset);
                Debug.Log($"Thrust curve imported: {RocketPhysics.Impulse(curve.keys, 0, keys[keys.Count - 1].time):F3} N s.");
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException)
            {
                Debug.LogError("Thrust curve not changed: " + e.Message);
            }
        }
    }
}
