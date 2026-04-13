using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUIController : MonoBehaviour
{
    // Configuracao da grade visual do inventario.
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform slotsParent;
    [SerializeField] private InventorySlotVisual slotPrefab;
    [SerializeField, Min(1)] private int slotCount = 20;

    // Estado interno da interface.
    private readonly List<InventorySlotVisual> slotVisuals = new();
    private int selectedSlotIndex = -1;

    // Leitura publica usada pelo sistema de inventario.
    public int SlotCount => slotCount;
    public int SlotsPerRow => ResolveSlotsPerRow();

    // Ciclo de vida.
    private void Awake()
    {
        ResolveInventorySystem();
        BuildSlots();
    }

    // Montagem e atualizacao da interface.
    private void BuildSlots()
    {
        if (slotsParent == null || slotPrefab == null)
        {
            Debug.LogWarning("SlotsParent ou SlotPrefab nao configurado no InventoryUIController.");
            return;
        }

        ClearExistingChildren();
        slotVisuals.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            InventorySlotVisual slot = Instantiate(slotPrefab, slotsParent);
            slot.name = $"Slot_{i + 1:00}";
            slot.SetEmpty();
            int slotIndex = i;
            slot.Clicked += () => HandleSlotClicked(slotIndex);
            slotVisuals.Add(slot);
        }

        RefreshSelectionState();
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

        RefreshSelectionState();
    }

    public void SetSelectedSlot(int slotIndex)
    {
        selectedSlotIndex = slotIndex;
        RefreshSelectionState();
    }

    // Utilitarios internos.
    private void ClearExistingChildren()
    {
        for (int i = slotsParent.childCount - 1; i >= 0; i--)
        {
            Destroy(slotsParent.GetChild(i).gameObject);
        }
    }

    private int ResolveSlotsPerRow()
    {
        if (slotsParent != null && slotsParent.TryGetComponent(out GridLayoutGroup gridLayout))
        {
            if (gridLayout.constraint == GridLayoutGroup.Constraint.FixedColumnCount ||
                gridLayout.constraint == GridLayoutGroup.Constraint.FixedRowCount)
            {
                return Mathf.Max(1, gridLayout.constraintCount);
            }
        }

        return slotCount;
    }

    private void HandleSlotClicked(int slotIndex)
    {
        ResolveInventorySystem();

        if (inventorySystem == null)
            return;

        inventorySystem.SelectSlot(slotIndex);
    }

    private void RefreshSelectionState()
    {
        for (int i = 0; i < slotVisuals.Count; i++)
        {
            slotVisuals[i].SetSelected(i == selectedSlotIndex);
        }
    }

    private void ResolveInventorySystem()
    {
        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();
    }
}
