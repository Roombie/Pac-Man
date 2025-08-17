using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PauseUIController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject pauseMenu;

    public void ShowPause()
    {
        if (pauseMenu != null) MenuManager.Instance.OpenMenu(pauseMenu);
    }

    public void HidePause()
    {
        MenuManager.Instance.CloseAll();
    }
}