using UnityEngine;

public class ResourceNodeDropper : MonoBehaviour
{
    [Header("Drop")]
    [SerializeField] private DroppedItemVisual dropPrefab;
    [SerializeField] private InventoryItemData dropItem;
    [SerializeField, Min(1)] private int totalDropAmount = 4;
    [SerializeField, Range(1, 10)] private int maxVisualDrops = 4;

    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform pickupTargetOverride;

    public void BreakNode()
    {
        if (dropPrefab == null || dropItem == null)
        {
            Debug.LogWarning("DropPrefab ou DropItem não configurado.");
            return;
        }

        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();

        SpawnDrops();
        Destroy(gameObject);
    }

    private void SpawnDrops()
    {
        int pieceCount = Mathf.Min(maxVisualDrops, totalDropAmount);
        int baseAmountPerDrop = totalDropAmount / pieceCount;
        int remainder = totalDropAmount % pieceCount;

        for (int i = 0; i < pieceCount; i++)
        {
            int pieceAmount = baseAmountPerDrop + (i < remainder ? 1 : 0);

            DroppedItemVisual dropInstance = Instantiate(
                dropPrefab,
                transform.position,
                Quaternion.identity
            );

            dropInstance.Initialize(
                dropItem,
                pieceAmount,
                inventorySystem,
                pickupTargetOverride
            );
        }
    }
}