import math
from pathlib import Path
import tempfile
import unittest

from compare_flights import compare, interpolate, read_or


class ComparisonChecks(unittest.TestCase):
    def test_different_sampling_and_origins(self):
        unity = [(0, 100), (1, 102), (2, 104)]
        reference = [(0, 0), (0.5, 1), (1.5, 3), (2, 4)]
        metrics, _ = compare(unity, reference, dt=0.25)
        self.assertEqual(metrics["rmse_m"], 0)

    def test_known_altitude_error(self):
        metrics, _ = compare([(0, 0), (2, 4)], [(0, 0), (2, 2)], dt=0.5)
        self.assertAlmostEqual(metrics["mse_m2"], 1.5)
        self.assertAlmostEqual(metrics["rmse_m"], math.sqrt(1.5))

    def test_spatial_error_is_squared_distance(self):
        metrics, _ = compare([(0, 0, 0, 0), (1, 3, 2, 4)],
                             [(0, 0, 0, 0), (1, 0, 2, 0)], dt=1)
        self.assertEqual(metrics["mse_m2"], 12.5)

    def test_bad_timestamps_and_extrapolation(self):
        with self.assertRaises(ValueError):
            compare([(0, 0), (0, 1)], [(0, 0), (1, 1)])
        with self.assertRaises(ValueError):
            interpolate([(0, 0), (1, 1)], [0, 1], 2)

    def test_default_stops_at_earlier_apogee(self):
        metrics, _ = compare([(0, 0), (1, 10), (2, 0)],
                             [(0, 0), (1, 9), (2, 10)], dt=0.5)
        self.assertEqual(metrics["window_end_s"], 1)

    def test_openrocket_headers(self):
        with tempfile.TemporaryDirectory() as folder:
            p = Path(folder) / "reference.csv"
            p.write_text("# OpenRocket export\n# Time (s),Altitude (m)\n0,0\n1,10\n")
            self.assertEqual(read_or(p, [0, 1]), [(0, 0), (1, 10)])


if __name__ == "__main__":
    unittest.main()
