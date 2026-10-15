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
    private Vector2 facingDirection;      // Direction the sprite was last rotated to
    private Quaternion facingRotation = Quaternion.identity;

    private SpriteRenderer spriteRenderer;
    public Animator animator;

    private void Awake()
    {
        // Cache required components
        spriteRenderer = GetComponent<SpriteRenderer>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        // Level speed (and the faster speed while ghosts are frightened) comes from the ghost controller
        var ghostController = GameManager.Instance != null ? GameManager.Instance.globalGhostModeController : null;
        if (ghostController != null) movement.speed = ghostController.PacmanSpeed;

        // Disable movement if Pacman is dead, input is locked, or game isn't playing
        if (isDead || isInputLocked ||
            GameManager.Instance == null ||
            GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

        // Apply buffered input if no new input but buffer still valid
        if (Time.time <= inputBufferTime && lastInputDirection != Vector2.zero)
        {
            TrySetDirection(lastInputDirection);
        }

        // Face the movement direction. Writing the transform of a Rigidbody2D object forces physics
        // to re-sync it, so only write when the actual rotation differs (also undoes any physics spin)
        Vector2 dir = movement.direction;
        if (dir != Vector2.zero && dir != facingDirection)
        {
            facingDirection = dir;
            float angle = Mathf.Atan2(dir.y, dir.x);
            facingRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);

            // Flip sprite vertically when heading left
            if (dir.x < 0) spriteRenderer.flipY = true;
            else if (dir.x > 0) spriteRenderer.flipY = false;
        }
        if (facingDirection != Vector2.zero && transform.rotation != facingRotation)
            transform.rotation = facingRotation;

        // freeze animation if blocked
        // animator.speed = movement.isBlocked ? 0f : 1f;
    }

    /// <summary>
    /// Called by this player's PlayerInputController when Move input is triggered
    /// Handles directional input and movement buffering
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        if (isDead || isInputLocked || !movement.enabled ||
            GameManager.Instance == null ||
            GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

        if (context.performed)
        {
            Vector2 inputDirection = context.ReadValue<Vector2>();

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

        lastInputDirection = Vector2.zero;
        inputBufferTime = 0f;
        isInputLocked = false;

        transform.position = movement.startingPosition;
        transform.rotation = Quaternion.identity;
        facingDirection = Vector2.zero;

        if (movement != null)
        {
            movement.ResetState();
            movement.ClearNextDirection();
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

        var gmc = GameManager.Instance.globalGhostModeController;
        gmc.StopAllGhosts();
        gmc.SetTimersFrozen(true);
        gmc.SetEyesAudioAllowed(false);

        AudioManager.Instance.StopAll();

        movement.rb.constraints = RigidbodyConstraints2D.FreezeAll;
        movement.enabled = false;
        animator.speed = 0f;

        if (arrowIndicator != null)
            arrowIndicator.ResetIndicator();

        yield return new WaitForSeconds(1f);

        animator.speed = 1f;
        transform.rotation = Quaternion.identity;
        facingDirection = Vector2.zero;

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = false;
            spriteRenderer.flipY = false;
        }

        // Play Pac-Man death sound and trigger animation
        AudioManager.Instance.Play(
            PacmanSpawner.Instance.SelectedCharacters[GameManager.Instance.CurrentIndex].deathSound,
            SoundCategory.SFX
        );
        animator.SetTrigger("death");

        yield return new WaitForSeconds(2f);

        GameManager.Instance.PacmanEaten(); // Game flow — GameManager owns death/turn logic
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