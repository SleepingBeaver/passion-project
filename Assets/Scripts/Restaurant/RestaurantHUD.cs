using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RestaurantHUD : MonoBehaviour
{
    private const string PanelName = "RestaurantServiceHUD";

    [SerializeField] private RestaurantServiceSystem serviceSystem;
    [SerializeField] private KitchenStationInteractable kitchenStation;
    [SerializeField] private Canvas targetCanvas;

    private readonly StringBuilder textBuilder = new(384);
    private RectTransform panelRect;
    private Image panelImage;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;
    private float nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
        EnsureLayout();
        Refresh();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        EnsureLayout();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 0.2f;
        Refresh();
    }

    public void Initialize(
        RestaurantServiceSystem service,
        KitchenStationInteractable kitchen,
        Canvas canvas = null)
    {
        Unsubscribe();
        serviceSystem = service != null ? service : serviceSystem;
        kitchenStation = kitchen != null ? kitchen : kitchenStation;
        targetCanvas = canvas != null ? canvas : targetCanvas;
        ResolveReferences();
        EnsureLayout();
        Subscribe();
        Refresh();
    }

    private void Refresh()
    {
        if (serviceSystem == null || bodyText == null || titleText == null)
            return;

        titleText.text = serviceSystem.HasActiveOrder
            ? "PEDIDO ATIVO"
            : serviceSystem.IsRestaurantOpen ? "RESTAURANTE ABERTO" : "RESTAURANTE FECHADO";

        if (serviceSystem.HasActiveOrder && serviceSystem.RemainingOrderSeconds <= 15f)
            titleText.color = new Color(1f, 0.48f, 0.35f, 1f);
        else
            titleText.color = new Color(1f, 0.83f, 0.45f, 1f);

        textBuilder.Clear();

        if (serviceSystem.HasActiveOrder)
        {
            DishDefinition dish = serviceSystem.ActiveDish;
            textBuilder.Append("Cliente: ").Append(serviceSystem.ActiveCustomerName).AppendLine();
            textBuilder.Append("Pedido: ").Append(dish != null ? dish.DisplayName : "Prato").AppendLine();
            textBuilder.Append("Prazo: ").Append(Mathf.CeilToInt(serviceSystem.RemainingOrderSeconds)).Append("s");

            if (dish != null)
                textBuilder.Append("  •  Recompensa: ").Append(dish.SellPrice);

            textBuilder.AppendLine();
            AppendIngredients(dish);
        }
        else
        {
            textBuilder.AppendLine(serviceSystem.StatusMessage);
        }

        textBuilder.Append("Cozinha: ");
        if (kitchenStation == null)
        {
            textBuilder.AppendLine("indisponível");
        }
        else if (kitchenStation.ReadyServings > 0)
        {
            textBuilder.AppendLine("prato pronto para recolher");
        }
        else if (kitchenStation.IsCooking)
        {
            textBuilder.Append("preparando (")
                .Append(Mathf.CeilToInt(kitchenStation.RemainingCookingSeconds))
                .AppendLine("s)");
        }
        else
        {
            textBuilder.Append(kitchenStation.StatusMessage)
                .AppendLine(" (estação laranja próxima ao início)");
        }

        textBuilder.AppendLine("Entrega: balcão verde próximo à cozinha");
        textBuilder.Append("Reputação: ").Append(serviceSystem.Reputation)
            .Append("  •  Hoje: ").Append(serviceSystem.CompletedOrdersToday)
            .Append(" entregues / ").Append(serviceSystem.FailedOrdersToday).Append(" perdidos");
        bodyText.text = textBuilder.ToString();
    }

    private void AppendIngredients(DishDefinition dish)
    {
        textBuilder.Append("Ingredientes: ");

        if (dish == null || dish.Ingredients.Length == 0)
        {
            textBuilder.AppendLine("não configurados");
            return;
        }

        DishIngredientRequirement[] ingredients = dish.Ingredients;
        for (int i = 0; i < ingredients.Length; i++)
        {
            if (i > 0)
                textBuilder.Append(", ");

            DishIngredientRequirement ingredient = ingredients[i];
            textBuilder.Append(ingredient.Amount).Append("x ")
                .Append(ingredient.Item != null ? ingredient.Item.itemName : "item");
        }

        textBuilder.AppendLine();
    }

    private void ResolveReferences()
    {
        serviceSystem ??= FindAnyObjectByType<RestaurantServiceSystem>();
        kitchenStation ??= FindAnyObjectByType<KitchenStationInteractable>();
        targetCanvas ??= ResolveScreenCanvas();
    }

    private static Canvas ResolveScreenCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        Canvas best = null;
        bool bestIsActive = false;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate == null || candidate.renderMode == RenderMode.WorldSpace)
                continue;

            bool candidateIsActive = candidate.gameObject.activeInHierarchy;
            if (best == null ||
                (candidateIsActive && !bestIsActive) ||
                (candidateIsActive == bestIsActive && candidate.sortingOrder > best.sortingOrder))
            {
                best = candidate;
                bestIsActive = candidateIsActive;
            }
        }

        if (best != null)
            return best;

        GameObject canvasObject = new(
            "RestaurantRuntimeCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return canvas;
    }

    private void EnsureLayout()
    {
        if (targetCanvas == null)
            return;

        Transform existing = targetCanvas.transform.Find(PanelName);
        GameObject panelObject = existing != null
            ? existing.gameObject
            : new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer));
        panelObject.transform.SetParent(targetCanvas.transform, false);

        panelRect = GetOrAdd<RectTransform>(panelObject);
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(24f, -24f);
        panelRect.sizeDelta = new Vector2(430f, 225f);

        panelImage = GetOrAdd<Image>(panelObject);
        panelImage.color = new Color(0.08f, 0.11f, 0.13f, 0.9f);
        panelImage.raycastTarget = false;

        titleText = EnsureText(panelRect, "Title", 25f, FontStyles.Bold);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -10f);
        titleRect.sizeDelta = new Vector2(-24f, 34f);
        titleText.alignment = TextAlignmentOptions.Center;

        bodyText = EnsureText(panelRect, "Body", 17f, FontStyles.Normal);
        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(18f, 14f);
        bodyRect.offsetMax = new Vector2(-18f, -48f);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.color = new Color(0.95f, 0.97f, 0.94f, 1f);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.raycastTarget = false;
    }

    private static TextMeshProUGUI EnsureText(RectTransform parent, string name, float fontSize, FontStyles style)
    {
        Transform existing = parent.Find(name);
        GameObject textObject = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(textObject);
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.raycastTarget = false;
        return text;
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled)
            return;

        if (serviceSystem != null)
        {
            serviceSystem.StateChanged -= Refresh;
            serviceSystem.StateChanged += Refresh;
        }

        if (kitchenStation != null)
        {
            kitchenStation.StateChanged -= Refresh;
            kitchenStation.StateChanged += Refresh;
        }
    }

    private void Unsubscribe()
    {
        if (serviceSystem != null)
            serviceSystem.StateChanged -= Refresh;

        if (kitchenStation != null)
            kitchenStation.StateChanged -= Refresh;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T component) ? component : target.AddComponent<T>();
    }
}
