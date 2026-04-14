using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : MonoBehaviour
{
    [Serializable]
    private sealed class StarterInventoryEntry
    {
        public ItemData item = null;
        [Min(1)] public int amount = 1;
    }

    // Configuracao principal do inventario.
    [SerializeField] private InventoryUIController inventoryUI;
    [SerializeField, Min(1)] private int fallbackSlotCount = 20;
    [SerializeField, Min(1)] private int fallbackSlotsPerRow = 10;
    [SerializeField] private StarterInventoryEntry[] starterItems;

    // Estado interno dos slots e da selecao atual.
    private readonly List<InventorySlotData> slots = new();
    private int selectedSlotIndex = -1;

    // Leitura publica do estado do inventario.
    public IReadOnlyList<InventorySlotData> Slots => slots;
    public int SlotCount => slots.Count;
    public int SlotsPerRow => inventoryUI != null ? inventoryUI.SlotsPerRow : fallbackSlotsPerRow;
    public int SelectedSlotIndex => selectedSlotIndex;
    public InventorySlotData SelectedSlot => TryGetSlot(selectedSlotIndex, out InventorySlotData slotData) ? slotData : null;
    public ItemData SelectedItem => SelectedSlot != null && !SelectedSlot.IsEmpty ? SelectedSlot.item : null;
    public InventorySlotVisual SlotPrefab => inventoryUI != null ? inventoryUI.SlotPrefab : null;

    // Eventos para sincronizar outras partes da UI.
    public event Action<IReadOnlyList<InventorySlotData>> InventoryChanged;
    public event Action<int, InventorySlotData> SelectionChanged;

    // Ciclo de vida.
    private void Awake()
    {
        InitializeSlots();
        ApplyStarterItems();
    }

    private void Start()
    {
        RefreshUI();
    }

    // Operacoes publicas de inventario.
    public bool AddItem(ItemData itemData, int amount = 1)
    {
        return TryAddItem(itemData, amount, refreshUI: true);
    }

    public bool RemoveItem(ItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;
        bool changed = false;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || slot.item != itemData)
                continue;

            int amountToRemove = Mathf.Min(slot.amount, remaining);
            slot.amount -= amountToRemove;
            remaining -= amountToRemove;
            changed = true;

            if (slot.amount <= 0)
                slot.Clear();
        }

        if (changed)
            RefreshUI();

        return remaining == 0;
    }

    public bool RemoveFromSlot(int slotIndex, int amount, out ItemData removedItem, out int removedAmount)
    {
        removedItem = null;
        removedAmount = 0;

        if (amount <= 0 || !TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null || slotData.IsEmpty)
            return false;

        removedItem = slotData.item;
        removedAmount = Mathf.Min(amount, slotData.amount);
        slotData.amount -= removedAmount;

        if (slotData.amount <= 0)
            slotData.Clear();

        RefreshUI();
        return removedAmount > 0;
    }

    public int CountItem(ItemData itemData)
    {
        if (itemData == null)
            return 0;

        int total = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty && slots[i].item == itemData)
                total += slots[i].amount;
        }

        return total;
    }

    public bool HasItem(ItemData itemData, int minimumAmount = 1)
    {
        return CountItem(itemData) >= Mathf.Max(1, minimumAmount);
    }

    public bool IsSelectedItem(ItemData itemData)
    {
        return itemData != null && SelectedItem == itemData;
    }

    public bool IsSelectedItemId(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               SelectedItem != null &&
               string.Equals(SelectedItem.itemId, itemId, StringComparison.OrdinalIgnoreCase);
    }

    public bool SelectSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        if (selectedSlotIndex == slotIndex)
        {
            NotifySelectionChanged();
            return true;
        }

        selectedSlotIndex = slotIndex;

        if (inventoryUI != null)
            inventoryUI.SetSelectedSlot(selectedSlotIndex);

        NotifySelectionChanged();
        return true;
    }

    public bool TryGetSlot(int slotIndex, out InventorySlotData slotData)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            slotData = null;
            return false;
        }

        slotData = slots[slotIndex];
        return true;
    }

    public bool MoveOrSwapSlots(int fromIndex, int toIndex)
    {
        if (!IsValidSlotIndex(fromIndex) || !IsValidSlotIndex(toIndex))
            return false;

        if (fromIndex == toIndex)
            return true;

        InventorySlotData fromSlot = slots[fromIndex];
        InventorySlotData toSlot = slots[toIndex];

        if (fromSlot.IsEmpty)
            return false;

        bool sourceWasSelected = selectedSlotIndex == fromIndex;
        bool sameItemStack = !toSlot.IsEmpty && fromSlot.item == toSlot.item;
        bool changed = TryMergeSlots(fromSlot, toSlot);

        if (!changed && !sameItemStack)
        {
            SwapSlotContents(fromSlot, toSlot);
            changed = true;
        }

        if (!changed)
            return false;

        if (sourceWasSelected && (!sameItemStack || fromSlot.IsEmpty))
            selectedSlotIndex = toIndex;

        RefreshUI();
        return true;
    }

    // Atualizacao interna do estado e da UI.
    private void InitializeSlots()
    {
        int slotCount = inventoryUI != null ? inventoryUI.SlotCount : fallbackSlotCount;

        slots.Clear();

        if (slots.Capacity < slotCount)
            slots.Capacity = slotCount;

        for (int i = 0; i < slotCount; i++)
        {
            slots.Add(new InventorySlotData());
        }

        selectedSlotIndex = slotCount > 0
            ? Mathf.Clamp(selectedSlotIndex, 0, slotCount - 1)
            : -1;
    }

    private bool TryAddItem(ItemData itemData, int amount, bool refreshUI)
    {
        if (itemData == null || amount <= 0)
            return false;

        if (itemData.isUnique)
        {
            if (HasItem(itemData))
                return false;

            amount = 1;
        }

        int remaining = amount;
        bool changed = false;
        int maxStack = itemData.isUnique ? 1 : Mathf.Max(1, itemData.maxStack);

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || slot.item != itemData)
                continue;

            int spaceLeft = maxStack - slot.amount;
            if (spaceLeft <= 0)
                continue;

            int amountToAdd = Mathf.Min(spaceLeft, remaining);
            slot.amount += amountToAdd;
            remaining -= amountToAdd;
            changed = true;
        }

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

        if (changed && refreshUI)
            RefreshUI();

        return remaining == 0;
    }

    private void ApplyStarterItems()
    {
        if (starterItems == null)
            return;

        for (int i = 0; i < starterItems.Length; i++)
        {
            StarterInventoryEntry starterEntry = starterItems[i];
            if (starterEntry.item == null)
                continue;

            TryAddItem(starterEntry.item, starterEntry.amount, refreshUI: false);
        }
    }

    private void RefreshUI()
    {
        if (inventoryUI != null)
        {
            inventoryUI.Refresh(slots);
            inventoryUI.SetSelectedSlot(selectedSlotIndex);
        }

        InventoryChanged?.Invoke(slots);
        NotifySelectionChanged();
    }

    // Utilitarios internos.
    private bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < slots.Count;
    }

    private bool TryMergeSlots(InventorySlotData fromSlot, InventorySlotData toSlot)
    {
        if (fromSlot == null || toSlot == null)
            return false;

        if (fromSlot.IsEmpty || toSlot.IsEmpty || fromSlot.item != toSlot.item)
            return false;

        int maxStack = fromSlot.item != null && fromSlot.item.isUnique
            ? 1
            : Mathf.Max(1, toSlot.item.maxStack);

        int spaceLeft = Mathf.Max(0, maxStack - toSlot.amount);
        if (spaceLeft <= 0)
            return false;

        int amountToTransfer = Mathf.Min(spaceLeft, fromSlot.amount);
        if (amountToTransfer <= 0)
            return false;

        toSlot.amount += amountToTransfer;
        fromSlot.amount -= amountToTransfer;

        if (fromSlot.amount <= 0)
            fromSlot.Clear();

        return true;
    }

    private void SwapSlotContents(InventorySlotData firstSlot, InventorySlotData secondSlot)
    {
        ItemData firstItem = firstSlot.item;
        int firstAmount = firstSlot.amount;

        firstSlot.SetItem(secondSlot.item, secondSlot.amount);
        secondSlot.SetItem(firstItem, firstAmount);
    }

    private void NotifySelectionChanged()
    {
        SelectionChanged?.Invoke(selectedSlotIndex, SelectedSlot);
    }
}
