using UnityEngine;
using UnityEngine.Rendering;

namespace Simulation.Terrain {
    /// <summary>
    /// Procedurally generates a flat subdivided terrain mesh and collider.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class TerrainGenerator : MonoBehaviour {
        [SerializeField]
        private float _size = 100f;

        [SerializeField]
        private int _divider = 50;

        [SerializeField]
        private bool _showMesh = true;

        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;
        private Mesh _generatedMesh;

        /// <summary>
        /// Gets or sets whether the wireframe gizmo mesh is drawn in the Scene view.
        /// </summary>
        public bool ShowMesh {
            get => _showMesh;
            set => _showMesh = value;
        }

        /// <summary>
        /// Gets or sets the side length of the terrain in world units.
        /// </summary>
        public float Size {
            get => _size;
            set {
                _size = Mathf.Max(0.1f, value);
                GenerateMesh();
            }
        }

        /// <summary>
        /// Gets or sets the number of subdivisions per terrain edge.
        /// </summary>
        public int Divider {
            get => _divider;
            set {
                _divider = Mathf.Max(1, value);
                GenerateMesh();
            }
        }

        /// <summary>
        /// Gets the generated terrain mesh.
        /// </summary>
        public Mesh GeneratedMesh => _generatedMesh;

        /// <summary>
        /// Gets the cached <see cref="MeshFilter"/> component.
        /// </summary>
        public MeshFilter TerrainMeshFilter => _meshFilter != null ? _meshFilter : (_meshFilter = GetComponent<MeshFilter>());

        /// <summary>
        /// Gets the cached <see cref="MeshCollider"/> component.
        /// </summary>
        public MeshCollider TerrainMeshCollider => _meshCollider != null ? _meshCollider : (_meshCollider = GetComponent<MeshCollider>());

        private void Awake() {
            GenerateMesh();
        }

        private void OnValidate() {
            _size = Mathf.Max(0.1f, _size);
            _divider = Mathf.Max(1, _divider);
            GenerateMesh();
        }

        private void OnDrawGizmos() {
            if (!_showMesh || _generatedMesh == null) {
                return;
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireMesh(_generatedMesh, transform.position, transform.rotation, transform.lossyScale);
        }

        /// <summary>
        /// Rebuilds the terrain mesh vertices, UVs, and triangles and updates the collider.
        /// </summary>
        public void GenerateMesh() {
            if (_meshFilter == null) {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_meshCollider == null) {
                _meshCollider = GetComponent<MeshCollider>();
            }

            int subdivisions = Mathf.Max(1, _divider);
            float terrainSize = Mathf.Max(0.1f, _size);

            int vertCountPerSide = subdivisions + 1;
            int totalVertices = vertCountPerSide * vertCountPerSide;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];
            int[] triangles = new int[subdivisions * subdivisions * 6];

            float halfSize = terrainSize * 0.5f;
            float step = terrainSize / subdivisions;

            int vIndex = 0;
            for (int z = 0; z <= subdivisions; z++) {
                float zPos = -halfSize + z * step;
                float v = (float)z / subdivisions;

                for (int x = 0; x <= subdivisions; x++) {
                    float xPos = -halfSize + x * step;
                    float u = (float)x / subdivisions;

                    vertices[vIndex] = new Vector3(xPos, 0f, zPos);
                    uvs[vIndex] = new Vector2(u, v);
                    vIndex++;
                }
            }

            int tIndex = 0;
            for (int z = 0; z < subdivisions; z++) {
                int rowStart = z * vertCountPerSide;
                int nextRowStart = (z + 1) * vertCountPerSide;

                for (int x = 0; x < subdivisions; x++) {
                    int bl = rowStart + x;
                    int br = rowStart + x + 1;
                    int tl = nextRowStart + x;
                    int tr = nextRowStart + x + 1;

                    triangles[tIndex++] = bl;
                    triangles[tIndex++] = tl;
                    triangles[tIndex++] = br;

                    triangles[tIndex++] = br;
                    triangles[tIndex++] = tl;
                    triangles[tIndex++] = tr;
                }
            }

            if (_generatedMesh == null) {
                _generatedMesh = new Mesh {
                    name = "GeneratedTerrainMesh"
                };
            } else {
                _generatedMesh.Clear();
            }

            _generatedMesh.indexFormat = totalVertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _generatedMesh.vertices = vertices;
            _generatedMesh.uv = uvs;
            _generatedMesh.triangles = triangles;
            _generatedMesh.RecalculateNormals();
            _generatedMesh.RecalculateBounds();

            if (_meshFilter != null) {
                _meshFilter.sharedMesh = _generatedMesh;
            }

            if (_meshCollider != null) {
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = _generatedMesh;
            }
        }
    }
}
