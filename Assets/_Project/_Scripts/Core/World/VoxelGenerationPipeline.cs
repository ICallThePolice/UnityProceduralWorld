// --- ФАЙЛ: VoxelGenerationPipeline.cs ---

using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using System;

#region Вспомогательные классы запросов
/// Хранит состояние запущенной задачи на генерацию данных о вокселях.
public class AsyncChunkDataRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> VoxelIDs;
    public NativeArray<BiomeInstanceBurst> BiomeInstances;
    public NativeArray<ArtifactInstanceBurst> ArtifactInstances;

    public void Dispose()
    {
        VoxelIDs.Dispose();
        BiomeInstances.Dispose();
        ArtifactInstances.Dispose();
    }
}
/// Хранит состояние запущенной задачи на генерацию меша.
public class AsyncChunkMeshRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> VoxelIDs;
    public NativeArray<ushort> NeighborPosX, NeighborNegX, NeighborPosZ, NeighborNegZ;
    public NativeList<Vertex> Vertices;
    public NativeList<int> Triangles;

    public void DisposeAll()
    {
        VoxelIDs.Dispose();
        if (NeighborPosX.IsCreated) NeighborPosX.Dispose();
        if (NeighborNegX.IsCreated) NeighborNegX.Dispose();
        if (NeighborPosZ.IsCreated) NeighborPosZ.Dispose();
        if (NeighborNegZ.IsCreated) NeighborNegZ.Dispose();
        Vertices.Dispose();
        Triangles.Dispose();
    }
}
#endregion
/// Управляет "конвейером" генерации: очередями, запуском Jobs и обработкой результатов.
/// Не является MonoBehaviour.
public class VoxelGenerationPipeline
{
    public event Action<Chunk, Mesh> OnChunkMeshReady;
    private readonly WorldSettingsSO settings;
    private readonly BiomeManager biomeManager;
    private readonly NativeArray<Vector2Int> voxelUvCoordinates;
    private readonly AdditiveBiomeGenerator additiveGenerator = new AdditiveBiomeGenerator();
    private readonly SubtractiveBiomeGenerator subtractiveGenerator = new SubtractiveBiomeGenerator();
    private readonly ReplaceBiomeGenerator replaceGenerator = new ReplaceBiomeGenerator();
    private readonly MiniCraterGenerator craterGenerator = new MiniCraterGenerator();
    private readonly FloatingIsletGenerator isletGenerator = new FloatingIsletGenerator();
    private readonly List<Chunk> chunksToGenerateData = new List<Chunk>();
    private readonly List<Chunk> chunksToGenerateMesh = new List<Chunk>();
    private readonly List<AsyncChunkDataRequest> runningDataJobs = new List<AsyncChunkDataRequest>();
    private readonly List<AsyncChunkMeshRequest> runningMeshJobs = new List<AsyncChunkMeshRequest>();

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        this.biomeManager = biomeManager;
        
