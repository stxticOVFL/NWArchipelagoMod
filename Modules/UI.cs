using System.Collections.Generic;
using System.Linq;
using NeonLite;
using NeonLite.Modules;
using NWArchipelago.UI.Testing;
using UnityEngine;
using static MainMenu;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class UI
    {
        const bool priority = false;
        const bool active = true;

        static void Activate(bool _)
        {
            var c = MainMenu.Instance().GetComponentInChildren<Canvas>();
            var cgO = NWArchipelago.bundle.LoadAsset<GameObject>("Assets/Prefabs/CanvasGroup.prefab");
            cgO = UnityEngine.Object.Instantiate(cgO, c.transform);
            cgO.name = "NWArchipelago";
            cgO.transform.localPosition = Vector3.zero;
            cgO.transform.localScale = Vector3.one;

            Cards.SetupTestPanel();

            Patching.AddPatch(typeof(MainMenu), "SetState", MainMenuState, Patching.PatchTarget.Postfix);
            MainMenuState(MainMenu.Instance().GetCurrentState());
        }

        static readonly HashSet<State> testingStates = [State.Staging, State.Results, State.Pause];
        static void MainMenuState(State newState)
        {
            TestPanel.i.gameObject.SetActive(testingStates.Contains(newState) && Settings.testing.Value);
        }
    }
}
