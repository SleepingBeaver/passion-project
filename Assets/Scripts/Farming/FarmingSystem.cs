using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
[RequireComponent(typeof(Tilemap))]
public class FarmingSystem : MonoBehaviour
{
    // Tipos de dominio compartilhados com o PlayerInteractor e com testes do Editor.
    public enum FarmingAction
    {
        None = 0,
        Till = 1,
        Water = 2,
        Plant = 3,
        Harvest = 4
    }

    public readonly struct FarmingInteraction
    {
        public FarmingInteraction(Vector3Int cell, FarmingAction action, string promptText)
        {
            Cell = cell;
            Action = action;
            PromptText = promptText;
        }

        public Vector3Int Cell { get; }
        public FarmingAction Action { get; }
        public string PromptText { get; }
        public bool IsValid => Action != FarmingAction.None;
    }

    public readonly struct PlotSnapshot
    {
        public PlotSnapshot(bool isTilled, bool isWatered, CropDefinition crop, int growthStage, int harvestRemaining)
        {
            IsTilled = isTilled;
            IsWatered = isWatered;
            Crop = crop;
            GrowthStage = growthStage;
            HarvestRemaining = harvestRemaining;
        }

        public bool IsTilled { get; }
        public bool IsWatered { get; }
        public CropDefinition Crop { get; }
        public int GrowthStage { get; }
        public int HarvestRemaining { get; }
        public bool HasCrop => Crop != null;
        public bool IsHarvestReady => Crop != null && GrowthStage >= Crop.MatureStageIndex && HarvestRemaining > 0;
    }

    private sealed class PlotState
    {
        public bool IsTilled;
        public bool IsWatered;
        public CropDefinition Crop;
        public int GrowthStage;
        public int HarvestRemaining;
        public SpriteRenderer CropRenderer;
    }

    // Textos e IDs ficam centralizados para manter teclado, mouse e inventario coerentes.
    private const string HoeToolId = "hoe_tool";
    private const string WateringToolId = "watering_tool";
    private const string TillPrompt = "E ou clique para arar o solo";
    private const string WaterPrompt = "E ou clique para regar";
    private const string PlantPrompt = "E ou clique para plantar tomate";
    private const string HarvestPrompt = "E para colher tomate";

    // Dependencias da cena e parametros visuais editaveis no Inspector.
    [Header("References")]
    [SerializeField] private Tilemap soilTilemap;
    [SerializeField] private GameplayDayCycleController dayCycleController;
    [SerializeField] private CropDefinition tomatoCrop;

    [Header("Soil Detection")]
    [SerializeField] private string soilSpriteNameToken = "Soil";

    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float mouseInteractionRadius = 1.25f;

