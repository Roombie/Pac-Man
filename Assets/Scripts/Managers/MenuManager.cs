using UnityEngine;
using System.Collections.Generic;

public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance;

    public static event System.Action<GameObject, GameObject> OnMenuChanged;

    private Stack<GameObject> menuStack = new();
    private bool isInteractionBlocked = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        EventManager<bool>.RegisterEvent(EventKey.UIInteractionChanged, OnInteractionBlocked);
    }

    private void OnDestroy()
    {
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

        GameObject previous = menuStack.Count > 0 ? menuStack.Peek() : null;

        // If stack is not on top, delete to reopen
        if (menuStack.Contains(menu))
        {
            Debug.Log($"[MenuManager] Menu '{menu.name}' ya estaba en el stack. Removiendo instancias previas para reabrirlo correctamente.");
            Stack<GameObject> tempStack = new Stack<GameObject>();
            while (menuStack.Count > 0)
            {
                GameObject top = menuStack.Pop();
                if (top == menu)
                {
                    top.GetComponent<IMenuPanel>()?.OnExit();
                    top.SetActive(false);
                    break;
                }
                else
                {
                    tempStack.Push(top);
                    top.GetComponent<IMenuPanel>()?.OnExit();
                    top.SetActive(false);
                }
            }

            while (tempStack.Count > 0)
            {
                menuStack.Push(tempStack.Pop());
            }

            previous = menuStack.Count > 0 ? menuStack.Peek() : null;
        }

        // Verificar si el menú actual permite salir
        // Verify if current menu allows you to exit
        if (previous != null)
        {
            var currentPanel = previous.GetComponent<IMenuPanel>();
            if (currentPanel != null && !currentPanel.CanLeave())
            {
                Debug.Log("Current menu prevents leaving.");
                return;
            }

            currentPanel?.OnExit();
            previous.SetActive(false);
        }

        menu.SetActive(true);
        menuStack.Push(menu);
        menu.GetComponent<IMenuPanel>()?.OnEnter();

        OnMenuChanged?.Invoke(menu, previous);
        DebugStack();
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

        OnMenuChanged?.Invoke(previous, current);
        DebugStack();
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

    public void ResetToMenu(GameObject menu)
    {
        while (menuStack.Count > 0)
        {
            GameObject m = menuStack.Pop();
            m.GetComponent<IMenuPanel>()?.OnExit();
            m.SetActive(false);
        }

        menu.SetActive(true);
        menuStack.Push(menu);
        menu.GetComponent<IMenuPanel>()?.OnEnter();

        OnMenuChanged?.Invoke(menu, null);
        DebugStack();
    }

    public bool CanGoBack()
    {
        return !isInteractionBlocked && menuStack.Count > 1;
    }

    public GameObject CurrentMenu => menuStack.Count > 0 ? menuStack.Peek() : null;

    private void DebugStack()
    {
        Debug.Log("Menu Stack:");
        foreach (var m in menuStack)
        {
            Debug.Log($" - {m.name} (active={m.activeSelf})");
        }
    }
}