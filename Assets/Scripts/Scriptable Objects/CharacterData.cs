using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;

    [Header("Animator Settings")]
    public RuntimeAnimatorController animatorController;

    [Header("Character Configuration")]
    public Sprite lifeIconSprite;
    public RuntimeAnimatorController playerAnimatorController;
}