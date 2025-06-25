using UnityEngine;

/// <summary>
/// ScriptableObject для хранения и настройки параметров одного слоя 
/// когерентного шума. Позволяет гейм-дизайнерам создавать и переиспользовать
/// профили шума для разных целей (карта высот, температура, влажность и т.д.).
/// </summary>
[CreateAssetMenu(fileName = "NewNoiseSettings", menuName = "Procedural Voxel World/Noise Settings")]
public class NoiseSettingsSO : ScriptableObject
{
    [Header("Структура шума")]
    [Tooltip("Масштаб шума. Большие значения создают более крупный и плавный рельеф.")]
    public float scale = 50f;

    [Tooltip("Количество слоев шума, которые накладываются друг на друга для создания детализации.")]
    [Range(1, 8)]
    public int octaves = 4;

    [Tooltip("Множитель изменения частоты для каждой следующей октавы (лакунарность). Значение > 1.")]
    public float lacunarity = 2f;

    [Tooltip("Множитель изменения амплитуды для каждой следующей октавы (устойчивость/влияние). Значение < 1.")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Header("Смещение и Сид")]
    [Tooltip("Начальное число (сид) для генератора псевдослучайных чисел.")]
    public int seed;

    [Tooltip("Смещение координат для сэмплирования шума. Критически важно для создания разных карт (высоты, влажности) из одного сида.")]
    public Vector2 offset;
}