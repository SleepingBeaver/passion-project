using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class CraftingSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;

    [Header("Recipes")]
    [SerializeField] private List<CraftingRecipeDefinition> recipes = new();
    [SerializeField] private string defaultWoodItemId = "wood";
    [SerializeField] private string defaultCrateItemId = "crate";

    private readonly List<SimulatedSlot> simulationSlots = new();

    public IReadOnlyList<CraftingRecipeDefinition> Recipes => recipes;
    public event Action RecipesChanged;

    private struct SimulatedSlot
    {
        public ItemData item;
        public int amount;

        public bool IsEmpty => item == null || amount <= 0;

        public void Clear()
        {
            item = null;
            amount = 0;
        }
    }

    private readonly struct ConsumedIngredient
    {
        public ConsumedIngredient(ItemData item, int amount)
        {
            Item = item;
            Amount = amount;
        }

        public ItemData Item { get; }
        public int Amount { get; }
    }

    private void Awake()
    {
        ResolveInventorySystem();
        EnsureRecipesConfigured();
    }

    private void OnValidate()
    {
        recipes ??= new List<CraftingRecipeDefinition>();
        ClampRecipeValues();
    }

    public bool TryCraft(CraftingRecipeDefinition recipe, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (inventorySystem == null)
        {
            resultMessage = "Inventario nao encontrado.";
            return false;
        }

        if (!CanCraft(recipe, out resultMessage))
            return false;

        CraftingIngredientRequirement[] ingredients = recipe.Ingredients;
        List<ConsumedIngredient> consumedIngredients = new(ingredients.Length);

        for (int i = 0; i < ingredients.Length; i++)
        {
            CraftingIngredientRequirement ingredient = ingredients[i];

            if (!TryConsumeIngredientFromInventory(ingredient, out int consumedAmount))
            {
                for (int rollbackIndex = 0; rollbackIndex < consumedIngredients.Count; rollbackIndex++)
                {
                    ConsumedIngredient consumed = consumedIngredients[rollbackIndex];
                    inventorySystem.AddItem(consumed.Item, consumed.Amount, out _);
                }

                resultMessage = "Nao foi possivel consumir os ingredientes.";
                return false;
            }

            consumedIngredients.Add(new ConsumedIngredient(ingredient.Item, consumedAmount));
        }

        bool addedAll = inventorySystem.AddItem(recipe.OutputItem, recipe.OutputAmount, out int addedAmount);
        if (!addedAll || addedAmount != recipe.OutputAmount)
        {
            if (addedAmount > 0)
                inventorySystem.RemoveItem(recipe.OutputItem, addedAmount);

            for (int i = 0; i < consumedIngredients.Count; i++)
            {
                ConsumedIngredient consumed = consumedIngredients[i];
                inventorySystem.AddItem(consumed.Item, consumed.Amount, out _);
            }

            resultMessage = "Nao foi possivel adicionar o item criado ao inventario.";
            return false;
        }

        string craftedItemName = !string.IsNullOrWhiteSpace(recipe.OutputItem.itemName)
            ? recipe.OutputItem.itemName
            : recipe.DisplayName;
        resultMessage = $"Criado: {recipe.OutputAmount}x {craftedItemName}";
        return true;
    }

    public bool CanCraft(CraftingRecipeDefinition recipe, out string reason)
    {
        reason = string.Empty;

        if (!IsRecipeUsable(recipe))
        {
            reason = "Receita invalida.";
            return false;
        }

        ResolveInventorySystem();

        if (inventorySystem == null)
        {
            reason = "Inventario nao encontrado.";
            return false;
        }

        BuildSimulationSlots();

        CraftingIngredientRequirement[] ingredients = recipe.Ingredients;
        for (int i = 0; i < ingredients.Length; i++)
        {
            CraftingIngredientRequirement ingredient = ingredients[i];

            if (!TryRemoveFromSimulation(ingredient.Item, ingredient.Amount))
            {
                string ingredientName = ingredient.Item != null && !string.IsNullOrWhiteSpace(ingredient.Item.itemName)
                    ? ingredient.Item.itemName
                    : "Ingrediente";
                reason = $"Faltam materiais: {ingredientName}";
                return false;
            }
        }

        if (!TryAddToSimulation(recipe.OutputItem, recipe.OutputAmount))
        {
            reason = "Sem espaco no inventario.";
            return false;
        }

        return true;
    }

    public int CountItem(ItemData itemData)
    {
        ResolveInventorySystem();

        if (inventorySystem == null || itemData == null)
            return 0;

        int total = 0;

        for (int i = 0; i < inventorySystem.SlotCount; i++)
        {
            if (!inventorySystem.TryGetSlot(i, out InventorySlotData slot) || slot == null || slot.IsEmpty)
                continue;

            if (ItemsMatch(slot.item, itemData))
                total += slot.amount;
        }

        return total;
    }

    private void ResolveInventorySystem()
    {
        if (inventorySystem == null)
            inventorySystem = GetComponent<InventorySystem>();

        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();
    }

    private void EnsureRecipesConfigured()
    {
        ClampRecipeValues();

        if (recipes.Count > 0)
            return;

        if (!TryResolveItem(defaultWoodItemId, out ItemData woodItem) ||
            !TryResolveItem(defaultCrateItemId, out ItemData crateItem))
        {
            Debug.LogWarning("CraftingSystem nao conseguiu localizar os itens padrao para montar a receita inicial.");
            return;
        }

        CraftingRecipeDefinition defaultRecipe = new()
        {
            RecipeId = "crate_from_wood",
            DisplayName = "Caixote",
            Description = "Transforma madeira suficiente em um bau para armazenamento.",
            OutputItem = crateItem,
            OutputAmount = 1,
            Ingredients = new[]
            {
                new CraftingIngredientRequirement
                {
                    Item = woodItem,
                    Amount = 10
                }
            }
        };

        recipes.Add(defaultRecipe);
        RecipesChanged?.Invoke();
    }

    private void ClampRecipeValues()
    {
        for (int i = recipes.Count - 1; i >= 0; i--)
        {
            CraftingRecipeDefinition recipe = recipes[i];
            if (recipe == null)
            {
                recipes.RemoveAt(i);
                continue;
            }

            recipe.OutputAmount = recipe.OutputAmount;

            CraftingIngredientRequirement[] ingredients = recipe.Ingredients;
            for (int j = 0; j < ingredients.Length; j++)
            {
                if (ingredients[j] == null)
                    continue;

                ingredients[j].Amount = ingredients[j].Amount;
            }
        }
    }

    private bool TryResolveItem(string itemId, out ItemData itemData)
    {
        itemData = null;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        InventoryDebugInput debugInput = FindFirstObjectByType<InventoryDebugInput>();
        if (debugInput != null && debugInput.TryGetConfiguredItem(itemId, out itemData) && itemData != null)
            return true;

        ItemData[] loadedItems = Resources.FindObjectsOfTypeAll<ItemData>();
        for (int i = 0; i < loadedItems.Length; i++)
        {
            ItemData candidate = loadedItems[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.itemId))
                continue;

            if (string.Equals(candidate.itemId, itemId, StringComparison.OrdinalIgnoreCase))
            {
                itemData = candidate;
                return true;
            }
        }

        return false;
    }

    private bool IsRecipeUsable(CraftingRecipeDefinition recipe)
    {
        return recipe != null && recipe.IsValid;
    }

    private void BuildSimulationSlots()
    {
        simulationSlots.Clear();

        if (inventorySystem == null)
            return;

        IReadOnlyList<InventorySlotData> inventorySlots = inventorySystem.Slots;
        for (int i = 0; i < inventorySlots.Count; i++)
        {
            InventorySlotData slot = inventorySlots[i];
            simulationSlots.Add(new SimulatedSlot
            {
                item = slot != null ? slot.item : null,
                amount = slot != null ? slot.amount : 0
            });
        }
    }

    private bool TryRemoveFromSimulation(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;

        for (int i = simulationSlots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            SimulatedSlot slot = simulationSlots[i];
            if (slot.IsEmpty || !ItemsMatch(slot.item, itemData))
                continue;

            int amountToRemove = Mathf.Min(slot.amount, remaining);
            slot.amount -= amountToRemove;
            remaining -= amountToRemove;

            if (slot.amount <= 0)
                slot.Clear();

            simulationSlots[i] = slot;
        }

        return remaining == 0;
    }

    private bool TryAddToSimulation(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0)
            return false;

        if (itemData.isUnique)
        {
            for (int i = 0; i < simulationSlots.Count; i++)
            {
                if (!simulationSlots[i].IsEmpty && ItemsMatch(simulationSlots[i].item, itemData))
                    return false;
            }

            amount = 1;
        }

        int remaining = amount;
        int maxStack = itemData.isUnique ? 1 : Mathf.Max(1, itemData.maxStack);

        for (int i = 0; i < simulationSlots.Count && remaining > 0; i++)
        {
            SimulatedSlot slot = simulationSlots[i];

            if (slot.IsEmpty || !ItemsMatch(slot.item, itemData))
                continue;

            int spaceLeft = maxStack - slot.amount;
            if (spaceLeft <= 0)
                continue;

            int amountToAdd = Mathf.Min(spaceLeft, remaining);
            slot.amount += amountToAdd;
            remaining -= amountToAdd;
            simulationSlots[i] = slot;
        }

        for (int i = 0; i < simulationSlots.Count && remaining > 0; i++)
        {
            SimulatedSlot slot = simulationSlots[i];
            if (!slot.IsEmpty)
                continue;

            int amountToAdd = Mathf.Min(maxStack, remaining);
            slot.item = itemData;
            slot.amount = amountToAdd;
            remaining -= amountToAdd;
            simulationSlots[i] = slot;
        }

        return remaining == 0;
    }

    private bool TryConsumeIngredientFromInventory(CraftingIngredientRequirement ingredient, out int consumedAmount)
    {
        consumedAmount = 0;

        if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0 || inventorySystem == null)
            return false;

        int remaining = ingredient.Amount;

        for (int i = inventorySystem.SlotCount - 1; i >= 0 && remaining > 0; i--)
        {
            if (!inventorySystem.TryGetSlot(i, out InventorySlotData slot) || slot == null || slot.IsEmpty)
                continue;

            if (!ItemsMatch(slot.item, ingredient.Item))
                continue;

            int amountToRemove = Mathf.Min(slot.amount, remaining);
            if (amountToRemove <= 0)
                continue;

            if (!inventorySystem.RemoveFromSlot(i, amountToRemove, out ItemData removedItem, out int removedAmount) ||
                !ItemsMatch(removedItem, ingredient.Item) ||
                removedAmount != amountToRemove)
            {
                if (consumedAmount > 0)
                    inventorySystem.AddItem(ingredient.Item, consumedAmount, out _);

                consumedAmount = 0;
                return false;
            }

            remaining -= removedAmount;
            consumedAmount += removedAmount;
        }

        if (remaining > 0 && consumedAmount > 0)
        {
            inventorySystem.AddItem(ingredient.Item, consumedAmount, out _);
            consumedAmount = 0;
        }

        return remaining == 0;
    }

    private static bool ItemsMatch(ItemData firstItem, ItemData secondItem)
    {
        if (firstItem == secondItem)
            return true;

        if (firstItem == null || secondItem == null)
            return false;

        return !string.IsNullOrWhiteSpace(firstItem.itemId) &&
               !string.IsNullOrWhiteSpace(secondItem.itemId) &&
               string.Equals(firstItem.itemId, secondItem.itemId, StringComparison.OrdinalIgnoreCase);
    }
}

public static class CraftingGameplayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallSystems();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallSystems();
    }

    private static void InstallSystems()
    {
        InventorySystem inventorySystem = UnityEngine.Object.FindFirstObjectByType<InventorySystem>();
        if (inventorySystem == null)
            return;

        if (!inventorySystem.TryGetComponent(out CraftingSystem _))
            inventorySystem.gameObject.AddComponent<CraftingSystem>();

        CraftingUI.GetOrCreate();
    }
}
