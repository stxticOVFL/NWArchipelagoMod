using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NWArchipelago.UI.Testing
{
    internal class TestPanel : MonoBehaviour
    {
        public static bool setup = false;

        internal static TestPanel i;
        void Awake()
        {
            i = this;
            cardSelectBase.SetActive(false);
        }

        public GameObject cardSelectBase;

        public delegate void OnToggled(int ID, bool state);
        static internal event OnToggled OnPressToggle;

        public static int id = 0;

        public readonly List<ColorToggle> toggles = new List<ColorToggle>();

        public CardSelect AddCard(string name, Color color, bool hasFire, bool hasDiscard)
        {
            var go = Instantiate(cardSelectBase, cardSelectBase.transform.parent);
            go.SetActive(true);
            var cs = go.GetComponent<CardSelect>();
            cs.SetCard(name, color);

            if (hasFire) {
                var copied = ++id;
                toggles.Add(cs.fire);
                cs.fire.toggle.onValueChanged.AddListener(x => OnPressToggle?.Invoke(copied, x));
            }
            else
                cs.fire.gameObject.SetActive(false);

            if (hasDiscard) {
                var copied = ++id;
                toggles.Add(cs.discard);
                cs.discard.toggle.onValueChanged.AddListener(x => OnPressToggle?.Invoke(copied, x));
            }
            else
                cs.discard.gameObject.SetActive(false);
            return cs;
        }
    }
}
