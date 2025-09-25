using UnityEngine;

namespace Roombie.CharacterSelect
{
    [CreateAssetMenu(fileName = "PlayerPrefsSelectionPersistence", menuName = "Roombie/Selection/PlayerPrefs Persistence")]
    public class PlayerPrefsSelectionPersistenceAsset : SelectionPersistenceAsset
    {
        public string keyPrefix = "SelectedCharacter_Player";

        public override void Save(int slot1Based, string characterName, string skinName)
        {
            if (slot1Based <= 0) return;
            PlayerPrefs.SetString($"{keyPrefix}{slot1Based}_Name", characterName ?? "");
            PlayerPrefs.SetString($"{keyPrefix}{slot1Based}_Skin", skinName ?? "");
        }

        public override void Clear(int slot1Based)
        {
            if (slot1Based <= 0) return;
            PlayerPrefs.DeleteKey($"{keyPrefix}{slot1Based}_Name");
            PlayerPrefs.DeleteKey($"{keyPrefix}{slot1Based}_Skin");
        }

        public override void Flush() => PlayerPrefs.Save();
    }
}