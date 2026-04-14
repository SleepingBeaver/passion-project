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
}
