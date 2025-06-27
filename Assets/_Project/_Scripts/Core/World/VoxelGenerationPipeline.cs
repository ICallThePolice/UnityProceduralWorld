using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using System;

#region Вспомогательные классы запросов
public class AsyncChunkDataRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> PrimaryBlockIDs;
    public NativeArray<ushort> SecondaryBlockIDs;
    public NativeArray<float> BlendFactors;
    public NativeArray<BiomeInstanceBurst> BiomeInstances;

    public void Dispose()
    {
        if (PrimaryBlockIDs.IsCreated) PrimaryBlockIDs.Dispose();
        if (SecondaryBlockIDs.IsCreated) SecondaryBlockIDs.Dispose();
        if (BlendFactors.IsCreated) BlendFactors.Dispose();
        if (BiomeInstances.IsCreated) BiomeInstances.Dispose();
    }
}

public class AsyncChunkMeshRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> PrimaryBlockIDs;
    public NativeArray<ushort> SecondaryBlockIDs;
    public NativeArray<float> BlendFactors;
    public NativeArray<ushort> NeighborPosX;
    public NativeArray<ushort> NeighborNegX;
    public NativeArray<ushort> NeighborPosZ;
    public NativeArray<ushort> NeighborNegZ;
    public NativeList<Vertex> Vertices;
    public NativeList<int> Triangles;

    public void DisposeAll()
    {
        if (PrimaryBlockIDs.IsCreated) PrimaryBlockIDs.Dispose();
        if (SecondaryBlockIDs.IsCreated) SecondaryBlockIDs.Dispose();
        if (BlendFactors.IsCreated) BlendFactors.Dispose();
        if (NeighborPosX.IsCreated) NeighborPosX.Dispose();
        if (NeighborNegX.IsCreated) NeighborNegX.Dispose();
        if (NeighborPosZ.IsCreated) NeighborPosZ.Dispose();
        if (NeighborNegZ.IsCreated) NeighborNegZ.Dispose();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
    }
}
#endregion

public class VoxelGenerationPipeline
{
    public event Action<Chunk, Mesh> OnChunkMeshReady;
    private readonly WorldSettingsSO settings;
    private readonly BiomeManager biomeManager;
    private NativeArray<Vector2> voxelIdToUvMap; // Карта ID -> UV
    private NativeArray<Color32> voxelIdToColorMap;
    private NativeArray<Vector4> voxelIdToEmissionDataMap;
    private NativeArray<Vector4> voxelIdToGapColorMap;
    private NativeArray<Vector2> voxelIdToMaterialPropsMap;
    private NativeArray<VoxelCategory> voxelIdToCategoryMap;

    private readonly List<Chunk> chunksToGenerateData = new List<Chunk>();
    private readonly List<Chunk> chunksToGenerateMesh = new List<Chunk>();
    private readonly List<AsyncChunkDataRequest> runningDataJobs = new List<AsyncChunkDataRequest>();
    private readonly List<AsyncChunkMeshRequest> runningMeshJobs = new List<AsyncChunkMeshRequest>();

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        this.biomeManager = biomeManager;

        ushort maxId = 0;
        foreach (var voxelType in settings.voxelTypes)
        {
            if (voxelType != null && voxelType.ID > maxId) maxId = voxelType.ID;
        }
        
        int arraySize = (int)maxId + 1;
        voxelIdToUvMap = new NativeArray<Vector2>(arraySize, Allocator.Persistent);
        voxelIdToColorMap = new NativeArray<Color32>(arraySize, Allocator.Persistent);
        voxelIdToEmissionDataMap = new NativeArray<Vector4>(arraySize, Allocator.Persistent);
        voxelIdToGapColorMap = new NativeArray<Vector4>(arraySize, Allocator.Persistent);
        voxelIdToMaterialPropsMap = new NativeArray<Vector2>(arraySize, Allocator.Persistent);
        voxelIdToCategoryMap = new NativeArray<VoxelCategory>(arraySize, Allocator.Persistent);

