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

        public void SetCard(string name, Color color)
        {
            cardName.text = name;
            cardName.color = color;

            fire.SetColor(color);
            discard.SetColor(color);
        }
    }
}
