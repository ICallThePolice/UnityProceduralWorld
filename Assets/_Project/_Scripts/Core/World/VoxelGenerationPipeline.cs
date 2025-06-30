using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class VoxelGenerationPipeline
{
    public event Action<Chunk, Mesh> OnChunkMeshReady;
    private readonly WorldSettingsSO settings;
    private readonly BiomeManager biomeManager;

    private readonly List<Chunk> chunksToGenerateData = new List<Chunk>();
    private readonly List<Chunk> chunksToGenerateMesh = new List<Chunk>();
    private readonly List<AsyncChunkDataRequest> runningDataJobs = new List<AsyncChunkDataRequest>();
    private readonly List<AsyncChunkMeshRequest> runningMeshJobs = new List<AsyncChunkMeshRequest>();

    private NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    private NativeArray<VoxelOverlayDataBurst> voxelOverlayMap;

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        this.biomeManager = biomeManager;
        InitializeVoxelTypeMap();
        InitializeVoxelOverlayMap();
    }

    public void RequestDataGeneration(Chunk chunk) { if (!chunksToGenerateData.Contains(chunk) && !chunk.isDataGenerated) chunksToGenerateData.Add(chunk); }
    public void RequestMeshGeneration(Chunk chunk) { if (!chunksToGenerateMesh.Contains(chunk) && !chunk.isMeshGenerated) chunksToGenerateMesh.Add(chunk); }
    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(c => c.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(c => c.chunkPosition == chunkPos);
    }

    #region Initialization
    private void InitializeVoxelTypeMap()
    {
        if (settings.voxelTypes == null || settings.voxelTypes.Count == 0) return;
        ushort maxId = 0;
        foreach (var voxelType in settings.voxelTypes)
        {
            if (voxelType != null && voxelType.ID > maxId) maxId = voxelType.ID;
        }
        voxelTypeMap = new NativeArray<VoxelTypeDataBurst>(maxId + 1, Allocator.Persistent);
        foreach (var voxelType in settings.voxelTypes)
        {
            if (voxelType == null) continue;
            voxelTypeMap[voxelType.ID] = new VoxelTypeDataBurst
            {
                id = voxelType.ID, isSolid = voxelType.isSolid, baseColor = voxelType.baseColor,
                baseUV = new float2(voxelType.textureAtlasCoord.x, voxelType.textureAtlasCoord.y),
                gapWidth = voxelType.GapWidth, gapColor = voxelType.GapColor,
                bevelData = new float3(voxelType.BevelWidth, voxelType.BevelStrength, voxelType.BevelDirection)
            };
        }
    }

    private void InitializeVoxelOverlayMap()
    {
        if (settings.voxelOverlays == null || settings.voxelOverlays.Count == 0)
        {
            voxelOverlayMap = new NativeArray<VoxelOverlayDataBurst>(0, Allocator.Persistent);
            return;
        }
        ushort maxId = 0;
        foreach (var overlay in settings.voxelOverlays)
        {
            if (overlay != null && overlay.OverlayID > maxId) maxId = overlay.OverlayID;
        }
        voxelOverlayMap = new NativeArray<VoxelOverlayDataBurst>(maxId + 1, Allocator.Persistent);
        foreach (var overlay in settings.voxelOverlays)
        {
            if (overlay == null) continue;
            voxelOverlayMap[overlay.OverlayID] = new VoxelOverlayDataBurst
            {
                id = overlay.OverlayID, priority = overlay.Priority, tintColor = overlay.TintColor,
                overlayUV = new float2(overlay.textureAtlasCoord.x, overlay.textureAtlasCoord.y),
                gapWidth = overlay.GapWidth, gapColor = overlay.GapColor,
                materialProps = new float2(overlay.Smoothness, overlay.Metallic),
                emissionData = new float4(overlay.EmissionColor.r, overlay.EmissionColor.g, overlay.EmissionColor.b, overlay.EmissionStrength),
                bevelData = new float3(overlay.BevelWidth, overlay.BevelStrength, overlay.BevelDirection)
            };
        }
    }
    #endregion

    public void ProcessQueues(Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue();
        ProcessMeshGenerationQueue(getChunkCallback);
    }

    /// <summary>
    /// Полностью переработанный метод для подготовки данных чанка.
    /// </summary>
    private void ProcessDataGenerationQueue()
    {
        if (chunksToGenerateData.Count == 0 || runningDataJobs.Count >= settings.maxDataJobsPerFrame) return;
        
        Chunk readyChunk = null;
        Task<Dictionary<int2, int>> completedSimulationTask = null;

        foreach (var chunk in chunksToGenerateData)
        {
            Vector2Int regionCoords = new Vector2Int(
                Mathf.FloorToInt((chunk.chunkPosition.x * Chunk.Width) / (float)biomeManager.regionSize),
                Mathf.FloorToInt((chunk.chunkPosition.z * Chunk.Width) / (float)biomeManager.regionSize));
            
            var simulationTask = biomeManager.GetBiomeSiteGridFor(regionCoords);

            if (simulationTask.IsCompleted)
            {
                readyChunk = chunk;
                completedSimulationTask = simulationTask;
                break;
            }
        }
        
        if (readyChunk == null) return;
        
        chunksToGenerateData.Remove(readyChunk);
        readyChunk.isDataGenerated = true;

        var biomeSiteGrid = completedSimulationTask.Result;
        if (biomeSiteGrid == null || biomeSiteGrid.Count == 0)
        {
            Debug.LogWarning($"Для чанка {readyChunk.chunkPosition} карта биомов пуста. Пропускаем.");
            return;
        }
        
        var primaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var secondaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var blendMap = new NativeArray<float>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        int chunkStartX = readyChunk.chunkPosition.x * Chunk.Width;
        int chunkStartZ = readyChunk.chunkPosition.z * Chunk.Width;

        for (int z = 0; z < Chunk.Width; z++)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                int mapIndex = x + z * Chunk.Width;
                int2 siteCoord = new int2(chunkStartX + x, chunkStartZ + z);

                if (biomeSiteGrid.TryGetValue(siteCoord, out int ownerId) && ownerId != -1)
                {
                    BiomeAgent ownerAgent = biomeManager.GetAgent(ownerId);
                    if (ownerAgent == null) 
                    {
                        primaryMap[mapIndex] = settings.globalBiomeBlock.ID;
                        secondaryMap[mapIndex] = settings.globalBiomeBlock.ID;
                        blendMap[mapIndex] = 0f;
                        continue;
                    }
                    
                    primaryMap[mapIndex] = ownerAgent.settings.biome.BiomeBlock.ID;
                    secondaryMap[mapIndex] = ownerAgent.settings.biome.BiomeBlock.ID;
                    blendMap[mapIndex] = 0f;

                    if (ownerAgent.isPaired)
                    {
                        BiomeAgent partnerAgent = biomeManager.GetAgent(ownerAgent.partnerId);
                        if (partnerAgent == null) continue;
                        
                        BiomeAgent dominant = ownerAgent.initialAggressiveness >= partnerAgent.initialAggressiveness ? ownerAgent : partnerAgent;
                        BiomeAgent subservient = dominant == ownerAgent ? partnerAgent : ownerAgent;
                        float dominanceRatio = subservient.initialAggressiveness / (dominant.initialAggressiveness + 0.001f);
                        float influenceChance = (ownerAgent.uniqueInstanceId == dominant.uniqueInstanceId) ? dominanceRatio * 0.25f : dominanceRatio * 0.5f;

                        if (Mathf.Abs(math.sin(siteCoord.x * 0.1f + siteCoord.y * 0.2f)) < influenceChance) {
                            secondaryMap[mapIndex] = partnerAgent.settings.biome.BiomeBlock.ID;
                            blendMap[mapIndex] = 0.5f;
                        }
                    }
                }
                else 
                {
                    primaryMap[mapIndex] = settings.globalBiomeBlock.ID;
                    secondaryMap[mapIndex] = settings.globalBiomeBlock.ID;
                    blendMap[mapIndex] = 0f;
                }
            }
        }
        
        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        var request = new AsyncChunkDataRequest
        {
            TargetChunk = readyChunk, // <--- ИСПРАВЛЕНО
            primaryBlockIDs = new NativeArray<ushort>(Chunk.Size, Allocator.Persistent),
            finalColors = new NativeArray<Color32>(Chunk.Size, Allocator.Persistent),
            finalUv0s = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalUv1s = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalTexBlends = new NativeArray<float>(Chunk.Size, Allocator.Persistent),
            finalEmissionData = new NativeArray<float4>(Chunk.Size, Allocator.Persistent),
            finalGapColors = new NativeArray<float4>(Chunk.Size, Allocator.Persistent),
            finalMaterialProps = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalGapWidths = new NativeArray<float>(Chunk.Size, Allocator.Persistent),
            finalBevelData = new NativeArray<float3>(Chunk.Size, Allocator.Persistent),
        };

        var job = new GenerationJob
        {
            chunkPosition = readyChunk.chunkPosition,
            heightMapNoise = heightNoise,
            chunkPrimaryIdMap = primaryMap,
            chunkSecondaryIdMap = secondaryMap,
            chunkBlendMap = blendMap,
            voxelTypeMap = this.voxelTypeMap,
            globalBiomeBlockID = settings.globalBiomeBlock.ID,
            primaryBlockIDs = request.primaryBlockIDs,
            finalColors = request.finalColors,
            finalUv0s = request.finalUv0s,
            finalUv1s = request.finalUv1s,
            finalTexBlends = request.finalTexBlends,
            finalEmissionData = request.finalEmissionData,
            finalGapColors = request.finalGapColors,
            finalMaterialProps = request.finalMaterialProps,
            finalGapWidths = request.finalGapWidths,
            finalBevelData = request.finalBevelData
        };
        
        JobHandle handle = job.Schedule();
        var handle1 = primaryMap.Dispose(handle);
        var handle2 = secondaryMap.Dispose(handle);
        var handle3 = blendMap.Dispose(handle);
        request.JobHandle = JobHandle.CombineDependencies(handle1, handle2, handle3);
        
        runningDataJobs.Add(request);
    }

    private void ProcessMeshGenerationQueue(Func<Vector3Int, Chunk> getChunkCallback)
    {
        if (runningMeshJobs.Count >= settings.maxMeshJobsPerFrame || chunksToGenerateMesh.Count == 0) return;

        Chunk chunkToProcess = null;
        int chunkIndexInQueue = -1;

        for (int i = 0; i < chunksToGenerateMesh.Count; i++)
        {
            var potentialChunk = chunksToGenerateMesh[i];
            if (!potentialChunk.isDataGenerated) continue;

            Chunk nPosX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.right);
            Chunk nNegX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.left);
            Chunk nPosY = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.up);
            Chunk nNegY = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.down);
            Chunk nPosZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.forward);
            Chunk nNegZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.back);
            
            bool allNeighborsReady = (nPosX == null || nPosX.isDataGenerated) && (nNegX == null || nNegX.isDataGenerated) &&
                                     (nPosY == null || nPosY.isDataGenerated) && (nNegY == null || nNegY.isDataGenerated) &&
                                     (nPosZ == null || nPosZ.isDataGenerated) && (nNegZ == null || nNegZ.isDataGenerated);

            if (allNeighborsReady)
            {
                chunkToProcess = potentialChunk;
                chunkIndexInQueue = i;
                break;
            }
        }
        
        if (chunkToProcess == null) return;

        chunksToGenerateMesh.RemoveAt(chunkIndexInQueue);
        chunkToProcess.isMeshGenerated = true;

        var request = new AsyncChunkMeshRequest
        {
            TargetChunk = chunkToProcess,
            primaryBlockIDs = new NativeArray<ushort>(chunkToProcess.primaryBlockIDs, Allocator.TempJob),
            finalColors = new NativeArray<Color32>(chunkToProcess.finalColors, Allocator.TempJob),
            finalUv0s = new NativeArray<float2>(chunkToProcess.finalUv0s, Allocator.TempJob),
            finalUv1s = new NativeArray<float2>(chunkToProcess.finalUv1s, Allocator.TempJob),
            finalTexBlends = new NativeArray<float>(chunkToProcess.finalTexBlends, Allocator.TempJob),
            finalEmissionData = new NativeArray<float4>(chunkToProcess.finalEmissionData, Allocator.TempJob),
            finalGapColors = new NativeArray<float4>(chunkToProcess.finalGapColors, Allocator.TempJob),
            finalMaterialProps = new NativeArray<float2>(chunkToProcess.finalMaterialProps, Allocator.TempJob),
            finalGapWidths = new NativeArray<float>(chunkToProcess.finalGapWidths, Allocator.TempJob),
            finalBevelData = new NativeArray<float3>(chunkToProcess.finalBevelData, Allocator.TempJob),

            NeighborPosX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.right),
            NeighborNegX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.left),
            NeighborPosY = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.up),
            NeighborNegY = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.down),
            NeighborPosZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.forward),
            NeighborNegZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.back),

            Vertices = new NativeList<Vertex>(Allocator.Persistent),
            Triangles = new NativeList<int>(Allocator.Persistent)
        };

        var job = new MeshingJob
        {
            primaryBlockIDs = request.primaryBlockIDs,
            finalColors = request.finalColors,
            finalUv0s = request.finalUv0s,
            finalUv1s = request.finalUv1s,
            finalTexBlends = request.finalTexBlends,
            finalEmissionData = request.finalEmissionData,
            finalGapColors = request.finalGapColors,
            finalMaterialProps = request.finalMaterialProps,
            finalGapWidths = request.finalGapWidths,
            finalBevelData = request.finalBevelData,
            neighborVoxelsPosX = request.NeighborPosX,
            neighborVoxelsNegX = request.NeighborNegX,
            neighborVoxelsPosY = request.NeighborPosY,
            neighborVoxelsNegY = request.NeighborNegY,
            neighborVoxelsPosZ = request.NeighborPosZ,
            neighborVoxelsNegZ = request.NeighborNegZ,
            voxelTypeMap = this.voxelTypeMap,
            atlasSizeInTiles = new float2(settings.atlasSizeInTiles.x, settings.atlasSizeInTiles.y),
            vertices = request.Vertices,
            triangles = request.Triangles
        };

        request.JobHandle = job.Schedule();
        runningMeshJobs.Add(request);
    }

    private NativeArray<ushort> GetNeighborData(Func<Vector3Int, Chunk> getChunkCallback, Vector3Int neighborPos)
    {
        Chunk neighbor = getChunkCallback(neighborPos);
        if (neighbor != null && neighbor.isDataGenerated)
        {
            return new NativeArray<ushort>(neighbor.primaryBlockIDs, Allocator.TempJob);
        }
        return new NativeArray<ushort>(0, Allocator.TempJob); // Пустой массив для отсутствующего соседа
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
                var chunk = request.TargetChunk;
                if (chunk != null) // Доп. проверка, что чанк еще существует
                {
                    request.primaryBlockIDs.CopyTo(chunk.primaryBlockIDs);
                    request.finalColors.CopyTo(chunk.finalColors);
                    request.finalUv0s.CopyTo(chunk.finalUv0s);
                    request.finalUv1s.CopyTo(chunk.finalUv1s);
                    request.finalTexBlends.CopyTo(chunk.finalTexBlends);
                    request.finalEmissionData.CopyTo(chunk.finalEmissionData);
                    request.finalGapColors.CopyTo(chunk.finalGapColors);
                    request.finalMaterialProps.CopyTo(chunk.finalMaterialProps);
                    request.finalGapWidths.CopyTo(chunk.finalGapWidths);
                    request.finalBevelData.CopyTo(chunk.finalBevelData);

                    onDataReadyCallback(chunk);
                }

                request.Dispose(); // Очищаем временные NativeArray
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
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 3)
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
        // Завершаем все еще работающие джобы, чтобы избежать ошибок при выходе
        foreach (var request in runningDataJobs)
        {
            request.JobHandle.Complete();
            request.Dispose();
        }
        runningDataJobs.Clear();

        foreach (var request in runningMeshJobs)
        {
            request.JobHandle.Complete();
            request.DisposeAll();
        }
        runningMeshJobs.Clear();

        // Освобождаем память от карт, которые мы храним постоянно
        if (voxelTypeMap.IsCreated) voxelTypeMap.Dispose();
        if (voxelOverlayMap.IsCreated) voxelOverlayMap.Dispose();
    }
}