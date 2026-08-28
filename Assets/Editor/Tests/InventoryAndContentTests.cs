using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class InventoryAndContentTests
{
    private readonly List<UnityEngine.Object> temporaryObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = temporaryObjects.Count - 1; i >= 0; i--)
        {
            if (temporaryObjects[i] != null)
                UnityEngine.Object.DestroyImmediate(temporaryObjects[i]);
        }

        temporaryObjects.Clear();
    }

    [Test]
    public void ItemIdentity_MatchesEquivalentIdsCaseInsensitively()
    {
        ItemData first = CreateItem("tomato_seed");
        ItemData second = CreateItem(" TOMATO_SEED ");

        Assert.That(ItemIdentity.Matches(first, second), Is.True);
        Assert.That(ItemIdentity.Matches(first, "Tomato_Seed"), Is.True);
        Assert.That(ItemIdentity.Matches(first, (ItemData)null), Is.False);
        Assert.That(ItemIdentity.Matches(string.Empty, string.Empty), Is.False);
    }

    [Test]
    public void InventoryRemoval_IsAtomicAndUsesStableIdentity()
    {
        InventorySystem inventory = CreateComponent<InventorySystem>("Inventory_Test");
        ItemData storedItem = CreateItem("wood");
        ItemData equivalentItem = CreateItem("WOOD");

        Assert.That(inventory.AddItem(storedItem, 2), Is.True);
        Assert.That(inventory.CountItem(equivalentItem), Is.EqualTo(2));

        Assert.That(inventory.RemoveItem(equivalentItem, 3), Is.False);
        Assert.That(inventory.CountItem(storedItem), Is.EqualTo(2), "Uma remocao insuficiente nao pode consumir parcialmente a pilha.");

        Assert.That(inventory.RemoveItem(equivalentItem, 1), Is.True);
        Assert.That(inventory.CountItem(storedItem), Is.EqualTo(1));
    }

    [Test]
    public void CrateRemoval_IsAtomicAndUsesStableIdentity()
    {
        CrateStorageInteractable crate = CreateComponent<CrateStorageInteractable>("Crate_Test");
        ItemData storedItem = CreateItem("tomato");
        ItemData equivalentItem = CreateItem("TOMATO");

        Assert.That(crate.AddItem(storedItem, 2), Is.True);
        Assert.That(crate.RemoveItem(equivalentItem, 3), Is.False);
        Assert.That(crate.CountItem(storedItem), Is.EqualTo(2));

        Assert.That(crate.RemoveItem(equivalentItem, 2), Is.True);
        Assert.That(crate.IsEmpty, Is.True);
    }

    [Test]
    public void InventorySlots_ExposeReadOnlyState()
    {
        PropertyInfo itemProperty = typeof(InventorySlotData).GetProperty(nameof(InventorySlotData.Item));
        PropertyInfo amountProperty = typeof(InventorySlotData).GetProperty(nameof(InventorySlotData.Amount));

        Assert.That(itemProperty, Is.Not.Null);
        Assert.That(amountProperty, Is.Not.Null);
        Assert.That(itemProperty.SetMethod, Is.Null);
        Assert.That(amountProperty.SetMethod, Is.Null);
    }

    [Test]
    public void ContentCatalog_ContainsEveryItemAndCropWithUniqueIds()
    {
        GameContentCatalog catalog = GameContentCatalog.LoadDefault();
        Assert.That(catalog, Is.Not.Null, "Assets/Resources/GameContentCatalog.asset precisa existir.");

        HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
        string[] itemGuids = AssetDatabase.FindAssets("t:ItemData", new[] { "Assets" });
        for (int i = 0; i < itemGuids.Length; i++)
        {
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(itemGuids[i]));
            Assert.That(item, Is.Not.Null);
            Assert.That(item.itemId, Is.Not.Null.And.Not.Empty, $"Item sem itemId: {item.name}");
            Assert.That(itemIds.Add(item.itemId.Trim()), Is.True, $"itemId duplicado: {item.itemId}");
            Assert.That(catalog.TryGetItem(item.itemId, out ItemData catalogItem), Is.True, $"Item ausente do catalogo: {item.itemId}");
            Assert.That(catalogItem, Is.SameAs(item));
        }

        HashSet<string> cropIds = new(StringComparer.OrdinalIgnoreCase);
        string[] cropGuids = AssetDatabase.FindAssets("t:CropDefinition", new[] { "Assets" });
        for (int i = 0; i < cropGuids.Length; i++)
        {
            CropDefinition crop = AssetDatabase.LoadAssetAtPath<CropDefinition>(AssetDatabase.GUIDToAssetPath(cropGuids[i]));
            Assert.That(crop, Is.Not.Null);
            Assert.That(crop.CropId, Is.Not.Null.And.Not.Empty, $"Cultura sem cropId: {crop.name}");
            Assert.That(cropIds.Add(crop.CropId.Trim()), Is.True, $"cropId duplicado: {crop.CropId}");
            Assert.That(catalog.TryGetCrop(crop.CropId, out CropDefinition catalogCrop), Is.True, $"Cultura ausente do catalogo: {crop.CropId}");
            Assert.That(catalogCrop, Is.SameAs(crop));
        }

        HashSet<string> dishIds = new(StringComparer.OrdinalIgnoreCase);
        string[] dishGuids = AssetDatabase.FindAssets("t:DishDefinition", new[] { "Assets" });
        for (int i = 0; i < dishGuids.Length; i++)
        {
            DishDefinition dish = AssetDatabase.LoadAssetAtPath<DishDefinition>(AssetDatabase.GUIDToAssetPath(dishGuids[i]));
            Assert.That(dish, Is.Not.Null);
            Assert.That(dish.IsConfigured, Is.True, $"Prato inválido: {dish.name}");
            Assert.That(dishIds.Add(dish.DishId.Trim()), Is.True, $"dishId duplicado: {dish.DishId}");
            Assert.That(catalog.TryGetDish(dish.DishId, out DishDefinition catalogDish), Is.True, $"Prato ausente do catálogo: {dish.DishId}");
            Assert.That(catalogDish, Is.SameAs(dish));
            Assert.That(catalog.TryGetItem(dish.PreparedItem.itemId, out ItemData preparedItem), Is.True);
            Assert.That(preparedItem, Is.SameAs(dish.PreparedItem));
        }
    }

    [Test]
    public void RestaurantService_ServesDishAndAwardsMoneyAndReputation()
    {
        InventorySystem inventory = CreateComponent<InventorySystem>("Restaurant_Inventory_Test");
        WorldInfoSystem world = CreateComponent<WorldInfoSystem>("Restaurant_World_Test");
        RestaurantServiceSystem service = CreateComponent<RestaurantServiceSystem>("Restaurant_Service_Test");
        ItemData tomato = CreateItem("tomato");
        ItemData soup = CreateItem("tomato_soup");
        DishDefinition dish = CreateDish(tomato, soup, cookingSeconds: 0f);

        world.SetMoney(100);
        service.Initialize(null, world, null);
        Assert.That(service.ForceStartOrder(dish, "Clara", 30f), Is.True);
        Assert.That(inventory.AddItem(soup, 1), Is.True);

        Assert.That(service.TryServeActiveOrder(inventory), Is.True);
        Assert.That(inventory.CountItem(soup), Is.Zero);
        Assert.That(world.Money, Is.EqualTo(180));
        Assert.That(service.Reputation, Is.EqualTo(5));
        Assert.That(service.CompletedOrdersToday, Is.EqualTo(1));
        Assert.That(service.HasActiveOrder, Is.False);
    }

    [Test]
    public void KitchenStation_ConsumesIngredientsAndCreatesPreparedDish()
    {
        InventorySystem inventory = CreateComponent<InventorySystem>("Kitchen_Inventory_Test");
        KitchenStationInteractable kitchen = CreateComponent<KitchenStationInteractable>("Kitchen_Station_Test");
        ItemData tomato = CreateItem("tomato");
        ItemData soup = CreateItem("tomato_soup");
        DishDefinition dish = CreateDish(tomato, soup, cookingSeconds: 0f);

        Assert.That(inventory.AddItem(tomato, 2), Is.True);
        kitchen.Initialize(dish, inventory);

        Assert.That(kitchen.TryBeginCooking(inventory), Is.True);
        Assert.That(inventory.CountItem(tomato), Is.Zero);
        Assert.That(kitchen.ReadyServings, Is.EqualTo(1));

        Assert.That(kitchen.TryCollectPreparedDish(inventory), Is.True);
        Assert.That(kitchen.ReadyServings, Is.Zero);
        Assert.That(inventory.CountItem(soup), Is.EqualTo(1));
    }

    private ItemData CreateItem(string itemId)
    {
        ItemData item = ScriptableObject.CreateInstance<ItemData>();
        item.itemId = itemId;
        item.itemName = itemId;
        item.maxStack = 999;
        temporaryObjects.Add(item);
        return item;
    }

    private DishDefinition CreateDish(ItemData ingredient, ItemData preparedItem, float cookingSeconds)
    {
        DishDefinition dish = ScriptableObject.CreateInstance<DishDefinition>();
        dish.DishId = "tomato_soup";
        dish.DisplayName = "Sopa de tomate caseira";
        dish.PreparedItem = preparedItem;
        dish.CookingDurationSeconds = cookingSeconds;
        dish.SellPrice = 80;
        dish.ReputationReward = 5;
        dish.Ingredients = new[]
        {
            new DishIngredientRequirement
            {
                Item = ingredient,
                Amount = 2
            }
        };
        temporaryObjects.Add(dish);
        return dish;
    }

    private T CreateComponent<T>(string name) where T : Component
    {
        GameObject gameObject = new(name);
        temporaryObjects.Add(gameObject);
        T component = gameObject.AddComponent<T>();
        MethodInfo awake = typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        awake?.Invoke(component, null);
        return component;
    }
}
