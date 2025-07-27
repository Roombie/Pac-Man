using UnityEngine;
using UnityEngine.EventSystems;

public class BaseMenuPanel : MonoBehaviour, IMenuPanel
{
    [SerializeField] protected GameObject defaultFirstSelected;

    private GameObject lastSelected;
    private GameObject overrideFirstSelected;

    public virtual void OnEnter()
    {
        GameObject toSelect = overrideFirstSelected ?? lastSelected ?? defaultFirstSelected;

        if (toSelect != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(toSelect);
        }

        overrideFirstSelected = null;
    }

    public virtual void OnExit()
    {
        lastSelected = EventSystem.current.currentSelectedGameObject;
    }

    public virtual bool CanLeave() => true;

    public void OverrideFirstSelected(GameObject target)
    {
        overrideFirstSelected = target;
    }
}