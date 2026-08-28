using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SaveGameController : MonoBehaviour
{
    public const int CurrentSaveVersion = 2;
    private const string SaveFileName = "savegame.json";
    private const string BackupSuffix = ".bak";
    private const string TemporarySuffix = ".tmp";

    [Serializable]
    private sealed class SaveEnvelope
    {
        public int envelopeVersion = 1;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class SaveGameData
    {
        public int version = CurrentSaveVersion;
        public string sceneName;
        public string savedAtUtc;
        public PlayerSaveData player = new();
        public WorldSaveData world = new();
        public InventorySaveData inventory = new();
        public List<FarmPlotSaveData> farmPlots = new();
        public List<CrateSaveData> crates = new();
        public RestaurantSaveData restaurant = new();
    }

    [Serializable]
    private sealed class PlayerSaveData
    {
        public SerializableVector3 position;
        public SerializableVector2 facing = new(0f, -1f);
    }

    [Serializable]
    private sealed class WorldSaveData
    {
        public int season;
        public int year = 1;
        public int dayOfSeason = 1;
        public int weekDay;
        public int hour24 = 6;
        public int minute;
        public int weather;
        public int weatherDayIndex;
        public int money;
    }

    [Serializable]
    private sealed class InventorySaveData
    {
        public int selectedSlotIndex;
        public List<SlotSaveData> slots = new();
    }

    [Serializable]
    private sealed class SlotSaveData
    {
        public int index;
        public string itemId;
        public int amount;
    }

    [Serializable]
    private sealed class FarmPlotSaveData
    {
        public SerializableVector3Int cell;
        public bool isTilled;
        public bool isWatered;
        public string cropId;
        public int growthStage;
        public int harvestRemaining;
    }

    [Serializable]
    private sealed class CrateSaveData
    {
        public SerializableVector3 position;
        public SerializableVector3Int anchorCell;
        public string itemId;
        public bool mirrored;
        public List<SlotSaveData> slots = new();
    }

    [Serializable]
    private sealed class RestaurantSaveData
    {
        public bool hasActiveOrder;
        public string activeDishId;
        public string customerName;
        public float remainingOrderSeconds;
        public float orderCooldownSeconds;
        public int nextOrderSequence;
        public int reputation;
        public int completedOrdersToday;
        public int failedOrdersToday;
        public int totalCompletedOrders;
        public int totalFailedOrders;
        public string kitchenDishId;
        public float remainingCookingSeconds;
        public int readyServings;
    }

    [Serializable]
    private struct SerializableVector2
    {
        public float x;
        public float y;

        public SerializableVector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public SerializableVector2(Vector2 value) : this(value.x, value.y) { }
        public Vector2 ToVector2() => new(x, y);
    }

    [Serializable]
    private struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public Vector3 ToVector3() => new(x, y, z);
    }

    [Serializable]
    private struct SerializableVector3Int
    {
        public int x;
        public int y;
        public int z;

        public SerializableVector3Int(Vector3Int value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public Vector3Int ToVector3Int() => new(x, y, z);
    }

    [Header("Save Policy")]
    [SerializeField] private bool loadAutomatically = true;
    [SerializeField] private bool saveOnNewDay = true;
    [SerializeField] private bool saveOnApplicationPause = true;
    [SerializeField] private bool saveOnApplicationQuit = true;

    private readonly List<FarmingSystem.PersistedPlotSnapshot> farmingSnapshots = new();
    private GameContentCatalog contentCatalog;
    private InventorySystem inventorySystem;
    private WorldInfoSystem worldInfoSystem;
    private GameplayDayCycleController dayCycleController;
    private FarmingSystem farmingSystem;
    private IsoPlayerController2D playerController;
    private RestaurantServiceSystem restaurantServiceSystem;
    private KitchenStationInteractable kitchenStation;
    private Grid targetGrid;
    private bool isApplyingSave;
    private bool hasLoaded;

    public string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);
    public bool HasSaveFile => File.Exists(SavePath) || File.Exists(SavePath + BackupSuffix);
    public bool HasLoaded => hasLoaded;

    private void Awake()
    {
        ResolveReferences();

        if (loadAutomatically)
            LoadNow();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (dayCycleController != null)
            dayCycleController.NewDayStarted += HandleNewDayStarted;
    }

    private void OnDisable()
    {
        if (dayCycleController != null)
            dayCycleController.NewDayStarted -= HandleNewDayStarted;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && saveOnApplicationPause && hasLoaded)
            SaveNow();
    }

    private void OnApplicationQuit()
    {
        if (saveOnApplicationQuit && hasLoaded)
            SaveNow();
    }

    [ContextMenu("Save Now")]
    public bool SaveNow()
    {
        if (isApplyingSave)
            return false;

        ResolveReferences();
        if (!CanPersist(out string dependencyError))
        {
            Debug.LogWarning($"Save ignorado: {dependencyError}", this);
            return false;
        }

        try
        {
            SaveGameData data = CaptureState();
            string payload = JsonUtility.ToJson(data, prettyPrint: false);
            SaveEnvelope envelope = new()
            {
                payload = payload,
                checksum = ComputeChecksum(payload)
            };

            WriteAtomically(JsonUtility.ToJson(envelope, prettyPrint: true));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Nao foi possivel salvar em '{SavePath}': {exception.Message}", this);
            return false;
        }
    }

    [ContextMenu("Load Now")]
    public bool LoadNow()
    {
        if (isApplyingSave)
            return false;

        ResolveReferences();
        hasLoaded = true;

        if (!HasSaveFile)
            return false;

        if (!CanPersist(out string dependencyError))
        {
            Debug.LogWarning($"Carregamento ignorado: {dependencyError}", this);
            return false;
        }

        string primaryPath = SavePath;
        if (TryReadSave(primaryPath, out SaveGameData data, out string primaryError))
            return ApplyState(data);

        string backupPath = SavePath + BackupSuffix;
        if (TryReadSave(backupPath, out data, out string backupError))
        {
            Debug.LogWarning($"Save principal invalido ({primaryError}). Backup carregado.", this);
            return ApplyState(data);
        }

        Debug.LogError($"Nenhum save valido foi encontrado. Principal: {primaryError}. Backup: {backupError}.", this);
        return false;
    }

    private void HandleNewDayStarted(GameplayDayCycleController.DayTransition _)
    {
        if (saveOnNewDay)
            SaveNow();
    }

    private SaveGameData CaptureState()
    {
        SaveGameData data = new()
        {
            sceneName = SceneManager.GetActiveScene().name,
            savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            player = new PlayerSaveData
            {
                position = new SerializableVector3(playerController.transform.position),
                facing = new SerializableVector2(playerController.FacingDirection)
            },
            world = new WorldSaveData
            {
                season = (int)worldInfoSystem.CurrentSeason,
                year = worldInfoSystem.CurrentYear,
                dayOfSeason = worldInfoSystem.CurrentDayOfSeason,
                weekDay = (int)worldInfoSystem.CurrentWeekDay,
                hour24 = worldInfoSystem.CurrentHour24,
                minute = worldInfoSystem.CurrentMinute,
                weather = (int)worldInfoSystem.CurrentWeather,
                weatherDayIndex = worldInfoSystem.WeatherDayIndex,
                money = worldInfoSystem.Money
            },
            inventory = CaptureInventory(),
            farmPlots = CaptureFarmPlots(),
            crates = CaptureCrates(),
            restaurant = CaptureRestaurant()
        };

        return data;
    }

    private RestaurantSaveData CaptureRestaurant()
    {
        return new RestaurantSaveData
        {
            hasActiveOrder = restaurantServiceSystem.HasActiveOrder,
            activeDishId = restaurantServiceSystem.ActiveDish != null
                ? restaurantServiceSystem.ActiveDish.DishId
                : string.Empty,
            customerName = restaurantServiceSystem.ActiveCustomerName,
            remainingOrderSeconds = restaurantServiceSystem.RemainingOrderSeconds,
            orderCooldownSeconds = restaurantServiceSystem.OrderCooldownSeconds,
            nextOrderSequence = restaurantServiceSystem.NextOrderSequence,
            reputation = restaurantServiceSystem.Reputation,
            completedOrdersToday = restaurantServiceSystem.CompletedOrdersToday,
            failedOrdersToday = restaurantServiceSystem.FailedOrdersToday,
            totalCompletedOrders = restaurantServiceSystem.TotalCompletedOrders,
            totalFailedOrders = restaurantServiceSystem.TotalFailedOrders,
            kitchenDishId = kitchenStation.Dish != null ? kitchenStation.Dish.DishId : string.Empty,
            remainingCookingSeconds = kitchenStation.RemainingCookingSeconds,
            readyServings = kitchenStation.ReadyServings
        };
    }

    private InventorySaveData CaptureInventory()
    {
        InventorySaveData inventory = new()
        {
            selectedSlotIndex = inventorySystem.SelectedSlotIndex
        };

        CaptureSlots(inventorySystem.Slots, inventory.slots);
        return inventory;
    }

    private List<FarmPlotSaveData> CaptureFarmPlots()
    {
        List<FarmPlotSaveData> results = new();
        farmingSystem.GetModifiedPlotSnapshots(farmingSnapshots);

        for (int i = 0; i < farmingSnapshots.Count; i++)
        {
            FarmingSystem.PersistedPlotSnapshot snapshot = farmingSnapshots[i];
            results.Add(new FarmPlotSaveData
            {
                cell = new SerializableVector3Int(snapshot.Cell),
                isTilled = snapshot.Plot.IsTilled,
                isWatered = snapshot.Plot.IsWatered,
                cropId = snapshot.Plot.Crop != null ? snapshot.Plot.Crop.CropId : string.Empty,
                growthStage = snapshot.Plot.GrowthStage,
                harvestRemaining = snapshot.Plot.HarvestRemaining
            });
        }

        results.Sort(CompareFarmPlots);
        return results;
    }

    private List<CrateSaveData> CaptureCrates()
    {
        List<CrateSaveData> results = new();
        PlaceableItemOccupancy[] occupancies = FindObjectsByType<PlaceableItemOccupancy>(
            FindObjectsInactive.Include);

        for (int i = 0; i < occupancies.Length; i++)
        {
            PlaceableItemOccupancy occupancy = occupancies[i];
            if (occupancy == null || !occupancy.TryGetComponent(out CrateStorageInteractable crate) ||
                crate.CrateItemData == null)
            {
                continue;
            }

            CrateSaveData crateData = new()
            {
                position = new SerializableVector3(crate.transform.position),
                anchorCell = new SerializableVector3Int(occupancy.AnchorCell),
                itemId = crate.CrateItemData.itemId,
                mirrored = crate.IsMirrored
            };
            CaptureSlots(crate.Slots, crateData.slots);
            results.Add(crateData);
        }

        results.Sort(CompareCrates);
        return results;
    }

    private static void CaptureSlots(IReadOnlyList<InventorySlotData> source, List<SlotSaveData> destination)
    {
        destination.Clear();

        for (int i = 0; i < source.Count; i++)
        {
            InventorySlotData slot = source[i];
            if (slot == null || slot.IsEmpty || string.IsNullOrWhiteSpace(slot.Item.itemId))
                continue;

            destination.Add(new SlotSaveData
            {
                index = i,
                itemId = slot.Item.itemId.Trim(),
                amount = slot.Amount
            });
        }
    }

    private bool ApplyState(SaveGameData data)
    {
        if (data == null || data.version <= 0 || data.version > CurrentSaveVersion)
        {
            Debug.LogError($"Versao de save nao suportada: {data?.version ?? 0}.", this);
            return false;
        }

        isApplyingSave = true;
        try
        {
            ApplyWorld(data.world);
            ApplyPlayer(data.player);
            ApplyInventory(data.inventory);
            ApplyFarmPlots(data.farmPlots);
            ApplyRestaurant(data.version, data.restaurant);
            ApplyCrates(data.crates);
            Physics2D.SyncTransforms();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Falha ao aplicar o save: {exception.Message}", this);
            return false;
        }
        finally
        {
            isApplyingSave = false;
        }
    }

    private void ApplyWorld(WorldSaveData world)
    {
        world ??= new WorldSaveData();
        worldInfoSystem.ApplySavedState(
            (WorldInfoSystem.Season)world.season,
            world.year,
            world.dayOfSeason,
            (WorldInfoSystem.WeekDay)world.weekDay,
            world.hour24,
            world.minute,
            (WorldInfoSystem.WeatherType)world.weather,
            world.weatherDayIndex,
            world.money);
    }

    private void ApplyPlayer(PlayerSaveData player)
    {
        player ??= new PlayerSaveData();
        Vector3 position = player.position.ToVector3();
        playerController.transform.position = position;

        if (playerController.TryGetComponent(out Rigidbody2D rigidbody2D))
            rigidbody2D.position = (Vector2)position;

        playerController.RestoreFacingDirection(player.facing.ToVector2());
    }

    private void ApplyInventory(InventorySaveData inventory)
    {
        inventory ??= new InventorySaveData();
        inventory.slots ??= new List<SlotSaveData>();

        inventorySystem.BeginBatchUpdate();
        try
        {
            inventorySystem.ClearAllSlots();

            for (int i = 0; i < inventory.slots.Count; i++)
            {
                SlotSaveData slot = inventory.slots[i];
                if (slot == null || slot.amount <= 0 ||
                    !contentCatalog.TryGetItem(slot.itemId, out ItemData itemData) ||
                    !inventorySystem.SetSlotContents(slot.index, itemData, slot.amount))
                {
                    Debug.LogWarning($"Slot de inventario ignorado no load: indice {slot?.index ?? -1}, item '{slot?.itemId}'.", this);
                }
            }

            int selectedIndex = Mathf.Clamp(inventory.selectedSlotIndex, 0, Mathf.Max(0, inventorySystem.SlotCount - 1));
            inventorySystem.SelectSlot(selectedIndex);
        }
        finally
        {
            inventorySystem.EndBatchUpdate();
        }
    }

    private void ApplyFarmPlots(List<FarmPlotSaveData> plots)
    {
        farmingSystem.ClearModifiedPlots();
        if (plots == null)
            return;

        for (int i = 0; i < plots.Count; i++)
        {
            FarmPlotSaveData plot = plots[i];
            if (plot == null)
                continue;

            CropDefinition crop = null;
            if (!string.IsNullOrWhiteSpace(plot.cropId) &&
                !contentCatalog.TryGetCrop(plot.cropId, out crop))
            {
                Debug.LogWarning($"Cultura desconhecida no save: '{plot.cropId}'.", this);
                continue;
            }

            if (!farmingSystem.RestorePlot(
                    plot.cell.ToVector3Int(),
                    plot.isTilled,
                    plot.isWatered,
                    crop,
                    plot.growthStage,
                    plot.harvestRemaining))
            {
                Debug.LogWarning($"Plot invalido ignorado no load: ({plot.cell.x}, {plot.cell.y}).", this);
            }
        }
    }

    private void ApplyCrates(List<CrateSaveData> crates)
    {
        RemoveExistingPlacedCrates();
        if (crates == null)
            return;

        for (int i = 0; i < crates.Count; i++)
        {
            CrateSaveData crateData = crates[i];
            if (crateData == null || !contentCatalog.TryGetItem(crateData.itemId, out ItemData crateItem))
            {
                Debug.LogWarning($"Caixote com item desconhecido ignorado: '{crateData?.itemId}'.", this);
                continue;
            }

            GameObject crateObject = new($"{crateItem.itemName}_World");
            crateObject.SetActive(false);
            crateObject.transform.position = crateData.position.ToVector3();

            PlaceableItemOccupancy occupancy = crateObject.AddComponent<PlaceableItemOccupancy>();
            occupancy.Initialize(targetGrid, crateData.anchorCell.ToVector3Int(), crateItem.PlacementFootprintSize);

            CrateStorageInteractable crate = crateObject.AddComponent<CrateStorageInteractable>();
            crate.Initialize(crateItem, sharedInventorySystem: inventorySystem, mirrored: crateData.mirrored);
            crateObject.SetActive(true);

            if (!occupancy.IsRegistered)
            {
                crateObject.SetActive(false);
                Destroy(crateObject);
                Debug.LogWarning($"Caixote em celula ocupada foi ignorado: ({crateData.anchorCell.x}, {crateData.anchorCell.y}).", this);
                continue;
            }

            RestoreCrateSlots(crate, crateData.slots);
        }
    }

    private void ApplyRestaurant(int saveVersion, RestaurantSaveData restaurant)
    {
        DishDefinition defaultDish = contentCatalog.Dishes.Count > 0 ? contentCatalog.Dishes[0] : null;

        if (saveVersion < 2 || restaurant == null)
        {
            restaurantServiceSystem.RestoreState(
                restoredDish: null,
                customerName: string.Empty,
                restoredRemainingSeconds: 0f,
                restoredCooldownSeconds: 3f,
                restoredNextOrderSequence: 0,
                restoredReputation: 0,
                restoredCompletedToday: 0,
                restoredFailedToday: 0,
                restoredTotalCompleted: 0,
                restoredTotalFailed: 0);
            kitchenStation.RestoreState(defaultDish, 0f, 0);
            return;
        }

        DishDefinition activeDish = null;
        if (restaurant.hasActiveOrder &&
            !contentCatalog.TryGetDish(restaurant.activeDishId, out activeDish))
        {
            Debug.LogWarning($"Pedido com prato desconhecido foi descartado: '{restaurant.activeDishId}'.", this);
        }

        restaurantServiceSystem.RestoreState(
            activeDish,
            restaurant.customerName,
            restaurant.remainingOrderSeconds,
            restaurant.orderCooldownSeconds,
            restaurant.nextOrderSequence,
            restaurant.reputation,
            restaurant.completedOrdersToday,
            restaurant.failedOrdersToday,
            restaurant.totalCompletedOrders,
            restaurant.totalFailedOrders);

        DishDefinition kitchenDish = defaultDish;
        if (!string.IsNullOrWhiteSpace(restaurant.kitchenDishId) &&
            !contentCatalog.TryGetDish(restaurant.kitchenDishId, out kitchenDish))
        {
            Debug.LogWarning($"Prato desconhecido na cozinha foi substituido pelo padrao: '{restaurant.kitchenDishId}'.", this);
            kitchenDish = defaultDish;
        }

        kitchenStation.RestoreState(
            kitchenDish,
            restaurant.remainingCookingSeconds,
            restaurant.readyServings);
    }

    private void RestoreCrateSlots(CrateStorageInteractable crate, List<SlotSaveData> savedSlots)
    {
        crate.ClearAllSlots(notify: false);

        if (savedSlots != null)
        {
            for (int i = 0; i < savedSlots.Count; i++)
            {
                SlotSaveData slot = savedSlots[i];
                if (slot == null || slot.amount <= 0 ||
                    !contentCatalog.TryGetItem(slot.itemId, out ItemData itemData) ||
                    !crate.SetSlotContents(slot.index, itemData, slot.amount, notify: false))
                {
                    Debug.LogWarning($"Slot de caixote ignorado no load: indice {slot?.index ?? -1}, item '{slot?.itemId}'.", this);
                }
            }
        }

        crate.NotifyRestoredContents();
    }

    private void RemoveExistingPlacedCrates()
    {
        PlaceableItemOccupancy[] occupancies = FindObjectsByType<PlaceableItemOccupancy>(
            FindObjectsInactive.Include);

        for (int i = 0; i < occupancies.Length; i++)
        {
            PlaceableItemOccupancy occupancy = occupancies[i];
            if (occupancy == null || !occupancy.TryGetComponent(out CrateStorageInteractable _))
                continue;

            occupancy.gameObject.SetActive(false);
            Destroy(occupancy.gameObject);
        }
    }

    private bool TryReadSave(string path, out SaveGameData data, out string error)
    {
        data = null;
        error = "arquivo ausente";

        if (!File.Exists(path))
            return false;

        try
        {
            SaveEnvelope envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path, Encoding.UTF8));
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.payload))
            {
                error = "envelope vazio";
                return false;
            }

            string actualChecksum = ComputeChecksum(envelope.payload);
            if (!string.Equals(actualChecksum, envelope.checksum, StringComparison.OrdinalIgnoreCase))
            {
                error = "checksum divergente";
                return false;
            }

            data = JsonUtility.FromJson<SaveGameData>(envelope.payload);
            if (data == null)
            {
                error = "payload invalido";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private void WriteAtomically(string contents)
    {
        string savePath = SavePath;
        string directory = Path.GetDirectoryName(savePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Diretorio de save invalido.");

        Directory.CreateDirectory(directory);
        string temporaryPath = savePath + TemporarySuffix;
        string backupPath = savePath + BackupSuffix;
        File.WriteAllText(temporaryPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!File.Exists(savePath))
        {
            File.Move(temporaryPath, savePath);
            return;
        }

        try
        {
            File.Replace(temporaryPath, savePath, backupPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceWithBackupFallback(temporaryPath, savePath, backupPath);
        }
        catch (IOException)
        {
            ReplaceWithBackupFallback(temporaryPath, savePath, backupPath);
        }
    }

    private bool CanPersist(out string error)
    {
        if (contentCatalog == null)
        {
            error = "GameContentCatalog nao encontrado em Resources";
            return false;
        }

        if (inventorySystem == null || worldInfoSystem == null || farmingSystem == null ||
            playerController == null || targetGrid == null || restaurantServiceSystem == null ||
            kitchenStation == null)
        {
            error = "dependencias principais da cena incompletas";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ResolveReferences()
    {
        contentCatalog ??= GameContentCatalog.LoadDefault();
        inventorySystem ??= FindAnyObjectByType<InventorySystem>();
        worldInfoSystem ??= FindAnyObjectByType<WorldInfoSystem>();
        dayCycleController ??= FindAnyObjectByType<GameplayDayCycleController>();
        farmingSystem ??= FindAnyObjectByType<FarmingSystem>();
        playerController ??= FindAnyObjectByType<IsoPlayerController2D>();
        restaurantServiceSystem ??= FindAnyObjectByType<RestaurantServiceSystem>();
        kitchenStation ??= FindAnyObjectByType<KitchenStationInteractable>();
        targetGrid ??= FindAnyObjectByType<Grid>();
    }

    private static string ComputeChecksum(string payload)
    {
        byte[] hash;
        using (SHA256 algorithm = SHA256.Create())
            hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty));

        StringBuilder builder = new(hash.Length * 2);

        for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static void ReplaceWithBackupFallback(string temporaryPath, string savePath, string backupPath)
    {
        File.Copy(savePath, backupPath, overwrite: true);
        File.Copy(temporaryPath, savePath, overwrite: true);
        File.Delete(temporaryPath);
    }

    private static int CompareFarmPlots(FarmPlotSaveData first, FarmPlotSaveData second)
    {
        int byX = first.cell.x.CompareTo(second.cell.x);
        return byX != 0 ? byX : first.cell.y.CompareTo(second.cell.y);
    }

    private static int CompareCrates(CrateSaveData first, CrateSaveData second)
    {
        int byX = first.anchorCell.x.CompareTo(second.anchorCell.x);
        return byX != 0 ? byX : first.anchorCell.y.CompareTo(second.anchorCell.y);
    }
}

public static class SaveGameBootstrap
{
    private static ulong installedSceneHandle = ulong.MaxValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        installedSceneHandle = ulong.MaxValue;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        InstallForScene(scene);
    }

    private static void InstallForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        ulong sceneHandle = scene.handle.GetRawData();
        if (sceneHandle == installedSceneHandle)
            return;

        installedSceneHandle = sceneHandle;
        WorldInfoSystem worldInfo = UnityEngine.Object.FindAnyObjectByType<WorldInfoSystem>();
        if (worldInfo == null)
            return;

        RestaurantGameplayBootstrap.EnsureSystemsForScene(scene);

        if (!worldInfo.TryGetComponent(out SaveGameController _))
            worldInfo.gameObject.AddComponent<SaveGameController>();
    }
}
