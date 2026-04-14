using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class CrateStorageUI : MonoBehaviour
{
    private const string OverlayRootName = "CrateStorageOverlay";
    private const string PanelName = "Panel";
    private const string TitlePath = PanelName + "/Title";
    private const string PlayerTitlePath = PanelName + "/PlayerTitle";
    private const string CrateTitlePath = PanelName + "/CrateTitle";
    private const string HintPath = PanelName + "/Hint";
    private const string PlayerSlotsPath = PanelName + "/PlayerSection/PlayerSlots";
    private const string CrateSlotsPath = PanelName + "/CrateSection/CrateSlots";
    private const string DefaultHintText = "Arraste entre os paineis ou use Shift+Clique para transferir.";
    private const string RuntimeCanvasName = "CrateStorageCanvas";

    private enum StorageArea
    {
        Player = 0,
        Crate = 1
    }

    private readonly struct SlotBinding
    {
        public SlotBinding(StorageArea area, int index)
        {
            Area = area;
            Index = index;
        }

        public StorageArea Area { get; }
        public int Index { get; }
    }

    [Header("References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private InventorySlotVisual slotPrefab;

    private readonly List<InventorySlotVisual> playerSlotVisuals = new();
    private readonly List<InventorySlotVisual> crateSlotVisuals = new();
    private readonly Dictionary<InventorySlotVisual, SlotBinding> slotBindings = new();

    private GameObject overlayRoot;
    private RectTransform panelRect;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI hintText;
    private TextMeshProUGUI playerSectionTitleText;
    private TextMeshProUGUI crateSectionTitleText;
    private RectTransform playerSlotsRoot;
    private RectTransform crateSlotsRoot;
    private GridLayoutGroup playerGrid;
    private GridLayoutGroup crateGrid;
    private RectTransform dragPreviewRect;
    private Image dragPreviewImage;
    private CanvasGroup dragPreviewCanvasGroup;

    private CrateStorageInteractable activeCrate;
    private PlayerInteractor activeInteractor;
    private IsoPlayerController2D activeMovement;
    private SimpleInventoryUI simpleInventoryUI;
    private Keyboard keyboard;

    private bool wasInteractorEnabled;
    private bool wasMovementEnabled;
    private bool wasSimpleInventoryUIEnabled;
    private bool pausedByStorage;
    private float previousTimeScale = 1f;

    private static Canvas runtimeFallbackCanvas;

    public static CrateStorageUI Instance { get; private set; }
    public bool IsOpen => overlayRoot != null && overlayRoot.activeSelf && activeCrate != null;

    private void Awake()
    {
        Instance = this;
        keyboard = Keyboard.current;
        ResolveReferences();
        EnsureRuntimeUI();
        SetVisible(false);
    }

    private void OnEnable()
    {
        Instance = this;
        keyboard = Keyboard.current;
        ResolveReferences();
        EnsureRuntimeUI();
    }

    private void OnDisable()
    {
        ClearDragPreview();

        if (pausedByStorage || IsOpen)
            Close();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!IsOpen)
            return;

        keyboard ??= Keyboard.current;

        if (activeCrate == null)
        {
            Close();
            return;
        }

        if (keyboard != null &&
            (keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame))
        {
            Close();
        }
    }

    public static CrateStorageUI GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        CrateStorageUI existingStorageUI = FindExistingStorageUI();
        if (existingStorageUI != null)
            return existingStorageUI;

        Canvas canvas = FindBestCanvas();
        if (canvas == null)
            return null;

        if (!canvas.TryGetComponent(out CrateStorageUI storageUI))
            storageUI = canvas.gameObject.AddComponent<CrateStorageUI>();

        return storageUI;
    }

    public static CrateStorageUI FindExistingStorageUI()
    {
        CrateStorageUI[] storageUIs = Resources.FindObjectsOfTypeAll<CrateStorageUI>();

        for (int i = 0; i < storageUIs.Length; i++)
        {
            CrateStorageUI storageUI = storageUIs[i];
            if (storageUI != null && storageUI.gameObject.scene.IsValid())
                return storageUI;
        }

        return null;
    }

    public static Canvas FindBestCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                return canvas;
        }

        Canvas[] hiddenCanvases = Resources.FindObjectsOfTypeAll<Canvas>();

        for (int i = 0; i < hiddenCanvases.Length; i++)
        {
            Canvas canvas = hiddenCanvases[i];
            if (canvas != null &&
                canvas.isRootCanvas &&
                canvas.renderMode != RenderMode.WorldSpace &&
                canvas.gameObject.scene.IsValid())
                return canvas;
        }

        return CreateRuntimeCanvas();
    }

    public bool Open(CrateStorageInteractable crate, PlayerInteractor interactor)
    {
        if (crate == null || interactor == null)
            return false;

        inventorySystem = interactor.InventorySystem;
        ResolveReferences();

        if (inventorySystem == null || !ResolveSlotPrefab())
        {
            Debug.LogWarning("CrateStorageUI nao encontrou o slot prefab do inventario para abrir o armazenamento.");
            return false;
        }

        EnsureRuntimeUI();
        Bind(crate, interactor);
        SetVisible(true);
        SetHint(DefaultHintText);
        PauseGameplay(true);
        RefreshAll();
        return true;
    }

    public void Close()
    {
        SetVisible(false);
        ClearDragPreview();
        PauseGameplay(false);
        Unbind();
        SetHint(string.Empty);
    }

    private void Bind(CrateStorageInteractable crate, PlayerInteractor interactor)
    {
        Unbind();

        activeCrate = crate;
        activeInteractor = interactor;
        activeMovement = interactor != null ? interactor.GetComponent<IsoPlayerController2D>() : null;

        if (inventorySystem != null)
            inventorySystem.InventoryChanged += HandleInventoryChanged;

        if (activeCrate != null)
            activeCrate.StorageChanged += HandleCrateChanged;
    }

    private void Unbind()
    {
        if (inventorySystem != null)
            inventorySystem.InventoryChanged -= HandleInventoryChanged;

        if (activeCrate != null)
            activeCrate.StorageChanged -= HandleCrateChanged;

        activeCrate = null;
        activeInteractor = null;
        activeMovement = null;
        simpleInventoryUI = null;
    }

    private void HandleInventoryChanged(IReadOnlyList<InventorySlotData> slots)
    {
        RefreshPlayerSlots();
    }

    private void HandleCrateChanged()
    {
        RefreshCrateSlots();
        RefreshTitles();
    }

    private void ResolveReferences()
    {
        targetCanvas ??= GetComponent<Canvas>();
        targetCanvas ??= GetComponentInParent<Canvas>();
        targetCanvas ??= FindBestCanvas();
        targetCanvas = targetCanvas != null ? targetCanvas.rootCanvas : null;
        inventorySystem ??= FindFirstObjectByType<InventorySystem>();
    }

    private bool ResolveSlotPrefab()
    {
        if (slotPrefab != null)
            return true;

        if (inventorySystem != null && inventorySystem.SlotPrefab != null)
        {
            slotPrefab = inventorySystem.SlotPrefab;
            return true;
        }

        InventoryUIController inventoryUI = FindFirstObjectByType<InventoryUIController>();
        if (inventoryUI != null)
        {
            slotPrefab = inventoryUI.SlotPrefab;
            return slotPrefab != null;
        }

        InventoryUIController[] hiddenInventoryUIs = Resources.FindObjectsOfTypeAll<InventoryUIController>();
        for (int i = 0; i < hiddenInventoryUIs.Length; i++)
        {
            if (hiddenInventoryUIs[i] != null && hiddenInventoryUIs[i].gameObject.scene.IsValid() && hiddenInventoryUIs[i].SlotPrefab != null)
            {
                slotPrefab = hiddenInventoryUIs[i].SlotPrefab;
                return true;
            }
        }

        return false;
    }

    private void PauseGameplay(bool shouldPause)
    {
        if (shouldPause)
        {
            if (pausedByStorage)
                return;

            simpleInventoryUI = FindFirstObjectByType<SimpleInventoryUI>();

            if (simpleInventoryUI != null)
            {
                wasSimpleInventoryUIEnabled = simpleInventoryUI.enabled;
                simpleInventoryUI.enabled = false;
            }

            if (activeInteractor != null)
            {
                wasInteractorEnabled = activeInteractor.enabled;
                activeInteractor.enabled = false;
            }

            if (activeMovement != null)
            {
                wasMovementEnabled = activeMovement.enabled;
                activeMovement.enabled = false;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            pausedByStorage = true;
            return;
        }

        if (!pausedByStorage)
            return;

        Time.timeScale = previousTimeScale;
        pausedByStorage = false;

        if (activeMovement != null)
            activeMovement.enabled = wasMovementEnabled;

        if (activeInteractor != null)
            activeInteractor.enabled = wasInteractorEnabled;

        if (simpleInventoryUI != null)
            simpleInventoryUI.enabled = wasSimpleInventoryUIEnabled;
    }

    private void EnsureRuntimeUI()
    {
        if (targetCanvas == null || HasResolvedLayout())
            return;

        if (TryResolveSceneLayout())
            return;

        overlayRoot = CreateUIObject(OverlayRootName, targetCanvas.transform).gameObject;
        RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        Image overlayImage = overlayRoot.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.58f);
        overlayImage.raycastTarget = true;

        panelRect = CreateUIObject("Panel", overlayRoot.transform);
        panelRect.SetParent(overlayRoot.transform, false);
        ConfigureRect(panelRect, Vector2.zero, new Vector2(1450f, 790f));

        Image panelImage = panelRect.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.925f, 0.882f, 0.792f, 1f);
        panelImage.raycastTarget = true;

        titleText = CreateText(panelRect, "Title", new Vector2(0f, 344f), new Vector2(720f, 48f), 34f);
        playerSectionTitleText = CreateText(panelRect, "PlayerTitle", new Vector2(-345f, 266f), new Vector2(480f, 36f), 24f);
        crateSectionTitleText = CreateText(panelRect, "CrateTitle", new Vector2(375f, 266f), new Vector2(320f, 36f), 24f);
        hintText = CreateText(panelRect, "Hint", new Vector2(0f, -353f), new Vector2(1120f, 32f), 18f);

        RectTransform playerSection = CreateSection(panelRect, "PlayerSection", new Vector2(-285f, -8f), new Vector2(800f, 520f));
        RectTransform crateSection = CreateSection(panelRect, "CrateSection", new Vector2(390f, -8f), new Vector2(430f, 520f));

        playerSlotsRoot = CreateUIObject("PlayerSlots", playerSection);
        ConfigureRect(playerSlotsRoot, new Vector2(0f, -8f), new Vector2(740f, 410f));
        playerGrid = ConfigureGrid(playerSlotsRoot, 10);

        crateSlotsRoot = CreateUIObject("CrateSlots", crateSection);
        ConfigureRect(crateSlotsRoot, new Vector2(0f, -20f), new Vector2(340f, 280f));
        crateGrid = ConfigureGrid(crateSlotsRoot, 4);

        SetVisible(false);
    }

    private bool HasResolvedLayout()
    {
        return overlayRoot != null &&
               panelRect != null &&
               titleText != null &&
               hintText != null &&
               playerSectionTitleText != null &&
               crateSectionTitleText != null &&
               playerSlotsRoot != null &&
               crateSlotsRoot != null &&
               playerGrid != null &&
               crateGrid != null;
    }

    private bool TryResolveSceneLayout()
    {
        if (targetCanvas == null)
            return false;

        Transform overlayTransform = targetCanvas.transform.Find(OverlayRootName);
        if (overlayTransform == null)
            return false;

        RectTransform resolvedPanel = overlayTransform.Find(PanelName) as RectTransform;
        TextMeshProUGUI resolvedTitle = FindText(overlayTransform, TitlePath);
        TextMeshProUGUI resolvedHint = FindText(overlayTransform, HintPath);
        TextMeshProUGUI resolvedPlayerTitle = FindText(overlayTransform, PlayerTitlePath);
        TextMeshProUGUI resolvedCrateTitle = FindText(overlayTransform, CrateTitlePath);
        RectTransform resolvedPlayerSlots = overlayTransform.Find(PlayerSlotsPath) as RectTransform;
        RectTransform resolvedCrateSlots = overlayTransform.Find(CrateSlotsPath) as RectTransform;
        GridLayoutGroup resolvedPlayerGrid = resolvedPlayerSlots != null ? resolvedPlayerSlots.GetComponent<GridLayoutGroup>() : null;
        GridLayoutGroup resolvedCrateGrid = resolvedCrateSlots != null ? resolvedCrateSlots.GetComponent<GridLayoutGroup>() : null;

        if (resolvedPanel == null ||
            resolvedTitle == null ||
            resolvedHint == null ||
            resolvedPlayerTitle == null ||
            resolvedCrateTitle == null ||
            resolvedPlayerSlots == null ||
            resolvedCrateSlots == null ||
            resolvedPlayerGrid == null ||
            resolvedCrateGrid == null)
        {
            return false;
        }

        overlayRoot = overlayTransform.gameObject;
        panelRect = resolvedPanel;
        titleText = resolvedTitle;
        hintText = resolvedHint;
        playerSectionTitleText = resolvedPlayerTitle;
        crateSectionTitleText = resolvedCrateTitle;
        playerSlotsRoot = resolvedPlayerSlots;
        crateSlotsRoot = resolvedCrateSlots;
        playerGrid = resolvedPlayerGrid;
        crateGrid = resolvedCrateGrid;
        return true;
    }

    private static Canvas CreateRuntimeCanvas()
    {
        if (runtimeFallbackCanvas != null)
            return runtimeFallbackCanvas;

        GameObject canvasObject = new(RuntimeCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystemObject = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystemObject);
        }

        runtimeFallbackCanvas = canvas;
        DontDestroyOnLoad(canvasObject);
        return canvas;
    }

    private void RefreshAll()
    {
        RefreshTitles();
        BuildPlayerSlotsIfNeeded();
        BuildCrateSlotsIfNeeded();
        RefreshPlayerSlots();
        RefreshCrateSlots();
    }

    private void RefreshTitles()
    {
        string crateName = activeCrate != null ? activeCrate.DisplayName : "Caixote";

        if (titleText != null)
            titleText.text = crateName;

        if (playerSectionTitleText != null)
            playerSectionTitleText.text = "Inventario";

        if (crateSectionTitleText != null)
            crateSectionTitleText.text = crateName;
    }

    private void BuildPlayerSlotsIfNeeded()
    {
        if (inventorySystem == null || playerSlotsRoot == null || slotPrefab == null)
            return;

        int requiredCount = inventorySystem.SlotCount;
        if (playerSlotVisuals.Count == requiredCount)
        {
            playerGrid.constraintCount = Mathf.Max(1, inventorySystem.SlotsPerRow);
            return;
        }

        ClearSlotVisuals(playerSlotVisuals, playerSlotsRoot);
        playerGrid.constraintCount = Mathf.Max(1, inventorySystem.SlotsPerRow);

        for (int i = 0; i < requiredCount; i++)
        {
            InventorySlotVisual slot = Instantiate(slotPrefab, playerSlotsRoot);
            slot.name = $"PlayerStorageSlot_{i + 1:00}";
            slot.SetSelected(false);
            slot.SetEmpty();
            int slotIndex = i;
            slot.Clicked += () => HandlePlayerSlotClicked(slotIndex);
            slot.DragStarted += () => HandlePlayerSlotDragStarted(slotIndex);
            slot.DragMoved += HandleSlotDragged;
            slot.DragEnded += HandleSlotDragEnded;
            slot.Dropped += draggedSlot => HandleSlotDropped(draggedSlot, StorageArea.Player, slotIndex);
            playerSlotVisuals.Add(slot);
            slotBindings[slot] = new SlotBinding(StorageArea.Player, slotIndex);
        }
    }

    private void BuildCrateSlotsIfNeeded()
    {
        if (activeCrate == null || crateSlotsRoot == null || slotPrefab == null)
            return;

        int requiredCount = activeCrate.SlotCount;
        if (crateSlotVisuals.Count == requiredCount)
        {
            crateGrid.constraintCount = Mathf.Max(1, activeCrate.SlotsPerRow);
            return;
        }

        ClearSlotVisuals(crateSlotVisuals, crateSlotsRoot);
        crateGrid.constraintCount = Mathf.Max(1, activeCrate.SlotsPerRow);

        for (int i = 0; i < requiredCount; i++)
        {
            InventorySlotVisual slot = Instantiate(slotPrefab, crateSlotsRoot);
            slot.name = $"CrateStorageSlot_{i + 1:00}";
            slot.SetSelected(false);
            slot.SetEmpty();
            int slotIndex = i;
            slot.Clicked += () => HandleCrateSlotClicked(slotIndex);
            slot.DragStarted += () => HandleCrateSlotDragStarted(slotIndex);
            slot.DragMoved += HandleSlotDragged;
            slot.DragEnded += HandleSlotDragEnded;
            slot.Dropped += draggedSlot => HandleSlotDropped(draggedSlot, StorageArea.Crate, slotIndex);
            crateSlotVisuals.Add(slot);
            slotBindings[slot] = new SlotBinding(StorageArea.Crate, slotIndex);
        }
    }

    private void RefreshPlayerSlots()
    {
        if (inventorySystem == null)
            return;

        for (int i = 0; i < playerSlotVisuals.Count; i++)
        {
            InventorySlotVisual slotVisual = playerSlotVisuals[i];

            if (inventorySystem.TryGetSlot(i, out InventorySlotData slotData) && slotData != null && !slotData.IsEmpty)
                slotVisual.Refresh(slotData);
            else
                slotVisual.SetEmpty();

            slotVisual.SetSelected(false);
        }
    }

    private void RefreshCrateSlots()
    {
        if (activeCrate == null)
            return;

        for (int i = 0; i < crateSlotVisuals.Count; i++)
        {
            InventorySlotVisual slotVisual = crateSlotVisuals[i];

            if (activeCrate.TryGetSlot(i, out InventorySlotData slotData) && slotData != null && !slotData.IsEmpty)
                slotVisual.Refresh(slotData);
            else
                slotVisual.SetEmpty();

            slotVisual.SetSelected(false);
        }
    }

    private void HandlePlayerSlotClicked(int slotIndex)
    {
        if (!IsQuickTransferModifierPressed())
            return;

        TryTransferPlayerSlotToCrate(slotIndex);
    }

    private void HandleCrateSlotClicked(int slotIndex)
    {
        if (!IsQuickTransferModifierPressed())
            return;

        TryTransferCrateSlotToInventory(slotIndex);
    }

    private void SetVisible(bool visible)
    {
        if (overlayRoot != null && overlayRoot.activeSelf != visible)
            overlayRoot.SetActive(visible);
    }

    private void SetHint(string text)
    {
        if (hintText != null)
            hintText.text = text;
    }

    private static void ClearSlotVisuals(List<InventorySlotVisual> slotVisuals, RectTransform root)
    {
        slotVisuals.Clear();

        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }

    private static RectTransform CreateSection(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        RectTransform sectionRect = CreateUIObject(name, parent);
        ConfigureRect(sectionRect, anchoredPosition, size);

        Image sectionImage = sectionRect.gameObject.AddComponent<Image>();
        sectionImage.color = new Color(0.862f, 0.792f, 0.682f, 0.92f);
        sectionImage.raycastTarget = false;

        return sectionRect;
    }

    private static GridLayoutGroup ConfigureGrid(RectTransform root, int constraintCount)
    {
        GridLayoutGroup grid = root.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(64f, 64f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(10, 10, 10, 10);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, constraintCount);
        return grid;
    }

    private static RectTransform CreateUIObject(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform), typeof(CanvasRenderer));
        RectTransform rectTransform = child.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        return rectTransform;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        RectTransform textRect = CreateUIObject(name, parent);
        ConfigureRect(textRect, anchoredPosition, size);

        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSharedMaterial = TMP_Settings.defaultFontAsset.material;
        }

        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.24f, 0.17f, 0.12f, 1f);
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        text.text = string.Empty;
        return text;
    }

    private static TextMeshProUGUI FindText(Transform root, string path)
    {
        Transform textTransform = root.Find(path);
        return textTransform != null ? textTransform.GetComponent<TextMeshProUGUI>() : null;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
    }

    private static void ConfigureRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private void HandlePlayerSlotDragStarted(int slotIndex)
    {
        HandleSlotDragStarted(StorageArea.Player, slotIndex);
    }

    private void HandleCrateSlotDragStarted(int slotIndex)
    {
        HandleSlotDragStarted(StorageArea.Crate, slotIndex);
    }

    private void HandleSlotDragStarted(StorageArea area, int slotIndex)
    {
        InventorySlotVisual slotVisual = GetSlotVisual(area, slotIndex);
        if (slotVisual == null || !slotVisual.HasItem)
            return;

        ShowDragPreview(slotVisual);
    }

    private void HandleSlotDragged(Vector2 screenPosition)
    {
        UpdateDragPreviewPosition(screenPosition);
    }

    private void HandleSlotDragEnded()
    {
        ClearDragPreview();
    }

    private void HandleSlotDropped(InventorySlotVisual draggedSlot, StorageArea targetArea, int targetIndex)
    {
        if (draggedSlot == null || !slotBindings.TryGetValue(draggedSlot, out SlotBinding sourceBinding))
            return;

        if (sourceBinding.Area == targetArea)
        {
            if (targetArea == StorageArea.Player)
            {
                inventorySystem?.MoveOrSwapSlots(sourceBinding.Index, targetIndex);
            }
            else
            {
                activeCrate?.MoveOrSwapSlots(sourceBinding.Index, targetIndex);
            }

            return;
        }

        if (sourceBinding.Area == StorageArea.Player)
            TryTransferPlayerSlotToCrate(sourceBinding.Index, showSuccessMessage: false);
        else
            TryTransferCrateSlotToInventory(sourceBinding.Index, showSuccessMessage: false);
    }

    private bool TryTransferPlayerSlotToCrate(int slotIndex, bool showSuccessMessage = true)
    {
        if (inventorySystem == null || activeCrate == null)
            return false;

        if (!inventorySystem.TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null || slotData.IsEmpty)
            return false;

        ItemData itemData = slotData.item;
        int amount = slotData.amount;

        if (!activeCrate.AddItem(itemData, amount))
        {
            SetHint("Caixote cheio.");
            return false;
        }

        if (!inventorySystem.RemoveFromSlot(slotIndex, amount, out ItemData removedItem, out int removedAmount) ||
            removedItem != itemData ||
            removedAmount != amount)
        {
            activeCrate.RemoveItem(itemData, amount);
            SetHint("Nao foi possivel guardar a pilha.");
            return false;
        }

        if (showSuccessMessage)
            SetHint($"Guardado: {removedAmount}x {itemData.itemName}");

        return true;
    }

    private bool TryTransferCrateSlotToInventory(int slotIndex, bool showSuccessMessage = true)
    {
        if (inventorySystem == null || activeCrate == null)
            return false;

        if (!activeCrate.TryGetSlot(slotIndex, out InventorySlotData slotData) || slotData == null || slotData.IsEmpty)
            return false;

        ItemData itemData = slotData.item;
        int amount = slotData.amount;

        if (!inventorySystem.AddItem(itemData, amount))
        {
            SetHint("Inventario cheio.");
            return false;
        }

        if (!activeCrate.RemoveFromSlot(slotIndex, amount, out ItemData removedItem, out int removedAmount) ||
            removedItem != itemData ||
            removedAmount != amount)
        {
            inventorySystem.RemoveItem(itemData, amount);
            SetHint("Nao foi possivel retirar a pilha.");
            return false;
        }

        if (showSuccessMessage)
            SetHint($"Retirado: {removedAmount}x {itemData.itemName}");

        return true;
    }

    private bool IsQuickTransferModifierPressed()
    {
        keyboard ??= Keyboard.current;

        return keyboard != null &&
               ((keyboard.leftShiftKey != null && keyboard.leftShiftKey.isPressed) ||
                (keyboard.rightShiftKey != null && keyboard.rightShiftKey.isPressed));
    }

    private InventorySlotVisual GetSlotVisual(StorageArea area, int slotIndex)
    {
        List<InventorySlotVisual> sourceList = area == StorageArea.Player ? playerSlotVisuals : crateSlotVisuals;

        return slotIndex >= 0 && slotIndex < sourceList.Count
            ? sourceList[slotIndex]
            : null;
    }

    private void ShowDragPreview(InventorySlotVisual sourceSlot)
    {
        if (targetCanvas == null || sourceSlot == null || !sourceSlot.HasItem || sourceSlot.DisplayedIcon == null)
            return;

        EnsureDragPreview();

        dragPreviewImage.sprite = sourceSlot.DisplayedIcon;
        dragPreviewImage.enabled = true;
        dragPreviewCanvasGroup.alpha = 0.9f;

        Vector2 previewSize = sourceSlot.DragPreviewSize;
        if (previewSize == Vector2.zero)
            previewSize = new Vector2(48f, 48f);

        dragPreviewRect.sizeDelta = previewSize;
        dragPreviewRect.SetAsLastSibling();
    }

    private void EnsureDragPreview()
    {
        if (dragPreviewImage != null)
            return;

        GameObject previewObject = new("CrateDraggedItemPreview", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        previewObject.transform.SetParent(targetCanvas.transform, false);

        dragPreviewRect = previewObject.GetComponent<RectTransform>();
        dragPreviewImage = previewObject.GetComponent<Image>();
        dragPreviewCanvasGroup = previewObject.GetComponent<CanvasGroup>();

        dragPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
        dragPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
        dragPreviewRect.pivot = new Vector2(0.5f, 0.5f);

        dragPreviewCanvasGroup.blocksRaycasts = false;
        dragPreviewCanvasGroup.interactable = false;
        dragPreviewImage.raycastTarget = false;
        dragPreviewImage.preserveAspect = true;
        dragPreviewImage.enabled = false;
    }

    private void UpdateDragPreviewPosition(Vector2 screenPosition)
    {
        if (targetCanvas == null || dragPreviewRect == null || dragPreviewImage == null || !dragPreviewImage.enabled)
            return;

        RectTransform canvasRect = targetCanvas.transform as RectTransform;
        Camera eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : targetCanvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            return;

        dragPreviewRect.anchoredPosition = localPoint + new Vector2(18f, -18f);
    }

    private void ClearDragPreview()
    {
        if (dragPreviewImage == null)
            return;

        dragPreviewImage.enabled = false;
        dragPreviewImage.sprite = null;
    }
}
