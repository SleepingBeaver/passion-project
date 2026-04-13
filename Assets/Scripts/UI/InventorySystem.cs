using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : MonoBehaviour
{
    // Configuracao principal do inventario.
    [SerializeField] private InventoryUIController inventoryUI;
    [SerializeField, Min(1)] private int fallbackSlotCount = 20;
    [SerializeField, Min(1)] private int fallbackSlotsPerRow = 10;

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

    // Eventos para sincronizar outras partes da UI.
    public event Action<IReadOnlyList<InventorySlotData>> InventoryChanged;
    public event Action<int, InventorySlotData> SelectionChanged;

    // Ciclo de vida.
    private void Awake()
    {
        InitializeSlots();
    }

    private void Start()
    {
        RefreshUI();
    }

    // Operacoes publicas de inventario.
    public bool AddItem(ItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;
        bool changed = false;

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || slot.item != itemData)
                continue;

            int spaceLeft = itemData.maxStack - slot.amount;
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

            int amountToAdd = Mathf.Min(itemData.maxStack, remaining);
            slot.SetItem(itemData, amountToAdd);
            remaining -= amountToAdd;
            changed = true;
        }

        if (changed)
            RefreshUI();

        return remaining == 0;
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

    private void NotifySelectionChanged()
    {
        SelectionChanged?.Invoke(selectedSlotIndex, SelectedSlot);
    }
}