        ushort maxId = 0;
        foreach (var voxelType in settings.voxelTypes) { if (voxelType != null && voxelType.ID > maxId) maxId = voxelType.ID; }
        voxelUvCoordinates = new NativeArray<Vector2Int>(maxId + 1, Allocator.Persistent);
        foreach (var voxelType in settings.voxelTypes) { if (voxelType != null) voxelUvCoordinates[voxelType.ID] = voxelType.textureAtlasCoord; }
    }

    /// Публичный метод для добавления нового чанка в очередь на генерацию данных.
    public void RequestDataGeneration(Chunk chunk) { if (!chunksToGenerateData.Contains(chunk)) chunksToGenerateData.Add(chunk); }
    public void RequestMeshGeneration(Chunk chunk) { if (!chunksToGenerateMesh.Contains(chunk)) chunksToGenerateMesh.Add(chunk); }

    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
    }

    /// Главный метод обработки очередей, вызывается каждый кадр из WorldController.
    public void ProcessQueues(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue(playerChunkPos);
        ProcessMeshGenerationQueue(playerChunkPos, getChunkCallback);
    }
    // Обрабатывает ОДИН чанк из очереди данных за вызов
    private void ProcessDataGenerationQueue(Vector3Int playerChunkPos)
    {
        int jobsToStart = Mathf.Min(settings.maxDataJobsPerFrame, SystemInfo.processorCount - runningDataJobs.Count);

        for (int j = 0; j < jobsToStart && chunksToGenerateData.Count > 0; j++)
        {
            chunksToGenerateData.Sort((a, b) => Vector3Int.Distance(a.chunkPosition, playerChunkPos).CompareTo(Vector3Int.Distance(b.chunkPosition, playerChunkPos)));
            Chunk chunkToProcess = chunksToGenerateData[0];
            chunksToGenerateData.RemoveAt(0);

            // 1. Получаем "чистые" ядра биомов из менеджера.
            List<BiomeInstance> relevantBiomes = biomeManager.GetBiomesInArea(chunkToProcess.chunkPosition, settings.renderDistance);

            // 2. Объявляем тот самый список 'artifactListToFill', о котором вы спрашивали.
            // Он создается здесь, пустой, для каждой новой генерации.
            var allRelevantArtifacts = new List<ArtifactInstance>();

            // 3. Запускаем процесс размещения артефактов, который заполнит наш новый список.
            foreach (var biome in relevantBiomes)
            {
                // Детерминированно создаем Random для каждого биома.
                Vector2Int regionCoords = new Vector2Int(
                    Mathf.FloorToInt(biome.position.x / biomeManager.regionSize),
                    Mathf.FloorToInt(biome.position.y / biomeManager.regionSize)
                );
                // 4. Исправляем ошибку с 'worldSettings'. У нас есть доступ к `settings` этого класса.
                int regionSeed = this.settings.heightmapNoiseSettings.seed + regionCoords.x * 16777619 + regionCoords.y * 3145739;
                var random = new System.Random(regionSeed);

                // 5. Вызываем наш новый публичный метод и передаем ему список для заполнения.
                biomeManager.PlaceArtifacts(biome, random, relevantBiomes, allRelevantArtifacts);

                biomeManager.lastGeneratedArtifacts = allRelevantArtifacts;
            }

            // 6. Готовим данные для Job, используя заполненный список allRelevantArtifacts.
            // Этот код теперь тоже будет работать правильно.
            var biomeInstancesForJob = new NativeArray<BiomeInstanceBurst>(relevantBiomes.Count, Allocator.Persistent);
            var artifactInstancesForJob = new NativeArray<ArtifactInstanceBurst>(allRelevantArtifacts.Count, Allocator.Persistent);

            for (int i = 0; i < relevantBiomes.Count; i++)
            {
                var instance = relevantBiomes[i];
                biomeInstancesForJob[i] = new BiomeInstanceBurst
                {
                    position = instance.position,
                    influenceRadius = instance.calculatedRadius,
                    aggressiveness = instance.calculatedAggressiveness,
                    tiers = instance.calculatedTiers,
                    contrast = instance.calculatedContrast,
                    isInverted = instance.isInverted,
                    biomeHighestPoint = instance.biomeHighestPoint,

                    surfaceVoxelID = instance.settings.biome.SurfaceVoxel.ID,
                    subSurfaceVoxelID = instance.settings.biome.SubSurfaceVoxel.ID,
                    mainVoxelID = instance.settings.biome.MainVoxel.ID,
                    subSurfaceDepth = instance.settings.biome.SubSurfaceDepth,
                    terrainModificationType = instance.settings.biome.terrainModificationType,
                    verticalDisplacementScale = instance.settings.biome.verticalDisplacementScale,
                    tierRadii = instance.calculatedTierRadii
                };
            }

            for (int i = 0; i < allRelevantArtifacts.Count; i++)
            {
                var artifact = allRelevantArtifacts[i];
                artifactInstancesForJob[i] = new ArtifactInstanceBurst
                {
                    position = artifact.position,
                    artifactType = artifact.settings.artifactType,
                    size = artifact.calculatedSize,
                    height = artifact.calculatedHeight,
                    yOffset = artifact.yOffset,
                    groundHeight = artifact.groundHeight,
                    mainVoxelID = artifact.mainVoxelID
                };
            }

            var dataJobVoxelIDs = new NativeArray<ushort>(Chunk.Width * Chunk.Height * Chunk.Width, Allocator.Persistent);

            var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
            heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
            heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
            heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
            heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

            var chaos = new FastNoiseLite(settings.chaosNoiseSettings.seed);
            chaos.SetFrequency(settings.chaosNoiseSettings.scale);
            chaos.SetFractalType(FastNoiseLite.FractalType.FBm);
            chaos.SetFractalOctaves(settings.chaosNoiseSettings.octaves);
            chaos.SetFractalLacunarity(settings.chaosNoiseSettings.lacunarity);
            chaos.SetFractalGain(settings.chaosNoiseSettings.persistence);
            chaos.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

            var saturation = new FastNoiseLite(settings.saturationNoiseSettings.seed);
            saturation.SetFrequency(settings.saturationNoiseSettings.scale);
            saturation.SetFractalType(FastNoiseLite.FractalType.FBm);
            saturation.SetFractalOctaves(settings.saturationNoiseSettings.octaves);
            saturation.SetFractalLacunarity(settings.saturationNoiseSettings.lacunarity);
            saturation.SetFractalGain(settings.saturationNoiseSettings.persistence);
            saturation.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

            var neutralBiomeBurst = new BiomeInstanceBurst
            {
                surfaceVoxelID = settings.neutralBiome.SurfaceVoxel.ID,
                subSurfaceVoxelID = settings.neutralBiome.SubSurfaceVoxel.ID,
                mainVoxelID = settings.neutralBiome.MainVoxel.ID,
                subSurfaceDepth = settings.neutralBiome.SubSurfaceDepth,
                terrainModificationType = settings.neutralBiome.terrainModificationType,
                verticalDisplacementScale = settings.neutralBiome.verticalDisplacementScale
            };

            // Создание и запуск Job'а
            var job = new GenerationJob
            {
                chunkPosition = chunkToProcess.chunkPosition,
                heightMapNoise = heightNoise,
                neutralBiome = neutralBiomeBurst,
                biomeInstances = biomeInstancesForJob,
                artifactInstances = artifactInstancesForJob,
                resultingVoxelIDs = dataJobVoxelIDs,

                // --- ПЕРЕДАЕМ ЭКЗЕМПЛЯРЫ ГЕНЕРАТОРОВ ---
                additiveGenerator = this.additiveGenerator,
                subtractiveGenerator = this.subtractiveGenerator,
                replaceGenerator = this.replaceGenerator,
                craterGenerator = this.craterGenerator,
                isletGenerator = this.isletGenerator
            };

            var handle = job.Schedule(dataJobVoxelIDs.Length, 64);
            runningDataJobs.Add(new AsyncChunkDataRequest
            {
                JobHandle = handle,
                TargetChunk = chunkToProcess,
                VoxelIDs = dataJobVoxelIDs,
                BiomeInstances = biomeInstancesForJob,
                ArtifactInstances = artifactInstancesForJob
            });
        }
    }

    // Главный цикл обработки очереди на создание меша
    private void ProcessMeshGenerationQueue(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        int jobsToStart = Mathf.Min(settings.maxMeshJobsPerFrame, SystemInfo.processorCount - runningMeshJobs.Count);

        for (int j = 0; j < jobsToStart && chunksToGenerateMesh.Count > 0; j++)
        {

            chunksToGenerateMesh.Sort((a, b) => Vector3Int.Distance(a.chunkPosition, playerChunkPos).CompareTo(Vector3Int.Distance(b.chunkPosition, playerChunkPos)));
            Chunk chunkToProcess = chunksToGenerateMesh[0];

            Chunk nPosX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(1, 0, 0));
            Chunk nNegX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(-1, 0, 0));
            Chunk nPosZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, 1));
            Chunk nNegZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, -1));

            bool allNeighborsReady = (nPosX == null || nPosX.isDataGenerated) && (nNegX == null || nNegX.isDataGenerated) && (nPosZ == null || nPosZ.isDataGenerated) && (nNegZ == null || nNegZ.isDataGenerated);

            if (allNeighborsReady)
            {
                chunksToGenerateMesh.RemoveAt(0);

                // 1. Выделяем всю необходимую память ПЕРЕД блоком try
                var jobVoxelIDs = new NativeArray<ushort>(chunkToProcess.GetAllVoxelIDs(), Allocator.Persistent);
                var neighborDataPosX = GetNeighborData(nPosX);
                var neighborDataNegX = GetNeighborData(nNegX);
                var neighborDataPosZ = GetNeighborData(nPosZ);
                var neighborDataNegZ = GetNeighborData(nNegZ);
                var vertices = new NativeList<Vertex>(Allocator.Persistent);
                var triangles = new NativeList<int>(Allocator.Persistent);

                bool success = false;
                try
                {
                    // 2. В блоке try мы выполняем основную работу: создаем и запускаем задачу
                    var job = new MeshingJob
                    {
                        voxelIDs = jobVoxelIDs,
                        voxelUvCoordinates = this.voxelUvCoordinates,
                        hasNeighborPosX = nPosX != null,
                        hasNeighborNegX = nNegX != null,
                        hasNeighborPosZ = nPosZ != null,
                        hasNeighborNegZ = nNegZ != null,
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
                        VoxelIDs = jobVoxelIDs,
                        NeighborPosX = neighborDataPosX,
                        NeighborNegX = neighborDataNegX,
                        NeighborPosZ = neighborDataPosZ,
                        NeighborNegZ = neighborDataNegZ,
                        Vertices = vertices,
                        Triangles = triangles
                    });

                    // 3. Только если все прошло успешно, помечаем операцию как успешную
                    success = true;
                }
                finally
                {
                    // 4. Блок finally выполнится В ЛЮБОМ СЛУЧАЕ.
                    // Если операция не была успешной, значит, произошла ошибка,
                    // и мы должны немедленно очистить всю память, которую выделили.
                    if (!success)
                    {
                        if (jobVoxelIDs.IsCreated) jobVoxelIDs.Dispose();
                        if (neighborDataPosX.IsCreated) neighborDataPosX.Dispose();
                        if (neighborDataNegX.IsCreated) neighborDataNegX.Dispose();
                        if (neighborDataPosZ.IsCreated) neighborDataPosZ.Dispose();
                        if (neighborDataNegZ.IsCreated) neighborDataNegZ.Dispose();
                        if (vertices.IsCreated) vertices.Dispose();
                        if (triangles.IsCreated) triangles.Dispose();
                    }
                }
            }
        }
    }

    // Методы проверки завершенных Job'ов
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
                // Эта логика остается. Если чанк был отменен, OnChunkMeshReady его проигнорирует.
                request.TargetChunk.SetAllVoxelIDs(request.VoxelIDs.ToArray());
                request.TargetChunk.isDataGenerated = true;
                onDataReadyCallback(request.TargetChunk);
                
                // Главное - память всегда освобождается.
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
                if (request.Vertices.Length > 0)
                {
                    // Описываем формат вершины (позиция + UV)
                    var vertexAttributes = new VertexAttributeDescriptor[]
                    {
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                    };

                    mesh.SetVertexBufferParams(request.Vertices.Length, vertexAttributes);
                    mesh.SetIndexBufferParams(request.Triangles.Length, IndexFormat.UInt32);

                    // Копируем данные напрямую из Native-контейнеров
                    mesh.SetVertexBufferData(request.Vertices.AsArray(), 0, 0, request.Vertices.Length);
                    mesh.SetIndexBufferData(request.Triangles.AsArray(), 0, 0, request.Triangles.Length);

                    // Определяем геометрию
                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, request.Triangles.Length));

                    // Финальные расчеты
                    mesh.RecalculateBounds();
                    mesh.RecalculateNormals();
                }
                
                // OnChunkMeshReady - это наш "предохранитель". Он вызовется в любом случае.
                OnChunkMeshReady?.Invoke(request.TargetChunk, mesh);

                // А память будет НАДЕЖНО очищена здесь, вне зависимости от того,
                // нужен нам этот чанк или нет. Это решает проблему утечки.
                request.DisposeAll();
                runningMeshJobs.RemoveAt(i);
            }
        }
    }

    // Очистка всех Native-ресурсов при закрытии
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
        if (voxelUvCoordinates.IsCreated) voxelUvCoordinates.Dispose();
    }

    private NativeArray<ushort> GetNeighborData(Chunk neighbor)
    {
        if (neighbor != null && neighbor.isDataGenerated)
            return new NativeArray<ushort>(neighbor.GetAllVoxelIDs(), Allocator.Persistent);
        return new NativeArray<ushort>(0, Allocator.Persistent);
    }
}