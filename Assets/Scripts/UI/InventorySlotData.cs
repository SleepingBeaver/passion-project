using System;

[Serializable]
public class InventorySlotData
{
    public InventoryItemData item;
    public int amount;

    public bool IsEmpty => item == null || amount <= 0;

    public void SetItem(InventoryItemData newItem, int newAmount)
    {
        item = newItem;
        amount = newAmount;
    }

    public void Clear()
    {
        item = null;
        amount = 0;
    }
}