using UnityEngine;

namespace Simulation
{
    [RequireComponent(typeof(LineRenderer))]
    public class TerrainDeformer : MonoBehaviour
    {
        [SerializeField] private TerrainGenerator terrainGenerator;
        [SerializeField] private float radius = 10f;
        [SerializeField] private float depth = 5f;
        [SerializeField] private int circleSegments = 64;
        [SerializeField] private float lineWidth = 0.2f;
        [SerializeField] private Color radiusColor = Color.red;

        private LineRenderer lineRenderer;
        private bool isSubscribed;

        private void Awake()
        {
            if (terrainGenerator == null)
            {
                terrainGenerator = GetComponent<TerrainGenerator>();
            }

            SetupLineRenderer();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void Update()
        {
            TrySubscribe();
            UpdateRadiusIndicator();
        }

        private void OnDisable()
        {
            if (isSubscribed && Mouse3D.Instance != null)
            {
                Mouse3D.Instance.OnLeftMouseClicked -= HandleLeftMouseClicked;
                isSubscribed = false;
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

            Vector3 center = Mouse3D.Instance != null ? Mouse3D.Instance.MousePosition : transform.position;
            float angleStep = 2f * Mathf.PI / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * angleStep;
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;
                lineRenderer.SetPosition(i, new Vector3(x, center.y + 0.05f, z));
            }
        }

        private void TrySubscribe()
        {
            if (!isSubscribed && Mouse3D.Instance != null)
            {
                Mouse3D.Instance.OnLeftMouseClicked += HandleLeftMouseClicked;
                isSubscribed = true;
            }
        }

        private void HandleLeftMouseClicked(Vector3 mouseWorldPosition)
        {
            DeformTerrain(mouseWorldPosition);
        }

        public void DeformTerrain(Vector3 worldPosition)
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
            float radiusSq = radius * radius;

            Vector3[] vertices = mesh.vertices;
            bool modified = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 vertexXZ = new Vector2(vertices[i].x, vertices[i].z);
                if ((vertexXZ - centerXZ).sqrMagnitude <= radiusSq)
                {
                    vertices[i].y -= depth;
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
