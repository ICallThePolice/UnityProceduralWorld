// --- ФАЙЛ: BiomeManager.cs (ГАРАНТИРОВАННО ЧИСТАЯ ДИАГНОСТИЧЕСКАЯ ВЕРСИЯ) ---
using UnityEngine;

public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }
    
    public WorldSettingsSO worldSettings;
    public BiomeMapSO biomeMap;

    void Awake()
    {
        Instance = this;
    }

    public void Initialize(WorldSettingsSO settings)
    {
        this.worldSettings = settings;
    }
}