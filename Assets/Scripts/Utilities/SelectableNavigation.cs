using UnityEngine;
using UnityEngine.UI;

public static class SelectableNavigation
{
    /// <summary>
    /// Hides a control the menus' explicit navigation points at. Explicit navigation can't skip
    /// a hidden control, so every neighbour that leads to it is pointed at whatever lies beyond it
    /// </summary>
    public static void HideAndRouteAround(GameObject hidden)
    {
        if (hidden == null) return;

        foreach (var selectable in hidden.GetComponentsInChildren<Selectable>(true))
            RouteAround(selectable);

        hidden.SetActive(false);
    }

    private static void RouteAround(Selectable hidden)
    {
        var nav = hidden.navigation;

        foreach (var other in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other == hidden || other.navigation.mode != Navigation.Mode.Explicit) continue;

            var n = other.navigation;
            if (n.selectOnUp == hidden) n.selectOnUp = nav.selectOnUp;
            if (n.selectOnDown == hidden) n.selectOnDown = nav.selectOnDown;
            if (n.selectOnLeft == hidden) n.selectOnLeft = nav.selectOnLeft;
            if (n.selectOnRight == hidden) n.selectOnRight = nav.selectOnRight;
            other.navigation = n;
        }
    }
}
