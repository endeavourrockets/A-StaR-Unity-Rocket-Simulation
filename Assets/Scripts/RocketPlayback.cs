using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static UnityEngine.InputSystem.HID.HID;
using static UnityEngine.UIElements.UxmlAttributeDescription;

public class RocketPlayback : MonoBehaviour
{
    public float playbackSpeed = 1f;
    private List<PlaybackFrame> states;
    private float playbackTime = 0f;

    void Start()
    {
        string csvPath = @"C:\Users\rizzo\Code\VSC-Asus\University Code\Endeavour\new_states.csv";
        if (!File.Exists(csvPath))
        {
            Debug.LogError("CSV file not found at: " + csvPath);
            return;
        }

        string csvText = File.ReadAllText(csvPath);
        states = CSVLoader.LoadStates(csvText);
    }

    void Update()
    {
        if (states == null || states.Count < 2) { return; }

        playbackTime += Time.deltaTime * playbackSpeed;

        // Loop or clamp
        if (playbackTime > states[states.Count - 1].time) { playbackTime = states[0].time; }

        // Find the two states around the current time
        int i = 0;
        while (i < states.Count - 1 && states[i + 1].time < playbackTime) { i++; }

        PlaybackFrame a = states[i];
        PlaybackFrame b = states[Mathf.Min(i + 1, states.Count - 1)]; // Avoid out of range

        float t = Mathf.InverseLerp(a.time, b.time, playbackTime); // Find time in between a and b

        // Interpolate
        transform.position = Vector3.Lerp(a.position, b.position, t);
        transform.rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
    }
}