using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4.2f;
    public float rotationSpeed = 12f;
    public float jumpForce = 5.2f;
    public Transform groundCheck;
    public float groundCheckRadius = 0.24f;
    public LayerMask groundLayer = 1 << 3;

    private Rigidbody body;
    private Animator animator;
    private EcoMission mission;
    private Vector3 direction;
    private float speed;
    private float dashLeft;
    private bool doubleJumpUsed;
    private bool jumpRequested;
    private bool glideHeld;

    /// <summary>Recebe a missão da cena e prepara física e animações do robô.</summary>
    public void Bind(EcoMission owner)
    {
        mission = owner;
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    /// <summary>Lê comandos e prepara movimento relativo à câmera. Não retorna valor.</summary>
    private void Update()
    {
        if (mission == null || !mission.Playing)
            return;

        Vector3 forward = mission.ViewForward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        direction = Vector3.ClampMagnitude(forward * Input.GetAxisRaw("Vertical") +
            right * Input.GetAxisRaw("Horizontal"), 1f);
        speed = direction.sqrMagnitude > 0.01f ? moveSpeed : 0f;
        if (speed > 0f && mission.HasSkill(0) && Input.GetKey(KeyCode.LeftShift))
            speed = moveSpeed * 1.65f;

        jumpRequested |= Input.GetKeyDown(KeyCode.Space);
        glideHeld = Input.GetKey(KeyCode.Space);
        if (Input.GetKeyDown(KeyCode.Q) && mission.UseDash())
        {
            dashLeft = 0.24f;
            if (direction.sqrMagnitude < 0.01f)
                direction = transform.forward;
        }
        if (Input.GetKeyDown(KeyCode.F))
            mission.Scan();
    }

    /// <summary>Move o corpo, executa pulos e alimenta o Animator a cada passo de física.</summary>
    private void FixedUpdate()
    {
        if (mission == null || !mission.Playing)
        {
            jumpRequested = false;
            return;
        }

        bool grounded = groundCheck != null &&
            Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore) &&
            body.linearVelocity.y < 1f;
        if (grounded)
            doubleJumpUsed = false;

        if (jumpRequested && (grounded || (mission.HasSkill(1) && !doubleJumpUsed)))
        {
            if (!grounded)
                doubleJumpUsed = true;

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.linearVelocity = velocity;
            body.AddForce(Vector3.up * (jumpForce * body.mass), ForceMode.Impulse);
            if (animator != null)
                animator.SetTrigger("Jump");
            grounded = false;
        }
        jumpRequested = false;

        if (mission.HasSkill(4) && glideHeld && body.linearVelocity.y < -2f)
        {
            Vector3 velocity = body.linearVelocity;
            velocity.y = -2f;
            body.linearVelocity = velocity;
        }

        dashLeft = Mathf.Max(0f, dashLeft - Time.fixedDeltaTime);
        Vector3 travel = dashLeft > 0f ? transform.forward : direction;
        float travelSpeed = dashLeft > 0f ? 13f : speed;
        Vector3 surfaceDirection = travel;
        if (grounded && travel.sqrMagnitude > 0.01f &&
            Physics.Raycast(body.position + Vector3.up * 0.7f + travel * 0.35f, Vector3.down,
                out RaycastHit ground, 1.3f, groundLayer, QueryTriggerInteraction.Ignore) &&
            ground.normal.y > 0.65f)
            surfaceDirection = Vector3.ProjectOnPlane(travel, ground.normal).normalized;
        Vector3 delta = surfaceDirection * (travelSpeed * Time.fixedDeltaTime);
        if (delta.sqrMagnitude > 0.00001f && !body.SweepTest(delta.normalized, out _, delta.magnitude, QueryTriggerInteraction.Ignore))
            transform.Translate(delta, Space.World);
        if (direction.sqrMagnitude > 0.01f)
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.fixedDeltaTime));

        if (animator != null)
        {
            animator.SetFloat("Speed", speed == 0f ? 0f : speed > moveSpeed ? 2f : 1f);
            animator.SetBool("IsGrounded", grounded);
            animator.SetFloat("VerticalSpeed", body.linearVelocity.y);
        }
    }

    /// <summary>Reposiciona o robô no ponto seguro recebido e cancela sua velocidade e impulsos.</summary>
    public void Respawn(Vector3 position)
    {
        body.position = position;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        dashLeft = 0f;
        doubleJumpUsed = false;
        jumpRequested = false;
    }
}
