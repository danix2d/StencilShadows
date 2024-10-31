using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using System;
using System.Linq;
using UnityEngine.Rendering;

public class ShadowVolumeBurst : MonoBehaviour
{
    public bool isStatic;
    private bool hasShadow = false;
    public Mesh mesh;
    public Light lightSource;
    public float extrudeDistance = 1.0f;

    NativeArray<float3> verticesArray;
    NativeArray<int> trianglesArray;
    NativeParallelHashSet<Edge> edgeCount;
    NativeList<int> newTriangles;
    NativeList<float3> newVertices;
    NativeParallelHashMap<int, int> extrudedVertexMap;

    Mesh silhouetteMesh;
    public Material shadowMaterial;
    private Matrix4x4 localToWorld;
    private Matrix4x4 worldToLocal;
    private Vector3 lightDirection;
    public float offset;

    private MeshFilter meshFilter;
    private SkinnedMeshRenderer skinnedMeshRenderer;

    public NativeArray<float3> transformedVertices;

    void Start()
    {

        meshFilter = GetComponent<MeshFilter>();
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();

        if (meshFilter != null)
        {
            mesh = meshFilter.sharedMesh;
            MeshWelder meshWelder = new MeshWelder();
            mesh = meshWelder.WeldVertices(Instantiate(mesh));
        }
        else if (skinnedMeshRenderer != null)
        {
            mesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(mesh);
            mesh = Instantiate(mesh);
        }



        verticesArray = new NativeArray<float3>(mesh.vertexCount, Allocator.Persistent);
        trianglesArray = new NativeArray<int>(mesh.triangles, Allocator.Persistent);
        transformedVertices = new NativeArray<float3>(verticesArray, Allocator.Persistent);

        using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
        {
            dataArray[0].GetVertices(verticesArray.Reinterpret<Vector3>());
        }

        edgeCount = new NativeParallelHashSet<Edge>(trianglesArray.Length, Allocator.Persistent);
        newTriangles = new NativeList<int>(Allocator.Persistent);
        newVertices = new NativeList<float3>(Allocator.Persistent);
        extrudedVertexMap = new NativeParallelHashMap<int, int>(trianglesArray.Length, Allocator.Persistent);

        silhouetteMesh = new Mesh();
        // Add a new MeshFilter and MeshRenderer for the silhouette geometry
        GameObject silhouetteObject = new GameObject("Silhouette");
        silhouetteObject.transform.SetParent(transform);
        silhouetteObject.transform.localPosition = Vector3.zero;
        silhouetteObject.transform.localRotation = Quaternion.identity;
        silhouetteObject.transform.localScale = Vector3.one * offset;

        MeshFilter silhouetteMeshFilter = silhouetteObject.AddComponent<MeshFilter>();
        silhouetteMeshFilter.mesh = silhouetteMesh;

        MeshRenderer silhouetteMeshRenderer = silhouetteObject.AddComponent<MeshRenderer>();
        silhouetteMeshRenderer.material = shadowMaterial;
        silhouetteMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (skinnedMeshRenderer != null)
        {
            skinnedMeshRenderer.BakeMesh(mesh);
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                dataArray[0].GetVertices(verticesArray.Reinterpret<Vector3>());
            }
        }

        if (isStatic && hasShadow) { return; }

