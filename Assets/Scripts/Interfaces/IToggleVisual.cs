using UnityEngine;

public interface IToggleVisual
{
    void SetOn(bool isOn);
    void SetPressed(bool pressed);
    void RefreshNow();
}