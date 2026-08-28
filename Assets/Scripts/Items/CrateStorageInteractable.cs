using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CrateStorageInteractable : WorldInteractable
{
    // Dimensoes publicas mantem o placement e o prefab dinamico com o mesmo footprint.
    public static readonly Vector2 DefaultColliderSize = new(0.72f, 0.48f);
    public static readonly Vector2 DefaultColliderOffset = new(0f, -0.12f);

    private const string DefaultDisplayName = "Caixote";

    // Configuracao de capacidade, aparencia, colisao e drop do caixote.
    [Header("Crate")]
    [SerializeField] private string axeToolId = "axe_tool";
    [SerializeField] private string openPromptText = "E para abrir";
    [SerializeField] private string breakPromptText = "Segure E para quebrar";
    [SerializeField, Min(1)] private int slotCount = 12;
    [SerializeField, Min(1)] private int slotsPerRow = 4;
    [SerializeField, Min(0.05f)] private float breakHoldDuration = 0.45f;

    [Header("World")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private BoxCollider2D boxCollider;
    [SerializeField] private BoxCollider2D interactionTrigger;
    [SerializeField] private string sortingLayerName = "Objects";
    [SerializeField] private int sortingOrder;
    [SerializeField] private bool mirrorSprite;
    [SerializeField] private Vector2 colliderSize = new(0.72f, 0.48f);
    [SerializeField] private Vector2 colliderOffset = new(0f, -0.12f);
    [SerializeField] private Vector2 interactionTriggerSize = new(1.25f, 1f);
    [SerializeField] private Vector2 interactionTriggerOffset = new(0f, -0.04f);

    [Header("Drop")]
    [SerializeField] private ItemData crateItemData;
    [SerializeField] private DroppedItemVisual dropPrefab;
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform pickupTargetOverride;

    // Estado de armazenamento em memoria; storedItemAmount permite testar IsEmpty em O(1).
    private readonly List<InventorySlotData> slots = new();
    private int storedItemAmount;

    // API observavel consumida pela CrateStorageUI.
    public event Action StorageChanged;

    public IReadOnlyList<InventorySlotData> Slots => slots;
    public int SlotCount => slots.Count;
    public int SlotsPerRow => Mathf.Max(1, slotsPerRow);
    public bool IsEmpty => storedItemAmount <= 0;
    public ItemData CrateItemData => crateItemData;
    public bool IsMirrored => mirrorSprite;
    public string DisplayName => crateItemData != null && !string.IsNullOrWhiteSpace(crateItemData.itemName)
        ? crateItemData.itemName
        : DefaultDisplayName;

    // Ciclo de vida e montagem dos componentes criados em runtime.
    private void Awake()
    {
        EnsureComponents();
        InitializeSlotsIfNeeded();
        ApplyWorldVisuals();
    }

    private void OnValidate()
    {
        EnsureComponents();
        ApplyWorldVisuals();
    }

    // Inicializacao usada pelo sistema de placement para compartilhar dependencias da cena.
    public void Initialize(
        ItemData itemData,
        DroppedItemVisual sharedDropPrefab = null,
        InventorySystem sharedInventorySystem = null,
        Transform sharedPickupTarget = null,
        bool mirrored = false)
    {
        crateItemData = itemData;
        mirrorSprite = mirrored;

        if (sharedDropPrefab != null)
            dropPrefab = sharedDropPrefab;

        if (sharedInventorySystem != null)
            inventorySystem = sharedInventorySystem;

        if (sharedPickupTarget != null)
            pickupTargetOverride = sharedPickupTarget;

        EnsureComponents();
        InitializeSlotsIfNeeded();
        ApplyWorldVisuals();
        NotifyStorageChanged();
    }

    // Operacoes de armazenamento; notificam a UI somente quando o estado realmente muda.
    public bool TryGetSlot(int slotIndex, out InventorySlotData slotData)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
        {
            slotData = null;
            return false;
        }

        slotData = slots[slotIndex];
        return true;
    }

    public bool AddItem(ItemData itemData, int amount)
    {
        return AddItem(itemData, amount, out _);
    }

    public bool AddItem(ItemData itemData, int amount, out int addedAmount)
    {
        addedAmount = 0;

        if (itemData == null || amount <= 0)
            return false;

        if (CountItem(itemData) < amount)
            return false;

        if (itemData.isUnique)
        {
            if (HasItem(itemData))
                return false;

            amount = 1;
        }

        int requestedAmount = amount;
        int remaining = amount;
        int maxStack = ResolveMaxStackSize(itemData);
        bool changed = TryFillExistingStacks(itemData, maxStack, ref remaining);
        changed |= TryFillEmptySlots(itemData, maxStack, ref remaining);

        addedAmount = requestedAmount - remaining;
        storedItemAmount += addedAmount;

        if (changed)
            NotifyStorageChanged();

        return remaining == 0;
    }

    public bool RemoveItem(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;
        bool changed = false;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || !ItemIdentity.Matches(slot.Item, itemData))
                continue;

            int amountToRemove = slot.RemoveAmount(remaining);
            remaining -= amountToRemove;
            changed = true;
        }

        if (changed)
        {
            storedItemAmount = Mathf.Max(0, storedItemAmount - (amount - remaining));
            NotifyStorageChanged();
        }

        return remaining == 0;
    }

    public bool RemoveFromSlot(int slotIndex, int amount, out ItemData removedItem, out int removedAmount)
    {
        removedItem = null;
        removedAmount = 0;

        if (amount <= 0 || !TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null || slotData.IsEmpty)
            return false;

        removedItem = slotData.Item;
        removedAmount = slotData.RemoveAmount(amount);
        storedItemAmount = Mathf.Max(0, storedItemAmount - removedAmount);

        NotifyStorageChanged();
        return removedAmount > 0;
    }

    public bool MoveOrSwapSlots(int fromIndex, int toIndex)
    {
        if (!TryGetSlot(fromIndex, out InventorySlotData fromSlot) ||
            !TryGetSlot(toIndex, out InventorySlotData toSlot) ||
            fromIndex == toIndex ||
            fromSlot.IsEmpty)
            return false;

        bool changed = TryMergeSlots(fromSlot, toSlot);

        if (!changed && !ItemIdentity.Matches(fromSlot.Item, toSlot.Item))
        {
            SwapSlotContents(fromSlot, toSlot);
            changed = true;
        }

        if (!changed)
            return false;

        NotifyStorageChanged();
        return true;
    }

    public void ClearAllSlots(bool notify = true)
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].Clear();

        storedItemAmount = 0;

        if (notify)
            NotifyStorageChanged();
    }

    public bool SetSlotContents(int slotIndex, ItemData itemData, int amount, bool notify = true)
    {
        if (!TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null)
            return false;

        if (itemData == null || amount <= 0)
        {
            storedItemAmount -= slotData.Amount;
            slotData.Clear();
            storedItemAmount = Mathf.Max(0, storedItemAmount);

            if (notify)
                NotifyStorageChanged();

            return true;
        }

        int maxStack = ResolveMaxStackSize(itemData);
        if (amount > maxStack)
            return false;

        storedItemAmount = Mathf.Max(0, storedItemAmount - slotData.Amount) + amount;
        slotData.SetItem(itemData, amount);

        if (notify)
            NotifyStorageChanged();

        return true;
    }

    public void NotifyRestoredContents()
    {
        NotifyStorageChanged();
    }

    // Contrato de interacao: abre normalmente e exige hold com machado quando esta vazio.
    public override bool GetRequiresHold(PlayerInteractor interactor)
    {
        return ShouldBreakCrate(interactor);
    }

    public override float GetHoldDuration(PlayerInteractor interactor)
    {
        return ShouldBreakCrate(interactor)
            ? breakHoldDuration
            : base.GetHoldDuration(interactor);
    }

    protected override string ResolvePromptText(PlayerInteractor interactor)
    {
        return ShouldBreakCrate(interactor)
            ? breakPromptText
            : openPromptText;
    }

    protected override bool PerformInteraction(PlayerInteractor interactor)
    {
        if (ShouldBreakCrate(interactor))
            return BreakCrate();

        CrateStorageUI storageUI = CrateStorageUI.GetOrCreate();
        return storageUI != null && storageUI.Open(this, interactor);
    }

    // Quebra, desativa colisores imediatamente e devolve o item ao mundo.
    private bool BreakCrate()
    {
        if (!IsEmpty)
            return false;

        LockInteraction();

        if (boxCollider != null)
            boxCollider.enabled = false;

        if (interactionTrigger != null)
            interactionTrigger.enabled = false;

        TrySpawnBreakDrop();
        Destroy(gameObject);
        return true;
    }

    private void TrySpawnBreakDrop()
    {
        if (crateItemData == null)
        {
            Debug.LogWarning("CrateStorageInteractable precisa do ItemData do caixote para gerar o drop.");
            return;
        }

        ResolveDropDependencies();

        if (dropPrefab == null)
        {
            Debug.LogWarning("CrateStorageInteractable nao encontrou um DroppedItemVisual para gerar o drop visual.");
            return;
        }

        DroppedItemVisual dropInstance = Instantiate(dropPrefab, transform.position, Quaternion.identity);
        dropInstance.Initialize(crateItemData, 1, inventorySystem, pickupTargetOverride);
    }

    // Montagem visual e fisica compartilhada por Awake, OnValidate e Initialize.
    private void EnsureComponents()
    {
        spriteRenderer ??= GetOrAddComponent<SpriteRenderer>(gameObject);
        boxCollider ??= GetOrAddComponent<BoxCollider2D>(gameObject);
        interactionTrigger ??= EnsureInteractionTrigger();
    }

    private void ApplyWorldVisuals()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = crateItemData != null ? crateItemData.icon : null;
            spriteRenderer.sortingLayerName = sortingLayerName;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.flipX = mirrorSprite;
        }

        if (boxCollider != null)
        {
            boxCollider.isTrigger = false;
            boxCollider.size = colliderSize;
            boxCollider.offset = colliderOffset;
        }

        if (interactionTrigger != null)
        {
            interactionTrigger.isTrigger = true;
            interactionTrigger.size = interactionTriggerSize;
            interactionTrigger.offset = interactionTriggerOffset;
        }
    }

    private void InitializeSlotsIfNeeded()
    {
        int desiredSlotCount = Mathf.Max(1, slotCount);
        if (slots.Count == desiredSlotCount)
            return;

        slots.Clear();
        slots.Capacity = desiredSlotCount;
        storedItemAmount = 0;

        for (int i = 0; i < desiredSlotCount; i++)
            slots.Add(new InventorySlotData());
    }

    private void ResolveDropDependencies()
    {
        if (inventorySystem == null)
            inventorySystem = FindAnyObjectByType<InventorySystem>();

        if (dropPrefab != null)
            return;

        ResourceNodeDropper sharedDropper = FindAnyObjectByType<ResourceNodeDropper>();
        if (sharedDropper == null)
            return;

        dropPrefab = sharedDropper.DropPrefab;

        if (pickupTargetOverride == null)
            pickupTargetOverride = sharedDropper.PickupTargetOverride;
    }

    // Regras internas de inventario e decisao da interacao contextual.
    public int CountItem(ItemData itemData)
    {
        if (itemData == null)
            return 0;

        int total = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty && ItemIdentity.Matches(slots[i].Item, itemData))
                total += slots[i].Amount;
        }

        return total;
    }

    private bool HasItem(ItemData itemData)
    {
        return CountItem(itemData) > 0;
    }

    private bool ShouldBreakCrate(PlayerInteractor interactor)
    {
        return interactor != null &&
               interactor.InventorySystem != null &&
               interactor.InventorySystem.IsSelectedItemId(axeToolId) &&
               IsEmpty;
    }

    private bool TryMergeSlots(InventorySlotData fromSlot, InventorySlotData toSlot)
    {
        if (fromSlot == null || toSlot == null || fromSlot.IsEmpty || toSlot.IsEmpty ||
            !ItemIdentity.Matches(fromSlot.Item, toSlot.Item))
            return false;

        int maxStack = ResolveMaxStackSize(toSlot.Item);
        int spaceLeft = Mathf.Max(0, maxStack - toSlot.Amount);
        if (spaceLeft <= 0)
            return false;

        int amountToTransfer = Mathf.Min(spaceLeft, fromSlot.Amount);
        if (amountToTransfer <= 0)
            return false;

        toSlot.AddAmount(amountToTransfer);
        fromSlot.RemoveAmount(amountToTransfer);

        return true;
    }

    private static void SwapSlotContents(InventorySlotData firstSlot, InventorySlotData secondSlot)
    {
        ItemData firstItem = firstSlot.Item;
        int firstAmount = firstSlot.Amount;

        firstSlot.SetItem(secondSlot.Item, secondSlot.Amount);
        secondSlot.SetItem(firstItem, firstAmount);
    }

    private void NotifyStorageChanged()
    {
        StorageChanged?.Invoke();
    }

    // Mantem o armazenamento compacto antes de expandir para slots vazios.
    private bool TryFillExistingStacks(ItemData itemData, int maxStack, ref int remaining)
    {
        bool changed = false;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || !ItemIdentity.Matches(slot.Item, itemData))
                continue;

            int spaceLeft = maxStack - slot.Amount;
            if (spaceLeft <= 0)
                continue;

            int amountToAdd = Mathf.Min(spaceLeft, remaining);
            slot.AddAmount(amountToAdd);
            remaining -= amountToAdd;
            changed = true;
        }

        return changed;
    }

    private bool TryFillEmptySlots(ItemData itemData, int maxStack, ref int remaining)
    {
        bool changed = false;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlotData slot = slots[i];

            if (!slot.IsEmpty)
                continue;

            int amountToAdd = Mathf.Min(maxStack, remaining);
            slot.SetItem(itemData, amountToAdd);
            remaining -= amountToAdd;
            changed = true;
        }

        return changed;
    }

    // Fabrica dos componentes auxiliares do caixote dinamico.
    private BoxCollider2D EnsureInteractionTrigger()
    {
        Transform interactionZone = transform.Find("InteractionZone");
        GameObject interactionObject;

        if (interactionZone != null)
        {
            interactionObject = interactionZone.gameObject;
        }
        else
        {
            interactionObject = new GameObject("InteractionZone");
            interactionObject.transform.SetParent(transform, false);
            interactionObject.transform.localPosition = Vector3.zero;
            interactionObject.transform.localRotation = Quaternion.identity;
            interactionObject.transform.localScale = Vector3.one;
        }

        return GetOrAddComponent<BoxCollider2D>(interactionObject);
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        if (!target.TryGetComponent(out T component))
            component = target.AddComponent<T>();

        return component;
    }

    private static int ResolveMaxStackSize(ItemData itemData)
    {
        if (itemData == null)
            return 0;

        return itemData.isUnique ? 1 : Mathf.Max(1, itemData.maxStack);
    }
}
