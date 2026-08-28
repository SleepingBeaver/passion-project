using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class KitchenStationInteractable : WorldInteractable
{
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

    [SerializeField] private DishDefinition dish;
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private BoxCollider2D interactionTrigger;

    private readonly List<ConsumedIngredient> consumedIngredients = new();
    private float remainingCookingSeconds;
    private int readyServings;
    private string statusMessage = "Cozinha pronta.";

    public event Action StateChanged;

    public DishDefinition Dish => dish;
    public bool IsCooking => remainingCookingSeconds > 0f;
    public float RemainingCookingSeconds => Mathf.Max(0f, remainingCookingSeconds);
    public int ReadyServings => readyServings;
    public string StatusMessage => statusMessage;

    private void Awake()
    {
        ResolveReferences();
        EnsureRuntimeComponents();
        ApplyVisuals();
    }

    private void Update()
    {
        if (!IsCooking)
            return;

        remainingCookingSeconds = Mathf.Max(0f, remainingCookingSeconds - Time.deltaTime);
        if (remainingCookingSeconds <= 0f)
            CompleteCooking();
    }

    public void Initialize(DishDefinition dishDefinition, InventorySystem sharedInventory)
    {
        dish = dishDefinition != null ? dishDefinition : dish;
        inventorySystem = sharedInventory != null ? sharedInventory : inventorySystem;
        ResolveReferences();
        EnsureRuntimeComponents();
        ApplyVisuals();
        StateChanged?.Invoke();
    }

    public bool TryBeginCooking(InventorySystem sourceInventory = null)
    {
        InventorySystem inventory = sourceInventory != null ? sourceInventory : inventorySystem;
        if (dish == null || !dish.IsConfigured || inventory == null || IsCooking || readyServings > 0)
            return false;

        DishIngredientRequirement[] ingredients = dish.Ingredients;
        for (int i = 0; i < ingredients.Length; i++)
        {
            DishIngredientRequirement ingredient = ingredients[i];
            if (ingredient == null || ingredient.Item == null ||
                inventory.CountItem(ingredient.Item) < ingredient.Amount)
            {
                statusMessage = $"Faltam ingredientes para {dish.DisplayName}.";
                StateChanged?.Invoke();
                return false;
            }
        }

        consumedIngredients.Clear();
        inventory.BeginBatchUpdate();
        try
        {
            for (int i = 0; i < ingredients.Length; i++)
            {
                DishIngredientRequirement ingredient = ingredients[i];
                if (!inventory.RemoveItem(ingredient.Item, ingredient.Amount))
                {
                    RollbackIngredients(inventory);
                    statusMessage = "O preparo foi cancelado e os ingredientes foram devolvidos.";
                    StateChanged?.Invoke();
                    return false;
                }

                consumedIngredients.Add(new ConsumedIngredient(ingredient.Item, ingredient.Amount));
            }
        }
        finally
        {
            inventory.EndBatchUpdate();
        }

        remainingCookingSeconds = dish.CookingDurationSeconds;
        statusMessage = $"Preparando {dish.DisplayName}.";

        if (remainingCookingSeconds <= 0f)
            CompleteCooking();
        else
            StateChanged?.Invoke();

        return true;
    }

    public bool TryCollectPreparedDish(InventorySystem targetInventory = null)
    {
        InventorySystem inventory = targetInventory != null ? targetInventory : inventorySystem;
        if (dish == null || dish.PreparedItem == null || inventory == null || readyServings <= 0)
            return false;

        if (!inventory.AddItem(dish.PreparedItem, 1, out int addedAmount) || addedAmount != 1)
        {
            statusMessage = "Sem espaço no inventário para recolher o prato.";
            StateChanged?.Invoke();
            return false;
        }

        readyServings--;
        statusMessage = $"{dish.DisplayName} foi colocado no inventário.";
        StateChanged?.Invoke();
        return true;
    }

    public void RestoreState(DishDefinition restoredDish, float restoredRemainingSeconds, int restoredReadyServings)
    {
        if (restoredDish != null)
            dish = restoredDish;

        remainingCookingSeconds = Mathf.Max(0f, restoredRemainingSeconds);
        readyServings = Mathf.Max(0, restoredReadyServings);
        statusMessage = readyServings > 0
            ? $"{dish?.DisplayName ?? "Prato"} pronto para recolher."
            : IsCooking ? $"Preparando {dish?.DisplayName ?? "prato"}." : "Cozinha pronta.";
        ApplyVisuals();
        StateChanged?.Invoke();
    }

    protected override string ResolvePromptText(PlayerInteractor interactor)
    {
        if (readyServings > 0)
            return $"E para recolher {dish?.DisplayName ?? "prato"}";

        if (IsCooking)
            return $"Preparando... {Mathf.CeilToInt(remainingCookingSeconds)}s";

        InventorySystem inventory = interactor != null ? interactor.InventorySystem : inventorySystem;
        return HasIngredients(inventory)
            ? $"E para preparar {dish?.DisplayName ?? "prato"}"
            : "Faltam ingredientes para cozinhar";
    }

    protected override bool PerformInteraction(PlayerInteractor interactor)
    {
        InventorySystem inventory = interactor != null ? interactor.InventorySystem : inventorySystem;

        if (readyServings > 0)
            return TryCollectPreparedDish(inventory);

        if (IsCooking)
            return false;

        return TryBeginCooking(inventory);
    }

    private void CompleteCooking()
    {
        remainingCookingSeconds = 0f;
        readyServings++;
        statusMessage = $"{dish?.DisplayName ?? "Prato"} está pronto.";
        StateChanged?.Invoke();
    }

    private bool HasIngredients(InventorySystem inventory)
    {
        if (dish == null || !dish.IsConfigured || inventory == null)
            return false;

        DishIngredientRequirement[] ingredients = dish.Ingredients;
        for (int i = 0; i < ingredients.Length; i++)
        {
            DishIngredientRequirement ingredient = ingredients[i];
            if (ingredient == null || ingredient.Item == null ||
                inventory.CountItem(ingredient.Item) < ingredient.Amount)
            {
                return false;
            }
        }

        return true;
    }

    private void RollbackIngredients(InventorySystem inventory)
    {
        for (int i = 0; i < consumedIngredients.Count; i++)
        {
            ConsumedIngredient consumed = consumedIngredients[i];
            inventory.AddItem(consumed.Item, consumed.Amount, out _);
        }

        consumedIngredients.Clear();
    }

    private void ResolveReferences()
    {
        inventorySystem ??= FindAnyObjectByType<InventorySystem>();

        if (dish != null)
            return;

        GameContentCatalog catalog = GameContentCatalog.LoadDefault();
        if (catalog != null && catalog.Dishes.Count > 0)
            dish = catalog.Dishes[0];
    }

    private void EnsureRuntimeComponents()
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        spriteRenderer ??= gameObject.AddComponent<SpriteRenderer>();
        interactionTrigger ??= GetComponent<BoxCollider2D>();
        interactionTrigger ??= gameObject.AddComponent<BoxCollider2D>();
        interactionTrigger.isTrigger = true;
        interactionTrigger.size = new Vector2(1.25f, 1f);
    }

    private void ApplyVisuals()
    {
        if (spriteRenderer == null)
            return;

        spriteRenderer.sprite = dish != null && dish.PreparedItem != null ? dish.PreparedItem.icon : null;
        spriteRenderer.color = new Color(1f, 0.72f, 0.35f, 1f);
        spriteRenderer.sortingLayerName = "Objects";
        spriteRenderer.sortingOrder = 2;
        transform.localScale = Vector3.one * 0.9f;
    }
}
