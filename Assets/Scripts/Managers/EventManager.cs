using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A generic static event manager that uses strings as event keys and supports event arguments.
/// </summary>
public static class EventManager<TEventArgs>
{
    // Dictionary to hold all event types and their corresponding handlers
    private static Dictionary<string, Action<TEventArgs>> eventDictionary = new();

    /// <summary>
    /// Registers a listener to the specified event.
    /// </summary>
    /// <param name="eventType">The string key identifying the event.</param>
    /// <param name="eventHandler">The method to call when the event is triggered.</param>
    public static void RegisterEvent(string eventType, Action<TEventArgs> eventHandler)
    {
        if (eventDictionary.ContainsKey(eventType))
        {
            eventDictionary[eventType] += eventHandler;
        }
        else
        {
            eventDictionary[eventType] = eventHandler;
        }
    }

    /// <summary>
    /// Unregisters a listener from the specified event.
    /// </summary>
    /// <param name="eventType">The string key identifying the event.</param>
    /// <param name="eventHandler">The method to remove from the event's call list.</param>
    public static void UnregisterEvent(string eventType, Action<TEventArgs> eventHandler)
    {
        if (eventDictionary.ContainsKey(eventType))
        {
            eventDictionary[eventType] -= eventHandler;
        }
    }

    /// <summary>
    /// Triggers the specified event and passes along any relevant event data.
    /// </summary>
    /// <param name="eventType">The string key identifying the event.</param>
    /// <param name="eventArgs">The event data to pass to listeners.</param>
    public static void TriggerEvent(string eventType, TEventArgs eventArgs)
    {
        if (eventDictionary.ContainsKey(eventType))
        {
            eventDictionary[eventType]?.Invoke(eventArgs);
        }
    }

    /// <summary>
    /// Removes all registered event listeners. Use with caution.
    /// </summary>
    public static void ClearAll()
    {
        eventDictionary.Clear();
    }
}

public static class EventKey
{
    public const string UIInteractionChanged = "UIInteractionChanged";
}