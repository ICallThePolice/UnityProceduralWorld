// --- ФАЙЛ: BiomeMapGPUGenerator.cs (ФИНАЛЬНАЯ ВЕРСИЯ) ---
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class BiomeMapGPUGenerator
{
    private ComputeShader computeShader;
    private int fillBlendKernel;
    private int drawBordersKernel;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct BiomeAgentData
    {
        public Vector2 position;
        public float maxRadius;
        public uint biomeTypeId;
        public uint uniqueInstanceId;
        public uint partnerId;
        public uint clusterId;
    }

    public BiomeMapGPUGenerator(ComputeShader shader)
    {
        this.computeShader = shader;
        if (this.computeShader == null)
        {
            Debug.LogError("В BiomeMapGPUGenerator не был передан Compute Shader!");
            return;
        }
        
        fillBlendKernel = computeShader.FindKernel("FillMapWithBlend");
        drawBordersKernel = computeShader.FindKernel("DrawBorders");
    }

    public RenderTexture GenerateMap(List<BiomeAgent> agents, int mapSize, Vector2 mapOrigin, int borderSize)
    {
        if (computeShader == null || agents == null || agents.Count == 0) return null;

        RenderTexture mapTexture = new RenderTexture(mapSize, mapSize, 0, RenderTextureFormat.ARGBFloat);
        mapTexture.enableRandomWrite = true;
        mapTexture.Create();
        
        var agentDataArray = new BiomeAgentData[agents.Count];
        for (int i = 0; i < agents.Count; i++) { /* ... (заполнение как в предыдущем ответе) ... */ }

        using (ComputeBuffer agentBuffer = new ComputeBuffer(agentDataArray.Length, Marshal.SizeOf(typeof(BiomeAgentData))))
        {
            agentBuffer.SetData(agentDataArray);
            computeShader.SetInt("_AgentCount", agents.Count);
            computeShader.SetVector("_MapOrigin", new Vector4(mapOrigin.x, mapOrigin.y, 0, 0));
            computeShader.SetBuffer(fillBlendKernel, "_Agents", agentBuffer);
            computeShader.SetTexture(fillBlendKernel, "_Result", mapTexture);
            computeShader.Dispatch(fillBlendKernel, Mathf.CeilToInt(mapSize / 8.0f), Mathf.CeilToInt(mapSize / 8.0f), 1);
            
            computeShader.SetBuffer(drawBordersKernel, "_Agents", agentBuffer);
            computeShader.SetTexture(drawBordersKernel, "_Result", mapTexture);
            // ИСПРАВЛЕНИЕ: Передаем толщину границ в шейдер
            computeShader.SetInt("_BorderSize", borderSize);
            computeShader.Dispatch(drawBordersKernel, Mathf.CeilToInt(mapSize / 8.0f), Mathf.CeilToInt(mapSize / 8.0f), 1);
        }

        return mapTexture;
    }
}