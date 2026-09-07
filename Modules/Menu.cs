
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
        internal static MelonPreferences_Entry<bool> sortLevel;

        static void Setup()
        {
            showKey = NeonLite.Settings.Add(Settings.h, "", "showKey", "Show Vanilla Level Location",
                """
                Whether to show the original location next to level names in the style of something like (1-2).
                Useful for checking the logic sheet for reference.
                """, true);
            sortLevel = NeonLite.Settings.Add(Settings.h, "", "sortLevel", "Sort Levels",
                """
                Whether to sort the levels in each mission in the menu for convenience.
                Sorts by: locked status, out of logic checks, inlogic checks, hinted checks
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

            Patching.AddPatch(typeof(MenuScreen), "LoadButtons", PreFasterButtons, Patching.PatchTarget.Prefix);
            Patching.AddPatch(typeof(MenuScreen), "LoadButtons", PostFasterButtons, Patching.PatchTarget.Postfix);

            APManage.OnStatusChanged += ForceTitle;
        }

        internal const string HINTED_MISSION_ID = "M_HINTED";

        internal static string currentMissionID = "";

        // Listen to the SelectMission method and save the current mission ID to a variable
        static bool SelectMissionOverride(string missionID)
        {
            NWArchipelago.Log.DebugMsg($"Selected mission: {missionID}");
            currentMissionID = missionID;

            // If we're in the hint mission but we don't have any more hinted levels available / playable
            if (missionID == HINTED_MISSION_ID && Logic.AllHinted(false, false) <= 0)
            {
                // Then send the player back into the mission list menu
                MainMenu.Instance().SelectCampaign(Campaign.campaign.campaignID);

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

        static MissionData hintMissionCache;
        static MissionData GenerateHintMission(out bool show)
        {
            if (!hintMissionCache)
            {
                hintMissionCache = ScriptableObject.CreateInstance<MissionData>();

                hintMissionCache.missionID = HINTED_MISSION_ID;
                hintMissionCache.name = "Hinted Missions";
                hintMissionCache.missionDisplayName = "NWArchipelago/MISSION_HINTS";
                hintMissionCache.hubContentData = Campaign.campaign.missionData[0].hubContentData;
            }

            show = Logic.hintDisplay.Value >= Logic.HintDisplay.Always && Logic.hints.Value;
            var levels = APManage.SlotData.levels.FindAll(x => Logic.Level(x).Hinteds(false, false) > 0);
            show |= Logic.hintDisplay.Value >= Logic.HintDisplay.WhenAny && levels.Any();
            show |= Logic.hintDisplay.Value >= Logic.HintDisplay.WhenAvailable && levels.Any(x => Logic.Level(x).Hinteds() > 0);

            hintMissionCache.levels = [.. levels
                .OrderByDescending(x => Logic.Level(x).CanAccessLevel())
                .OrderByDescending(x => Logic.Level(x, true).Hinteds(inLogic: false))
                .OrderByDescending(x => Logic.Level(x).Hinteds(inLogic: false))
                .OrderByDescending(x => Logic.Level(x).Hinteds())
            ];

            return hintMissionCache;
        }

        // Add the hinted levels dynamically when we open the hint mission
        static bool GetMissionOverride(string missionID, ref MissionData __result)
        {
            if (missionID == HINTED_MISSION_ID)
            {
                __result = GenerateHintMission(out var _);
                return false;
            }

            return true;
        }

        static bool Prevent() => false;

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

        static void ForceTitle(APManage.ConnectStatus _)
        {
            if (MainMenu.Instance().GetCurrentState() != MainMenu.State.Title)
            {
                // this won't work right without it for some reason
                MainMenu.Instance().SetState(MainMenu.State.None);
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
                    {
                        var receive = APManage.SlotData.unlockMethod switch
                        {
                            APManage.UnlockMethod.Ranks => "NWArchipelago/RECIEVED_RANKS",
                            APManage.UnlockMethod.Missions => "NWArchipelago/RECIEVED_MISSONS",
                            _ => "",
                        };
                        __instance.newGameButton.SetActive(false);
                        __instance.continueGameButton.SetActive(true);
                        __instance.continueGameButton.GetComponentInChildren<AxKLocalizedText>()
                            .SetKey("NWArchipelago/BUTTON_PLAY_V2",
                                replacementPairs: [
                                    new AxKReplacementPair("{PRE1}", receive),
                                    new AxKReplacementPair("{0}", SaveHandler.archiSaveData.neonRank - previousRank),
                                    new AxKReplacementPair("{PRE2}", Logic.GetStrings(out var s, force: Logic.display.Value != Logic.LogicDisplay.None)),
                                    ..s.Replacements,
                                    new AxKReplacementPair("<br><br>", "<br>") // if disabled, get rid of double br
                                ]);
                        break;
                    }
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

            Game.Instance.PlayLevel("HUB_HEAVEN", fromArchive: false);
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
            var hintedMission = GenerateHintMission(out var show);

            MissionData[] missions = [hintedMission, .. campaign.missionData];
            var template = __instance._missionMenuButtonTemplate.gameObject;
            template.SetActive(value: true);

            bool doneLocked = false;
            for (int i = 0; i < missions.Length; ++i)
            {
                var mission = missions[i];
                bool isHintMission = i == 0;
                if (isHintMission && !show)
                    continue;

                MenuButtonHolder b = Utils.InstantiateUI(template, "Mission Button", template.transform.parent).GetComponent<MenuButtonHolder>();
                __instance.buttonsToLoad.Add(b);
                ____missionButtons.Add(b);
                b.SetMissionData(mission, i);
                b.onClickEvent.AddListener(() => __instance._firstNavElement = b.gameObject);

                if (i - 1 <= GameDataManager.campaignStats[Game.Instance.GetGameData().GetCurrentCampaign().campaignID].GetFarthestMission() || GS.unlockLevels)
                {
                    b.SetLocked(val: false);
                    if (isHintMission)
                    {
                        if (mission.levels.Count == 0 || !mission.levels.Any(x => Logic.Level(x).CanAccessLevel()))
                        {
                            b.SetLocked(mission.levels.Count == 0);

                            b.localizedText.SetKey("NWArchipelago/MISSION_NAME_V2", [
                                new AxKReplacementPair("{OG}", mission.missionDisplayName),
                                new AxKReplacementPair("{PRE1}", "NWArchipelago/HINTS_LEVELSNONE"),
                            ]);
                        }
                        else
                        {
                            var inlogic = Logic.AllHinted();
                            b.localizedText.SetKey("NWArchipelago/MISSION_NAME_V2", [
                                new AxKReplacementPair("{OG}", mission.missionDisplayName),
                                new AxKReplacementPair("{PRE1}", "NWArchipelago/HINTS_SOME_LOGIC"),
                                new AxKReplacementPair("{HN}", inlogic),
                            ]);
                        }
                    }
                    else
                    {
                        b.localizedText.SetKey("NWArchipelago/MISSION_NAME_V2", [
                            new AxKReplacementPair("{OG}", mission.missionDisplayName),
                            new AxKReplacementPair("{MN}", i),
                            new AxKReplacementPair("{PRE1}", Logic.GetStrings(out var s, mission: mission)),
                            ..s.Replacements,
                        ]);
                    }
                    b.buttonTextRef.lineSpacing = -15;
                    SetButtonColor(b.ButtonRef, Logic.GetColor(mission));
                }
                else if (!doneLocked)
                {
                    doneLocked = true;
                    b.SetLocked(val: true);

                    b.localizedText.SetKey("NWArchipelago/MISSION_NAME_V2", [
                        new AxKReplacementPair("{OG}", mission.missionDisplayName),
                        new AxKReplacementPair("{MN}", i),
                        new AxKReplacementPair("{PRE1}", APManage.SlotData.unlockMethod == APManage.UnlockMethod.Missions ? "" : "NWArchipelago/MISSION_RANKS"),
                        new AxKReplacementPair("{R0}", SaveHandler.archiSaveData.neonRank),
                        new AxKReplacementPair("{R1}", mission.medalsRequired),
                    ]);
                    b.buttonTextRef.lineSpacing = -15;
                }
                else
                    b.gameObject.SetActive(value: false);

                if (mission.missionID.StartsWith("M_SIDEQUESTS"))
                    b.GetComponentInChildren<MenuButtonMission>()._textMissionIndex.text =
                        Campaign.sidequestLRegex.Match(mission.missionID).Groups[1].Value;
                else if (isHintMission)
                    b.GetComponentInChildren<MenuButtonMission>()._textMissionIndex.text = "";
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

            if (sortLevel.Value) {
                // order by:
                // level access, out of logic checks, in logic checks, hint count
                levels = [.. levels
                    .OrderByDescending(x => Logic.Level(x).CanAccessLevel())
                    .OrderByDescending(x => Logic.Level(x, true).Checks())
                    .OrderByDescending(x => Logic.Level(x).Checks())
                    .OrderByDescending(x => Logic.Level(x).Hinteds())];
            }

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
            Logic.GetStrings(out var strings, level: ld);

            if (Logic.display.Value >= Logic.LogicDisplay.Full)
            {
                var leveldisplay = showKey.Value ? "NWArchipelago/LEVEL_NAME_WKEY" : ld.GetLevelDisplayName();
                string str;
                int num;

                if (strings.hN > 0 && currentMissionID != HINTED_MISSION_ID)
                {
                    str = strings.hS;
                    num = strings.hN;
                }
                else
                {
                    str = strings.cS;
                    num = strings.cN;
                }

                __instance._textLevelName_Localized.SetKey("NWArchipelago/LEVEL_NAME", [
                    new AxKReplacementPair("{OG?}", leveldisplay),
                    new AxKReplacementPair("{KEY}", Campaign.levelKey[ld.levelID], false),
                    new AxKReplacementPair("{OG}", ld.GetLevelDisplayName()),
                    new AxKReplacementPair("{CHK}", str),
                    new AxKReplacementPair("{CN}", num),
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

            SetButtonColor(__instance._button, Logic.GetColor(level: ld));


            if (APManage.SlotData.unlockMethod == APManage.UnlockMethod.Levels)
                __instance.SetLocked(!Campaign.unlockedLevels.Contains(ld.levelIntegerID));
        }

        static float preButtonSpeed;
        static void PreFasterButtons(MenuScreen __instance, ref float ___buttonLoadDelay)
        {
            if (__instance is not MenuScreenLevel)
                return;
            preButtonSpeed = ___buttonLoadDelay;
            ___buttonLoadDelay = Math.Min(___buttonLoadDelay, .5f / __instance.buttonsToLoad.Count);
        }

        static void PostFasterButtons(MenuScreen __instance, ref float ___buttonLoadDelay)
        {
            if (__instance is not MenuScreenLevel)
                return;
            ___buttonLoadDelay = preButtonSpeed;
        }


        static void ReplaceEnvironment(LevelInfo __instance, LevelData ____currentLevel)
        {
            if (Logic.display.Value < Logic.LogicDisplay.Full || !Logic.HasLogic(____currentLevel))
                return;
            __instance._levelEnvironmentNameText.text = LocalizationManager
                .GetTranslation("NWArchipelago/CHECKS_REMAINING")
                .Replace("{CHK}", LocalizationManager.GetTranslation(Logic.GetStrings(out var s, level: ____currentLevel, hints: false)))
                .Replace("{CN}", s.cN.ToString());
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

                if (!Check(logic.Full(Logic.outOfLogic.Value)))
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
                else
                    text.color = Logic.GetColor(level: level, medal: medal, gift: gift);

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
            var gd = Game.Instance.GetGameData();
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
