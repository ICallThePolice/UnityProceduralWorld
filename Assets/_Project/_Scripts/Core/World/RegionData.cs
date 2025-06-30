// --- ФАЙЛ: RegionData.cs ---
using Unity.Collections;
using UnityEngine;

public class RegionData
{
    public readonly NativeArray<Color> mapData;
    public readonly int width;
    public readonly int height;

    public RegionData(NativeArray<Color> data, int w, int h)
    {
        mapData = data;
        width = w;
        height = h;
    }

    public Color GetValue(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return Color.clear;
        return mapData[x + y * width];
    }
    
    public void Dispose()
    {
        if (mapData.IsCreated)
        {
            mapData.Dispose();
        }
    }
}