using UnityEngine;

namespace Simulation
{
    [RequireComponent(typeof(LineRenderer))]
    public class TerrainDeformer : MonoBehaviour
    {
        [SerializeField] private TerrainGenerator terrainGenerator;
        [SerializeField] private ImpactSimulation impactSimulation;
        [SerializeField] private Target target;
        [SerializeField] private int circleSegments = 64;
        [SerializeField] private float lineWidth = 0.2f;
        [SerializeField] private Color radiusColor = Color.red;

        private LineRenderer lineRenderer;
        private bool isSimulationSubscribed;

        private void Awake()
        {
            if (terrainGenerator == null)
            {
                terrainGenerator = GetComponent<TerrainGenerator>();
            }

            if (impactSimulation == null)
            {
                impactSimulation = FindFirstObjectByType<ImpactSimulation>();
            }

            if (target == null)
            {
                target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
            }

            SetupLineRenderer();
        }

        private void OnEnable()
        {
            TrySubscribeSimulation();
        }

        private void Start()
        {
            TrySubscribeSimulation();
        }

        private void Update()
        {
            TrySubscribeSimulation();
            UpdateRadiusIndicator();
        }

        private void OnDisable()
        {
            if (isSimulationSubscribed && impactSimulation != null)
            {
                impactSimulation.ResultsComputed -= HandleResultsComputed;
                isSimulationSubscribed = false;
            }
        }

        private void TrySubscribeSimulation()
        {
            if (!isSimulationSubscribed && impactSimulation != null)
            {
                impactSimulation.ResultsComputed += HandleResultsComputed;
                isSimulationSubscribed = true;
            }
        }

        private void HandleResultsComputed(ImpactResults results)
        {
            DeformAtTarget((float)results.CraterRimRadiusM, (float)results.finalDepthM);
        }

        public void DeformAtTarget()
        {
            float craterRadius = impactSimulation != null ? (float)impactSimulation.Results.CraterRimRadiusM : 0f;
            float craterDepth = impactSimulation != null ? (float)impactSimulation.Results.finalDepthM : 0f;
            DeformAtTarget(craterRadius, craterDepth);
        }

        public void DeformAtTarget(float craterRadius, float craterDepth)
        {
            if (target == null)
            {
                target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
            }

            if (target != null)
            {
                if (!target.gameObject.activeInHierarchy)
                {
                    return;
                }
                DeformTerrain(target.transform.position, craterRadius, craterDepth);
                return;
            }

            if (Mouse3D.Instance != null)
            {
                DeformTerrain(Mouse3D.Instance.MousePosition, craterRadius, craterDepth);
            }
        }

        private void SetupLineRenderer()
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = true;
            lineRenderer.positionCount = Mathf.Max(3, circleSegments);
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.startColor = radiusColor;
            lineRenderer.endColor = radiusColor;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (lineRenderer.sharedMaterial == null)
            {
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null)
                {
                    lineRenderer.sharedMaterial = new Material(defaultShader);
                }
            }
        }

        private void UpdateRadiusIndicator()
        {
            if (lineRenderer == null)
            {
                SetupLineRenderer();
            }

            int segments = Mathf.Max(3, circleSegments);
            if (lineRenderer.positionCount != segments)
            {
                lineRenderer.positionCount = segments;
            }

            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.startColor = radiusColor;
            lineRenderer.endColor = radiusColor;

            float currentRadius = impactSimulation != null ? (float)impactSimulation.Results.CraterRimRadiusM : 0f;
            Vector3 center = Mouse3D.Instance != null ? Mouse3D.Instance.MousePosition : transform.position;
            float angleStep = 2f * Mathf.PI / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * angleStep;
                float x = center.x + Mathf.Cos(angle) * currentRadius;
                float z = center.z + Mathf.Sin(angle) * currentRadius;
                lineRenderer.SetPosition(i, new Vector3(x, center.y + 0.05f, z));
            }
        }

        public void DeformTerrain(Vector3 worldPosition, float craterRadius, float craterDepth)
        {
            if (terrainGenerator == null)
            {
                terrainGenerator = GetComponent<TerrainGenerator>();
                if (terrainGenerator == null)
                {
                    return;
                }
            }

            Mesh mesh = terrainGenerator.GeneratedMesh;
            if (mesh == null && terrainGenerator.TerrainMeshFilter != null)
            {
                mesh = terrainGenerator.TerrainMeshFilter.sharedMesh;
            }

            if (mesh == null)
            {
                return;
            }

            Transform terrainTransform = terrainGenerator.transform;
            Vector3 localCenter = terrainTransform.InverseTransformPoint(worldPosition);
            Vector2 centerXZ = new Vector2(localCenter.x, localCenter.z);
            float radiusSq = craterRadius * craterRadius;

            Vector3[] vertices = mesh.vertices;
            bool modified = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 vertexXZ = new Vector2(vertices[i].x, vertices[i].z);
                if ((vertexXZ - centerXZ).sqrMagnitude <= radiusSq)
                {
                    vertices[i].y -= craterDepth;
                    modified = true;
                }
            }

            if (modified)
            {
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                MeshCollider collider = terrainGenerator.TerrainMeshCollider;
                if (collider != null)
                {
                    collider.sharedMesh = null;
                    collider.sharedMesh = mesh;
                }
            }
        }
    }
}
