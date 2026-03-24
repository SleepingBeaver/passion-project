using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : MonoBehaviour
{
    [SerializeField] private InventoryUIController inventoryUI;
    [SerializeField, Min(1)] private int fallbackSlotCount = 20;

    private List<InventorySlotData> slots = new();

    public IReadOnlyList<InventorySlotData> Slots => slots;

    private void Awake()
    {
        InitializeSlots();
    }

    private void Start()
    {
        RefreshUI();
    }

    public bool AddItem(InventoryItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;

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
        }

        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlotData slot = slots[i];

            if (!slot.IsEmpty)
                continue;

            int amountToAdd = Mathf.Min(itemData.maxStack, remaining);
            slot.SetItem(itemData, amountToAdd);
            remaining -= amountToAdd;
        }

        RefreshUI();
        return remaining == 0;
    }

    public bool RemoveItem(InventoryItemData itemData, int amount = 1)
    {
        if (itemData == null || amount <= 0)
            return false;

        int remaining = amount;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlotData slot = slots[i];

            if (slot.IsEmpty || slot.item != itemData)
                continue;

            int amountToRemove = Mathf.Min(slot.amount, remaining);
            slot.amount -= amountToRemove;
            remaining -= amountToRemove;

            if (slot.amount <= 0)
                slot.Clear();
        }

        RefreshUI();
        return remaining == 0;
    }

    public int CountItem(InventoryItemData itemData)
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

    private void InitializeSlots()
    {
        int slotCount = inventoryUI != null ? inventoryUI.SlotCount : fallbackSlotCount;

        slots.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            slots.Add(new InventorySlotData());
        }
    }

    private void RefreshUI()
    {
        if (inventoryUI != null)
            inventoryUI.Refresh(slots);
    }
}