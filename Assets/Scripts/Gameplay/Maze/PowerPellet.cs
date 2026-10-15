using UnityEngine;

public class PowerPellet : Pellet
{
    protected override void Eat()
    {
        PelletHandler.Instance.PowerPelletEaten(this);
    }
}