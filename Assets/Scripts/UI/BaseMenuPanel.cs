using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class BaseMenuPanel : MonoBehaviour, IMenuPanel
{
    [SerializeField] protected GameObject defaultFirstSelected;

    private GameObject lastSelected;
    private GameObject overrideFirstSelected;

    public virtual void OnEnter()
    {
        StartCoroutine(SelectDefaultNextFrame());
    }

    private IEnumerator SelectDefaultNextFrame()
    {
        yield return null;
        yield return null;

        GameObject toSelect = overrideFirstSelected != null
            ? overrideFirstSelected
            : defaultFirstSelected;

        if (toSelect != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(toSelect);
        }
        else
        {
            Debug.LogWarning($"No valid selectable object in {gameObject.name}");
        }

        overrideFirstSelected = null;
    }

    public virtual void OnExit()
    {
        // For now, I don't need anything
        // This is used to do something when you exit the menu
    }

    public virtual bool CanLeave() => true;

    public void OverrideFirstSelected(GameObject target)
    {
        overrideFirstSelected = target;
    }
}