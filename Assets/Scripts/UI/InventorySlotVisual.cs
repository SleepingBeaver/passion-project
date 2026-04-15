using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotVisual : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    // Referencias visuais do slot.
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Sprite defaultBackgroundSprite;
    [SerializeField] private Sprite selectedBackgroundSprite;
    [SerializeField] private Color selectionOutlineColor = new Color(1f, 0.83f, 0.31f, 1f);
    [SerializeField] private Vector2 selectionOutlineDistance = new Vector2(5f, -5f);
    [SerializeField, Range(0.15f, 1f)] private float dragAlpha = 0.4f;
    [SerializeField] private Vector2 iconPadding = new Vector2(12f, 12f);
    [SerializeField, Range(0.35f, 1f)] private float maxIconFill = 0.92f;

    // Estado interno do que esta sendo exibido.
    private ItemData displayedItem;
    private int displayedAmount = -1;
    private bool isSelected;
    private bool isDragging;
    private bool iconLayoutDirty = true;
    private Outline selectionOutline;
    private CanvasGroup canvasGroup;
    private RectTransform slotRectTransform;
    private RectTransform itemIconRectTransform;

    // Evento disparado quando o jogador clica no slot.
    public event Action Clicked;
    public event Action DragStarted;
    public event Action<Vector2> DragMoved;
    public event Action DragEnded;
    public event Action<InventorySlotVisual> Dropped;

    public bool HasItem => displayedItem != null && displayedAmount > 0;
    public Sprite DisplayedIcon => itemIcon != null ? itemIcon.sprite : null;
    public Vector2 DragPreviewSize
    {
        get
        {
            if (itemIconRectTransform != null)
                return itemIconRectTransform.rect.size;

            return slotRectTransform != null ? slotRectTransform.rect.size : Vector2.zero;
        }
    }

    // Ciclo de vida.
    private void Awake()
    {
        slotRectTransform = transform as RectTransform;
        itemIconRectTransform = itemIcon != null ? itemIcon.rectTransform : null;

        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();

        if (backgroundImage != null && defaultBackgroundSprite == null)
            defaultBackgroundSprite = backgroundImage.sprite;

        if (backgroundImage != null)
            backgroundImage.raycastTarget = true;

        if (itemIcon != null)
            itemIcon.raycastTarget = false;

        if (amountText != null)
            amountText.raycastTarget = false;

        selectionOutline = GetOrAddComponent<Outline>(gameObject);
        canvasGroup = GetOrAddComponent<CanvasGroup>(gameObject);
        selectionOutline.effectColor = selectionOutlineColor;
        selectionOutline.effectDistance = selectionOutlineDistance;
        selectionOutline.useGraphicAlpha = true;
        selectionOutline.enabled = false;
        iconLayoutDirty = true;
    }

    private void OnEnable()
    {
        iconLayoutDirty = true;
    }

    private void LateUpdate()
    {
        if (!iconLayoutDirty || !HasItem || itemIcon == null || itemIconRectTransform == null)
            return;

        ResizeItemSpriteToCurrentSlot();
    }

    private void OnRectTransformDimensionsChange()
    {
        iconLayoutDirty = true;
    }

    // Atualizacao visual do conteudo do slot.
    public void Refresh(InventorySlotData slotData)
    {
        if (slotData == null || slotData.IsEmpty)
        {
            SetEmpty();
            return;
        }

        if (displayedItem == slotData.item && displayedAmount == slotData.amount)
            return;

        displayedItem = slotData.item;
        displayedAmount = slotData.amount;
        iconLayoutDirty = true;

        if (itemIcon != null)
        {
            itemIcon.enabled = true;
            itemIcon.sprite = slotData.item.icon;
            itemIcon.preserveAspect = true;
            ResizeItemSpriteToCurrentSlot();
        }

        if (amountText != null)
        {
            amountText.gameObject.SetActive(slotData.amount > 1);
            amountText.text = slotData.amount.ToString();
        }
    }

    public void SetEmpty()
    {
        if (displayedItem == null && displayedAmount == 0)
            return;

        displayedItem = null;
        displayedAmount = 0;
        iconLayoutDirty = false;

        if (itemIcon != null)
        {
            itemIcon.enabled = false;
            itemIcon.sprite = null;
            ResetItemIconLayout();
        }

        if (amountText != null)
        {
            amountText.gameObject.SetActive(false);
            amountText.text = string.Empty;
        }
    }

    public void ConfigureBackground(Sprite normalSprite, Sprite selectedSprite = null)
    {
        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();

        defaultBackgroundSprite = normalSprite;
        selectedBackgroundSprite = selectedSprite;
        ApplyBackgroundState();
    }

    public void SetSelected(bool value)
    {
        if (isSelected == value)
            return;

        isSelected = value;
        ApplyBackgroundState();
    }

    // Interacao do ponteiro.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || isDragging)
            return;

        Clicked?.Invoke();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !HasItem || DragStarted == null)
            return;

        isDragging = true;
        ApplyDraggingState(true);
        DragStarted?.Invoke();
        DragMoved?.Invoke(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging)
            return;

        DragMoved?.Invoke(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging)
            return;

        isDragging = false;
        ApplyDraggingState(false);
        DragEnded?.Invoke();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || Dropped == null)
            return;

        InventorySlotVisual draggedSlot = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<InventorySlotVisual>()
            : null;

        if (draggedSlot == null || draggedSlot == this)
            return;

        Dropped?.Invoke(draggedSlot);
    }

    // Utilitarios visuais.
    private void ApplyBackgroundState()
    {
        if (backgroundImage == null)
            return;

        Sprite spriteToUse = isSelected && selectedBackgroundSprite != null
            ? selectedBackgroundSprite
            : defaultBackgroundSprite;
        bool shouldEnableBackground = spriteToUse != null;
        bool shouldEnableOutline = isSelected && selectedBackgroundSprite == null;

        if (backgroundImage.enabled != shouldEnableBackground)
            backgroundImage.enabled = shouldEnableBackground;

        if (spriteToUse != null && backgroundImage.sprite != spriteToUse)
            backgroundImage.sprite = spriteToUse;

        if (selectionOutline.enabled != shouldEnableOutline)
            selectionOutline.enabled = shouldEnableOutline;
    }

    private void OnDisable()
    {
        isDragging = false;
        ApplyDraggingState(false);
    }

    private void ApplyDraggingState(bool dragging)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = dragging ? dragAlpha : 1f;
        canvasGroup.blocksRaycasts = !dragging;
    }

    private float GetCurrentIconScale()
    {
        return GetItemIconScale(displayedItem);
    }

    // Retorna o tamanho atual do slot onde o item esta sendo desenhado,
    // permitindo que o mesmo item se adapte a hotbar, inventario e bau.
    public Vector2 GetCurrentSlotSize()
    {
        if (slotRectTransform != null)
        {
            Vector2 rectSize = slotRectTransform.rect.size;
            if (rectSize.x > 0.01f && rectSize.y > 0.01f)
                return rectSize;
        }

        if (backgroundImage != null)
        {
            RectTransform backgroundRect = backgroundImage.rectTransform;
            Vector2 rectSize = backgroundRect.rect.size;
            if (rectSize.x > 0.01f && rectSize.y > 0.01f)
                return rectSize;
        }

        if (itemIconRectTransform != null && itemIconRectTransform.parent is RectTransform parentRect)
        {
            Vector2 rectSize = parentRect.rect.size;
            if (rectSize.x > 0.01f && rectSize.y > 0.01f)
                return rectSize;
        }

        return Vector2.zero;
    }

    public void ResizeItemSpriteToCurrentSlot()
    {
        ApplyItemIconLayout(displayedItem);
    }

    private void ApplyItemIconLayout(ItemData itemData)
    {
        if (itemIconRectTransform == null)
            return;

        if (itemData == null || itemIcon == null || itemIcon.sprite == null)
            return;

        Vector2 slotSize = GetCurrentSlotSize();
        if (slotSize.x <= 0.01f || slotSize.y <= 0.01f)
        {
            iconLayoutDirty = true;
            return;
        }

        Vector2 availableSize = new Vector2(
            Mathf.Max(8f, slotSize.x - Mathf.Max(0f, iconPadding.x)),
            Mathf.Max(8f, slotSize.y - Mathf.Max(0f, iconPadding.y))
        );

        Vector2 spriteSize = itemIcon.sprite.rect.size;
        if (spriteSize.x <= 0.01f || spriteSize.y <= 0.01f)
            spriteSize = availableSize;

        float fitScale = Mathf.Min(availableSize.x / spriteSize.x, availableSize.y / spriteSize.y);
        fitScale = Mathf.Max(0.01f, fitScale);

        float displayScale = Mathf.Max(0.1f, GetItemIconScale(itemData));
        Vector2 finalSize = spriteSize * fitScale * displayScale * maxIconFill;

        if (finalSize.x > availableSize.x || finalSize.y > availableSize.y)
        {
            float clampScale = Mathf.Min(availableSize.x / finalSize.x, availableSize.y / finalSize.y);
            finalSize *= Mathf.Max(0.01f, clampScale);
        }

        itemIconRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.pivot = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.anchoredPosition = Vector2.zero;
        itemIconRectTransform.sizeDelta = finalSize;
        itemIconRectTransform.localScale = Vector3.one;
        iconLayoutDirty = false;
    }

    private void ResetItemIconLayout()
    {
        if (itemIconRectTransform == null)
            return;

        itemIconRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.pivot = new Vector2(0.5f, 0.5f);
        itemIconRectTransform.anchoredPosition = Vector2.zero;
        itemIconRectTransform.localScale = Vector3.one;
        iconLayoutDirty = false;
    }

    private static float GetItemIconScale(ItemData itemData)
    {
        if (itemData == null)
            return 1f;

        return Mathf.Max(0.1f, itemData.inventoryIconScale);
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        if (!target.TryGetComponent(out T component))
            component = target.AddComponent<T>();

        return component;
    }
}
