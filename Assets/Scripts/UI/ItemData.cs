using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Passion Town/Inventory/Item Data")]
public class ItemData : ScriptableObject
{
    // Informacoes principais do item.
    [Header("Info")]
    public string itemId;
    public string itemName;
    public Sprite icon;

    // Configuracao de pilha no inventario.
    [Header("Stack")]
    [Min(1)] public int maxStack = 999;

    [Header("Rules")]
    public bool isUnique;

    [Header("UI")]
    [Min(0.1f)] public float inventoryIconScale = 1f;

    [Header("World")]
    [Min(0.01f)] public float worldIconScale = 1f;

    [Header("Placement")]
    [Tooltip("Quantidade de celulas da grade ocupadas pelo item nos eixos X e Y.")]
    [SerializeField] private Vector2Int placementFootprintSize = Vector2Int.one;

    public Vector2Int PlacementFootprintSize => new(
        Mathf.Max(1, placementFootprintSize.x),
        Mathf.Max(1, placementFootprintSize.y)
    );

    private void OnValidate()
    {
        itemId = itemId?.Trim();
        placementFootprintSize = PlacementFootprintSize;
    }
}
