using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using Unity.Mathematics;

public class MeshWelder
{
    [BurstCompile]
    public struct WeldVerticesJob : IJob
    {
        [ReadOnly] public NativeArray<int> triangles;
        [ReadOnly] public NativeArray<float3> vertices;
        public NativeParallelHashMap<float3,int> vertexMap;
        public NativeList<int> newTriangles;
        public NativeList<float3> newVertices;

        public void Execute()
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                float3 vertex = vertices[i];
                if (!vertexMap.ContainsKey(vertex))
                {
                    vertexMap[vertex] = newVertices.Length;
                    newVertices.Add(vertex);
                }
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int originalVertexIndex = triangles[i];
                int newVertexIndex = vertexMap[vertices[originalVertexIndex]];
                newTriangles.Add(newVertexIndex);
            }
        }
    }

    public Mesh WeldVertices(Mesh mesh)
    {

        NativeArray<int> triangles;
        NativeArray<float3> vertices;

        vertices = new NativeArray<float3>(mesh.vertexCount, Allocator.Persistent);
        triangles = new NativeArray<int>(mesh.triangles, Allocator.Persistent);

        using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
        {
            dataArray[0].GetVertices(vertices.Reinterpret<Vector3>());
        }

        NativeParallelHashMap<float3,int> vertexMap = new NativeParallelHashMap<float3, int>(vertices.Length, Allocator.Persistent);
        NativeList<int> newTriangles = new NativeList<int>(vertices.Length, Allocator.Persistent);
        NativeList<float3> newVertices = new NativeList<float3>(triangles.Length, Allocator.Persistent);

        var weldVerticesJob = new WeldVerticesJob
        {
            triangles = triangles,
            vertices = vertices,
            vertexMap = vertexMap,
            newTriangles = newTriangles,
            newVertices = newVertices,
        };

        var jobHandle = weldVerticesJob.Schedule();
        jobHandle.Complete();

        mesh.Clear();
        mesh.SetVertices(newVertices.AsArray());
        mesh.SetIndices(newTriangles.AsArray(), MeshTopology.Triangles, 0);

        triangles.Dispose();
        vertices.Dispose();
        vertexMap.Dispose();
        newTriangles.Dispose();
        newVertices.Dispose();

        return mesh;
    }
}