        localToWorld = transform.localToWorldMatrix;
        worldToLocal = transform.worldToLocalMatrix;
        lightDirection = -lightSource.transform.forward;


    }

    void LateUpdate()
    {
        // Clear previous edges
        if (isStatic && hasShadow) { return; }

        edgeCount.Clear();
        newTriangles.Clear();
        newVertices.Clear();
        extrudedVertexMap.Clear();
        newVertices.AddRange(verticesArray);



        if(transform.hasChanged)
        {
            var transformVerts = new TransformLocalToWorldVerts
            {
                localToWorldMatrix = localToWorld,
                vertices = verticesArray,
                transformedVertices = transformedVertices,
            };

            JobHandle transVerts = transformVerts.Schedule(verticesArray.Length, 64);
            transVerts.Complete();

            transform.hasChanged = false;
        }
        
        var findEdgesJob = new FindEdgesJob
        {
            triangles = trianglesArray,
            vertices = verticesArray,
            lightDirection = lightDirection,
            edgeCount = edgeCount,
            newTriangles = newTriangles,
            transformedVertices = transformedVertices,
        };

        var findEdgesJobHandle = findEdgesJob.Schedule();
        findEdgesJobHandle.Complete();

        transform.hasChanged = false;

        var generateSilhouetteJob = new GenerateSilhouetteJob
        {
            vertices = verticesArray,
            edgeCount = edgeCount,
            extrudedVertexMap = extrudedVertexMap,
            newTriangles = newTriangles,
            newVertices = newVertices,
            lightDirection = lightDirection,
            localToWorldMatrix = localToWorld,
            worldToLocalMatrix = worldToLocal,
            extrudeDistance = extrudeDistance,
            transformedVertices = transformedVertices,
        };

        var jobHandle = generateSilhouetteJob.Schedule();
        jobHandle.Complete();

        silhouetteMesh.Clear();
        silhouetteMesh.MarkDynamic();
        silhouetteMesh.SetVertices(newVertices.AsArray());
        silhouetteMesh.SetIndices(newTriangles.AsArray(), MeshTopology.Triangles, 0);
        silhouetteMesh.RecalculateNormals();
        hasShadow = true;
    }

    void OnDestroy()
    {
        // Dispose of NativeArrays when the script is destroyed
        verticesArray.Dispose();
        trianglesArray.Dispose();
        edgeCount.Dispose();
        newTriangles.Dispose();
        newVertices.Dispose();
        extrudedVertexMap.Dispose();
        transformedVertices.Dispose();
        
    }

    void OnDrawGizmos()
    {
        if (edgeCount.IsCreated && Application.isPlaying)
        {
            Gizmos.color = Color.green;

            foreach (var edge in edgeCount)
            {
                Vector3 start = verticesArray[edge.vertexIndex1];
                Vector3 end = verticesArray[edge.vertexIndex2];

                Gizmos.DrawLine(transform.TransformPoint(start), transform.TransformPoint(end));
            }
        }
    }


    [BurstCompile]
    public struct TransformLocalToWorldVerts : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public Matrix4x4 localToWorldMatrix;
        [WriteOnly] public NativeArray<float3> transformedVertices;

        public void Execute(int index)
        {
                transformedVertices[index] = LocalToWorld(vertices[index]);
        }

        private float3 LocalToWorld(float3 vert)
        {
            return math.mul(localToWorldMatrix, new float4(vert, 1.0f)).xyz;
        }
    }

    [BurstCompile]
    public struct FindEdgesJob : IJob
    {
        [ReadOnly] public NativeArray<int> triangles;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public float3 lightDirection;
        public NativeParallelHashSet<Edge> edgeCount;
        public NativeList<int> newTriangles;
        [ReadOnly] public NativeArray<float3> transformedVertices;

        public void Execute()
        {

            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Get vertex indices of the triangle
                int index1 = triangles[i];
                int index2 = triangles[i + 1];
                int index3 = triangles[i + 2];

                float3 v0 = transformedVertices[index1];
                float3 v1 = transformedVertices[index2];
                float3 v2 = transformedVertices[index3];

                float3 normal = math.normalize(math.cross(v1 - v0, v2 - v0));

                if (math.dot(normal, lightDirection) < 0)
                {
                    // Check each edge if it is a silhouette edge
                    CheckAndAddEdge(index1,index2);
                    CheckAndAddEdge(index2,index3);
                    CheckAndAddEdge(index3,index1);
                }
            }
        }
        private void CheckAndAddEdge(int v1,int v2)
        {
            Edge edge = new Edge(v1, v2);

            if (!edgeCount.Contains(edge))
            {
                edgeCount.Add(edge);
            }
            else
            {
                edgeCount.Remove(edge);
            }
        }
    }

    [BurstCompile]
    public struct GenerateSilhouetteJob : IJob
    {
        [ReadOnly] public NativeParallelHashSet<Edge> edgeCount;
        [ReadOnly] public NativeArray<float3> transformedVertices;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public float3 lightDirection;
        [ReadOnly] public Matrix4x4 localToWorldMatrix;
        [ReadOnly] public Matrix4x4 worldToLocalMatrix;
        [ReadOnly] public float extrudeDistance;

        public NativeList<int> newTriangles;
        public NativeList<float3> newVertices;
        public NativeParallelHashMap<int, int> extrudedVertexMap;
        public float3 apexVertex;
        public int apexVertexIndex;
        public float3 apexVertex2;
        public int apexVertexIndex2;
        
        public void Execute()
        {
            apexVertex = WorldToLocal(LocalToWorld(float3.zero) + -lightDirection * extrudeDistance);
            apexVertexIndex = newVertices.Length;
            newVertices.Add(apexVertex);

            apexVertex2 = WorldToLocal(LocalToWorld(float3.zero));
            apexVertexIndex2 = newVertices.Length;
            newVertices.Add(apexVertex2);

            foreach (var edge in edgeCount)
            {

                ExtrudeVertex(edge.vertexIndex1,transformedVertices[edge.vertexIndex1]);
                ExtrudeVertex(edge.vertexIndex2,transformedVertices[edge.vertexIndex2]);

                int originalVertex1Index = edge.vertexIndex1;
                int originalVertex2Index = edge.vertexIndex2;
                int extrudedVertex1Index = extrudedVertexMap[originalVertex1Index];
                int extrudedVertex2Index = extrudedVertexMap[originalVertex2Index];

                // Create two triangles to form a quad between the original and extruded vertices
                newTriangles.Add(originalVertex2Index);
                newTriangles.Add(originalVertex1Index);
                newTriangles.Add(apexVertexIndex2);

                newTriangles.Add(originalVertex1Index);
                newTriangles.Add(originalVertex2Index);
                newTriangles.Add(extrudedVertex1Index);

                newTriangles.Add(extrudedVertex1Index);
                newTriangles.Add(originalVertex2Index);
                newTriangles.Add(extrudedVertex2Index);

                newTriangles.Add(extrudedVertex1Index);
                newTriangles.Add(extrudedVertex2Index);
                newTriangles.Add(apexVertexIndex);
            }
        }

        private void ExtrudeVertex(int index, float3 vert)
        {
            if (!extrudedVertexMap.TryGetValue(index, out int newIndex))
            {
                float3 extrudedVertex = vert + (-lightDirection * extrudeDistance);
                extrudedVertex = WorldToLocal(extrudedVertex);
                newIndex = newVertices.Length;
                extrudedVertexMap[index] = newIndex;
                newVertices.Add(extrudedVertex);
            }
        }

        private float3 WorldToLocal(float3 vert)
        {
            return math.mul(worldToLocalMatrix, new float4(vert, 1.0f)).xyz;
        }

        private float3 LocalToWorld(float3 vert)
        {
            return math.mul(localToWorldMatrix, new float4(vert, 1.0f)).xyz;
        }
    }

    public struct Edge : IEquatable<Edge>
    {
        public int vertexIndex1;
        public int vertexIndex2;

        public Edge(int v1, int v2)
        {
            vertexIndex1 = v1;
            vertexIndex2 = v2;
        }

        public override int GetHashCode()
        {
            return vertexIndex1.GetHashCode() ^ vertexIndex2.GetHashCode();
        }

        public bool Equals(Edge other)
        {
            return (vertexIndex1 == other.vertexIndex1 && vertexIndex2 == other.vertexIndex2) ||
                   (vertexIndex1 == other.vertexIndex2 && vertexIndex2 == other.vertexIndex1);
        }
    }
}
