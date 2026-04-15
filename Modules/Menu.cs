
using System.Collections;
using System.Reflection;
using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using I2.Loc;
using MelonLoader;
using NeonLite.Modules;
using NWArchipelago.Objects;
using UnityEngine;
using UnityEngine.UI;

using static NeonLite.Modules.CommunityMedals;


namespace NWArchipelago.Modules
{

    [Module]
    internal static class Menu
    {
        const bool priority = true;
        const bool active = true;

        internal static MelonPreferences_Entry<bool> showKey;

        static void Setup()
        {
            showKey = NeonLite.Settings.Add(Settings.h, "", "showKey", "Show Vanilla Level Location",
                """
                Whether to show the original location next to level names in the style of something like (1-2).
                Useful for checking the logic sheet for reference.
                """, true);
        }

        static void Activate(bool _)
        {
            if (Settings.testMode)
                return;
            Patching.AddPatch(typeof(MainMenu), "OnPressButtonNewGame", Connect, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "OnPressButtonStartGame", LoadHub, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(MenuScreenTitle), "OnSetVisible", SetupTitle, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenTitle), "OnSetVisible", Helpers.HM(SorryAnticheat).SetPriority(Priority.Last), Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MenuScreenMission), "Setup", SetupMission, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "OnPressBackButton", ReloadMission, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(LevelInfo), "SetLevel", LevelInfoSetLevel, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MenuScreenPause), "OnSetVisible", AntiSidequestPre, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenResults), "OnSetVisible", AntiSidequestPre, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenPause), "OnSetVisible", AntiSidequestPost, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MenuScreenResults), "OnSetVisible", AntiSidequestPost, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(LevelInfo), "SetLevel", YesSidequestPre, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(LevelInfo), "SetLevel", YesSidequestPost, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MenuButtonLevel), "SetLevelData", AddLevelCheck, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(LevelInfo), "Localize", ReplaceEnvironment, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(CommunityMedals), "PostSetLevel", Prevent, Patching.PatchTarget.Prefix);
        }

        static bool Prevent() => false;

        static Color lowLight = new(0.8f, 0.8f, 0.8f);
        static void SetButtonColor(Button button, Color c)
        {
            if (Logic.display.Value < Logic.LogicDisplay.Colors)
                return;

            var colors = button.colors;
            colors.normalColor = c;
            button.colors = colors;
        }

        static bool titleFirst = false;
        static bool noAnimate = false;

        static GameObject[] titleLUL;

        internal static int previousRank;

        static bool SetupTitle(MenuScreenTitle __instance)
        {
            if (!titleFirst)
            {
                titleFirst = true;
                titleLUL = [__instance.newGameButton, __instance.continueGameButton];
                APManage.OnStatusChanged += TitleConnectChange;
            }
            __instance.levelRushButton.SetActive(false);
            __instance.quitButton.SetActive(true);
            __instance.time.gameObject.SetActive(false);

            switch (APManage.ConnectionStatus)
            {
                case APManage.ConnectStatus.Failed:
                case APManage.ConnectStatus.Idle:
                    __instance.newGameButton.SetActive(true);
                    __instance.newGameButton.GetComponent<MenuButtonHolder>().ShouldBeInteractable = true;
                    __instance.newGameButton.GetComponentInChildren<AxKLocalizedText>().SetKey("NWArchipelago/BUTTON_CONNECT");
                    __instance.continueGameButton.SetActive(false);

                    break;
                case APManage.ConnectStatus.Connecting:
                    __instance.newGameButton.SetActive(true);
                    __instance.newGameButton.GetComponent<MenuButtonHolder>().ShouldBeInteractable = false;
                    __instance.newGameButton.GetComponentInChildren<AxKLocalizedText>().SetKey("NWArchipelago/BUTTON_CONNECTING");
                    __instance.continueGameButton.SetActive(false);

                    break;
                case APManage.ConnectStatus.Connected:
                    __instance.newGameButton.SetActive(false);
                    __instance.continueGameButton.SetActive(true);
                    __instance.continueGameButton.GetComponentInChildren<AxKLocalizedText>()
                        .SetKey("NWArchipelago/BUTTON_PLAY",
                            replacementPairs: [
                                new AxKReplacementPair("{0}", SaveHandler.archiSaveData.neonRank - previousRank),
                                new AxKReplacementPair("{CHK}", Logic.ChecksString(out var c, force: Logic.display.Value != Logic.LogicDisplay.None)),
                                new AxKReplacementPair("{CN}", c),
                                new AxKReplacementPair("<br><br>", "<br>") // if disabled, get rid of double br
                            ]);
                    break;
            }

            if (!noAnimate)
            {
                int i = 0;
                foreach (var b in titleLUL)
                {
                    if (b.activeSelf)
                        b.GetComponent<MenuButtonHolder>().LoadButton(0.05f * i++);
                }

                var qbh = __instance.quitButton.GetComponent<MenuButtonHolder>();
                qbh.ResetButton();
                qbh.ForceVisible();
            }
            noAnimate = false;

            return false;
        }
        static void SorryAnticheat(MenuScreenTitle __instance)
            => __instance.newGameButton.GetComponent<MenuButtonHolder>().ShouldBeInteractable = APManage.ConnectionStatus == APManage.ConnectStatus.Idle;

        static void TitleConnectChange(APManage.ConnectStatus newState)
        {
            var title = (MenuScreenTitle)MainMenu.Instance()._screenTitle;

            if (newState == APManage.ConnectStatus.Connecting)
            {
                if (title.GetIsVisible())
                {
                    noAnimate = true;
                    SetupTitle(title);
                }
                return;
            }

            IEnumerator Coro()
            {
                int i = 0;
                foreach (var b in titleLUL)
                {
                    if (b.activeSelf)
                        b.GetComponent<MenuButtonHolder>().UnloadButton(0.05f * i);
                }

                if (title.GetIsVisible())
                {
                    if (i > 0)
                        yield return new WaitForSeconds(0.05f * (i + 10));
                    SetupTitle(title);
                }
            }

            MelonCoroutines.Start(Coro());
        }

        static bool Connect()
        {
            NWArchipelago.CoroTask(Wrapper.StartSession());
            return false;
        }

        static bool LoadHub()
        {
            LevelRush.SetLevelRush(LevelRush.LevelRushType.None, heavenRush: false, shuffleLevelOrder: false);
            MainMenu.Instance()._screenLoading.SetLoadingType(MenuScreenLoading.LoadingType.Normal);

            Singleton<Game>.Instance.PlayLevel("HUB_HEAVEN", fromArchive: false);
            APManage.session.SetClientState(ArchipelagoClientState.ClientPlaying);
            return false;
        }

        static bool SetupMission(MenuScreenMission __instance, CampaignData campaign, List<MenuButtonHolder> ____missionButtons)
        {
            if (__instance._setup)
                return false;
            if (campaign.campaignID != Campaign.CAMPAIGN_ID)
                return true;

            MissionData[] missions = [.. campaign.missionData];
            var template = __instance._missionMenuButtonTemplate.gameObject;
            template.SetActive(value: true);

            bool doneLocked = false;
            for (int i = 0; i < missions.Length; i++)
            {
                MenuButtonHolder b = Utils.InstantiateUI(template, "Mission Button", template.transform.parent).GetComponent<MenuButtonHolder>();
                __instance.buttonsToLoad.Add(b);
                ____missionButtons.Add(b);
                b.SetMissionData(missions[i], i + 1);
                b.onClickEvent.AddListener(() => __instance._firstNavElement = b.gameObject);

                if (i <= GameDataManager.campaignStats[Singleton<Game>.Instance.GetGameData().GetCurrentCampaign().campaignID].GetFarthestMission() || GS.unlockLevels)
                {
                    b.SetLocked(val: false);
                    b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                        new AxKReplacementPair("{MN}", i + 1),
                        new AxKReplacementPair("{CHK}", Logic.ChecksString(out var n, mission: missions[i])),
                        new AxKReplacementPair("{CN}", n),
                        new AxKReplacementPair("{MRK}", ""),
                    ]);
                    b.buttonTextRef.lineSpacing = -15;
                    if (n <= 0)
                        SetButtonColor(b.ButtonRef, lowLight);
                }
                else if (!doneLocked)
                {
                    doneLocked = true;
                    b.SetLocked(val: true);
                    b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                        new AxKReplacementPair("{MN}", i + 1),
                        new AxKReplacementPair("{MRK}", "NWArchipelago/MISSION_RANKS"),
                        new AxKReplacementPair("{R0}", SaveHandler.archiSaveData.neonRank),
                        new AxKReplacementPair("{R1}", missions[i].medalsRequired),
                        new AxKReplacementPair("{CHK}", ""),
                    ]);
                    b.buttonTextRef.lineSpacing = -15;
                }
                else
                    b.gameObject.SetActive(value: false);

            }
            __instance._scrollRectRef.verticalNormalizedPosition = 1f;
            __instance._missionMenuButtonTemplate.gameObject.SetActive(value: false);
            __instance._setup = true;
            return false;
        }

        static void AddLevelCheck(MenuButtonLevel __instance, LevelData ld)
        {
            var s = Logic.ChecksString(out var checks, level: ld);

            if (Logic.display.Value >= Logic.LogicDisplay.Full)
            {
                var leveldisplay = showKey.Value ? "NWArchipelago/LEVEL_NAME_WKEY" : ld.GetLevelDisplayName();

                __instance._textLevelName_Localized.SetKey("NWArchipelago/LEVEL_NAME", [
                    new AxKReplacementPair("{OG?}", leveldisplay),
                    new AxKReplacementPair("{KEY}", Campaign.levelKey[ld.levelID], false),
                    new AxKReplacementPair("{OG}", ld.GetLevelDisplayName()),
                    new AxKReplacementPair("{CHK}", s),
                    new AxKReplacementPair("{CN}", checks),
                ]);
            }
            else if (showKey.Value)
            {
                __instance._textLevelName_Localized.SetKey("NWArchipelago/LEVEL_NAME_WKEY", [
                    new AxKReplacementPair("{KEY}", Campaign.levelKey[ld.levelID], false),
                    new AxKReplacementPair("{OG}", ld.GetLevelDisplayName()),
                ]);
            }

            __instance._textLevelIndex.gameObject.SetActive(false);

            if (checks <= 0)
            {
                if (Logic.outOfLogic.Value && Logic.Level(ld, true).Checks() > 0)
                    SetButtonColor(__instance._button, new Color32(222, 213, 169, 255));
                else
                    SetButtonColor(__instance._button, lowLight);
            }
            else
                SetButtonColor(__instance._button, new Color32(230, 255, 230, 255));
        }

        static void ReplaceEnvironment(LevelInfo __instance, LevelData ____currentLevel)
        {
            if (Logic.display.Value < Logic.LogicDisplay.Full || !Logic.HasLogic(____currentLevel))
                return;
            __instance._levelEnvironmentNameText.text = LocalizationManager
                .GetTranslation("NWArchipelago/CHECKS_REMAINING")
                .Replace("{CHK}", LocalizationManager.GetTranslation(Logic.ChecksString(out var n, level: ____currentLevel)))
                .Replace("{CN}", n.ToString());
        }

        static void ReloadMission(MainMenu.State ____backButtonState, ref bool ____reloadButtonsOnBack)
        {
            if (____backButtonState == MainMenu.State.Mission)
                ____reloadButtonsOnBack = true;
        }

        static readonly MethodInfo styleTime = Helpers.Method(typeof(LevelInfo), "StyleMedalTime");

        static void LevelInfoSetLevel(LevelInfo __instance, LevelData level)
        {
            if (!level)
                return;

            // handle GhostsEverywhere code
            Image[] dotteds = __instance._insightAniamtor.GetComponentsInChildren<Image>();
            dotteds[0].enabled = !level.isSidequest;
            dotteds[1].enabled = !level.isSidequest;

            __instance._crystalLock.gameObject.SetActive(false);

            var medalEarned = GetMedalIndex(level.levelID);
            var shift = medalEarned > (int)MedalEnum.Silver && true; //APManage.SlotData.medalCap >= MedalEnum.Dev;

            Image aceImage = __instance._aceMedalBG.transform.parent.Find("Medal Icon").GetComponent<Image>();
            Image goldImage = __instance._goldMedalBG.transform.parent.Find("Medal Icon").GetComponent<Image>();
            Image silverImage = __instance._silverMedalBG.transform.parent.Find("Medal Icon").GetComponent<Image>();

            // still try to respect AdjustMaterial
            Image[] stamps = __instance.devStamp.GetComponentsInChildren<Image>();
            if (stamps.Length < 3) return;

            CommunityMedals.AdjustMaterial(stamps[1]);
            CommunityMedals.AdjustMaterial(stamps[2]);

            CommunityMedals.AdjustMaterial(aceImage);
            CommunityMedals.AdjustMaterial(goldImage);
            CommunityMedals.AdjustMaterial(silverImage);

            if (level.isSidequest || !shift)
            {
                aceImage.sprite = Medals[(int)MedalEnum.Ace];
                goldImage.sprite = Medals[(int)MedalEnum.Gold];
                silverImage.sprite = Medals[(int)MedalEnum.Silver];

                return;
            }

            aceImage.sprite = Medals[(int)MedalEnum.Dev];
            goldImage.sprite = Medals[(int)MedalEnum.Ace];
            silverImage.sprite = Medals[(int)MedalEnum.Gold];

            __instance._aceMedalBG.SetActive(medalEarned >= (int)MedalEnum.Dev);
            __instance._goldMedalBG.SetActive(medalEarned >= (int)MedalEnum.Ace);
            __instance._silverMedalBG.SetActive(medalEarned >= (int)MedalEnum.Gold);

            long[] communityTimes = medalTimes[level.levelID];

            __instance._aceMedalTime.text = (string)styleTime.Invoke(__instance, [
                Helpers.FormatTime(communityTimes[(int)MedalEnum.Dev] / 1000, true, '.', true),
                medalEarned >= (int)MedalEnum.Dev]);
            __instance._goldMedalTime.text = (string)styleTime.Invoke(__instance, [
                Helpers.FormatTime(communityTimes[(int)MedalEnum.Ace] / 1000, true, '.', true),
                medalEarned >= (int)MedalEnum.Ace]);
            __instance._silverMedalTime.text = (string)styleTime.Invoke(__instance, [
                Helpers.FormatTime(communityTimes[(int)MedalEnum.Gold] / 1000, true, '.', true),
                medalEarned >= (int)MedalEnum.Gold]);
        }


        static void YesSidequestPre(ref bool __state)
        {
            var level = LoadManager.currentLevel;
            if (level && level.levelID.Contains("SIDEQUEST") && !level.isSidequest)
            {
                __state = true;
                level.isSidequest = true;
            }
        }
        static void YesSidequestPost(bool __state)
        {
            if (__state)
                LoadManager.currentLevel.isSidequest = false;
        }

        static void AntiSidequestPre(ref bool __state)
        {
            var level = LoadManager.currentLevel;
            if (level && level.isSidequest)
            {
                __state = true;
                level.isSidequest = false;
            }
        }

        static void AntiSidequestPost(bool __state)
        {
            if (__state)
                LoadManager.currentLevel.isSidequest = true;
        }
    }
}
