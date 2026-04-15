using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class CraftingUI : MonoBehaviour
{
    private const string OverlayRootName = "CraftingOverlay";
    private const string PanelName = "Panel";
    private const string TitleName = "Title";
    private const string HintName = "Hint";
    private const string FooterName = "Footer";
    private const string ScrollViewName = "RecipeScrollView";
    private const string ViewportName = "Viewport";
    private const string ContentName = "Content";
    private const string RecipeSlotTemplateName = "RecipeSlotTemplate";
    private const string RecipeSlotIconFrameName = "IconFrame";
    private const string RecipeSlotIconName = "Icon";
    private const string RecipeSlotAmountName = "Amount";
    private const string DetailsPanelName = "DetailsPanel";
    private const string SelectedItemFrameName = "SelectedItemFrame";
    private const string SelectedItemIconName = "SelectedItemIcon";
    private const string SelectedItemNameName = "SelectedItemName";
    private const string SelectedItemDescriptionName = "SelectedItemDescription";
    private const string ResourcesTitleName = "ResourcesTitle";
    private const string ResourcesListName = "ResourcesList";
    private const string LegacyCraftButtonName = "CraftButton";
    private const string DefaultHintText = "Selecione um item. Clique novamente no slot selecionado para criar.";
    private const string DefaultFooterText = "C ou Esc para fechar";
    private const string RuntimeCanvasName = "CraftingRuntimeCanvas";

    private sealed class RecipeSlotBinding
    {
        public CraftingRecipeDefinition Recipe;
        public RectTransform Root;
        public Button SlotButton;
        public Image SlotBackground;
        public Image IconFrame;
        public Image OutputIcon;
        public TextMeshProUGUI AmountText;
        public Sprite CachedOutputSprite;
    }

    [Header("References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private CraftingSystem craftingSystem;
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private PlayerInteractor playerInteractor;

    private readonly List<RecipeSlotBinding> recipeSlots = new();

    private GameObject overlayRoot;
    private RectTransform panelRect;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI hintText;
    private TextMeshProUGUI footerText;
    private RectTransform recipeScrollRectTransform;
    private ScrollRect recipeScrollRect;
    private RectTransform recipeViewportRect;
    private RectTransform recipeContentRoot;
    private RectTransform recipeSlotTemplate;
    private RectTransform detailsPanelRect;
    private Image detailsPanelImage;
    private RectTransform selectedItemFrameRect;
    private Image selectedItemFrameImage;
    private Image selectedItemIcon;
    private TextMeshProUGUI selectedItemNameText;
    private TextMeshProUGUI selectedItemDescriptionText;
    private TextMeshProUGUI selectedItemResourcesTitleText;
    private TextMeshProUGUI selectedItemResourcesText;
    private CraftingRecipeDefinition selectedRecipe;

    private InventorySystem boundInventorySystem;
    private CraftingSystem boundCraftingSystem;
    private SimpleInventoryUI simpleInventoryUI;
    private IsoPlayerController2D playerMovement;
    private Keyboard keyboard;

    private bool pausedByCrafting;
    private bool usingSimpleInventoryModal;
    private bool wasInteractorEnabled;
    private bool wasMovementEnabled;
    private bool suppressInventoryRefresh;
    private bool refreshRequestedWhileSuppressed;
    private bool toggleInputArmed;
    private float previousTimeScale = 1f;

    private static Canvas runtimeFallbackCanvas;

    public static CraftingUI Instance { get; private set; }
    public bool IsOpen => overlayRoot != null && overlayRoot.activeSelf;

    private void Awake()
    {
        Instance = this;
        keyboard = Keyboard.current;
        toggleInputArmed = !Application.isPlaying;
        ResolveReferences();
        EnsureRuntimeUI();
        TryBindSources();
        SetVisible(false);
    }

    private void OnEnable()
    {
        Instance = this;
        keyboard = Keyboard.current;
        toggleInputArmed = !Application.isPlaying;
        ResolveReferences();
        EnsureRuntimeUI();
        TryBindSources();

        if (Application.isPlaying)
            SetVisible(false);

        if (!Application.isPlaying)
            RefreshAll();
    }

    private void OnDisable()
    {
        if (pausedByCrafting || IsOpen)
            Close();

        UnbindSources();
    }

    private void OnValidate()
    {
        keyboard = Keyboard.current;
        ResolveReferences();
        EnsureRuntimeUI();

        if (!Application.isPlaying)
            RefreshAll();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        keyboard ??= Keyboard.current;

        ResolveReferences();
        TryBindSources();

        if (keyboard == null)
            return;

        if (!toggleInputArmed)
        {
            if (keyboard.cKey.isPressed || keyboard.escapeKey.isPressed)
                return;

            toggleInputArmed = true;
        }

        if (IsOpen)
        {
            if (keyboard.escapeKey.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame)
                Close();

            return;
        }

        if (keyboard.cKey.wasPressedThisFrame)
            Open();
    }

    public static CraftingUI GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        CraftingUI existingUI = FindExistingCraftingUI();
        if (existingUI != null)
            return existingUI;

        Canvas canvas = FindBestCanvas();
        if (canvas == null)
            return null;

        if (!canvas.TryGetComponent(out CraftingUI craftingUI))
            craftingUI = canvas.gameObject.AddComponent<CraftingUI>();

        return craftingUI;
    }

    public static CraftingUI FindExistingCraftingUI()
    {
        CraftingUI[] craftingUIs = Resources.FindObjectsOfTypeAll<CraftingUI>();

        for (int i = 0; i < craftingUIs.Length; i++)
        {
            CraftingUI craftingUI = craftingUIs[i];
            if (craftingUI != null && craftingUI.gameObject.scene.IsValid())
                return craftingUI;
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
            {
                return canvas;
            }
        }

        return CreateRuntimeCanvas();
    }

    public bool Open()
    {
        ResolveReferences();
        EnsureRuntimeUI();
        TryBindSources();

        if (!CanOpen())
            return false;

        RefreshAll();

        SetHint(DefaultHintText);
        PauseGameplay(true);
        SetVisible(true);
        return true;
    }

    public void Close()
    {
        SetVisible(false);
        PauseGameplay(false);
        SetHint(string.Empty);
    }

    private bool CanOpen()
    {
        if (craftingSystem == null || inventorySystem == null || overlayRoot == null)
            return false;

        simpleInventoryUI ??= FindFirstObjectByType<SimpleInventoryUI>();

        if (simpleInventoryUI != null &&
            (simpleInventoryUI.IsOpen || simpleInventoryUI.IsTransitioning || simpleInventoryUI.IsExternalModalOpen))
        {
            return false;
        }

        CrateStorageUI storageUI = CrateStorageUI.FindExistingStorageUI();
        if (storageUI != null && storageUI.IsOpen)
            return false;

        return true;
    }

    private void ResolveReferences()
    {
        targetCanvas ??= GetComponent<Canvas>();
        targetCanvas ??= GetComponentInParent<Canvas>();
        targetCanvas ??= FindBestCanvas();
        targetCanvas = targetCanvas != null ? targetCanvas.rootCanvas : null;

        craftingSystem ??= FindFirstObjectByType<CraftingSystem>();
        inventorySystem ??= craftingSystem != null ? craftingSystem.GetComponent<InventorySystem>() : null;
        playerInteractor ??= FindFirstObjectByType<PlayerInteractor>();

        if (inventorySystem == null && playerInteractor != null)
            inventorySystem = playerInteractor.InventorySystem;

        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();

        if (playerInteractor != null)
            playerMovement = playerInteractor.GetComponent<IsoPlayerController2D>();

        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<IsoPlayerController2D>();
    }

    private void TryBindSources()
    {
        if (boundInventorySystem != inventorySystem)
        {
            if (boundInventorySystem != null)
                boundInventorySystem.InventoryChanged -= HandleInventoryChanged;

            boundInventorySystem = inventorySystem;

            if (boundInventorySystem != null)
                boundInventorySystem.InventoryChanged += HandleInventoryChanged;
        }

        if (boundCraftingSystem != craftingSystem)
        {
            if (boundCraftingSystem != null)
                boundCraftingSystem.RecipesChanged -= HandleRecipesChanged;

            boundCraftingSystem = craftingSystem;

            if (boundCraftingSystem != null)
                boundCraftingSystem.RecipesChanged += HandleRecipesChanged;
        }
    }

    private void UnbindSources()
    {
        if (boundInventorySystem != null)
            boundInventorySystem.InventoryChanged -= HandleInventoryChanged;

        if (boundCraftingSystem != null)
            boundCraftingSystem.RecipesChanged -= HandleRecipesChanged;

        boundInventorySystem = null;
        boundCraftingSystem = null;
    }

    private void HandleInventoryChanged(IReadOnlyList<InventorySlotData> slots)
    {
        if (suppressInventoryRefresh)
        {
            refreshRequestedWhileSuppressed = true;
            return;
        }

        if (Application.isPlaying && !IsOpen)
            return;

        ResolveReferences();
        EnsureRuntimeUI();
        RefreshAll();
    }

    private void HandleRecipesChanged()
    {
        if (Application.isPlaying && !IsOpen)
            return;

        ResolveReferences();
        EnsureRuntimeUI();
        RefreshAll();
    }

    private void PauseGameplay(bool shouldPause)
    {
        if (shouldPause)
        {
            if (pausedByCrafting)
                return;

            simpleInventoryUI = FindFirstObjectByType<SimpleInventoryUI>();
            usingSimpleInventoryModal = simpleInventoryUI != null && simpleInventoryUI.TryOpenExternalModal();

            if (playerInteractor != null)
            {
                wasInteractorEnabled = playerInteractor.enabled;
                playerInteractor.enabled = false;
            }

            if (!usingSimpleInventoryModal && playerMovement != null)
            {
                wasMovementEnabled = playerMovement.enabled;
                playerMovement.enabled = false;
            }

            if (!usingSimpleInventoryModal)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            pausedByCrafting = true;
            return;
        }

        if (!pausedByCrafting)
            return;

        pausedByCrafting = false;

        if (usingSimpleInventoryModal)
        {
            if (simpleInventoryUI != null)
                simpleInventoryUI.CloseExternalModal();
        }
        else
        {
            Time.timeScale = previousTimeScale;

            if (playerMovement != null)
                playerMovement.enabled = wasMovementEnabled;
        }

        if (playerInteractor != null)
            playerInteractor.enabled = wasInteractorEnabled;

        usingSimpleInventoryModal = false;
    }

    private void EnsureRuntimeUI()
    {
        if (targetCanvas == null)
            return;

        bool wasVisible = overlayRoot != null && overlayRoot.activeSelf;

        RectTransform overlayRect = EnsureOverlayRoot();
        panelRect = EnsurePanel(overlayRect);
        RemoveLegacyChild(panelRect, LegacyCraftButtonName);
        titleText = EnsureTitle(panelRect);
        hintText = EnsureHint(panelRect);
        footerText = EnsureFooter(panelRect);
        recipeScrollRectTransform = EnsureRecipeScrollView(panelRect, out recipeScrollRect);
        recipeViewportRect = EnsureViewport(recipeScrollRectTransform, out RectMask2D _, out Mask _);
        recipeContentRoot = EnsureContent(recipeViewportRect);
        recipeSlotTemplate = EnsureRecipeSlotTemplate(recipeContentRoot);
        detailsPanelRect = EnsureDetailsPanel(panelRect, out detailsPanelImage);
        selectedItemFrameRect = EnsureSelectedItemFrame(detailsPanelRect, out selectedItemFrameImage);
        selectedItemIcon = EnsureSelectedItemIcon(selectedItemFrameRect);
        selectedItemNameText = EnsureSelectedItemName(detailsPanelRect);
        selectedItemDescriptionText = EnsureSelectedItemDescription(detailsPanelRect);
        selectedItemResourcesTitleText = EnsureResourcesTitle(detailsPanelRect);
        selectedItemResourcesText = EnsureResourcesList(detailsPanelRect);

        if (recipeScrollRect != null)
        {
            recipeScrollRect.viewport = recipeViewportRect;
            recipeScrollRect.content = recipeContentRoot;
        }

        if (Application.isPlaying && overlayRoot != null && overlayRoot.activeSelf != wasVisible)
            overlayRoot.SetActive(wasVisible);

    }

    private RectTransform EnsureOverlayRoot()
    {
        bool created;
        RectTransform overlayRect = FindOrCreateChildRect(targetCanvas.transform, OverlayRootName, out created);
        overlayRoot = overlayRect.gameObject;

        if (created)
            StretchToParent(overlayRect);

        Image overlayImage = GetOrAddComponent<Image>(overlayRoot, out bool imageAdded);
        if (created || imageAdded)
        {
            overlayImage.color = new Color(0f, 0f, 0f, 0.58f);
            overlayImage.raycastTarget = true;
        }

        return overlayRect;
    }

    private RectTransform EnsurePanel(RectTransform overlayRect)
    {
        bool created;
        RectTransform panel = FindOrCreateChildRect(overlayRect, PanelName, out created);

        if (created)
            ConfigureCenteredRect(panel, Vector2.zero, new Vector2(1180f, 700f));

        Image panelImage = GetOrAddComponent<Image>(panel.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            panelImage.color = new Color(0.933f, 0.898f, 0.823f, 1f);
            panelImage.raycastTarget = true;
        }

        return panel;
    }

    private TextMeshProUGUI EnsureTitle(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, TitleName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, 294f), new Vector2(780f, 48f));
            text.fontSize = 34f;
            text.alignment = TextAlignmentOptions.Center;
        }

        if (created || string.IsNullOrWhiteSpace(text.text))
            text.text = "Crafting";

        return text;
    }

    private TextMeshProUGUI EnsureHint(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, HintName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, -306f), new Vector2(1080f, 32f));
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;
        }

        return text;
    }

    private TextMeshProUGUI EnsureFooter(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, FooterName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, -336f), new Vector2(1080f, 26f));
            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.Center;
        }

        if (created || string.IsNullOrWhiteSpace(text.text))
            text.text = DefaultFooterText;

        return text;
    }

    private RectTransform EnsureRecipeScrollView(RectTransform parent, out ScrollRect scrollRect)
    {
        bool created;
        RectTransform scrollView = FindOrCreateChildRect(parent, ScrollViewName, out created);
        if (created)
            ConfigureCenteredRect(scrollView, new Vector2(-250f, -24f), new Vector2(560f, 520f));

        Image scrollImage = GetOrAddComponent<Image>(scrollView.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            scrollImage.color = new Color(0.835f, 0.784f, 0.702f, 0.92f);
            scrollImage.raycastTarget = true;
        }

        scrollRect = GetOrAddComponent<ScrollRect>(scrollView.gameObject, out bool scrollRectAdded);
        if (created || scrollRectAdded)
        {
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 28f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
        }

        return scrollView;
    }

    private RectTransform EnsureViewport(RectTransform parent, out RectMask2D rectMask, out Mask mask)
    {
        bool created;
        RectTransform viewport = FindOrCreateChildRect(parent, ViewportName, out created);
        if (created)
            StretchToParent(viewport, new Vector2(18f, 18f), new Vector2(-18f, -18f));

        Image viewportImage = GetOrAddComponent<Image>(viewport.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
        }

        rectMask = GetOrAddComponent<RectMask2D>(viewport.gameObject, out _);
        mask = GetOrAddComponent<Mask>(viewport.gameObject, out bool maskAdded);
        if (created || maskAdded)
            mask.showMaskGraphic = false;

        return viewport;
    }

    private RectTransform EnsureContent(RectTransform viewport)
    {
        bool created;
        RectTransform content = FindOrCreateChildRect(viewport, ContentName, out created);
        if (created)
        {
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            content.localScale = Vector3.one;
            content.localRotation = Quaternion.identity;
        }

        if (content.TryGetComponent(out VerticalLayoutGroup verticalLayout))
            verticalLayout.enabled = false;

        GridLayoutGroup layoutGroup = GetOrAddComponent<GridLayoutGroup>(content.gameObject, out bool layoutAdded);
        if (created || layoutAdded)
        {
            layoutGroup.padding = new RectOffset(8, 8, 8, 8);
            layoutGroup.spacing = new Vector2(14f, 14f);
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.startAxis = GridLayoutGroup.Axis.Horizontal;
            layoutGroup.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layoutGroup.constraintCount = 4;
            layoutGroup.cellSize = new Vector2(112f, 112f);
        }

        ContentSizeFitter fitter = GetOrAddComponent<ContentSizeFitter>(content.gameObject, out bool fitterAdded);
        if (created || fitterAdded)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        return content;
    }

    private RectTransform EnsureRecipeSlotTemplate(RectTransform parent)
    {
        if (parent == null)
            return null;

        bool created;
        RectTransform template = FindOrCreateChildRect(parent, RecipeSlotTemplateName, out created);
        ConfigureRecipeSlotVisuals(template, applyDefaults: created);
        template.gameObject.SetActive(false);
        return template;
    }

    private RectTransform EnsureDetailsPanel(RectTransform parent, out Image panelImage)
    {
        bool created;
        RectTransform detailsPanel = FindOrCreateChildRect(parent, DetailsPanelName, out created);
        if (created)
            ConfigureCenteredRect(detailsPanel, new Vector2(296f, -24f), new Vector2(370f, 520f));

        panelImage = GetOrAddComponent<Image>(detailsPanel.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            panelImage.color = new Color(0.862f, 0.792f, 0.682f, 0.92f);
            panelImage.raycastTarget = true;
        }

        return detailsPanel;
    }

    private RectTransform EnsureSelectedItemFrame(RectTransform parent, out Image frameImage)
    {
        bool created;
        RectTransform frame = FindOrCreateChildRect(parent, SelectedItemFrameName, out created);
        if (created)
            ConfigureCenteredRect(frame, new Vector2(0f, 176f), new Vector2(132f, 132f));

        frameImage = GetOrAddComponent<Image>(frame.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            frameImage.color = new Color(0.757f, 0.678f, 0.561f, 0.95f);
            frameImage.raycastTarget = false;
        }

        return frame;
    }

    private Image EnsureSelectedItemIcon(RectTransform parent)
    {
        bool created;
        RectTransform iconRect = FindOrCreateChildRect(parent, SelectedItemIconName, out created);
        if (created)
            StretchToParent(iconRect, new Vector2(12f, 12f), new Vector2(-12f, -12f));

        Image iconImage = GetOrAddComponent<Image>(iconRect.gameObject, out bool imageAdded);
        if (created || imageAdded)
        {
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        return iconImage;
    }

    private TextMeshProUGUI EnsureSelectedItemName(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, SelectedItemNameName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, 82f), new Vector2(310f, 40f));
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
        }

        return text;
    }

    private TextMeshProUGUI EnsureSelectedItemDescription(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, SelectedItemDescriptionName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, 4f), new Vector2(310f, 112f));
            text.fontSize = 18f;
            text.fontStyle = FontStyles.Normal;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        return text;
    }

    private TextMeshProUGUI EnsureResourcesTitle(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, ResourcesTitleName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, -86f), new Vector2(310f, 32f));
            text.fontSize = 20f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Left;
        }

        if (created || string.IsNullOrWhiteSpace(text.text))
            text.text = "Recursos necessarios";

        return text;
    }

    private TextMeshProUGUI EnsureResourcesList(RectTransform parent)
    {
        bool created;
        TextMeshProUGUI text = FindOrCreateText(parent, ResourcesListName, out created);
        if (created)
        {
            ConfigureCenteredRect(text.rectTransform, new Vector2(0f, -182f), new Vector2(310f, 150f));
            text.fontSize = 18f;
            text.fontStyle = FontStyles.Normal;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        return text;
    }

    private void RefreshAll()
    {
        if (recipeContentRoot == null)
            return;

        if (craftingSystem == null)
        {
            selectedRecipe = null;
            ClearRecipeSlots();
            RefreshSelectedRecipeDetails();
            return;
        }

        RebuildRecipeSlots();
        EnsureSelectedRecipe();
        RefreshRecipeSlots();
        RefreshSelectedRecipeDetails();
    }

    private void RebuildRecipeSlots()
    {
        if (craftingSystem == null)
        {
            ClearRecipeSlots();
            return;
        }

        IReadOnlyList<CraftingRecipeDefinition> recipes = craftingSystem.Recipes;
        CraftingRecipeDefinition previouslySelectedRecipe = selectedRecipe;

        ClearRecipeSlots();

        for (int i = 0; i < recipes.Count; i++)
            recipeSlots.Add(CreateRecipeSlot(recipes[i], i));

        selectedRecipe = previouslySelectedRecipe;
    }

    private void BuildRecipeSlotsIfNeeded()
    {
        IReadOnlyList<CraftingRecipeDefinition> recipes = craftingSystem.Recipes;

        if (!NeedsRecipeRebuild(recipes))
            return;

        ClearRecipeSlots();

        for (int i = 0; i < recipes.Count; i++)
            recipeSlots.Add(CreateRecipeSlot(recipes[i], i));
    }

    private void EnsureSelectedRecipe()
    {
        if (craftingSystem == null || craftingSystem.Recipes.Count == 0)
        {
            selectedRecipe = null;
            return;
        }

        IReadOnlyList<CraftingRecipeDefinition> recipes = craftingSystem.Recipes;
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i] == selectedRecipe)
                return;
        }

        selectedRecipe = recipes[0];
    }

    private bool NeedsRecipeRebuild(IReadOnlyList<CraftingRecipeDefinition> recipes)
    {
        if (recipeSlots.Count != recipes.Count)
            return true;

        for (int i = 0; i < recipeSlots.Count; i++)
        {
            if (recipeSlots[i] == null || recipeSlots[i].Recipe != recipes[i] || recipeSlots[i].Root == null)
                return true;
        }

        return false;
    }

    private void ClearRecipeSlots()
    {
        if (recipeContentRoot != null)
        {
            for (int i = recipeContentRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = recipeContentRoot.GetChild(i);
                if (recipeSlotTemplate != null && child == recipeSlotTemplate)
                    continue;

                DestroyUIObject(child.gameObject);
            }
        }

        recipeSlots.Clear();
    }

    private RecipeSlotBinding CreateRecipeSlot(CraftingRecipeDefinition recipe, int index)
    {
        RectTransform slotRect;
        if (recipeSlotTemplate != null)
        {
            GameObject slotObject = Instantiate(recipeSlotTemplate.gameObject, recipeContentRoot, false);
            slotObject.name = $"RecipeSlot_{index + 1:00}";
            slotObject.SetActive(true);
            slotRect = slotObject.GetComponent<RectTransform>();
        }
        else
        {
            slotRect = CreateUIObject($"RecipeSlot_{index + 1:00}", recipeContentRoot);
        }

        RecipeSlotBinding binding = ConfigureRecipeSlotVisuals(slotRect, applyDefaults: recipeSlotTemplate == null);
        binding.Recipe = recipe;
        binding.CachedOutputSprite = ResolveRecipeOutputSprite(recipe);

        if (binding.SlotButton != null)
        {
            binding.SlotButton.onClick.RemoveAllListeners();
            binding.SlotButton.onClick.AddListener(() => HandleRecipeSlotClicked(recipe));
        }

        return binding;
    }

    private RecipeSlotBinding ConfigureRecipeSlotVisuals(RectTransform slotRect, bool applyDefaults)
    {
        Image slotBackground = GetOrAddComponent<Image>(slotRect.gameObject, out bool slotBackgroundAdded);
        if (applyDefaults || slotBackgroundAdded)
            slotBackground.color = new Color(0.976f, 0.949f, 0.898f, 1f);

        Button slotButton = GetOrAddComponent<Button>(slotRect.gameObject, out bool slotButtonAdded);
        if (applyDefaults || slotButtonAdded)
            slotButton.transition = Selectable.Transition.None;
        slotButton.targetGraphic = slotBackground;

        bool iconFrameCreated;
        RectTransform iconFrameRect = FindOrCreateChildRect(slotRect, RecipeSlotIconFrameName, out iconFrameCreated);
        if (applyDefaults || iconFrameCreated)
            StretchToParent(iconFrameRect, new Vector2(12f, 12f), new Vector2(-12f, -12f));

        Image iconFrameImage = GetOrAddComponent<Image>(iconFrameRect.gameObject, out bool iconFrameAdded);
        if (applyDefaults || iconFrameAdded)
            iconFrameImage.color = new Color(0.757f, 0.678f, 0.561f, 0.95f);

        bool iconCreated;
        RectTransform iconRect = FindOrCreateChildRect(iconFrameRect, RecipeSlotIconName, out iconCreated);
        if (applyDefaults || iconCreated)
            StretchToParent(iconRect, new Vector2(10f, 10f), new Vector2(-10f, -10f));

        Image iconImage = GetOrAddComponent<Image>(iconRect.gameObject, out bool iconAdded);
        if (applyDefaults || iconAdded)
        {
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        bool amountCreated;
        TextMeshProUGUI amountText = FindOrCreateText(slotRect, RecipeSlotAmountName, out amountCreated);
        if (applyDefaults || amountCreated)
        {
            amountText.rectTransform.anchorMin = new Vector2(1f, 0f);
            amountText.rectTransform.anchorMax = new Vector2(1f, 0f);
            amountText.rectTransform.pivot = new Vector2(1f, 0f);
            amountText.rectTransform.anchoredPosition = new Vector2(-10f, 8f);
            amountText.rectTransform.sizeDelta = new Vector2(48f, 24f);
            amountText.fontSize = 18f;
            amountText.fontStyle = FontStyles.Bold;
            amountText.alignment = TextAlignmentOptions.BottomRight;
        }

        return new RecipeSlotBinding
        {
            Root = slotRect,
            SlotButton = slotButton,
            SlotBackground = slotBackground,
            IconFrame = iconFrameImage,
            OutputIcon = iconImage,
            AmountText = amountText
        };
    }

    private void RefreshRecipeSlots()
    {
        if (craftingSystem == null)
            return;

        for (int i = 0; i < recipeSlots.Count; i++)
            RefreshRecipeSlot(recipeSlots[i]);
    }

    private void RefreshRecipeSlot(RecipeSlotBinding slot)
    {
        if (slot == null || slot.Recipe == null)
            return;

        CraftingRecipeDefinition recipe = slot.Recipe;
        bool isSelected = recipe == selectedRecipe;
        bool craftable = craftingSystem != null && craftingSystem.CanCraft(recipe, out _);
        Sprite outputSprite = ResolveRecipeOutputSprite(recipe, slot.CachedOutputSprite);
        if (outputSprite != null)
            slot.CachedOutputSprite = outputSprite;

        if (slot.OutputIcon != null)
        {
            slot.OutputIcon.sprite = outputSprite;
            slot.OutputIcon.enabled = outputSprite != null;
            slot.OutputIcon.color = craftable ? Color.white : new Color(0.34f, 0.34f, 0.34f, 1f);
        }

        if (slot.AmountText != null)
        {
            slot.AmountText.text = recipe.OutputAmount > 1 ? recipe.OutputAmount.ToString() : string.Empty;
            slot.AmountText.color = craftable
                ? new Color(0.24f, 0.17f, 0.12f, 1f)
                : new Color(0.42f, 0.34f, 0.28f, 1f);
        }

        if (slot.IconFrame != null)
        {
            slot.IconFrame.color = isSelected
                ? new Color(0.88f, 0.764f, 0.49f, 1f)
                : new Color(0.757f, 0.678f, 0.561f, 0.95f);
        }

        if (slot.SlotBackground != null)
        {
            if (isSelected)
            {
                slot.SlotBackground.color = craftable
                    ? new Color(0.949f, 0.882f, 0.733f, 1f)
                    : new Color(0.913f, 0.84f, 0.752f, 1f);
            }
            else
            {
                slot.SlotBackground.color = craftable
                    ? new Color(0.976f, 0.949f, 0.898f, 1f)
                    : new Color(0.88f, 0.834f, 0.79f, 1f);
            }
        }
    }

    private void RefreshSelectedRecipeDetails()
    {
        if (selectedItemNameText == null ||
            selectedItemDescriptionText == null ||
            selectedItemResourcesTitleText == null ||
            selectedItemResourcesText == null ||
            selectedItemIcon == null)
        {
            return;
        }

        if (selectedRecipe == null)
        {
            selectedItemNameText.text = "Nenhuma receita";
            selectedItemDescriptionText.text = "Adicione receitas ao sistema de crafting para visualizar itens aqui.";
            selectedItemResourcesTitleText.text = "Recursos necessarios";
            selectedItemResourcesText.text = "Selecione um item na grade.";
            selectedItemIcon.enabled = false;

            if (selectedItemFrameImage != null)
                selectedItemFrameImage.color = new Color(0.757f, 0.678f, 0.561f, 0.75f);

            if (detailsPanelImage != null)
                detailsPanelImage.color = new Color(0.862f, 0.792f, 0.682f, 0.92f);

            return;
        }

        string reason = string.Empty;
        bool craftable = craftingSystem != null && craftingSystem.CanCraft(selectedRecipe, out reason);
        ItemData outputItem = selectedRecipe.OutputItem;
        Sprite outputSprite = ResolveRecipeOutputSprite(selectedRecipe, FindCachedRecipeSprite(selectedRecipe, selectedItemIcon.sprite));
        string outputName = outputItem != null && !string.IsNullOrWhiteSpace(outputItem.itemName)
            ? outputItem.itemName
            : selectedRecipe.DisplayName;

        selectedItemNameText.text = outputName;
        selectedItemDescriptionText.text = BuildRecipeDescription(selectedRecipe, craftable, reason);
        selectedItemResourcesTitleText.text = "Recursos necessarios";
        selectedItemResourcesText.text = BuildResourcesDetailText(selectedRecipe);

        selectedItemIcon.sprite = outputSprite;
        selectedItemIcon.enabled = outputSprite != null;
        selectedItemIcon.color = craftable ? Color.white : new Color(0.34f, 0.34f, 0.34f, 1f);

        if (selectedItemFrameImage != null)
        {
            selectedItemFrameImage.color = craftable
                ? new Color(0.88f, 0.764f, 0.49f, 1f)
                : new Color(0.757f, 0.678f, 0.561f, 0.95f);
        }

        if (detailsPanelImage != null)
        {
            detailsPanelImage.color = craftable
                ? new Color(0.878f, 0.812f, 0.71f, 0.95f)
                : new Color(0.84f, 0.772f, 0.69f, 0.92f);
        }
    }

    private string BuildRecipeDescription(CraftingRecipeDefinition recipe, bool craftable, string reason)
    {
        string description = !string.IsNullOrWhiteSpace(recipe.Description)
            ? recipe.Description
            : "Combine os materiais necessarios para criar este item.";

        string status = craftable
            ? "Pronto para criar. Clique novamente no slot para confirmar."
            : string.IsNullOrWhiteSpace(reason)
                ? "Ainda faltam materiais para liberar esta criacao."
                : reason;

        return $"{description}\n\n{status}";
    }

    private string BuildResourcesDetailText(CraftingRecipeDefinition recipe)
    {
        if (recipe == null)
            return "Nenhum recurso necessario.";

        CraftingIngredientRequirement[] ingredients = recipe.Ingredients;
        if (ingredients.Length == 0)
            return "Nenhum recurso necessario.";

        StringBuilder builder = new();

        for (int i = 0; i < ingredients.Length; i++)
        {
            CraftingIngredientRequirement ingredient = ingredients[i];
            if (ingredient == null || ingredient.Item == null)
                continue;

            int currentAmount = craftingSystem != null ? craftingSystem.CountItem(ingredient.Item) : 0;
            bool hasEnough = currentAmount >= ingredient.Amount;
            string itemName = !string.IsNullOrWhiteSpace(ingredient.Item.itemName)
                ? ingredient.Item.itemName
                : ingredient.Item.name;
            string color = hasEnough ? "#4C6E35" : "#7A463B";

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append("<color=");
            builder.Append(color);
            builder.Append(">• ");
            builder.Append(itemName);
            builder.Append("</color> ");
            builder.Append(currentAmount);
            builder.Append('/');
            builder.Append(ingredient.Amount);
        }

        return builder.Length > 0 ? builder.ToString() : "Nenhum recurso necessario.";
    }

    private void HandleRecipeSlotClicked(CraftingRecipeDefinition recipe)
    {
        if (recipe == null)
            return;

        if (selectedRecipe != recipe)
        {
            selectedRecipe = recipe;
            RefreshRecipeSlots();
            RefreshSelectedRecipeDetails();
            SetHint(DefaultHintText);
            return;
        }

        AttemptCraftSelectedRecipe();
    }

    private void AttemptCraftSelectedRecipe()
    {
        if (craftingSystem == null || selectedRecipe == null)
        {
            SetHint("Selecione uma receita.");
            return;
        }

        suppressInventoryRefresh = true;
        refreshRequestedWhileSuppressed = false;

        bool craftedSuccessfully;
        string resultMessage;

        try
        {
            craftedSuccessfully = craftingSystem.TryCraft(selectedRecipe, out resultMessage);
        }
        finally
        {
            suppressInventoryRefresh = false;
        }

        if (refreshRequestedWhileSuppressed || IsOpen)
            RefreshAll();

        refreshRequestedWhileSuppressed = false;

        if (craftedSuccessfully)
        {
            SetHint(resultMessage);
            return;
        }

        SetHint(resultMessage);
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

    private Sprite ResolveRecipeOutputSprite(CraftingRecipeDefinition recipe, Sprite fallbackSprite = null)
    {
        if (recipe?.OutputItem != null && recipe.OutputItem.icon != null)
            return recipe.OutputItem.icon;

        return fallbackSprite;
    }

    private Sprite FindCachedRecipeSprite(CraftingRecipeDefinition recipe, Sprite fallbackSprite = null)
    {
        for (int i = 0; i < recipeSlots.Count; i++)
        {
            RecipeSlotBinding slot = recipeSlots[i];
            if (slot != null && slot.Recipe == recipe && slot.CachedOutputSprite != null)
                return slot.CachedOutputSprite;
        }

        return fallbackSprite;
    }

    private void RemoveLegacyChild(RectTransform parent, string childName)
    {
        if (parent == null)
            return;

        Transform legacyChild = parent.Find(childName);
        if (legacyChild != null)
            DestroyUIObject(legacyChild.gameObject);
    }

    private static void DestroyUIObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private static RectTransform FindOrCreateChildRect(Transform parent, string name, out bool created)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            created = false;
            return child as RectTransform;
        }

        created = true;
        return CreateUIObject(name, parent);
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

    private static RectTransform CreateLayoutChild(Transform parent, string name)
    {
        RectTransform rectTransform = CreateUIObject(name, parent);
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        return rectTransform;
    }

    private static TextMeshProUGUI FindOrCreateText(RectTransform parent, string name, out bool created)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            if (!child.TryGetComponent(out TextMeshProUGUI existingText))
            {
                existingText = child.gameObject.AddComponent<TextMeshProUGUI>();
                ApplyTextDefaults(existingText);
                created = true;
                return existingText;
            }

            created = false;
            return existingText;
        }

        created = true;
        RectTransform textRect = CreateUIObject(name, parent);
        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextDefaults(text);
        return text;
    }

    private static TextMeshProUGUI CreateLayoutText(Transform parent, string name, float fontSize, FontStyles fontStyle)
    {
        RectTransform textRect = CreateLayoutChild(parent, name);
        LayoutElement layoutElement = GetOrAddComponent<LayoutElement>(textRect.gameObject);
        layoutElement.preferredHeight = fontSize + 12f;

        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextDefaults(text);
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.text = string.Empty;
        return text;
    }

    private static TextMeshProUGUI CreateCenteredText(RectTransform parent, string name, float fontSize)
    {
        RectTransform textRect = CreateUIObject(name, parent);
        StretchToParent(textRect);

        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTextDefaults(text);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.text = string.Empty;
        return text;
    }

    private static void ApplyTextDefaults(TextMeshProUGUI text)
    {
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSharedMaterial = TMP_Settings.defaultFontAsset.material;
        }

        text.color = new Color(0.24f, 0.17f, 0.12f, 1f);
        text.raycastTarget = false;
        text.enableAutoSizing = false;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        StretchToParent(rectTransform, Vector2.zero, Vector2.zero);
    }

    private static void StretchToParent(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
    }

    private static void ConfigureCenteredRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        return GetOrAddComponent<T>(target, out _);
    }

    private static T GetOrAddComponent<T>(GameObject target, out bool added) where T : Component
    {
        if (!target.TryGetComponent(out T component))
        {
            component = target.AddComponent<T>();
            added = true;
            return component;
        }

        added = false;
        return component;
    }

    private static Canvas CreateRuntimeCanvas()
    {
        if (runtimeFallbackCanvas != null)
            return runtimeFallbackCanvas;

        GameObject canvasObject = new(RuntimeCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 520;
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
}
