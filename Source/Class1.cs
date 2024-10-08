using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net.Sockets;

using UnityEngine;
using Verse;
using RimWorld;
using System.Net;
using Verse.AI.Group;
using Verse.Noise;


namespace WebsocketEvents
{
    public class ExternalEventReceiver
    {
        private TcpListener listener;

        public ExternalEventReceiver()
        {
            try
            {
                this.listener = new TcpListener(IPAddress.Any, 12345);
                this.listener.Start();
                Log.Message("TCP Listener started successfully on port 12345.");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to start TCP Listener: " + ex.Message);
            }
        }

        public void CheckForExternalData()
        {
            if (!this.listener.Pending())
                return;

            TcpClient tcpClient = this.listener.AcceptTcpClient();
            NetworkStream stream = tcpClient.GetStream();
            byte[] numArray = new byte[tcpClient.ReceiveBufferSize];

            int bytesRead = stream.Read(numArray, 0, numArray.Length);

            string message = Encoding.ASCII.GetString(numArray, 0, bytesRead);
            Log.Message($"Received message: {message} Length of message: {message.Length}");
            this.ProcessMessage(message);
        }

        public void ProcessMessage(string message)
        {

            string[] parts = message.Replace("\"", "").TrimEnd().Split(':');
            if (parts.Length == 0)
            {
                Log.Error("Invalid message format.");
                return;
            }

            string command = parts[0].ToLower();
            string[] parameters = parts.Skip(1).ToArray();

            switch (command)
            {
                case "raid":
                    HandleRaidGeneral(null, -1.0f, null);
                    break;
                case "siege":
                    HandleRaidGeneral(null, -1.0f, DefDatabase<RaidStrategyDef>.GetNamed("Siege"));
                    break;
                case "itempods":
                    ItemDropPods((parameters.Length == 0) ? null : parameters[0]);
                    break;
                case "boom":
                    TriggerRandomExplosion();
                    break;
                case "wanderer":
                    WandererJoinsEvent((parameters.Length == 0) ? null : parameters[0]);
                    break;
                case "retreat":
                    ForceEnemyFlee();
                    break;
                case "resurrect":
                    ResurrectAllCorpses();
                    break;
                case "weather":
                    ChangeWeather((parameters.Length == 0) ? null : parameters[0]);
                    break;
                case "solarflare":
                    CallEventSimple("SolarFlare");
                    break;
                case "eclipse":
                    CallEventSimple("Eclipse");
                    break;
                case "aurora":
                    CallEventSimple("Aurora");
                    break;
                case "fallout":
                    CallEventSimple("ToxicFallout");
                    break;
                case "zzzt":
                    CallEventSimple("ShortCircuit");
                    break;
                case "message":
                    Find.LetterStack.ReceiveLetter(
                        (parameters.Length < 1) ? new TaggedString("") : new TaggedString(parameters[0]),  // Title of the message
                        (parameters.Length < 2) ? new TaggedString("") : new TaggedString(parameters[1]),  // Body of letter
                        LetterDefOf.NeutralEvent
                    );
                    break;
                default:
                    Log.Error("Unknown command received: " + command);
                    break;
            }
        }

        private void CallEventSimple(string eventName)
        {
            IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(eventName);
            if (incidentDef == null)
            {
                return;
            }
            IncidentParms parms = new IncidentParms
            {
                target = Find.CurrentMap
            };
            bool success = incidentDef.Worker.TryExecute(parms);

            if (!success)
            {
                Log.Warning($"Failed to trigger event: {eventName}");
            }
        }


        private void ChangeWeather(string weatherName)
        {
            if (weatherName == null)
            {
                RandomizeWeather();
                return;
            }
            WeatherDef weatherDef = DefDatabase<WeatherDef>.GetNamedSilentFail(weatherName);

            if (weatherDef == null)
            {
                Log.Warning($"Weather name '{weatherName}' is invalid. Randomizing weather.");
                RandomizeWeather();
            }
            else
            {
                // Set the weather to the provided weatherDef
                SetWeatherTo(weatherDef);
            }
        }

        private void RandomizeWeather()
        {
            List<WeatherDef> allWeatherDefs = DefDatabase<WeatherDef>.AllDefsListForReading;
            WeatherDef randomWeather = allWeatherDefs.RandomElement();
            SetWeatherTo(randomWeather);
        }

        private void SetWeatherTo(WeatherDef weatherDef)
        {
            Find.CurrentMap.weatherManager.TransitionTo(weatherDef);
        }


