using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventorySlotVisual : MonoBehaviour
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text amountText;

    public void Refresh(InventorySlotData slotData)
    {
        if (slotData == null || slotData.IsEmpty)
        {
            SetEmpty();
            return;
        }

        if (itemIcon != null)
        {
            itemIcon.enabled = true;
            itemIcon.sprite = slotData.item.icon;
        }

        if (amountText != null)
        {
            amountText.gameObject.SetActive(slotData.amount > 1);
            amountText.text = slotData.amount.ToString();
        }
    }

    public void SetEmpty()
    {
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
}