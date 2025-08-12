using UnityEngine;

public class Ghost : MonoBehaviour
{
    public Movement movement;
    private Pacman pacman;
    public enum GhostType
    {
        Blinky,
        Inky,
        Pinky,
        Clyde
    }

    public enum Mode
    {
        Home,
        Scatter,
        Chase,
        Frightened,
        Eaten
    }

    Mode currentMode = Mode.Home;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
