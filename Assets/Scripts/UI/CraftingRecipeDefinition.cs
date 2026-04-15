using System;
using UnityEngine;

[Serializable]
public sealed class CraftingIngredientRequirement
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

[Serializable]
public sealed class CraftingRecipeDefinition
{
    [SerializeField] private string recipeId = "recipe";
    [SerializeField] private string displayName;
    [SerializeField, TextArea(2, 4)] private string description;
    [SerializeField] private ItemData outputItem;
    [SerializeField, Min(1)] private int outputAmount = 1;
    [SerializeField] private CraftingIngredientRequirement[] ingredients = Array.Empty<CraftingIngredientRequirement>();

    public string RecipeId
    {
        get => recipeId;
        set => recipeId = value;
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return outputItem != null && !string.IsNullOrWhiteSpace(outputItem.itemName)
                ? outputItem.itemName
                : "Receita";
        }
        set => displayName = value;
    }

    public string Description
    {
        get => description;
        set => description = value;
    }

    public ItemData OutputItem
    {
        get => outputItem;
        set => outputItem = value;
    }

    public int OutputAmount
    {
        get => Mathf.Max(1, outputAmount);
        set => outputAmount = Mathf.Max(1, value);
    }

    public CraftingIngredientRequirement[] Ingredients
    {
        get => ingredients ?? Array.Empty<CraftingIngredientRequirement>();
        set => ingredients = value ?? Array.Empty<CraftingIngredientRequirement>();
    }

    public bool IsValid
    {
        get
        {
            if (outputItem == null || outputAmount <= 0 || Ingredients.Length == 0)
                return false;

            for (int i = 0; i < Ingredients.Length; i++)
            {
                if (Ingredients[i] == null || !Ingredients[i].IsValid)
                    return false;
            }

            return true;
        }
    }
}
