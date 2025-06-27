using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using System;
using Unity.Mathematics;

#region Вспомогательные классы запросов
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
    public NativeArray<BiomeInstanceBurst> BiomeInstances;
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
        if (BiomeInstances.IsCreated) BiomeInstances.Dispose();
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
    public NativeArray<ushort> NeighborPosX, NeighborNegX, NeighborPosZ, NeighborNegZ;
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
        if (NeighborPosZ.IsCreated) NeighborPosZ.Dispose();
        if (NeighborNegZ.IsCreated) NeighborNegZ.Dispose();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
    }
}
#endregion
///
/// <summary> НАЧАЛО КЛАССА VoxelGenerationPipeline </summary>
///
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
        chunksToGenerateData.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
    }

    private void InitializeVoxelTypeMap()
    {
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
                baseColor = voxelType.baseColor,
                baseUV = new float2(voxelType.textureAtlasCoord.x, voxelType.textureAtlasCoord.y),
                gapWidth = voxelType.GapWidth,
                gapColor = voxelType.GapColor
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
                overlayUV = new float2(0, 0), // ЗАГЛУШКА: Здесь должны быть UV из атласа
                gapWidth = overlay.GapWidth,
                gapColor = overlay.GapColor,
                materialProps = new float2(overlay.Smoothness, overlay.Metallic),
                emissionData = new float4(overlay.EmissionColor.r, overlay.EmissionColor.g, overlay.EmissionColor.b, overlay.EmissionStrength),
                bevelData = new float3(overlay.BevelWidth, overlay.BevelStrength, overlay.BevelDirection)
            };
        }
    }

    public void ProcessQueues(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue(playerChunkPos);
        ProcessMeshGenerationQueue(playerChunkPos, getChunkCallback);
    }

    private void ProcessDataGenerationQueue(Vector3Int playerChunkPos)
    {
        // 1. ПРОВЕРКА УСЛОВИЙ ЗАПУСКА
        // Не запускаем больше джобов, чем есть ядер процессора, и если очередь пуста
        if (runningDataJobs.Count >= SystemInfo.processorCount || chunksToGenerateData.Count == 0) return;

        // 2. ВЫБОР ЧАНКА
        // Берем первый чанк из очереди для обработки
        Chunk chunkToProcess = chunksToGenerateData[0];
        chunksToGenerateData.RemoveAt(0);
        chunkToProcess.isDataGenerated = true; // Помечаем сразу, чтобы не попасть в очередь снова

        // 3. СБОР ДАННЫХ О МИРЕ
        // Получаем информацию о биомах и оверлеях в зоне видимости чанка.
        // ПРИМЕЧАНИЕ: Вам нужно будет реализовать GetOverlaysInArea в BiomeManager
        List<BiomeInstance> relevantBiomes = biomeManager.GetBiomesInArea(chunkToProcess.chunkPosition, settings.renderDistance);
        List<OverlayPlacementDataBurst> relevantOverlays = biomeManager.GetOverlaysInArea(chunkToProcess.chunkPosition, settings.renderDistance);

        // 4. ПОДГОТОВКА NATIVE-КОНТЕЙНЕРОВ ДЛЯ ДЖОБА
        // 4.1. Входные данные о биомах и оверлеях
        var biomeInstancesForJob = new NativeArray<BiomeInstanceBurst>(relevantBiomes.Count, Allocator.Persistent);
        for (int i = 0; i < relevantBiomes.Count; i++)
        {
            var instance = relevantBiomes[i];
            biomeInstancesForJob[i] = new BiomeInstanceBurst
            {
                biomeID = instance.settings.biome.biomeID,
                position = instance.position,
                influenceRadius = instance.calculatedRadius,
                contrast = instance.calculatedContrast,
                blockID = instance.settings.biome.BiomeBlock.ID,
                coreRadiusPercentage = instance.coreRadiusPercentage,
                sharpness = instance.sharpness
            };
        }

        var overlayPlacementsForJob = new NativeArray<OverlayPlacementDataBurst>(relevantOverlays.Count, Allocator.Persistent);
        for (int i = 0; i < relevantOverlays.Count; i++)
        {
            overlayPlacementsForJob[i] = relevantOverlays[i];
        }

        // 4.2. Настройка шума
        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        // 4.3. Создание обертки для запроса, которая будет хранить все ВЫХОДНЫЕ массивы
        int voxelCount = Chunk.Size;
        var request = new AsyncChunkDataRequest
        {
            TargetChunk = chunkToProcess,
            primaryBlockIDs = new NativeArray<ushort>(voxelCount, Allocator.Persistent),
            finalColors = new NativeArray<Color32>(voxelCount, Allocator.Persistent),
            finalUv0s = new NativeArray<float2>(voxelCount, Allocator.Persistent),
            finalUv1s = new NativeArray<float2>(voxelCount, Allocator.Persistent),
            finalTexBlends = new NativeArray<float>(voxelCount, Allocator.Persistent),
            finalEmissionData = new NativeArray<float4>(voxelCount, Allocator.Persistent),
            finalGapColors = new NativeArray<float4>(voxelCount, Allocator.Persistent),
            finalMaterialProps = new NativeArray<float2>(voxelCount, Allocator.Persistent),
            finalGapWidths = new NativeArray<float>(voxelCount, Allocator.Persistent),
            finalBevelData = new NativeArray<float3>(voxelCount, Allocator.Persistent),
            BiomeInstances = biomeInstancesForJob,
            OverlayPlacements = overlayPlacementsForJob,
        };

        // 5. ИНИЦИАЛИЗАЦИЯ И ЗАПУСК ДЖОБА
        var job = new GenerationJob
        {
            chunkPosition = chunkToProcess.chunkPosition,
            heightMapNoise = heightNoise,
            biomeInstances = biomeInstancesForJob,
            overlayPlacements = overlayPlacementsForJob,
            voxelTypeMap = this.voxelTypeMap,
            voxelOverlayMap = this.voxelOverlayMap,
            globalBiomeBlockID = settings.globalBiomeBlock.ID,
            atlasSizeInTiles = new float2(settings.atlasSizeInTiles.x, settings.atlasSizeInTiles.y),
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

        request.JobHandle = job.Schedule();
        runningDataJobs.Add(request);
    }

    /// <summary>
    /// Обрабатывает очередь чанков, ожидающих генерации меша.
    /// </summary>
    /// <param name="playerChunkPos">Позиция чанка игрока (для возможной сортировки в будущем)</param>
    /// <param name="getChunkCallback">Функция для получения экземпляра чанка по его координатам</param>
    private void ProcessMeshGenerationQueue(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        // 1. ПРОВЕРКА УСЛОВИЙ ЗАПУСКА
        if (runningMeshJobs.Count >= settings.maxMeshJobsPerFrame || chunksToGenerateMesh.Count == 0)
        {
            return;
        }

        // 2. ПРОВЕРКА ГОТОВНОСТИ СОСЕДЕЙ
        // Мы не можем просто взять первый чанк. Сначала нужно убедиться, что его соседи готовы.
        // Мы ищем в очереди первый чанк, у которого все соседи уже сгенерировали свои данные.
        Chunk chunkToProcess = null;
        int chunkIndexInQueue = -1;

        for (int i = 0; i < chunksToGenerateMesh.Count; i++)
        {
            var potentialChunk = chunksToGenerateMesh[i];
            // Сам чанк должен быть готов
            if (!potentialChunk.isDataGenerated) continue;

            // Получаем всех четырех горизонтальных соседей
            Chunk nPosX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.right);
            Chunk nNegX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.left);
            Chunk nPosZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.forward);
            Chunk nNegZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.back);

            // Сосед считается "готовым" если он либо не существует (край мира), либо у него сгенерированы данные.
            bool allNeighborsReady =
                (nPosX == null || nPosX.isDataGenerated) &&
                (nNegX == null || nNegX.isDataGenerated) &&
                (nPosZ == null || nPosZ.isDataGenerated) &&
                (nNegZ == null || nNegZ.isDataGenerated);

            if (allNeighborsReady)
            {
                chunkToProcess = potentialChunk;
                chunkIndexInQueue = i;
                break; // Нашли подходящий чанк, выходим из цикла
            }
        }

        // Если не нашли ни одного готового чанка, выходим и ждем следующего кадра.
        if (chunkToProcess == null)
        {
            return;
        }

        // Удаляем выбранный чанк из очереди
        chunksToGenerateMesh.RemoveAt(chunkIndexInQueue);
        chunkToProcess.isMeshGenerated = true; // Помечаем, чтобы не добавить в очередь снова

        // 3. ПОДГОТОВКА ДАННЫХ ДЛЯ ДЖОБА
        // Создаем обертку для запроса. Она будет владеть всеми Native-контейнерами для этого джоба.
        var request = new AsyncChunkMeshRequest
        {
            TargetChunk = chunkToProcess,

            // 3.1. Копируем данные самого чанка в NativeArray
            primaryBlockIDs = new NativeArray<ushort>(chunkToProcess.primaryBlockIDs, Allocator.Persistent),
            overlayIDs = new NativeArray<ushort>(chunkToProcess.overlayIDs, Allocator.Persistent),
            finalColors = new NativeArray<Color32>(chunkToProcess.finalColors, Allocator.Persistent),
            finalUv0s = new NativeArray<float2>(chunkToProcess.finalUv0s, Allocator.Persistent),
            finalUv1s = new NativeArray<float2>(chunkToProcess.finalUv1s, Allocator.Persistent),
            finalTexBlends = new NativeArray<float>(chunkToProcess.finalTexBlends, Allocator.Persistent),
            finalEmissionData = new NativeArray<float4>(chunkToProcess.finalEmissionData, Allocator.Persistent),
            finalGapColors = new NativeArray<float4>(chunkToProcess.finalGapColors, Allocator.Persistent),
            finalMaterialProps = new NativeArray<float2>(chunkToProcess.finalMaterialProps, Allocator.Persistent),
            finalGapWidths = new NativeArray<float>(chunkToProcess.finalGapWidths, Allocator.Persistent),
            finalBevelData = new NativeArray<float3>(chunkToProcess.finalBevelData, Allocator.Persistent),

            voxelTypeMap = new NativeArray<VoxelTypeDataBurst>(this.voxelTypeMap, Allocator.Persistent),
            voxelOverlayMap = new NativeArray<VoxelOverlayDataBurst>(this.voxelOverlayMap, Allocator.Persistent),

            // 3.2. Копируем данные о соседях
            
            NeighborPosX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.right),
            NeighborNegX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.left),
            NeighborPosZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.forward),
            NeighborNegZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.back),

            // 3.3. Создаем выходные контейнеры для результатов работы джоба
            Vertices = new NativeList<Vertex>(Allocator.Persistent),
            Triangles = new NativeList<int>(Allocator.Persistent)
        };

        // 4. ИНИЦИАЛИЗАЦИЯ И ЗАПУСК ДЖОБА
        var job = new MeshingJob
        {
            atlasTileSize = new float2(settings.atlasSizeInTiles.x, settings.atlasSizeInTiles.y),
            primaryBlockIDs = request.primaryBlockIDs,
            overlayIDs = request.overlayIDs,
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
            neighborVoxelsPosZ = request.NeighborPosZ,
            neighborVoxelsNegZ = request.NeighborNegZ,

            voxelTypeMap = this.voxelTypeMap,
            voxelOverlayMap = this.voxelOverlayMap,

            vertices = request.Vertices,
            triangles = request.Triangles
        };

        request.JobHandle = job.Schedule();
        runningMeshJobs.Add(request);
    }

    /// <summary>
    /// Вспомогательный метод для получения данных о соседнем чанке.
    /// </summary>
    /// <returns>NativeArray с ID вокселей соседа или пустой массив, если соседа нет.</returns>
    private NativeArray<ushort> GetNeighborData(Func<Vector3Int, Chunk> getChunkCallback, Vector3Int neighborPos)
    {
        Chunk neighbor = getChunkCallback(neighborPos);
        if (neighbor != null && neighbor.isDataGenerated)
        {
            return new NativeArray<ushort>(neighbor.primaryBlockIDs, Allocator.Persistent);
        }
        return new NativeArray<ushort>(0, Allocator.Persistent);
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
                        new VertexAttributeDescriptor(VertexAttribute.Tangent,    VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.Color,      VertexAttributeFormat.UNorm8,  4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0,  VertexAttributeFormat.Float32, 2), // uv0
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord1,  VertexAttributeFormat.Float32, 2), // uv1
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord2,  VertexAttributeFormat.Float32, 1), // texBlend
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord3,  VertexAttributeFormat.Float32, 4), // emissionData
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord4,  VertexAttributeFormat.Float32, 4), // gapColor
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord5,  VertexAttributeFormat.Float32, 2), // materialProps
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord6,  VertexAttributeFormat.Float32, 1), // gapWidth
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord7,  VertexAttributeFormat.Float32, 3)  // bevelData
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
        if (voxelTypeMap.IsCreated) voxelTypeMap.Dispose();
        if (voxelOverlayMap.IsCreated) voxelOverlayMap.Dispose();
    }
}