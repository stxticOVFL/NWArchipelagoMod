using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Timers;

namespace NWArchipelago.UI.Testing
{
    internal class ColorToggle : MonoBehaviour
    {
        internal Toggle toggle;
        TextMeshProUGUI text;
        Graphic coloredBG;

        Color color;
        Color onColor;

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            coloredBG = toggle.graphic;
            text = GetComponentInChildren<TextMeshProUGUI>();
            onColor = text.color;
            toggle.onValueChanged.AddListener(OnToggle);
        }

        void Start()
        {
            OnToggle(toggle.isOn);
        }

        public void SetColor(Color color)
        {
            this.color = color;
            coloredBG.color = color;
            if (!toggle.isOn)
                text.color = color;
        }

        public void OnToggle(bool on)
        {
            if (on)
                text.color = onColor;
            else
                text.color = color;
        }
    }
}
