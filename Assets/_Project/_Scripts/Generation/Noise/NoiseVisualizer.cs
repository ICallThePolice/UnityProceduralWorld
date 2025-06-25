using UnityEngine;

/// <summary>
/// Компонент для визуализации настроек шума (NoiseSettingsSO) на 2D текстуре.
/// Позволяет в реальном времени видеть, как параметры влияют на карту шума.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class NoiseVisualizer : MonoBehaviour
{
    [Header("Настройки для визуализации")]
    [Tooltip("Перетащите сюда ассет с настройками шума (например, ChaosNoise, HeightmapNoise).")]
    public NoiseSettingsSO noiseSettings;

    [Tooltip("Размер генерируемой текстуры в пикселях.")]
    public int textureSize = 256;

    // Ссылка на компонент для отображения текстуры.
    private Renderer targetRenderer;
    private Texture2D generatedTexture;

    // Вызывается при запуске сцены в режиме игры.
    private void Start()
    {
        targetRenderer = GetComponent<Renderer>();
        GenerateAndApplyTexture();
    }

    /// <summary>
    /// Этот метод позволяет обновлять текстуру прямо в редакторе,
    /// когда вы изменяете значения в инспекторе.
    /// </summary>
    private void OnValidate()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }
        GenerateAndApplyTexture();
    }
    
    /// <summary>
    /// Добавляет кнопку в контекстное меню компонента для ручной генерации.
    /// </summary>
    [ContextMenu("Generate Noise Texture")]
    public void GenerateAndApplyTexture()
    {
        if (noiseSettings == null)
        {
            // Не делаем ничего, если настройки не заданы
            return;
        }

        // Создаем текстуру, если ее еще нет
        if (generatedTexture == null)
        {
            generatedTexture = new Texture2D(textureSize, textureSize);
            generatedTexture.name = "Generated Noise Texture";
            // Применяем текстуру к материалу объекта
            // Используем sharedMaterial для корректной работы в редакторе
            targetRenderer.sharedMaterial.mainTexture = generatedTexture;
        }

        // 1. Инициализируем генератор шума с параметрами из ScriptableObject
        var noise = new FastNoiseLite(noiseSettings.seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFrequency(noiseSettings.scale); // В FastNoiseLite частота - это аналог масштаба
        
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(noiseSettings.octaves);
        noise.SetFractalLacunarity(noiseSettings.lacunarity);
        noise.SetFractalGain(noiseSettings.persistence);
        
        // 2. Проходим по каждому пикселю текстуры
        for (int x = 0; x < textureSize; x++)
        {
            for (int y = 0; y < textureSize; y++)
            {
                // Смещаем координаты на offset из настроек
                float sampleX = x + noiseSettings.offset.x;
                float sampleY = y + noiseSettings.offset.y;

                // 3. Получаем значение шума в точке
                float noiseValue = noise.GetNoise(sampleX, sampleY);

                // 4. Нормализуем значение из диапазона [-1, 1] в диапазон [0, 1]
                float normalizedValue = (noiseValue + 1f) / 2f;

                // 5. Устанавливаем цвет пикселя (от черного к белому)
                generatedTexture.SetPixel(x, y, new Color(normalizedValue, normalizedValue, normalizedValue));
            }
        }

        // 6. Применяем все изменения к текстуре
        generatedTexture.Apply();
    }
}