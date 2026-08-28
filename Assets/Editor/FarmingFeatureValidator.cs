using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class FarmingFeatureValidator
{
    // Caminhos e dimensoes que formam o contrato de assets da funcionalidade.
    private const string DevelopmentScenePath = "Assets/Scenes/SampleScene.unity";
    private const string TomatoCropPath = "Assets/ScriptableObjects/Crops/Tomato.asset";
    private const string TomatoSeedPath = "Assets/Prefabs/Items/TomatoSeed.asset";
    private const string TomatoItemPath = "Assets/Prefabs/Items/TomatoCrop.asset";

    private static readonly Dictionary<string, Vector2Int> MigratedTextureDimensions = new()
    {
        { "Assets/Art/Tiles/Tilemap1.png", new Vector2Int(256, 192) },
        { "Assets/Art/Player/Idle/Model Sheet 1.png", new Vector2Int(354, 88) },
        { "Assets/Art/Player/Walk/Model Sheet.png", new Vector2Int(320, 768) },
        { "Assets/Art/Sprites/Model Sheet 1.png", new Vector2Int(354, 88) },
        { "Assets/Art/World/House_1.png", new Vector2Int(362, 330) },
        { "Assets/Art/Items/Flower.png", new Vector2Int(64, 64) },
        { "Assets/Art/Items/Log.png", new Vector2Int(64, 64) }
    };

    // Entradas manual e de CI executam a mesma sequencia de verificacoes.
    [MenuItem("Tools/Farming/Validate Feature", priority = 130)]
    private static void ValidateFromMenu()
    {
        int failures = Validate();
        EditorUtility.DisplayDialog(
            "Farming Validation",
            failures == 0
                ? "Sistema de plantacao validado com sucesso."
                : $"A validacao encontrou {failures} falha(s). Consulte o Console.",
            "OK");
    }

    public static void ValidateCommandLine()
    {
        EditorApplication.Exit(Validate() == 0 ? 0 : 1);
    }

    private static int Validate()
    {
        int failures = 0;
        Debug.Log("[FARMING-VALIDATION] Starting validation.");

        EditorSceneManager.OpenScene(DevelopmentScenePath, OpenSceneMode.Single);
        Validate64PixelPipeline(ref failures);
        ValidateSceneConfiguration(ref failures);
        ValidateFunctionalFlow(ref failures);

        Debug.Log($"[FARMING-VALIDATION] Completed with {failures} failure(s).");
        return failures;
    }

    // Auditoria do pipeline de pixel art: PPU, filtro, compressao, slices e dimensoes.
    private static void Validate64PixelPipeline(ref int failures)
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" });
        int validatedSpriteTextures = 0;

        for (int i = 0; i < textureGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer ||
                importer.textureType != TextureImporterType.Sprite)
            {
                continue;
            }

            validatedSpriteTextures++;
            Check(Mathf.Approximately(importer.spritePixelsPerUnit, 64f),
                $"{path} uses the 64 PPU project standard.", ref failures);
            Check(importer.filterMode == FilterMode.Point,
                $"{path} uses Point filtering.", ref failures);
            Check(!importer.mipmapEnabled,
                $"{path} has mipmaps disabled.", ref failures);
            Check(importer.textureCompression == TextureImporterCompression.Uncompressed,
                $"{path} uses uncompressed pixel-art import.", ref failures);
        }

        Check(validatedSpriteTextures > 0,
            $"Validated {validatedSpriteTextures} project sprite textures.", ref failures);

        foreach (KeyValuePair<string, Vector2Int> entry in MigratedTextureDimensions)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Key);
            Check(texture != null && texture.width == entry.Value.x && texture.height == entry.Value.y,
                $"{entry.Key} has the expected deterministic 2x source dimensions.", ref failures);

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(entry.Key);
            bool foundSprite = false;
            bool allRectsUseDoubledCoordinates = true;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not Sprite sprite)
                    continue;

                foundSprite = true;
                Rect rect = sprite.rect;
                allRectsUseDoubledCoordinates &=
                    IsEvenPixel(rect.x) && IsEvenPixel(rect.y) &&
                    IsEvenPixel(rect.width) && IsEvenPixel(rect.height);
            }

            Check(foundSprite && allRectsUseDoubledCoordinates,
                $"{entry.Key} preserves its sliced sprite IDs with doubled pixel rects.", ref failures);
        }

        Texture2D tileTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Tiles/Tilemap1.png");
        UnityEngine.Object[] tileAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Tiles/Tilemap1.png");
        int tileSpriteCount = 0;
        bool tileRectsAre64By32 = tileTexture != null;
        for (int i = 0; i < tileAssets.Length; i++)
        {
            if (tileAssets[i] is not Sprite tileSprite)
                continue;

            tileSpriteCount++;
            tileRectsAre64By32 &=
                Mathf.Approximately(tileSprite.rect.width, 64f) &&
                Mathf.Approximately(tileSprite.rect.height, 32f);
        }

        Check(tileSpriteCount == 22 && tileRectsAre64By32,
            "Isometric tiles keep 22 references and use 64x32 source rects.", ref failures);

        Grid grid = UnityEngine.Object.FindAnyObjectByType<Grid>(FindObjectsInactive.Include);
        Check(grid != null && grid.cellLayout == GridLayout.CellLayout.IsometricZAsY &&
              Approximately(grid.cellSize, new Vector3(1f, 0.5f, 1f)),
            "Grid remains isometric Z-as-Y at 1x0.5 world units after source migration.", ref failures);

        Camera mainCamera = Camera.main;
        Check(mainCamera != null && mainCamera.orthographic && Mathf.Approximately(mainCamera.orthographicSize, 5f),
            "Orthographic camera framing remains unchanged at size 5.", ref failures);
    }

    // Verificacao das referencias serializadas e dos objetos obrigatorios na cena.
    private static void ValidateSceneConfiguration(ref int failures)
    {
        FarmingSystem[] farmingSystems =
            UnityEngine.Object.FindObjectsByType<FarmingSystem>(FindObjectsInactive.Include);
        Check(farmingSystems.Length == 1,
            "Scene has exactly one tilemap farming system.", ref failures);

        CropDefinition tomatoCrop = AssetDatabase.LoadAssetAtPath<CropDefinition>(TomatoCropPath);
        ItemData tomatoSeed = AssetDatabase.LoadAssetAtPath<ItemData>(TomatoSeedPath);
        ItemData tomatoItem = AssetDatabase.LoadAssetAtPath<ItemData>(TomatoItemPath);

        Check(tomatoCrop != null && tomatoCrop.IsConfigured,
            "Tomato crop definition links seed, harvest item, and growth sprites.", ref failures);
        Check(tomatoCrop != null && tomatoCrop.StageCount == 6,
            "Tomato uses all six named visual growth stages.", ref failures);
        Check(tomatoSeed != null && tomatoSeed.icon != null && tomatoSeed.itemId == "tomato_seed",
            "Tomato seed is a configured inventory item.", ref failures);
        Check(tomatoItem != null && tomatoItem.icon != null && tomatoItem.itemId == "tomato",
            "Harvested tomato is a configured collectible item.", ref failures);

        Check(
            FarmingSystem.GetCellOffsetForFacing(Vector2.right) == new Vector3Int(1, -1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(Vector2.left) == new Vector3Int(-1, 1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(Vector2.up) == new Vector3Int(1, 1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(Vector2.down) == new Vector3Int(-1, -1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(new Vector2(1f, 1f)) == new Vector3Int(1, 0, 0) &&
            FarmingSystem.GetCellOffsetForFacing(new Vector2(-1f, 1f)) == new Vector3Int(0, 1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(new Vector2(1f, -1f)) == new Vector3Int(0, -1, 0) &&
            FarmingSystem.GetCellOffsetForFacing(new Vector2(-1f, -1f)) == new Vector3Int(-1, 0, 0),
            "All eight visual facing directions map to the correct isometric cell.", ref failures);

        if (farmingSystems.Length == 1)
        {
            FarmingSystem farming = farmingSystems[0];
            farming.RebuildSoilCache();
            Check(farming.SoilCellCount > 0,
                $"Soil sprite cache contains {farming.SoilCellCount} cells.", ref failures);
            Check(farming.TomatoCrop == tomatoCrop,
                "Scene farming system references the tomato definition.", ref failures);

            bool grassWasFound = false;
            bool grassWasAcceptedAsSoil = false;
            foreach (Vector3Int cell in farming.SoilTilemap.cellBounds.allPositionsWithin)
            {
                Sprite sprite = farming.SoilTilemap.GetSprite(cell);
                if (sprite == null || !string.Equals(sprite.name, "Grass", StringComparison.OrdinalIgnoreCase))
                    continue;

                grassWasFound = true;
                grassWasAcceptedAsSoil |= farming.IsSoilCell(cell);
            }

            Check(grassWasFound && !grassWasAcceptedAsSoil,
                "Grass is excluded while sprites named Soil are farmable.", ref failures);
        }

        PlayerInteractor player =
            UnityEngine.Object.FindAnyObjectByType<PlayerInteractor>(FindObjectsInactive.Include);
        Check(HasReference(player, "farmingSystem"),
            "Player interaction is linked to the farming system.", ref failures);
        Check(HasReference(player, "worldCamera") && HasReference(player, "eventSystem"),
            "Mouse farming uses cached camera and UI event-system references.", ref failures);
        Check(HasReference(player, "farmingGroundCollider") && HasReference(player, "interactionDetector"),
            "Player targeting uses the feet collider and keeps a cached interaction detector.", ref failures);
        Check(
            typeof(IPlayerInteractionSource).IsInterface &&
            typeof(PlayerInteractionSourceBehaviour).IsAbstract &&
            typeof(IPlayerInteractionSource).IsAssignableFrom(typeof(PlayerInteractionSourceBehaviour)) &&
            typeof(PlayerInteractor).GetMethod(nameof(PlayerInteractor.RegisterInteractionSource)) != null &&
            typeof(PlayerInteractor).GetMethod(nameof(PlayerInteractor.UnregisterInteractionSource)) != null,
            "Interaction sources use a public extensible contract and runtime registration.", ref failures);
        Check(
            typeof(PlayerInteractor).GetEvent(nameof(PlayerInteractor.InteractionPerformed)) != null,
            "All interaction types publish through the generic typed interaction event.", ref failures);

        InventoryDebugInput debugInput =
            UnityEngine.Object.FindAnyObjectByType<InventoryDebugInput>(FindObjectsInactive.Include);
        Check(HasReference(debugInput, "tomatoSeedItem"),
            "Debug input can add tomato seeds with the T key.", ref failures);
    }

    // Fluxo completo em memoria: arar, regar, plantar, crescer e colher.
    private static void ValidateFunctionalFlow(ref int failures)
    {
        GameObject root = new("FarmingValidation_Temporary");
        root.SetActive(false);

        Texture2D texture = null;
        Sprite sprite = null;
        Tile soilTile = null;
        CropDefinition crop = null;
        ItemData hoe = null;
        ItemData wateringCan = null;
        ItemData seed = null;
        ItemData tomato = null;
        ItemData axe = null;
        FarmingSystem farming = null;
        InventorySystem inventory = null;
        PlayerInteractor player = null;

        try
        {
            Grid grid = root.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.IsometricZAsY;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            GameObject tilemapObject = new("ValidationSoilTilemap");
            tilemapObject.transform.SetParent(root.transform, false);
            Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();

            texture = new Texture2D(2, 2);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            sprite.name = "Soil_Validation";
            soilTile = ScriptableObject.CreateInstance<Tile>();
            soilTile.sprite = sprite;
            Vector3Int targetCell = Vector3Int.zero;
            Vector3Int farCell = new(4, 4, 0);
            tilemap.SetTile(targetCell, soilTile);
            tilemap.SetTile(farCell, soilTile);

            hoe = CreateItem("hoe_tool", "Enxada");
            wateringCan = CreateItem("watering_tool", "Regador");
            seed = CreateItem("tomato_seed", "Sementes de tomate");
            tomato = CreateItem("tomato", "Tomate");
            axe = CreateItem("axe_tool", "Machado");
            crop = CreateCrop(seed, tomato, sprite);

            farming = tilemapObject.AddComponent<FarmingSystem>();
            SerializedObject farmingObject = new(farming);
            farmingObject.FindProperty("soilTilemap").objectReferenceValue = tilemap;
            farmingObject.FindProperty("tomatoCrop").objectReferenceValue = crop;
            farmingObject.ApplyModifiedPropertiesWithoutUndo();

            GameObject inventoryObject = new("ValidationInventory");
            inventoryObject.transform.SetParent(root.transform, false);
            inventory = inventoryObject.AddComponent<InventorySystem>();

            GameObject playerObject = new("ValidationPlayer");
            playerObject.transform.SetParent(root.transform, false);
            playerObject.AddComponent<Rigidbody2D>();
            BoxCollider2D groundCollider = playerObject.AddComponent<BoxCollider2D>();
            groundCollider.offset = new Vector2(0f, -0.55f);
            groundCollider.size = new Vector2(0.6f, 0.2f);

            GameObject detectorObject = new("ValidationInteractionDetector");
            detectorObject.transform.SetParent(playerObject.transform, false);
            CircleCollider2D interactionDetector = detectorObject.AddComponent<CircleCollider2D>();
            interactionDetector.isTrigger = true;
            interactionDetector.radius = 0.8f;

            player = playerObject.AddComponent<PlayerInteractor>();
            SerializedObject playerObjectData = new(player);
            playerObjectData.FindProperty("inventorySystem").objectReferenceValue = inventory;
            playerObjectData.FindProperty("farmingSystem").objectReferenceValue = farming;
            playerObjectData.FindProperty("farmingGroundCollider").objectReferenceValue = groundCollider;
            playerObjectData.FindProperty("interactionDetector").objectReferenceValue = interactionDetector;
            playerObjectData.ApplyModifiedPropertiesWithoutUndo();

            InvokeLifecycle(inventory, "Awake");
            InvokeLifecycle(farming, "Awake");
            InvokeLifecycle(player, "Awake");

            Vector3 targetCenter = tilemap.GetCellCenterWorld(targetCell);
            Vector3Int playerCell = new(1, 1, 0);
            Vector3 playerGroundCenter = tilemap.GetCellCenterWorld(playerCell);
            playerObject.transform.position = playerGroundCenter - (Vector3)groundCollider.offset;
            Check(Approximately(player.FarmingGroundPosition, playerGroundCenter),
                "Keyboard farming targeting is anchored at the player's feet collider.", ref failures);

            inventory.AddItem(hoe);
            inventory.AddItem(wateringCan);
            inventory.AddItem(seed, 2);
            inventory.AddItem(axe);

            inventory.SelectSlot(0);
            Check(farming.TryGetInteraction(player, out FarmingSystem.FarmingInteraction hoeTarget) &&
                  hoeTarget.Action == FarmingSystem.FarmingAction.Till,
                "Hoe resolves a valid farming target before the action.", ref failures);
            Check(farming.TryGetMouseInteraction(
                      player,
                      targetCenter,
                      out FarmingSystem.FarmingInteraction mouseHoeTarget,
                      out bool mouseTargetsSoil) &&
                  mouseTargetsSoil && mouseHoeTarget.Cell == hoeTarget.Cell &&
                  mouseHoeTarget.Action == FarmingSystem.FarmingAction.Till,
                "Mouse hover resolves the same valid hoe target inside the action radius.", ref failures);
            Check(!farming.TryGetMouseInteraction(
                      player,
                      tilemap.GetCellCenterWorld(farCell),
                      out _,
                      out bool farSoilInRange) && !farSoilInRange,
                "Mouse farming rejects soil outside the short action radius.", ref failures);
            farming.ShowTargetOutline(mouseHoeTarget);
            Check(farming.IsTargetOutlineVisible && farming.HighlightedCell == mouseHoeTarget.Cell,
                "Hoe outline reuses the exact mouse/gameplay target cell.", ref failures);
            Check(farming.TryPerformInteraction(player, mouseHoeTarget),
                "Mouse-resolved hoe interaction tills a cell whose sprite is named Soil.", ref failures);
            Check(farming.TryGetPlotSnapshot(targetCell, out FarmingSystem.PlotSnapshot tilled) && tilled.IsTilled,
                "Tilled soil state is stored only after hoe use.", ref failures);
            Check(Approximately(tilemap.GetColor(targetCell), new Color(0.82f, 0.82f, 0.82f, 1f)),
                "Tilled soil receives the lighter dark filter.", ref failures);

            inventory.SelectSlot(1);
            Check(farming.TryGetInteraction(player, out FarmingSystem.FarmingInteraction waterTarget) &&
                  waterTarget.Action == FarmingSystem.FarmingAction.Water,
                "Watering can resolves a valid farming target before the action.", ref failures);
            Check(farming.TryGetMouseInteraction(
                      player,
                      targetCenter,
                      out FarmingSystem.FarmingInteraction mouseWaterTarget,
                      out mouseTargetsSoil) &&
                  mouseTargetsSoil && mouseWaterTarget.Cell == waterTarget.Cell &&
                  mouseWaterTarget.Action == FarmingSystem.FarmingAction.Water,
                "Mouse hover resolves the same valid watering target.", ref failures);
            farming.ShowTargetOutline(mouseWaterTarget);
            Check(farming.IsTargetOutlineVisible && farming.HighlightedCell == mouseWaterTarget.Cell,
                "Watering outline reuses the exact mouse/gameplay target cell.", ref failures);
            Check(farming.TryPerformInteraction(player, mouseWaterTarget),
                "Mouse-resolved watering interaction waters tilled soil.", ref failures);
            Check(Approximately(tilemap.GetColor(targetCell), new Color(0.64f, 0.64f, 0.64f, 1f)),
                "Watered soil receives the darker filter.", ref failures);

            inventory.SelectSlot(2);
            Check(farming.TryGetInteraction(player, out FarmingSystem.FarmingInteraction seedTarget) &&
                  seedTarget.Action == FarmingSystem.FarmingAction.Plant,
                "Seed bag resolves a valid tilled-soil target before planting.", ref failures);
            Check(farming.TryGetMouseInteraction(
                      player,
                      targetCenter,
                      out FarmingSystem.FarmingInteraction mouseSeedTarget,
                      out mouseTargetsSoil) &&
                  mouseTargetsSoil && mouseSeedTarget.Cell == seedTarget.Cell &&
                  mouseSeedTarget.Action == FarmingSystem.FarmingAction.Plant,
                "Mouse hover resolves the same valid seed target.", ref failures);
            farming.ShowTargetOutline(mouseSeedTarget);
            Check(farming.IsTargetOutlineVisible && farming.HighlightedCell == mouseSeedTarget.Cell,
                "Seed outline reuses the exact mouse/gameplay target cell.", ref failures);
            Check(farming.TryPerformInteraction(player, mouseSeedTarget),
                "Mouse-resolved seed interaction plants only on tilled soil.", ref failures);
            Check(inventory.CountItem(seed) == 1,
                "Planting consumes exactly one tomato seed.", ref failures);

            farming.AdvanceCropsOneDay();
            farming.TryGetPlotSnapshot(targetCell, out FarmingSystem.PlotSnapshot firstGrowth);
            Check(firstGrowth.GrowthStage == 1 && !firstGrowth.IsWatered,
                "A watered crop advances once and the soil dries next day.", ref failures);

            farming.AdvanceCropsOneDay();
            farming.TryGetPlotSnapshot(targetCell, out FarmingSystem.PlotSnapshot dryDay);
            Check(dryDay.GrowthStage == firstGrowth.GrowthStage,
                "An unwatered crop does not grow.", ref failures);

            while (dryDay.GrowthStage < crop.MatureStageIndex)
            {
                farming.TryWaterCell(targetCell);
                farming.AdvanceCropsOneDay();
                farming.TryGetPlotSnapshot(targetCell, out dryDay);
            }

            Check(dryDay.IsHarvestReady,
                "Tomato becomes harvestable after every configured growth stage.", ref failures);
            Check(farming.TryGetInteraction(player, out FarmingSystem.FarmingInteraction harvestTarget) &&
                  harvestTarget.Action == FarmingSystem.FarmingAction.Harvest,
                "Mature crop resolves the harvest action.", ref failures);
            farming.ShowTargetOutline(harvestTarget);
            Check(!farming.IsTargetOutlineVisible,
                "Target outline remains hidden for harvest and unrelated active items.", ref failures);
            Check(TryPerformExpectedAction(farming, player, FarmingSystem.FarmingAction.Harvest),
                "Mature tomato can be collected through the player interaction.", ref failures);
            Check(inventory.CountItem(tomato) == 1,
                "Harvest adds the tomato item to the inventory.", ref failures);
            Check(farming.TryGetPlotSnapshot(targetCell, out FarmingSystem.PlotSnapshot harvested) &&
                  !harvested.HasCrop && harvested.IsTilled,
                "Harvest clears the plant and keeps the soil tilled.", ref failures);

            GameObject treeObject = new("ValidationChopTest");
            treeObject.transform.SetParent(root.transform, false);
            treeObject.transform.position = player.FarmingGroundPosition + Vector3.right * 0.4f;
            treeObject.AddComponent<BoxCollider2D>();
            TreeInteractable tree = treeObject.AddComponent<TreeInteractable>();
            SerializedObject treeObjectData = new(tree);
            treeObjectData.FindProperty("requiredTool").objectReferenceValue = axe;
            treeObjectData.ApplyModifiedPropertiesWithoutUndo();

            // Reproduz uma area carregada. A antiga consulta usava um array fixo de 32 hits,
            // entao o machado podia perder a arvore conforme a ordem retornada pela fisica.
            for (int i = 0; i < 48; i++)
            {
                GameObject distractor = new($"ValidationCollider_{i}");
                distractor.transform.SetParent(root.transform, false);
                distractor.transform.position = playerObject.transform.position +
                                                new Vector3((i % 8) * 0.01f, (i / 8) * 0.01f, 0f);
                CircleCollider2D distractorCollider = distractor.AddComponent<CircleCollider2D>();
                distractorCollider.radius = 0.02f;
            }

            root.SetActive(true);
            inventory.SelectSlot(3);
            Physics2D.SyncTransforms();
            InvokeLifecycle(player, "RefreshNearbyInteractables");
            Check(tree.CanInteract(player),
                "Selected axe remains valid independently from farming tool rules.", ref failures);
            Check(InvokePrivateBool(player, "UpdateCurrentInteractable"),
                "ChopTest is recovered among more than 32 overlaps without a fresh OnTriggerEnter.", ref failures);

            ValidationInteractionSource futureSource = new();
            bool futureEventReceived = false;
            PlayerInteractionEvent futureEvent = default;
            Action<PlayerInteractionEvent> futureHandler = interactionEvent =>
            {
                futureEventReceived = true;
                futureEvent = interactionEvent;
            };

            player.InteractionPerformed += futureHandler;
            bool futureSourceRegistered = player.RegisterInteractionSource(futureSource);
            bool futureInteractionPerformed =
                player.TryPerformCurrentInteraction(PlayerInteractionInput.Primary);
            player.UnregisterInteractionSource(futureSource);
            player.InteractionPerformed -= futureHandler;

            Check(futureSourceRegistered && futureInteractionPerformed && futureSource.PerformCount == 1,
                "A future interaction type registers and executes without changing PlayerInteractor.", ref failures);
            Check(futureEventReceived &&
                  futureEvent.Interaction.SourceId == ValidationInteractionSource.ValidationSourceId &&
                  futureEvent.TryGetPayload(out string futurePayload) &&
                  futurePayload == ValidationInteractionSource.ValidationPayload,
                "Generic interaction events preserve the future source id and its typed payload.", ref failures);
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
        }
        finally
        {
            if (player != null)
                InvokeLifecycle(player, "OnDisable");
            if (farming != null)
                InvokeLifecycle(farming, "OnDisable");

            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(soilTile);
            UnityEngine.Object.DestroyImmediate(crop);
            UnityEngine.Object.DestroyImmediate(hoe);
            UnityEngine.Object.DestroyImmediate(wateringCan);
            UnityEngine.Object.DestroyImmediate(seed);
            UnityEngine.Object.DestroyImmediate(tomato);
            UnityEngine.Object.DestroyImmediate(axe);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    // Fabrica de fixtures temporarias e helpers de reflexao/comparacao.
    private static ItemData CreateItem(string itemId, string itemName)
    {
        ItemData item = ScriptableObject.CreateInstance<ItemData>();
        item.itemId = itemId;
        item.itemName = itemName;
        return item;
    }

    private sealed class ValidationInteractionSource : IPlayerInteractionSource
    {
        public const string ValidationSourceId = "validation_future_interaction";
        public const string ValidationPayload = "future_interaction_payload";

        public string SourceId => ValidationSourceId;
        public int PerformCount { get; private set; }

        public void Refresh(PlayerInteractor interactor) { }

        public bool TryGetCandidate(
            PlayerInteractor interactor,
            PlayerInteractionInput input,
            out PlayerInteractionCandidate candidate)
        {
            candidate = default;
            if (input != PlayerInteractionInput.Primary)
                return false;

            candidate = new PlayerInteractionCandidate(
                this,
                input,
                priority: 1000,
                promptText: "Validar interacao futura",
                contextKey: 73);
            return true;
        }

        public bool TryPerform(
            PlayerInteractor interactor,
            PlayerInteractionCandidate candidate,
            out object payload)
        {
            payload = null;
            if (candidate.ContextKey != 73)
                return false;

            PerformCount++;
            payload = ValidationPayload;
            return true;
        }
    }

    private static CropDefinition CreateCrop(ItemData seed, ItemData harvest, Sprite sprite)
    {
        CropDefinition crop = ScriptableObject.CreateInstance<CropDefinition>();
        SerializedObject cropObject = new(crop);
        cropObject.FindProperty("seedItem").objectReferenceValue = seed;
        cropObject.FindProperty("harvestItem").objectReferenceValue = harvest;

        SerializedProperty sprites = cropObject.FindProperty("growthSprites");
        sprites.arraySize = 3;
        for (int i = 0; i < sprites.arraySize; i++)
            sprites.GetArrayElementAtIndex(i).objectReferenceValue = sprite;

        cropObject.FindProperty("harvestAmount").intValue = 1;
        cropObject.ApplyModifiedPropertiesWithoutUndo();
        return crop;
    }

    private static bool TryPerformExpectedAction(
        FarmingSystem farming,
        PlayerInteractor player,
        FarmingSystem.FarmingAction expectedAction)
    {
        return farming.TryGetInteraction(player, out FarmingSystem.FarmingInteraction interaction) &&
               interaction.Action == expectedAction &&
               farming.TryPerformInteraction(player, interaction);
    }

    private static bool HasReference(UnityEngine.Object target, string propertyName)
    {
        if (target == null)
            return false;

        SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
        return property?.objectReferenceValue != null;
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f &&
               Mathf.Abs(left.g - right.g) < 0.001f &&
               Mathf.Abs(left.b - right.b) < 0.001f &&
               Mathf.Abs(left.a - right.a) < 0.001f;
    }

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return Mathf.Abs(left.x - right.x) < 0.001f &&
               Mathf.Abs(left.y - right.y) < 0.001f &&
               Mathf.Abs(left.z - right.z) < 0.001f;
    }

    private static bool IsEvenPixel(float value)
    {
        int rounded = Mathf.RoundToInt(value);
        return Mathf.Approximately(value, rounded) && rounded % 2 == 0;
    }

    private static void InvokeLifecycle(MonoBehaviour target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static bool InvokePrivateBool(MonoBehaviour target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        return method != null && method.Invoke(target, null) is true;
    }

    private static void Check(bool condition, string message, ref int failures)
    {
        if (condition)
        {
            Debug.Log($"[FARMING-VALIDATION][PASS] {message}");
            return;
        }

        failures++;
        Debug.LogError($"[FARMING-VALIDATION][FAIL] {message}");
    }
}
