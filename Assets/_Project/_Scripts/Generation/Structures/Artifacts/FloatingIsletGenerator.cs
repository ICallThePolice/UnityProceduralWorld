// --- ФАЙЛ: FloatingIsletGenerator.cs (ФИНАЛЬНАЯ ОТКАЗОУСТОЙЧИВАЯ ВЕРСИЯ) ---
using UnityEngine;

public struct FloatingIsletGenerator : IArtifactGenerator
{
    public ushort dominantBiomeMainVoxelID;
    public float parentBiomeHighestPoint;

    public void Apply(ref VoxelStateData voxelData, in ArtifactInstanceBurst artifact, in Vector2 worldPos, int y, int baseTerrainHeight)
    {
        // 1. Проверка по горизонтальному радиусу (без изменений)
        float distToArtifactCenter = Vector2.Distance(worldPos, artifact.position);
        float maxRadius = artifact.size.x / 2f;
        if (distToArtifactCenter > maxRadius) return;

        // 2. Новая, надежная логика расчета высоты
        // Рассчитываем желаемое дно и верх острова
        float desiredBottomY = artifact.groundHeight + artifact.yOffset;
        float islandHeight = Mathf.Max(1, artifact.height);
        float desiredTopY = desiredBottomY + islandHeight;

        // 3. Проверяем, находится ли ТЕКУЩИЙ воксель (y) в пределах желаемой высоты острова
        if (y >= desiredBottomY && y < desiredTopY)
        {
            // Убедимся, что мы не пытаемся рисовать за пределами мира
            if (y < 0 || y >= Chunk.Height) return;

            // 4. Расчет формы (без изменений)
            float platformThickness = Mathf.Min(islandHeight / 3f, 4f);
            float currentRadius;

            // Используем желаемый, а не "прижатый" верх для расчета формы
            if (y >= desiredTopY - platformThickness)
            {
                currentRadius = maxRadius;
            }
            else
            {
                float coneHeight = desiredTopY - platformThickness - desiredBottomY;
                if (coneHeight > 0)
                {
                    float coneProgress = (y - desiredBottomY) / coneHeight;
                    currentRadius = Mathf.Lerp(0f, maxRadius, InterpQuintic(coneProgress));
                }
                else
                {
                    currentRadius = maxRadius;
                }
            }

            // Финальная проверка и установка вокселя
            if (distToArtifactCenter < currentRadius)
            {
                voxelData.voxelID = this.dominantBiomeMainVoxelID;
            }
        }
    }
    
    // Вспомогательная функция для сглаживания
    private float InterpQuintic(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
}