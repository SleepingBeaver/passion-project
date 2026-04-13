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

        bool added = interactor.InventorySystem.AddItem(itemData, amount);

        if (!added)
        {
            Debug.Log($"Inventario cheio. Nao foi possivel coletar {itemData.itemName}.");
            return false;
        }

        if (destroyOnCollect)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);

        return true;
    }
}
