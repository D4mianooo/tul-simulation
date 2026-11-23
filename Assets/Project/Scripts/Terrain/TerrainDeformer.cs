using Simulation.Core;
using UnityEngine;

namespace Simulation.Terrain {
    /// <summary>
    /// Deforms the terrain mesh to form an impact crater and renders a crater radius preview ring.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TerrainDeformer : MonoBehaviour {
        [SerializeField]
        private TerrainGenerator _terrainGenerator;

        [SerializeField]
        private ImpactSimulation _impactSimulation;

        [SerializeField]
        private Target _target;

        [SerializeField]
        private int _circleSegments = 64;

        [SerializeField]
        private float _lineWidth = 0.2f;

        [SerializeField]
        private Color _radiusColor = Color.red;

        private LineRenderer _lineRenderer;
        private bool _isSimulationSubscribed;

        private void Awake() {
            if (_terrainGenerator == null) {
                _terrainGenerator = GetComponent<TerrainGenerator>();
            }

            if (_impactSimulation == null) {
                _impactSimulation = FindFirstObjectByType<ImpactSimulation>();
            }

            if (_target == null) {
                _target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
            }

            SetupLineRenderer();
        }

        private void OnEnable() {
            TrySubscribeSimulation();
        }

        private void Start() {
            TrySubscribeSimulation();
        }

        private void Update() {
            TrySubscribeSimulation();
            UpdateRadiusIndicator();
        }

        private void OnDisable() {
            if (_isSimulationSubscribed && _impactSimulation != null) {
                _impactSimulation.ResultsComputed -= HandleResultsComputed;
                _isSimulationSubscribed = false;
            }
        }

        /// <summary>
        /// Deforms the terrain at the current target using the latest simulation crater dimensions.
        /// </summary>
        public void DeformAtTarget() {
            float craterRadius = _impactSimulation != null ? (float)_impactSimulation.Results.CraterRimRadiusM : 0f;
            float craterDepth = _impactSimulation != null ? (float)_impactSimulation.Results.finalDepthM : 0f;
            DeformAtTarget(craterRadius, craterDepth);
        }

        /// <summary>
        /// Deforms the terrain at the active target or mouse world position using the specified crater dimensions.
        /// </summary>
        /// <param name="craterRadius">Crater rim radius in world units.</param>
        /// <param name="craterDepth">Crater depth in world units.</param>
        public void DeformAtTarget(float craterRadius, float craterDepth) {
            if (_target == null) {
                _target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
            }

            if (_target != null) {
                if (!_target.gameObject.activeInHierarchy) {
                    return;
                }
                DeformTerrain(_target.transform.position, craterRadius, craterDepth);
                return;
            }

            if (Mouse3D.Instance != null) {
                DeformTerrain(Mouse3D.Instance.MousePosition, craterRadius, craterDepth);
            }
        }

        /// <summary>
        /// Displaces terrain vertices within <paramref name="craterRadius"/> of <paramref name="worldPosition"/> downward by <paramref name="craterDepth"/>.
        /// </summary>
        /// <param name="worldPosition">Impact center in world space.</param>
        /// <param name="craterRadius">Crater radius in world units.</param>
        /// <param name="craterDepth">Crater depth in world units.</param>
        public void DeformTerrain(Vector3 worldPosition, float craterRadius, float craterDepth) {
            if (_terrainGenerator == null) {
                _terrainGenerator = GetComponent<TerrainGenerator>();
                if (_terrainGenerator == null) {
                    return;
                }
            }

            Mesh mesh = _terrainGenerator.GeneratedMesh;
            if (mesh == null && _terrainGenerator.TerrainMeshFilter != null) {
                mesh = _terrainGenerator.TerrainMeshFilter.sharedMesh;
            }

            if (mesh == null) {
                return;
            }

            Transform terrainTransform = _terrainGenerator.transform;
            Vector3 localCenter = terrainTransform.InverseTransformPoint(worldPosition);
            Vector2 centerXZ = new Vector2(localCenter.x, localCenter.z);
            float radiusSq = craterRadius * craterRadius;

            Vector3[] vertices = mesh.vertices;
            bool modified = false;

            for (int i = 0; i < vertices.Length; i++) {
                Vector2 vertexXZ = new Vector2(vertices[i].x, vertices[i].z);
                if ((vertexXZ - centerXZ).sqrMagnitude <= radiusSq) {
                    vertices[i].y -= craterDepth;
                    modified = true;
                }
            }

            if (modified) {
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                MeshCollider collider = _terrainGenerator.TerrainMeshCollider;
                if (collider != null) {
                    collider.sharedMesh = null;
                    collider.sharedMesh = mesh;
                }
            }
        }

        private void TrySubscribeSimulation() {
            if (!_isSimulationSubscribed && _impactSimulation != null) {
                _impactSimulation.ResultsComputed += HandleResultsComputed;
                _isSimulationSubscribed = true;
            }
        }

        private void HandleResultsComputed(ImpactResults results) {
            DeformAtTarget((float)results.CraterRimRadiusM, (float)results.finalDepthM);
        }

        private void SetupLineRenderer() {
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null) {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            _lineRenderer.useWorldSpace = true;
            _lineRenderer.loop = true;
            _lineRenderer.positionCount = Mathf.Max(3, _circleSegments);
            _lineRenderer.startWidth = _lineWidth;
            _lineRenderer.endWidth = _lineWidth;
            _lineRenderer.startColor = _radiusColor;
            _lineRenderer.endColor = _radiusColor;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            if (_lineRenderer.sharedMaterial == null) {
                Shader defaultShader = Shader.Find("Sprites/Default");
                if (defaultShader != null) {
                    _lineRenderer.sharedMaterial = new Material(defaultShader);
                }
            }
        }

        private void UpdateRadiusIndicator() {
            if (_lineRenderer == null) {
                SetupLineRenderer();
            }

            int segments = Mathf.Max(3, _circleSegments);
            if (_lineRenderer.positionCount != segments) {
                _lineRenderer.positionCount = segments;
            }

            _lineRenderer.startWidth = _lineWidth;
            _lineRenderer.endWidth = _lineWidth;
            _lineRenderer.startColor = _radiusColor;
            _lineRenderer.endColor = _radiusColor;

            float currentRadius = _impactSimulation != null ? (float)_impactSimulation.Results.CraterRimRadiusM : 0f;
            Vector3 center = Mouse3D.Instance != null ? Mouse3D.Instance.MousePosition : transform.position;
            float angleStep = 2f * Mathf.PI / segments;

            for (int i = 0; i < segments; i++) {
                float angle = i * angleStep;
                float x = center.x + Mathf.Cos(angle) * currentRadius;
                float z = center.z + Mathf.Sin(angle) * currentRadius;
                _lineRenderer.SetPosition(i, new Vector3(x, center.y + 0.05f, z));
            }
        }
    }
}
