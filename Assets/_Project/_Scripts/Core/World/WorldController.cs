using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    [Header("Основные настройки")]
    public WorldSettingsSO worldSettings;
    public Transform player;
    public BiomeMapSO biomeMap;

    [Header("UI для отладки")]
    public RawImage minimapDisplay;
    public ComputeShader minimapShader;
    
    [Tooltip("Как часто (в секундах) миникарта будет обновляться.")]
    public float minimapUpdateInterval = 0.2f;
    [Tooltip("Масштаб миникарты. Меньше значение = дальше 'камера'. 1.0 - хороший вариант для обзора.")]
    public float minimapScale = 1.0f;

    private ChunkManager chunkManager;
    private VoxelGenerationPipeline generationPipeline;
    private BiomeManager biomeManager;
    private float chunkUpdateTimer;
    private ComputeBuffer biomeColorBuffer;
    private RenderTexture minimapRenderTexture;
    private readonly int regionSize = 256;

    private void Awake()
    {
        Instance = this;
        if (worldSettings == null || player == null || biomeMap == null) { this.enabled = false; return; }
        biomeManager = gameObject.AddComponent<BiomeManager>();
        biomeManager.biomeMap = this.biomeMap;
        biomeManager.Initialize(worldSettings);
        generationPipeline = new VoxelGenerationPipeline(worldSettings, biomeManager); 
        chunkManager = new ChunkManager(generationPipeline, worldSettings, this.transform);
    }

    private void Start()
    {
        InitializeBiomeColorBuffer();
        if (minimapDisplay != null && minimapShader != null)
        {
            StartCoroutine(MinimapUpdateRoutine());
        }
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
        generationPipeline.ProcessQueues(chunkManager.GetChunk);
        generationPipeline.CheckCompletedJobs(chunkManager.OnChunkDataReady);
    }
    
    private void OnDestroy()
    {
        // Корректно освобождаем все ресурсы при закрытии
        generationPipeline?.Dispose();
        chunkManager?.Dispose();
        biomeColorBuffer?.Release();
        minimapRenderTexture?.Release();
    }
    
    /// <summary>
    /// Корутина для асинхронного обновления текстуры миникарты.
    /// </summary>
    private IEnumerator MinimapUpdateRoutine()
    {
        int textureSize = 256;
        minimapRenderTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true };
        minimapRenderTexture.Create();
        minimapDisplay.texture = minimapRenderTexture;
        
        int kernel = minimapShader.FindKernel("RenderMinimap");
        minimapShader.SetBuffer(kernel, "BiomeColors", biomeColorBuffer);
        minimapShader.SetTexture(kernel, "MinimapTexture", minimapRenderTexture);
        minimapShader.SetInt("TextureSize", textureSize);
        
        while (true)
        {
            Vector2Int playerRegionCoords = new Vector2Int(
                Mathf.FloorToInt(player.position.x / regionSize),
                Mathf.FloorToInt(player.position.z / regionSize)
            );

            var regionGrid = new List<RegionData>(9);
            var neededCoords = new List<Vector2Int>(9);
            bool allRegionsReady = true;

            for (int y = -1; y <= 1; y++) {
                for (int x = -1; x <= 1; x++) {
                    Vector2Int targetCoords = playerRegionCoords + new Vector2Int(x, y);
                    neededCoords.Add(targetCoords);
                    RegionData data = generationPipeline.GetRegionData(targetCoords);
                    if (data == null) allRegionsReady = false;
                    regionGrid.Add(data);
                }
            }
            
            generationPipeline.RequestNeededRegions(neededCoords);

            if (!allRegionsReady) {
                yield return new WaitForSeconds(minimapUpdateInterval);
                continue;
            }

            int largeMapSide = regionSize * 3;
            var largeMapData = new NativeArray<Color>(largeMapSide * largeMapSide, Allocator.Temp);
            
            // --- БОЛЕЕ НАДЕЖНЫЙ СПОСОБ КОПИРОВАНИЯ ---
            for (int i = 0; i < 9; i++) {
                int gridX = i % 3;
                int gridY = i / 3;
                RegionData currentRegion = regionGrid[i];

                for (int y = 0; y < regionSize; y++) {
                    for (int x = 0; x < regionSize; x++) {
                        int sourceIndex = x + y * regionSize;
                        int destX = gridX * regionSize + x;
                        int destY = gridY * regionSize + y;
                        int destIndex = destX + destY * largeMapSide;
                        largeMapData[destIndex] = currentRegion.mapData[sourceIndex];
                    }
                }
            }
            
            using (var largeBiomeMapBuffer = new ComputeBuffer(largeMapData.Length, sizeof(float) * 4))
            {
                largeBiomeMapBuffer.SetData(largeMapData.Reinterpret<Vector4>());
                minimapShader.SetBuffer(kernel, "BiomeMap", largeBiomeMapBuffer);
                
                Vector2 newRegionOffset = new Vector2((playerRegionCoords.x - 1) * regionSize, (playerRegionCoords.y - 1) * regionSize);
                
                minimapShader.SetInt("RegionSize", largeMapSide);
                minimapShader.SetVector("RegionOffset", new Vector4(newRegionOffset.x, newRegionOffset.y, 0, 0));
                minimapShader.SetVector("PlayerWorldPos", new Vector4(player.position.x, player.position.z, 0, 0));
                minimapShader.SetFloat("MinimapScale", minimapScale);

                int threadGroups = Mathf.CeilToInt(textureSize / 8.0f);
                minimapShader.Dispatch(kernel, threadGroups, threadGroups, 1);
            }
            
            largeMapData.Dispose();
            yield return new WaitForSeconds(minimapUpdateInterval);
        }
    }
    
    /// <summary>
    /// Создает буфер с цветами для каждого ID вокселя для использования в шейдере миникарты.
    /// </summary>
    private void InitializeBiomeColorBuffer()
    {
        var colors = new List<Vector4>();
        ushort maxId = 0;
        foreach (var voxelType in worldSettings.voxelTypes)
        {
            if (voxelType.ID > maxId) maxId = voxelType.ID;
        }
        // Заполняем с запасом, чтобы избежать ошибок выхода за пределы массива
        for (int i = 0; i <= maxId + 1; i++)
        {
            colors.Add(Color.black); // Цвет по умолчанию
        }
        foreach (var voxelType in worldSettings.voxelTypes)
        {
            colors[voxelType.ID] = new Vector4(voxelType.baseColor.r, voxelType.baseColor.g, voxelType.baseColor.b, 1);
        }
        
        biomeColorBuffer = new ComputeBuffer(colors.Count, sizeof(float) * 4);
        biomeColorBuffer.SetData(colors);
    }
    
    private Vector3Int GetChunkPositionFromWorldPos(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Chunk.Width);
        int z = Mathf.FloorToInt(worldPosition.z / Chunk.Width);
        return new Vector3Int(x, 0, z);
    }
}