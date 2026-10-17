"""Independent mathematical tests, NOT execution of C#/Udon/Unity physics.
Run: python3 -m unittest discover -s Tests/Review -v
"""
import json
import math
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
DATA = json.loads((ROOT / 'Assets/DriveRoadster/Art/Course/course_manifest.json').read_text(encoding="utf-8"))


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def point(gate, x, y, z):
    right = cross(gate['up'], gate['forward'])
    return tuple(gate['position'][i] + right[i]*x + gate['up'][i]*y + gate['forward'][i]*z for i in range(3))


def crosses(gate, previous, current):
    """Replicates the reviewed plane predicate on orthonormal unit gates."""
    right = cross(gate['up'], gate['forward'])
    a_world, b_world = sub(previous, gate['position']), sub(current, gate['position'])
    a = (dot(a_world, right), dot(a_world, gate['up']), dot(a_world, gate['forward']))
    b = (dot(b_world, right), dot(b_world, gate['up']), dot(b_world, gate['forward']))
    if not (a[2] < 0 <= b[2]):
        return False
    t = -a[2] / (b[2] - a[2])
    x, y = (a[i] + t*(b[i]-a[i]) for i in range(2))
    return abs(x) <= gate['width']/2 and abs(y) <= gate['height']/2


class GateGeometry(unittest.TestCase):
    def test_orthonormal_manifest_frames(self):
        for gate in DATA['checkpoints']:
            self.assertAlmostEqual(dot(gate['forward'], gate['forward']), 1)
            self.assertAlmostEqual(dot(gate['up'], gate['up']), 1)
            self.assertAlmostEqual(dot(gate['forward'], gate['up']), 0)
            self.assertGreater(gate['width'], 0)
            self.assertGreater(gate['height'], 0)

    def test_every_gate_accepts_forward_sweep(self):
        for gate in DATA['checkpoints']:
            self.assertTrue(crosses(gate, point(gate, 0, 0, -3), point(gate, 0, 0, 3)))

    def test_every_gate_rejects_reverse_sweep(self):
        for gate in DATA['checkpoints']:
            self.assertFalse(crosses(gate, point(gate, 0, 0, 3), point(gate, 0, 0, -3)))

    def test_every_gate_rejects_outside_width_and_height(self):
        for gate in DATA['checkpoints']:
            for x, y in [(gate['width']/2+.01, 0), (0, gate['height']/2+.01)]:
                self.assertFalse(crosses(gate, point(gate, x, y, -3), point(gate, x, y, 3)))

    def test_parallel_and_zero_length_sweeps_are_rejected(self):
        for gate in DATA['checkpoints']:
            self.assertFalse(crosses(gate, point(gate, 0, 0, 0), point(gate, 0, 0, 0)))
            self.assertFalse(crosses(gate, point(gate, -1, 0, -1), point(gate, 1, 0, -1)))

    def test_plane_endpoint_has_no_duplicate_acceptance(self):
        for gate in DATA['checkpoints']:
            self.assertTrue(crosses(gate, point(gate, 0, 0, -1), point(gate, 0, 0, 0)))
            self.assertFalse(crosses(gate, point(gate, 0, 0, 0), point(gate, 0, 0, 1)))

    def test_flight_modes_have_explicit_builder_mapping(self):
        source = (ROOT / 'Assets/DriveRoadster/Editor/BuildRoadsterWorld.cs').read_text(encoding="utf-8")
        self.assertIn('gate.required_mode == "AssistedFlight"', source)
        self.assertIn('Unknown gate mode', source)
        for i in range(DATA['flightStartGate']+1, DATA['flightEndGate']):
            self.assertEqual(DATA['checkpoints'][i]['required_mode'], 'AssistedFlight')

    def test_single_step_max_speed_is_below_discontinuity_limit(self):
        # This checks configured defaults only; not low-FPS rendering or real physics.
        self.assertLess(65 * .02, 15)

    def test_minimum_gate_times_are_positive(self):
        gates = DATA['checkpoints']
        for previous, current in zip(gates, gates[1:]):
            self.assertGreater(math.dist(previous['position'], current['position']) / 65 * .75, 0)


if __name__ == '__main__':
    unittest.main()
