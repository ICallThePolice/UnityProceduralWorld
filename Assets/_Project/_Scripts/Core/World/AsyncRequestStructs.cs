using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class AsyncChunkDataRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> primaryBlockIDs;
    public NativeArray<Color32> finalColors;
    public NativeArray<float2> finalUv0s;
    public NativeArray<float2> finalUv1s;
    public NativeArray<float> finalTexBlends;
    public NativeArray<float4> finalEmissionData;
    public NativeArray<float4> finalGapColors;
    public NativeArray<float2> finalMaterialProps;
    public NativeArray<float> finalGapWidths;
    public NativeArray<float3> finalBevelData;
    public NativeArray<ClusterInfoBurst> clustersForJob;
    public NativeArray<float2> allNodesForJob;
    public NativeArray<EdgeInfoBurst> allEdgesForJob;
    public NativeArray<OverlayPlacementDataBurst> OverlayPlacements;

    public void Dispose()
    {
        if (primaryBlockIDs.IsCreated) primaryBlockIDs.Dispose();
        if (finalColors.IsCreated) finalColors.Dispose();
        if (finalUv0s.IsCreated) finalUv0s.Dispose();
        if (finalUv1s.IsCreated) finalUv1s.Dispose();
        if (finalTexBlends.IsCreated) finalTexBlends.Dispose();
        if (finalEmissionData.IsCreated) finalEmissionData.Dispose();
        if (finalGapColors.IsCreated) finalGapColors.Dispose();
        if (finalMaterialProps.IsCreated) finalMaterialProps.Dispose();
        if (finalGapWidths.IsCreated) finalGapWidths.Dispose();
        if (finalBevelData.IsCreated) finalBevelData.Dispose();
        if (clustersForJob.IsCreated) clustersForJob.Dispose();
        if (allNodesForJob.IsCreated) allNodesForJob.Dispose();
        if (allEdgesForJob.IsCreated) allEdgesForJob.Dispose();
        if (OverlayPlacements.IsCreated) OverlayPlacements.Dispose();
    }
}

public class AsyncChunkMeshRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> primaryBlockIDs;
    public NativeArray<Color32> finalColors;
    public NativeArray<float2> finalUv0s;
    public NativeArray<float2> finalUv1s;
    public NativeArray<float> finalTexBlends;
    public NativeArray<float4> finalEmissionData;
    public NativeArray<float4> finalGapColors;
    public NativeArray<float2> finalMaterialProps;
    public NativeArray<float> finalGapWidths;
    public NativeArray<float3> finalBevelData;
    public NativeArray<ushort> NeighborPosX, NeighborNegX, NeighborPosY, NeighborNegY, NeighborPosZ, NeighborNegZ;
    public NativeList<Vertex> Vertices;
    public NativeList<int> Triangles;

    public void DisposeAll()
    {
        if (primaryBlockIDs.IsCreated) primaryBlockIDs.Dispose();
        if (finalColors.IsCreated) finalColors.Dispose();
        if (finalUv0s.IsCreated) finalUv0s.Dispose();
        if (finalUv1s.IsCreated) finalUv1s.Dispose();
        if (finalTexBlends.IsCreated) finalTexBlends.Dispose();
        if (finalEmissionData.IsCreated) finalEmissionData.Dispose();
        if (finalGapColors.IsCreated) finalGapColors.Dispose();
        if (finalMaterialProps.IsCreated) finalMaterialProps.Dispose();
        if (finalGapWidths.IsCreated) finalGapWidths.Dispose();
        if (finalBevelData.IsCreated) finalBevelData.Dispose();
        if (NeighborPosX.IsCreated) NeighborPosX.Dispose();
        if (NeighborNegX.IsCreated) NeighborNegX.Dispose();
        if (NeighborPosY.IsCreated) NeighborPosY.Dispose();
        if (NeighborNegY.IsCreated) NeighborNegY.Dispose();
        if (NeighborPosZ.IsCreated) NeighborPosZ.Dispose();
        if (NeighborNegZ.IsCreated) NeighborNegZ.Dispose();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
    }
}