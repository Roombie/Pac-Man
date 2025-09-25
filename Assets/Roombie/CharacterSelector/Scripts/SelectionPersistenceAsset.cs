using UnityEngine;

namespace Roombie.CharacterSelect
{
    public abstract class SelectionPersistenceAsset : ScriptableObject
    {
        public abstract void Save(int slot1Based, string characterName, string skinName);
        public abstract void Clear(int slot1Based);
        public abstract void Flush();
    }
}