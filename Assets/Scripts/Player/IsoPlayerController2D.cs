using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInput))]
public class TopDown8DirController : MonoBehaviour
{
    // Movimento
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float deadzone = 0.15f;

    [SerializeField] private bool alignToIsoTiles = true;
    [SerializeField] private float isoYScale = 0.5f;

    // Animação
    [Header("Animation")]
    [SerializeField] private Animator animator;

    // Correção do Idle Diagonal
    [Header("Idle Diagonal Fix")]
    [SerializeField] private float diagonalToCardinalCommitTime = 0.08f;

    // Interno
    private Rigidbody2D rb;
    private PlayerInput playerInput;
    private InputAction sprintAction;

    private Vector2 inputRaw;
    private Vector2 animDir;
    private Vector2 moveDir;    // normalizado
    private Vector2 lastDir = Vector2.down;

    private Vector2 pendingCardinal;
    private float pendingSince;

    private bool sprintHeld;
    private bool isMoving;

    // Animator hashes
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int LastMoveXHash = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveYHash = Animator.StringToHash("LastMoveY");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");

    private bool hasIsSprintingParam;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (!animator) animator = GetComponentInChildren<Animator>();

        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.actions != null)
            sprintAction = playerInput.actions.FindAction("Sprint", true);

        // Detecta se o parâmetro existe (evita warning)
        hasIsSprintingParam = HasBoolParam(animator, IsSprintingHash);

        // Direção inicial do idle
        animator.SetFloat(LastMoveXHash, 0f);
        animator.SetFloat(LastMoveYHash, -1f);

        if (hasIsSprintingParam)
            animator.SetBool(IsSprintingHash, false);
    }

    // Input (Send Messages)
    public void OnMove(InputValue value)
    {
        inputRaw = value.Get<Vector2>();
    }

    private void Update()
    {
        // Sprint “segurar e soltar” (robusto)
        sprintHeld = sprintAction != null && sprintAction.IsPressed();

        // Leitura / deadzone
        Vector2 v = inputRaw;
        if (v.sqrMagnitude < deadzone * deadzone) v = Vector2.zero;

        // Direção discreta (animação)
        animDir = new Vector2(
            Mathf.Approximately(v.x, 0f) ? 0f : Mathf.Sign(v.x),
            Mathf.Approximately(v.y, 0f) ? 0f : Mathf.Sign(v.y)
        );

        isMoving = animDir != Vector2.zero;

        // Direção real de movimento (velocidade constante)
        Vector2 moveBase = animDir;

        if (alignToIsoTiles && isMoving)
            moveBase = new Vector2(animDir.x, animDir.y * isoYScale);

        moveDir = isMoving ? moveBase.normalized : Vector2.zero;

        // Animator (walk)
        animator.SetFloat(MoveXHash, animDir.x);
        animator.SetFloat(MoveYHash, animDir.y);
        animator.SetFloat(SpeedHash, isMoving ? 1f : 0f);

        if (hasIsSprintingParam)
            animator.SetBool(IsSprintingHash, sprintHeld && isMoving);

        // Correção do Idle diagonal
        UpdateLastMove();
    }

    private void FixedUpdate()
    {
        if (!isMoving) return;

        float speed = sprintHeld ? sprintSpeed : walkSpeed;
        rb.MovePosition(rb.position + moveDir * speed * Time.fixedDeltaTime);
    }

    // LastMove (idle)
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
        else
        {
            if (IsDiagonal(lastDir))
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
        }

        animator.SetFloat(LastMoveXHash, lastDir.x);
        animator.SetFloat(LastMoveYHash, lastDir.y);
    }

    private static bool IsDiagonal(Vector2 dir)
        => Mathf.Abs(dir.x) > 0.5f && Mathf.Abs(dir.y) > 0.5f;

    private void ClearPending()
    {
        pendingCardinal = Vector2.zero;
        pendingSince = 0f;
    }

    private static bool HasBoolParam(Animator a, int nameHash)
    {
        if (a == null) return false;

        foreach (var p in a.parameters)
        {
            if (p.nameHash == nameHash && p.type == AnimatorControllerParameterType.Bool)
                return true;
        }
        return false;
    }
}