using UnityEngine;

public class PowerPellet : Pellet
{
    public float duration = 8f;

    protected override void Eat()
    {
        PelletHandler.Instance.PowerPelletEaten(this);
    }
}