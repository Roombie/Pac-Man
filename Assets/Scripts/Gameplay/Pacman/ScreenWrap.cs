using UnityEngine;

public class ScreenWrap : MonoBehaviour
{
    [Tooltip("How far the object must go past the screen before wrapping (in world units).")]
    public float margin = 0.5f;

    private Camera mainCamera;
    private Vector2 screenBounds;

    void Start()
    {
        mainCamera = Camera.main;
        CalculateScreenBounds(); // We get the screen bounds as soon as the game starts
    }

    void Update()
    {
        Wrap();
    }

    void CalculateScreenBounds()
    {
        // First, we divide the screen's width between the screen's height to get the aspect ratio
        // Then get camera's height, which is just the ortographic size and multiply it by 2
        // We use the camera size and aspect ratio to determine how far the edges of the screen are in world space.
        // Thanks to that now we can store these as screenBounds.x (horizontal screen bounds) and screenBounds.y (vertical screen bounds).
        // This video was really useful to get it how I needed it: https://youtu.be/1a9ag16PeFw (thanks Sunny Valley Studio)
        float screenAspect = (float)Screen.width / Screen.height;
        float camHeight = mainCamera.orthographicSize * 2;
        screenBounds = new Vector2(camHeight * screenAspect / 2f, camHeight / 2f);
    }

    void Wrap()
    {
        Vector3 pos = transform.position; // Get player's current position

        bool wrapped = false; // starts false to avoid wrong wrapping

        if (pos.x > screenBounds.x + margin) // right margin
        {
            pos.x = -screenBounds.x - margin; // go left
            wrapped = true;
        }
        else if (pos.x < -screenBounds.x - margin) // left margin
        {
            pos.x = screenBounds.x + margin; // go right
            wrapped = true;
        }

        if (pos.y > screenBounds.y + margin) // up margin
        {
            pos.y = -screenBounds.y - margin; // go down
            wrapped = true;
        }
        else if (pos.y < -screenBounds.y - margin) // down margin
        {
            pos.y = screenBounds.y + margin; // go up
            wrapped = true;
        }

        if (wrapped) // Everytime wrapped is true, change player's position to it's new position
        {
            transform.position = pos;
        }
    }
}