using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UIInventoryBar : MonoBehaviour
{
    // Referencias do HUD e do inventario.
    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private RectTransform backgroundRoot;
    [SerializeField] private RectTransform slotsRoot;
    [SerializeField] private RectTransform upButtonRoot;
    [SerializeField] private RectTransform downButtonRoot;
    [SerializeField] private InventorySlotVisual slotPrefab;

    // Sprites usados pela barra rapida.
    [Header("Sprites")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite slotSprite;
    [SerializeField] private Sprite selectedSlotSprite;
    [SerializeField] private Sprite upButtonSprite;
    [SerializeField] private Sprite upButtonPressedSprite;
    [SerializeField] private Sprite downButtonSprite;
    [SerializeField] private Sprite downButtonPressedSprite;

    // Ajustes de comportamento da barra.
    [Header("Behaviour")]
    [SerializeField, Min(1)] private int fallbackVisibleSlotCount = 10;
    [SerializeField, Range(0f, 1f)] private float disabledButtonAlpha = 0.4f;
    [SerializeField] private float buttonFeedbackDuration = 0.08f;

    // Estado interno da barra e componentes auxiliares.
    private readonly List<InventorySlotVisual> slotVisuals = new();
    private readonly List<InventorySlotData> inventory = new();

    private Button upButton;
    private Button downButton;
    private Image backgroundImage;
    private Image upButtonImage;
    private Image downButtonImage;

    private int currentRow;
    private int selectedSlotIndex = -1;
    private bool isBoundToInventory;
    private Keyboard keyboard;
    private Coroutine upButtonFeedbackRoutine;
    private Coroutine downButtonFeedbackRoutine;

    // Leitura derivada usada para descobrir quantos slots mostrar por linha.
    private int VisibleSlotCount => inventorySystem != null
        ? Mathf.Max(1, inventorySystem.SlotsPerRow)
        : Mathf.Max(1, fallbackVisibleSlotCount);

    // Ciclo de vida.
    private void Awake()
    {
        keyboard = Keyboard.current;
        ResolveReferences();
        ConfigureVisuals();
        ConfigureButtons();
        RebuildSlotsIfNeeded(forceRebuild: true);
    }

    private void OnEnable()
    {
        BindInventorySystem();
        SyncFromInventorySystem();
    }

    private void Start()
    {
        SyncFromInventorySystem();
    }

    private void OnDisable()
    {
        UnbindInventorySystem();
    }

    private void Update()
    {
        keyboard ??= Keyboard.current;

        if (keyboard == null || GetMaxRowIndex() <= 0)
            return;

        if (keyboard.upArrowKey.wasPressedThisFrame)
            PreviousRow(triggerFeedback: true);

        if (keyboard.downArrowKey.wasPressedThisFrame)
            NextRow(triggerFeedback: true);
    }

    // API publica para alimentar a barra e navegar entre linhas.
    public void SetInventory(IReadOnlyList<InventorySlotData> items)
    {
        inventory.Clear();

        if (items != null)
            inventory.AddRange(items);

        RebuildSlotsIfNeeded();
        RefreshVisibleRow();
    }

    public void NextRow(bool triggerFeedback = false)
    {
        int nextRow = currentRow + 1;
        if (!CanShowRow(nextRow))
            return;

        currentRow = nextRow;
        RefreshVisibleRow();

        if (triggerFeedback)
            PlayButtonFeedback(upwards: false);
    }

    public void PreviousRow(bool triggerFeedback = false)
    {
        int previousRow = currentRow - 1;
        if (!CanShowRow(previousRow))
            return;

        currentRow = previousRow;
        RefreshVisibleRow();

        if (triggerFeedback)
            PlayButtonFeedback(upwards: true);
    }

    // Sincronizacao com o InventorySystem.
    private void HandleInventoryChanged(IReadOnlyList<InventorySlotData> slots)
    {
        SetInventory(slots);
    }

    private void HandleSelectionChanged(int slotIndex, InventorySlotData slotData)
    {
        selectedSlotIndex = slotIndex;

        if (selectedSlotIndex >= 0)
            currentRow = Mathf.Clamp(selectedSlotIndex / VisibleSlotCount, 0, Mathf.Max(0, GetMaxRowIndex()));

        RefreshVisibleRow();
    }

    private void SyncFromInventorySystem()
    {
        ResolveInventorySystem();

        if (inventorySystem == null)
            return;

        SetInventory(inventorySystem.Slots);
        HandleSelectionChanged(inventorySystem.SelectedSlotIndex, inventorySystem.SelectedSlot);
    }

    private void BindInventorySystem()
    {
        if (isBoundToInventory)
            return;

        ResolveInventorySystem();

        if (inventorySystem == null)
            return;

        inventorySystem.InventoryChanged += HandleInventoryChanged;
        inventorySystem.SelectionChanged += HandleSelectionChanged;
        isBoundToInventory = true;
    }

    private void UnbindInventorySystem()
    {
        if (!isBoundToInventory || inventorySystem == null)
            return;

        inventorySystem.InventoryChanged -= HandleInventoryChanged;
        inventorySystem.SelectionChanged -= HandleSelectionChanged;
        isBoundToInventory = false;
    }

    // Montagem e configuracao visual do HUD.
    private void AutoAssignReferences()
    {
        ResolveInventorySystem();

        backgroundRoot ??= FindChildRect("Background");
        slotsRoot ??= FindChildRect("SlotsContainer");
        upButtonRoot ??= FindChildRect("Button_Up");
        downButtonRoot ??= FindChildRect("Button_Down");
    }

    private void ResolveReferences()
    {
        AutoAssignReferences();

        if (backgroundRoot != null)
            backgroundImage = GetOrAddComponent<Image>(backgroundRoot.gameObject);

        if (upButtonRoot != null)
        {
            upButtonImage = GetOrAddComponent<Image>(upButtonRoot.gameObject);
            upButton = GetOrAddComponent<Button>(upButtonRoot.gameObject);
        }

        if (downButtonRoot != null)
        {
            downButtonImage = GetOrAddComponent<Image>(downButtonRoot.gameObject);
            downButton = GetOrAddComponent<Button>(downButtonRoot.gameObject);
        }
    }

    private RectTransform FindChildRect(string childName)
    {
        Transform child = transform.Find(childName);
        return child as RectTransform;
    }

    private void ConfigureVisuals()
    {
        if (backgroundImage != null)
        {
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.raycastTarget = false;
        }

        ConfigureButtonGraphic(upButton, upButtonImage, upButtonSprite);
        ConfigureButtonGraphic(downButton, downButtonImage, downButtonSprite);
    }

    private void ConfigureButtonGraphic(Button button, Image image, Sprite normalSprite)
    {
        if (button == null || image == null)
            return;

        image.sprite = normalSprite;
        image.preserveAspect = true;
        image.raycastTarget = true;

        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;

        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
    }

    private void ConfigureButtons()
    {
        if (upButton != null)
        {
            upButton.onClick.RemoveListener(HandleUpButtonClicked);
            upButton.onClick.AddListener(HandleUpButtonClicked);
        }

        if (downButton != null)
        {
            downButton.onClick.RemoveListener(HandleDownButtonClicked);
            downButton.onClick.AddListener(HandleDownButtonClicked);
        }
    }

    private void HandleUpButtonClicked()
    {
        PreviousRow(triggerFeedback: true);
    }

    private void HandleDownButtonClicked()
    {
        NextRow(triggerFeedback: true);
    }

    private void RebuildSlotsIfNeeded(bool forceRebuild = false)
    {
        if (slotsRoot == null || slotPrefab == null)
        {
            if (slotPrefab == null)
                Debug.LogWarning("UIInventoryBar precisa de um SlotPrefab para montar os 10 slots da HUD.");

            return;
        }

        int requiredSlotCount = VisibleSlotCount;
        bool hasSameVisualCount = slotVisuals.Count == requiredSlotCount;

        if (!forceRebuild && hasSameVisualCount)
            return;

        ClearSlots();

        for (int i = 0; i < requiredSlotCount; i++)
        {
            InventorySlotVisual slotVisual = Instantiate(slotPrefab, slotsRoot);
            slotVisual.name = $"HUDSlot_{i + 1:00}";
            slotVisual.ConfigureBackground(slotSprite, selectedSlotSprite);
            slotVisual.SetSelected(false);
            slotVisual.SetEmpty();
            int visibleIndex = i;
            slotVisual.Clicked += () => HandleSlotClicked(visibleIndex);

            slotVisuals.Add(slotVisual);
        }

        RefreshVisibleRow();
    }

    private void ClearSlots()
    {
        slotVisuals.Clear();

        for (int i = slotsRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = slotsRoot.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    // Atualizacao visual dos slots e botoes.
    private void RefreshVisibleRow()
    {
        if (slotVisuals.Count == 0)
            return;

        currentRow = Mathf.Clamp(currentRow, 0, Mathf.Max(0, GetMaxRowIndex()));

        int visibleSlotCount = slotVisuals.Count;
        int startIndex = currentRow * visibleSlotCount;

        for (int i = 0; i < visibleSlotCount; i++)
        {
            int inventoryIndex = startIndex + i;
            bool isSelected = inventoryIndex == selectedSlotIndex;

            if (inventoryIndex < inventory.Count && !inventory[inventoryIndex].IsEmpty)
                slotVisuals[i].Refresh(inventory[inventoryIndex]);
            else
                slotVisuals[i].SetEmpty();

            slotVisuals[i].SetSelected(isSelected);
        }

        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        bool canGoUp = CanShowRow(currentRow - 1);
        bool canGoDown = CanShowRow(currentRow + 1);

        ApplyButtonState(upButton, upButtonImage, upButtonSprite, canGoUp);
        ApplyButtonState(downButton, downButtonImage, downButtonSprite, canGoDown);
    }

    private void ApplyButtonState(Button button, Image image, Sprite normalSprite, bool isInteractable)
    {
        if (button != null)
            button.interactable = isInteractable;

        if (image == null)
            return;

        image.sprite = normalSprite;
        image.color = new Color(1f, 1f, 1f, isInteractable ? 1f : disabledButtonAlpha);
    }

    // Navegacao e feedback da barra.
    private bool CanShowRow(int rowIndex)
    {
        if (rowIndex < 0)
            return false;

        return rowIndex * VisibleSlotCount < inventory.Count;
    }

    private int GetMaxRowIndex()
    {
        if (inventory.Count == 0)
            return 0;

        return Mathf.CeilToInt(inventory.Count / (float)VisibleSlotCount) - 1;
    }

    private void HandleSlotClicked(int visibleIndex)
    {
        ResolveInventorySystem();

        if (inventorySystem == null)
            return;

        int inventoryIndex = currentRow * VisibleSlotCount + visibleIndex;
        inventorySystem.SelectSlot(inventoryIndex);
    }

    private void PlayButtonFeedback(bool upwards)
    {
        Image image = upwards ? upButtonImage : downButtonImage;
        Sprite normalSprite = upwards ? upButtonSprite : downButtonSprite;
        Sprite pressedSprite = upwards ? upButtonPressedSprite : downButtonPressedSprite;

        if (image == null || pressedSprite == null)
            return;

        ref Coroutine routine = ref upwards ? ref upButtonFeedbackRoutine : ref downButtonFeedbackRoutine;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(ButtonFeedbackRoutine(image, normalSprite, pressedSprite));
    }

    private IEnumerator ButtonFeedbackRoutine(Image image, Sprite normalSprite, Sprite pressedSprite)
    {
        image.sprite = pressedSprite;
        yield return new WaitForSecondsRealtime(buttonFeedbackDuration);

        bool isUpButton = image == upButtonImage;
        bool isInteractable = isUpButton ? CanShowRow(currentRow - 1) : CanShowRow(currentRow + 1);

        image.sprite = normalSprite;
        image.color = new Color(1f, 1f, 1f, isInteractable ? 1f : disabledButtonAlpha);
    }

    // Utilitarios genericos.
    private void ResolveInventorySystem()
    {
        if (inventorySystem == null)
            inventorySystem = FindFirstObjectByType<InventorySystem>();
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        if (!target.TryGetComponent(out T component))
            component = target.AddComponent<T>();

        return component;
    }
}
