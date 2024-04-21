using AbilitiesExperienceBars;
using BetterTrainLoot.Config;
using BetterTrainLoot.Data;
using BetterTrainLoot.GamePatch;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Locations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BetterTrainLoot
{
    public class BetterTrainLootMod : Mod
    {
        public static BetterTrainLootMod Instance { get; private set; }
        public static int numberOfRewardsPerTrain = 0;

        internal static Multiplayer multiplayer;

        internal Harmony harmony { get; private set; }

        private int maxNumberOfTrains;
        private int numberOfTrains = 0;
        private int startTimeOfFirstTrain = 600;

        internal TRAINS trainType;

        private double pctChanceOfNewTrain = 0.0;

        private bool startupMessage = true;
        private bool forceNewTrain;
        private bool enableCreatedTrain = true;
        private bool railroadMapBlocked;
        private bool isMainPlayer;

        internal ModConfig config;
        internal Dictionary<TRAINS, TrainData> trainCars;

        private Railroad railroad;

        public override void Entry(IModHelper helper)
        {
            Instance = this;

            config = helper.Data.ReadJsonFile<ModConfig>("config.json") ?? ModConfigDefaultConfig.CreateDefaultConfig("config.json");
            config = ModConfigDefaultConfig.UpdateConfigToLatest(config, "config.json");

            if (config.enableMod)
            {
                helper.Events.Input.ButtonReleased += Input_ButtonReleased;
                helper.Events.GameLoop.DayStarted += GameLoop_DayStarted;
                helper.Events.GameLoop.TimeChanged += GameLoop_TimeChanged;
                helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
                helper.Events.GameLoop.GameLaunched += OnGameLaunched;

                harmony = new Harmony("com.aairthegreat.mod.trainloot");
                harmony.Patch(typeof(TrainCar).GetMethod("draw"), null, new HarmonyMethod(typeof(TrainCarOverrider).GetMethod("postfix_getTrainTreasure")));
                harmony.Patch(typeof(Railroad).GetMethod("PlayTrainApproach"), new HarmonyMethod(typeof(RailroadOverrider).GetMethod("prefix_playTrainApproach")));
                string trainCarFile = Path.Combine("DataFiles", "trains.json");
                trainCars = helper.Data.ReadJsonFile<Dictionary<TRAINS, TrainData>>(trainCarFile) ?? TrainDefaultConfig.CreateTrainCarData(trainCarFile);

                bool updateLoot = false;

                //updated list to include new base game treasure
                foreach (var train in  trainCars.Values)
                    if (!train.HasItem("(B)806"))
                    {
                        train.treasureList.Add(new TrainTreasure("(B)806", "Leprechaun Shoes", 0.01, LOOT_RARITY.RARE, true));
                        updateLoot = true;
                    }

                if (updateLoot)
                    helper.Data.WriteJsonFile(trainCarFile, trainCars);

                SetupMultiplayerObject();
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Config Menu
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null)
            {
                configMenu.Register(
                    mod: ModManifest,
                    reset: () => config = new ModConfig(),
                    save: () => Helper.WriteConfig(config)
                );

                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Enable",
                    tooltip: () => "Sets the mod as enabled or disabled. Normally should be set to true unless you are having issues and want to test something without removing the mod.",
                    getValue: () => config.enableMod,
                    setValue: value => config.enableMod = value
                );

                configMenu.AddSectionTitle(
                    mod: ModManifest,
                    text: () => "Train Treasure"
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Custom Treasure List",
                    tooltip: () => "Uses the custom treasure list. If set to false, then the base game item list is used.",
                    getValue: () => config.useCustomTrainTreasure,
                    setValue: value => config.useCustomTrainTreasure = value
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Treasure Limite Per Train",
                    tooltip: () => "Maximum treasure from each train. The amount is still random but there is not limit on the amount.",
                    getValue: () => config.enableNoLimitTreasurePerTrain,
                    setValue: value => config.enableNoLimitTreasurePerTrain = value
                );

                configMenu.AddSectionTitle(
                    mod: ModManifest,
                    text: () => "Train Messages"
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Train Message",
                    tooltip: () => "Shows/hides the message when a train is passing thru the valley and you are not in the desert.",
                    getValue: () => config.showTrainIsComingMessage,
                    setValue: value => config.showTrainIsComingMessage = value
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Desert Train Message",
                    tooltip: () => "Shows/hides the message when a train is passing thru the valley while you are in the desert.",
                    getValue: () => config.showDesertTrainIsComingMessage,
                    setValue: value => config.showDesertTrainIsComingMessage = value
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Island Train Message",
                    tooltip: () => "Shows/hides the message when a train is passing thru the valley while you are on the island.",
                    getValue: () => config.showIslandTrainIsComingMessage,
                    setValue: value => config.showIslandTrainIsComingMessage = value
                );

                configMenu.AddSectionTitle(
                    mod: ModManifest,
                    text: () => "Train Whistles"
                );

                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Train Whistle",
                    tooltip: () => "When the train comes thru the valley and you are not in the desert, does it make a sound?",
                    getValue: () => config.enableTrainWhistle,
                    setValue: value => config.enableTrainWhistle = value
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Desert Train Whistle",
                    tooltip: () => "When the train comes thru the valley and you are in the desert, does it make a sound?",
                    getValue: () => config.enableDesertTrainWhistle,
                    setValue: value => config.enableDesertTrainWhistle = value
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Island Train Whistle",
                    tooltip: () => "When the train comes thru the valley and you are on the island, does it make a sound?",
                    getValue: () => config.enableIslandTrainWhistle,
                    setValue: value => config.enableIslandTrainWhistle = value
                );

                configMenu.AddSectionTitle(
                    mod: ModManifest,
                    text: () => "Train Creation"
                );
                configMenu.AddNumberOption(
                    mod: ModManifest,
                    name: () => "Base Item Chance",
                    tooltip: () => "What is the chance to get something from a train. The player's daily luck does factor into this.",
                    getValue: () => config.baseChancePercent,
                    setValue: value => config.baseChancePercent = value
                );
                configMenu.AddNumberOption(
                    mod: ModManifest,
                    name: () => "Chance of Train",
                    tooltip: () => "Every time the game time changes, this the percent chance of a new train, assuming the daily maximum has not been met.",
                    getValue: () => config.basePctChanceOfTrain,
                    setValue: value => config.basePctChanceOfTrain = value
                );
                configMenu.AddNumberOption(
                    mod: ModManifest,
                    name: () => "Creation Delay",
                    tooltip: () => "How many milliseconds from when the message about a train is going thru Stardew Valley and when the train shows up.",
                    getValue: () => config.trainCreateDelay,
                    setValue: value => config.trainCreateDelay = value
                );
                configMenu.AddNumberOption(
                    mod: ModManifest,
                    name: () => "Max Trains",
                    tooltip: () => "Sets the maximum possible number of trains.",
                    getValue: () => config.maxTrainsPerDay,
                    setValue: value => config.maxTrainsPerDay = value
                );
                configMenu.AddNumberOption(
                    mod: ModManifest,
                    name: () => "Max Items per Train",
                    tooltip: () => "Limits the amount of items per train that the mod will create.",
                    getValue: () => config.maxNumberOfItemsPerTrain,
                    setValue: value => config.maxNumberOfItemsPerTrain = value
                );

                configMenu.AddSectionTitle(
                    mod: ModManifest,
                    text: () => "Other Options"
                );
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => "Force Train Creation",
                    tooltip: () => "If value is true, allows the user to force a train to be created by pressing the button O.",
                    getValue: () => config.enableForceCreateTrain,
                    setValue: value => config.enableForceCreateTrain = value
                );
            }
        }

        private void Input_ButtonReleased(object sender, StardewModdingAPI.Events.ButtonReleasedEventArgs e)
        {
            if (e.Button == SButton.O && !railroadMapBlocked 
                && config.enableForceCreateTrain && isMainPlayer)
            {
                this.Monitor.Log("Player press O... Choo choo");                
                forceNewTrain = true;
                enableCreatedTrain = true;
            }
            else if (e.Button == SButton.O && railroadMapBlocked)
                this.Monitor.Log("Player press O, but the railraod map is not available... No choo choo for you.");
        }

        private void GameLoop_SaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            railroad = (Game1.getLocationFromName("Railroad") as Railroad);
            startupMessage = true;
        }

        private void GameLoop_TimeChanged(object sender, StardewModdingAPI.Events.TimeChangedEventArgs e)
        {
            if (railroad != null && !railroadMapBlocked && isMainPlayer)
            {
                if (Game1.player.currentLocation.IsOutdoors && railroad.train.Value == null)
                {
                    var ranValue = Game1.random.NextDouble();
                    if (forceNewTrain)
                        CreateNewTrain();
                    else if (enableCreatedTrain && numberOfTrains < maxNumberOfTrains && e.NewTime >= startTimeOfFirstTrain && ranValue <= pctChanceOfNewTrain)
                    {
                        this.Monitor.Log($"Creating Train: {ranValue} <= {pctChanceOfNewTrain} from {e.NewTime} >= {startTimeOfFirstTrain}");
                        CreateNewTrain();
                    }
                }

                if (railroad.train.Value != null && !enableCreatedTrain)
                {
                    enableCreatedTrain = true;
                    trainType = (TRAINS)railroad.train.Value.type.Value;
                }
            }
        }

        private void GameLoop_DayStarted(object sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            if (CheckForMapAccess())
            {
                if (IsMainPlayer())
                {
                    ResetDailyValues();
                    SetMaxNumberOfTrainsAndStartTime();
                }
                UpdateTrainLootChances();
            }            
        }

        private bool IsMainPlayer()
        {
            if (Context.IsMainPlayer)
            {
                if (!isMainPlayer)
                {
                    isMainPlayer = true;
                    startupMessage = true;
                }
            }
            else
                isMainPlayer = false;

            SetStartupMessage();

            return isMainPlayer;
        }

        private void SetStartupMessage()
        {
            if (startupMessage && isMainPlayer)
                this.Monitor.Log("Single player or Host:  Mod Enabled.");
            else if (startupMessage && !isMainPlayer)
                this.Monitor.Log("Farmhand player: (Mostly) Mod Disabled.");

            startupMessage = false;
        }

        private bool CheckForMapAccess()
        {
            railroadMapBlocked = (Game1.stats.DaysPlayed < 31U);
            if (railroadMapBlocked)
                Monitor.Log("Railroad map blocked.  No trains can be created, yet.");

            return !railroadMapBlocked;
        }

        private void CreateNewTrain()
        {
            numberOfRewardsPerTrain = 0;
            railroad.setTrainComing(config.trainCreateDelay);
            numberOfTrains++;
            forceNewTrain = false;
            trainType = TRAINS.UNKNOWN;

            //this.Monitor.Log($"Setting train... Choo choo... {Game1.timeOfDay}");
            enableCreatedTrain = false;            
        }

        private void ResetDailyValues()
        {
            forceNewTrain = false;
            enableCreatedTrain = true;
            numberOfTrains = 0;
            numberOfRewardsPerTrain = 0;
            pctChanceOfNewTrain = Game1.player.DailyLuck + config.basePctChanceOfTrain;                                                                                    
        }

        private void SetMaxNumberOfTrainsAndStartTime()
        {
            maxNumberOfTrains = (int)Math.Round((Game1.random.NextDouble() + Game1.player.DailyLuck) * (double)config.maxTrainsPerDay, 0, MidpointRounding.AwayFromZero);  

            double ratio = (double)maxNumberOfTrains / (double)config.maxTrainsPerDay;  

            startTimeOfFirstTrain = 1200 - (int)(ratio * 500);
            
            Monitor.Log($"Setting Max Trains to {maxNumberOfTrains}");
        }

        private void UpdateTrainLootChances()
        {
            //Update the treasure chances for today
            foreach (TrainData train in trainCars.Values)
                train.UpdateTrainLootChances(Game1.player.DailyLuck);                                                                                                     
        }

        private void SetupMultiplayerObject()
        {
            Type type = typeof(Game1);
            FieldInfo info = type.GetField("multiplayer", BindingFlags.NonPublic | BindingFlags.Static);
            multiplayer = info.GetValue(null) as Multiplayer;
        }
    }
}
