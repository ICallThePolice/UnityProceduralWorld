using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDefinition", menuName = "Procedural Voxel World/Biome Definition")]
public class BiomeDefinitionSO : ScriptableObject
{
    [Header("Метаданные Биома")]
    [Tooltip("Уникальный ID биома для быстрой идентификации в коде.")]
    public ushort biomeID;

    [Header("Основной блок биома")]
    [Tooltip("Единственный тип блока, который определяет 'лицо' этого биома.")]
    public VoxelTypeSO BiomeBlock;
}