using UnityEngine;

[DisallowMultipleComponent]
public sealed class ServingCounterInteractable : WorldInteractable
{
    [SerializeField] private RestaurantServiceSystem serviceSystem;
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private BoxCollider2D interactionTrigger;

    private void Awake()
    {
        ResolveReferences();
        EnsureRuntimeComponents();
        ApplyVisuals();
    }

    public void Initialize(RestaurantServiceSystem service, InventorySystem sharedInventory)
    {
        serviceSystem = service != null ? service : serviceSystem;
        inventorySystem = sharedInventory != null ? sharedInventory : inventorySystem;
        ResolveReferences();
        EnsureRuntimeComponents();
        ApplyVisuals();
    }

    protected override string ResolvePromptText(PlayerInteractor interactor)
    {
        if (serviceSystem == null || !serviceSystem.HasActiveOrder)
            return "Balcão: nenhum pedido aguardando";

        DishDefinition dish = serviceSystem.ActiveDish;
        InventorySystem inventory = interactor != null ? interactor.InventorySystem : inventorySystem;
        return dish != null && inventory != null && inventory.HasItem(dish.PreparedItem)
            ? $"E para servir {dish.DisplayName}"
            : $"Pedido de {serviceSystem.ActiveCustomerName}: {dish?.DisplayName ?? "prato"}";
    }

    protected override bool PerformInteraction(PlayerInteractor interactor)
    {
        ResolveReferences();
        InventorySystem inventory = interactor != null ? interactor.InventorySystem : inventorySystem;
        return serviceSystem != null && serviceSystem.TryServeActiveOrder(inventory);
    }

    private void ResolveReferences()
    {
        serviceSystem ??= FindAnyObjectByType<RestaurantServiceSystem>();
        inventorySystem ??= FindAnyObjectByType<InventorySystem>();
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

        DishDefinition activeOrDefaultDish = serviceSystem != null && serviceSystem.ActiveDish != null
            ? serviceSystem.ActiveDish
            : ResolveDefaultDish();
        spriteRenderer.sprite = activeOrDefaultDish != null && activeOrDefaultDish.PreparedItem != null
            ? activeOrDefaultDish.PreparedItem.icon
            : null;
        spriteRenderer.color = new Color(0.55f, 1f, 0.65f, 1f);
        spriteRenderer.sortingLayerName = "Objects";
        spriteRenderer.sortingOrder = 2;
        transform.localScale = Vector3.one * 0.9f;
    }

    private static DishDefinition ResolveDefaultDish()
    {
        GameContentCatalog catalog = GameContentCatalog.LoadDefault();
        return catalog != null && catalog.Dishes.Count > 0 ? catalog.Dishes[0] : null;
    }
}
