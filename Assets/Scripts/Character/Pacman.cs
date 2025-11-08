using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Core Pac-Man player controller
/// Handles player input, movement buffering, facing direction,
/// animation, death sequence, and interaction with GameManager
/// </summary>
public class Pacman : MonoBehaviour
{
    public bool isDead = false;

    [Header("References")]
    public Movement movement;
    public ArrowIndicator arrowIndicator;

    [Header("Input Settings")]
    private Vector2 lastInputDirection = Vector2.zero; // Stores last movement input
    private float inputBufferTime = 0f; // When the current buffer expires
    private float bufferDuration = 0.18f; // How long buffered input lasts
    public bool isInputLocked = false; // Blocks movement during transitions
    private bool indicatorVisible = true; // Controls visibility of the direction arrow

    private SpriteRenderer spriteRenderer;
    public Animator animator;
    private PlayerInput playerInput;

    private void Awake()
    {
        // Cache required components
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerInput = GetComponent<PlayerInput>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        // Disable movement if Pacman is dead, input is locked, or game isn't playing
        if (isDead || isInputLocked || GameManager.Instance == null ||
            GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

        // Apply buffered input if no new input but buffer still valid
        if (Time.time <= inputBufferTime && lastInputDirection != Vector2.zero)
        {
            TrySetDirection(lastInputDirection);
        }

        // Rotate Pac-Man sprite based on current movement direction
        if (movement.direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(movement.direction.y, movement.direction.x);
            transform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
        }

        // Flip sprite vertically based on direction
        if (movement.direction.x < 0)
            spriteRenderer.flipY = true;
        else if (movement.direction.x > 0)
            spriteRenderer.flipY = false;

        // freeze animation if blocked
        // animator.speed = movement.isBlocked ? 0f : 1f;
    }

    /// <summary>
    /// Called by InputSystem when Move input is triggered
    /// Handles directional input and movement buffering
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        if (isDead || isInputLocked || GameManager.Instance == null ||
            GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

        if (context.performed)
        {
            Vector2 inputDirection = context.ReadValue<Vector2>();

            var moveAction = context.action;
            var activeControl = context.control;

            // Debug which device triggered input
            if (activeControl != null)
            {
                Debug.Log($"[Pacman] Input from: {activeControl.device.displayName} | " +
                          $"Scheme: {playerInput.currentControlScheme} | " +
                          $"Direction: {inputDirection} | " +
                          $"Key: {activeControl.path}");
            }

            // Prioritize dominant axis (horizontal or vertical)
            if (Mathf.Abs(inputDirection.x) > Mathf.Abs(inputDirection.y))
                inputDirection = new Vector2(Mathf.Sign(inputDirection.x), 0f);
            else
                inputDirection = new Vector2(0f, Mathf.Sign(inputDirection.y));

            // Save new direction and buffer expiration time
            if (lastInputDirection != inputDirection)
            {
                inputBufferTime = Time.time + bufferDuration;
                lastInputDirection = inputDirection;
            }

            // Try to set direction immediately or buffer it
            TrySetDirection(inputDirection);

            // Update arrow indicator direction
            UpdateIndicator(inputDirection);
        }
        else if (context.canceled)
        {
            // Clear buffer when input is released
            inputBufferTime = 0f;
        }
    }

    /// <summary>
    /// Safely attempts to change direction
    /// Allows instant reversal if not blocked, otherwise queues input
    /// </summary>
    private void TrySetDirection(Vector2 dir)
    {
        bool wantsReverse = dir == -movement.direction;
        if (wantsReverse && !movement.Occupied(dir))
            movement.SetDirection(dir, forced: true); // Force instant reverse if possible
        else
            movement.SetDirection(dir);  // Normal queued turn
    }

    /// <summary>
    /// Updates the arrow indicator direction during movement
    /// </summary>
    public void UpdateIndicator(Vector2 direction)
    {
        if (arrowIndicator != null && movement.enabled && indicatorVisible)
            arrowIndicator.UpdateIndicator(direction);
    }

    /// <summary>
    /// Resets Pac-Man to initial position and default state after death or level restart
    /// </summary>
    public void ResetState()
    {
        Debug.Log("Resetting Pacman state...");

        movement.rb.constraints = RigidbodyConstraints2D.None;
        transform.position = movement.startingPosition;
        transform.rotation = Quaternion.identity;

        if (movement != null)
        {
            movement.SetDirection(Vector2.zero);
            movement.ResetState();
        }

        if (animator != null)
            animator.Play("move", 0, 0f);

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = false;
            spriteRenderer.flipY = false;
        }

        isDead = false;

        if (arrowIndicator != null)
            arrowIndicator.ResetIndicator();
    }

