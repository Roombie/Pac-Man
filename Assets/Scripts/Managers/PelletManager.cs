using System.Collections.Generic;
using UnityEngine;

public class PelletManager : MonoBehaviour
{
    [Header("Pellet Settings")]
    [SerializeField] private Transform pelletsParent; // The object that contains the pellets in the hierarchy
    private readonly List<Pellet> allPellets = new();
    private Dictionary<int, HashSet<Vector2>> pelletStatesPerPlayer = new();

    private int remainingPellets;

    public System.Action OnAllPelletsCollected;

    private void Awake()
    {
        CollectAllPellets();
    }

    public void CachePelletLayout(int playerId)
    {
        var layout = new HashSet<Vector2>();

        foreach (Pellet pellet in allPellets)
        {
            if (pellet.gameObject.activeSelf)
                layout.Add(pellet.transform.position);
        }

        pelletStatesPerPlayer[playerId] = layout;
    }

    public void RestorePelletsForPlayer(HashSet<int> eatenPelletIDs)
    {
        remainingPellets = 0;

        foreach (var pellet in allPellets)
        {
            if (eatenPelletIDs.Contains(pellet.pelletID))
            {
                pellet.gameObject.SetActive(false);
            }
            else
            {
                pellet.gameObject.SetActive(true);
                remainingPellets++;
            }
        }

        Debug.Log($"PelletManager: Restored state. {remainingPellets} pellets active.");
    }

   private void CollectAllPellets()
    {
        allPellets.Clear();
        int id = 0;

        foreach (Transform child in pelletsParent)
        {
            if (child.TryGetComponent(out Pellet pellet))
            {
                pellet.pelletID = id++;  // Assign unique ID
                allPellets.Add(pellet);
            }
        }

        remainingPellets = allPellets.Count;
        Debug.Log($"PelletManager: {remainingPellets} pellets found.");
    }

    /// <summary>
    /// Call when Pac-Man eats a pellet.
    /// </summary>
    public void PelletEaten(Pellet pellet)
    {
        remainingPellets--;

        pellet.gameObject.SetActive(false);

        if (remainingPellets <= 0)
        {
            Debug.Log("PelletManager: All pellets were eaten!");
            OnAllPelletsCollected?.Invoke();
        }

        //GameManager.Instance.UpdateSiren(remainingPellets);
    }

    /// <summary>
    /// Resets all pellets for a new level or attempt.
    /// </summary>
    public void ResetPellets(HashSet<int> eatenPelletsByPlayer)
    {
        foreach (var pellet in allPellets)
        {
            pellet.gameObject.SetActive(!eatenPelletsByPlayer.Contains(pellet.pelletID));
        }

        remainingPellets = 0;
        foreach (var pellet in allPellets)
        {
            if (pellet.gameObject.activeSelf)
                remainingPellets++;
        }

        Debug.Log($"[PelletManager] Reset for turn. Pellets visible: {remainingPellets}");
    }


    public bool HasRemainingPellets()
    {
        return remainingPellets > 0;
    }

    public int RemainingPelletCount()
    {
        return remainingPellets;
    }
}