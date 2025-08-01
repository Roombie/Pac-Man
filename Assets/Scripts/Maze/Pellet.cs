using UnityEngine;

public class Pellet : MonoBehaviour
{
    public int pelletID;
    public int points = 10;

    protected virtual void Eat()
    {
        GameManager.Instance.PelletEaten(this);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Pacman")){
            Eat();
        }
    }
}