    [Header("Soil Filters")]
    [SerializeField] private Color tilledSoilFilter = new(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color wateredSoilFilter = new(0.64f, 0.64f, 0.64f, 1f);

    [Header("Crop Visual")]
    [SerializeField] private Transform cropVisualRoot;
    [SerializeField] private Vector2 cropVisualOffset = new(0f, 0.35f);
    [SerializeField] private string cropSortingLayerName = "Characters";
    [SerializeField] private int cropSortingOrder;

    [Header("Target Outline")]
    [SerializeField] private Color targetOutlineColor = new(1f, 1f, 1f, 0.72f);
    [SerializeField, Range(1, 2)] private int targetOutlineThickness = 2;
    [SerializeField] private string targetOutlineSortingLayerName = "Ground";
    [SerializeField] private int targetOutlineSortingOrder = 1;

    // Caches de runtime: o HashSet torna a validacao do solo O(1) e o dicionario guarda apenas plots alterados.
    private readonly HashSet<Vector3Int> soilCells = new();
    private readonly Dictionary<Vector3Int, PlotState> plots = new();
    private GameplayDayCycleController subscribedDayCycle;
    private SpriteRenderer targetOutlineRenderer;
    private Texture2D targetOutlineTexture;
    private Sprite targetOutlineSprite;
    private Vector3Int highlightedCell;
    private bool hasHighlightedCell;

    // Estado de leitura exposto para interacao, UI e validacao sem liberar mutacoes internas.
    public Tilemap SoilTilemap => soilTilemap;
    public CropDefinition TomatoCrop => tomatoCrop;
    public int SoilCellCount => soilCells.Count;
    public int ModifiedPlotCount => plots.Count;
    public float MouseInteractionRadius => mouseInteractionRadius;
    public bool IsTargetOutlineVisible => targetOutlineRenderer != null && targetOutlineRenderer.enabled;
    public Vector3Int HighlightedCell => highlightedCell;

    // Ciclo de vida, cache do Tilemap e assinatura na troca de dia.
    private void Reset()
    {
        soilTilemap = GetComponent<Tilemap>();
    }

    private void Awake()
    {
        ResolveReferences();
        RebuildSoilCache();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToDayCycle();

        if (soilCells.Count == 0)
            RebuildSoilCache();
    }

    private void OnDisable()
    {
        UnsubscribeFromDayCycle();
        HideTargetOutline();
    }

    private void OnDestroy()
    {
        ReleaseTargetOutlineResources();
    }

    private void OnValidate()
    {
        soilTilemap ??= GetComponent<Tilemap>();
        mouseInteractionRadius = Mathf.Max(0.1f, mouseInteractionRadius);
        targetOutlineThickness = Mathf.Clamp(targetOutlineThickness, 1, 2);
    }

    // Indexacao e consultas dos tiles que aceitam cultivo.
    public void RebuildSoilCache()
    {
        soilCells.Clear();

        if (soilTilemap == null)
            return;

        BoundsInt bounds = soilTilemap.cellBounds;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            Sprite sprite = soilTilemap.GetSprite(cell);
            if (sprite != null && IsSoilSpriteName(sprite.name))
                soilCells.Add(cell);
        }
    }

    public bool IsSoilCell(Vector3Int cell)
    {
        return soilCells.Contains(cell);
    }

    public void ShowTargetOutline(FarmingInteraction interaction)
    {
        if (!interaction.IsValid || !UsesFarmingTargetOutline(interaction.Action) ||
            soilTilemap == null || !soilCells.Contains(interaction.Cell))
        {
            HideTargetOutline();
            return;
        }

        EnsureTargetOutline();
        if (targetOutlineRenderer == null)
            return;

        if (!hasHighlightedCell || highlightedCell != interaction.Cell)
        {
            highlightedCell = interaction.Cell;
            hasHighlightedCell = true;
            targetOutlineRenderer.transform.position = soilTilemap.GetCellCenterWorld(interaction.Cell);
        }

        targetOutlineRenderer.color = targetOutlineColor;
        targetOutlineRenderer.enabled = true;
    }

    public void HideTargetOutline()
    {
        hasHighlightedCell = false;

        if (targetOutlineRenderer != null)
            targetOutlineRenderer.enabled = false;
    }

    public bool TryGetInteraction(PlayerInteractor interactor, out FarmingInteraction interaction)
    {
        interaction = default;

        if (interactor == null || interactor.InventorySystem == null || soilTilemap == null)
            return false;

        Vector3Int cell = ResolveTargetCell(interactor);
        return TryGetInteractionForCell(interactor, cell, out interaction);
    }

    public bool IsMouseFarmingEnabled(PlayerInteractor interactor)
    {
        InventorySystem inventory = interactor != null ? interactor.InventorySystem : null;
        if (inventory == null)
            return false;

        return inventory.IsSelectedItemId(HoeToolId) ||
               inventory.IsSelectedItemId(WateringToolId) ||
               tomatoCrop != null && tomatoCrop.MatchesSeed(inventory.SelectedItem);
    }

