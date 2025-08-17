using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class Pacman : MonoBehaviour
{
    public bool isDead = false;

    public Movement movement;
    public ArrowIndicator arrowIndicator;

    private Vector2 lastInputDirection = Vector2.zero;
    private float inputBufferTime = 0f;
    private float bufferDuration = 0.18f;
    public bool isInputLocked = false;
    private bool indicatorVisible = true;

    private SpriteRenderer spriteRenderer;
    public Animator animator;
    private PlayerInput playerInput;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerInput = GetComponent<PlayerInput>();
        movement = GetComponent<Movement>();
    }

    private void Update()
    {
        if (isDead || isInputLocked || GameManager.Instance.CurrentGameState != GameManager.GameState.Playing) return;

        // Get move input direction
        Vector2 inputDirection = playerInput.actions["Move"].ReadValue<Vector2>();

        // Filter out diagonal input: prioritize horizontal or vertical based on which is greater
        // I'll save this line for future projects because it took me a while to find it and I know it'll forget in the future lol
        if (inputDirection != Vector2.zero)
        {
            // Prioritize horizontal or vertical input
            if (Mathf.Abs(inputDirection.x) > Mathf.Abs(inputDirection.y))
                inputDirection = new Vector2(Mathf.Sign(inputDirection.x), 0f);
            else
                inputDirection = new Vector2(0f, Mathf.Sign(inputDirection.y));

            if (lastInputDirection != inputDirection)
            {
                inputBufferTime = Time.time + bufferDuration;
                lastInputDirection = inputDirection;
            }

            movement.SetDirection(inputDirection);

            UpdateIndicator(inputDirection);
        }
        else if (Time.time <= inputBufferTime)
        {
            movement.SetDirection(lastInputDirection);
        }

        // If there's input, rotate depending on the input
        if (movement.direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(movement.direction.y, movement.direction.x);
            transform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
        }

        if (movement.direction.x < 0)
        {
            spriteRenderer.flipY = true;
        }
        else if (movement.direction.x > 0)
        {
            spriteRenderer.flipY = false;
        }

        // If the player collides with an obstacle, their animator speed changes to zero
        // animator.speed = movement.isBlocked ? 0f : 1f; // is it blocked? animations stops | is it not block? animation keeps playing 
    }

    public void UpdateIndicator(Vector2 direction)
    {
        if (arrowIndicator != null && movement.enabled && indicatorVisible)
            arrowIndicator.UpdateIndicator(direction);
    }

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
        {
            animator.Play("move", 0, 0f); // Reset the animation to the first frame
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = false;
            spriteRenderer.flipY = false;
        }

        isDead = false;

        if (arrowIndicator != null)
            arrowIndicator.ResetIndicator();
    }

    public void UpdateIndicatorVisibility(bool value)
    {
        indicatorVisible = value;

        if (arrowIndicator == null) return;

        if (value)
        {
            // prefer the actual movement direction, else last input, else face right
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

    public void Death()
    {
        if (isDead) return;
        StartCoroutine(DieSequence());
    }

    private IEnumerator DieSequence()
    {
        isDead = true;

        GameManager.Instance.globalGhostModeController.StopAllGhosts();

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

        AudioManager.Instance.Play(GameManager.Instance.selectedCharacters[GameManager.Instance.GetCurrentIndex()].deathSound, SoundCategory.SFX);
        animator.SetTrigger("death");

        yield return new WaitForSeconds(2f);

        GameManager.Instance.PacmanEaten();
    }

    public void OnPause(InputAction.CallbackContext context)
    {
        if (context.performed && (GameManager.Instance.CurrentGameState == GameManager.GameState.Playing || GameManager.Instance.CurrentGameState == GameManager.GameState.Paused))
        {
            GameManager.Instance.TogglePause();
        }
    }

    void OnDrawGizmos()
    {
        if (movement == null || !movement.enabled) return;

        Vector2 size = new Vector2(1f, 1.75f);
        Vector2 direction = this.movement.direction;

        bool isOccupied = movement.Occupied(direction);

        Gizmos.color = isOccupied ? Color.red : Color.green;

        Gizmos.DrawWireCube(transform.position + (Vector3)direction * 1.5f, size);
    }
}