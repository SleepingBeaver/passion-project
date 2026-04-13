using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryDebugInput : MonoBehaviour
{
    // Configuracao usada apenas em ambiente de teste.
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private ItemData woodItem;
    [SerializeField] private int addAmountPerPress = 1;
    [SerializeField] private int removeAmountPerPress = 1;

    private Keyboard keyboard;

    // Ciclo de vida.
    private void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        enabled = false;
#endif

        keyboard = Keyboard.current;
    }

    // Atalhos de teste para povoar e limpar o inventario.
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        keyboard ??= Keyboard.current;

        if (keyboard == null || inventorySystem == null || woodItem == null)
            return;

        if (keyboard.mKey.wasPressedThisFrame)
        {
            bool addedAll = inventorySystem.AddItem(woodItem, addAmountPerPress);

            Debug.Log(addedAll
                ? $"Adicionado: {addAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Inventario cheio. Total atual de {woodItem.itemName}: {inventorySystem.CountItem(woodItem)}");
        }

        if (keyboard.nKey.wasPressedThisFrame)
        {
            bool removed = inventorySystem.RemoveItem(woodItem, removeAmountPerPress);

            Debug.Log(removed
                ? $"Removido: {removeAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Nao ha quantidade suficiente para remover. Total atual: {inventorySystem.CountItem(woodItem)}");
        }
#endif
    }
}
