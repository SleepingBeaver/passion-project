using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class PauseMenuUI : MonoBehaviour
{
    private const string PauseRootName = "PauseMenuRoot";
    private const string MainPanelName = "PauseMainPanel";
    private const string OptionsPanelName = "PauseOptionsPanel";
    private const string TitleName = "Title";
    private const string SubtitleName = "Subtitle";
    private const string ResumeButtonName = "ResumeButton";
    private const string OptionsButtonName = "OptionsButton";
    private const string QuitButtonName = "QuitButton";
    private const string VideoHeaderName = "VideoHeader";
    private const string AudioHeaderName = "AudioHeader";
    private const string LanguageHeaderName = "LanguageHeader";
    private const string FullscreenRowName = "FullscreenRow";
    private const string QualityRowName = "QualityRow";
    private const string VSyncRowName = "VSyncRow";
    private const string VolumeRowName = "VolumeRow";
    private const string LanguageRowName = "LanguageRow";
    private const string BackButtonName = "BackButton";

    private const string MasterVolumePrefKey = "pause_menu.master_volume";
    private const string FullscreenPrefKey = "pause_menu.fullscreen";
    private const string QualityPrefKey = "pause_menu.quality_index";
    private const string VSyncPrefKey = "pause_menu.vsync";
    private const string LanguagePrefKey = "pause_menu.language";

    private const float VolumeStep = 0.1f;

    private enum MenuPanel
    {
        Main = 0,
        Options = 1
    }

    private enum MenuLanguage
    {
        Portuguese = 0,
        English = 1,
        Spanish = 2
    }

    [Header("References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private PlayerInteractor playerInteractor;
    [SerializeField] private IsoPlayerController2D playerMovement;
    [SerializeField] private SimpleInventoryUI simpleInventoryUI;

    [Header("Generated Layout")]
    [SerializeField] private GameObject pauseMenuRoot;
    [SerializeField] private CanvasGroup pauseMenuCanvasGroup;
    [SerializeField] private RectTransform mainPanelRect;
    [SerializeField] private CanvasGroup mainPanelCanvasGroup;
    [SerializeField] private RectTransform optionsPanelRect;
    [SerializeField] private CanvasGroup optionsPanelCanvasGroup;

    [SerializeField] private TextMeshProUGUI mainTitleText;
    [SerializeField] private TextMeshProUGUI mainSubtitleText;
    [SerializeField] private Button resumeButton;
    [SerializeField] private TextMeshProUGUI resumeButtonText;
    [SerializeField] private Button optionsButton;
    [SerializeField] private TextMeshProUGUI optionsButtonText;
    [SerializeField] private Button quitButton;
    [SerializeField] private TextMeshProUGUI quitButtonText;

    [SerializeField] private TextMeshProUGUI optionsTitleText;
    [SerializeField] private TextMeshProUGUI optionsSubtitleText;
    [SerializeField] private TextMeshProUGUI videoHeaderText;
    [SerializeField] private TextMeshProUGUI fullscreenLabelText;
    [SerializeField] private Button fullscreenValueButton;
    [SerializeField] private TextMeshProUGUI fullscreenValueText;
    [SerializeField] private TextMeshProUGUI qualityLabelText;
    [SerializeField] private Button qualityPreviousButton;
    [SerializeField] private TextMeshProUGUI qualityPreviousButtonText;
    [SerializeField] private TextMeshProUGUI qualityValueText;
    [SerializeField] private Button qualityNextButton;
    [SerializeField] private TextMeshProUGUI qualityNextButtonText;
    [SerializeField] private TextMeshProUGUI vSyncLabelText;
    [SerializeField] private Button vSyncValueButton;
    [SerializeField] private TextMeshProUGUI vSyncValueText;
    [SerializeField] private TextMeshProUGUI audioHeaderText;
    [SerializeField] private TextMeshProUGUI volumeLabelText;
    [SerializeField] private Button volumeDecreaseButton;
    [SerializeField] private TextMeshProUGUI volumeDecreaseButtonText;
    [SerializeField] private TextMeshProUGUI volumeValueText;
    [SerializeField] private Button volumeIncreaseButton;
    [SerializeField] private TextMeshProUGUI volumeIncreaseButtonText;
    [SerializeField] private TextMeshProUGUI languageHeaderText;
    [SerializeField] private TextMeshProUGUI languageLabelText;
    [SerializeField] private Button languagePreviousButton;
    [SerializeField] private TextMeshProUGUI languagePreviousButtonText;
    [SerializeField] private TextMeshProUGUI languageValueText;
    [SerializeField] private Button languageNextButton;
    [SerializeField] private TextMeshProUGUI languageNextButtonText;
    [SerializeField] private Button backButton;
    [SerializeField] private TextMeshProUGUI backButtonText;

    private Keyboard keyboard;
    private bool pausedByMenu;
    private bool usingSimpleInventoryModal;
    private bool wasInteractorEnabled;
    private bool wasMovementEnabled;
    private bool toggleInputArmed;
    private float previousTimeScale = 1f;

    private float masterVolume = 1f;
    private bool fullscreenEnabled = true;
    private int qualityIndex;
    private bool vSyncEnabled = true;
    private MenuLanguage currentLanguage = MenuLanguage.Portuguese;

    private static Sprite generatedWhiteSprite;
    private bool isInitialized;

    private bool IsMenuOpen => pauseMenuRoot != null && pauseMenuRoot.activeSelf;
    private bool IsOptionsOpen => optionsPanelRect != null && optionsPanelRect.gameObject.activeSelf;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        if (Application.isPlaying)
            InitializeRuntime();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && !isInitialized)
            InitializeRuntime();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        isInitialized = false;

        if (pausedByMenu || IsMenuOpen)
            CloseMenuInternal();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        keyboard ??= Keyboard.current;

        if (keyboard == null)
            return;

        if (!toggleInputArmed)
        {
            if (keyboard.escapeKey.isPressed)
                return;

            toggleInputArmed = true;
        }

        if (!keyboard.escapeKey.wasPressedThisFrame)
            return;

        if (IsMenuOpen)
        {
            if (IsOptionsOpen)
                ShowMainPanel();
            else
                CloseMenuInternal();

            return;
        }

        if (CanOpenMenu())
            OpenMainPanel();
    }

    // Runtime setup centralizado para evitar repetir a mesma preparacao em Awake/OnEnable.
    private void InitializeRuntime()
    {
        isInitialized = true;
        keyboard = Keyboard.current;
        toggleInputArmed = !Application.isPlaying || keyboard == null || !keyboard.escapeKey.isPressed;

        ResolveReferences();
        LoadSettings();
        EnsureLayout();
        BindButtonCallbacks();
        RefreshDisplayedCopy();

        if (Application.isPlaying)
        {
            ApplySettings();
            SetClosedVisualStateImmediate();
        }
    }

    private void ResolveReferences()
    {
        targetCanvas ??= GetComponent<Canvas>();
        targetCanvas ??= GetComponentInParent<Canvas>();
        targetCanvas = targetCanvas != null ? targetCanvas.rootCanvas : null;

        playerInteractor ??= FindFirstObjectByType<PlayerInteractor>();

        if (playerInteractor != null)
            playerMovement ??= playerInteractor.GetComponent<IsoPlayerController2D>();

        playerMovement ??= FindFirstObjectByType<IsoPlayerController2D>();
        simpleInventoryUI ??= FindFirstObjectByType<SimpleInventoryUI>();
    }

    private void LoadSettings()
    {
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, AudioListener.volume));
        fullscreenEnabled = PlayerPrefs.GetInt(FullscreenPrefKey, Screen.fullScreen ? 1 : 0) != 0;
        vSyncEnabled = PlayerPrefs.GetInt(VSyncPrefKey, QualitySettings.vSyncCount > 0 ? 1 : 0) != 0;
        qualityIndex = Mathf.Clamp(
            PlayerPrefs.GetInt(QualityPrefKey, QualitySettings.GetQualityLevel()),
            0,
            Mathf.Max(0, QualitySettings.names.Length - 1));
        currentLanguage = (MenuLanguage)Mathf.Clamp(
            PlayerPrefs.GetInt(LanguagePrefKey, (int)MenuLanguage.Portuguese),
            0,
            2);
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetFloat(MasterVolumePrefKey, masterVolume);
        PlayerPrefs.SetInt(FullscreenPrefKey, fullscreenEnabled ? 1 : 0);
        PlayerPrefs.SetInt(QualityPrefKey, qualityIndex);
        PlayerPrefs.SetInt(VSyncPrefKey, vSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(LanguagePrefKey, (int)currentLanguage);
        PlayerPrefs.Save();
    }

    private void ApplySettings()
    {
        AudioListener.volume = masterVolume;
        Screen.fullScreen = fullscreenEnabled;
        QualitySettings.vSyncCount = vSyncEnabled ? 1 : 0;

        if (QualitySettings.names.Length > 0)
            QualitySettings.SetQualityLevel(Mathf.Clamp(qualityIndex, 0, QualitySettings.names.Length - 1), true);
    }

    private bool CanOpenMenu()
    {
        if (pauseMenuRoot == null)
            return false;

        if (Time.timeScale <= 0.0001f && !pausedByMenu)
            return false;

        simpleInventoryUI ??= FindFirstObjectByType<SimpleInventoryUI>();
        if (simpleInventoryUI != null &&
            (simpleInventoryUI.IsOpen || simpleInventoryUI.IsTransitioning || simpleInventoryUI.IsExternalModalOpen))
        {
            return false;
        }

        CraftingUI craftingUI = CraftingUI.FindExistingCraftingUI();
        if (craftingUI != null && craftingUI.IsOpen)
            return false;

        CrateStorageUI storageUI = CrateStorageUI.FindExistingStorageUI();
        if (storageUI != null && storageUI.IsOpen)
            return false;

        return true;
    }

    // Fluxo principal de exibicao do menu: root visivel e painel correto selecionado.
    private void OpenMenu(MenuPanel panel, Button defaultSelection)
    {
        PauseGameplay(true);
        SetRootVisibility(true);
        ApplyPanelState(panel, defaultSelection, selectDefault: true);
    }

    private void ApplyPanelState(MenuPanel panel, Button defaultSelection, bool selectDefault)
    {
        SetPanelState(mainPanelCanvasGroup, panel == MenuPanel.Main);
        SetPanelState(optionsPanelCanvasGroup, panel == MenuPanel.Options);
        RefreshDisplayedCopy();

        if (selectDefault)
            SelectButton(defaultSelection);
    }

    private void RefreshDisplayedCopy()
    {
        RefreshLocalizedText();
        RefreshValueText();
    }

    private void SetRootVisibility(bool visible)
    {
        if (pauseMenuCanvasGroup != null)
        {
            pauseMenuCanvasGroup.alpha = visible ? 1f : 0f;
            pauseMenuCanvasGroup.interactable = visible;
            pauseMenuCanvasGroup.blocksRaycasts = visible;
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(visible);

            if (visible)
                pauseMenuRoot.transform.SetAsLastSibling();
        }
    }

    private void OpenMainPanel()
    {
        OpenMenu(MenuPanel.Main, resumeButton);
    }

    private void ShowOptionsPanel()
    {
        if (!IsMenuOpen)
        {
            if (Application.isPlaying)
            {
                OpenMenu(MenuPanel.Options, fullscreenValueButton);
                return;
            }

            SetRootVisibility(true);
        }

        ApplyPanelState(MenuPanel.Options, fullscreenValueButton, selectDefault: Application.isPlaying);
    }

    private void ShowMainPanel()
    {
        if (!IsMenuOpen && Application.isPlaying)
        {
            OpenMenu(MenuPanel.Main, optionsButton);
            return;
        }

        if (!IsMenuOpen)
            SetRootVisibility(true);

        ApplyPanelState(MenuPanel.Main, optionsButton, selectDefault: Application.isPlaying);
    }

    private void ResumeGameplay()
    {
        CloseMenuInternal();
    }

    private void CloseMenuInternal()
    {
        SetClosedVisualStateImmediate();
        PauseGameplay(false);
        ClearSelectedObject();
    }

    private void SetClosedVisualStateImmediate()
    {
        SetPanelState(mainPanelCanvasGroup, false);
        SetPanelState(optionsPanelCanvasGroup, false);
        SetRootVisibility(false);
    }

    private void PauseGameplay(bool shouldPause)
    {
        if (shouldPause)
        {
            if (pausedByMenu)
                return;

            simpleInventoryUI ??= FindFirstObjectByType<SimpleInventoryUI>();
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

            pausedByMenu = true;
            return;
        }

        if (!pausedByMenu)
            return;

        pausedByMenu = false;

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

    private void BindButtonCallbacks()
    {
        BindButton(resumeButton, ResumeGameplay);
        BindButton(optionsButton, ShowOptionsPanel);
        BindButton(quitButton, QuitGame);
        BindButton(fullscreenValueButton, ToggleFullscreen);
        BindButton(qualityPreviousButton, DecreaseQuality);
        BindButton(qualityNextButton, IncreaseQuality);
        BindButton(vSyncValueButton, ToggleVSync);
        BindButton(volumeDecreaseButton, DecreaseVolume);
        BindButton(volumeIncreaseButton, IncreaseVolume);
        BindButton(languagePreviousButton, PreviousLanguage);
        BindButton(languageNextButton, NextLanguage);
        BindButton(backButton, ShowMainPanel);
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction callback)
    {
        if (button == null || callback == null)
            return;

        button.onClick.RemoveListener(callback);
        button.onClick.AddListener(callback);
    }

    private void ToggleFullscreen()
    {
        fullscreenEnabled = !fullscreenEnabled;
        ApplyAndSaveSettings();
    }

    private void ToggleVSync()
    {
        vSyncEnabled = !vSyncEnabled;
        ApplyAndSaveSettings();
    }

    private void IncreaseQuality()
    {
        if (QualitySettings.names.Length == 0)
            return;

        qualityIndex = (qualityIndex + 1) % QualitySettings.names.Length;
        ApplyAndSaveSettings();
    }

    private void DecreaseQuality()
    {
        if (QualitySettings.names.Length == 0)
            return;

        qualityIndex--;
        if (qualityIndex < 0)
            qualityIndex = QualitySettings.names.Length - 1;

        ApplyAndSaveSettings();
    }

    private void IncreaseVolume()
    {
        masterVolume = Mathf.Clamp01(masterVolume + VolumeStep);
        ApplyAndSaveSettings();
    }

    private void DecreaseVolume()
    {
        masterVolume = Mathf.Clamp01(masterVolume - VolumeStep);
        ApplyAndSaveSettings();
    }

    private void NextLanguage()
    {
        currentLanguage = (MenuLanguage)(((int)currentLanguage + 1) % 3);
        ApplyAndSaveSettings();
    }

    private void PreviousLanguage()
    {
        int previous = (int)currentLanguage - 1;
        if (previous < 0)
            previous = 2;

        currentLanguage = (MenuLanguage)previous;
        ApplyAndSaveSettings();
    }

    private void ApplyAndSaveSettings()
    {
        SaveSettings();

        if (Application.isPlaying)
            ApplySettings();

        RefreshDisplayedCopy();
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void RefreshLocalizedText()
    {
        SetText(mainTitleText, GetMainTitle());
        SetText(mainSubtitleText, GetMainSubtitle());
        SetText(resumeButtonText, GetResumeLabel());
        SetText(optionsButtonText, GetOptionsLabel());
        SetText(quitButtonText, GetQuitLabel());

        SetText(optionsTitleText, GetOptionsTitle());
        SetText(optionsSubtitleText, GetOptionsSubtitle());
        SetText(videoHeaderText, GetVideoHeader());
        SetText(fullscreenLabelText, GetFullscreenLabel());
        SetText(qualityLabelText, GetQualityLabel());
        SetText(vSyncLabelText, GetVSyncLabel());
        SetText(audioHeaderText, GetAudioHeader());
        SetText(volumeLabelText, GetVolumeLabel());
        SetText(languageHeaderText, GetLanguageHeader());
        SetText(languageLabelText, GetLanguageLabel());
        SetText(backButtonText, GetBackLabel());

        SetText(qualityPreviousButtonText, "<");
        SetText(qualityNextButtonText, ">");
        SetText(volumeDecreaseButtonText, "-");
        SetText(volumeIncreaseButtonText, "+");
        SetText(languagePreviousButtonText, "<");
        SetText(languageNextButtonText, ">");
    }

    private void RefreshValueText()
    {
        SetText(fullscreenValueText, GetOnOffLabel(fullscreenEnabled));
        SetText(vSyncValueText, GetOnOffLabel(vSyncEnabled));
        SetText(volumeValueText, $"{Mathf.RoundToInt(masterVolume * 100f)}%");
        SetText(languageValueText, GetLanguageDisplayName(currentLanguage));

        if (QualitySettings.names.Length > 0)
        {
            qualityIndex = Mathf.Clamp(qualityIndex, 0, QualitySettings.names.Length - 1);
            SetText(qualityValueText, QualitySettings.names[qualityIndex]);
        }
        else
        {
            SetText(qualityValueText, "-");
        }
    }

    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null)
            target.text = value;
    }

    private string GetMainTitle()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Pause Menu",
            MenuLanguage.Spanish => "Menu de Pausa",
            _ => "Menu de Pausa"
        };
    }

    private string GetMainSubtitle()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Resume the game or open the settings.",
            MenuLanguage.Spanish => "Vuelve al juego o ajusta las opciones.",
            _ => "Retome ou ajuste as configuracoes da partida."
        };
    }

    private string GetResumeLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Resume",
            MenuLanguage.Spanish => "Continuar",
            _ => "Retomar"
        };
    }

    private string GetOptionsLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Options",
            MenuLanguage.Spanish => "Opciones",
            _ => "Opcoes"
        };
    }

    private string GetQuitLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Quit",
            MenuLanguage.Spanish => "Salir",
            _ => "Sair"
        };
    }

    private string GetOptionsTitle()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Options",
            MenuLanguage.Spanish => "Opciones",
            _ => "Opcoes"
        };
    }

    private string GetOptionsSubtitle()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Video, audio and language controls are applied instantly.",
            MenuLanguage.Spanish => "Video, audio e idioma se aplican al instante.",
            _ => "Video, audio e idioma sao aplicados na hora."
        };
    }

    private string GetVideoHeader()
    {
        return "Video";
    }

    private string GetFullscreenLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Fullscreen",
            MenuLanguage.Spanish => "Pantalla completa",
            _ => "Tela cheia"
        };
    }

    private string GetQualityLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Quality",
            MenuLanguage.Spanish => "Calidad",
            _ => "Qualidade"
        };
    }

    private string GetVSyncLabel()
    {
        return "V-Sync";
    }

    private string GetAudioHeader()
    {
        return "Audio";
    }

    private string GetVolumeLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Master volume",
            MenuLanguage.Spanish => "Volumen general",
            _ => "Volume geral"
        };
    }

    private string GetLanguageHeader()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Language",
            MenuLanguage.Spanish => "Idioma",
            _ => "Idioma"
        };
    }

    private string GetLanguageLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Menu language",
            MenuLanguage.Spanish => "Idioma del menu",
            _ => "Idioma do menu"
        };
    }

    private string GetBackLabel()
    {
        return currentLanguage switch
        {
            MenuLanguage.English => "Back",
            MenuLanguage.Spanish => "Volver",
            _ => "Voltar"
        };
    }

    private string GetOnOffLabel(bool value)
    {
        return currentLanguage switch
        {
            MenuLanguage.English => value ? "On" : "Off",
            MenuLanguage.Spanish => value ? "Activo" : "Inactivo",
            _ => value ? "Ligado" : "Desligado"
        };
    }

    private static string GetLanguageDisplayName(MenuLanguage language)
    {
        return language switch
        {
            MenuLanguage.English => "English",
            MenuLanguage.Spanish => "Espanol",
            _ => "Portugues"
        };
    }

    private void EnsureLayout()
    {
        if (targetCanvas == null)
            return;

        pauseMenuRoot = EnsureRootObject(PauseRootName, targetCanvas.transform);
        pauseMenuCanvasGroup = GetOrAddComponent<CanvasGroup>(pauseMenuRoot);
        pauseMenuCanvasGroup.interactable = false;
        pauseMenuCanvasGroup.blocksRaycasts = false;

        RectTransform rootRect = pauseMenuRoot.transform as RectTransform;
        StretchToParent(rootRect);
        pauseMenuRoot.transform.SetAsLastSibling();

        Image overlayImage = GetOrAddComponent<Image>(pauseMenuRoot);
        overlayImage.sprite = GetWhiteSprite();
        overlayImage.type = Image.Type.Simple;
        overlayImage.color = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        overlayImage.raycastTarget = true;

        mainPanelRect = EnsureWindowPanel(MainPanelName, pauseMenuRoot.transform, new Vector2(420f, 380f), new Color(0.15f, 0.18f, 0.24f, 0.98f), out mainPanelCanvasGroup);
        optionsPanelRect = EnsureWindowPanel(OptionsPanelName, pauseMenuRoot.transform, new Vector2(760f, 520f), new Color(0.12f, 0.16f, 0.22f, 0.98f), out optionsPanelCanvasGroup);

        EnsureMainPanelContent();
        EnsureOptionsPanelContent();
    }

    private void EnsureMainPanelContent()
    {
        ConfigurePanelLayout(mainPanelRect, 28, 18);

        mainTitleText = EnsurePanelTitle(mainPanelRect, TitleName, 40f);
        mainSubtitleText = EnsureBodyText(mainPanelRect, SubtitleName, 19f, new Color(0.82f, 0.88f, 0.94f, 0.92f), 50f);

        resumeButton = EnsureMenuButton(mainPanelRect, ResumeButtonName, new Color(0.21f, 0.56f, 0.38f, 1f), out resumeButtonText);
        optionsButton = EnsureMenuButton(mainPanelRect, OptionsButtonName, new Color(0.72f, 0.53f, 0.18f, 1f), out optionsButtonText);
        quitButton = EnsureMenuButton(mainPanelRect, QuitButtonName, new Color(0.67f, 0.24f, 0.24f, 1f), out quitButtonText);
    }

    private void EnsureOptionsPanelContent()
    {
        ConfigurePanelLayout(optionsPanelRect, 26, 12);

        optionsTitleText = EnsurePanelTitle(optionsPanelRect, TitleName, 36f);
        optionsSubtitleText = EnsureBodyText(optionsPanelRect, SubtitleName, 17f, new Color(0.82f, 0.88f, 0.94f, 0.9f), 44f);

        videoHeaderText = EnsureSectionHeader(optionsPanelRect, VideoHeaderName, new Color(0.55f, 0.8f, 0.95f, 1f));
        EnsureToggleRow(optionsPanelRect, FullscreenRowName, out fullscreenLabelText, out fullscreenValueButton, out fullscreenValueText);
        EnsureCyclerRow(optionsPanelRect, QualityRowName, out qualityLabelText, out qualityPreviousButton, out qualityPreviousButtonText, out qualityValueText, out qualityNextButton, out qualityNextButtonText);
        EnsureToggleRow(optionsPanelRect, VSyncRowName, out vSyncLabelText, out vSyncValueButton, out vSyncValueText);

        audioHeaderText = EnsureSectionHeader(optionsPanelRect, AudioHeaderName, new Color(0.98f, 0.73f, 0.38f, 1f));
        EnsureCyclerRow(optionsPanelRect, VolumeRowName, out volumeLabelText, out volumeDecreaseButton, out volumeDecreaseButtonText, out volumeValueText, out volumeIncreaseButton, out volumeIncreaseButtonText);

        languageHeaderText = EnsureSectionHeader(optionsPanelRect, LanguageHeaderName, new Color(0.59f, 0.88f, 0.7f, 1f));
        EnsureCyclerRow(optionsPanelRect, LanguageRowName, out languageLabelText, out languagePreviousButton, out languagePreviousButtonText, out languageValueText, out languageNextButton, out languageNextButtonText);

        backButton = EnsureMenuButton(optionsPanelRect, BackButtonName, new Color(0.27f, 0.34f, 0.46f, 1f), out backButtonText);
    }

    private static GameObject EnsureRootObject(string objectName, Transform parent)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
            return existing.gameObject;

        GameObject root = new(objectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        return root;
    }

    private static RectTransform EnsureWindowPanel(string objectName, Transform parent, Vector2 size, Color backgroundColor, out CanvasGroup canvasGroup)
    {
        GameObject panelObject = EnsureRootObject(objectName, parent);
        RectTransform rect = panelObject.transform as RectTransform;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        Image image = GetOrAddComponent<Image>(panelObject);
        image.sprite = GetWhiteSprite();
        image.color = backgroundColor;
        image.raycastTarget = true;

        Outline outline = GetOrAddComponent<Outline>(panelObject);
        outline.effectColor = new Color(0.03f, 0.05f, 0.08f, 0.55f);
        outline.effectDistance = new Vector2(3f, -3f);

        canvasGroup = GetOrAddComponent<CanvasGroup>(panelObject);
        return rect;
    }

    private static void ConfigurePanelLayout(RectTransform panelRect, int padding, float spacing)
    {
        VerticalLayoutGroup layout = GetOrAddComponent<VerticalLayoutGroup>(panelRect.gameObject);
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = spacing;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childAlignment = TextAnchor.UpperCenter;
    }

    private static TextMeshProUGUI EnsurePanelTitle(RectTransform parent, string objectName, float fontSize)
    {
        TextMeshProUGUI text = EnsureText(parent, objectName, fontSize, FontStyles.Bold, new Color(0.96f, 0.97f, 0.99f, 1f), TextAlignmentOptions.Center);
        SetPreferredHeight(text.rectTransform, 56f);
        return text;
    }

    private static TextMeshProUGUI EnsureBodyText(RectTransform parent, string objectName, float fontSize, Color color, float preferredHeight)
    {
        TextMeshProUGUI text = EnsureText(parent, objectName, fontSize, FontStyles.Normal, color, TextAlignmentOptions.Center);
        SetPreferredHeight(text.rectTransform, preferredHeight);
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static TextMeshProUGUI EnsureSectionHeader(RectTransform parent, string objectName, Color color)
    {
        TextMeshProUGUI text = EnsureText(parent, objectName, 24f, FontStyles.Bold, color, TextAlignmentOptions.Left);
        SetPreferredHeight(text.rectTransform, 30f);
        return text;
    }

    private static void EnsureToggleRow(RectTransform parent, string objectName, out TextMeshProUGUI labelText, out Button valueButton, out TextMeshProUGUI valueText)
    {
        RectTransform row = EnsureRow(parent, objectName);

        labelText = EnsureText(row, "Label", 18f, FontStyles.Normal, new Color(0.94f, 0.96f, 0.98f, 1f), TextAlignmentOptions.Left);
        SetFlexibleWidth(labelText.rectTransform, 1f);

        valueButton = EnsureValueButton(row, "ValueButton", out valueText);
        SetPreferredWidth(valueButton.transform as RectTransform, 190f);
    }

    private static void EnsureCyclerRow(
        RectTransform parent,
        string objectName,
        out TextMeshProUGUI labelText,
        out Button previousButton,
        out TextMeshProUGUI previousButtonText,
        out TextMeshProUGUI valueText,
        out Button nextButton,
        out TextMeshProUGUI nextButtonText)
    {
        RectTransform row = EnsureRow(parent, objectName);

        labelText = EnsureText(row, "Label", 18f, FontStyles.Normal, new Color(0.94f, 0.96f, 0.98f, 1f), TextAlignmentOptions.Left);
        SetFlexibleWidth(labelText.rectTransform, 1f);

        previousButton = EnsureSquareButton(row, "PreviousButton", out previousButtonText);
        valueText = EnsureValueLabel(row, "ValueText");
        nextButton = EnsureSquareButton(row, "NextButton", out nextButtonText);
    }

    private static RectTransform EnsureRow(RectTransform parent, string objectName)
    {
        GameObject rowObject = EnsureRootObject(objectName, parent);
        RectTransform row = rowObject.transform as RectTransform;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 0.5f);
        row.sizeDelta = new Vector2(0f, 48f);

        HorizontalLayoutGroup layout = GetOrAddComponent<HorizontalLayoutGroup>(rowObject);
        layout.spacing = 10f;
        layout.padding = new RectOffset(0, 0, 2, 2);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;

        SetPreferredHeight(row, 50f);
        return row;
    }

    private static Button EnsureMenuButton(RectTransform parent, string objectName, Color color, out TextMeshProUGUI labelText)
    {
        GameObject buttonObject = EnsureRootObject(objectName, parent);
        RectTransform rect = buttonObject.transform as RectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(0f, 62f);

        Image image = GetOrAddComponent<Image>(buttonObject);
        image.sprite = GetWhiteSprite();
        image.color = color;
        image.raycastTarget = true;

        Button button = GetOrAddComponent<Button>(buttonObject);
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateButtonColors(color);

        LayoutElement layout = GetOrAddComponent<LayoutElement>(buttonObject);
        layout.preferredHeight = 62f;
        layout.flexibleWidth = 1f;

        labelText = EnsureButtonLabel(rect, "Label");
        return button;
    }

    private static Button EnsureValueButton(RectTransform parent, string objectName, out TextMeshProUGUI valueText)
    {
        GameObject buttonObject = EnsureRootObject(objectName, parent);
        RectTransform rect = buttonObject.transform as RectTransform;
        rect.sizeDelta = new Vector2(190f, 42f);

        Image image = GetOrAddComponent<Image>(buttonObject);
        image.sprite = GetWhiteSprite();
        image.color = new Color(0.22f, 0.3f, 0.42f, 1f);

        Button button = GetOrAddComponent<Button>(buttonObject);
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateButtonColors(image.color);

        LayoutElement layout = GetOrAddComponent<LayoutElement>(buttonObject);
        layout.preferredWidth = 190f;
        layout.preferredHeight = 42f;

        valueText = EnsureButtonLabel(rect, "Label", 17f);
        return button;
    }

    private static Button EnsureSquareButton(RectTransform parent, string objectName, out TextMeshProUGUI labelText)
    {
        GameObject buttonObject = EnsureRootObject(objectName, parent);
        RectTransform rect = buttonObject.transform as RectTransform;
        rect.sizeDelta = new Vector2(42f, 42f);

        Image image = GetOrAddComponent<Image>(buttonObject);
        image.sprite = GetWhiteSprite();
        image.color = new Color(0.23f, 0.31f, 0.42f, 1f);

        Button button = GetOrAddComponent<Button>(buttonObject);
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateButtonColors(image.color);

        LayoutElement layout = GetOrAddComponent<LayoutElement>(buttonObject);
        layout.preferredWidth = 42f;
        layout.preferredHeight = 42f;

        labelText = EnsureButtonLabel(rect, "Label", 20f);
        return button;
    }

    private static TextMeshProUGUI EnsureValueLabel(RectTransform parent, string objectName)
    {
        TextMeshProUGUI text = EnsureText(parent, objectName, 17f, FontStyles.Normal, new Color(0.95f, 0.97f, 1f, 1f), TextAlignmentOptions.Center);
        text.fontWeight = FontWeight.Medium;
        SetPreferredWidth(text.rectTransform, 150f);
        SetPreferredHeight(text.rectTransform, 42f);
        return text;
    }

    private static TextMeshProUGUI EnsureButtonLabel(RectTransform parent, string objectName, float fontSize = 20f)
    {
        TextMeshProUGUI text = EnsureText(parent, objectName, fontSize, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        StretchToParent(text.rectTransform);
        return text;
    }

    private static TextMeshProUGUI EnsureText(RectTransform parent, string objectName, float fontSize, FontStyles fontStyle, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = EnsureRootObject(objectName, parent);
        RectTransform rect = textObject.transform as RectTransform;

        TextMeshProUGUI text = GetOrAddComponent<TextMeshProUGUI>(textObject);
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = string.Empty;

        if (text.font == null && TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        LayoutElement layout = GetOrAddComponent<LayoutElement>(textObject);
        layout.flexibleWidth = 0f;

        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0f, 40f);
        return text;
    }

    private static void SetPanelState(CanvasGroup group, bool visible)
    {
        if (group == null)
            return;

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        group.gameObject.SetActive(visible);
    }

    private static void StretchToParent(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;
    }

    private static void SetPreferredHeight(RectTransform rect, float value)
    {
        if (rect == null)
            return;

        LayoutElement layout = GetOrAddComponent<LayoutElement>(rect.gameObject);
        layout.preferredHeight = value;
    }

    private static void SetPreferredWidth(RectTransform rect, float value)
    {
        if (rect == null)
            return;

        LayoutElement layout = GetOrAddComponent<LayoutElement>(rect.gameObject);
        layout.preferredWidth = value;
    }

    private static void SetFlexibleWidth(RectTransform rect, float value)
    {
        if (rect == null)
            return;

        LayoutElement layout = GetOrAddComponent<LayoutElement>(rect.gameObject);
        layout.flexibleWidth = value;
    }

    private static ColorBlock CreateButtonColors(Color baseColor)
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = baseColor;
        colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.18f);
        colors.selectedColor = Color.Lerp(baseColor, Color.white, 0.12f);
        colors.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        return colors;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        if (!target.TryGetComponent(out T component))
            component = target.AddComponent<T>();

        return component;
    }

    private static Sprite GetWhiteSprite()
    {
        if (generatedWhiteSprite != null)
            return generatedWhiteSprite;

        generatedWhiteSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        generatedWhiteSprite.name = "PauseMenuGeneratedWhiteSprite";
        return generatedWhiteSprite;
    }

    private static void SelectButton(Button button)
    {
        if (button == null || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private static void ClearSelectedObject()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

#if UNITY_EDITOR
    public void BuildLayoutForEditorPreview(bool showOptionsPanel)
    {
        PrepareEditorLayout();
        SetRootVisibility(true);
        ApplyPanelState(
            showOptionsPanel ? MenuPanel.Options : MenuPanel.Main,
            defaultSelection: null,
            selectDefault: false);
        MarkEditorLayoutDirty();
    }

    public void HideLayoutPreviewInEditor()
    {
        PrepareEditorLayout();
        SetClosedVisualStateImmediate();
        MarkEditorLayoutDirty();
    }

    private void PrepareEditorLayout()
    {
        ResolveReferences();
        LoadSettings();
        EnsureLayout();
        RefreshDisplayedCopy();
    }

    private void MarkEditorLayoutDirty()
    {
        EditorUtility.SetDirty(this);

        if (pauseMenuRoot != null)
            EditorUtility.SetDirty(pauseMenuRoot);

        EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    [ContextMenu("Build Pause Menu Layout")]
    private void BuildPauseMenuLayout()
    {
        BuildLayoutForEditorPreview(showOptionsPanel: false);
    }

    [ContextMenu("Preview Pause Menu/Main Panel")]
    private void PreviewMainPanelInEditor()
    {
        BuildLayoutForEditorPreview(showOptionsPanel: false);
    }

    [ContextMenu("Preview Pause Menu/Options Panel")]
    private void PreviewOptionsPanelInEditor()
    {
        BuildLayoutForEditorPreview(showOptionsPanel: true);
    }

    [ContextMenu("Preview Pause Menu/Hide Preview")]
    private void HidePauseMenuPreviewInEditor()
    {
        HideLayoutPreviewInEditor();
    }
#endif
}
