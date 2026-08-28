using UnityEngine;
using UnityEngine.SceneManagement;

public static class RestaurantGameplayBootstrap
{
    private const string KitchenObjectName = "KitchenStation_Runtime";
    private const string ServingObjectName = "ServingCounter_Runtime";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        EnsureSystemsForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        EnsureSystemsForScene(scene);
    }

    public static RestaurantServiceSystem EnsureSystemsForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        WorldInfoSystem worldInfo = Object.FindAnyObjectByType<WorldInfoSystem>();
        InventorySystem inventory = Object.FindAnyObjectByType<InventorySystem>();
        GameplayDayCycleController dayCycle = Object.FindAnyObjectByType<GameplayDayCycleController>();
        if (worldInfo == null || inventory == null)
            return null;

        GameContentCatalog catalog = GameContentCatalog.LoadDefault();
        DishDefinition defaultDish = catalog != null && catalog.Dishes.Count > 0 ? catalog.Dishes[0] : null;

        RestaurantServiceSystem service = worldInfo.GetComponent<RestaurantServiceSystem>();
        service ??= worldInfo.gameObject.AddComponent<RestaurantServiceSystem>();
        service.Initialize(catalog, worldInfo, dayCycle);

        Transform player = ResolvePlayerTransform();
        Vector3 origin = player != null ? player.position : Vector3.zero;

        KitchenStationInteractable kitchen = Object.FindAnyObjectByType<KitchenStationInteractable>();
        if (kitchen == null)
        {
            GameObject kitchenObject = new(KitchenObjectName);
            kitchenObject.transform.position = origin + new Vector3(2f, -1f, 0f);
            kitchen = kitchenObject.AddComponent<KitchenStationInteractable>();
        }
        kitchen.Initialize(defaultDish, inventory);

        ServingCounterInteractable servingCounter = Object.FindAnyObjectByType<ServingCounterInteractable>();
        if (servingCounter == null)
        {
            GameObject servingObject = new(ServingObjectName);
            servingObject.transform.position = origin + new Vector3(4f, -2f, 0f);
            servingCounter = servingObject.AddComponent<ServingCounterInteractable>();
        }
        servingCounter.Initialize(service, inventory);

        RestaurantHUD hud = service.GetComponent<RestaurantHUD>();
        hud ??= service.gameObject.AddComponent<RestaurantHUD>();
        hud.Initialize(service, kitchen);
        return service;
    }

    private static Transform ResolvePlayerTransform()
    {
        PlayerInteractor interactor = Object.FindAnyObjectByType<PlayerInteractor>();
        if (interactor != null)
            return interactor.ActorTransform;

        IsoPlayerController2D controller = Object.FindAnyObjectByType<IsoPlayerController2D>();
        return controller != null ? controller.transform : null;
    }
}
