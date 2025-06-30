// --- ФАЙЛ: GPUBiomeMapGenerator.cs ---
using UnityEngine;
using UnityEngine.Rendering;
using System;
using Unity.Collections;

public class GPUBiomeMapGenerator
{
    private ComputeShader shader;
    private int kernelHandle;
    private BiomeMapSO biomeMap;

    public GPUBiomeMapGenerator(ComputeShader computeShader, BiomeMapSO biomeMap)
    {
        if (computeShader == null || biomeMap == null)
        {
            Debug.LogError("Compute Shader или BiomeMap не переданы в GPUBiomeMapGenerator!");
            return;
        }
        this.shader = computeShader;
        this.biomeMap = biomeMap;
        this.kernelHandle = shader.FindKernel("GenerateBiomeMap");
    }

    public void GenerateMap(Vector2Int regionCoords, int regionSize, Action<RegionData> onComplete)
    {
        RenderTexture target = new RenderTexture(regionSize, regionSize, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true
        };
        target.Create();

        var biomeDataArray = new Vector3[biomeMap.biomeMappings.Length];
        for (int i = 0; i < biomeMap.biomeMappings.Length; i++)
        {
            var mapping = biomeMap.biomeMappings[i];
            if(mapping.biome == null || mapping.biome.BiomeBlock == null)
            {
                 Debug.LogError($"Ошибка в ассете BiomeMap! Элемент {i} не настроен корректно.");
                 continue;
            }
            biomeDataArray[i] = new Vector3(mapping.biome.BiomeBlock.ID, mapping.position.x, mapping.position.y);
        }

        using (var biomeBuffer = new ComputeBuffer(biomeDataArray.Length, sizeof(float) * 3))
        {
            biomeBuffer.SetData(biomeDataArray);
            shader.SetBuffer(kernelHandle, "BiomeData", biomeBuffer);
        }

        shader.SetTexture(kernelHandle, "Result", target);
        shader.SetVector("WorldOffset", new Vector4(regionCoords.x * regionSize, regionCoords.y * regionSize, 0, 0));
        shader.SetFloat("AtlasSize", biomeMap.biomeMappings.Length);

        int threadGroups = Mathf.CeilToInt(regionSize / 8.0f);
        shader.Dispatch(kernelHandle, threadGroups, threadGroups, 1);

        AsyncGPUReadback.Request(target, 0, (request) =>
        {
            if (request.hasError)
            {
                Debug.LogError("Ошибка чтения карты биомов с GPU!");
                onComplete?.Invoke(null);
            }
            else
            {
                // --- ГЛАВНОЕ ИСПРАВЛЕНИЕ ЗДЕСЬ ---
                // 1. Получаем временный массив от Unity
                NativeArray<Color> tempData = request.GetData<Color>();
                
                // 2. Создаем наш собственный постоянный массив нужного размера
                NativeArray<Color> persistentData = new NativeArray<Color>(tempData.Length, Allocator.Persistent);

                // 3. Явно копируем данные из временного массива в наш постоянный
                persistentData.CopyFrom(tempData);

                // 4. Создаем RegionData, который теперь владеет надежным, постоянным массивом
                var regionData = new RegionData(persistentData, regionSize, regionSize);

                onComplete?.Invoke(regionData);
            }
            // Уничтожаем временную текстуру, она больше не нужна
            target.Release();
        });
    }
}