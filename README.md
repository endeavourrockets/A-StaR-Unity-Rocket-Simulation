# A-StaR Unity Rocket Simulation

A prototype simulation for A-StaR's rocket. The simulation aims to tackle dynamic canard actuation with reasonable realism and sensor logging, aiming to provide finer PID tuning or improved control algorithms.

![Flight Screenshot](Images/flight0.png)

## Opening the project

1. Install Unity Hub and Unity Editor (Unity 6), matching `ProjectSettings/ProjectVersion.txt` for the current Unity version.
2. Clone this repo:
   ```sh
   git clone https://github.com/endeavourrockets/A-StaR-Unity-Rocket-Simulation.git
   ```
3. Open Unity Hub and add the cloned project directory (**Add** -> **Add project from disk**). Alternatively, skip step 2 and add the project directly from this repository (**Add** -> **Add project from repository**).
4. Open with the matching Unity version. Wait for packages, libraries, and shaders to sort themselves out. Normally, this can take up to 5 minutes on the first run.
5. Once loaded in, **File** -> **Open Scene** -> `Assets/Scenes/RocketScene.unity`. This is imporant as otherwise you won't have anything in your editor. Check the Console for compilation errors before entering Play mode.

By this point you should be in the editor with a view of the rocket and landscape. Let me know if anything breaks. A more detailed visual guide about usage and features can be found under [usage](docs/usage.md).

## Useful features and sim parameters

- Selecting **TheRocket** allows you to change different configuration parameters directly through Unity's UI editor in the **Inspector**. You can inspect the **RocketSim** component for mass, geometry, thrust, wind, gravity settings, etc... Whatever values you assign through the inspector will override the hardcoded values int the C# scripts. This is standard in Unity for debugging, and any values you alter in the Inspector will be saved for other people too.
- For active stabilisation keep **Rocket PID Controller** enabled and CanardController's (**3_Canards** object under **TheRocket**) **Allow Manual Input** disabled. For manual control, do the reverse.
- FlightRecorder writes paired `flight_<date>_<time>_truth.jsonl` and `flight_<date>_<time>_sensors.jsonl` files into `FlightLogs/`. The Console reports their paths. Generated logs are ignored by Git.
- There are extra camera behaviours in `Assets/Scripts/CameraScripts/`.

If references are missing, inspect RocketSim, RocketPIDController, SensorSimulator, FlightRecorder, and CanardController. Unity relies on drag and drop for referencing, which is handled through the `ProjectSettings/` directory. This means that any new values or parameters you assign get committed for other users. Most Monobehavaiour scripts are attached to the main **TheRocket** component, or the **3_Canards** child.

## What currently exists

| Feature | Current status |
| --- | --- |
| Rigidbody flight, thrust, mass change, basic aerodynamics, partially dynamic CoG/CoP | Implemented. |
| Four-canard commands and PID | Implemented. Sensor timing still needs to be connected. |
| Accelerometer, gyro, barometer noise and delay | Implemented. Not connected to PID. |
| Truth and sensor JSONL recording | Implemented. |
| CSV trajectory renderer | Original code used solely for rendering on a precomputed path. It relies on a hardcoded path but it should still work. |
| Parachute ejection | There as placeholders to be implemented. |

[model assumptions and limitations](docs/model.md) contains some key assumptions made to keep things simple for the time being. The aerodynamics use a simplified model and sensors are treated separately but not completely emulated to the hardware level.

## Documentation

- [Contributing and Git workflow](CONTRIBUTING.md)
- [Architecture and code map](docs/architecture.md)
- [Model assumptions and data conventions](docs/model.md)
- [Verification and first-run checklist](docs/verification.md)
- [Usage and features](docs/usage.md)

Some of these are currently prototype. Whether or not they will be used in the future is debatable.

## Repo contents

Assets (including .meta files), Packages, and ProjectSettings. Unity caches, IDE folders, builds, FlightLogs, and Temp files are excluded. Use issues for tasks and proposed features. Documentation describes the current implementation. Use the development branch to merge changes onto main. 

## TODO
- [ ] PID uses true attitude and angular rate, not simulated sensor readings.
- [ ] Parachute launch not implemented.
- [ ] Old playback/rendering code needs to be modified.
- [ ] More comparisons need to be run to validate dynamic canard actuation.
- [ ] Usability tests.


## Potential future features and restructuring 
- Reorganise Assets/Scripts while maintaining inspector references.
- Add ML based controls and landing approaches.
- TVC integration.
- In-built CFD computations for static coefficients.
- Separate rendering scripts from computation scripts.