        private void ResurrectAllCorpses()
        {
            List<Thing> corpses = Find.CurrentMap.listerThings.AllThings.Where(thing => thing is Corpse).ToList();

            foreach (Corpse corpse in corpses)
            {
                ResurrectionUtility.TryResurrect(corpse.InnerPawn);
            }

            if (corpses.Count > 0)
            {

                bool anyEnemiesResurrected = corpses
                    .OfType<Corpse>() // Cast to Corpses
                    .Any(corpse => corpse.InnerPawn != null &&
                    corpse.InnerPawn.Faction != null &&
                    corpse.InnerPawn.Faction.HostileTo(Faction.OfPlayer));

                if (anyEnemiesResurrected)
                {
                    Find.LetterStack.ReceiveLetter(
                   new TaggedString("Resurrection Event")
                   , new TaggedString($"A total of {corpses.Count} corpses were resurrected on the current map!"),
                   LetterDefOf.ThreatBig);
                    Find.TickManager.slower.SignalForceNormalSpeedShort();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                }
                else
                {
                    Find.LetterStack.ReceiveLetter(
                   new TaggedString("Resurrection Event")
                   , new TaggedString($"A total of {corpses.Count} corpses were resurrected on the current map!"),
                   LetterDefOf.PositiveEvent);
                }

            }

        }

        private void ForceEnemyFlee()
        {
            foreach (Lord lord in Find.CurrentMap.lordManager.lords)
            {
                if (lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer) && lord.faction.def.autoFlee)
                {
                    LordToil newLordToil = lord.Graph.lordToils.FirstOrDefault<LordToil>((Predicate<LordToil>)(st => st is LordToil_PanicFlee));
                    if (newLordToil != null)
                        lord.GotoToil(newLordToil);
                }
            }

            Messages.Message("All foes on the map are retreating.", null, MessageTypeDefOf.PositiveEvent);
        }
        private void WandererJoinsEvent(string customName)
        {
            Map map = Find.CurrentMap;

            if (map == null)
            {
                return;
            }

            Pawn newColonist = GenerateWandererPawn();

            if (newColonist == null)
            {
                return;
            }

            if (customName != null)
            {
                if (newColonist.Name is NameTriple nameTriple)
                {
                    newColonist.Name = new NameTriple(nameTriple.First, customName, nameTriple.Last);
                }
            }

            Faction playerFaction = Faction.OfPlayer;
            newColonist.SetFaction(playerFaction);

            IntVec3 spawnLocation = GetRandomMapLocation(map);
            GenSpawn.Spawn(newColonist, spawnLocation, map);

            Find.LetterStack.ReceiveLetter(
                "Wanderer Joins",  // Title of the message
                $"{newColonist.Name.ToStringFull} has joined your colony!",
                LetterDefOf.PositiveEvent,
                new LookTargets(newColonist)  // Focus the camera on the new colonist
            );
        }

        private void TriggerRandomExplosion()
        {
            Map map = Find.CurrentMap;

            if (map == null)
            {
                return;
            }

            IntVec3 centerOf = GetRandomMapLocation(map);

            GenExplosion.DoExplosion(
                center: centerOf,
                map: map,
                radius: 4.9f,
                damType: DamageDefOf.Bomb,
                instigator: null,
                postExplosionSpawnThingDef: null,
                postExplosionSpawnChance: 0f,
                postExplosionSpawnThingCount: 0,
                postExplosionGasType: null,
                applyDamageToExplosionCellsNeighbors: false
            );

            Messages.Message("An explosion occured.", new LookTargets(new TargetInfo(centerOf, map)), MessageTypeDefOf.NegativeEvent);
        }

        private Pawn GenerateWandererPawn()
        {
            PawnKindDef wandererKind = PawnKindDefOf.Colonist;

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: wandererKind,
                faction: Faction.OfPlayer,
                context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                colonistRelationChanceFactor: 30f
            );

