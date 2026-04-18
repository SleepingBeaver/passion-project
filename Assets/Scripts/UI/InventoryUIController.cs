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
    [SerializeField] private Canvas dragPreviewCanvas;
    [SerializeField] private Vector2 dragPreviewOffset = new Vector2(18f, -18f);
    [SerializeField, Range(0.15f, 1f)] private float dragPreviewAlpha = 0.9f;

    // Estado interno da interface.
    private readonly List<InventorySlotVisual> slotVisuals = new();
    private readonly Dictionary<InventorySlotVisual, int> slotIndices = new();
    private int selectedSlotIndex = -1;
    private RectTransform dragPreviewRect;
    private Image dragPreviewImage;
    private CanvasGroup dragPreviewCanvasGroup;

    // Leitura publica usada pelo sistema de inventario.
    public int SlotCount => slotCount;
    public int SlotsPerRow => ResolveSlotsPerRow();
    public InventorySlotVisual SlotPrefab => slotPrefab;

    // Ciclo de vida.
    private void Awake()
    {
        ResolveInventorySystem();
        ResolveDragPreviewCanvas();
        BuildSlots();
        SyncFromInventorySystem();
    }

    private void OnEnable()
    {
        SyncFromInventorySystem();
    }

    private void OnDisable()
    {
        ClearDragPreview();
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
        slotIndices.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            InventorySlotVisual slot = CreateSlotVisual(i);
            slotVisuals.Add(slot);
            slotIndices[slot] = i;
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

    private void HandleSlotDragStarted(int slotIndex)
    {
        ResolveInventorySystem();

        if (inventorySystem == null || !inventorySystem.TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null || slotData.IsEmpty)
            return;

        inventorySystem.SelectSlot(slotIndex);

        if (slotIndex >= 0 && slotIndex < slotVisuals.Count)
            ShowDragPreview(slotVisuals[slotIndex]);
    }

    private void HandleSlotDragged(Vector2 screenPosition)
    {
        UpdateDragPreviewPosition(screenPosition);
    }

    private void HandleSlotDragEnded()
    {
        ClearDragPreview();
    }

    private void HandleSlotDropped(InventorySlotVisual draggedSlot, int targetSlotIndex)
    {
        ResolveInventorySystem();

        if (inventorySystem == null || !TryResolveSourceSlotIndex(draggedSlot, out int sourceSlotIndex))
            return;

        if (sourceSlotIndex == targetSlotIndex)
            return;

        inventorySystem.MoveOrSwapSlots(sourceSlotIndex, targetSlotIndex);
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

    private void ResolveDragPreviewCanvas()
    {
        if (dragPreviewCanvas != null)
            dragPreviewCanvas = dragPreviewCanvas.rootCanvas;

        if (dragPreviewCanvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                dragPreviewCanvas = canvas.rootCanvas;
        }
    }

    // Quando a janela acorda depois de ter ficado desativada, ela precisa puxar o estado atual
    // do inventario para nao sobrescrever a UI com slots vazios reconstruidos no Awake.
    private void SyncFromInventorySystem()
    {
        ResolveInventorySystem();

        if (inventorySystem == null)
            return;

        Refresh(inventorySystem.Slots);
        SetSelectedSlot(inventorySystem.SelectedSlotIndex);
    }

    private InventorySlotVisual CreateSlotVisual(int slotIndex)
    {
        InventorySlotVisual slot = Instantiate(slotPrefab, slotsParent);
        slot.name = $"Slot_{slotIndex + 1:00}";
        slot.SetEmpty();
        BindSlotEvents(slot, slotIndex);
        return slot;
    }

    // Centraliza o bind dos eventos para deixar claro qual acao cada interacao dispara.
    private void BindSlotEvents(InventorySlotVisual slot, int slotIndex)
    {
        slot.Clicked += () => HandleSlotClicked(slotIndex);
        slot.DragStarted += () => HandleSlotDragStarted(slotIndex);
        slot.DragMoved += HandleSlotDragged;
        slot.DragEnded += HandleSlotDragEnded;
        slot.Dropped += draggedSlot => HandleSlotDropped(draggedSlot, slotIndex);
    }

    private bool TryResolveSourceSlotIndex(InventorySlotVisual draggedSlot, out int slotIndex)
    {
        slotIndex = -1;
        return draggedSlot != null && slotIndices.TryGetValue(draggedSlot, out slotIndex);
    }

    private void ShowDragPreview(InventorySlotVisual sourceSlot)
    {
        ResolveDragPreviewCanvas();

        if (dragPreviewCanvas == null || sourceSlot == null || !sourceSlot.HasItem || sourceSlot.DisplayedIcon == null)
            return;

        EnsureDragPreview();

        dragPreviewImage.sprite = sourceSlot.DisplayedIcon;
        dragPreviewImage.enabled = true;
        dragPreviewCanvasGroup.alpha = dragPreviewAlpha;

        Vector2 previewSize = sourceSlot.DragPreviewSize;
        if (previewSize == Vector2.zero)
            previewSize = new Vector2(48f, 48f);

        dragPreviewRect.sizeDelta = previewSize;
        dragPreviewRect.SetAsLastSibling();
    }

    private void EnsureDragPreview()
    {
        if (dragPreviewImage != null)
            return;

        GameObject previewObject = new("DraggedItemPreview", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        previewObject.transform.SetParent(dragPreviewCanvas.transform, false);

        dragPreviewRect = previewObject.GetComponent<RectTransform>();
        dragPreviewImage = previewObject.GetComponent<Image>();
        dragPreviewCanvasGroup = previewObject.GetComponent<CanvasGroup>();

        dragPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
        dragPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
        dragPreviewRect.pivot = new Vector2(0.5f, 0.5f);

        dragPreviewCanvasGroup.blocksRaycasts = false;
        dragPreviewCanvasGroup.interactable = false;
        dragPreviewImage.raycastTarget = false;
        dragPreviewImage.preserveAspect = true;
        dragPreviewImage.enabled = false;
    }

    private void UpdateDragPreviewPosition(Vector2 screenPosition)
    {
        if (dragPreviewCanvas == null || dragPreviewRect == null || !dragPreviewImage.enabled)
            return;

        RectTransform canvasRect = dragPreviewCanvas.transform as RectTransform;
        Camera eventCamera = dragPreviewCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : dragPreviewCanvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            return;

        dragPreviewRect.anchoredPosition = localPoint + dragPreviewOffset;
    }

    private void ClearDragPreview()
    {
        if (dragPreviewImage == null)
            return;

        dragPreviewImage.enabled = false;
        dragPreviewImage.sprite = null;
    }
}
