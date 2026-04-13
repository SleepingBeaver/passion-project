using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInput))]
public class IsoPlayerController2D : MonoBehaviour
{
    // Configuracao de movimento isometrico.
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float deadzone = 0.15f;
    [SerializeField] private bool alignToIsoTiles = true;
    [SerializeField] private float isoYScale = 0.5f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Idle Diagonal Fix")]
    [SerializeField] private float diagonalToCardinalCommitTime = 0.08f;

    // Referencias e cache usados em runtime.
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private InputAction sprintAction;

    private Vector2 inputRaw;
    private Vector2 animDir;
    private Vector2 moveDir;
    private Vector2 lastDir = Vector2.down;

    private Vector2 pendingCardinal;
    private float pendingSince;

    private bool sprintHeld;
    private bool isMoving;
    private bool hasIsSprintingParam;
    private bool hasCachedSprintValue;

    // Hashes de animacao para evitar buscas por string a cada frame.
    private float cachedMoveX = float.NaN;
    private float cachedMoveY = float.NaN;
    private float cachedSpeed = float.NaN;
    private float cachedLastMoveX = float.NaN;
    private float cachedLastMoveY = float.NaN;
    private bool cachedSprintValue;

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");

    // Ciclo de vida.
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
            sprintAction = playerInput.actions.FindAction("Sprint", true);

        hasIsSprintingParam = HasBoolParam(animator, IsSprintingHash);

        if (animator != null)
        {
            SetAnimatorFloatIfChanged(LastMoveXHash, 0f, ref cachedLastMoveX);
            SetAnimatorFloatIfChanged(LastMoveYHash, -1f, ref cachedLastMoveY);

            if (hasIsSprintingParam)
                SetAnimatorBoolIfChanged(IsSprintingHash, false);
        }
    }

    // Leitura de input vinda do Input System.
    public void OnMove(InputValue value)
    {
        inputRaw = value.Get<Vector2>();
    }

    // Atualizacao de input, direcao e animacao.
    private void Update()
    {
        sprintHeld = sprintAction != null && sprintAction.IsPressed();

        Vector2 v = inputRaw;
        if (v.sqrMagnitude < deadzone * deadzone)
            v = Vector2.zero;

        animDir = new Vector2(
            Mathf.Approximately(v.x, 0f) ? 0f : Mathf.Sign(v.x),
            Mathf.Approximately(v.y, 0f) ? 0f : Mathf.Sign(v.y)
        );

        isMoving = animDir != Vector2.zero;

        Vector2 moveBase = animDir;
        if (alignToIsoTiles && isMoving)
            moveBase = new Vector2(animDir.x, animDir.y * isoYScale);

        moveDir = isMoving ? moveBase.normalized : Vector2.zero;

        if (animator != null)
        {
            SetAnimatorFloatIfChanged(MoveXHash, animDir.x, ref cachedMoveX);
            SetAnimatorFloatIfChanged(MoveYHash, animDir.y, ref cachedMoveY);
            SetAnimatorFloatIfChanged(SpeedHash, isMoving ? 1f : 0f, ref cachedSpeed);

            if (hasIsSprintingParam)
                SetAnimatorBoolIfChanged(IsSprintingHash, sprintHeld && isMoving);
        }

        UpdateLastMove();
    }

    // Aplicacao final do deslocamento no Rigidbody.
    private void FixedUpdate()
    {
        if (!isMoving)
            return;

        float speed = sprintHeld ? sprintSpeed : walkSpeed;
        rb.MovePosition(rb.position + moveDir * speed * Time.fixedDeltaTime);
    }

    // Regras para manter a direcao de idle mais natural nas diagonais.
    private void UpdateLastMove()
    {
        if (!isMoving)
        {
            ClearPending();
            return;
        }

        if (IsDiagonal(animDir))
        {
            lastDir = animDir;
            ClearPending();
        }
        else if (IsDiagonal(lastDir))
        {
            if (animDir != pendingCardinal)
            {
                pendingCardinal = animDir;
                pendingSince = Time.time;
            }

            if (Time.time - pendingSince >= diagonalToCardinalCommitTime)
            {
                lastDir = pendingCardinal;
                ClearPending();
            }
        }
        else
        {
            lastDir = animDir;
            ClearPending();
        }

        if (animator != null)
        {
            SetAnimatorFloatIfChanged(LastMoveXHash, lastDir.x, ref cachedLastMoveX);
            SetAnimatorFloatIfChanged(LastMoveYHash, lastDir.y, ref cachedLastMoveY);
        }
    }

    // Utilitarios de direcao e animator.
    private static bool IsDiagonal(Vector2 dir)
    {
        return Mathf.Abs(dir.x) > 0.5f && Mathf.Abs(dir.y) > 0.5f;
    }

    private void ClearPending()
    {
        pendingCardinal = Vector2.zero;
        pendingSince = 0f;
    }

    private static bool HasBoolParam(Animator targetAnimator, int nameHash)
    {
        if (targetAnimator == null)
            return false;

        foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
        {
            if (parameter.nameHash == nameHash && parameter.type == AnimatorControllerParameterType.Bool)
                return true;
        }

        return false;
    }

    private void SetAnimatorFloatIfChanged(int hash, float value, ref float cachedValue)
    {
        if (Mathf.Approximately(cachedValue, value))
            return;

        animator.SetFloat(hash, value);
        cachedValue = value;
    }

    private void SetAnimatorBoolIfChanged(int hash, bool value)
    {
        if (hasCachedSprintValue && cachedSprintValue == value)
            return;

        animator.SetBool(hash, value);
        cachedSprintValue = value;
        hasCachedSprintValue = true;
    }
}
