using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIInventorySlot : MonoBehaviour
{
    // Referencias visuais do slot simples.
    [Header("UI")]
    public Image icon;
    public TextMeshProUGUI quantityText;
    public GameObject highlight;

    // Estado interno do item mostrado.
    private ItemData currentItem;

    // Atualizacao do conteudo exibido.
    public void SetItem(ItemData item, int amount = 1)
    {
        currentItem = item;

        if (item == null)
        {
            Clear();
            return;
        }

        if (icon != null)
        {
            icon.sprite = item.icon;
            icon.enabled = true;
        }

        if (quantityText != null)
            quantityText.text = amount > 1 ? amount.ToString() : string.Empty;
    }

    public void Clear()
    {
        currentItem = null;

        if (icon != null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }

        if (quantityText != null)
            quantityText.text = string.Empty;
    }

    // Destaque visual do slot selecionado.
    public void SetSelected(bool value)
    {
        if (highlight != null)
            highlight.SetActive(value);
    }
}
