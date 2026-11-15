using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Simulation.EditorTools
{
    /// Menu: Simulation > Add Results HUD + Visuals
    /// Dodaje do otwartej sceny obiekt z panelem wyników/wykresami (ImpactHUD) i wizualizacją 3D (ImpactVisuals),
    /// podpina prefab wybuchu z Cartoon FX (jeśli jest zaimportowany) i włącza w URP opcje potrzebne efektom.
    public static class ImpactVisualizationSetup
    {
        private const string ObjectName = "ImpactVisualization";

        [MenuItem("Simulation/Add Results HUD + Visuals")]
        public static void AddToScene()
        {
            var simulation = Object.FindFirstObjectByType<ImpactSimulation>();
            if (simulation == null)
            {
                EditorUtility.DisplayDialog("Impact visualization",
                    "W scenie nie ma ImpactSimulation. Otwórz scenę symulacji (np. scena.unity) i spróbuj ponownie.", "OK");
                return;
            }

            var go = GameObject.Find(ObjectName);
            if (go == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Add impact visualization");
            }

            // TryGetComponent zamiast "??": w edytorze GetComponent zwraca obiekt-atrapę, a nie prawdziwy null.
            if (!go.TryGetComponent(out ImpactHUD hud)) hud = Undo.AddComponent<ImpactHUD>(go);
            if (!go.TryGetComponent(out ImpactVisuals visuals)) visuals = Undo.AddComponent<ImpactVisuals>(go);

            // Warstwa miasta tylko wtedy, gdy są zaimportowane budynki (Resources/City/buildings.csv).
            string cityInfo = "Brak budynków: użyj Simulation > Import buildings from OpenStreetMap...";
            if (Resources.Load<TextAsset>("City/buildings") != null)
            {
                if (!go.TryGetComponent(out CityLayer city)) city = Undo.AddComponent<CityLayer>(go);
                SetReference(city, "simulation", simulation);
                cityInfo = "Dodano warstwę miasta (CityLayer).";
            }

            SetReference(hud, "simulation", simulation);
            SetReference(visuals, "simulation", simulation);

            string groundInfo = AssignGroundMaterial();
            var cam = Camera.main;
            if (cam != null && !cam.TryGetComponent(out CameraController _))
                Undo.AddComponent<CameraController>(cam.gameObject);
            string prefabInfo = AssignExplosionPrefab(visuals);
            string urpInfo = ConfigureUrp();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log($"[ImpactVisualizationSetup] Dodano {ObjectName}. {groundInfo} {prefabInfo} {urpInfo} {cityInfo}", go);
        }

        /// Teren bez materiału jest różowy: tworzymy Assets/Materials/Ground.mat i przypisujemy go.
        private static string AssignGroundMaterial()
        {
            var terrain = Object.FindFirstObjectByType<TerrainGenerator>();
            if (terrain == null) return "";
            var r = terrain.GetComponent<MeshRenderer>();
            if (r == null || r.sharedMaterial != null) return "";

            const string path = "Assets/Materials/Ground.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
                mat = ImpactVisuals.CreateGroundMaterial();
                AssetDatabase.CreateAsset(mat, path);
            }
            Undo.RecordObject(r, "Assign ground material");
            r.sharedMaterial = mat;
            return "Teren dostał materiał " + path + ".";
        }

        private static void SetReference(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        /// Szuka prefabu wybuchu z Cartoon FX Remaster (pliki CFXR*). Nie nadpisuje prefabu wybranego ręcznie.
        private static string AssignExplosionPrefab(ImpactVisuals visuals)
        {
            var so = new SerializedObject(visuals);
            var prop = so.FindProperty("explosionPrefab");
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

        /// Cartoon FX w URP wymaga Depth Texture (miękkie cząsteczki) i HDR (jasne kolory z bloomem).
        private static string ConfigureUrp()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
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
