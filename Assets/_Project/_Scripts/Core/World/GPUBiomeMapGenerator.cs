// --- ФАЙЛ: GPUBiomeMapGenerator.cs (МНОГОПРОХОДНАЯ ВЕРСИЯ) ---
using UnityEngine;
using UnityEngine.Rendering;
using System;
using Unity.Collections;

public class GPUBiomeMapGenerator
{
    private ComputeShader shader;
    private int kernelHandle;
    private int generationKernel;
    private int filteringKernel;
    private BiomeMapSO biomeMap;

    public GPUBiomeMapGenerator(ComputeShader computeShader, BiomeMapSO biomeMap)
    {
        this.shader = computeShader;
        this.biomeMap = biomeMap;
        this.kernelHandle = shader.FindKernel("GenerateBiomeMap");
    }
    public void GenerateMap(Vector2Int regionCoords, int regionSize, WorldSettingsSO settings, Action<RegionData> onComplete)
    {
        RenderTexture initialMap = new RenderTexture(regionSize, regionSize, 0, RenderTextureFormat.RInt) { enableRandomWrite = true };
        initialMap.Create();
        
        RenderTexture finalMap = new RenderTexture(regionSize, regionSize, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
        finalMap.Create();

        // --- ИЗМЕНЕНИЕ: Теперь мы передаем Vector4 ---
        // x = ID ТИПА биома, yz = позиция, w = УНИКАЛЬНЫЙ ID ЭКЗЕМПЛЯРА
        var biomeDataArray = new Vector4[biomeMap.biomeMappings.Length];
        for (int i = 0; i < biomeMap.biomeMappings.Length; i++)
        {
            var mapping = biomeMap.biomeMappings[i];
            // Уникальный ID экземпляра - это просто его индекс в массиве.
            biomeDataArray[i] = new Vector4(mapping.biome.BiomeBlock.ID, mapping.position.x, mapping.position.y, i + 1); // +1 чтобы 0 был зарезервирован
        }
        
        // Используем буфер для Vector4
        using (var biomeBuffer = new ComputeBuffer(biomeDataArray.Length, sizeof(float) * 4))
        {
            biomeBuffer.SetData(biomeDataArray);
            
            // --- ПРОХОД 1: ГЕНЕРАЦИЯ ПОЛИТИЧЕСКОЙ КАРТЫ ---
            shader.SetBuffer(generationKernel, "BiomeData", biomeBuffer);
            shader.SetTexture(generationKernel, "InitialResult", initialMap);
            shader.SetVector("WorldOffset", new Vector4(regionCoords.x * regionSize, regionCoords.y * regionSize, 0, 0));
            shader.SetFloat("AtlasSize", biomeMap.biomeMappings.Length);
            shader.SetFloat("MaxComplexityDistance", settings.maxComplexityDistance);
            shader.SetFloat("EasyFrequencyMultiplier", settings.easyFrequencyMultiplier);
            shader.SetFloat("HardFrequencyMultiplier", settings.hardFrequencyMultiplier);
            
            int threadGroups = Mathf.CeilToInt(regionSize / 8.0f);
            shader.Dispatch(generationKernel, threadGroups, threadGroups, 1);

            // --- ПРОХОД 2: ФИЛЬТРАЦИЯ И ЭРОЗИЯ ---
            shader.SetInt("RegionSize", regionSize);
            shader.SetInt("FilteringRadius", settings.filteringRadius);
            shader.SetFloat("RequiredNeighborPercentage", settings.requiredNeighborPercentage);
            shader.SetInt("NeutralBiomeID", settings.globalBiomeBlock.ID);
            shader.SetFloat("ErosionThreshold", settings.erosionThreshold);
            shader.SetFloat("ErosionNoiseScale", settings.erosionNoiseScale);
            shader.SetTexture(filteringKernel, "InitialMap", initialMap);
            shader.SetTexture(filteringKernel, "FinalResult", finalMap);
            
            shader.Dispatch(filteringKernel, threadGroups, threadGroups, 1);
        }

        // 3. Читаем данные из ИТОГОВОЙ текстуры (finalMap)
        AsyncGPUReadback.Request(finalMap, 0, (request) =>
        {
            if (request.hasError) { onComplete?.Invoke(null); } 
            else 
            {
                NativeArray<Color> tempData = request.GetData<Color>();
                NativeArray<Color> persistentData = new NativeArray<Color>(tempData.Length, Allocator.Persistent);
                persistentData.CopyFrom(tempData);
                var regionData = new RegionData(persistentData, regionSize, regionSize);
                onComplete?.Invoke(regionData);
            }
            // Освобождаем обе временные текстуры
            initialMap.Release();
            finalMap.Release();
        });
    }
}