using UnityEngine;

[CreateAssetMenu(fileName = "NewInventoryItem", menuName = "Passion Town/Inventory/Item Data")]
public class InventoryItemData : ScriptableObject
{
    [Header("Info")]
    public string itemId;
    public string itemName;
    public Sprite icon;

    [Header("Stack")]
    [Min(1)] public int maxStack = 999;
}