    public bool TryGetMouseInteraction(
        PlayerInteractor interactor,
        Vector3 worldPosition,
        out FarmingInteraction interaction,
        out bool isTargetingSoilInRange)
    {
        interaction = default;
        isTargetingSoilInRange = false;

        if (interactor == null || soilTilemap == null || !IsMouseFarmingEnabled(interactor))
            return false;

        Vector3Int cell = soilTilemap.WorldToCell(worldPosition);
        if (!soilCells.Contains(cell) || !IsWithinMouseInteractionRadius(interactor, cell))
            return false;

        isTargetingSoilInRange = true;
        return TryGetInteractionForCell(interactor, cell, out interaction);
    }

    public static Vector3Int GetCellOffsetForFacing(Vector2 facing)
    {
        int horizontal = Mathf.Abs(facing.x) < 0.5f ? 0 : facing.x > 0f ? 1 : -1;
        int vertical = Mathf.Abs(facing.y) < 0.5f ? 0 : facing.y > 0f ? 1 : -1;

        if (horizontal == 0 && vertical == 0)
            vertical = -1;

        // No Grid isometrico 2:1, direita visual e o vizinho (1, -1), nao apenas +X.
        int cellX = horizontal + vertical;
        int cellY = -horizontal + vertical;

        if (horizontal != 0 && vertical != 0)
        {
            cellX /= 2;
            cellY /= 2;
        }

        return new Vector3Int(cellX, cellY, 0);
    }

    private bool TryGetInteractionForCell(
        PlayerInteractor interactor,
        Vector3Int cell,
        out FarmingInteraction interaction)
    {
        interaction = default;

        if (interactor == null || interactor.InventorySystem == null || !soilCells.Contains(cell))
            return false;

        plots.TryGetValue(cell, out PlotState plot);

        if (IsHarvestReady(plot))
        {
            interaction = new FarmingInteraction(cell, FarmingAction.Harvest, HarvestPrompt);
            return true;
        }

        InventorySystem inventory = interactor.InventorySystem;

        if (inventory.IsSelectedItemId(HoeToolId) && (plot == null || !plot.IsTilled) && !HasCrop(plot))
        {
            interaction = new FarmingInteraction(cell, FarmingAction.Till, TillPrompt);
            return true;
        }

        if (inventory.IsSelectedItemId(WateringToolId) && plot is { IsTilled: true, IsWatered: false })
        {
            interaction = new FarmingInteraction(cell, FarmingAction.Water, WaterPrompt);
            return true;
        }

        if (plot is { IsTilled: true, Crop: null } &&
            tomatoCrop != null &&
            tomatoCrop.MatchesSeed(inventory.SelectedItem))
        {
            interaction = new FarmingInteraction(cell, FarmingAction.Plant, PlantPrompt);
            return true;
        }

        return false;
    }

    // Execucao atomica das quatro acoes de cultivo.
    public bool TryPerformInteraction(PlayerInteractor interactor, FarmingInteraction interaction)
    {
        if (interactor == null || !interaction.IsValid || !soilCells.Contains(interaction.Cell))
            return false;

        return interaction.Action switch
        {
            FarmingAction.Till => TryTillCell(interaction.Cell),
            FarmingAction.Water => TryWaterCell(interaction.Cell),
            FarmingAction.Plant => TryPlantSelectedSeed(interactor, interaction.Cell),
            FarmingAction.Harvest => TryHarvestCell(interaction.Cell, interactor.InventorySystem, out _),
            _ => false
        };
    }

    public bool TryTillCell(Vector3Int cell)
    {
        if (!soilCells.Contains(cell))
            return false;

        PlotState plot = GetOrCreatePlot(cell);
        if (plot.IsTilled || plot.Crop != null)
            return false;

        plot.IsTilled = true;
        plot.IsWatered = false;
        ApplySoilFilter(cell, plot);
        return true;
    }

    public bool TryWaterCell(Vector3Int cell)
    {
        if (!plots.TryGetValue(cell, out PlotState plot) || !plot.IsTilled || plot.IsWatered)
            return false;

        plot.IsWatered = true;
        ApplySoilFilter(cell, plot);
        return true;
    }

