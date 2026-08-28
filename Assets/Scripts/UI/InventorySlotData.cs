using System;

[Serializable]
public class InventorySlotData
{
    // Dados armazenados dentro de um slot do inventario.
    private ItemData item;
    private int amount;

    // Leitura rapida do estado do slot.
    public ItemData Item => item;
    public int Amount => amount;
    public bool IsEmpty => item == null || amount <= 0;

    // Mutacoes basicas do slot.
    public void SetItem(ItemData newItem, int newAmount)
    {
        if (newItem == null || newAmount <= 0)
        {
            Clear();
            return;
        }

        item = newItem;
        amount = newAmount;
    }

    public void Clear()
    {
        item = null;
        amount = 0;
    }

    public int AddAmount(int value)
    {
        if (IsEmpty || value <= 0)
            return 0;

        amount += value;
        return value;
    }

    public int RemoveAmount(int value)
    {
        if (IsEmpty || value <= 0)
            return 0;

        int removedAmount = Math.Min(amount, value);
        amount -= removedAmount;

        if (amount <= 0)
            Clear();

        return removedAmount;
    }
}
