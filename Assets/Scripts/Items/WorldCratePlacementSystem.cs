using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class WorldCratePlacementSystem : MonoBehaviour
{
    // Configuracao do item reconhecido, alcance, colisao e feedback do ghost.
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
    private Keyboard keyboard;
    private Transform placementOrigin;
    private SpriteRenderer ghostRenderer;
    private DroppedItemVisual sharedDropPrefab;
    private readonly Collider2D[] placementOverlapResults = new Collider2D[32];
    private ContactFilter2D placementContactFilter;
    private float nextReferenceResolveTime;
    private float maxPlacementDistanceSqr;
    private bool mirrorPlacementSprite;

    // Ciclo de vida, cache de input e assinatura no inventario.
    private void Awake()
    {
        mouse = Mouse.current;
        keyboard = Keyboard.current;
        CachePlacementDistance();
        ConfigurePlacementContactFilter();
        ResolveReferences();
        EnsureGhost();
        HideGhost();
    }

    private void OnEnable()
    {
        mouse = Mouse.current;
        keyboard = Keyboard.current;
        CachePlacementDistance();
        ConfigurePlacementContactFilter();
        ResolveReferences();
        EnsureGhost();
        HideGhost();
    }

    private void OnDisable()
    {
        HideGhost();
    }

    private void OnValidate()
    {
        CachePlacementDistance();
    }

    private void Update()
    {
        mouse ??= Mouse.current;
        keyboard ??= Keyboard.current;

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

        Vector3 placementPosition = ResolvePlacementPosition(out Vector3Int placementCell);
        bool canPlace = CanPlaceAt(selectedItem, placementCell, placementPosition);

        ShowGhost(selectedItem.icon, placementPosition, canPlace);

        if (canPlace && mouse.leftButton.wasPressedThisFrame)
            TryPlaceCrate(selectedItem, placementCell, placementPosition);
    }

    // Evita procurar referencias todo frame quando a cena ainda esta terminando de montar.
    private void TryRefreshMissingReferences()
    {
        if (HasMissingReferences() && Time.unscaledTime >= nextReferenceResolveTime)
            ResolveReferences();
    }

    // Validacao e commit atomico do placement; o item so e consumido apos a criacao.
    private bool TryPlaceCrate(ItemData selectedItem, Vector3Int placementCell, Vector3 placementPosition)
    {
        if (inventorySystem == null || selectedItem == null || targetGrid == null)
            return false;

        GameObject crateObject = null;
        bool itemWasConsumed = false;

        try
        {
            crateObject = new GameObject($"{selectedItem.itemName}_World");
            crateObject.SetActive(false);
            crateObject.transform.position = placementPosition;

            PlaceableItemOccupancy occupancy = crateObject.AddComponent<PlaceableItemOccupancy>();
            occupancy.Initialize(targetGrid, placementCell, selectedItem.PlacementFootprintSize);

            CrateStorageInteractable crate = crateObject.AddComponent<CrateStorageInteractable>();
            crate.Initialize(
                selectedItem,
                ResolveSharedDropPrefab(),
                inventorySystem,
                mirrored: mirrorPlacementSprite
            );

            int selectedSlotIndex = inventorySystem.SelectedSlotIndex;
            bool removedFromInventory = inventorySystem.RemoveFromSlot(
                selectedSlotIndex,
                1,
                out ItemData removedItem,
                out int removedAmount);
            if (!removedFromInventory || removedAmount != 1 || !ItemIdentity.Matches(selectedItem, removedItem))
            {
                if (removedAmount > 0 && removedItem != null)
                    inventorySystem.AddItem(removedItem, removedAmount, out _);

                Destroy(crateObject);
                return false;
            }

            itemWasConsumed = true;
            crateObject.SetActive(true);

            if (occupancy.IsRegistered)
                return true;

            inventorySystem.AddItem(selectedItem, 1, out _);
            itemWasConsumed = false;
            crateObject.SetActive(false);
            Destroy(crateObject);
            return false;
        }
        catch (Exception exception)
        {
            if (itemWasConsumed)
                inventorySystem.AddItem(selectedItem, 1, out _);

            if (crateObject != null)
            {
                crateObject.SetActive(false);
                Destroy(crateObject);
            }

            Debug.LogException(exception, this);
            return false;
        }
    }

    private bool IsSelectedCrateItem(ItemData itemData)
    {
        return ItemIdentity.Matches(itemData, placeableCrateItemId);
    }

    private bool IsPointerInputBlocked()
    {
        return Time.timeScale <= 0f ||
               eventSystem != null && eventSystem.IsPointerOverGameObject();
    }

    // Conversao do ponteiro para o Grid e testes fisicos sem alocacao.
    private Vector3 ResolvePlacementPosition(out Vector3Int placementCell)
    {
        Vector2 mouseScreenPosition = mouse.position.ReadValue();
        Vector3 worldPosition = worldCamera != null
            ? worldCamera.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, Mathf.Abs(worldCamera.transform.position.z)))
            : new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0f);

        worldPosition.z = 0f;

        if (targetGrid == null)
        {
            placementCell = new Vector3Int(
                Mathf.RoundToInt(worldPosition.x),
                Mathf.RoundToInt(worldPosition.y),
                0
            );
            return new Vector3(placementCell.x, placementCell.y, 0f);
        }

        placementCell = targetGrid.WorldToCell(worldPosition);
        Vector3 snapped = targetGrid.GetCellCenterWorld(placementCell);
        snapped.z = 0f;
        return snapped;
    }

    private bool CanPlaceAt(ItemData selectedItem, Vector3Int placementCell, Vector3 placementPosition)
    {
        if (!IsWithinPlacementRange(placementPosition))
            return false;

        Vector2Int footprintSize = selectedItem != null
            ? selectedItem.PlacementFootprintSize
            : Vector2Int.one;

        if (!PlaceableItemOccupancy.CanOccupy(targetGrid, placementCell, footprintSize))
            return false;

        for (int y = 0; y < footprintSize.y; y++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                Vector3Int cell = placementCell + new Vector3Int(x, y, 0);
                Vector3 cellCenter = targetGrid != null
                    ? targetGrid.GetCellCenterWorld(cell)
                    : placementPosition + new Vector3(x, y, 0f);

                if (HasBlockingCollision(cellCenter))
                    return false;
            }
        }

        return true;
    }

    private bool HasBlockingCollision(Vector3 cellCenter)
    {
        Vector2 checkCenter = (Vector2)cellCenter + CrateStorageInteractable.DefaultColliderOffset;
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

            PlaceableItemOccupancy placeableOccupancy = hit.GetComponentInParent<PlaceableItemOccupancy>();
            if (placeableOccupancy != null &&
                placeableOccupancy.IsRegistered &&
                placeableOccupancy.TargetGrid == targetGrid)
                continue;

            return false;
        }

        return false;
    }

    private bool IsWithinPlacementRange(Vector3 placementPosition)
    {
        if (placementOrigin == null)
            return true;

        Vector2 origin = placementOrigin.position;
        Vector2 target = placementPosition;
        return (target - origin).sqrMagnitude <= maxPlacementDistanceSqr;
    }

    // Resolucao tardia das dependencias compartilhadas da cena.
    private void ResolveReferences()
    {
        // Se a cena ainda estiver montando, basta tentar de novo mais tarde sem ficar vasculhando tudo em loop.
        nextReferenceResolveTime = Time.unscaledTime + ReferenceResolveRetryInterval;

        inventorySystem ??= FindAnyObjectByType<InventorySystem>();

        if (worldCamera == null)
        {
            Camera mainCamera = Camera.main;
            worldCamera = mainCamera != null ? mainCamera : FindAnyObjectByType<Camera>();
        }

        targetGrid ??= FindAnyObjectByType<Grid>();
        eventSystem ??= EventSystem.current;

        if (placementOrigin == null)
        {
            PlayerInteractor playerInteractor = FindAnyObjectByType<PlayerInteractor>();
            if (playerInteractor != null)
                placementOrigin = playerInteractor.ActorTransform;
        }

        if (placementOrigin == null)
        {
            IsoPlayerController2D playerMovement = FindAnyObjectByType<IsoPlayerController2D>();
            if (playerMovement != null)
                placementOrigin = playerMovement.transform;
        }
    }

    private bool HasMissingReferences()
    {
        return inventorySystem == null ||
               worldCamera == null ||
               targetGrid == null ||
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

    private void CachePlacementDistance()
    {
        maxPlacementDistanceSqr = maxPlacementDistance * maxPlacementDistance;
    }

    private DroppedItemVisual ResolveSharedDropPrefab()
    {
        if (sharedDropPrefab != null)
            return sharedDropPrefab;

        ResourceNodeDropper sharedDropper = FindAnyObjectByType<ResourceNodeDropper>();
        if (sharedDropper != null)
            sharedDropPrefab = sharedDropper.DropPrefab;

        return sharedDropPrefab;
    }

    // Feedback visual reutilizado enquanto o jogador escolhe a celula.
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
        return keyboard != null && keyboard.rKey.wasPressedThisFrame;
    }
}

public static class CrateGameplayBootstrap
{
    private static ulong installedSceneHandle = ulong.MaxValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    // Bootstrap idempotente para instalar o sistema junto ao InventorySystem.
    private static void RegisterSceneCallback()
    {
        installedSceneHandle = ulong.MaxValue;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallSystemsForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallSystemsForScene(scene);
    }

    private static void InstallSystemsForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        ulong sceneHandle = scene.handle.GetRawData();
        if (sceneHandle == installedSceneHandle)
            return;

        installedSceneHandle = sceneHandle;
        InstallSystems();
    }

    private static void InstallSystems()
    {
        InventorySystem inventorySystem = UnityEngine.Object.FindAnyObjectByType<InventorySystem>();
        if (inventorySystem != null && !inventorySystem.TryGetComponent(out WorldCratePlacementSystem _))
            inventorySystem.gameObject.AddComponent<WorldCratePlacementSystem>();

        CrateStorageUI.GetOrCreate();
    }
}
