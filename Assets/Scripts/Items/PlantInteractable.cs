using UnityEngine;

public class PlantInteractable : WorldInteractable
{
    // Configuracao da coleta da planta.
    [Header("Plant")]
    [SerializeField] private ItemData itemData;
    [SerializeField, Min(1)] private int amount = 1;
    [SerializeField] private bool destroyOnCollect = true;

    // Fluxo de interacao com a planta.
    protected override bool PerformInteraction(PlayerInteractor interactor)
    {
        if (itemData == null || interactor.InventorySystem == null)
            return false;

        interactor.InventorySystem.AddItem(itemData, amount, out int addedAmount);

        if (addedAmount <= 0)
        {
            Debug.Log($"Inventario cheio. Nao foi possivel coletar {itemData.itemName}.");
            return false;
        }

        amount -= addedAmount;

        if (amount > 0)
        {
            Debug.Log($"Inventario cheio. Coletado parcialmente: {addedAmount}x {itemData.itemName}.");
            return true;
        }

        if (destroyOnCollect)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);

        return true;
    }
}
