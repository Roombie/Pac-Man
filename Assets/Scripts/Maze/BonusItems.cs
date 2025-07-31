using UnityEngine;

public class BonusItem : MonoBehaviour
{
    public int points = 100;
    public AudioClip fruitEaten;
    public GameObject pointsPopupPrefab;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Pacman"))
        {
            Debug.Log("Collected bonus item! +" + points + " points");
            GameManager.Instance.AddScore(points);
            if (pointsPopupPrefab != null)
            {
                Instantiate(pointsPopupPrefab, transform.position, Quaternion.identity);
            }
            AudioManager.Instance.Play(fruitEaten, SoundCategory.SFX);
            Destroy(gameObject);
        }
    }
}