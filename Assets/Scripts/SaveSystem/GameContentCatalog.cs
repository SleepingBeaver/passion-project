using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameContentCatalog", menuName = "A Taste of Home/Game Content Catalog")]
public sealed class GameContentCatalog : ScriptableObject
{
    private const string DefaultResourcePath = "GameContentCatalog";

    [SerializeField] private ItemData[] items = Array.Empty<ItemData>();
    [SerializeField] private CropDefinition[] crops = Array.Empty<CropDefinition>();
    [SerializeField] private DishDefinition[] dishes = Array.Empty<DishDefinition>();

    public IReadOnlyList<DishDefinition> Dishes => dishes ?? Array.Empty<DishDefinition>();

    public static GameContentCatalog LoadDefault()
    {
        return Resources.Load<GameContentCatalog>(DefaultResourcePath);
    }

    public bool TryGetItem(string itemId, out ItemData itemData)
    {
        itemData = null;

        if (string.IsNullOrWhiteSpace(itemId) || items == null)
            return false;

        for (int i = 0; i < items.Length; i++)
        {
            ItemData candidate = items[i];
            if (!ItemIdentity.Matches(candidate, itemId))
                continue;

            itemData = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetCrop(string cropId, out CropDefinition cropDefinition)
    {
        cropDefinition = null;

        if (string.IsNullOrWhiteSpace(cropId) || crops == null)
            return false;

        for (int i = 0; i < crops.Length; i++)
        {
            CropDefinition candidate = crops[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.CropId))
                continue;

            if (!string.Equals(candidate.CropId.Trim(), cropId.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            cropDefinition = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetDish(string dishId, out DishDefinition dishDefinition)
    {
        dishDefinition = null;

        if (string.IsNullOrWhiteSpace(dishId) || dishes == null)
            return false;

        for (int i = 0; i < dishes.Length; i++)
        {
            DishDefinition candidate = dishes[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.DishId))
                continue;

            if (!string.Equals(candidate.DishId.Trim(), dishId.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            dishDefinition = candidate;
            return true;
        }

        return false;
    }
}