            return PawnGenerator.GeneratePawn(request);
        }

        private void ItemDropPods(string itemName)
        {

            IncidentParms parms = new IncidentParms
            {
                target = Find.CurrentMap,
                points = StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap)
            };

            ThingDef actualItem;

            if (itemName == null)
            {
                actualItem = GetRandomItem();
            }
            else
            {
                actualItem = DefDatabase<ThingDef>.GetNamedSilentFail(itemName);
                if (actualItem == null)
                {
                    Log.Error($"No item named {itemName}, drop pods cancelling...");
                    return;
                }
            }

            parms.spawnCenter = GetRandomMapLocation(Find.CurrentMap);


            List<Thing> things = new List<Thing>();

            int podcount = Rand.RangeInclusive(1, 6);

            ThingDef chosenStuff = null;

            for (int i = 0; i < podcount; i++)
            {
                if (actualItem.MadeFromStuff)
                {
                    List<ThingDef> allStuffs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(stuff => actualItem.stuffCategories != null && stuff.stuffProps != null && stuffCategoriesOverlap(actualItem.stuffCategories, stuff.stuffProps.categories))
                        .ToList();
                    chosenStuff = allStuffs.RandomElement();
                }
                Thing item = ThingMaker.MakeThing(actualItem, chosenStuff);
                item.stackCount = (actualItem.stackLimit > 10) ? Rand.RangeInclusive(10, 75) : actualItem.stackLimit;
                things.Add(item);
            }
            DropPodUtility.DropThingsNear(parms.spawnCenter, Find.CurrentMap, things, 110, false, true, true, false);

            Find.LetterStack.ReceiveLetter(
                "Cargo Pods",
                "You have detected a cluster of cargo pods dropping nearby.\n\nPerhaps you'll find something useful in the wreckage.",
                LetterDefOf.PositiveEvent,
                new TargetInfo(parms.spawnCenter, Find.CurrentMap)
            );

        }

        private static bool stuffCategoriesOverlap(List<StuffCategoryDef> thingStuffCategories, List<StuffCategoryDef> stuffCategories)
        {
            return thingStuffCategories.Any(category => stuffCategories.Contains(category));
        }

        private ThingDef GetRandomItem()
        {
            List<ThingDef> allItems = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => !def.IsBlueprint && !def.IsFrame && def.category == ThingCategory.Item && def.EverHaulable)
                .ToList();

            if (allItems.Count > 0)
            {
                return allItems.RandomElement();
            }
            else
            {
                return null;
            }
        }

        private IntVec3 GetRandomMapLocation(Map map)
        {
            int mapWidth = map.Size.x;
            int mapHeight = map.Size.z;

            int randomX = Rand.Range(0, mapWidth);
            int randomZ = Rand.Range(0, mapHeight);

            IntVec3 randomLocation = new IntVec3(randomX, 0, randomZ);
            if (randomLocation.InBounds(map) && randomLocation.Standable(map))
            {
                return randomLocation;
            }
            else
            {
                return GetRandomMapLocation(map);
            }
        }

        private bool CanUseStrategy(RaidStrategyDef def, IncidentParms parms, PawnGroupKindDef groupKind)
        {
            if (def == null || !def.Worker.CanUseWith(parms, groupKind))
            {
                return false;
            }

            if (parms.raidArrivalMode != null)
            {
                return true;
            }

            return def.arriveModes != null && def.arriveModes.Any(mode => mode.Worker.CanUseWith(parms));
        }


        private void HandleRaidGeneral(Faction raid_faction, float raid_points, RaidStrategyDef raid_strategy)
        {
            try
            {
                IncidentParms raidParms = Find.Storyteller.storytellerComps.First<StorytellerComp>((Func<StorytellerComp, bool>)(x => x is StorytellerComp_OnOffCycle || x is StorytellerComp_RandomMain)).GenerateParms(IncidentCategoryDefOf.ThreatBig, (IIncidentTarget)Find.CurrentMap);


                raidParms.target = Find.CurrentMap;
                raidParms.faction = (raid_faction == null) ? Find.FactionManager.RandomEnemyFaction() : raid_faction;

                raidParms.raidStrategy = (raid_strategy == null) ? RaidStrategyDefOf.ImmediateAttack : raid_strategy;

                raidParms.points = (raid_points < 0) ? StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap) : raid_points;

                bool factionCanRaid = false;

                int attempts = 0;

                while (!factionCanRaid)
                {
                    raidParms.faction = Find.FactionManager.RandomEnemyFaction();

                    if (raidParms.raidStrategy.Worker.CanUseWith(raidParms, PawnGroupKindDefOf.Combat))
                    {
                        factionCanRaid = true;
                    }
                    else
                    {
                        attempts++;
                    }
                    // Times out after 100 attempts, or a single attempt if faction is specified
                    if (attempts == 100 || (!factionCanRaid && raid_faction != null))
                    {
                        Log.Message($"No factions can raid with {raidParms.points} points");
                        return;
                    }
                }

                raidParms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;

                IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms);
            }
            catch (Exception ex)
            {
                Log.Error("Error handling raid command: " + ex.Message);
            }
        }

        public class MyMapComponent : MapComponent
        {
            private ExternalEventReceiver eventReceiver;

            public MyMapComponent(Map map)
                : base(map)
            {
                this.eventReceiver = new ExternalEventReceiver();
                Log.Message("MyMapComponent initialized.");
            }

            public override void MapComponentTick()
            {
                base.MapComponentTick();
                this.eventReceiver.CheckForExternalData();
            }

        }
    }
}