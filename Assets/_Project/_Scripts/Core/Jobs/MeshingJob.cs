// --- ФАЙЛ: MeshingJob.cs ---
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshingJob : IJob
{
    [ReadOnly] public NativeArray<ushort> voxelIDs;
    [ReadOnly] public NativeArray<Color32> voxelColors;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX, neighborVoxelsNegX, neighborVoxelsPosZ, neighborVoxelsNegZ;
    [ReadOnly] public bool hasNeighborPosX, hasNeighborNegX, hasNeighborPosZ, hasNeighborNegZ;
    
    public NativeList<Vertex> vertices;
    public NativeList<int> triangles;

    public void Execute()
    {
        for (int y = 0; y < Chunk.Height; y++)
        for (int x = 0; x < Chunk.Width; x++)
        for (int z = 0; z < Chunk.Width; z++)
        {
            int index = GetIndex(x, y, z);
            ushort currentVoxelId = voxelIDs[index];
            if (currentVoxelId == 0) continue;

            if (IsVoxelTransparent(x, y + 1, z)) CreateFace(Direction.Top,    new Vector3(x, y, z), index);
            if (IsVoxelTransparent(x, y - 1, z)) CreateFace(Direction.Bottom, new Vector3(x, y, z), index);
            if (IsVoxelTransparent(x + 1, y, z)) CreateFace(Direction.Right,  new Vector3(x, y, z), index);
            if (IsVoxelTransparent(x - 1, y, z)) CreateFace(Direction.Left,   new Vector3(x, y, z), index);
            if (IsVoxelTransparent(x, y, z + 1)) CreateFace(Direction.Front,  new Vector3(x, y, z), index);
            if (IsVoxelTransparent(x, y, z - 1)) CreateFace(Direction.Back,   new Vector3(x, y, z), index);
        }
    }

    private void CreateFace(Direction direction, Vector3 voxelLocalPosition, int voxelIndex)
    {
        int vertexIndex = vertices.Length;
        int vertStartIndex = (int)direction * 4;
        Color32 vertexColor = voxelColors[voxelIndex];

        for (int i = 0; i < 4; i++)
        {
            Vector3 vertexPosition = voxelLocalPosition + VoxelData.FaceVertices[vertStartIndex + i];
            Vector2 local_uv = VoxelData.FaceUVs[i];
            vertices.Add(new Vertex(vertexPosition, vertexColor));
        }

        triangles.Add(vertexIndex + VoxelData.TriangleIndices[0]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[1]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[2]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[3]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[4]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[5]);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return true;
        if (x >= 0 && x < Chunk.Width && z >= 0 && z < Chunk.Width) return voxelIDs[GetIndex(x, y, z)] == 0;
        if (x < 0) return !hasNeighborNegX || neighborVoxelsNegX[GetIndex(Chunk.Width - 1, y, z)] == 0;
        if (x >= Chunk.Width) return !hasNeighborPosX || neighborVoxelsPosX[GetIndex(0, y, z)] == 0;
        if (z < 0) return !hasNeighborNegZ || neighborVoxelsNegZ[GetIndex(x, y, Chunk.Width - 1)] == 0;
        if (z >= Chunk.Width) return !hasNeighborPosZ || neighborVoxelsPosZ[GetIndex(x, y, 0)] == 0;
        return true;
    }

    private int GetIndex(int x, int y, int z) => z + x * Chunk.Width + y * Chunk.Width * Chunk.Width;
}

public static class VoxelData
{
    public static readonly Vector3[] FaceVertices = {
        new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0), // Back
        new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1), // Front
        new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), // Top
        new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), // Bottom
        new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0), // Left
        new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1)  // Right
    };
    public static readonly Vector2[] FaceUVs = {
        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0)
    };
    public static readonly int[] TriangleIndices = { 0, 1, 2, 0, 2, 3 };
}