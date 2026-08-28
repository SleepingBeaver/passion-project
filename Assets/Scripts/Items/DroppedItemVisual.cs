using UnityEngine;

public class DroppedItemVisual : MonoBehaviour
{
    // Constantes e estados usados pelo drop no mundo.
    private const float PickupTargetResolveRetryInterval = 0.25f;

    private enum DropState
    {
        Scatter,
        Idle,
        Magnetized
    }

    // Configuracao visual e de magnetismo.
    [Header("Visual")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField, Min(0f)] private float scatterDuration = 0.22f;
    [SerializeField, Min(0f)] private float scatterRadiusMin = 0.25f;
    [SerializeField, Min(0f)] private float scatterRadiusMax = 0.65f;
    [SerializeField, Min(0f)] private float arcHeight = 0.18f;

    [Header("Magnet")]
    [SerializeField, Min(0f)] private float magnetDelay = 0.08f;
    [SerializeField, Min(0f)] private float magnetRadius = 1.35f;
    [SerializeField, Min(0f)] private float startMagnetSpeed = 2.5f;
    [SerializeField, Min(0f)] private float maxMagnetSpeed = 8f;
    [SerializeField, Min(0f)] private float magnetAcceleration = 18f;
    [SerializeField, Min(0f)] private float collectDistance = 0.12f;

    [Header("References")]
    [SerializeField] private InventorySystem inventorySystem;
    [SerializeField] private Transform pickupTargetOverride;
    [SerializeField] private string playerTag = "Player";

    // Cache compartilhado para evitar buscas repetidas pelo alvo de coleta.
    private static InventorySystem cachedSharedInventorySystem;
    private static Transform cachedSharedPickupTarget;
    private static string cachedSharedPickupTag;

    // Estado interno do item dropado.
    private ItemData itemData;
    private int amount;

    private Transform pickupTarget;

    private Vector3 scatterStart;
    private Vector3 scatterTarget;
    private float scatterTimer;
    private float currentMagnetSpeed;
    private float magnetUnlockTime;
    private float nextPickupResolveTime;
    private float nextInventoryResolveTime;
    private float magnetRadiusSqr;
    private float collectDistanceSqr;

    private DropState state;
    private bool isInitialized;

    // Ciclo de vida.
    private void Awake()
    {
        SanitizeConfiguration();
        CacheDistanceThresholds();

        ResolveInventorySystem();
    }

    private void OnValidate()
    {
        SanitizeConfiguration();
        CacheDistanceThresholds();
    }

    private void OnEnable()
    {
        if (visualRoot != null)
            visualRoot.localPosition = Vector3.zero;

        if (iconRenderer != null)
            iconRenderer.transform.localScale = Vector3.one;

        nextPickupResolveTime = 0f;
    }

    // Inicializacao externa do drop quando ele nasce no mundo.
    public void Initialize(
        ItemData newItemData,
        int newAmount,
        InventorySystem newInventorySystem = null,
        Transform newPickupTarget = null)
    {
        itemData = newItemData;
        amount = Mathf.Max(1, newAmount);

        if (newInventorySystem != null)
        {
            inventorySystem = newInventorySystem;
            cachedSharedInventorySystem = newInventorySystem;
        }

        if (newPickupTarget != null)
            pickupTargetOverride = newPickupTarget;

        if (iconRenderer != null && itemData != null)
        {
            iconRenderer.sprite = itemData.icon;
            iconRenderer.transform.localScale = Vector3.one * itemData.worldIconScale;
        }

        ResolvePickupTarget();
        BeginScatter();

        isInitialized = true;
    }

    // Atualizacao do comportamento do drop.
    private void Update()
    {
        if (!isInitialized)
            return;

        if (pickupTarget == null && Time.time >= nextPickupResolveTime)
            ResolvePickupTarget();

        if (inventorySystem == null && Time.time >= nextInventoryResolveTime)
            ResolveInventorySystem();

        switch (state)
        {
            case DropState.Scatter:
                UpdateScatter();
                break;

            case DropState.Idle:
                UpdateIdle();
                break;

            case DropState.Magnetized:
                UpdateMagnet();
                break;
        }
    }

    // Fluxo de dispersao inicial.
    private void BeginScatter()
    {
        scatterStart = transform.position;

        Vector2 direction = Random.insideUnitCircle;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;

        direction.Normalize();

        float distance = Random.Range(scatterRadiusMin, scatterRadiusMax);
        scatterTarget = scatterStart + (Vector3)(direction * distance);

        scatterTimer = 0f;
        currentMagnetSpeed = startMagnetSpeed;
        magnetUnlockTime = Time.time + scatterDuration + magnetDelay;
        state = DropState.Scatter;

        if (scatterDuration <= 0f)
            CompleteScatter();
    }

