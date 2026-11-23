using System.Linq;
using Simulation.City;
using Simulation.Core;
using Simulation.Terrain;
using Simulation.Visualization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Simulation.Editor {
    /// <summary>
    /// Editor utility that adds and configures <see cref="ImpactHUD"/> and <see cref="ImpactVisuals"/> in the active scene.
    /// </summary>
    public static class ImpactVisualizationSetup {
        private const string OBJECT_NAME = "ImpactVisualization";

        /// <summary>
        /// Creates or updates the ImpactVisualization object and configures URP and materials.
        /// </summary>
        [MenuItem("Simulation/Add Results HUD + Visuals")]
        public static void AddToScene() {
            var simulation = Object.FindFirstObjectByType<ImpactSimulation>();
            if (simulation == null) {
                EditorUtility.DisplayDialog("Impact visualization",
                    "W scenie nie ma ImpactSimulation. Otwórz scenę symulacji (np. scena.unity) i spróbuj ponownie.", "OK");
                return;
            }

            var go = GameObject.Find(OBJECT_NAME);
            if (go == null) {
                go = new GameObject(OBJECT_NAME);
                Undo.RegisterCreatedObjectUndo(go, "Add impact visualization");
            }

            if (!go.TryGetComponent(out ImpactHUD hud)) hud = Undo.AddComponent<ImpactHUD>(go);
            if (!go.TryGetComponent(out ImpactVisuals visuals)) visuals = Undo.AddComponent<ImpactVisuals>(go);

            string cityInfo = "Brak budynków: użyj Simulation > Import buildings from OpenStreetMap...";
            if (Resources.Load<TextAsset>("City/buildings") != null) {
                if (!go.TryGetComponent(out CityLayer city)) city = Undo.AddComponent<CityLayer>(go);
                SetReference(city, "_simulation", simulation);
                cityInfo = "Dodano warstwę miasta (CityLayer).";
            }

            SetReference(hud, "_simulation", simulation);
            SetReference(visuals, "_simulation", simulation);

            string groundInfo = AssignGroundMaterial();
            var cam = Camera.main;
            if (cam != null && !cam.TryGetComponent(out CameraController _))
                Undo.AddComponent<CameraController>(cam.gameObject);
            string prefabInfo = AssignExplosionPrefab(visuals);
            string urpInfo = ConfigureUrp();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log($"[ImpactVisualizationSetup] Dodano {OBJECT_NAME}. {groundInfo} {prefabInfo} {urpInfo} {cityInfo}", go);
        }

        private static string AssignGroundMaterial() {
            var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
            if (terrain == null) return "";
            var r = terrain.GetComponent<MeshRenderer>();
            if (r == null || r.sharedMaterial != null) return "";

            const string materialsFolder = "Assets/Project/Data/Materials";
            const string path = materialsFolder + "/M_Ground.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                EnsureFolder(materialsFolder);
                mat = ImpactVisuals.CreateGroundMaterial();
                AssetDatabase.CreateAsset(mat, path);
            }
            Undo.RecordObject(r, "Assign ground material");
            r.sharedMaterial = mat;
            return "Teren dostał materiał " + path + ".";
        }

        private static void EnsureFolder(string folderPath) {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++) {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void SetReference(Object component, string field, Object value) {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static string AssignExplosionPrefab(ImpactVisuals visuals) {
            var so = new SerializedObject(visuals);
            var prop = so.FindProperty("_explosionPrefab");
            if (prop.objectReferenceValue != null)
                return "Prefab wybuchu: " + prop.objectReferenceValue.name + " (bez zmian).";

            var paths = AssetDatabase.FindAssets("Explosion t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("CFXR") || p.Contains("Cartoon FX"))
                .OrderBy(p => p.Contains("CFXR Explosion 1") ? 0 : 1)
                .ThenBy(p => p)
                .ToList();

            if (paths.Count == 0)
                return "Nie znaleziono Cartoon FX: używany będzie wbudowany efekt kuli ognia " +
                       "(po imporcie Cartoon FX uruchom to menu jeszcze raz).";

            prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]);
            so.ApplyModifiedProperties();
            return "Prefab wybuchu: " + paths[0] + ".";
        }

        private static string ConfigureUrp() {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset")) {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                if (asset.supportsCameraDepthTexture && asset.supportsHDR) continue;

                Undo.RecordObject(asset, "Enable depth texture / HDR");
                asset.supportsCameraDepthTexture = true;
                asset.supportsHDR = true;
                EditorUtility.SetDirty(asset);
                changed++;
            }
            if (changed > 0) AssetDatabase.SaveAssets();
            return changed > 0
                ? $"Włączono Depth Texture i HDR w {changed} URP Asset(ach)."
                : "URP: Depth Texture i HDR już włączone.";
        }
    }
}