    public bool TryPlantCell(Vector3Int cell, CropDefinition crop)
    {
        if (crop == null || !crop.IsConfigured ||
            !plots.TryGetValue(cell, out PlotState plot) ||
            !plot.IsTilled || plot.Crop != null)
        {
            return false;
        }

        plot.Crop = crop;
        plot.GrowthStage = 0;
        plot.HarvestRemaining = crop.MatureStageIndex == 0 ? crop.HarvestAmount : 0;
        UpdateCropVisual(cell, plot);
        return true;
    }

    public bool TryHarvestCell(Vector3Int cell, InventorySystem inventorySystem, out int harvestedAmount)
    {
        harvestedAmount = 0;

        if (inventorySystem == null ||
            !plots.TryGetValue(cell, out PlotState plot) ||
            !IsHarvestReady(plot) ||
            plot.Crop.HarvestItem == null)
        {
            return false;
        }

        inventorySystem.AddItem(plot.Crop.HarvestItem, plot.HarvestRemaining, out harvestedAmount);
        if (harvestedAmount <= 0)
        {
            Debug.Log("Inventario cheio. Nao foi possivel colher o tomate.");
            return false;
        }

        plot.HarvestRemaining -= harvestedAmount;
        if (plot.HarvestRemaining > 0)
            return true;

        plot.Crop = null;
        plot.GrowthStage = 0;
        plot.HarvestRemaining = 0;
        HideCropVisual(plot);
        return true;
    }

    public void AdvanceCropsOneDay()
    {
        foreach (KeyValuePair<Vector3Int, PlotState> entry in plots)
        {
            PlotState plot = entry.Value;

            if (plot.Crop != null && plot.IsWatered && plot.GrowthStage < plot.Crop.MatureStageIndex)
            {
                plot.GrowthStage++;

                if (plot.GrowthStage >= plot.Crop.MatureStageIndex)
                    plot.HarvestRemaining = plot.Crop.HarvestAmount;

                UpdateCropVisual(entry.Key, plot);
            }

            if (!plot.IsWatered)
                continue;

            plot.IsWatered = false;
            ApplySoilFilter(entry.Key, plot);
        }
    }

    public bool TryGetPlotSnapshot(Vector3Int cell, out PlotSnapshot snapshot)
    {
        if (!plots.TryGetValue(cell, out PlotState plot))
        {
            snapshot = default;
            return false;
        }

        snapshot = new PlotSnapshot(
            plot.IsTilled,
            plot.IsWatered,
            plot.Crop,
            plot.GrowthStage,
            plot.HarvestRemaining);
        return true;
    }

    // Consumo da semente com rollback caso o plantio deixe de ser valido durante a acao.
    private bool TryPlantSelectedSeed(PlayerInteractor interactor, Vector3Int cell)
    {
        InventorySystem inventory = interactor.InventorySystem;
        if (inventory == null || tomatoCrop == null || !tomatoCrop.MatchesSeed(inventory.SelectedItem))
            return false;

        if (!plots.TryGetValue(cell, out PlotState plot) || !plot.IsTilled || plot.Crop != null)
            return false;

        int selectedSlotIndex = inventory.SelectedSlotIndex;
        if (!inventory.RemoveFromSlot(selectedSlotIndex, 1, out ItemData removedItem, out int removedAmount) ||
            removedAmount != 1 ||
            !tomatoCrop.MatchesSeed(removedItem))
        {
            return false;
        }

        if (TryPlantCell(cell, tomatoCrop))
            return true;

        inventory.AddItem(removedItem, removedAmount);
        return false;
    }

    private Vector3Int ResolveTargetCell(PlayerInteractor interactor)
    {
        Vector3Int actorCell = soilTilemap.WorldToCell(interactor.FarmingGroundPosition);
        return actorCell + GetCellOffsetForFacing(interactor.FacingDirection);
    }

