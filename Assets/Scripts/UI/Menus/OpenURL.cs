using UnityEngine;
using UnityEngine.UI;

public class OpenURL : MonoBehaviour
{
    public void OpenWebsite(string url)
    {
        Application.OpenURL(url);
    }
} 