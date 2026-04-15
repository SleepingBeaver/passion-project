using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
public class UIInfoPanel : MonoBehaviour
{
    private const int MoneyDigitsCount = 7;

    // Rotulos padrao usados para apresentar os dados do mundo.
    private static readonly string[] SeasonLabels = { "Spring", "Summer", "Autumn", "Winter" };
    private static readonly string[] WeekDayLabels = { "Mon.", "Tue.", "Wed.", "Thu.", "Fri.", "Sat.", "Sun." };
    private static readonly Color DefaultPanelTextColor = new Color(0.11320752f, 0.11320752f, 0.11320752f, 1f);

    // Fonte principal dos dados exibidos no painel.
    [Header("System")]
    [SerializeField] private WorldInfoSystem worldInfoSystem;

    // Sprites base usados para montar o painel.
    [Header("Sprites")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite seasonFrameSprite;
    [SerializeField] private Sprite dayFrameSprite;
    [SerializeField] private Sprite weekDayFrameSprite;
    [SerializeField] private Sprite moneyFrameSprite;
    [SerializeField] private Sprite moneySymbolSprite;
    [SerializeField] private Sprite sunnyWeatherSprite;
    [SerializeField] private Sprite partialWeatherSprite;
    [SerializeField] private Sprite cloudyWeatherSprite;
    [SerializeField] private Sprite windWeatherSprite;
    [SerializeField] private Sprite rainWeatherSprite;
    [SerializeField] private Sprite stormWeatherSprite;

    // Configuracao geral do texto exibido no painel.
    [Header("Text")]
    [SerializeField] private Color generalTextColor = new Color(0.11320752f, 0.11320752f, 0.11320752f, 1f);

    // Referencias geradas ou vinculadas diretamente no canvas.
    [Header("Generated References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image seasonFrameImage;
    [SerializeField] private Image dayFrameImage;
    [SerializeField] private Image weekDayFrameImage;
    [SerializeField] private Image timeFrameImage;
    [SerializeField] private Image weatherImage;
    [SerializeField] private Image moneySymbolImage;
    [SerializeField] private Image[] moneySquareImages = new Image[MoneyDigitsCount];
    [SerializeField] private TextMeshProUGUI seasonText;
    [SerializeField] private TextMeshProUGUI dayText;
    [SerializeField] private TextMeshProUGUI weekDayText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI[] moneyDigitTexts = new TextMeshProUGUI[MoneyDigitsCount];

    // Estado interno de binding e montagem do layout.
    private WorldInfoSystem boundWorldInfoSystem;
    private bool isEnsuringLayout;

    // Ciclo de vida.
    private void Reset()
    {
        RectTransform rectTransform = transform as RectTransform;
        if (rectTransform != null && rectTransform.sizeDelta == Vector2.zero)
            rectTransform.sizeDelta = new Vector2(230f, 112f);

        generalTextColor = DefaultPanelTextColor;
    }

    private void Awake()
    {
        EnsureLayout();
        ApplyStaticVisuals();
    }

    private void OnEnable()
    {
        EnsureLayout();
        RebindWorldInfoSystem();
        ApplyStaticVisuals();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindWorldInfoSystem();
    }

    private void OnValidate()
    {
        EnsureLayout();
        RebindWorldInfoSystem();
        ApplyStaticVisuals();
        Refresh();
    }

    // Sincronizacao com o sistema do mundo.
    private void HandleInfoChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        WorldInfoSystem info = ResolveWorldInfoSystem();

        if (info == null)
            return;

        SetTextIfChanged(seasonText, SeasonLabels[(int)info.CurrentSeason]);
        SetTextIfChanged(dayText, info.CurrentDayOfSeason.ToString("00"));
        SetTextIfChanged(weekDayText, WeekDayLabels[(int)info.CurrentWeekDay]);
        SetTextIfChanged(timeText, info.GetFormattedTime12Hour());

        string moneyString = info.Money.ToString("0000000");

        for (int i = 0; i < moneyDigitTexts.Length; i++)
        {
            if (moneyDigitTexts[i] == null)
                continue;

            string digitText = i < moneyString.Length
                ? moneyString[i].ToString()
                : "0";

            SetTextIfChanged(moneyDigitTexts[i], digitText);
        }

        if (weatherImage != null)
        {
            Sprite weatherSprite = GetWeatherSprite(info.CurrentWeather);
            if (weatherImage.sprite != weatherSprite)
                weatherImage.sprite = weatherSprite;

            bool shouldEnableWeather = weatherSprite != null;
            if (weatherImage.enabled != shouldEnableWeather)
                weatherImage.enabled = shouldEnableWeather;
        }
    }

    private void RebindWorldInfoSystem()
    {
        WorldInfoSystem info = ResolveWorldInfoSystem();

        if (boundWorldInfoSystem == info)
            return;

        UnbindWorldInfoSystem();

        if (info == null)
            return;

        boundWorldInfoSystem = info;
        worldInfoSystem = info;
        boundWorldInfoSystem.InfoChanged += HandleInfoChanged;
    }

    private void UnbindWorldInfoSystem()
    {
        if (boundWorldInfoSystem == null)
            return;

        boundWorldInfoSystem.InfoChanged -= HandleInfoChanged;
        boundWorldInfoSystem = null;
    }

    private WorldInfoSystem ResolveWorldInfoSystem()
    {
        if (worldInfoSystem != null)
            return worldInfoSystem;

        if (boundWorldInfoSystem != null)
        {
            worldInfoSystem = boundWorldInfoSystem;
            return boundWorldInfoSystem;
        }

        worldInfoSystem = FindFirstObjectByType<WorldInfoSystem>();

        return worldInfoSystem;
    }

    // Montagem automatica do layout no canvas.
    private void EnsureLayout()
    {
        if (isEnsuringLayout)
            return;

        isEnsuringLayout = true;

        try
        {
            bool changed = false;
            Color textColor = ResolveTextColor();
            RectTransform root = transform as RectTransform;
            EnsureArraySize(ref moneySquareImages, MoneyDigitsCount);
            EnsureArraySize(ref moneyDigitTexts, MoneyDigitsCount);

            if (root != null && root.sizeDelta == Vector2.zero)
            {
                root.sizeDelta = new Vector2(230f, 112f);
                changed = true;
            }

            CleanupLegacyMoneyVisuals();

            backgroundImage = EnsureImage(
                backgroundImage,
                root,
                "Background",
                new Vector2(0f, 0f),
                new Vector2(230f, 112f),
                backgroundSprite,
                preserveAspect: false,
                out bool backgroundCreated) ?? backgroundImage;
            changed |= backgroundCreated;

            seasonFrameImage = EnsureImage(
                seasonFrameImage,
                root,
                "SeasonFrame",
                new Vector2(-34f, 33f),
                new Vector2(88f, 30f),
                seasonFrameSprite,
                preserveAspect: false,
                out bool seasonCreated) ?? seasonFrameImage;
            changed |= seasonCreated;

            dayFrameImage = EnsureImage(
                dayFrameImage,
                root,
                "DayFrame",
                new Vector2(30f, 33f),
                new Vector2(42f, 30f),
                dayFrameSprite,
                preserveAspect: false,
                out bool dayCreated) ?? dayFrameImage;
            changed |= dayCreated;

            weekDayFrameImage = EnsureImage(
                weekDayFrameImage,
                root,
                "WeekDayFrame",
                new Vector2(-62f, 2f),
                new Vector2(54f, 30f),
                weekDayFrameSprite,
                preserveAspect: false,
                out bool weekDayCreated) ?? weekDayFrameImage;
            changed |= weekDayCreated;

            timeFrameImage = EnsureImage(
                timeFrameImage,
                root,
                "TimeFrame",
                new Vector2(8f, 2f),
                new Vector2(88f, 30f),
                seasonFrameSprite,
                preserveAspect: false,
                out bool timeFrameCreated) ?? timeFrameImage;
            changed |= timeFrameCreated;

            weatherImage = EnsureImage(
                weatherImage,
                root,
                "WeatherIcon",
                new Vector2(71f, 2f),
                new Vector2(44f, 46f),
                sunnyWeatherSprite,
                preserveAspect: true,
                out bool weatherCreated) ?? weatherImage;
            changed |= weatherCreated;

            RectTransform seasonParent = seasonFrameImage != null ? seasonFrameImage.rectTransform : root;
            RectTransform dayParent = dayFrameImage != null ? dayFrameImage.rectTransform : root;
            RectTransform weekDayParent = weekDayFrameImage != null ? weekDayFrameImage.rectTransform : root;
            RectTransform timeParent = timeFrameImage != null ? timeFrameImage.rectTransform : root;

            if (moneySymbolImage != null && moneySymbolImage.rectTransform.parent != root)
            {
                moneySymbolImage.rectTransform.SetParent(root, false);
                ConfigureRect(moneySymbolImage.rectTransform, new Vector2(-96f, -30f), new Vector2(18f, 26f));
                changed = true;
            }

            if (timeText != null && timeText.rectTransform.parent != timeParent)
            {
                timeText.rectTransform.SetParent(timeParent, false);
                ConfigureRect(timeText.rectTransform, Vector2.zero, new Vector2(80f, 24f));
                changed = true;
            }

            MigrateChildToParent(root, timeParent, "TimeText");
            MigrateChildToParent(root, root, "MoneySymbol");

            moneySymbolImage = EnsureImage(
                moneySymbolImage,
                root,
                "MoneySymbol",
                new Vector2(-96f, -30f),
                new Vector2(18f, 26f),
                moneySymbolSprite,
                preserveAspect: true,
                out bool moneySymbolCreated) ?? moneySymbolImage;
            changed |= moneySymbolCreated;

            seasonText = EnsureText(
                seasonText,
                seasonParent,
                "SeasonText",
                Vector2.zero,
                new Vector2(78f, 24f),
                15f,
                TextAlignmentOptions.Center,
                textColor,
                out bool seasonTextCreated) ?? seasonText;
            changed |= seasonTextCreated;

            dayText = EnsureText(
                dayText,
                dayParent,
                "DayText",
                Vector2.zero,
                new Vector2(34f, 24f),
                16f,
                TextAlignmentOptions.Center,
                textColor,
                out bool dayTextCreated) ?? dayText;
            changed |= dayTextCreated;

            weekDayText = EnsureText(
                weekDayText,
                weekDayParent,
                "WeekDayText",
                Vector2.zero,
                new Vector2(46f, 24f),
                14f,
                TextAlignmentOptions.Center,
                textColor,
                out bool weekDayTextCreated) ?? weekDayText;
            changed |= weekDayTextCreated;

            timeText = EnsureText(
                timeText,
                timeParent,
                "TimeText",
                Vector2.zero,
                new Vector2(80f, 24f),
                14f,
                TextAlignmentOptions.Center,
                textColor,
                out bool timeTextCreated) ?? timeText;
            changed |= timeTextCreated;

            for (int i = 0; i < moneySquareImages.Length; i++)
            {
                float squareX = -69f + (i * 22f);

                moneySquareImages[i] = EnsureImage(
                    moneySquareImages[i],
                    root,
                    $"MoneySquare_{i + 1:00}",
                    new Vector2(squareX, -30f),
                    new Vector2(22f, 30f),
                    moneyFrameSprite,
                    preserveAspect: false,
                    out bool moneySquareCreated) ?? moneySquareImages[i];
                changed |= moneySquareCreated;

                RectTransform digitParent = moneySquareImages[i] != null ? moneySquareImages[i].rectTransform : root;

                moneyDigitTexts[i] = EnsureText(
                    moneyDigitTexts[i],
                    digitParent,
                    $"MoneyDigit_{i + 1:00}",
                    Vector2.zero,
                    new Vector2(18f, 22f),
                    13f,
                    TextAlignmentOptions.Center,
                    textColor,
                    out bool digitCreated) ?? moneyDigitTexts[i];
                changed |= digitCreated;
            }

#if UNITY_EDITOR
            if (changed && !Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }
        finally
        {
            isEnsuringLayout = false;
        }
    }

    // Aplicacao visual dos sprites e estilos base.
    private void ApplyStaticVisuals()
    {
        WorldInfoSystem info = ResolveWorldInfoSystem();
        Color textColor = ResolveTextColor();

        ApplyImage(backgroundImage, backgroundSprite, preserveAspect: false);
        ApplyImage(seasonFrameImage, seasonFrameSprite, preserveAspect: false);
        ApplyImage(dayFrameImage, dayFrameSprite, preserveAspect: false);
        ApplyImage(weekDayFrameImage, weekDayFrameSprite, preserveAspect: false);
        ApplyImage(timeFrameImage, seasonFrameSprite, preserveAspect: false);
        ApplyImage(moneySymbolImage, moneySymbolSprite, preserveAspect: true);
        ApplyImage(weatherImage, GetWeatherSprite(info != null ? info.CurrentWeather : WorldInfoSystem.WeatherType.Sunny), preserveAspect: true);

        for (int i = 0; i < moneySquareImages.Length; i++)
        {
            ApplyImage(moneySquareImages[i], moneyFrameSprite, preserveAspect: false);
            ApplyTextStyle(moneyDigitTexts[i], textColor);
        }

        ApplyTextStyle(seasonText, textColor);
        ApplyTextStyle(dayText, textColor);
        ApplyTextStyle(weekDayText, textColor);
        ApplyTextStyle(timeText, textColor);
    }

    private Sprite GetWeatherSprite(WorldInfoSystem.WeatherType weatherType)
    {
        return weatherType switch
        {
            WorldInfoSystem.WeatherType.Partial => partialWeatherSprite,
            WorldInfoSystem.WeatherType.Cloudy => cloudyWeatherSprite,
            WorldInfoSystem.WeatherType.Windy => windWeatherSprite,
            WorldInfoSystem.WeatherType.Rain => rainWeatherSprite,
            WorldInfoSystem.WeatherType.Storm => stormWeatherSprite,
            _ => sunnyWeatherSprite
        };
    }

    // Fabrica de elementos de UI.
    private static Image EnsureImage(
        Image currentImage,
        RectTransform parent,
        string childName,
        Vector2 anchoredPosition,
        Vector2 size,
        Sprite sprite,
        bool preserveAspect,
        out bool created)
    {
        if (currentImage != null)
        {
            created = false;
            return currentImage;
        }

        GameObject child = FindDirectChild(parent, childName);
        created = child == null;

        if (child == null)
            child = CreateChildObject(parent, childName);

        RectTransform rectTransform = child.GetComponent<RectTransform>();
        if (created)
            ConfigureRect(rectTransform, anchoredPosition, size);

        Image image = child.GetComponent<Image>();
        if (image == null)
            image = child.AddComponent<Image>();

        image.sprite = sprite;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        image.type = Image.Type.Simple;

        return image;
    }

    private static TextMeshProUGUI EnsureText(
        TextMeshProUGUI currentText,
        RectTransform parent,
        string childName,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment,
        Color textColor,
        out bool created)
    {
        if (currentText != null)
        {
            created = false;
            return currentText;
        }

        GameObject child = FindDirectChild(parent, childName);
        created = child == null;

        if (child == null)
            child = CreateChildObject(parent, childName);

        RectTransform rectTransform = child.GetComponent<RectTransform>();
        if (created)
            ConfigureRect(rectTransform, anchoredPosition, size);

        TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
        if (text == null)
            text = child.AddComponent<TextMeshProUGUI>();

        TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
        if (defaultFont != null)
        {
            text.font = defaultFont;
            text.fontSharedMaterial = defaultFont.material;
        }

        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = textColor;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        text.margin = Vector4.zero;
        text.text = string.Empty;

        return text;
    }

    // Utilitarios visuais.
    private static void ApplyImage(Image image, Sprite sprite, bool preserveAspect)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        image.type = Image.Type.Simple;
        image.enabled = sprite != null;
    }

    private static void ApplyTextStyle(TextMeshProUGUI text, Color textColor)
    {
        if (text == null)
            return;

        text.color = textColor;
        text.raycastTarget = false;
    }

    private static void SetTextIfChanged(TextMeshProUGUI text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }

    private Color ResolveTextColor()
    {
        return generalTextColor.a <= 0f &&
               generalTextColor.r <= 0f &&
               generalTextColor.g <= 0f &&
               generalTextColor.b <= 0f
            ? DefaultPanelTextColor
            : generalTextColor;
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

    private static void EnsureArraySize<T>(ref T[] array, int size)
    {
        if (array != null && array.Length == size)
            return;

        T[] resized = new T[size];

        if (array != null)
        {
            int copyLength = Mathf.Min(array.Length, size);
            for (int i = 0; i < copyLength; i++)
                resized[i] = array[i];
        }

        array = resized;
    }

    private static GameObject FindDirectChild(RectTransform parent, string childName)
    {
        if (parent == null)
            return null;

        Transform child = parent.Find(childName);
        return child != null ? child.gameObject : null;
    }

    private static GameObject CreateChildObject(RectTransform parent, string childName)
    {
        GameObject child = new(childName, typeof(RectTransform), typeof(CanvasRenderer));
        RectTransform rectTransform = child.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
#endif

        return child;
    }

    // Utilitarios de migracao e limpeza do layout antigo.
    private void CleanupLegacyMoneyVisuals()
    {
        DisableLegacyChild("MoneyFrame");
        DisableLegacyChild("MoneyText");
    }

    private void DisableLegacyChild(string childName)
    {
        Transform legacyChild = transform.Find(childName);
        if (legacyChild == null)
            return;

        if (legacyChild.gameObject.activeSelf)
            legacyChild.gameObject.SetActive(false);
    }

    private void MigrateChildToParent(RectTransform searchRoot, RectTransform targetParent, string childName)
    {
        if (searchRoot == null || targetParent == null)
            return;

        Transform child = searchRoot.Find(childName);
        if (child == null || child.parent == targetParent)
            return;

        child.SetParent(targetParent, false);
    }
}
