"""Targeted source regression for the known CS0136 bug; not a C# compiler."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / 'Assets/DriveRoadster/Editor/BuildRoadsterWorld.cs').read_text(encoding="utf-8")
BUILD = SOURCE.split('public static void Build()', 1)[1].split('static void RemapVehicleMaterials', 1)[0]

class BuilderScopeRegression(unittest.TestCase):
    def test_lod_path_has_distinct_name_and_uses(self):
        self.assertIn('string lodPath = Root + "/Art/Vehicle/DR_Asterion_LOD"', BUILD)
        self.assertIn('AssetDatabase.LoadAssetAtPath<GameObject>(lodPath)', BUILD)
        self.assertIn('new FileNotFoundException("Missing LOD", lodPath)', BUILD)

    def test_scene_path_has_distinct_name_and_uses(self):
        self.assertIn('string scenePath = AssetDatabase.GenerateUniqueAssetPath', BUILD)
        self.assertIn('EditorSceneManager.SaveScene(scene, scenePath)', BUILD)
        self.assertIn('"Roadster scene generated: " + scenePath', BUILD)

    def test_ambiguous_path_declaration_does_not_return(self):
        self.assertNotRegex(BUILD, r'\bstring\s+path\b')

if __name__ == '__main__':
    unittest.main()
