using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NWArchipelago.UI.Testing
{
    internal class TestPanel : MonoBehaviour
    {
        internal static TestPanel i;
        void Awake()
        {
            i = this;
            cardSelectBase.SetActive(false);
        }

        public GameObject cardSelectBase;

        public delegate void OnToggled(string ID, bool state);
        static internal event OnToggled OnFireToggle;
        static internal event OnToggled OnDiscardToggle;

        public CardSelect AddCard(string name, string ID, Color color, bool hasFire, bool hasDiscard)
        {
            var go = Instantiate(cardSelectBase, cardSelectBase.transform.parent);
            go.SetActive(true);
            var cs = go.GetComponent<CardSelect>();
            cs.SetCard(name, ID, color);

            if (hasFire)
                cs.fire.toggle.onValueChanged.AddListener(x => OnFireToggle?.Invoke(ID, x));
            else
                cs.fire.gameObject.SetActive(false);

            if (hasDiscard)
                cs.discard.toggle.onValueChanged.AddListener(x => OnDiscardToggle?.Invoke(ID, x));
            else
                cs.discard.gameObject.SetActive(false);
            return cs;
        }
    }
}
