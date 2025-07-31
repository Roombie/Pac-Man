using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void PelletEaten(Pellet pellet)
    {
        // alternatePelletSound = !alternatePelletSound;
        //AudioManager.Instance.PlayPelletEatenSound(alternatePelletSound);
        //AddScore(pellet.points);
    }

    public void PowerPelletEaten(PowerPellet pellet)
    {

    }

    public void AddScore(int points)
    {
        
    }
}
