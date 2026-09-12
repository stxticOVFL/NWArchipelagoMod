using HarmonyLib;
using I2.Loc;
using MelonLoader;
using NeonLite;
using NeonLite.Modules;
using NWArchipelago.UI.Testing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace NWArchipelago.Modules
{
    [Module]
    internal static class Cards
    {
        const bool priority = true;
        const bool active = true;

        internal static HashSet<string> fires = [];
        internal static HashSet<string> discards = [];

        internal static List<CardPickup> cardPickups = [];

        static MelonPreferences_Entry<bool> gifts;
        internal static MelonPreferences_Entry<bool> tutorials;

        internal static void Setup()
        {
            gifts = NeonLite.Settings.Add(Settings.h, "Testing", "gifts", "Show Gifts", null, true, true);
            tutorials = NeonLite.Settings.Add(Settings.h, "", "tutorials", "Show Tutorials",
                "Show tutorial cards and the little card instructions that shows up when you pick up a card for the first time.",
                false);
        }

        internal static void Activate(bool _)
        {
            Patching.AddPatch(typeof(CardPickup), "SetCard", RegisterCard, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(CardPickup), "CanPickup", CheckPickup, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(LevelData), "GetIsDiscardLocked", CheckDiscardLocked, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(PlayerUICardHUD), "UpdateHUD", CheckAmmoText, Patching.PatchTarget.Postfix);
            Patching.AddPatch(Helpers.Method(typeof(MechController), "FireCard", [typeof(int)]), JamGun, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(PlayerCardDeck), "Initialize", InitializeWithSidearm, Patching.PatchTarget.Transpiler);

            Patching.AddPatch(typeof(CardPickupSpawner), "SpawnCard", EnsureGiftsSpawn, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(ObjectSpawner), "SpawnObject", EnsureDarkInsight, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(CardPickupSpawner), "SpawnCard", StopTutorial, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MechController), "DoCardPickup", CardShowcaseForce, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(MechController), "GetCardShowcaseCallback", ForceStaging, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MechController), "GetCanPickupAmmo", CheckAmmo, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MechController), "OnCollectAmmo", DirectAmmo, Patching.PatchTarget.Prefix);
        }

        internal static void Clear()
        {
            fires.Clear();
            discards.Clear();
            cardPickups.Clear();
        }

        internal static void OnLevelLoad(LevelData _)
        {
            cardPickups = [.. cardPickups.Where(c => c != null)];
        }


        internal static void RegisterCard(CardPickup __instance, bool __state = false)
        {
            if (!__state && !cardPickups.Contains(__instance))
                cardPickups.Add(__instance);

            bool check = true;
            CheckPickup(__instance.GetPlayerCardData(), ref check);
            __instance.uiCard.UICards[0].SetCollectible(check);
        }

        static readonly PlayerCardData.Type[] types = [
            PlayerCardData.Type.WeaponProjectile, PlayerCardData.Type.WeaponHitscan, PlayerCardData.Type.SpecialAbility
        ];

        internal static void CheckPickup(PlayerCardData data, ref bool __result)
        {
            if (!__result || !types.Contains(data.cardType))
                return;

            __result = fires.Contains(data.cardID) || discards.Contains(data.cardID);
            var lvl = Game.Instance.GetCurrentLevel();
            if (!lvl)
                return;

            var discardLock = lvl.discardLockData.FirstOrDefault(x => x.cards.Any(c => c == data));
            if (discardLock == null)
                return;

            if (discards.Contains(data.cardID) && discardLock.TestConditionLocked())
                __result = fires.Contains(data.cardID);
        }

        internal static bool JamGun(int handIndex, ref bool __result)
        {
            var deck = RM.mechController.GetPlayerCardDeck();
            var card = deck.GetCardInHand(handIndex);
            PlayerCardData cardData = card.data;
            if (fires.Contains(cardData.cardID) || cardData.cardType == PlayerCardData.Type.WeaponHitscan)
                return true;

            if (RM.mechController._bulletsFiredThisBurst++ <= 0)
            {
                RM.ui.ShakeUICard(deck.GetCardInHand(handIndex));
                AudioController.Play("CARD_DISCARD_FAIL");
            }

            __result = true;
            return false;
        }

        internal static void CheckDiscardLocked(LevelData __instance, PlayerCardData cardData, ref bool __result)
        {
            if (__result)
                return;

            if (cardData.discardAbility == PlayerCardData.DiscardAbility.None || cardData.cardType == PlayerCardData.Type.WeaponHitscan)
                __result = false;
            else
                __result = !discards.Contains(cardData.cardID);
        }

        static void CheckAmmoText(PlayerUICardHUD __instance, PlayerCard card)
        {
            PlayerCardData cardData = card.data;
            if (fires.Contains(cardData.cardID) || cardData.cardType == PlayerCardData.Type.WeaponHitscan)
                return;

            __instance.textAmmo.text = "X";
        }

        internal static string GetCurrentFirearm()
        {
            if (Miracle.miracleCount > 0)
                return "KATANA_MIRACLE";
            else if (!fires.Contains("KATANA"))
                return "FISTS";
            else
                return "KATANA";
        }

        static readonly FieldInfo gotFists = Helpers.Field(typeof(MechController), "_gotFistsCard");
        static PlayerCard SetupFirearm(PlayerCard card)
        {
            if (GetCurrentFirearm() == "FISTS")
                gotFists.SetValue(RM.mechController, true);

            card.data = Game.Instance.GetGameData().GetCard(GetCurrentFirearm());
            return card;
        }

        private static IEnumerable<CodeInstruction> InitializeWithSidearm(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(false, new CodeMatch(x => x.LoadsField(Helpers.Field(typeof(PlayerCardDeck), "deck"))))
                .Advance(-1)
                .Do(x => x.Labels.Clear())
                .CloneInPlace(out var postif)
                .MatchBack(false, new CodeMatch(x => x.opcode == OpCodes.Newobj))
                .Advance(1)
                .InsertAndAdvance(CodeInstruction.Call(typeof(Cards), "SetupFirearm"))
                .Advance(1)
                .CloneInPlace(out var store)
                .RemoveInstructionsInRange(store.Pos, postif.Pos)
                .InstructionEnumeration();
        }

        internal static void CheckCards(HashSet<string> newFires = null)
        {
            foreach (var card in cardPickups)
            {
                if (!card)
                    continue;

                bool check = true;
                CheckPickup(card.GetPlayerCardData(), ref check);
                card.uiCard.UICards[0].SetCollectible(check);
            }

            if (RM.mechController)
            {
                var heldCard = RM.mechController.GetPlayerCardDeck().GetCardInHand(0);
                if (newFires != null && newFires.Contains(heldCard.data.cardID))
                    RM.mechController._bulletsFiredThisBurst = 0;
                RM.ui.cardHUDUI.UpdateHUD(heldCard);
            }
        }

        static readonly string[] affectedCards = ["KATANA", "PISTOL", "MACHINEGUN", "RIFLE", "UZI", "SHOTGUN", "ROCKETLAUNCHER", "RAPTURE"];
        static readonly string[] englishCards = ["Katana", "Elevate", "Purify", "Godspeed", "Stomp", "Fireball", "Dominion", "Book of Life"];
        internal static string EngToID(string eng) => affectedCards[Array.IndexOf(englishCards, eng)];

        internal static bool ParseCard(string item)
        {
            if (item.EndsWith("Fire") || item.EndsWith("Discard"))
            {
                var card = EngToID(item.Split()[0]);
                SetOverrides(item.EndsWith("Fire") ? fires : discards, card, true);
                return true;
            }
            if (item == "Katana")
            {
                SetOverrides(fires, EngToID(item), true);
                return true;
            }
            if (item == "Book of Life")
            {
                SetOverrides(discards, EngToID(item), true);
                return true;
            }

            return false;
        }

        internal static void SetOverrides(HashSet<string> set, string value, bool add)
        {
            if (add)
                set.Add(value);
            else
                set.Remove(value);

            CheckCards();
        }

        internal static void SetupTestPanel()
        {
            TestPanel.OnFireToggle += (ID, state) => SetOverrides(fires, ID, state);
            TestPanel.OnDiscardToggle += (ID, state) => SetOverrides(discards, ID, state);

            foreach (var cid in affectedCards)
            {
                var card = Game.Instance.GetGameData().GetCard(cid);
                TestPanel.i.AddCard(LocalizationManager.GetTranslation(UICard.GetAbilityNameFormatted(card)), cid, card.cardColor, cid != "RAPTURE", cid != "KATANA");
            }
        }

        static void EnsureGiftsSpawn(CardPickupSpawner __instance, MethodBase __originalMethod, GameObject ____spawnedObject)
        {
            if (__instance.holderType != CardPickupSpawner.Type.Collectible)
                return;

            if (____spawnedObject)
            {
                if (!APManage.SlotData.gifts)
                    UnityEngine.Object.Destroy(____spawnedObject);
                return;
            }

            var lvl = Game.Instance.GetCurrentLevel();
            if (!lvl || lvl.collectibleGiftForCharacter == null)
                return;

            var lstats = GameDataManager.levelStats[lvl.levelID];
            var force = Settings.testMode && gifts.Value;

            if (lstats.HasCollectibleBeenFound() && !force)
                return;

            if (lvl.collectibleGiftForCharacter.ID == "GREEN")
            {
                // check to see if it spawned
                if (!lstats.HasCollectibleBeenFound() && lstats.GetInsightLevel() >= 1)
                    return; // it did
            }

            var prevF = lstats._collectibleFound;
            var prevI = lstats._insightXp;

            lstats._collectibleFound = false;
            lstats._insightXp = LevelStats.TRYS_PERLEVEL * LevelStats.MAX_INSIGHT_LEVEL;

            __originalMethod.Invoke(__instance, []);

            lstats._collectibleFound = prevF;
            lstats._insightXp = prevI;
        }

        static void EnsureDarkInsight(ObjectSpawner.Type objType, GameObject __result, MethodBase __originalMethod, object[] __args)
        {
            if (objType != ObjectSpawner.Type.DarkInsight)
                return;

            if (__result)
            {
                if (!APManage.SlotData.gifts)
                    UnityEngine.Object.Destroy(__result);
                return;
            }

            var lvl = Game.Instance.GetCurrentLevel();
            if (!lvl || lvl.collectibleGiftForCharacter == null)
                return;

            var lstats = GameDataManager.levelStats[lvl.levelID];
            var force = Settings.testMode && gifts.Value;

            if (lstats.HasCollectibleBeenFound() && !force)
                return;

            var prevF = lstats._collectibleFound;
            var prevI = lstats._insightXp;

            lstats._collectibleFound = false;
            lstats._insightXp = LevelStats.TRYS_PERLEVEL * LevelStats.MAX_INSIGHT_LEVEL;

            __originalMethod.Invoke(null, __args);

            lstats._collectibleFound = prevF;
            lstats._insightXp = prevI;
        }

        static bool StopTutorial(CardPickupSpawner __instance)
        {
            if (__instance.card && __instance.card.consumableType == PlayerCardData.ConsumableType.Tutorial && !tutorials.Value)
                return false;

            return true;
        }

        static bool CardShowcaseForce(PlayerCardData card)
        {
            var level = Game.Instance.GetCurrentLevel();
            if (level.isSidequest && card.consumableType == PlayerCardData.ConsumableType.GreenMemoryItem)
            {
                // we actually wanna do our own thang.
                MainMenu.Instance().SetItemShowcaseCard(card, delegate
                {
                    var ld = Campaign.campaign.missionData
                        .SelectMany(x => x.levels)
                        .Where(x => x.collectibleGiftForCharacter?.ID == "GREEN")
                        .First(x => x.collectiblePortalData.differentLevelData.levelID == level.levelID);

                    if (!GameDataManager.levelStats[ld.levelID].HasCollectibleBeenFound())
                    {
                        GameDataManager.OnGiftCollect("GREEN");
                        GameDataManager.OnCollectibleCollect(ld);
                        GameDataManager.levelStats[ld.levelID].SetCollectibleFound();
                        GameDataManager.SaveLevelStats();
                        if (!GameDataManager.saveData.playerAchievementData.gotcoin)
                        {
                            GameDataManager.saveData.playerAchievementData.gotcoin = true;
                            GameDataManager.SaveGame();
                        }
                        Achievements.SyncGetCoinAchievement(true);
                        GameDataManager.SaveGame();
                    }

                    Game.Instance.PlayLevel(ld, true, true);
                });
                return false;
            }

            if (tutorials.Value)
                return true;

            if (GameDataManager.cardShowcase.ContainsKey(card.cardID))
                GameDataManager.cardShowcase[card.cardID] = true;
            else
                GameDataManager.cardShowcase.Add(card.cardID, true);

            return true;
        }

        static void ForceStaging(MechController __instance, ref Action __result)
        {
            if (__result == null)
                return;
            __result = () =>
            {
                NeonLite.Modules.Optimization.SuperRestart.ForceStagingNextRestart();
                __instance.Die(true);
            };
        }

        static readonly string[] NONAMMO = [
            "KATANA",
            "FISTS",
            "KATANA_MIRACLE",
            "RAPTURE"
        ];

        internal static void CheckAmmo(MechController __instance, ref bool __result)
        {
            if (!__result)
                return;

            __result = __instance.GetCurrentHand().Take(__instance.GetPlayerCardDeck().GetHandCount())
                .Any(x => !NONAMMO.Contains(x.data.cardID) && fires.Contains(x.data.cardID));
        }
        static bool DirectAmmo(MechController __instance)
        {
            var cards = __instance.GetCurrentHand().Take(__instance.GetPlayerCardDeck().GetHandCount()).ToArray();

            var card = cards
                .FirstOrDefault(x => !NONAMMO.Contains(x.data.cardID) && fires.Contains(x.data.cardID));
            if (card == null)
                return false;

            card.currentAmmo += card.data.clipSize;
            RM.ui.OnCollectAmmo(card);
            RM.ui.UpdateCardAmmo(card, card == cards.First());

            return false;
        }
    }
}
