using UnityEngine;

public class ArrowIndicator : MonoBehaviour
{
    [Header("Indicator References")]
    [SerializeField] private Transform pivot;
    [SerializeField] private Transform arrow;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Settings")]
    [SerializeField] private float distance = 1.25f;
    
    private Vector2 lastDir = Vector2.right;

    private void Awake()
    {
        if (spriteRenderer == null)
        {
            Debug.LogError("SpriteRenderer not found on ArrowIndicator!");
        }
    }

    public void UpdateIndicator(Vector2 direction)
    {
        if (arrow == null) return;

        Vector2 dir = (direction == Vector2.zero) ? lastDir : direction.normalized;
        lastDir = dir;

        Vector3 indicatorPos = (Vector3)dir * distance;
        arrow.localPosition = indicatorPos;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        arrow.localRotation = Quaternion.Euler(0, 0, angle);

        if (!arrow.gameObject.activeSelf)
            arrow.gameObject.SetActive(true);
    }

    public void ResetIndicator()
    {
        if (arrow == null) return;
        arrow.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        arrow.gameObject.SetActive(false);

        if (pivot != null)
            pivot.localRotation = Quaternion.identity;
    }

    private void LateUpdate()
    {
        if (pivot != null)
            pivot.localRotation = Quaternion.Inverse(transform.localRotation);
    }

    public void SetColor(Color color)
    {
        spriteRenderer.color = color;
    }
}