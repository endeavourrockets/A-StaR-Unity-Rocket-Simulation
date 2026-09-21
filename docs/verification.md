# Verification

Planned checks and recorded comparison results. Follow these steps before submitting a PR and add the latest results.

## Clean-clone run

- Clone into a new directory and open the project root in Unity.
- Allow dependencies/imports to complete. Record any compilation errors.
- Enter Play mode and make sure the scene doesn't throw exceptions.
- Exit Play mode and confirm both JSONL files aren't empty but readable. Check no NaN values are registered.
- Check flightlogs and cache don't get added to Git. If they do, check gitignore.

## Reference comparison

Use OpenRocket's static flight of A-StaR's rocket for reference (`Assets\CFD Data\OR_Unity_comparison_data.csv`). Check all parameters match, including: motor, atmosphere, recovery, etc. If a different flight/rocket was used for comparison, label it somewhere.

## PR Experiment template

Template PR experiment (for future use):

- Date and person:
- Git commit and any uncommitted changes:
- Unity version, OS:
- Scene and configuration changes:
- Physics timestep, sensor/controller settings, random seed:
- Question, expected result, and preselected tolerance:
- Procedure:
- Observed result and relevant plots/logs:
- Pass/fail/not run:
- Limitations and next action:

## Latest status (21-09-2026)

Compared `FlightLogs/baseline_20260921_151312_270.csv` with `Assets/CFD Data/OR_Unity_comparison_data.csv`. Comparison calculations are handled by `tool/compare_flights.py`.

Launch settings used: Cesaroni 266H125 motor, sea-level launch, no wind, parachute disabled.

| Measurement | Result |
| --- | ---: |
| Unity apogee above initial position | 1067.0863 m |
| OpenRocket apogee above initial position | 1013.4970 m |
| Apogee difference | +53.5893 m (~5.29%) |
| Altitude MSE | 1141.9488 m^2 |
| Altitude RMSE | 33.7927 m |
| Maximum absolute altitude error in comparison window | 53.1572 m |
| Apogee time difference | +0.2900 s |
| Comparison window | 0–12.44 s |
| Samples / resampling interval | 623 / 0.02 s |

No tolerances have been set yet for pass/fail criteria Thrust, mass, velocity, and drag stuill need to be compared, which is not handled by the current script.