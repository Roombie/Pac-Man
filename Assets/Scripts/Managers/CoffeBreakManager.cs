using UnityEngine;
using UnityEngine.Playables;
using System.Collections.Generic;

public class CoffeeBreakManager : MonoBehaviour
{
    [Header("References")]
    public GameObject maze;
    public GameObject ui;

    [Header("Coffee Breaks by Round")]
    public List<CoffeeBreakData> coffeeBreaks = new();

    private readonly Dictionary<int, CoffeeBreakData> coffeeBreakLookup = new();
    private CoffeeBreakData currentCoffeeBreak;

    void Awake()
    {
        foreach (var cb in coffeeBreaks)
        {
            if (!coffeeBreakLookup.ContainsKey(cb.round))
            {
                coffeeBreakLookup.Add(cb.round, cb);
            }
        }
    }

    public void StartCoffeeBreak(int round)
    {
        if (!coffeeBreakLookup.TryGetValue(round, out CoffeeBreakData coffeeBreak))
        {
            Debug.Log("No coffee break for round: " + round);
            // GameManager.Instance.StartNextLevel();
            return;
        }

        Debug.Log($"Starting coffee break for round: {round}");

        // GameManager.Instance.SetState(GameManager.GameState.Intermission);

        maze.SetActive(false);
        ui.SetActive(false);

        coffeeBreak.cutsceneRoot?.SetActive(true);

        currentCoffeeBreak = coffeeBreak;

        var director = coffeeBreak.cutsceneDirector;
        director.stopped -= OnCoffeeBreakEnd;
        director.stopped += OnCoffeeBreakEnd;

        director.Play();
    }

    private void OnCoffeeBreakEnd(PlayableDirector director)
    {
        Debug.Log("Coffee break ended!");

        if (currentCoffeeBreak != null)
        {
            currentCoffeeBreak.cutsceneRoot?.SetActive(false);
            currentCoffeeBreak.cutsceneDirector.stopped -= OnCoffeeBreakEnd;

            currentCoffeeBreak = null;
        }

        maze.SetActive(true);
        ui.SetActive(true);

        // GameManager.Instance.SetState(GameManager.GameState.Transition);
        // GameManager.Instance.StartNextLevel();
    }

    public bool HasCoffeeBreakForLevel(int level)
    {
        return coffeeBreakLookup.ContainsKey(level);
    }
}

[System.Serializable]
public class CoffeeBreakData
{
    public int round;
    public GameObject cutsceneRoot;
    public PlayableDirector cutsceneDirector;
}