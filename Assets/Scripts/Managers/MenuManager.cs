using UnityEngine;
using System.Collections.Generic;

public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance;

    private Stack<GameObject> menuStack = new();
    private bool isInteractionBlocked = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Register listener for UI interaction changes
        EventManager<bool>.RegisterEvent(EventKey.UIInteractionChanged, OnInteractionBlocked);
    }

    private void OnDestroy()
    {
        // Unregister listener
        EventManager<bool>.UnregisterEvent(EventKey.UIInteractionChanged, OnInteractionBlocked);
    }

    private void OnInteractionBlocked(bool isBlocked)
    {
        isInteractionBlocked = isBlocked;
    }

    public void OpenMenu(GameObject menu)
    {
        if (isInteractionBlocked)
        {
            Debug.LogWarning("Cannot change menu while UI interaction is active.");
            return;
        }

        if (menuStack.Count > 0)
        {
            GameObject current = menuStack.Peek();
            IMenuPanel currentPanel = current.GetComponent<IMenuPanel>();
            if (currentPanel != null && !currentPanel.CanLeave())
            {
                Debug.Log("Current menu prevents leaving.");
                return;
            }

            currentPanel?.OnExit();
            current.SetActive(false);
        }

        menu.SetActive(true);
        menuStack.Push(menu);
        menu.GetComponent<IMenuPanel>()?.OnEnter();
    }

    public void Back()
    {
        if (isInteractionBlocked)
        {
            Debug.LogWarning("UI interaction is active, cannot go back.");
            return;
        }

        if (menuStack.Count <= 1)
        {
            Debug.LogWarning("Only one or no menus in stack.");
            return;
        }

        GameObject current = menuStack.Pop();
        current.GetComponent<IMenuPanel>()?.OnExit();
        current.SetActive(false);

        GameObject previous = menuStack.Peek();
        previous.SetActive(true);
        previous.GetComponent<IMenuPanel>()?.OnEnter();
    }

    public void CloseAll()
    {
        while (menuStack.Count > 0)
        {
            GameObject m = menuStack.Pop();
            m.GetComponent<IMenuPanel>()?.OnExit();
            m.SetActive(false);
        }
    }
}