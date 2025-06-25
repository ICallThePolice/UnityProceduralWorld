using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewBiomeArtifact", menuName = "Procedural Voxel World/Biome Artifact")]
public class BiomeArtifactSO : ScriptableObject
{
    public BiomeArtifactType artifactType;

    // Эти поля теперь используются только для корневых артефактов, чью плотность мы задаем в BiomePlacementSettings
    [Header("Параметры спавна (для корневых артефактов)")]
    [Tooltip("Плотность артефактов. Больше - больше артефактов.")]
    [Range(0.01f, 5f)] public float spawnDensity = 0.1f; 
    [Tooltip("Шанс появления для каждой отдельной попытки.")]
    [Range(0f, 1f)] public float spawnChance = 0.5f;

    [Header("Динамические параметры формы (ОТНОСИТЕЛЬНЫЕ)")]
    [Tooltip("ОТНОСИТЕЛЬНЫЙ размер артефакта.")]
    public Vector2 relativeSize = new Vector2(0.1f, 0.25f);
    [Tooltip("ОТНОСИТЕЛЬНАЯ высота/глубина.")]
    [Range(0f, 5f)] public float relativeHeight = 0.5f;

    [Header("Дочерние артефакты (относительно этого)")]
    public List<ChildArtifactPlacement> childArtifacts;
}