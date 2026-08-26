
using System.Collections;
using System.Reflection;
using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using I2.Loc;
using MelonLoader;
using NeonLite.Modules;
using NWArchipelago.Objects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using static NeonLite.Modules.CommunityMedals;


namespace NWArchipelago.Modules
{

    [Module]
    internal static class Menu
    {
        const bool priority = true;
        static bool active = true;

        internal static MelonPreferences_Entry<bool> showKey;

        static void Setup()
        {
            showKey = NeonLite.Settings.Add(Settings.h, "", "showKey", "Show Vanilla Level Location",
                """
                Whether to show the original location next to level names in the style of something like (1-2).
                Useful for checking the logic sheet for reference.
                """, true);

            active = !Settings.testMode;
        }

        static void Activate(bool _)
        {
            Patching.AddPatch(typeof(MainMenu), "OnPressButtonNewGame", Connect, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "OnPressButtonStartGame", LoadHub, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(MenuScreenTitle), "OnSetVisible", SetupTitle, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenTitle), "OnSetVisible", Helpers.HM(SorryAnticheat).SetPriority(Priority.Last), Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MenuScreenLevel), "Setup", SetupLevel, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenMission), "Setup", SetupMission, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "OnPressBackButton", ReloadMission, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(LevelInfo), "SetLevel", LevelInfoSetLevel, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MenuScreenPause), "OnSetVisible", AntiSidequestPre, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenResults), "OnSetVisible", AntiSidequestPre, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreenPause), "OnSetVisible", AntiSidequestPost, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(MenuScreenResults), "OnSetVisible", AntiSidequestPost, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(LevelInfo), "SetLevel", Helpers.HM(YesSidequestPre).SetPriority(1000), Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(LevelInfo), "SetLevel", Helpers.HM(YesSidequestPost).SetPriority(-1000), Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MenuButtonLevel), "SetLevelData", LevelButtonPost, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(LevelInfo), "Localize", ReplaceEnvironment, Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(MainMenu), "SelectLevel",
                Helpers.HM(SelectIfAllowed).SetPriority(Priority.First), Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuPanelInventoryItem), "SetLevel",
                Helpers.HM(HidePressStart).SetPriority(Priority.First), Patching.PatchTarget.Postfix);

            Patching.AddPatch(typeof(CommunityMedals), "PostSetLevel", Prevent, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(MenuScreenLocation), "CreateActionButton", NoMission, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(GameData), "GetMission", GetMissionOverride, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MainMenu), "SelectMission", SelectMissionOverride, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(GameData), "GetLevelInformation", GetLevelInformationOverride, Patching.PatchTarget.Postfix);

            APManage.OnStatusChanged += ForceTitle;
        }

        internal const string HINTED_MISSION_ID = "M_ARCHI_HINTED";

        internal static string currentMissionID = "";

        // Listen to the SelectMission method and save the current mission ID to a variable
        static bool SelectMissionOverride(string missionID)
        {
            NWArchipelago.Log.DebugMsg($"Selected mission: {missionID}");
            currentMissionID = missionID;

            // If we're in the hint mission but we don't have any more hinted levels available / playable
            if (missionID == HINTED_MISSION_ID && Logic.AllHinted(andAvailable: true) <= 0)
            {
                // Then send the player back into the mission list menu
                Singleton<MainMenu>.Instance.SelectCampaign(Campaign.campaign.campaignID);

                return false;
            }

            return true;
        }

        // Override the level information when we are in the hinted mission
        static void GetLevelInformationOverride(ref LevelInformation __result)
        {
            if (currentMissionID == HINTED_MISSION_ID && __result != null)
            {
                __result.mission = 0;
                __result.missionID = currentMissionID;
            }
        }

        // Add the hinted levels dynamically when we open the hint mission
        static bool GetMissionOverride(string missionID, ref MissionData __result)
        {
            if (missionID == HINTED_MISSION_ID)
            {
                __result = new MissionData
                {
                    missionID = HINTED_MISSION_ID,
                    name = "Hinted Missions",
                    hubContentData = Campaign.campaign.missionData[0].hubContentData
                };
                __result.levels.AddRange(APManage.SlotData.levels.FindAll(level =>
                {
                    var logic = Logic.Level(level);

                    return logic.IsHinted() && logic.CanAccessLevel();
                }));

                return false;
            }

            return true;
        }

        static bool Prevent() => false;

        static Color lowLight = new(0.8f, 0.8f, 0.8f);
        static Color yellowLight = new Color32(222, 213, 169, 255);
        static Color greenLight = new Color32(230, 255, 230, 255);
        static Color blueLight = new Color32(130, 210, 255, 255);
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

        static void ForceTitle(APManage.ConnectStatus _) {
            if (MainMenu.Instance().GetCurrentState() != MainMenu.State.Title)
            {
                // this won't work right without it for some reason
                MainMenu.Instance().PauseGame(true, animate: false);
                MainMenu.Instance().PauseGameNoStateChange(false);
                Game.Instance.QuitToTitle();
            }
        }

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

            // Create a hint mission with all levels that are in logic and the player has access to
            MissionData hintedMission = new()
            {
                missionID = HINTED_MISSION_ID,
                missionDisplayName = "Hinted Mission"
            };
            foreach (var m in campaign.missionData)
            {
                hintedMission.levels.AddRange(m.levels.FindAll(level =>
                {
                    var logic = Logic.Level(level);
                    return logic.IsHinted() && logic.CanAccessLevel();
                }));
            }

            MissionData[] missions = [hintedMission, .. campaign.missionData];
            var template = __instance._missionMenuButtonTemplate.gameObject;
            template.SetActive(value: true);

            bool doneLocked = false;
            for (int i = 0; i < missions.Length; ++i)
            {
                var mission = missions[i];
                bool isHintMission = i == 0;
                var missionHasHints = mission.levels.Any(level => Logic.Level(level).IsHinted());

                MenuButtonHolder b = Utils.InstantiateUI(template, "Mission Button", template.transform.parent).GetComponent<MenuButtonHolder>();
                __instance.buttonsToLoad.Add(b);
                ____missionButtons.Add(b);
                b.SetMissionData(mission, i);
                b.onClickEvent.AddListener(() => __instance._firstNavElement = b.gameObject);

                if (i - 1 <= GameDataManager.campaignStats[Singleton<Game>.Instance.GetGameData().GetCurrentCampaign().campaignID].GetFarthestMission() || GS.unlockLevels)
                {
                    b.SetLocked(val: false);
                    var chks = Logic.ChecksString(out var n, mission: mission);

                    if (isHintMission)
                    {
                        if (mission.levels.Count() == 0)
                        {
                            b.SetLocked(val: true);

                            b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                                new AxKReplacementPair("{PREFIX}", "Hinted Levels", false),
                                new AxKReplacementPair("{MN}", ""),
                                new AxKReplacementPair("{CHK}", ""),
                                new AxKReplacementPair("{MRK}", "No Hinted Levels Playable", false),
                            ]);
                        }
                        else
                        {
                            b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                                new AxKReplacementPair("{PREFIX}", "Hinted Levels", false),
                                new AxKReplacementPair("{MN}", ""),
                                new AxKReplacementPair("{CHK}", chks),
                                // TODO: Localization, plus "level" vs. "levels"
                                new AxKReplacementPair("{CN}", $"{mission.levels.Count()} levels - {n}", false),
                                new AxKReplacementPair("{MRK}", ""),
                            ]);
                        }
                    }
                    else
                    {
                        // TODO: In "Levels Unlock" mode the a mission that has hints should also have the small text above
                        if (APManage.SlotData.unlockMethod != APManage.UnlockMethod.Levels)
                        {
                            if (missionHasHints)
                            {
                                b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                                    new AxKReplacementPair("{PREFIX}", "MISSION_PREFIX"),
                                    new AxKReplacementPair("{MN}", i),
                                    new AxKReplacementPair("{CHK}", chks),
                                    // TODO: This needs a locale.csv update.
                                    // Do we even need this with the Hint Mission and / or blue button background?
                                    new AxKReplacementPair("{CN}", $"Hinted - {n}", false),
                                    new AxKReplacementPair("{MRK}", ""),
                                ]);
                            }
                            else
                            {
                                b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                                    new AxKReplacementPair("{PREFIX}", "MISSION_PREFIX"),
                                    new AxKReplacementPair("{MN}", i),
                                    new AxKReplacementPair("{CHK}", chks),
                                    new AxKReplacementPair("{CN}", n),
                                    new AxKReplacementPair("{MRK}", ""),
                                ]);
                            }
                        }
                        else
                        {
                            b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                                new AxKReplacementPair("{PREFIX}", mission.missionDisplayName),
                                new AxKReplacementPair("{MN}", ""),
                                new AxKReplacementPair("{CHK}", chks),
                                new AxKReplacementPair("{CN}", n),
                                new AxKReplacementPair("{MRK}", ""),
                            ]);
                        }
                    }
                    b.buttonTextRef.lineSpacing = -15;

                    if (n <= 0)
                    {
                        if (Logic.outOfLogic.Value && Logic.MissionChecks(mission, true) > 0)
                            SetButtonColor(b.ButtonRef, yellowLight);
                        else
                            SetButtonColor(b.ButtonRef, lowLight);
                    }
                    else
                        SetButtonColor(b.ButtonRef, greenLight);

                    if (isHintMission)
                        SetButtonColor(b.ButtonRef, blueLight);
                }
                else if (!doneLocked)
                {
                    doneLocked = true;
                    b.SetLocked(val: true);

                    if (APManage.SlotData.unlockMethod == APManage.UnlockMethod.Missions)
                    {
                        b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                            new AxKReplacementPair("{MN}", i),
                            new AxKReplacementPair("{MRK}", ""),
                            new AxKReplacementPair("{CHK}", ""),
                        ]);
                    }
                    else
                    {
                        b.localizedText.SetKey("NWArchipelago/MISSION_NAME", [
                            new AxKReplacementPair("{MN}", i),
                            new AxKReplacementPair("{MRK}", "NWArchipelago/MISSION_RANKS"),
                            new AxKReplacementPair("{R0}", SaveHandler.archiSaveData.neonRank),
                            new AxKReplacementPair("{R1}", mission.medalsRequired),
                            new AxKReplacementPair("{CHK}", ""),
                        ]);
                    }
                    b.buttonTextRef.lineSpacing = -15;
                }
                else
                    b.gameObject.SetActive(value: false);

                if (mission.missionID.StartsWith("M_SIDEQUESTS"))
                    b.GetComponentInChildren<MenuButtonMission>()._textMissionIndex.text =
                        Campaign.sidequestLRegex.Match(mission.missionID).Groups[1].Value;
            }

            __instance._scrollRectRef.verticalNormalizedPosition = 1f;
            __instance._missionMenuButtonTemplate.gameObject.SetActive(value: false);
            __instance._setup = true;
            return false;
        }


        static bool SetupLevel(MenuScreenLevel __instance, LevelData[] levels, List<MenuButtonHolder> ____levelButtons)
        {
            if (__instance._setup)
                return false;

            var template = __instance._levelButtonTemplate.gameObject;
            template.SetActive(value: true);

            var setupnav = Helpers.Method(typeof(MenuScreenLevel), "SetupNavigation");

            // Sort levels alphabetaically by display name
            Array.Sort(levels, (a, b) =>
            {
                string levelA = LocalizationManager.GetTranslation(a.GetLevelDisplayName());
                string levelB = LocalizationManager.GetTranslation(b.GetLevelDisplayName());
                return string.Compare(levelA, levelB);
            });

            for (int i = 0; i < levels.Length; i++)
            {
                var level = levels[i];
                MenuButtonHolder b = Utils.InstantiateUI(template, "Level Button", template.transform.parent).GetComponent<MenuButtonHolder>();

                __instance.buttonsToLoad.Add(b);
                ____levelButtons.Add(b);
                if (level.isSidequest)
                    Campaign.checkComplete = true;
                b.SetLevelData(level, i + 1);
                b.onClickEvent.AddListener(() => setupnav.Invoke(__instance, []));

                if (!Logic.Level(level).CanAccessLevel())
                    b.SetLocked(true);
                else
                    b.SetLocked(false);
            }

            // navigation code from the original
            if (____levelButtons.Count > 0)
            {
                int num = 0;
                Toggle personalGhostToggle = MainMenu.Instance()._screenInspector.leaderboardsAndLevelInfoRef.insightInfoRef.personalGhostToggle;
                Toggle selectOnRight = personalGhostToggle.interactable ? personalGhostToggle : null;
                for (int j = 0; j < ____levelButtons.Count; j++)
                {
                    Navigation navigation = ____levelButtons[j].ButtonRef.navigation;
                    if (____levelButtons[j].GetLocked())
                        navigation.mode = Navigation.Mode.None;
                    else
                    {
                        navigation.mode = Navigation.Mode.Explicit;
                        navigation.selectOnUp = null;
                        navigation.selectOnDown = Singleton<BackButtonAccessor>.Instance.BackButton;
                        for (int num2 = j - 1; num2 >= 0; num2--)
                        {
                            if (!____levelButtons[num2].GetLocked())
                            {
                                navigation.selectOnUp = ____levelButtons[num2].ButtonRef;
                                break;
                            }
                        }
                        for (int k = j + 1; k < levels.Length; k++)
                        {
                            if (!____levelButtons[k].GetLocked())
                            {
                                navigation.selectOnDown = ____levelButtons[k].ButtonRef;
                                break;
                            }
                        }
                        navigation.selectOnRight = selectOnRight;
                    }
                    ____levelButtons[j].ButtonRef.navigation = navigation;
                    if (j == num && ____levelButtons[j].gameObject.activeSelf)
                    {
                        __instance._firstNavElement = ____levelButtons[j].gameObject;
                        MainMenu.Instance()._screenInspector.SetVisible(vis: true, animate: true);
                        MainMenu.Instance()._screenInspector.SetLevel(____levelButtons[j], ____levelButtons[j].GetLevelData(), justRequestingScores: true);
                    }
                    else
                        num++;
                }
            }
            else
                __instance._firstNavElement = MainMenu.Instance()._backButton.gameObject;

            __instance.scrollRectRef.verticalNormalizedPosition = 1f;
            __instance._levelButtonTemplate.gameObject.SetActive(value: false);
            __instance._setup = true;
            return false;
        }

        static void LevelButtonPost(MenuButtonLevel __instance, LevelData ld)
        {
            var s = Logic.ChecksString(out var checks, level: ld);

            if (Logic.display.Value >= Logic.LogicDisplay.Full)
            {
                var leveldisplay = showKey.Value ? "NWArchipelago/LEVEL_NAME_WKEY" : ld.GetLevelDisplayName();

                if (Logic.Level(ld).IsHinted() && currentMissionID != HINTED_MISSION_ID)
                {
                    __instance._textLevelName_Localized.SetKey("NWArchipelago/LEVEL_NAME", [
                        new AxKReplacementPair("{OG?}", leveldisplay),
                        new AxKReplacementPair("{KEY}", Campaign.levelKey[ld.levelID], false),
                        new AxKReplacementPair("{OG}", ld.GetLevelDisplayName()),
                        new AxKReplacementPair("{CHK}", s),
                        new AxKReplacementPair("{CN}", $"Hinted - {checks}", false),
                    ]);
                }
                else
                {
                    __instance._textLevelName_Localized.SetKey("NWArchipelago/LEVEL_NAME", [
                        new AxKReplacementPair("{OG?}", leveldisplay),
                        new AxKReplacementPair("{KEY}", Campaign.levelKey[ld.levelID], false),
                        new AxKReplacementPair("{OG}", ld.GetLevelDisplayName()),
                        new AxKReplacementPair("{CHK}", s),
                        new AxKReplacementPair("{CN}", checks),
                    ]);
                }
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
                    SetButtonColor(__instance._button, yellowLight);
                else
                    SetButtonColor(__instance._button, lowLight);
            }
            else
                SetButtonColor(__instance._button, greenLight);

            // Only use blue hint color if the level is hinted, has checks available and is not in the hint mission
            if (Logic.Level(ld).IsHinted() && checks > 0 && currentMissionID != HINTED_MISSION_ID)
                SetButtonColor(__instance._button, blueLight);

            if (APManage.SlotData.unlockMethod == APManage.UnlockMethod.Levels)
                __instance.SetLocked(!Campaign.unlockedLevels.Contains(ld.levelIntegerID));
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
            if (!level || !Logic.HasLogic(level))
                return;

            var isSidequest = level.isSidequest || level.levelID.Contains("SIDEQUEST");

            // handle insight stuff
            var insight = __instance._insightAniamtor.GetComponent<InsightInfo>();
            Campaign.checkComplete = true;
            var stats = GameDataManager.GetLevelStats(level.levelID);
            var completed = stats.GetCompleted();
            if (!completed)
            {
                __instance._bestTimeEmptyHolder.SetActive(true);
                __instance._bestTimeHolder.SetActive(false);

                if (isSidequest)
                {
                    __instance._crystalHolderFilled.SetActive(false);
                    __instance._crystalFillBG.SetActive(false);
                    __instance._crystalStateDescriptionText_Localized.SetKey("Interface/LEVELINFO_CRYSTAL_NOT_FOUND");
                }
            }

            // remove any insight stuff from the actual visuals
            insight.insightXpBar.gameObject.SetActive(false);
            // insight.GetComponentInChildren<EvilEye_Icon>()?.gameObject?.SetActive(false);

            var insighttext_loc = insight.transform.Find("InsightText").GetComponent<AxKLocalizedText>();
            insighttext_loc.SetKey("Interface/INTERFACE_LABEL_005");
            var textbuf = insighttext_loc.textMeshProUGUI;
            textbuf.margin = new(80, 0, 15, -13);
            textbuf.fontSize = 34;
            textbuf.alignment = TMPro.TextAlignmentOptions.Bottom;
            textbuf.fontStyle &= ~TMPro.FontStyles.Italic;

            textbuf = __instance._crystalStateDescriptionText;
            var margin = textbuf.margin;
            margin.x = 3;
            margin.w = -15;
            textbuf.margin = margin;
            __instance._crystalStateCaptionText.gameObject.SetActive(false);

            // handle GhostsEverywhere code
            Image[] dotteds = insight.GetComponentsInChildren<Image>();
            dotteds[0].enabled = !level.isSidequest;
            dotteds[1].enabled = !level.isSidequest;


            var medalEarned = GetMedalIndex(level.levelID);
            var shift = medalEarned > (int)MedalEnum.Silver && APManage.SlotData.medals.DefaultIfEmpty(MedalEnum.Bronze).Max() >= MedalEnum.Dev;

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

            void SetTextColor(MedalEnum medal, TextMeshProUGUI text, GameObject bg = null, bool gift = false)
            {
                text.color = Color.white;
                if (bg)
                {
                    bg.transform.SetAsFirstSibling();
                    if (gift || isSidequest)
                        bg.GetComponentsInChildren<Image>(true)
                            .Do(x => x.color = Color.black);
                    else
                        bg.GetComponentsInChildren<Image>(true)
                            .Do(x => x.color = new(1, 1, 1, 0.5f));
                }

                if (Logic.display.Value < Logic.LogicDisplay.ColorsDetailed ||
                    ((int)medal <= medalEarned && !gift) ||
                    (stats.HasCollectibleBeenFound() && gift))
                    return;

                var logic = Logic.Level(level);
                bool Check(Logic logic) => gift ? logic.CanGift() : logic.CanGetMedal(medal);

                // NWArchipelago.Log.DebugMsg(Check(logic));
                // NWArchipelago.Log.DebugMsg(Check(logic.Full()));
                // NWArchipelago.Log.DebugMsg(Logic.outOfLogic.Value);
                // NWArchipelago.Log.DebugMsg(Logic.outOfLogic.Value && Check(logic.Full()));

                if (Check(logic))
                    text.color = greenLight;
                else if (Logic.outOfLogic.Value && Check(logic.Full()))
                    text.color = yellowLight;
                else
                {
                    if (bg)
                    {
                        bg.GetComponentsInChildren<Image>(true)
                            .Do(x => x.color = new(0, 0, 0, 0.6f));
                        bg.gameObject.SetActive(true);
                        bg.transform.SetAsLastSibling();
                    }
                    return;
                }

                Color.RGBToHSV(text.color, out var h, out var s, out var v);
                s += 0.05f;
                v += .1f;
                if (v > 1)
                    v = 1;
                text.color = Color.HSVToRGB(h, s, v);
            }

            SetTextColor(MedalEnum.Bronze, __instance._crystalStateDescriptionText, __instance._crystalFillBG, gift: !isSidequest);
            SetTextColor(MedalEnum.Bronze, __instance._crystalStateCaptionText, gift: !isSidequest);

            if (isSidequest || !shift)
            {
                aceImage.sprite = Medals[(int)MedalEnum.Ace];
                goldImage.sprite = Medals[(int)MedalEnum.Gold];
                silverImage.sprite = Medals[(int)MedalEnum.Silver];

                SetTextColor(MedalEnum.Silver, __instance._silverMedalTime, __instance._silverMedalBG);
                SetTextColor(MedalEnum.Gold, __instance._goldMedalTime, __instance._goldMedalBG);
                SetTextColor(MedalEnum.Ace, __instance._aceMedalTime, __instance._aceMedalBG);

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

            SetTextColor(MedalEnum.Gold, __instance._silverMedalTime, __instance._silverMedalBG);
            SetTextColor(MedalEnum.Ace, __instance._goldMedalTime, __instance._goldMedalBG);
            SetTextColor(MedalEnum.Dev, __instance._aceMedalTime, __instance._aceMedalBG);
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

        static bool SelectIfAllowed(string levelID)
        {
            var gd = Singleton<Game>.Instance.GetGameData();
            return Logic.Level(gd.GetLevelData(levelID)).CanAccessLevel();
        }
        static void HidePressStart(MenuPanelInventoryItem __instance, LevelData level)
        {
            if (__instance._pressToPlayPrompt && __instance._pressToPlayPrompt.gameObject.activeSelf)
                __instance._pressToPlayPrompt.gameObject.SetActive(Logic.Level(level).CanAccessLevel());
        }

        static bool NoMission(HubAction hubAction) => hubAction.ID != "PORTAL_CONTINUE_MISSION";
    }
}