    private void UpdateScatter()
    {
        if (scatterDuration <= 0f)
        {
            CompleteScatter();
            return;
        }

        scatterTimer += Time.deltaTime;
        float t = Mathf.Clamp01(scatterTimer / scatterDuration);

        transform.position = Vector3.Lerp(scatterStart, scatterTarget, t);

        if (visualRoot != null)
        {
            float arcY = Mathf.Sin(t * Mathf.PI) * arcHeight;
            visualRoot.localPosition = new Vector3(0f, arcY, 0f);
        }

        if (t >= 1f)
            CompleteScatter();
    }

    private void CompleteScatter()
    {
        transform.position = scatterTarget;

        if (visualRoot != null)
            visualRoot.localPosition = Vector3.zero;

        state = DropState.Idle;
    }

    // Fluxo de espera e magnetismo.
    private void UpdateIdle()
    {
        if (pickupTarget == null)
            return;

        if (Time.time < magnetUnlockTime)
            return;

        Vector3 toTarget = pickupTarget.position - transform.position;
        if (toTarget.sqrMagnitude <= magnetRadiusSqr)
            state = DropState.Magnetized;
    }

    private void UpdateMagnet()
    {
        if (pickupTarget == null)
        {
            state = DropState.Idle;
            return;
        }

        currentMagnetSpeed = Mathf.MoveTowards(
            currentMagnetSpeed,
            maxMagnetSpeed,
            magnetAcceleration * Time.deltaTime
        );

        transform.position = Vector2.MoveTowards(
            transform.position,
            pickupTarget.position,
            currentMagnetSpeed * Time.deltaTime
        );

        Vector3 toTarget = pickupTarget.position - transform.position;
        if (toTarget.sqrMagnitude <= collectDistanceSqr)
            TryCollect();
    }

    // Tentativa de coleta e integracao com o inventario.
    private void TryCollect()
    {
        if (itemData == null)
        {
            Debug.LogError("DroppedItemVisual nao pode coletar um drop sem ItemData; o objeto foi preservado para diagnostico.", this);
            enabled = false;
            return;
        }

        if (inventorySystem == null)
        {
            ResolveInventorySystem();
            state = DropState.Idle;
            currentMagnetSpeed = startMagnetSpeed;
            return;
        }

        bool addedAll = inventorySystem.AddItem(itemData, amount, out int addedAmount);

        if (addedAll)
        {
            Destroy(gameObject);
        }
        else
        {
            if (addedAmount > 0)
                amount -= addedAmount;

            state = DropState.Idle;
            currentMagnetSpeed = startMagnetSpeed;
            magnetUnlockTime = Time.time + 0.2f;
        }
    }

    // Suporte interno para encontrar o alvo e recalcular distancias.
    private void ResolvePickupTarget()
    {
        nextPickupResolveTime = Time.time + PickupTargetResolveRetryInterval;

        if (pickupTargetOverride != null)
        {
            pickupTarget = pickupTargetOverride;
            return;
        }

        if (cachedSharedPickupTarget != null && cachedSharedPickupTag == playerTag)
        {
            pickupTarget = cachedSharedPickupTarget;
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null)
            return;

        Transform anchor = player.transform.Find("PickupTarget");
        pickupTarget = anchor != null ? anchor : player.transform;
        cachedSharedPickupTarget = pickupTarget;
        cachedSharedPickupTag = playerTag;
    }

    private void ResolveInventorySystem()
    {
        nextInventoryResolveTime = Time.time + PickupTargetResolveRetryInterval;

        if (inventorySystem == null)
        {
            inventorySystem = cachedSharedInventorySystem != null
                ? cachedSharedInventorySystem
                : FindAnyObjectByType<InventorySystem>();
        }

        if (inventorySystem != null)
            cachedSharedInventorySystem = inventorySystem;
    }

    private void CacheDistanceThresholds()
    {
        magnetRadiusSqr = magnetRadius * magnetRadius;
        collectDistanceSqr = collectDistance * collectDistance;
    }

    private void SanitizeConfiguration()
    {
        scatterDuration = Mathf.Max(0f, scatterDuration);
        scatterRadiusMin = Mathf.Max(0f, scatterRadiusMin);
        scatterRadiusMax = Mathf.Max(scatterRadiusMin, scatterRadiusMax);
        arcHeight = Mathf.Max(0f, arcHeight);
        magnetDelay = Mathf.Max(0f, magnetDelay);
        magnetRadius = Mathf.Max(0f, magnetRadius);
        startMagnetSpeed = Mathf.Max(0f, startMagnetSpeed);
        maxMagnetSpeed = Mathf.Max(startMagnetSpeed, maxMagnetSpeed);
        magnetAcceleration = Mathf.Max(0f, magnetAcceleration);
        collectDistance = Mathf.Max(0f, collectDistance);
    }
}
