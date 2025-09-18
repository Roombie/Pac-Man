using UnityEngine;

[System.Serializable]
public class PanelState
{
    public int PlayerIndex { get; set; }
    public int CharacterIndex { get; set; }
    public int SkinIndex { get; set; }
    public bool HasSelectedCharacter { get; set; }
    public bool HasConfirmedSkin { get; set; }
    public bool HasConfirmedFinal { get; set; }

    public void Reset()
    {
        CharacterIndex = 0;
        SkinIndex = 0;
        HasSelectedCharacter = false;
        HasConfirmedSkin = false;
        HasConfirmedFinal = false;
    }
}