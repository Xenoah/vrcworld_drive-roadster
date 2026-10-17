"""Source/geometry contract checks, NOT Unity, Udon or multiplayer runtime tests."""
import json, math, pathlib, unittest
ROOT = pathlib.Path(__file__).resolve().parents[1]
class SourceContracts(unittest.TestCase):
    def test_course_schema(self):
        d = json.loads((ROOT/'Assets/DriveRoadster/Art/Course/course_manifest.json').read_text(encoding="utf-8"))
        gates = d['checkpoints']
        self.assertGreaterEqual(len(gates), 10)
        self.assertEqual([g['index'] for g in gates], list(range(len(gates))))
        for g in gates:
            for key in ('position', 'forward', 'up'):
                self.assertEqual(len(g[key]), 3)
                self.assertTrue(all(math.isfinite(v) for v in g[key]))
            self.assertGreater(g['width'], 0); self.assertGreater(g['height'], 0)
            self.assertIn(g['required_mode'], ('Ground','WallRide','AssistedFlight','Any'))
            for key in ('forward','up'):
                self.assertAlmostEqual(sum(x*x for x in g[key]), 1, delta=.01)
        self.assertGreaterEqual(d['flightStartGate'], 0)
        self.assertGreater(d['flightEndGate'], d['flightStartGate'])
        self.assertLess(d['flightEndGate'], len(gates))
        delta = [d['spawn']['position'][i]-gates[0]['position'][i] for i in range(3)]
        self.assertLess(sum(delta[i]*gates[0]['forward'][i] for i in range(3)), 0)
        self.assertLess(math.dist(gates[0]['position'], gates[-1]['position']), 5)
    def test_authority_guards(self):
        s=(ROOT/'Assets/DriveRoadster/Scripts/RoadsterVehicle.cs').read_text(encoding="utf-8")
        self.assertIn('if (!Networking.IsOwner(gameObject)) return;', s)
        self.assertIn('OnOwnershipRequest',s); self.assertIn('OnOwnershipTransferred',s)
        self.assertIn('OnPlayerLeft',s); self.assertIn('OnDeserialization',s)
        self.assertIn('observedReset != resetSequence',s)
        self.assertNotIn('AddComponent<VRCObjectSync>',(ROOT/'Assets/DriveRoadster/Editor/BuildRoadsterWorld.cs').read_text(encoding="utf-8"))
    def test_swept_gate_contract(self):
        s=(ROOT/'Assets/DriveRoadster/Scripts/RoadsterRun.cs').read_text(encoding="utf-8")
        for fragment in ('a.z < 0f && b.z >= 0f','gateHalfWidths[nextGate]','gateHalfHeights[nextGate]', 'requiredModes[nextGate]', 'minimumInterval', 'nextGate == gates.Length', 'Vector3.Distance(current, previousPosition) > 15f'):
            self.assertIn(fragment,s)
    def test_validation_scope_is_explicit(self):
        s=(ROOT/'README.md').read_text(encoding="utf-8")
        for text in ('未検証', 'ClientSim', '複数クライアント', 'VRChat Build & Test'):
            self.assertIn(text, s)
    def test_source_files(self):
        files=list((ROOT/'Assets/DriveRoadster/Scripts').glob('*.cs'))
        self.assertEqual(len(files),3)
        for p in files:
            self.assertIn('UdonSharpBehaviour',p.read_text(encoding="utf-8"))
    def test_fbx_headers(self):
        files=list((ROOT/'Assets/DriveRoadster/Art').rglob('*.fbx'))
        self.assertGreaterEqual(len(files),2)
        for p in files:
            self.assertTrue(p.read_bytes().startswith(b'Kaydara FBX Binary'),str(p))

# Independent numerical examples of the swept-plane formula used in RoadsterRun.
def crossing(a,b,width=10,height=8):
    if not a[2]<0<=b[2]: return False
    t=-a[2]/(b[2]-a[2])
    p=[a[i]+(b[i]-a[i])*t for i in range(3)]
    return abs(p[0])<=width and abs(p[1])<=height
class GateMath(unittest.TestCase):
    def test_forward(self): self.assertTrue(crossing([0,0,-20],[0,0,20]))
    def test_reverse(self): self.assertFalse(crossing([0,0,20],[0,0,-20]))
    def test_miss_side(self): self.assertFalse(crossing([11,0,-1],[11,0,1]))
    def test_miss_height(self): self.assertFalse(crossing([0,9,-1],[0,9,1]))
    def test_endpoint(self): self.assertTrue(crossing([10,8,-1],[10,8,0]))
    def test_no_double(self): self.assertFalse(crossing([0,0,0],[0,0,1]))
    def test_parallel(self): self.assertFalse(crossing([0,0,-1],[1,0,-1]))
    def test_diagonal(self): self.assertTrue(crossing([-20,0,-20],[20,0,20]))
if __name__=='__main__': unittest.main()
