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

        string baseText;

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            coloredBG = toggle.graphic;
            text = GetComponentInChildren<TextMeshProUGUI>();
            baseText = text.text;
            onColor = text.color;
            toggle.onValueChanged.AddListener(OnToggle);

            OnToggle(toggle.isOn);
        }

        void Start()
        {
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

        public void Disable(bool setDisabled) {
            toggle.SetIsOnWithoutNotify(false);
            toggle.interactable = !setDisabled;
            text.color = setDisabled ? Color.gray : color;
            text.text = setDisabled ? "X" : baseText;
        }
    }
}
