using System.Collections;
using UnityEngine;
using System;

public class MazeFlashController : MonoBehaviour
{
    [Header("Maze GameObjects")]
    [SerializeField] private GameObject maze;          // Default maze
    [SerializeField] private GameObject mazeBlue;      // Ghost gate open
    [SerializeField] private GameObject mazeWhite;     // Flash maze

    [Header("Flash Settings")]
    [SerializeField] private int mazeFlashCount = 4;   // Total flashes (toggle count)
    [SerializeField] private float mazeFlashInterval = 0.2f; // Time between flashes

    private bool isFlashing = false;

    /// <summary>
    /// Starts the flash animation.
    /// </summary>
    /// <param name="onComplete">Optional callback when the flash animation is done.</param>
    public void StartFlash(Action onComplete = null)
    {
        if (!isFlashing)
        {
            StartCoroutine(PlayFlashAnimation(onComplete));
        }
    }

    /// <summary>
    /// Plays the maze flash animation.
    /// </summary>
    /// <param name="onComplete">Optional callback when done.</param>
    private IEnumerator PlayFlashAnimation(Action onComplete = null)
    {
        isFlashing = true;

        // Disable the default maze while flashing
        maze.SetActive(false);

        for (int i = 0; i < mazeFlashCount; i++)
        {
            mazeBlue.SetActive(false);
            mazeWhite.SetActive(true);

            yield return new WaitForSeconds(mazeFlashInterval);

            mazeBlue.SetActive(true);
            mazeWhite.SetActive(false);

            yield return new WaitForSeconds(mazeFlashInterval);
        }

        // Ensure the maze ends up in the "Blue" state
        mazeBlue.SetActive(true);
        mazeWhite.SetActive(false);

        isFlashing = false;

        // Invoke callback (optional)
        onComplete?.Invoke();
    }

    /// <summary>
    /// Resets the maze to the default state.
    /// </summary>
    public void ResetMaze()
    {
        StopAllCoroutines();
        isFlashing = false;

        maze.SetActive(true);
        mazeBlue.SetActive(false);
        mazeWhite.SetActive(false);
    }

    /// <summary>
    /// Returns true if the maze is currently flashing.
    /// </summary>
    public bool IsFlashing()
    {
        return isFlashing;
    }
}