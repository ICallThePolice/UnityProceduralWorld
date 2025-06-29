using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using Unity.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;

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
    private BiomeMapGPUGenerator mapGenerator;

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        this.biomeManager = biomeManager;
        this.mapGenerator = new BiomeMapGPUGenerator(settings.biomeMapComputeShader);
        InitializeVoxelTypeMap();
        InitializeVoxelOverlayMap();
    }

    public void RequestDataGeneration(Chunk chunk) { if (!chunksToGenerateData.Contains(chunk) && !chunk.isDataGenerated) chunksToGenerateData.Add(chunk); }
    public void RequestMeshGeneration(Chunk chunk) { if (!chunksToGenerateMesh.Contains(chunk) && !chunk.isMeshGenerated) chunksToGenerateMesh.Add(chunk); }
    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
    }

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
                id = voxelType.ID,
                isSolid = voxelType.isSolid,
                baseColor = voxelType.baseColor,
                baseUV = new float2(voxelType.textureAtlasCoord.x, voxelType.textureAtlasCoord.y),
                gapWidth = voxelType.GapWidth,
                gapColor = voxelType.GapColor,
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
                id = overlay.OverlayID,
                priority = overlay.Priority,
                tintColor = overlay.TintColor,
                overlayUV = new float2(overlay.textureAtlasCoord.x, overlay.textureAtlasCoord.y),
                gapWidth = overlay.GapWidth,
                gapColor = overlay.GapColor,
                materialProps = new float2(overlay.Smoothness, overlay.Metallic),
                emissionData = new float4(overlay.EmissionColor.r, overlay.EmissionColor.g, overlay.EmissionColor.b, overlay.EmissionStrength),
                bevelData = new float3(overlay.BevelWidth, overlay.BevelStrength, overlay.BevelDirection)
            };
        }
    }

    public void ProcessQueues(Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue();
        ProcessMeshGenerationQueue(getChunkCallback);
    }

    private void ProcessDataGenerationQueue()
    {
        if (runningDataJobs.Count >= settings.maxMeshJobsPerFrame || chunksToGenerateData.Count == 0) return;

        Chunk chunkToProcess = chunksToGenerateData[0];
        chunksToGenerateData.RemoveAt(0);
        chunkToProcess.isDataGenerated = true;

        List<BiomeAgent> relevantAgents = biomeManager.GetBiomesInArea(chunkToProcess.chunkPosition);
        if (relevantAgents.Count == 0)
        {
            Debug.LogWarning($"Для чанка {chunkToProcess.chunkPosition} не найдено релевантных агентов. Генерация будет пропущена.");
            return;
        }

        int mapSize = biomeManager.regionSize;
        Vector2Int chunkStartWorldPos = new Vector2Int(chunkToProcess.chunkPosition.x * Chunk.Width, chunkToProcess.chunkPosition.z * Chunk.Width);
        
        int regionX = Mathf.FloorToInt((float)chunkStartWorldPos.x / mapSize);
        int regionZ = Mathf.FloorToInt((float)chunkStartWorldPos.y / mapSize);
        Vector2 mapOrigin = new Vector2(regionX * mapSize, regionZ * mapSize);

        RenderTexture biomeMapRT = mapGenerator.GenerateMap(relevantAgents, mapSize, mapOrigin, biomeManager.neutralZoneWidth);

        if (biomeMapRT == null)
        {
            Debug.LogError("Не удалось сгенерировать карту биомов на GPU. GenerateMap вернул null.");
            return;
        }

        Texture2D cpuTexture = new Texture2D(mapSize, mapSize, TextureFormat.RGBAFloat, false);
        RenderTexture.active = biomeMapRT;
        cpuTexture.ReadPixels(new Rect(0, 0, mapSize, mapSize), 0, 0);
        cpuTexture.Apply();
        RenderTexture.active = null;

        NativeArray<float4> fullMapData = cpuTexture.GetRawTextureData<float4>();
        
        var primaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var secondaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var blendMap = new NativeArray<float>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        
        int mapOriginX = regionX * mapSize;
        int mapOriginZ = regionZ * mapSize;
        uint NO_AGENT_ID = 0xFFFFFFFF;
        
        for (int z = 0; z < Chunk.Width; z++)
        {
            for (int x = 0; x < Chunk.Width; x++)
            {
                int worldX = chunkStartWorldPos.x + x;
                int worldZ = chunkStartWorldPos.y + z;

                int mapX = worldX - mapOriginX;
                int mapZ = worldZ - mapOriginZ;

                int chunkMapIndex = x + z * Chunk.Width;
                int fullMapIndex = mapX + mapZ * mapSize;

                if (fullMapIndex >= 0 && fullMapIndex < fullMapData.Length)
                {
                    float4 data = fullMapData[fullMapIndex];

                    int primaryIndex = (int)data.x;
                    int secondaryIndex = (int)data.y;

                    primaryMap[chunkMapIndex] = (primaryIndex == NO_AGENT_ID) ? (ushort)0 : relevantAgents[(int)primaryIndex].settings.biome.biomeID;
                    secondaryMap[chunkMapIndex] = (secondaryIndex == NO_AGENT_ID) ? (ushort)0 : relevantAgents[(int)secondaryIndex].settings.biome.biomeID;
                    blendMap[chunkMapIndex] = data.z;
                }
            }
        }

        // Очищаем временные GPU/CPU объекты
        UnityEngine.Object.Destroy(cpuTexture);
        biomeMapRT.Release();

        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        var request = new AsyncChunkDataRequest
        {
            TargetChunk = chunkToProcess,
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
            chunkPosition = chunkToProcess.chunkPosition,
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
        
        // Планируем удаление всех трех временных массивов после завершения основного джоба
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