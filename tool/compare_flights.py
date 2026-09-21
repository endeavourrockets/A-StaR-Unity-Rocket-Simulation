"""Compare the baseline CSV with a numeric OpenRocket CSV export (all SI units).

Example:
python tool/compare_flights.py FlightLogs/baseline.csv reference.csv \
    --or-time-col 0 --or-altitude-col 1

Default window: common ascent, ending at the earlier apogee. Initial positions
are subtracted from both runs; timestamps are not fitted or shifted.
"""
import argparse
import bisect
import csv
import json
import math
from pathlib import Path


def validate(rows):
    if len(rows) < 2:
        raise ValueError("Need at least two samples.")
    if not all(math.isfinite(v) for row in rows for v in row):
        raise ValueError("Samples must be finite.")
    if any(b[0] <= a[0] for a, b in zip(rows, rows[1:])):
        raise ValueError("Timestamps must strictly increase; check duplicate export rows.")
    return rows


def read_unity(path, spatial):
    with open(path, encoding="utf-8-sig", newline="") as stream:
        fields = ["x_m", "y_m", "z_m"] if spatial else ["y_m"]
        return validate([tuple(float(row[k]) for k in ["time_s"] + fields)
                         for row in csv.DictReader(stream)])


def read_or(path, columns, delimiter=","):
    rows = []
    with open(path, encoding="utf-8-sig", newline="") as stream:
        for line in stream:
            if not line.strip() or line.lstrip().startswith(("#", ";")):
                continue
            try:
                fields = next(csv.reader([line], delimiter=delimiter))
                row = tuple(float(fields[i]) for i in columns)
            except (ValueError, IndexError):
                if rows:
                    raise ValueError("Non-numeric or missing columns after OpenRocket data began.")
                continue  # Optional un-commented column headings.
            rows.append(row)
    return validate(rows)


def interpolate(rows, times, t):
    if t < times[0] or t > times[-1]:
        raise ValueError("Comparison would extrapolate outside the available data.")
    i = min(max(bisect.bisect_right(times, t) - 1, 0), len(rows) - 2)
    a, b = rows[i:i + 2]
    fraction = (t - a[0]) / (b[0] - a[0])
    return [x + fraction * (y - x) for x, y in zip(a[1:], b[1:])]


def compare(unity, reference, dt=0.02, end=None):
    validate(unity)
    validate(reference)
    if len(unity[0]) != len(reference[0]):
        raise ValueError("Coordinate dimensions differ.")
    if not math.isfinite(dt) or dt <= 0:
        raise ValueError("Comparison timestep must be positive and finite.")
    altitude_index = 2 if len(unity[0]) == 4 else 1
    ua = max(unity, key=lambda r: r[altitude_index])
    ra = max(reference, key=lambda r: r[altitude_index])
    start = max(unity[0][0], reference[0][0])
    if end is None:
        end = min(ua[0], ra[0])
    end = min(end, unity[-1][0], reference[-1][0])
    if not math.isfinite(end) or end <= start:
        raise ValueError("No common comparison interval; check ignition timestamps and exports.")
    origins = (unity[0][1:], reference[0][1:])
    ut, rt = [r[0] for r in unity], [r[0] for r in reference]
    samples = []
    for i in range(math.floor((end - start) / dt) + 1):
        t = start + i * dt
        u, r = interpolate(unity, ut, t), interpolate(reference, rt, t)
        error = [(a - u0) - (b - r0) for a, b, u0, r0 in zip(u, r, *origins)]
        samples.append([t] + error)
    mse = sum(sum(e * e for e in row[1:]) for row in samples) / len(samples)
    metrics = {
        "comparison": "3D squared distance" if len(unity[0]) == 4 else "altitude only",
        "window_start_s": start, "window_end_s": samples[-1][0], "samples": len(samples),
        "mse_m2": mse, "rmse_m": math.sqrt(mse),
        "max_error_m": max(math.sqrt(sum(e * e for e in row[1:])) for row in samples),
        "unity_apogee_above_initial_m": ua[altitude_index] - unity[0][altitude_index],
        "or_apogee_above_initial_m": ra[altitude_index] - reference[0][altitude_index],
        "apogee_time_difference_s": ua[0] - ra[0],
        "origin_handling": "initial positions subtracted; no time shift or trajectory fitting",
    }
    return metrics, samples


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("unity", type=Path)
    parser.add_argument("reference", type=Path)
    parser.add_argument("--or-time-col", type=int, default=0)
    parser.add_argument("--or-altitude-col", type=int, default=1)
    parser.add_argument("--or-x-col", type=int)
    parser.add_argument("--or-z-col", type=int)
    parser.add_argument("--delimiter", default=",")
    parser.add_argument("--dt", type=float, default=0.02)
    parser.add_argument("--end-time", type=float)
    parser.add_argument("--errors-out", type=Path)
    args = parser.parse_args()
    if (args.or_x_col is None) != (args.or_z_col is None):
        parser.error("Supply both horizontal columns, mapped to Unity world X/Z, or neither.")
    spatial = args.or_x_col is not None
    columns = [args.or_time_col] + ([args.or_x_col, args.or_altitude_col, args.or_z_col]
                                  if spatial else [args.or_altitude_col])
    try:
        metrics, errors = compare(read_unity(args.unity, spatial),
                                  read_or(args.reference, columns, args.delimiter), args.dt, args.end_time)
    except (ValueError, KeyError) as error:
        parser.error(str(error))
    print(json.dumps(metrics, indent=2))
    if args.errors_out:
        with args.errors_out.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow(["time_s"] + (["dx_m", "dy_m", "dz_m"] if spatial else ["dy_m"]))
            writer.writerows(errors)


if __name__ == "__main__":
    main()
