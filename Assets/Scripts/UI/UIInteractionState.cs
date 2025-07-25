using UnityEngine;
using System.Collections.Generic;

public static class UIInteractionState
{
    private static readonly HashSet<Object> activeInteractors = new();

    public static void RegisterActive(Object component)
    {
        activeInteractors.Add(component);
    }

    public static void UnregisterActive(Object component)
    {
        activeInteractors.Remove(component);
    }

    public static bool IsAnyInteractionActive() => activeInteractors.Count > 0;
}