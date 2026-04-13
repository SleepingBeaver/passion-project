using System;

[Serializable]
public class InventorySlotData
{
    // Dados armazenados dentro de um slot do inventario.
    public ItemData item;
    public int amount;

    // Leitura rapida do estado do slot.
    public bool IsEmpty => item == null || amount <= 0;

    // Mutacoes basicas do slot.
    public void SetItem(ItemData newItem, int newAmount)
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
