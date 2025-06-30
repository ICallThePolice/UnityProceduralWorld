// --- ФАЙЛ: WorldController.cs (ФИНАЛЬНАЯ ВЕРСИЯ С РАБОТАЮЩЕЙ МИНИКАРТОЙ) ---
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic; // <-- Добавлено для Dictionary

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    [Header("Основные настройки")]
    public WorldSettingsSO worldSettings;
    public Transform player;
    public BiomeMapSO biomeMap;
    public ComputeShader minimapShader;

    [Header("UI для отладки")]
    public RawImage minimapDisplay;
    
    [Tooltip("Как часто (в секундах) миникарта будет обновляться.")]
    public float minimapUpdateInterval = 0.1f;
    [Tooltip("Масштаб миникарты. 1 = 1 пиксель на 1 юнит мира.")]
    public float minimapScale = 4.0f;

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
        if (worldSettings == null || player == null || biomeMap == null) 
        { 
            Debug.LogError("Ключевые компоненты не назначены в WorldController!");
            this.enabled = false; 
            return; 
        }

        // Здесь мы создаем и инициализируем BiomeManager
        biomeManager = gameObject.AddComponent<BiomeManager>();
        biomeManager.worldSettings = this.worldSettings;
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
        else if (minimapShader == null)
        {
            Debug.LogWarning("Шейдер для миникарты (Minimap Shader) не назначен в инспекторе WorldController. Миникарта не будет работать.");
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
        generationPipeline?.Dispose();
        chunkManager?.Dispose();
        biomeColorBuffer?.Release();
        minimapRenderTexture?.Release();
    }
    
    private IEnumerator MinimapUpdateRoutine()
    {
        int textureSize = 256;
        minimapRenderTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true
        };
        minimapRenderTexture.Create();
        minimapDisplay.texture = minimapRenderTexture;
        
        int kernel = minimapShader.FindKernel("RenderMinimap");
        minimapShader.SetBuffer(kernel, "BiomeColors", biomeColorBuffer);
        minimapShader.SetTexture(kernel, "MinimapTexture", minimapRenderTexture);
        minimapShader.SetInt("TextureSize", textureSize);
        minimapShader.SetInt("RegionSize", regionSize);
        
        while (true)
        {
            Vector2Int playerRegionCoords = new Vector2Int(
                Mathf.FloorToInt(player.position.x / regionSize),
                Mathf.FloorToInt(player.position.z / regionSize)
            );
            
            RegionData regionData = generationPipeline.GetRegionData(playerRegionCoords);

            if(regionData != null && regionData.mapData.IsCreated)
            {
                // --- ИСПРАВЛЕННЫЙ БЛОК ---
                // Теперь Dispatch вызывается внутри using, чтобы буфер не успел удалиться
                using (var biomeMapBuffer = new ComputeBuffer(regionData.mapData.Length, sizeof(float) * 4))
                {
                    // Конвертируем NativeArray<Color> в NativeArray<Vector4> для буфера
                    var mapDataVector4 = regionData.mapData.Reinterpret<Vector4>();
                    biomeMapBuffer.SetData(mapDataVector4);
                    
                    minimapShader.SetBuffer(kernel, "BiomeMap", biomeMapBuffer);
                    
                    minimapShader.SetVector("PlayerWorldPos", new Vector4(player.position.x, player.position.z, 0, 0));
                    minimapShader.SetVector("RegionOffset", new Vector4(playerRegionCoords.x * regionSize, playerRegionCoords.y * regionSize, 0, 0));
                    minimapShader.SetFloat("MinimapScale", minimapScale);

                    int threadGroups = Mathf.CeilToInt(textureSize / 8.0f);
                    minimapShader.Dispatch(kernel, threadGroups, threadGroups, 1);
                }
            }
            
            yield return new WaitForSeconds(minimapUpdateInterval);
        }
    }
    
    private void InitializeBiomeColorBuffer()
    {
        var colors = new List<Vector4>();
        ushort maxId = 0;
        foreach (var voxelType in worldSettings.voxelTypes)
        {
            if (voxelType.ID > maxId) maxId = voxelType.ID;
        }
        for (int i = 0; i <= maxId + 1; i++)
        {
            colors.Add(Color.black); 
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