using UnityEngine;

public class ResourceNodeDropper : MonoBehaviour
{
    // Configuracao principal dos drops gerados pelo nodo.
    [Header("Drop")]
    [SerializeField] private DroppedItemVisual dropPrefab;
    [SerializeField] private ItemData dropItem;
    [SerializeField, Min(1)] private int totalDropAmount = 4;
    [SerializeField, Range(1, 10)] private int maxVisualDrops = 4;

    // Referencias opcionais usadas para mandar os drops ao jogador.
    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform pickupTargetOverride;

    // Entrada publica chamada quando o nodo e quebrado.
    public void BreakNode()
    {
        if (dropPrefab == null || dropItem == null)
        {
            Debug.LogWarning("DropPrefab ou DropItem nao configurado.");
            return;
        }

        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();

        SpawnDrops();
        Destroy(gameObject);
    }

    // Geracao visual dos itens que vao cair no mundo.
    private void SpawnDrops()
    {
        if (totalDropAmount <= 0)
        {
            Debug.LogWarning("TotalDropAmount precisa ser maior que zero.");
            return;
        }

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
