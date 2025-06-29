using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Rendering;

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    public WorldSettingsSO worldSettings;
    public Transform player;

    [Header("UI")]
    public RawImage minimapDisplay;

    private ChunkManager chunkManager;
    private VoxelGenerationPipeline generationPipeline;
    private float chunkUpdateTimer;

    // --- Логика миникарты ---
    private BiomeMapGPUGenerator mapGenerator;
    private RenderTexture currentMinimapTexture;
    private Vector2Int lastUpdatedPlayerRegion = new Vector2Int(int.MinValue, int.MinValue);
    private bool isMinimapRequestInProgress = false;

    private void Awake()
    {
        Instance = this;
        if (worldSettings == null || player == null) { this.enabled = false; return; }
        if (BiomeManager.Instance == null) { this.enabled = false; return; }

        SetupShaderGlobals();

        // ИСПРАВЛЕНИЕ 1: Инициализируем mapGenerator
        mapGenerator = new BiomeMapGPUGenerator(worldSettings.biomeMapComputeShader);

        BiomeManager.Instance.Initialize(worldSettings);
        generationPipeline = new VoxelGenerationPipeline(worldSettings, BiomeManager.Instance);
        chunkManager = new ChunkManager(generationPipeline, worldSettings, this.transform);
    }
    
    private void Update()
    {
        chunkUpdateTimer -= Time.deltaTime;
        if (chunkUpdateTimer <= 0f)
        {
            chunkUpdateTimer = worldSettings.chunkUpdateInterval;
            var playerChunkPosition = GetChunkPositionFromWorldPos(player.position);
            chunkManager.Update(playerChunkPosition);
        }

        // Вызываем обновление миникарты здесь
        
        TryRequestMinimapUpdate();
        
        generationPipeline.ProcessQueues(chunkManager.GetChunk);
        generationPipeline.CheckCompletedJobs(chunkManager.OnChunkDataReady);
    }
    
    private void OnDestroy()
    {
        generationPipeline?.Dispose();
        chunkManager?.Dispose();
        if (currentMinimapTexture != null) currentMinimapTexture.Release();
    }

    private void TryRequestMinimapUpdate()
    {
        if (minimapDisplay == null || mapGenerator == null || isMinimapRequestInProgress) return;

        int mapSize = BiomeManager.Instance.regionSize;
        Vector2 playerWorldPos = new Vector2(player.position.x, player.position.z);
        
        int regionX = Mathf.FloorToInt(playerWorldPos.x / mapSize);
        int regionZ = Mathf.FloorToInt(playerWorldPos.y / mapSize);
        var currentPlayerRegion = new Vector2Int(regionX, regionZ);

        if (currentPlayerRegion == lastUpdatedPlayerRegion) return;

        lastUpdatedPlayerRegion = currentPlayerRegion;
        isMinimapRequestInProgress = true; // Ставим флаг, что мы в процессе

        var playerChunkPos = GetChunkPositionFromWorldPos(player.position);
        List<BiomeAgent> agents = BiomeManager.Instance.GetBiomesInArea(playerChunkPos);

        if (agents == null || agents.Count == 0)
        {
            isMinimapRequestInProgress = false;
            return;
        }

        float resolutionScale = BiomeManager.Instance.resolutionScale;
        int mapResolution = Mathf.NextPowerOfTwo((int)(mapSize * resolutionScale));
        Vector2 mapOrigin = new Vector2(regionX * mapSize, regionZ * mapSize);

        if (currentMinimapTexture != null) currentMinimapTexture.Release();
        
        currentMinimapTexture = mapGenerator.GenerateMap(agents, mapResolution, mapOrigin, BiomeManager.Instance.neutralZoneWidth);
        
        // --- АСИНХРОННЫЙ ЗАПРОС ---
        AsyncGPUReadback.Request(currentMinimapTexture, 0, (AsyncGPUReadbackRequest request) =>
        {
            if (!request.hasError)
            {
                // Этот код выполнится через несколько кадров, когда GPU закончит работу
                if (minimapDisplay != null && minimapDisplay.material != null)
                {
                    // Создаем временную текстуру, чтобы загрузить в нее данные
                    Texture2D cpuTexture = new Texture2D(request.width, request.height, TextureFormat.RGBAFloat, false);
                    cpuTexture.LoadRawTextureData(request.GetData<float4>());
                    cpuTexture.Apply();
                    
                    // Обновляем текстуру на материале миникарты
                    minimapDisplay.material.mainTexture = cpuTexture;
                    
                    // Важно: старую RenderTargetTexture можно теперь удалить
                    if (currentMinimapTexture != null) currentMinimapTexture.Release();
                }
            }
            else
            {
                Debug.LogError("Ошибка асинхронного чтения с GPU!");
            }
            
            // Сбрасываем флаг, чтобы можно было запросить новое обновление
            isMinimapRequestInProgress = false;
        });
    }
    
    private void SetupShaderGlobals()
    {
        if (worldSettings.worldMaterial != null && worldSettings.worldTextureAtlas != null)
        {
            worldSettings.worldMaterial.SetTexture("_MainTex", worldSettings.worldTextureAtlas);
        }
    }

    private Vector3Int GetChunkPositionFromWorldPos(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Chunk.Width);
        int z = Mathf.FloorToInt(worldPosition.z / Chunk.Width);
        return new Vector3Int(x, 0, z);
    }
}