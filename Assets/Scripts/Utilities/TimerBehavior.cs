using System;
using UnityEngine;
using UnityEngine.Events;

namespace TimerSystem
{
    public class TimerBehavior : MonoBehaviour
    {
        [SerializeField] private float duration = 1f;
        [SerializeField] private UnityEvent onTimerEnd = null;

        private Timer timer;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void Start()
        {
            timer = new Timer(duration);
            timer.OnTimerEnd += HandleTimerEnd;
        }

        private void HandleTimerEnd()
        {
            onTimerEnd?.Invoke();
            Destroy(this);
        }

        // Update is called once per frame
        private void Update()
        {
            if (timer.RemainingSeconds > 0f)
            {
                timer.Tick(Time.deltaTime);
            }
                
        }
    }
}