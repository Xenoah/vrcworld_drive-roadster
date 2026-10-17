"""Source regression sentinels only; no C# or Udon is executed."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / 'Assets/DriveRoadster/Scripts'
VEHICLE = (SCRIPTS / 'RoadsterVehicle.cs').read_text()
RUN = (SCRIPTS / 'RoadsterRun.cs').read_text()

class RuntimeContracts(unittest.TestCase):
    def test_initial_state_gate_exists(self):
        self.assertIn('!Networking.IsNetworkSettled', VEHICLE)
        self.assertIn('!Networking.IsOwner(gameObject) && !hasSnapshot', VEHICLE)

    def test_run_is_bound_to_driver_generation(self):
        self.assertIn('driverGeneration = vehicle.generation', RUN)
        self.assertIn('driverGeneration != vehicle.generation', RUN)

    def test_gate_sampling_is_explicit_after_motion_mode(self):
        self.assertNotIn('void FixedUpdate()', RUN)
        start = VEHICLE.index('void FixedUpdate()')
        end = VEHICLE.index('public void EnableFlight()', start)
        physics = VEHICLE[start:end]
        self.assertGreater(physics.index('run.SamplePosition()'), physics.index('motionMode = 4'))

    def test_flight_bounds_failure_invalidates_run(self):
        for axis in 'xyz':
            self.assertIn(f'Mathf.Abs(offset.{axis}) > flightBoundsSize.{axis} * .5f', VEHICLE)
        self.assertIn('run.InvalidateFlightBounds()', VEHICLE)
        self.assertIn('INVALID / flight bounds', RUN)

    def test_live_snapshot_and_send_diagnostics_exist(self):
        self.assertIn('public override void OnPreSerialization()', VEHICLE)
        self.assertIn('public override void OnPostSerialization(SerializationResult result)', VEHICLE)
        self.assertIn('lastSerializationSucceeded = result.success', VEHICLE)

    def test_personal_best_and_five_record_limit_are_explicit(self):
        self.assertIn('private float personalBest;', RUN)
        self.assertIn('seconds < personalBest', RUN)
        self.assertIn('new string[5]', RUN)
        self.assertIn('new float[5]', RUN)

    def test_nonowner_and_owner_ccd_modes_are_separate(self):
        self.assertIn('body.isKinematic = !owner', VEHICLE)
        self.assertIn('CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.ContinuousSpeculative', VEHICLE)

if __name__ == '__main__':
    unittest.main()
