using System.Reflection.Emit;
using HarmonyLib;
using NeonLite.Modules;
using TMPro;
using UnityEngine;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class Miracle
    {
        const bool priority = false;
        const bool active = true;

        static void Setup()
        {
            var settingI = NeonLite.Settings.Add(Settings.h, "Testing", "miracleOverride", "Miracle Count", null, 0, true);
            if (Settings.testMode)
            {
                settingI.OnEntryValueChanged.Subscribe((_, after) => miracleCount = after);
                miracleCount = settingI.Value;
            }
        }

        static void Activate(bool _)
        {
            Patching.AddPatch(typeof(LevelRush), "CanUseMiracle", CanUseMiracle, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(LevelRush), "MiraclesLeft", MiraclesLeft, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(PlayerUICardHUD), "UpdateHUD", CheckMiracleText, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MiracleButton), "OnMiracleSelected", PotentiallyUseMiracle, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MenuScreenMiracle), "OnSetVisible", CheckAllMiracles, Patching.PatchTarget.Prefix);
        }


        static TextMeshPro miracleText;
        static void CheckMiracleText(PlayerUICardHUD __instance, PlayerCard card)
        {
            PlayerCardData cardData = card.data;
            if (cardData.discardAbility == PlayerCardData.DiscardAbility.Miracle)
            {
                // oh boy miracle UI time
                var firstAb = __instance.abilityIcon[0];
                if (!miracleText)
                {
                    miracleText = UnityEngine.Object.Instantiate(__instance.textAmmo.gameObject).GetComponentInChildren<TextMeshPro>();
                    miracleText.transform.parent = firstAb.abilityIconRenderer.transform.parent;
                    miracleText.transform.localRotation = Quaternion.identity;
                    miracleText.verticalAlignment = VerticalAlignmentOptions.Middle;
                    miracleText.fontSizeMax = 4.5f;
                    Vector3 off = new(0.35f, 0.53f);
                    miracleText.transform.localPosition = firstAb.abilityIconRenderer.transform.localPosition + off;
                }
                miracleText.text = $"<size=80%>x</size>{miracleCount}";
                miracleText.gameObject.SetActive(true);
                firstAb.SetAbility(card, true);
                __instance.abilityIcon.Skip(1).Do(x => x.abilityIconRenderer.gameObject.SetActive(false));
            }
            else if (miracleText)
            {
                miracleText.gameObject.SetActive(false);
                // __instance.abilityIcon.Do(x => x.abilityIconRenderer.gameObject.SetActive(true));
            }
        }

        internal static int miracleCount = 0;
        static bool CanUseMiracle(ref bool __result)
        {
            __result = miracleCount > 0;
            return false;
        }
        static bool MiraclesLeft(ref int __result)
        {
            __result = miracleCount;
            return false;
        }
        static void PotentiallyUseMiracle(MiracleButton __instance)
        {
            if (__instance.cardToShowcase.discardAbility != PlayerCardData.DiscardAbility.Back)
                miracleCount--;
        }
        static void CheckIfMiracleShown(MiracleButton __instance)
        {
            if (__instance.cardToShowcase.discardAbility == PlayerCardData.DiscardAbility.Back)
                return;
            var button = __instance.transform.parent.GetComponent<MenuButtonHolder>();
            bool check = true;
            Cards.CheckPickup(__instance.cardToShowcase, ref check);
            button.gameObject.SetActive(check);
        }

        static void CheckAllMiracles(MenuScreenMiracle __instance)
        {
            foreach (var button in __instance.buttonsToLoad)
            {
                var miracle = button.ButtonRef?.GetComponent<MiracleButton>();
                if (!miracle)
                    continue;
                CheckIfMiracleShown(miracle);
            }
        }
    }
}