        Vector2 atlasTileSize = new Vector2(1.0f / 2, 1.0f / 2);

        if (settings.voxelTypes != null)
        {
            foreach (var voxelType in settings.voxelTypes)
            {
                if (voxelType == null || voxelType.ID >= arraySize) continue;
                if (voxelType.ID == 0) {
                    voxelIdToCategoryMap[0] = voxelType.category;
                    continue;
                }
                
                Vector2 uv = new Vector2(voxelType.textureAtlasCoord.x * atlasTileSize.x, voxelType.textureAtlasCoord.y * atlasTileSize.y);
                voxelIdToUvMap[voxelType.ID] = uv;
                voxelIdToColorMap[voxelType.ID] = voxelType.color;
                voxelIdToCategoryMap[voxelType.ID] = voxelType.category;
                
                Color emissionColor = voxelType.emissionColor;
                voxelIdToEmissionDataMap[voxelType.ID] = new Vector4(emissionColor.r, emissionColor.g, emissionColor.b, voxelType.emissionStrength);
                voxelIdToGapColorMap[voxelType.ID] = new Vector4(voxelType.gapColor.r, voxelType.gapColor.g, voxelType.gapColor.b, voxelType.gapColor.a);
                voxelIdToMaterialPropsMap[voxelType.ID] = new Vector2(voxelType.smoothness, voxelType.metallic);
            }
        }
    }

    public void RequestDataGeneration(Chunk chunk) { if (!chunksToGenerateData.Contains(chunk)) chunksToGenerateData.Add(chunk); }
    public void RequestMeshGeneration(Chunk chunk) { if (!chunksToGenerateMesh.Contains(chunk)) chunksToGenerateMesh.Add(chunk); }

    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
    }

    public void ProcessQueues(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue(playerChunkPos);
        ProcessMeshGenerationQueue(playerChunkPos, getChunkCallback);
    }
    
    private void ProcessDataGenerationQueue(Vector3Int playerChunkPos)
    {
        if (runningDataJobs.Count >= SystemInfo.processorCount || chunksToGenerateData.Count == 0) return;
        
        Chunk chunkToProcess = chunksToGenerateData[0];
        chunksToGenerateData.RemoveAt(0);

        List<BiomeInstance> relevantBiomes = biomeManager.GetBiomesInArea(chunkToProcess.chunkPosition, settings.renderDistance);
        var biomeInstancesForJob = new NativeArray<BiomeInstanceBurst>(relevantBiomes.Count, Allocator.Persistent);
        for (int i = 0; i < relevantBiomes.Count; i++)
        {
            var instance = relevantBiomes[i];
            biomeInstancesForJob[i] = new BiomeInstanceBurst
            {
                biomeID = instance.settings.biome.biomeID,
                position = instance.position,
                influenceRadius = instance.calculatedRadius,
                contrast = instance.calculatedContrast, // Это поле можно будет использовать для более сложных переходов
                blockID = instance.settings.biome.BiomeBlock.ID,
                coreRadiusPercentage = instance.coreRadiusPercentage,
                sharpness = instance.sharpness
            };
        }

        var primaryIDs = new NativeArray<ushort>(Chunk.Width * Chunk.Height * Chunk.Width, Allocator.Persistent);
        var secondaryIDs = new NativeArray<ushort>(primaryIDs.Length, Allocator.Persistent);
        var blendFactors = new NativeArray<float>(primaryIDs.Length, Allocator.Persistent);

        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        var job = new GenerationJob
        {
            chunkPosition = chunkToProcess.chunkPosition,
            heightMapNoise = heightNoise,
            biomeInstances = biomeInstancesForJob,
            globalBiomeBlockID = settings.globalBiomeBlock.ID,
            voxelIdToCategoryMap = this.voxelIdToCategoryMap,
            primaryBlockIDs = primaryIDs,
            secondaryBlockIDs = secondaryIDs,
            blendFactors = blendFactors
        };

        var handle = job.Schedule();
        runningDataJobs.Add(new AsyncChunkDataRequest
        {
            JobHandle = handle,
            TargetChunk = chunkToProcess,
            PrimaryBlockIDs = primaryIDs,
            SecondaryBlockIDs = secondaryIDs,
            BlendFactors = blendFactors,
            BiomeInstances = biomeInstancesForJob,
        });
    }
    
    // Главный цикл обработки очереди на создание меша
    private void ProcessMeshGenerationQueue(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
{
    if (runningMeshJobs.Count >= SystemInfo.processorCount || chunksToGenerateMesh.Count == 0) return;
    
    Chunk chunkToProcess = chunksToGenerateMesh[0];

    Chunk nPosX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(1, 0, 0));
    Chunk nNegX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(-1, 0, 0));
    Chunk nPosZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, 1));
    Chunk nNegZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, -1));

    bool allNeighborsReady = (nPosX == null || nPosX.isDataGenerated) && (nNegX == null || nNegX.isDataGenerated) && (nPosZ == null || nPosZ.isDataGenerated) && (nNegZ == null || nNegZ.isDataGenerated);

    if (allNeighborsReady)
    {
        chunksToGenerateMesh.RemoveAt(0);

        // Создаем NativeArray-копии здесь, прямо перед запуском джоба
        var primaryIDs = new NativeArray<ushort>(chunkToProcess.primaryBlockIDs, Allocator.Persistent);
        var secondaryIDs = new NativeArray<ushort>(chunkToProcess.secondaryBlockIDs, Allocator.Persistent);
        var blendFactors = new NativeArray<float>(chunkToProcess.blendFactors, Allocator.Persistent);
        
        // --- ИСПРАВЛЕНИЕ: Вместо GetNeighborData создаем копии здесь ---
        var neighborDataPosX = (nPosX != null && nPosX.isDataGenerated) ? new NativeArray<ushort>(nPosX.primaryBlockIDs, Allocator.Persistent) : new NativeArray<ushort>(0, Allocator.Persistent);
        var neighborDataNegX = (nNegX != null && nNegX.isDataGenerated) ? new NativeArray<ushort>(nNegX.primaryBlockIDs, Allocator.Persistent) : new NativeArray<ushort>(0, Allocator.Persistent);
        var neighborDataPosZ = (nPosZ != null && nPosZ.isDataGenerated) ? new NativeArray<ushort>(nPosZ.primaryBlockIDs, Allocator.Persistent) : new NativeArray<ushort>(0, Allocator.Persistent);
        var neighborDataNegZ = (nNegZ != null && nNegZ.isDataGenerated) ? new NativeArray<ushort>(nNegZ.primaryBlockIDs, Allocator.Persistent) : new NativeArray<ushort>(0, Allocator.Persistent);

        var vertices = new NativeList<Vertex>(Allocator.Persistent);
        var triangles = new NativeList<int>(Allocator.Persistent);

        var job = new MeshingJob
        {
            primaryBlockIDs = primaryIDs,
            secondaryBlockIDs = secondaryIDs,
            blendFactors = blendFactors,
            
            voxelIdToCategoryMap = this.voxelIdToCategoryMap,
            voxelIdToUvMap = this.voxelIdToUvMap,
            voxelIdToColorMap = this.voxelIdToColorMap,
            voxelIdToEmissionDataMap = this.voxelIdToEmissionDataMap,
            voxelIdToGapColorMap = this.voxelIdToGapColorMap,
            voxelIdToMaterialPropsMap = this.voxelIdToMaterialPropsMap,
            
            atlasTileSize = new Vector2(1.0f / 16f, 1.0f / 16f),
            
            neighborVoxelsPosX = neighborDataPosX,
            neighborVoxelsNegX = neighborDataNegX,
            neighborVoxelsPosZ = neighborDataPosZ,
            neighborVoxelsNegZ = neighborDataNegZ,

            vertices = vertices,
            triangles = triangles
        };

        var handle = job.Schedule();
        
        runningMeshJobs.Add(new AsyncChunkMeshRequest
        {
            JobHandle = handle,
            TargetChunk = chunkToProcess,
            PrimaryBlockIDs = primaryIDs,
            SecondaryBlockIDs = secondaryIDs,
            BlendFactors = blendFactors,
            NeighborPosX = neighborDataPosX,
            NeighborNegX = neighborDataNegX,
            NeighborPosZ = neighborDataPosZ,
            NeighborNegZ = neighborDataNegZ,
            Vertices = vertices,
            Triangles = triangles
        });
    }
}
    
    public void CheckCompletedJobs(Action<Chunk> onDataReadyCallback)
    {
        CheckCompletedDataJobs(onDataReadyCallback);
        CheckCompletedMeshJobs();
    }
    private void CheckCompletedDataJobs(Action<Chunk> onDataReadyCallback)
    {
        for (int i = runningDataJobs.Count - 1; i >= 0; i--)
        {
            var request = runningDataJobs[i];
            if (request.JobHandle.IsCompleted)
            {
                request.JobHandle.Complete();
                request.TargetChunk.SetPrimaryVoxelIDs(request.PrimaryBlockIDs.ToArray());
                request.TargetChunk.secondaryBlockIDs = request.SecondaryBlockIDs.ToArray();
                request.TargetChunk.blendFactors = request.BlendFactors.ToArray();
                request.TargetChunk.isDataGenerated = true;

                onDataReadyCallback(request.TargetChunk);
                request.Dispose();
                runningDataJobs.RemoveAt(i);
            }
        }
    }

    private void CheckCompletedMeshJobs()
    {
        for (int i = runningMeshJobs.Count - 1; i >= 0; i--)
        {
            var request = runningMeshJobs[i];
            if (request.JobHandle.IsCompleted)
            {
                request.JobHandle.Complete();
                Mesh mesh = new Mesh();
                if (request.Vertices.Length > 0 && request.Triangles.Length > 0)
                {
                    var vertexAttributes = new[]
                    {
                        new VertexAttributeDescriptor(VertexAttribute.Position,   VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal,     VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Tangent,    VertexAttributeFormat.Float32, 4), // Используем Float32 для тангента
                        new VertexAttributeDescriptor(VertexAttribute.Color,      VertexAttributeFormat.UNorm8,  4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0,  VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord1,  VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord2,  VertexAttributeFormat.Float32, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord3,  VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord4,  VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord5,  VertexAttributeFormat.Float32, 2)
                    };

                    mesh.SetVertexBufferParams(request.Vertices.Length, vertexAttributes);
                    mesh.SetIndexBufferParams(request.Triangles.Length, IndexFormat.UInt32);

                    mesh.SetVertexBufferData(request.Vertices.AsArray(), 0, 0, request.Vertices.Length);
                    mesh.SetIndexBufferData(request.Triangles.AsArray(), 0, 0, request.Triangles.Length);

                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, request.Triangles.Length));
                    
                    mesh.RecalculateBounds();
                }
                
                OnChunkMeshReady?.Invoke(request.TargetChunk, mesh);
                request.DisposeAll();
                runningMeshJobs.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        foreach (var request in runningDataJobs)
        {
            request.JobHandle.Complete();
            request.Dispose();
        }
        foreach (var request in runningMeshJobs)
        {
            request.JobHandle.Complete();
            request.DisposeAll();
        }

        if (voxelIdToUvMap.IsCreated) voxelIdToUvMap.Dispose();
        if (voxelIdToColorMap.IsCreated) voxelIdToColorMap.Dispose();
        if (voxelIdToEmissionDataMap.IsCreated) voxelIdToEmissionDataMap.Dispose();
        if (voxelIdToGapColorMap.IsCreated) voxelIdToGapColorMap.Dispose();
        if (voxelIdToMaterialPropsMap.IsCreated) voxelIdToMaterialPropsMap.Dispose();
        if (voxelIdToCategoryMap.IsCreated) voxelIdToCategoryMap.Dispose();
    }
}