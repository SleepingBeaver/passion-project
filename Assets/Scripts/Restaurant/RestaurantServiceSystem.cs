using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class RestaurantServiceSystem : MonoBehaviour
{
    private static readonly string[] CustomerNames =
    {
        "Clara", "Theo", "Maya", "Bento", "Lina", "Noah"
    };

    [Header("References")]
    [SerializeField] private WorldInfoSystem worldInfoSystem;
    [SerializeField] private GameplayDayCycleController dayCycleController;
    [SerializeField] private GameContentCatalog contentCatalog;

    [Header("Service")]
    [SerializeField, Range(0, 23)] private int openingHour = 8;
    [SerializeField, Range(1, 24)] private int closingHour = 22;
    [SerializeField, Min(0f)] private float initialOrderDelaySeconds = 3f;
    [SerializeField, Min(0f)] private float interOrderDelaySeconds = 8f;
    [SerializeField, Min(5f)] private float orderDurationSeconds = 60f;

    private DishDefinition activeDish;
    private string activeCustomerName;
    private float remainingOrderSeconds;
    private float orderCooldownSeconds;
    private int nextOrderSequence;
    private int reputation;
    private int completedOrdersToday;
    private int failedOrdersToday;
    private int totalCompletedOrders;
    private int totalFailedOrders;
    private string statusMessage = "O restaurante abre às 08:00.";
    private bool stateWasRestored;

    public event Action StateChanged;
    public event Action<DishDefinition> OrderStarted;
    public event Action<DishDefinition> OrderCompleted;
    public event Action<DishDefinition> OrderFailed;

    public bool HasActiveOrder => activeDish != null;
    public bool IsRestaurantOpen => IsWithinOpeningHours(worldInfoSystem != null ? worldInfoSystem.CurrentHour24 : 0);
    public DishDefinition ActiveDish => activeDish;
    public string ActiveCustomerName => activeCustomerName ?? string.Empty;
    public float RemainingOrderSeconds => Mathf.Max(0f, remainingOrderSeconds);
    public float OrderCooldownSeconds => Mathf.Max(0f, orderCooldownSeconds);
    public int Reputation => reputation;
    public int CompletedOrdersToday => completedOrdersToday;
    public int FailedOrdersToday => failedOrdersToday;
    public int TotalCompletedOrders => totalCompletedOrders;
    public int TotalFailedOrders => totalFailedOrders;
    public int NextOrderSequence => nextOrderSequence;
    public string StatusMessage => statusMessage;

    private void Awake()
    {
        ResolveReferences();
        SanitizeConfiguration();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (dayCycleController != null)
            dayCycleController.NewDayStarted += HandleNewDayStarted;
    }

    private void Start()
    {
        if (!stateWasRestored)
            ResetForNewGame();
    }

    private void OnDisable()
    {
        if (dayCycleController != null)
            dayCycleController.NewDayStarted -= HandleNewDayStarted;
    }

    private void OnValidate()
    {
        SanitizeConfiguration();
    }

    private void Update()
    {
        if (activeDish != null)
        {
            remainingOrderSeconds -= Time.deltaTime;
            if (remainingOrderSeconds <= 0f)
                FailActiveOrder();

            return;
        }

        if (!IsRestaurantOpen)
            return;

        if (orderCooldownSeconds > 0f)
        {
            orderCooldownSeconds = Mathf.Max(0f, orderCooldownSeconds - Time.deltaTime);
            return;
        }

        TryStartNextOrder();
    }

    public void Initialize(
        GameContentCatalog catalog,
        WorldInfoSystem worldInfo,
        GameplayDayCycleController dayCycle)
    {
        contentCatalog = catalog != null ? catalog : contentCatalog;
        worldInfoSystem = worldInfo != null ? worldInfo : worldInfoSystem;

        if (dayCycleController != dayCycle)
        {
            if (isActiveAndEnabled && dayCycleController != null)
                dayCycleController.NewDayStarted -= HandleNewDayStarted;

            dayCycleController = dayCycle;

            if (isActiveAndEnabled && dayCycleController != null)
                dayCycleController.NewDayStarted += HandleNewDayStarted;
        }

        ResolveReferences();
    }

    public void ResetForNewGame()
    {
        stateWasRestored = false;
        activeDish = null;
        activeCustomerName = string.Empty;
        remainingOrderSeconds = 0f;
        orderCooldownSeconds = initialOrderDelaySeconds;
        nextOrderSequence = 0;
        reputation = 0;
        completedOrdersToday = 0;
        failedOrdersToday = 0;
        totalCompletedOrders = 0;
        totalFailedOrders = 0;
        statusMessage = IsRestaurantOpen
            ? "Aguardando o primeiro cliente."
            : $"O restaurante abre às {openingHour:00}:00.";
        StateChanged?.Invoke();
    }

    public bool TryStartNextOrder()
    {
        ResolveReferences();
        IReadOnlyList<DishDefinition> dishes = contentCatalog != null
            ? contentCatalog.Dishes
            : Array.Empty<DishDefinition>();

        if (activeDish != null || !IsRestaurantOpen || dishes.Count == 0)
            return false;

        for (int offset = 0; offset < dishes.Count; offset++)
        {
            int index = (nextOrderSequence + offset) % dishes.Count;
            DishDefinition candidate = dishes[index];
            if (candidate == null || !candidate.IsConfigured)
                continue;

            return ForceStartOrder(candidate, ResolveCustomerName(nextOrderSequence), orderDurationSeconds);
        }

        statusMessage = "Nenhum prato válido está configurado para atendimento.";
        orderCooldownSeconds = interOrderDelaySeconds;
        StateChanged?.Invoke();
        return false;
    }

    public bool ForceStartOrder(DishDefinition dish, string customerName, float durationSeconds)
    {
        if (dish == null || !dish.IsConfigured || activeDish != null)
            return false;

        activeDish = dish;
        activeCustomerName = !string.IsNullOrWhiteSpace(customerName)
            ? customerName.Trim()
            : ResolveCustomerName(nextOrderSequence);
        remainingOrderSeconds = Mathf.Max(1f, durationSeconds);
        orderCooldownSeconds = 0f;
        nextOrderSequence++;
        statusMessage = $"{activeCustomerName} pediu {activeDish.DisplayName}.";
        OrderStarted?.Invoke(activeDish);
        StateChanged?.Invoke();
        return true;
    }

    public bool TryServeActiveOrder(InventorySystem inventorySystem)
    {
        if (activeDish == null)
        {
            statusMessage = "Não há pedido aguardando entrega.";
            StateChanged?.Invoke();
            return false;
        }

        ItemData preparedItem = activeDish.PreparedItem;
        if (inventorySystem == null || preparedItem == null || !inventorySystem.HasItem(preparedItem))
        {
            statusMessage = $"Prepare {activeDish.DisplayName} antes de servir.";
            StateChanged?.Invoke();
            return false;
        }

        if (!inventorySystem.RemoveItem(preparedItem, 1))
        {
            statusMessage = "Não foi possível retirar o prato do inventário.";
            StateChanged?.Invoke();
            return false;
        }

        DishDefinition completedDish = activeDish;
        string customer = activeCustomerName;
        worldInfoSystem?.AddMoney(completedDish.SellPrice);
        reputation = Mathf.Max(0, reputation + completedDish.ReputationReward);
        completedOrdersToday++;
        totalCompletedOrders++;
        activeDish = null;
        activeCustomerName = string.Empty;
        remainingOrderSeconds = 0f;
        orderCooldownSeconds = interOrderDelaySeconds;
        statusMessage = $"Pedido de {customer} entregue: +{completedDish.SellPrice} moedas, +{completedDish.ReputationReward} reputação.";
        OrderCompleted?.Invoke(completedDish);
        StateChanged?.Invoke();
        return true;
    }

    public void RestoreState(
        DishDefinition restoredDish,
        string customerName,
        float restoredRemainingSeconds,
        float restoredCooldownSeconds,
        int restoredNextOrderSequence,
        int restoredReputation,
        int restoredCompletedToday,
        int restoredFailedToday,
        int restoredTotalCompleted,
        int restoredTotalFailed)
    {
        stateWasRestored = true;
        activeDish = restoredDish != null && restoredRemainingSeconds > 0f ? restoredDish : null;
        activeCustomerName = activeDish != null ? customerName : string.Empty;
        remainingOrderSeconds = activeDish != null ? Mathf.Max(1f, restoredRemainingSeconds) : 0f;
        orderCooldownSeconds = activeDish == null ? Mathf.Max(0f, restoredCooldownSeconds) : 0f;
        nextOrderSequence = Mathf.Max(0, restoredNextOrderSequence);
        reputation = Mathf.Max(0, restoredReputation);
        completedOrdersToday = Mathf.Max(0, restoredCompletedToday);
        failedOrdersToday = Mathf.Max(0, restoredFailedToday);
        totalCompletedOrders = Mathf.Max(completedOrdersToday, restoredTotalCompleted);
        totalFailedOrders = Mathf.Max(failedOrdersToday, restoredTotalFailed);
        statusMessage = activeDish != null
            ? $"{activeCustomerName} aguarda {activeDish.DisplayName}."
            : IsRestaurantOpen ? "Aguardando o próximo cliente." : "Restaurante fechado.";
        StateChanged?.Invoke();
    }

    private void FailActiveOrder()
    {
        DishDefinition failedDish = activeDish;
        string customer = activeCustomerName;
        activeDish = null;
        activeCustomerName = string.Empty;
        remainingOrderSeconds = 0f;
        orderCooldownSeconds = interOrderDelaySeconds;
        failedOrdersToday++;
        totalFailedOrders++;
        statusMessage = $"O pedido de {customer} expirou.";
        OrderFailed?.Invoke(failedDish);
        StateChanged?.Invoke();
    }

    private void HandleNewDayStarted(GameplayDayCycleController.DayTransition _)
    {
        activeDish = null;
        activeCustomerName = string.Empty;
        remainingOrderSeconds = 0f;
        orderCooldownSeconds = initialOrderDelaySeconds;
        completedOrdersToday = 0;
        failedOrdersToday = 0;
        statusMessage = IsRestaurantOpen
            ? "Um novo dia de atendimento começou."
            : $"O restaurante abre às {openingHour:00}:00.";
        StateChanged?.Invoke();
    }

    private bool IsWithinOpeningHours(int hour24)
    {
        int hour = Mathf.Clamp(hour24, 0, 23);
        int sanitizedClosingHour = Mathf.Clamp(closingHour, 1, 24);
        return hour >= openingHour && hour < sanitizedClosingHour;
    }

    private static string ResolveCustomerName(int sequence)
    {
        int index = Mathf.Abs(sequence) % CustomerNames.Length;
        return CustomerNames[index];
    }

    private void ResolveReferences()
    {
        contentCatalog ??= GameContentCatalog.LoadDefault();
        worldInfoSystem ??= GetComponent<WorldInfoSystem>();
        worldInfoSystem ??= FindAnyObjectByType<WorldInfoSystem>();
        dayCycleController ??= GetComponent<GameplayDayCycleController>();
        dayCycleController ??= FindAnyObjectByType<GameplayDayCycleController>();
    }

    private void SanitizeConfiguration()
    {
        openingHour = Mathf.Clamp(openingHour, 0, 23);
        closingHour = Mathf.Clamp(closingHour, openingHour + 1, 24);
        initialOrderDelaySeconds = Mathf.Max(0f, initialOrderDelaySeconds);
        interOrderDelaySeconds = Mathf.Max(0f, interOrderDelaySeconds);
        orderDurationSeconds = Mathf.Max(5f, orderDurationSeconds);
    }
}