    private bool IsWithinMouseInteractionRadius(PlayerInteractor interactor, Vector3Int cell)
    {
        Vector2 actorPosition = interactor.FarmingGroundPosition;
        Vector2 cellCenter = soilTilemap.GetCellCenterWorld(cell);
        float radiusSqr = mouseInteractionRadius * mouseInteractionRadius;
        return (cellCenter - actorPosition).sqrMagnitude <= radiusSqr;
    }

    // Contorno procedural temporario: criado uma vez e liberado junto com o sistema.
    private void EnsureTargetOutline()
    {
        if (targetOutlineRenderer != null || soilTilemap == null)
            return;

        GameObject outlineObject = new("FarmingTargetOutline")
        {
            hideFlags = HideFlags.DontSave
        };
        outlineObject.transform.SetParent(soilTilemap.transform, false);

        targetOutlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
        targetOutlineRenderer.sprite = CreateTargetOutlineSprite();
        targetOutlineRenderer.color = targetOutlineColor;
        targetOutlineRenderer.sortingLayerName = targetOutlineSortingLayerName;
        targetOutlineRenderer.sortingOrder = targetOutlineSortingOrder;
        targetOutlineRenderer.spriteSortPoint = SpriteSortPoint.Center;
        targetOutlineRenderer.enabled = false;
    }

    private Sprite CreateTargetOutlineSprite()
    {
        const int width = 64;
        const int height = 32;

        targetOutlineTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "FarmingTargetOutlineTexture_64px",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[width * height];
        Color32 white = new(255, 255, 255, 255);
        DrawDiamond(pixels, width, height, white, targetOutlineThickness);
        targetOutlineTexture.SetPixels32(pixels);
        targetOutlineTexture.Apply(false, true);

        targetOutlineSprite = Sprite.Create(
            targetOutlineTexture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            64f,
            0,
            SpriteMeshType.FullRect);
        targetOutlineSprite.name = "FarmingTargetOutline_64px";
        targetOutlineSprite.hideFlags = HideFlags.DontSave;
        return targetOutlineSprite;
    }

    private static void DrawDiamond(
        Color32[] pixels,
        int width,
        int height,
        Color32 color,
        int thickness)
    {
        Vector2Int top = new(width / 2, height - 1);
        Vector2Int right = new(width - 1, height / 2);
        Vector2Int bottom = new(width / 2, 0);
        Vector2Int left = new(0, height / 2);

        DrawLine(pixels, width, height, top, right, color);
        DrawLine(pixels, width, height, right, bottom, color);
        DrawLine(pixels, width, height, bottom, left, color);
        DrawLine(pixels, width, height, left, top, color);

        if (thickness < 2)
            return;

        DrawLine(pixels, width, height, top + Vector2Int.down, right + Vector2Int.left, color);
        DrawLine(pixels, width, height, right + Vector2Int.left, bottom + Vector2Int.up, color);
        DrawLine(pixels, width, height, bottom + Vector2Int.up, left + Vector2Int.right, color);
        DrawLine(pixels, width, height, left + Vector2Int.right, top + Vector2Int.down, color);
    }

