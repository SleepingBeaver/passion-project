using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryDebugInput : MonoBehaviour
{
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private InventoryItemData woodItem;
    [SerializeField] private int addAmountPerPress = 1;
    [SerializeField] private int removeAmountPerPress = 1;

    private void Update()
    {
        if (Keyboard.current == null || inventorySystem == null || woodItem == null)
            return;

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            bool addedAll = inventorySystem.AddItem(woodItem, addAmountPerPress);

            Debug.Log(addedAll
                ? $"Adicionado: {addAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Inventário cheio. Total atual de {woodItem.itemName}: {inventorySystem.CountItem(woodItem)}");
        }

        if (Keyboard.current.nKey.wasPressedThisFrame)
        {
            bool removed = inventorySystem.RemoveItem(woodItem, removeAmountPerPress);

            Debug.Log(removed
                ? $"Removido: {removeAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Não ha quantidade suficiente para remover. Total atual: {inventorySystem.CountItem(woodItem)}");
        }
    }
}