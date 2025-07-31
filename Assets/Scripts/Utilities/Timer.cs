using System;

public class Timer
{
    public float RemainingSeconds { get; private set; }
    
    private bool isFinished = false; 

    public Timer(float duration)
    {
        RemainingSeconds = duration;
    }

    public event Action OnTimerEnd;

    public void Tick(float deltaTime)
    {
        if (isFinished) return;

        RemainingSeconds -= deltaTime;
        CheckForTimerEnd();
    }

    private void CheckForTimerEnd()
    {
        if (RemainingSeconds > 0f) return;

        RemainingSeconds = 0f;
        isFinished = true;
        OnTimerEnd?.Invoke();
    }

    public void Reset(float newDuration)
    {
        RemainingSeconds = newDuration;
        isFinished = false;
    }
}