    private static void DrawLine(
        Color32[] pixels,
        int width,
        int height,
        Vector2Int start,
        Vector2Int end,
        Color32 color)
    {
        int x = start.x;
        int y = start.y;
        int deltaX = Mathf.Abs(end.x - start.x);
        int stepX = start.x < end.x ? 1 : -1;
        int deltaY = -Mathf.Abs(end.y - start.y);
        int stepY = start.y < end.y ? 1 : -1;
        int error = deltaX + deltaY;

        while (true)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
                pixels[y * width + x] = color;

            if (x == end.x && y == end.y)
                break;

            int doubledError = error * 2;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }
    }

    private void ReleaseTargetOutlineResources()
    {
        if (targetOutlineSprite != null)
            DestroyGeneratedResource(targetOutlineSprite);

        if (targetOutlineTexture != null)
            DestroyGeneratedResource(targetOutlineTexture);

        targetOutlineSprite = null;
        targetOutlineTexture = null;
        targetOutlineRenderer = null;
    }

    private static void DestroyGeneratedResource(UnityEngine.Object resource)
    {
        if (Application.isPlaying)
            Destroy(resource);
        else
            DestroyImmediate(resource);
    }

    private static bool UsesFarmingTargetOutline(FarmingAction action)
    {
        return action is FarmingAction.Till or FarmingAction.Water or FarmingAction.Plant;
    }

    // Persistencia em memoria e representacao visual dos plots modificados.
    private PlotState GetOrCreatePlot(Vector3Int cell)
    {
        if (plots.TryGetValue(cell, out PlotState plot))
            return plot;

        plot = new PlotState();
        plots.Add(cell, plot);
        return plot;
    }

    private void ApplySoilFilter(Vector3Int cell, PlotState plot)
    {
        if (soilTilemap == null || plot == null || !plot.IsTilled)
            return;

        soilTilemap.SetTileFlags(cell, TileFlags.None);
        soilTilemap.SetColor(cell, plot.IsWatered ? wateredSoilFilter : tilledSoilFilter);
    }

    private void UpdateCropVisual(Vector3Int cell, PlotState plot)
    {
        if (plot?.Crop == null || soilTilemap == null)
            return;

        if (plot.CropRenderer == null)
            plot.CropRenderer = CreateCropRenderer(cell);

        plot.CropRenderer.sprite = plot.Crop.GetStageSprite(plot.GrowthStage);
        plot.CropRenderer.enabled = plot.CropRenderer.sprite != null;
    }

    private SpriteRenderer CreateCropRenderer(Vector3Int cell)
    {
        GameObject cropObject = new($"Crop_{cell.x}_{cell.y}");
        Transform parent = cropVisualRoot != null ? cropVisualRoot : transform;
        cropObject.transform.SetParent(parent, true);
        cropObject.transform.position = soilTilemap.GetCellCenterWorld(cell) + (Vector3)cropVisualOffset;

        SpriteRenderer spriteRenderer = cropObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingLayerName = cropSortingLayerName;
        spriteRenderer.sortingOrder = cropSortingOrder;
        spriteRenderer.spriteSortPoint = SpriteSortPoint.Pivot;
        return spriteRenderer;
    }

    private static void HideCropVisual(PlotState plot)
    {
        if (plot?.CropRenderer == null)
            return;

        plot.CropRenderer.sprite = null;
        plot.CropRenderer.enabled = false;
    }

    private static bool HasCrop(PlotState plot)
    {
        return plot?.Crop != null;
    }

    private static bool IsHarvestReady(PlotState plot)
    {
        return plot?.Crop != null &&
               plot.GrowthStage >= plot.Crop.MatureStageIndex &&
               plot.HarvestRemaining > 0;
    }

    private bool IsSoilSpriteName(string spriteName)
    {
        return !string.IsNullOrWhiteSpace(spriteName) &&
               !string.IsNullOrWhiteSpace(soilSpriteNameToken) &&
               spriteName.IndexOf(soilSpriteNameToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Integracao com o ciclo do dia e resolucao tardia de referencias da cena.
    private void HandleNewDayStarted(GameplayDayCycleController.DayTransition _)
    {
        AdvanceCropsOneDay();
    }

    private void ResolveReferences()
    {
        soilTilemap ??= GetComponent<Tilemap>();

        if (dayCycleController == null)
            dayCycleController = FindAnyObjectByType<GameplayDayCycleController>();
    }

    private void SubscribeToDayCycle()
    {
        if (subscribedDayCycle == dayCycleController)
            return;

        UnsubscribeFromDayCycle();
        subscribedDayCycle = dayCycleController;

        if (subscribedDayCycle != null)
            subscribedDayCycle.NewDayStarted += HandleNewDayStarted;
    }

    private void UnsubscribeFromDayCycle()
    {
        if (subscribedDayCycle != null)
            subscribedDayCycle.NewDayStarted -= HandleNewDayStarted;

        subscribedDayCycle = null;
    }
}
