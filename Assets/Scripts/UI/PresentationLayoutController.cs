using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class PresentationLayoutController : MonoBehaviour
{
    private const string PresentationRootName = "PresentationFrame";
    private static readonly Vector2 DefaultReferenceResolution = new(1920f, 1080f);

    [Header("Reference")]
    [SerializeField] private Vector2 referenceResolution = DefaultReferenceResolution;

    [Header("Runtime References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private RectTransform presentationRoot;

    private readonly List<Transform> reparentBuffer = new();
    private bool isApplyingLayout;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    private void Awake()
    {
        ApplyLayout();
    }

    private void OnEnable()
    {
        ApplyLayout();
    }

    private void LateUpdate()
    {
        if (LayoutNeedsRefresh())
            ApplyLayout();
    }

    private void OnTransformChildrenChanged()
    {
        if (!isApplyingLayout)
            ApplyLayout();
    }

    public void ForceRefresh()
    {
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        ApplyLayout();
    }

    private bool LayoutNeedsRefresh()
    {
        return Screen.width != lastScreenWidth ||
               Screen.height != lastScreenHeight ||
               targetCanvas == null ||
               presentationRoot == null ||
               (targetCamera == null && Camera.main != null);
    }

    private void ApplyLayout()
    {
        if (isApplyingLayout)
            return;

        isApplyingLayout = true;

        try
        {
            ResolveReferences();

            if (targetCanvas == null || referenceResolution.x <= 0f || referenceResolution.y <= 0f)
                return;

            ConfigureCanvasScaler();
            EnsurePresentationRoot();
            ReparentDirectChildren();
            UpdatePresentationRootTransform();
            ApplyCameraViewport();

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }
        finally
        {
            isApplyingLayout = false;
        }
    }

    private void ResolveReferences()
    {
        targetCanvas ??= GetComponent<Canvas>();
        canvasScaler ??= GetComponent<CanvasScaler>();

        if (presentationRoot == null)
            presentationRoot = transform.Find(PresentationRootName) as RectTransform;

        if (targetCamera == null)
        {
            targetCamera = targetCanvas != null && targetCanvas.worldCamera != null
                ? targetCanvas.worldCamera
                : Camera.main;

            if (targetCamera == null)
                targetCamera = FindFirstObjectByType<Camera>();
        }
    }

    private void ConfigureCanvasScaler()
    {
        if (canvasScaler == null)
            return;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasScaler.scaleFactor = 1f;
    }

    private void EnsurePresentationRoot()
    {
        if (presentationRoot != null)
            return;

        GameObject rootObject = new(PresentationRootName, typeof(RectTransform));
        rootObject.layer = gameObject.layer;

        presentationRoot = rootObject.GetComponent<RectTransform>();
        presentationRoot.SetParent(transform, false);
        presentationRoot.SetSiblingIndex(0);
    }

    private void ReparentDirectChildren()
    {
        if (presentationRoot == null)
            return;

        reparentBuffer.Clear();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (child == presentationRoot || ShouldIgnoreChild(child))
                continue;

            reparentBuffer.Add(child);
        }

        for (int i = 0; i < reparentBuffer.Count; i++)
        {
            Transform child = reparentBuffer[i];

            if (child != null && child.parent != presentationRoot)
                child.SetParent(presentationRoot, false);
        }
    }

    private static bool ShouldIgnoreChild(Transform child)
    {
        return child.name == "CursorVisual";
    }

    private void UpdatePresentationRootTransform()
    {
        RectTransform canvasRect = targetCanvas.transform as RectTransform;
        if (canvasRect == null || presentationRoot == null)
            return;

        Rect rect = canvasRect.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        float scale = Mathf.Min(rect.width / referenceResolution.x, rect.height / referenceResolution.y);

        presentationRoot.anchorMin = new Vector2(0.5f, 0.5f);
        presentationRoot.anchorMax = new Vector2(0.5f, 0.5f);
        presentationRoot.pivot = new Vector2(0.5f, 0.5f);
        presentationRoot.anchoredPosition = Vector2.zero;
        presentationRoot.sizeDelta = referenceResolution;
        presentationRoot.localScale = new Vector3(scale, scale, 1f);
        presentationRoot.localRotation = Quaternion.identity;
    }

    private void ApplyCameraViewport()
    {
        if (targetCamera == null || Screen.height <= 0)
            return;

        float targetAspect = referenceResolution.x / referenceResolution.y;
        float screenAspect = Screen.width / (float)Screen.height;
        Rect viewport = new(0f, 0f, 1f, 1f);

        if (screenAspect > targetAspect)
        {
            viewport.width = targetAspect / screenAspect;
            viewport.x = (1f - viewport.width) * 0.5f;
        }
        else if (screenAspect < targetAspect)
        {
            viewport.height = screenAspect / targetAspect;
            viewport.y = (1f - viewport.height) * 0.5f;
        }

        if (targetCamera.rect != viewport)
            targetCamera.rect = viewport;
    }
}

public static class PresentationLayoutBootstrap
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
        InstallControllers();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallControllers();
    }

    private static void InstallControllers()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (!ShouldManageCanvas(canvas))
                continue;

            if (!canvas.TryGetComponent(out PresentationLayoutController controller))
                controller = canvas.gameObject.AddComponent<PresentationLayoutController>();

            controller.ForceRefresh();
        }
    }

    private static bool ShouldManageCanvas(Canvas canvas)
    {
        return canvas != null &&
               canvas.isRootCanvas &&
               canvas.renderMode != RenderMode.WorldSpace;
    }
}
