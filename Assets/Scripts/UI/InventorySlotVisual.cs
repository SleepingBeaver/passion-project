using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotVisual : MonoBehaviour, IPointerClickHandler
{
    // Referencias visuais do slot.
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Sprite defaultBackgroundSprite;
    [SerializeField] private Sprite selectedBackgroundSprite;
    [SerializeField] private Color selectionOutlineColor = new Color(1f, 0.83f, 0.31f, 1f);
    [SerializeField] private Vector2 selectionOutlineDistance = new Vector2(5f, -5f);

    // Estado interno do que esta sendo exibido.
    private ItemData displayedItem;
    private int displayedAmount = -1;
    private bool isSelected;
    private Outline selectionOutline;

    // Evento disparado quando o jogador clica no slot.
    public event Action Clicked;

    // Ciclo de vida.
    private void Awake()
    {
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
        selectionOutline.effectColor = selectionOutlineColor;
        selectionOutline.effectDistance = selectionOutlineDistance;
        selectionOutline.useGraphicAlpha = true;
        selectionOutline.enabled = false;
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

        if (itemIcon != null)
        {
            itemIcon.enabled = true;
            itemIcon.sprite = slotData.item.icon;
            itemIcon.preserveAspect = true;
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

        if (itemIcon != null)
        {
            itemIcon.enabled = false;
            itemIcon.sprite = null;
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
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        Clicked?.Invoke();
    }

    // Utilitarios visuais.
    private void ApplyBackgroundState()
    {
        if (backgroundImage == null)
            return;

        Sprite spriteToUse = isSelected && selectedBackgroundSprite != null
            ? selectedBackgroundSprite
            : defaultBackgroundSprite;

        backgroundImage.enabled = spriteToUse != null;

        if (spriteToUse != null)
            backgroundImage.sprite = spriteToUse;

        selectionOutline.enabled = isSelected && selectedBackgroundSprite == null;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        if (!target.TryGetComponent(out T component))
            component = target.AddComponent<T>();

        return component;
    }
}
