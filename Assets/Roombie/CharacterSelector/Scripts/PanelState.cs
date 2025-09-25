using UnityEngine;

public enum PanelJoinState { Idle, Joined, CharacterSelected, SkinConfirmed }

[System.Serializable]
public class PanelState
{
    public int CharacterIndex { get; set; }
    public int SkinIndex { get; set; }
    public bool HasSelectedCharacter { get; set; }
    public bool HasConfirmedSkin { get; set; }
    public bool HasConfirmedFinal { get; set; }
    public PanelJoinState Phase = PanelJoinState.Idle;

    public void Reset()
    {
        CharacterIndex = 0;
        SkinIndex = 0;
        HasSelectedCharacter = false;
        HasConfirmedSkin = false;
        HasConfirmedFinal = false;
        Phase = PanelJoinState.Idle;
    }
}