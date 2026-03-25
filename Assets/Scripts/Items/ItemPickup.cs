using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class ItemPickup : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private InventoryItemData itemData;
    [SerializeField, Min(1)] private int amount = 1;

    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;

    [Header("Feedback")]
    [SerializeField] private bool destroyOnPickup = true;

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (itemData == null || inventorySystem == null)
            return;

        bool added = inventorySystem.AddItem(itemData, amount);

        if (!added)
        {
            Debug.Log($"Inventário cheio. Não foi possível coletar {itemData.itemName}.");
            return;
        }

        Debug.Log($"Coletado: {amount}x {itemData.itemName}");

        if (destroyOnPickup)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}