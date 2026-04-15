using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NWArchipelago.UI.Testing
{
    internal class CardSelect : MonoBehaviour
    {
        public TMPro.TextMeshProUGUI cardName;
        public ColorToggle fire;
        public ColorToggle discard;

        string cardID;

        public void SetCard(string name, string ID, Color color)
        {
            cardName.text = name;
            cardName.color = color;
            cardID = ID;

            fire.SetColor(color);
            discard.SetColor(color);
        }
    }
}
