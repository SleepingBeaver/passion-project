using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInput))]
public class TopDown8DirController : MonoBehaviour
{
    // Configuração
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float deadzone = 0.15f;

    [Header("Iso Visual Adjust (optional)")]
    [SerializeField] private bool alignToIsoTiles = true;
    [SerializeField] private float isoYScale = 0.5f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Idle Diagonal Fix")]
    [SerializeField] private float diagonalToCardinalCommitTime = 0.08f;

    // Animator Params (names)
    private const string MoveXParam = "MoveX";
    private const string MoveYParam = "MoveY";
    private const string SpeedParam = "Speed";
    private const string LastMoveXParam = "LastMoveX";
    private const string LastMoveYParam = "LastMoveY";
    private const string IsSprintingParam = "IsSprinting"; // crie esse bool no Animator

    // Runtime
    private Rigidbody2D rb;
    private PlayerInput playerInput;

    private InputAction sprintAction;
    private Vector2 inputRaw;

    private Vector2 animDir;   // -1/0/1 para BlendTree
    private Vector2 moveDir;   // direção real normalizada
    private Vector2 lastDir = Vector2.down;

    private Vector2 pendingCardinal;
    private float pendingSince;

    // Animator hashes
    private int hMoveX, hMoveY, hSpeed, hLastX, hLastY, hSprint;

    private void Awake()
    {
        // Física
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // Animator
        if (!animator) animator = GetComponentInChildren<Animator>();

        // Hashes (mais rápido e evita typos em runtime)
        hMoveX = Animator.StringToHash(MoveXParam);
        hMoveY = Animator.StringToHash(MoveYParam);
        hSpeed = Animator.StringToHash(SpeedParam);
        hLastX = Animator.StringToHash(LastMoveXParam);
        hLastY = Animator.StringToHash(LastMoveYParam);
        hSprint = Animator.StringToHash(IsSprintingParam);

        // Input: lê sprint por polling (não trava no release)
        playerInput = GetComponent<PlayerInput>();
        sprintAction = playerInput.actions["Sprint"]; // precisa existir com esse nome

        // Direção inicial do idle
        animator.SetFloat(hLastX, 0f);
        animator.SetFloat(hLastY, -1f);
        animator.SetBool(hSprint, false);
    }

    // Input callbacks (Send Messages)
    public void OnMove(InputValue value)
    {
        inputRaw = value.Get<Vector2>();
    }

    // Loop
    private void Update()
    {
        bool sprintHeld = sprintAction != null && sprintAction.IsPressed();

        // 1) Input + deadzone
        Vector2 v = inputRaw;
        if (v.sqrMagnitude < deadzone * deadzone) v = Vector2.zero;

        // 2) Direção discreta para animação (-1,0,1)
        animDir = new Vector2(
            Mathf.Approximately(v.x, 0f) ? 0f : Mathf.Sign(v.x),
            Mathf.Approximately(v.y, 0f) ? 0f : Mathf.Sign(v.y)
        );

        bool isMoving = animDir != Vector2.zero;

        // 3) Movimento (normalizado sempre que se move)
        Vector2 moveBase = animDir;
        if (alignToIsoTiles && isMoving)
            moveBase = new Vector2(animDir.x, animDir.y * isoYScale);

        moveDir = isMoving ? moveBase.normalized : Vector2.zero;

        // 4) Animator (walk)
        animator.SetFloat(hMoveX, animDir.x);
        animator.SetFloat(hMoveY, animDir.y);
        animator.SetFloat(hSpeed, isMoving ? 1f : 0f);
        animator.SetBool(hSprint, sprintHeld && isMoving);

        // 5) LastMove (idle) com proteção diagonal -> cardinal
        UpdateLastMove(isMoving);
    }

    private void FixedUpdate()
    {
        if (moveDir == Vector2.zero) return;

        bool sprintHeld = sprintAction != null && sprintAction.IsPressed();
        float speed = sprintHeld ? sprintSpeed : walkSpeed;

        rb.MovePosition(rb.position + moveDir * speed * Time.fixedDeltaTime);
    }


    // Idle diagonal fix

    private void UpdateLastMove(bool isMoving)
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

        animator.SetFloat(hLastX, lastDir.x);
        animator.SetFloat(hLastY, lastDir.y);
    }

    private static bool IsDiagonal(Vector2 dir)
        => Mathf.Abs(dir.x) > 0.5f && Mathf.Abs(dir.y) > 0.5f;

    private void ClearPending()
    {
        pendingCardinal = Vector2.zero;
        pendingSince = 0f;
    }
}