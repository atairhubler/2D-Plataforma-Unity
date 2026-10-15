using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float jumpForce = 12f;

    [Header("Dash (toque duplo na seta)")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.5f;
    [SerializeField] private float doubleTapWindow = 0.25f;

    [Header("Chão")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer = ~0;

    [Header("Queda")]
    [SerializeField] private float fallLimitY = -15f;

    private Rigidbody2D rb;
    private Collider2D col;
    private Vector3 startPosition;
    private float moveInput;
    private bool jumpPressed;
    private bool isGrounded;
    private float lastLeftTap = -10f;
    private float lastRightTap = -10f;
    private float dashEndTime = -10f;
    private float nextDashTime;
    private float dashDirection;

    public bool IsGrounded => isGrounded;
    public float MoveInput => moveInput;
    public bool IsDashing => Time.time < dashEndTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.freezeRotation = true;
        startPosition = transform.position;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        moveInput = 0f;
        if (kb.leftArrowKey.isPressed) moveInput -= 1f;
        if (kb.rightArrowKey.isPressed) moveInput += 1f;

        if (kb.upArrowKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            jumpPressed = true;

        if (kb.leftArrowKey.wasPressedThisFrame) TryDash(-1f, ref lastLeftTap);
        if (kb.rightArrowKey.wasPressedThisFrame) TryDash(1f, ref lastRightTap);

        if (IsDashing) moveInput = dashDirection;

        if (moveInput != 0f)
        {
            var scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(moveInput);
            transform.localScale = scale;
        }
    }

    private void FixedUpdate()
    {
        if (transform.position.y < fallLimitY)
        {
            transform.position = startPosition;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        isGrounded = CheckGrounded();

        float speed = IsDashing ? dashSpeed : moveSpeed;
        rb.linearVelocity = new Vector2(moveInput * speed, rb.linearVelocity.y);

        if (jumpPressed && isGrounded)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        jumpPressed = false;
    }

    // Segundo toque na mesma seta dentro de doubleTapWindow dispara o dash.
    private void TryDash(float direction, ref float lastTap)
    {
        if (Time.time - lastTap <= doubleTapWindow && Time.time >= nextDashTime)
        {
            dashDirection = direction;
            dashEndTime = Time.time + dashDuration;
            nextDashTime = Time.time + dashCooldown;
            lastTap = -10f;
            return;
        }
        lastTap = Time.time;
    }

    private bool CheckGrounded()
    {
        Vector2 point = groundCheck != null
            ? (Vector2)groundCheck.position
            : new Vector2(col.bounds.center.x, col.bounds.min.y);

        foreach (var hit in Physics2D.OverlapCircleAll(point, groundCheckRadius, groundLayer))
        {
            if (hit != col && !hit.isTrigger) return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
