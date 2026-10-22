import unittest
from metrics import median_measurement

class MedianMeasurementTests(unittest.TestCase):
    def test_even_samples_average_middle_pair(self):
        self.assertEqual(median_measurement([7, 1, 5, 3]), 4)

    def test_zero_is_a_real_measurement(self):
        self.assertEqual(median_measurement([0, 2, 4]), 2)

    def test_zero_only_sample_is_valid(self):
        self.assertEqual(median_measurement([0]), 0)

    def test_odd_sample(self):
        self.assertEqual(median_measurement([9, 1, 3]), 3)

    def test_invalid_measurements_are_rejected(self):
        for value in [None, True, -1, float("nan"), float("inf"), "2"]:
            with self.subTest(value=repr(value)):
                with self.assertRaises(ValueError):
                    median_measurement([1, value, 3])

    def test_empty_measurements_are_rejected(self):
        with self.assertRaises(ValueError):
            median_measurement([])

if __name__ == "__main__":
    unittest.main()
