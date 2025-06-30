// --- ФАЙЛ: BiomeAgent.cs (ПЕРЕРАБОТАННАЯ ВЕРСИЯ) ---
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;

/// <summary>
/// Представляет "семя" биома, которое участвует в симуляции распространения.
/// Хранит свое состояние, принадлежность к паре и захваченные территории.
/// </summary>
public class BiomeAgent
{
    // --- Основные идентификаторы и настройки ---
    public readonly int uniqueInstanceId;
    public readonly BiomePlacementSettingsSO settings;

    // --- Динамические параметры ---
    public readonly float initialAggressiveness;
    public float2 startingPosition;

    // --- Состояние пары ---
    public bool isPaired = false;
    public int partnerId = -1; // Уникальный ID партнера

    // --- Захваченная территория ---
    // Храним список координат "сайтов" (ячеек), которые принадлежат этому агенту.
    // HashSet обеспечивает быстрый доступ и предотвращает дублирование.
    public HashSet<int2> ownedSites = new HashSet<int2>();

    /// <summary>
    /// Конструктор для нового агента биома.
    /// </summary>
    /// <param name="id">Уникальный идентификатор, присваиваемый BiomeManager'ом.</param>
    /// <param name="pos">Начальная позиция "семени" в мире.</param>
    /// <param name="biomeSettings">ScriptableObject с настройками этого типа биома.</param>
    /// <param name="aggressiveness">Рассчитанный начальный уровень агрессивности (влияет на скорость роста и доминирование).</param>
    public BiomeAgent(int id, float2 pos, BiomePlacementSettingsSO biomeSettings, float aggressiveness)
    {
        this.uniqueInstanceId = id;
        this.settings = biomeSettings;
        this.startingPosition = pos;
        this.initialAggressiveness = aggressiveness;

        // При создании агент сразу "захватывает" свою стартовую ячейку.
        // Мы используем int2 для координат ячеек на сетке симуляции.
        this.ownedSites.Add(new int2(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y)));
    }
}