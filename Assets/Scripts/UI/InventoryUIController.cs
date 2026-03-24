using UnityEngine;
using System.Collections.Generic;
using UnityEngine;

public class InventoryUIController : MonoBehaviour
{
    [SerializeField] private Transform slotsParent;
    [SerializeField] private InventorySlotVisual slotPrefab;
    [SerializeField, Min(1)] private int slotCount = 20;

    private readonly List<InventorySlotVisual> slotVisuals = new();

    public int SlotCount => slotCount;

    private void Awake()
    {
        BuildSlots();
    }

    private void BuildSlots()
    {
        if (slotsParent == null || slotPrefab == null)
        {
            Debug.LogWarning("SlotsParent ou SlotPrefab não configurado no InventoryUIController.");
            return;
        }

        ClearExistingChildren();
        slotVisuals.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            InventorySlotVisual slot = Instantiate(slotPrefab, slotsParent);
            slot.name = $"Slot_{i + 1:00}";
            slot.SetEmpty();
            slotVisuals.Add(slot);
        }
    }

    public void Refresh(IReadOnlyList<InventorySlotData> slots)
    {
        if (slotVisuals.Count == 0)
            BuildSlots();

        for (int i = 0; i < slotVisuals.Count; i++)
        {
            if (slots != null && i < slots.Count)
                slotVisuals[i].Refresh(slots[i]);
            else
                slotVisuals[i].SetEmpty();
        }
    }

    private void ClearExistingChildren()
    {
        for (int i = slotsParent.childCount - 1; i >= 0; i--)
        {
            Destroy(slotsParent.GetChild(i).gameObject);
        }
    }
}