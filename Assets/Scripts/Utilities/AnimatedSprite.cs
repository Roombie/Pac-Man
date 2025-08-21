using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class AnimatedSprite : MonoBehaviour
{
    public Sprite[] sprites;
    public float framerate = 1f / 6f;
    public bool loop = true; // Add this flag to control looping behavior

    private SpriteRenderer spriteRenderer;
    private int frame;
    private bool isAnimating = true;
    public bool IsPlaying => isAnimating && IsInvoking(nameof(Animate));

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        if (sprites == null || sprites.Length == 0) { enabled = false; return; }
        frame = 0;
        isAnimating = true;
        InvokeRepeating(nameof(Animate), framerate, framerate);
    }

    private void OnDisable()
    {
        CancelInvoke();
    }

    private void Animate()
    {
        if (!isAnimating)
            return;

        frame++;

        // Check for looping condition
        if (frame >= sprites.Length)
        {
            if (loop)
            {
                frame = 0;
            }
            else
            {
                frame = sprites.Length - 1; // Freeze on the last frame
                CancelInvoke(); // Stop animation if not looping
                return;
            }
        }

        if (frame >= 0 && frame < sprites.Length)
        {
            spriteRenderer.sprite = sprites[frame];
        }
    }

    public void Stop(bool resetToFirstFrame = false)
    {
        isAnimating = false;
        CancelInvoke(nameof(Animate));

        if (resetToFirstFrame && sprites != null && sprites.Length > 0)
        {
            frame = 0;
            spriteRenderer.sprite = sprites[0];
        }
    }


    public void PauseAnimation()
    {
        isAnimating = false;
    }

    public void ResumeAnimation()
    {
        isAnimating = true;
    }
}
