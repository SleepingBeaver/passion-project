using System;
using UnityEngine;

[Serializable]
public sealed class DishIngredientRequirement
{
    [SerializeField] private ItemData item;
    [SerializeField, Min(1)] private int amount = 1;

    public ItemData Item
    {
        get => item;
        set => item = value;
    }

    public int Amount
    {
        get => Mathf.Max(1, amount);
        set => amount = Mathf.Max(1, value);
    }

    public bool IsValid => item != null && amount > 0;
}

[CreateAssetMenu(fileName = "NewDish", menuName = "A Taste of Home/Restaurant/Dish")]
public sealed class DishDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string dishId = "dish";
    [SerializeField] private string displayName = "Prato";
    [SerializeField, TextArea(2, 4)] private string description;

    [Header("Recipe")]
    [SerializeField] private ItemData preparedItem;
    [SerializeField] private DishIngredientRequirement[] ingredients = Array.Empty<DishIngredientRequirement>();
    [SerializeField, Min(0f)] private float cookingDurationSeconds = 5f;

    [Header("Service Reward")]
    [SerializeField, Min(0)] private int sellPrice = 50;
    [SerializeField, Min(0)] private int reputationReward = 1;

    public string DishId
    {
        get => dishId;
        set => dishId = value?.Trim();
    }

    public string DisplayName
    {
        get => !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : preparedItem != null ? preparedItem.itemName : "Prato";
        set => displayName = value;
    }

    public string Description
    {
        get => description;
        set => description = value;
    }

    public ItemData PreparedItem
    {
        get => preparedItem;
        set => preparedItem = value;
    }

    public DishIngredientRequirement[] Ingredients
    {
        get => ingredients ?? Array.Empty<DishIngredientRequirement>();
        set => ingredients = value ?? Array.Empty<DishIngredientRequirement>();
    }

    public float CookingDurationSeconds
    {
        get => Mathf.Max(0f, cookingDurationSeconds);
        set => cookingDurationSeconds = Mathf.Max(0f, value);
    }

    public int SellPrice
    {
        get => Mathf.Max(0, sellPrice);
        set => sellPrice = Mathf.Max(0, value);
    }

    public int ReputationReward
    {
        get => Mathf.Max(0, reputationReward);
        set => reputationReward = Mathf.Max(0, value);
    }

    public bool IsConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(dishId) || preparedItem == null || Ingredients.Length == 0)
                return false;

            for (int i = 0; i < Ingredients.Length; i++)
            {
                if (Ingredients[i] == null || !Ingredients[i].IsValid)
                    return false;
            }

            return true;
        }
    }

    private void OnValidate()
    {
        dishId = dishId?.Trim();
        cookingDurationSeconds = Mathf.Max(0f, cookingDurationSeconds);
        sellPrice = Mathf.Max(0, sellPrice);
        reputationReward = Mathf.Max(0, reputationReward);
    }
}
