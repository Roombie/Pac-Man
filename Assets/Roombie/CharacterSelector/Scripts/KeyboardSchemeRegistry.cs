using System.Collections.Generic;
using UnityEngine;

namespace Roombie.CharacterSelect
{
    [CreateAssetMenu(fileName = "KeyboardSchemeRegistry", menuName = "Roombie/Input/Keyboard Scheme Registry")]
    public class KeyboardSchemeRegistry : ScriptableObject
    {
        [Tooltip("Order defines which keyboard scheme belongs to each panel index.")]
        public List<string> schemeNames = new() { "P1Keyboard", "P2Keyboard" };

        public string ForPanel(int i)
        {
            return (i >= 0 && i < schemeNames.Count) ? schemeNames[i] : null;
        }

        public string[] AllAsArray()
        {
            return schemeNames.ToArray();
        }
    }
}