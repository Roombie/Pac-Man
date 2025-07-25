using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Animator))]
public class AnimationEndInputTrigger : MonoBehaviour
{
    public string animationName = "MyAnimation";
    public UnityEvent onFinalInput;

    private Animator animator;
    private Pacman input;

    private bool hasJumpedToEnd = false;
    private bool eventTriggered = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        input = new Pacman();
    }

    private void OnEnable()
    {
        input.Enable();
    }

    private void OnDisable()
    {
        input.Disable();
    }

    private void Start()
    {
        animator.Play(animationName, 0, 0f); // Start animation from beginning
        hasJumpedToEnd = false;
        eventTriggered = false;
    }

    private void Update()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        if (!state.IsName(animationName))
            return;

        if (input.UI.Submit.WasPressedThisFrame())
        {
            if (!hasJumpedToEnd && state.normalizedTime < 1f)
            {
                // Jump to last frame
                animator.Play(animationName, 0, 0.999f); // Just before 1 to allow time for Update to detect end
                hasJumpedToEnd = true;
            }
            else if (!eventTriggered && state.normalizedTime >= 1f)
            {
                // Trigger the final event
                eventTriggered = true;
                onFinalInput?.Invoke();
            }
        }
    }
}