    /// <summary>
    /// Toggles visibility of the directional arrow indicator
    /// Automatically updates direction when re-enabled
    /// </summary>
    public void UpdateIndicatorVisibility(bool value)
    {
        indicatorVisible = value;

        if (arrowIndicator == null) return;

        if (value)
        {
            // Choose the best direction to display
            Vector2 dir =
                (movement != null && movement.direction != Vector2.zero) ? movement.direction :
                (lastInputDirection != Vector2.zero) ? lastInputDirection :
                Vector2.right;

            arrowIndicator.UpdateIndicator(dir);
        }
        else
        {
            arrowIndicator.ResetIndicator();
        }
    }

    /// <summary>
    /// Triggers Pac-Man's death sequence
    /// </summary>
    public void Death()
    {
        if (isDead) return;
        StartCoroutine(DieSequence());
    }

    /// <summary>
    /// Coroutine that handles Pac-Man's death animation and logic
    /// Freezes movement, plays animation and sound, then notifies GameManager
    /// </summary>
    private IEnumerator DieSequence()
    {
        isDead = true;

        GameManager.Instance.globalGhostModeController.StopAllGhosts();
        GameManager.Instance.globalGhostModeController.SetTimersFrozen(true);
        GameManager.Instance.globalGhostModeController.SetEyesAudioAllowed(false);

        AudioManager.Instance.StopAll();

        movement.rb.constraints = RigidbodyConstraints2D.FreezeAll;
        movement.enabled = false;
        animator.speed = 0f;

        if (arrowIndicator != null)
            arrowIndicator.ResetIndicator();

        yield return new WaitForSeconds(1f);

        animator.speed = 1f;
        transform.rotation = Quaternion.identity;

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = false;
            spriteRenderer.flipY = false;
        }

        // Play Pac-Man death sound and trigger animation
        AudioManager.Instance.Play(
            GameManager.Instance.selectedCharacters[GameManager.Instance.GetCurrentIndex()].deathSound,
            SoundCategory.SFX
        );
        animator.SetTrigger("death");

        yield return new WaitForSeconds(2f);

        GameManager.Instance.PacmanEaten();
    }

    /// <summary>
    /// Called by InputSystem when Pause input is triggered
    /// Toggles the pause state if the game is currently playing or paused
    /// </summary>
    public void OnPause(InputAction.CallbackContext context)
    {
        if (context.performed &&
            (GameManager.Instance.CurrentGameState == GameManager.GameState.Playing ||
             GameManager.Instance.CurrentGameState == GameManager.GameState.Paused))
        {
            GameManager.Instance.TogglePause();
        }
    }

    /// <summary>
    /// Called by InputSystem when Dejoin input is triggered
    /// Requests a global dejoin via InputManager if rejoin mode isn't active
    /// </summary>
    public void OnDejoin(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;

        if (GameManager.Instance != null && !InputManager.Instance.waitingForRejoin)
        {
            InputManager.Instance.RequestDejoin(
                GameManager.Instance,
                GameManager.Instance.currentPlayer,
                GameManager.Instance.IsTwoPlayerMode,
                GameManager.Instance.TotalPlayers
            );
        }
    }

    /// <summary>
    /// Draws debug gizmos showing Pac-Man's next tile collision state
    /// Green = free, Red = blocked
    /// </summary>
    void OnDrawGizmos()
    {
        if (movement == null || !movement.enabled) return;

        Vector2 size = new Vector2(1f, 1.75f);
        Vector2 direction = movement.direction;
        bool isOccupied = movement.Occupied(direction);

        Gizmos.color = isOccupied ? Color.red : Color.green;
        Gizmos.DrawWireCube(transform.position + (Vector3)direction * 1.5f, size);
    }
}