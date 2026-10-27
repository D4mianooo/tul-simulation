using UnityEngine;
using UnityEngine.Rendering;

namespace Simulation
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class TerrainGenerator : MonoBehaviour
    {
        [SerializeField] private float size = 100f;
        [SerializeField] private int divider = 50;
        [SerializeField] private bool showMesh = true;

        public bool ShowMesh
        {
            get => showMesh;
            set => showMesh = value;
        }

        public float Size
        {
            get => size;
            set
            {
                size = Mathf.Max(0.1f, value);
                GenerateMesh();
            }
        }

        public int Divider
        {
            get => divider;
            set
            {
                divider = Mathf.Max(1, value);
                GenerateMesh();
            }
        }

        private MeshFilter meshFilter;
        private MeshCollider meshCollider;
        private Mesh generatedMesh;

        public Mesh GeneratedMesh => generatedMesh;
        public MeshFilter TerrainMeshFilter => meshFilter != null ? meshFilter : (meshFilter = GetComponent<MeshFilter>());
        public MeshCollider TerrainMeshCollider => meshCollider != null ? meshCollider : (meshCollider = GetComponent<MeshCollider>());

        private void Awake()
        {
            GenerateMesh();
        }

        private void OnValidate()
        {
            size = Mathf.Max(0.1f, size);
            divider = Mathf.Max(1, divider);
            GenerateMesh();
        }

        public void GenerateMesh()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }

            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }

            int subdivisions = Mathf.Max(1, divider);
            float terrainSize = Mathf.Max(0.1f, size);

            int vertCountPerSide = subdivisions + 1;
            int totalVertices = vertCountPerSide * vertCountPerSide;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];
            int[] triangles = new int[subdivisions * subdivisions * 6];

            float halfSize = terrainSize * 0.5f;
            float step = terrainSize / subdivisions;

            int vIndex = 0;
            for (int z = 0; z <= subdivisions; z++)
            {
                float zPos = -halfSize + z * step;
                float v = (float)z / subdivisions;

                for (int x = 0; x <= subdivisions; x++)
                {
                    float xPos = -halfSize + x * step;
                    float u = (float)x / subdivisions;

                    vertices[vIndex] = new Vector3(xPos, 0f, zPos);
                    uvs[vIndex] = new Vector2(u, v);
                    vIndex++;
                }
            }

            int tIndex = 0;
            for (int z = 0; z < subdivisions; z++)
            {
                int rowStart = z * vertCountPerSide;
                int nextRowStart = (z + 1) * vertCountPerSide;

                for (int x = 0; x < subdivisions; x++)
                {
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

            if (generatedMesh == null)
            {
                generatedMesh = new Mesh
                {
                    name = "GeneratedTerrainMesh"
                };
            }
            else
            {
                generatedMesh.Clear();
            }

            generatedMesh.indexFormat = totalVertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            generatedMesh.vertices = vertices;
            generatedMesh.uv = uvs;
            generatedMesh.triangles = triangles;
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateBounds();

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = generatedMesh;
            }

            if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = generatedMesh;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showMesh || generatedMesh == null)
            {
                return;
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireMesh(generatedMesh, transform.position, transform.rotation, transform.lossyScale);
        }
    }
}
