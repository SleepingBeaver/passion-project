using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class WorldCratePlacementSystem : MonoBehaviour
{
    private const float ReferenceResolveRetryInterval = 0.5f;

    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Grid targetGrid;
    [SerializeField] private EventSystem eventSystem;

    [Header("Placement")]
    [SerializeField] private string placeableCrateItemId = "crate";
    [SerializeField, Min(0.1f)] private float maxPlacementDistance = 2.5f;
    [SerializeField] private Color validTint = new(1f, 1f, 1f, 0.75f);
    [SerializeField] private Color invalidTint = new(1f, 0.45f, 0.45f, 0.72f);
    [SerializeField] private string sortingLayerName = "Objects";
    [SerializeField] private int sortingOrder;

    private Mouse mouse;
    private Transform placementOrigin;
    private SpriteRenderer ghostRenderer;
    private DroppedItemVisual sharedDropPrefab;
    private readonly Collider2D[] placementOverlapResults = new Collider2D[8];
    private ContactFilter2D placementContactFilter;
    private float nextReferenceResolveTime;
    private bool mirrorPlacementSprite;

    private void Awake()
    {
        mouse = Mouse.current;
        ConfigurePlacementContactFilter();
        ResolveReferences();
        EnsureGhost();
        HideGhost();
    }

    private void OnEnable()
    {
        mouse = Mouse.current;
        ConfigurePlacementContactFilter();
        ResolveReferences();
        EnsureGhost();
        HideGhost();
    }

    private void OnDisable()
    {
        HideGhost();
    }

    private void Update()
    {
        mouse ??= Mouse.current;

        TryRefreshMissingReferences();

        if (mouse == null || inventorySystem == null || IsPointerInputBlocked())
        {
            HideGhost();
            return;
        }

        ItemData selectedItem = inventorySystem.SelectedItem;
        if (!IsSelectedCrateItem(selectedItem))
        {
            StopPlacementMode();
            return;
        }

        if (WasMirrorKeyPressed())
            TogglePlacementMirror();

        Vector3 placementPosition = ResolvePlacementPosition();
        bool canPlace = CanPlaceAt(placementPosition);

        ShowGhost(selectedItem.icon, placementPosition, canPlace);

        if (canPlace && mouse.leftButton.wasPressedThisFrame)
            TryPlaceCrate(selectedItem, placementPosition);
    }

    // Evita procurar referencias todo frame quando a cena ainda esta terminando de montar.
    private void TryRefreshMissingReferences()
    {
        if (HasMissingReferences() && Time.unscaledTime >= nextReferenceResolveTime)
            ResolveReferences();
    }

    private bool TryPlaceCrate(ItemData selectedItem, Vector3 placementPosition)
    {
        int selectedSlotIndex = inventorySystem.SelectedSlotIndex;
        if (!inventorySystem.RemoveFromSlot(selectedSlotIndex, 1, out ItemData removedItem, out int removedAmount) ||
            removedAmount <= 0 ||
            removedItem == null)
            return false;

        if (selectedItem != removedItem)
            selectedItem = removedItem;

        GameObject crateObject = new($"{selectedItem.itemName}_World");
        crateObject.transform.position = placementPosition;

        CrateStorageInteractable crate = crateObject.AddComponent<CrateStorageInteractable>();
        crate.Initialize(
            selectedItem,
            ResolveSharedDropPrefab(),
            inventorySystem,
            mirrored: mirrorPlacementSprite
        );

        return true;
    }

    private bool IsSelectedCrateItem(ItemData itemData)
    {
        return itemData != null &&
               !string.IsNullOrWhiteSpace(itemData.itemId) &&
               string.Equals(itemData.itemId, placeableCrateItemId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPointerInputBlocked()
    {
        return Time.timeScale <= 0f ||
               eventSystem != null && eventSystem.IsPointerOverGameObject();
    }

    private Vector3 ResolvePlacementPosition()
    {
        Vector2 mouseScreenPosition = mouse.position.ReadValue();
        Vector3 worldPosition = worldCamera != null
            ? worldCamera.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, Mathf.Abs(worldCamera.transform.position.z)))
            : new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0f);

        worldPosition.z = 0f;

        if (targetGrid == null)
            return new Vector3(Mathf.Round(worldPosition.x), Mathf.Round(worldPosition.y), 0f);

        Vector3Int cell = targetGrid.WorldToCell(worldPosition);
        Vector3 snapped = targetGrid.GetCellCenterWorld(cell);
        snapped.z = 0f;
        return snapped;
    }

    private bool CanPlaceAt(Vector3 placementPosition)
    {
        if (!IsWithinPlacementRange(placementPosition))
            return false;

        Vector2 checkCenter = (Vector2)placementPosition + CrateStorageInteractable.DefaultColliderOffset;
        int hitCount = Physics2D.OverlapBox(
            checkCenter,
            CrateStorageInteractable.DefaultColliderSize,
            0f,
            placementContactFilter,
            placementOverlapResults
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = placementOverlapResults[i];

            if (hit == null || hit.isTrigger)
                continue;

            return false;
        }

        return true;
    }

    private bool IsWithinPlacementRange(Vector3 placementPosition)
    {
        if (placementOrigin == null)
            return true;

        Vector2 origin = placementOrigin.position;
        Vector2 target = placementPosition;
        float maxDistanceSqr = maxPlacementDistance * maxPlacementDistance;
        return (target - origin).sqrMagnitude <= maxDistanceSqr;
    }

    private void ResolveReferences()
    {
        // Se a cena ainda estiver montando, basta tentar de novo mais tarde sem ficar vasculhando tudo em loop.
        nextReferenceResolveTime = Time.unscaledTime + ReferenceResolveRetryInterval;

        inventorySystem ??= FindFirstObjectByType<InventorySystem>();
        worldCamera ??= Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        targetGrid ??= FindFirstObjectByType<Grid>();
        eventSystem ??= EventSystem.current;

        if (placementOrigin == null)
        {
            PlayerInteractor playerInteractor = FindFirstObjectByType<PlayerInteractor>();
            if (playerInteractor != null)
                placementOrigin = playerInteractor.ActorTransform;
        }

        if (placementOrigin == null)
        {
            IsoPlayerController2D playerMovement = FindFirstObjectByType<IsoPlayerController2D>();
            if (playerMovement != null)
                placementOrigin = playerMovement.transform;
        }
    }

    private bool HasMissingReferences()
    {
        return inventorySystem == null ||
               worldCamera == null ||
               eventSystem == null ||
               placementOrigin == null;
    }

    private void ConfigurePlacementContactFilter()
    {
        placementContactFilter = default;
        placementContactFilter.useTriggers = false;
        placementContactFilter.useLayerMask = false;
        placementContactFilter.useDepth = false;
        placementContactFilter.useNormalAngle = false;
    }

    private DroppedItemVisual ResolveSharedDropPrefab()
    {
        if (sharedDropPrefab != null)
            return sharedDropPrefab;

        ResourceNodeDropper sharedDropper = FindFirstObjectByType<ResourceNodeDropper>();
        if (sharedDropper != null)
            sharedDropPrefab = sharedDropper.DropPrefab;

        return sharedDropPrefab;
    }

    private void EnsureGhost()
    {
        if (ghostRenderer != null)
            return;

        GameObject ghostObject = new("CratePlacementGhost");
        ghostObject.transform.SetParent(transform, false);
        ghostRenderer = ghostObject.AddComponent<SpriteRenderer>();
        ghostRenderer.sortingLayerName = sortingLayerName;
        ghostRenderer.sortingOrder = sortingOrder;
        ghostRenderer.color = validTint;
    }

    private void ShowGhost(Sprite iconSprite, Vector3 worldPosition, bool canPlace)
    {
        if (ghostRenderer == null)
            return;

        if (ghostRenderer.sprite != iconSprite)
            ghostRenderer.sprite = iconSprite;

        if (ghostRenderer.flipX != mirrorPlacementSprite)
            ghostRenderer.flipX = mirrorPlacementSprite;

        Color tint = canPlace ? validTint : invalidTint;
        if (ghostRenderer.color != tint)
            ghostRenderer.color = tint;

        if (ghostRenderer.transform.position != worldPosition)
            ghostRenderer.transform.position = worldPosition;

        if (!ghostRenderer.gameObject.activeSelf)
            ghostRenderer.gameObject.SetActive(true);
    }

    private void HideGhost()
    {
        if (ghostRenderer != null && ghostRenderer.gameObject.activeSelf)
            ghostRenderer.gameObject.SetActive(false);
    }

    private void StopPlacementMode()
    {
        mirrorPlacementSprite = false;

        if (ghostRenderer != null)
            ghostRenderer.flipX = false;

        HideGhost();
    }

    private void TogglePlacementMirror()
    {
        mirrorPlacementSprite = !mirrorPlacementSprite;

        if (ghostRenderer != null)
            ghostRenderer.flipX = mirrorPlacementSprite;
    }

    private bool WasMirrorKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.rKey.wasPressedThisFrame;
    }
}

public static class CrateGameplayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallSystems();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallSystems();
    }

    private static void InstallSystems()
    {
        InventorySystem inventorySystem = UnityEngine.Object.FindFirstObjectByType<InventorySystem>();
        if (inventorySystem != null && !inventorySystem.TryGetComponent(out WorldCratePlacementSystem _))
            inventorySystem.gameObject.AddComponent<WorldCratePlacementSystem>();

        CrateStorageUI.GetOrCreate();
    }
}
