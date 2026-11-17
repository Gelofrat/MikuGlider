"""Synthetic regressions for failures that the former nonzero-area gate missed."""
import unittest
import numpy as np
from hand_quality import surface_metrics, failures


class HandQualityTests(unittest.TestCase):
    def setUp(self):
        self.p = np.array([[0., 0, 0], [1, 0, 0], [0, 1, 0]])
        self.tri = [[0, 1, 2]]

    def row(self, moved):
        return dict(surface_metrics(self.p, moved, self.tri), frames=2, clips=1,
                    maximumCorrectiveJumpOverPalmWidth=0.)

    def test_translation_is_safe(self):
        self.assertEqual(failures(self.row(self.p+3)), [])

    def test_nonzero_area_collapse_fails(self):
        moved = self.p.copy(); moved[2, 1] = .13
        row = self.row(moved)
        self.assertEqual(row['newDegenerateTriangleFrames'], 0)
        self.assertTrue(failures(row))

    def test_stretch_fails(self):
        for scale in (3., .1):
            moved = self.p.copy(); moved[1, 0] = scale
            self.assertTrue(failures(self.row(moved)))

    def test_rigid_rotation_is_not_surface_inversion(self):
        moved = self.p * [-1, 1, -1]  # 180 degree rigid rotation about Y
        row = self.row(moved)
        self.assertEqual(row['largeNormalRotationTriangleFrames'], 1)
        self.assertEqual(failures(row), [])

    def test_missing_coverage_and_abrupt_activation_fail(self):
        row = self.row(self.p); row['frames'] = 0
        self.assertTrue(failures(row))
        row['frames'] = 2; row['maximumCorrectiveJumpOverPalmWidth'] = .1
        self.assertTrue(failures(row))

    def test_invalid_inputs_fail(self):
        with self.assertRaises(ValueError):
            surface_metrics(self.p, self.p*np.nan, self.tri)
        with self.assertRaises(ValueError):
            surface_metrics(self.p, self.p, [])


if __name__ == '__main__':
    unittest.main()
