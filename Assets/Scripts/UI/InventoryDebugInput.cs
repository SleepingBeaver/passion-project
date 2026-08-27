using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryDebugInput : MonoBehaviour
{
    // Configuracao usada apenas em ambiente de teste.
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private ItemData woodItem;
    [SerializeField] private ItemData crateItem;
    [SerializeField] private ItemData tomatoSeedItem;
    [SerializeField] private int addAmountPerPress = 1;
    [SerializeField] private int removeAmountPerPress = 1;
    [SerializeField] private int addCrateAmountPerPress = 1;
    [SerializeField, Min(1)] private int addTomatoSeedAmountPerPress = 10;

    [Header("Day Cycle Debug")]
    [SerializeField] private GameplayDayCycleController gameplayDayCycleController;

    private Keyboard keyboard;

    public bool TryGetConfiguredItem(string itemId, out ItemData itemData)
    {
        itemData = null;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (MatchesItemId(woodItem, itemId))
        {
            itemData = woodItem;
            return true;
        }

        if (MatchesItemId(crateItem, itemId))
        {
            itemData = crateItem;
            return true;
        }

        if (MatchesItemId(tomatoSeedItem, itemId))
        {
            itemData = tomatoSeedItem;
            return true;
        }

        return false;
    }

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

        if (keyboard == null)
            return;

        // Atalho temporario. A futura cama chamara o mesmo RequestSleep().
        if (gameplayDayCycleController != null && keyboard.numpad1Key.wasPressedThisFrame)
        {
            bool accepted = gameplayDayCycleController.RequestSleep();
            Debug.Log(accepted
                ? "Sono solicitado pelo atalho temporario NumPad 1."
                : "Solicitacao de sono ignorada porque uma transicao ja esta em andamento.");
        }

        if (inventorySystem == null)
            return;

        if (woodItem != null && keyboard.mKey.wasPressedThisFrame)
        {
            bool addedAll = inventorySystem.AddItem(woodItem, addAmountPerPress);

            Debug.Log(addedAll
                ? $"Adicionado: {addAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Inventario cheio. Total atual de {woodItem.itemName}: {inventorySystem.CountItem(woodItem)}");
        }

        if (woodItem != null && keyboard.nKey.wasPressedThisFrame)
        {
            bool removed = inventorySystem.RemoveItem(woodItem, removeAmountPerPress);

            Debug.Log(removed
                ? $"Removido: {removeAmountPerPress}x {woodItem.itemName}. Total: {inventorySystem.CountItem(woodItem)}"
                : $"Nao ha quantidade suficiente para remover. Total atual: {inventorySystem.CountItem(woodItem)}");
        }

        if (crateItem != null && keyboard.bKey.wasPressedThisFrame)
        {
            bool addedAll = inventorySystem.AddItem(crateItem, addCrateAmountPerPress);

            Debug.Log(addedAll
                ? $"Adicionado: {addCrateAmountPerPress}x {crateItem.itemName}. Total: {inventorySystem.CountItem(crateItem)}"
                : $"Inventario cheio. Total atual de {crateItem.itemName}: {inventorySystem.CountItem(crateItem)}");
        }

        if (tomatoSeedItem != null && keyboard.tKey.wasPressedThisFrame)
            AddTomatoSeedsForDebug();
#endif
    }

    public bool AddTomatoSeedsForDebug()
    {
        if (inventorySystem == null || tomatoSeedItem == null)
            return false;

        bool addedAll = inventorySystem.AddItem(tomatoSeedItem, addTomatoSeedAmountPerPress);
        Debug.Log(addedAll
            ? $"Debug [T]: {addTomatoSeedAmountPerPress}x {tomatoSeedItem.itemName} adicionadas. Total: {inventorySystem.CountItem(tomatoSeedItem)}"
            : $"Inventario cheio. Total atual de {tomatoSeedItem.itemName}: {inventorySystem.CountItem(tomatoSeedItem)}");
        return addedAll;
    }

    [ContextMenu("Debug/Add Tomato Seeds")]
    private void AddTomatoSeedsFromContextMenu()
    {
        AddTomatoSeedsForDebug();
    }

    private static bool MatchesItemId(ItemData itemData, string itemId)
    {
        return itemData != null &&
               !string.IsNullOrWhiteSpace(itemData.itemId) &&
               string.Equals(itemData.itemId, itemId, System.StringComparison.OrdinalIgnoreCase);
    }
}
