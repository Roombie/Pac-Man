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
    private bool isPlaying;

    void Awake()
    {
        foreach (var cb in coffeeBreaks)
            if (!coffeeBreakLookup.ContainsKey(cb.round))
                coffeeBreakLookup.Add(cb.round, cb);
    }

    public void StartCoffeeBreak(int round)
    {
        if (!coffeeBreakLookup.TryGetValue(round, out CoffeeBreakData coffeeBreak))
        {
            Debug.Log("No coffee break for round: " + round);
            GameManager.Instance.StartNextLevel();
            return;
        }

        Debug.Log($"Starting coffee break for round: {round}");
        GameManager.Instance.SetState(GameManager.GameState.Intermission);

        if (maze) maze.SetActive(false);
        if (ui) ui.SetActive(false);

        if (coffeeBreak.cutsceneRoot) coffeeBreak.cutsceneRoot.SetActive(true);

        currentCoffeeBreak = coffeeBreak;
        isPlaying = true;

        var director = coffeeBreak.cutsceneDirector;
        if (director)
        {
            director.stopped -= OnCoffeeBreakEnd;
            director.stopped += OnCoffeeBreakEnd;
            director.Play();
        }
        else
        {
            OnCoffeeBreakEnd(null);
        }
    }

    private void OnCoffeeBreakEnd(PlayableDirector director)
    {
        if (!isPlaying) return; // guard double invoke
        isPlaying = false;

        Debug.Log("Coffee break ended!");

        if (currentCoffeeBreak != null)
        {
            if (currentCoffeeBreak.cutsceneRoot)
                currentCoffeeBreak.cutsceneRoot.SetActive(false);

            if (currentCoffeeBreak.cutsceneDirector)
                currentCoffeeBreak.cutsceneDirector.stopped -= OnCoffeeBreakEnd;

            currentCoffeeBreak = null;
        }

        if (maze) maze.SetActive(true);
        if (ui) ui.SetActive(true);

        GameManager.Instance.SetState(GameManager.GameState.Intro);
        GameManager.Instance.StartNextLevel();
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