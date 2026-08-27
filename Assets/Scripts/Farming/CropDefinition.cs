using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCrop", menuName = "Passion Town/Farming/Crop Definition")]
public class CropDefinition : ScriptableObject
{
    // Vinculos entre a semente consumida, o item colhido e a identidade da cultura.
    [Header("Items")]
    [SerializeField] private string cropId = "tomato";
    [SerializeField] private ItemData seedItem;
    [SerializeField] private ItemData harvestItem;

    [Header("Growth")]
    [SerializeField] private Sprite[] growthSprites;
    [SerializeField, Min(1)] private int harvestAmount = 1;

    // API somente de leitura usada pelo sistema de plantacao e pelos validadores.
    public string CropId => cropId;
    public ItemData SeedItem => seedItem;
    public ItemData HarvestItem => harvestItem;
    public int StageCount => growthSprites?.Length ?? 0;
    public int MatureStageIndex => Mathf.Max(0, StageCount - 1);
    public int HarvestAmount => Mathf.Max(1, harvestAmount);
    public bool IsConfigured => seedItem != null && harvestItem != null && StageCount > 0;

    // Consultas tolerantes a indices e a instancias equivalentes pelo itemId.
    public Sprite GetStageSprite(int stageIndex)
    {
        if (growthSprites == null || growthSprites.Length == 0)
            return null;

        return growthSprites[Mathf.Clamp(stageIndex, 0, growthSprites.Length - 1)];
    }

    public bool MatchesSeed(ItemData itemData)
    {
        if (itemData == null || seedItem == null)
            return false;

        if (itemData == seedItem)
            return true;

        return !string.IsNullOrWhiteSpace(itemData.itemId) &&
               !string.IsNullOrWhiteSpace(seedItem.itemId) &&
               string.Equals(itemData.itemId, seedItem.itemId, StringComparison.OrdinalIgnoreCase);
    }
}
