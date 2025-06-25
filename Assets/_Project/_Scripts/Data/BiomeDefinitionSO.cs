using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDefinition", menuName = "Procedural Voxel World/Biome Definition")]
public class BiomeDefinitionSO : ScriptableObject
{
    [Header("Метаданные Биома")]
    public ushort biomeID;

    [Header("Основной блок биома")]
    [Tooltip("Единственный тип блока, который определяет этот биом.")]
    public VoxelTypeSO BiomeBlock;
}