// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using System;
using System.Text.RegularExpressions;
using DaggerfallWorkshop.Utility;
using FullSerializer;
using DaggerfallWorkshop.Game.Utility;

namespace DaggerfallWorkshop.Game.Questing
{
    /// <summary>
    /// Spawn a Foe resource into the world.
    /// </summary>
    public class CreateFoe : ActionTemplate
    {
        const string optionsMatchStr = @"msg (?<msgId>\d+)";

        Symbol foeSymbol;
        uint spawnInterval;
        int spawnMaxTimes = -1;
        int spawnChance;
        int msgMessageID = -1;

        ulong lastSpawnTime = 0;
        int spawnCounter = 0;

        bool spawnInProgress = false;
        GameObject[] pendingFoeGameObjects;
        int pendingFoesSpawned;
        bool isSendAction = false;

        public override string Pattern
        {
            get
            {
                return @"create foe (?<symbol>[a-zA-Z0-9_.-]+) every (?<minutes>\d+) minutes (?<infinite>indefinitely) with (?<percent>\d+)% success|" +
                       @"create foe (?<symbol>[a-zA-Z0-9_.-]+) every (?<minutes>\d+) minutes (?<count>\d+) times with (?<percent>\d+)% success|" +
                       @"(?<send>send) (?<symbol>[a-zA-Z0-9_.-]+) every (?<minutes>\d+) minutes (?<count>\d+) times with (?<percent>\d+)% success|" +
                       @"(?<send>send) (?<symbol>[a-zA-Z0-9_.-]+) every (?<minutes>\d+) minutes with (?<percent>\d+)% success";
            }
        }

        public CreateFoe(Quest parentQuest)
            : base(parentQuest)
        {
            PlayerEnterExit.OnTransitionDungeonExterior += PlayerEnterExit_OnTransitionExterior;
            PlayerEnterExit.OnTransitionExterior += PlayerEnterExit_OnTransitionExterior;
            StreamingWorld.OnInitWorld += StreamingWorld_OnInitWorld;
        }

        public override void InitialiseOnSet()
        {
            lastSpawnTime = 0;
            spawnCounter = 0;
        }

        public override IQuestAction CreateNew(string source, Quest parentQuest)
        {
            // Source must match pattern
            Match match = Test(source);
            if (!match.Success)
                return null;

            // Factory new action
            CreateFoe action = new CreateFoe(parentQuest);
            action.foeSymbol = new Symbol(match.Groups["symbol"].Value);
            action.spawnInterval = (uint)Parser.ParseInt(match.Groups["minutes"].Value) * 60;
            action.spawnMaxTimes = Parser.ParseInt(match.Groups["count"].Value);
            action.spawnChance = Parser.ParseInt(match.Groups["percent"].Value);

            // Handle infinite
            if (!string.IsNullOrEmpty(match.Groups["infinite"].Value))
                action.spawnMaxTimes = -1;

            // Handle "send" variant
            if (!string.IsNullOrEmpty(match.Groups["send"].Value))
            {
                action.isSendAction = true;

                // "send" without "count" implies infinite
                if (action.spawnMaxTimes == 0)
                    action.spawnMaxTimes = -1;
            }

            // Split options from declaration
            string optionsSource = source.Substring(match.Length);
            MatchCollection options = Regex.Matches(optionsSource, optionsMatchStr);
            foreach (Match option in options)
            {
                // Message ID
                Group msgIDGroup = option.Groups["msgId"];
                if (msgIDGroup.Success)
                    action.msgMessageID = Parser.ParseInt(msgIDGroup.Value);
            }

            return action;
        }

        public override void Update(Task caller)
        {
            ulong gameSeconds = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToSeconds();

            // Init spawn timer on first update
            if (lastSpawnTime == 0)
                lastSpawnTime = gameSeconds - (uint)UnityEngine.Random.Range(0, spawnInterval);

            // Do nothing if max foes already spawned
            // This can be cleared on next set/rearm
            if (spawnCounter >= spawnMaxTimes && spawnMaxTimes != -1)
                return;

            // Clear pending foes if all have been spawned
            if (spawnInProgress && pendingFoesSpawned >= pendingFoeGameObjects.Length)
            {
                spawnInProgress = false;
                spawnCounter++;
                return;
            }

            // Check for a new spawn event - only one spawn event can be running at a time
            if (gameSeconds >= lastSpawnTime + spawnInterval && !spawnInProgress)
            {
                // Update last spawn time
                lastSpawnTime = gameSeconds;

                // Roll for spawn chance
                if (Dice100.FailedRoll(spawnChance))
                    return;

                // Get the Foe resource
                Foe foe = ParentQuest.GetFoe(foeSymbol);
                if (foe == null)
                {
                    SetComplete();
                    throw new Exception(string.Format("create foe could not find Foe with symbol name {0}", Symbol.Name));
                }

                // Do not spawn if foe is hidden
                if (foe.IsHidden)
                    return;

                // Start deploying GameObjects
                CreatePendingFoeSpawn(foe);
            }

            // Try to deploy a pending spawns
            if(spawnInProgress){
                TryPlacement();
                GameManager.Instance.RaiseOnEncounterEvent();
            }
        }

        #region Private Methods

        void CreatePendingFoeSpawn(Foe foe)
        {
            // Get foe GameObjects
            pendingFoeGameObjects = GameObjectHelper.CreateFoeGameObjects(Vector3.zero, foe.FoeType, foe.SpawnCount, MobileReactions.Hostile, foe);
            if (pendingFoeGameObjects == null || pendingFoeGameObjects.Length != foe.SpawnCount)
            {
                SetComplete();
                throw new Exception(string.Format("create foe attempted to create {0}x{1} GameObjects and failed.", foe.SpawnCount, Symbol.Name));
            }

            // Initiate deployment process
            // Usually the foe will spawn immediately but can take longer depending on available placement space
            // This process ensures these foes have all been deployed before starting next cycle
            spawnInProgress = true;
            pendingFoesSpawned = 0;
        }

        bool TryPlacement(bool placeEnemyBehindPlayer = true)
        {
            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;

            // The "send" variant is only used when player within a town/exterior location
            // The placement will remain pending until player matches conditions
            if (isSendAction)
            {
                if (!GameManager.Instance.PlayerGPS.IsPlayerInLocationRect)
                    return false;
            }

            // Place in world near player depending on local area
            if (playerEnterExit.IsPlayerInsideBuilding)
            {
                return PlaceFoeBuildingInterior(pendingFoeGameObjects, playerEnterExit.Interior, placeEnemyBehindPlayer);
            }
            else if (playerEnterExit.IsPlayerInsideDungeon)
            {
                return PlaceFoeDungeonInterior(pendingFoeGameObjects, playerEnterExit.Dungeon, placeEnemyBehindPlayer);
            }
            else if (!playerEnterExit.IsPlayerInside && GameManager.Instance.PlayerGPS.IsPlayerInLocationRect)
            {
                return PlaceFoeExteriorLocation(pendingFoeGameObjects, GameManager.Instance.StreamingWorld.CurrentPlayerLocationObject, placeEnemyBehindPlayer);
            }
            else
            {
                return PlaceFoeWilderness(pendingFoeGameObjects, placeEnemyBehindPlayer);
            }
        }

        #endregion

        #region Placement Methods

        // Place foe somewhere near player when inside a building
        // Building interiors have spawn nodes for this placement so we can roll out foes all at once
        bool PlaceFoeBuildingInterior(GameObject[] gameObjects, DaggerfallInterior interiorParent, bool placeEnemyBehindPlayer = true)
        {
            // Must have a DaggerfallLocation parent
            if (interiorParent == null)
            {
                SetComplete();
                throw new Exception("PlaceFoeFreely() must have a DaggerfallLocation parent object.");
            }

            // Always place foes around player rather than use spawn points
            // Spawn points work well for "interior hunt" quests but less so for "directly attack the player"
            // Feel just placing freely will yield best results overall
            return PlaceFoeFreely(gameObjects, interiorParent.transform, 5, 20, placeEnemyBehindPlayer);
        }

        // Place foe somewhere near player when inside a dungeon
        // Dungeons interiors are complex 3D environments with no navgrid/navmesh or known spawn nodes
        bool PlaceFoeDungeonInterior(GameObject[] gameObjects, DaggerfallDungeon dungeonParent, bool placeEnemyBehindPlayer = true)
        {
            return PlaceFoeFreely(gameObjects, dungeonParent.transform, 5, 20, placeEnemyBehindPlayer);
        }

        // Place foe somewhere near player when outside a location navgrid is available
        // Navgrid placement helps foe avoid getting tangled in geometry like buildings
        bool PlaceFoeExteriorLocation(GameObject[] gameObjects, DaggerfallLocation locationParent, bool placeEnemyBehindPlayer = true)
        {
            return PlaceFoeFreely(gameObjects, locationParent.transform, 5, 20, placeEnemyBehindPlayer);
        }

        // Place foe somewhere near player when outside and no navgrid available
        // Wilderness environments are currently open so can be placed on ground anywhere within range
        bool PlaceFoeWilderness(GameObject[] gameObjects, bool placeEnemyBehindPlayer = true)
        {
            // TODO this false will need to be true when start caching enemies
            GameManager.Instance.StreamingWorld.TrackLooseObject(gameObjects[pendingFoesSpawned], false, -1, -1, true);
            return PlaceFoeFreely(gameObjects, null, 8f, 25f, placeEnemyBehindPlayer, false);
        }

        // Uses raycasts to find next spawn position just outside of player's field of view
        bool PlaceFoeFreely(GameObject[] gameObjects, Transform parent, float minDistance = 5f, float maxDistance = 20f, bool placeEnemyBehindPlayer = true, bool lineOfSight = true, int spawnTries = 15)
        {
            const float maxFloorDistance = 4f;
            const float overlapSphereRadius = 0.65f;

            // override min/max distance in small interior spaces.
            var dfInterior = GameObject.FindObjectOfType<DaggerfallInterior>();
            if(dfInterior){
                	Bounds bounds = new Bounds (dfInterior.transform.position, Vector3.one);
                    Renderer[] renderers = dfInterior.GetComponentsInChildren<Renderer> ();
                    foreach (Renderer renderer in renderers)
                    {
                        bounds.Encapsulate (renderer.bounds);
                    }
                    Vector3 playerPos = GameManager.Instance.PlayerController.transform.position;
                    Vector3 boundsMin = bounds.min;
                    Vector3 boundsMax = bounds.max;
                    boundsMin.y = boundsMax.y = playerPos.y;
                    maxDistance = Mathf.Max(Vector3.Distance(playerPos, boundsMin), Vector3.Distance(playerPos, boundsMax));
                    minDistance = Mathf.Min(minDistance, maxDistance/4f);
            }

            // Must have received a valid array
            if (gameObjects == null || gameObjects.Length == 0)
                return false;

            // Skip this foe if destroyed (e.g. player left building where pending)
            if (!gameObjects[pendingFoesSpawned])
            {
                pendingFoesSpawned++;
                return true;
            }

            // Set parent - otherwise caller must set a parent
            if (parent)
                gameObjects[pendingFoesSpawned].transform.parent = parent;

            var enemyCC = gameObjects[pendingFoesSpawned].GetComponent<CharacterController>();
            var playerCC = GameManager.Instance.PlayerController;
            // Set parent if none specified already
            if (!gameObjects[pendingFoesSpawned].transform.parent)
                gameObjects[pendingFoesSpawned].transform.parent = GameObjectHelper.GetBestParent();
            
            for(int i =0; i < spawnTries; ++i){
                float fov = GameManager.Instance.MainCamera.fieldOfView;
                float randomAngle;
                if(!lineOfSight || i > spawnTries/3) // give up on line of sight checks after we've tried a few times
                    randomAngle = UnityEngine.Random.Range(-180, 180);
                else if(!placeEnemyBehindPlayer)
                    randomAngle = UnityEngine.Random.Range(-fov, fov);
                else
                    randomAngle = UnityEngine.Random.Range(fov, 180f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
                Quaternion rot = Quaternion.Euler(0, randomAngle, 0);
                Vector3 angle = (rot * Vector3.forward).normalized;
                Vector3 spawnDirection = GameManager.Instance.PlayerObject.transform.TransformDirection(angle).normalized;
                Vector3 playerFootPosition = playerCC.transform.position + playerCC.center - playerCC.height/2f * Vector3.down;
                Vector3 spawnPos = playerFootPosition  + spawnDirection * UnityEngine.Random.Range(minDistance, maxDistance);
                enemyCC.transform.rotation = playerCC.transform.rotation;
                spawnPos += enemyCC.height/2f * Vector3.up + enemyCC.center;

                // Must be able to find a surface below
                if (!Physics.Raycast(spawnPos, Vector3.down, out RaycastHit floorHit, maxFloorDistance, DFULayerMasks.CorporealMask))
                    continue;

                // Ensure this is open space
                spawnPos = floorHit.point + enemyCC.center + (enemyCC.height/2f + .5f)*Vector3.up;
                // Collider[] colliders = Physics.OverlapCapsule(spawnPos - enemyCC.height/2f * Vector3.up, spawnPos + enemyCC.height/2f * Vector3.up, enemyCC.radius, DFULayerMasks.CorporealMask);
                Collider[] colliders = Physics.OverlapSphere(spawnPos, overlapSphereRadius);
                if (colliders.Length > 0)
                    continue;

                Debug.Log($"CreateFoe: Found an enemy spawn point in {i} tries");

                // This looks like a good spawn position
                pendingFoeGameObjects[pendingFoesSpawned].transform.position = spawnPos;
                FinalizeFoe(pendingFoeGameObjects[pendingFoesSpawned]);
                gameObjects[pendingFoesSpawned].transform.LookAt(GameManager.Instance.PlayerObject.transform.position);

                // Send msg message on first spawn only
                if (msgMessageID != -1)
                {
                    ParentQuest.ShowMessagePopup(msgMessageID, oncePerQuest:true);
                    msgMessageID = -1;
                }

                // Increment count
                pendingFoesSpawned++;
                return true;
            }

            // Couldn't find a spawn point
            Debug.Log($"CreateFoe: Couldn't find a valid enemy spawn point after {spawnTries} tries.");
            return false;
        }
        // Fine tunes foe position slightly based on mobility and enables GameObject
        void FinalizeFoe(GameObject go)
        {
            var mobileUnit = go.GetComponentInChildren<MobileUnit>();
            if (mobileUnit)
            {
                // Align ground creatures on surface, raise flying creatures slightly into air
                if (mobileUnit.Enemy.Behaviour != MobileBehaviour.Flying)
                    GameObjectHelper.AlignControllerToGround(go.GetComponent<CharacterController>());
                else
                    go.transform.localPosition += Vector3.up * 1.5f;
            }
            else
            {
                // Just align to ground
                GameObjectHelper.AlignControllerToGround(go.GetComponent<CharacterController>());
            }

            go.SetActive(true);
        }

        #endregion

        #region Event Handlers

        private void PlayerEnterExit_OnTransitionExterior(PlayerEnterExit.TransitionEventArgs args)
        {
            // Any foes pending placement to dungeon or building interior are now invalid
            pendingFoeGameObjects = null;
            spawnInProgress = false;
        }

        private void StreamingWorld_OnInitWorld()
        {
            // Any foes pending placement to loose objects container are now invalid
            pendingFoeGameObjects = null;
            spawnInProgress = false;
        }

        #endregion

        #region Serialization

        [fsObject("v1")]
        public struct SaveData_v1
        {
            public Symbol foeSymbol;
            public ulong lastSpawnTime;
            public uint spawnInterval;
            public int spawnMaxTimes;
            public int spawnChance;
            public int spawnCounter;
            public bool isSendAction;
            public int msgMessageID;
        }

        public override object GetSaveData()
        {
            SaveData_v1 data = new SaveData_v1();
            data.foeSymbol = foeSymbol;
            data.lastSpawnTime = lastSpawnTime;
            data.spawnInterval = spawnInterval;
            data.spawnMaxTimes = spawnMaxTimes;
            data.spawnChance = spawnChance;
            data.spawnCounter = spawnCounter;
            data.isSendAction = isSendAction;
            data.msgMessageID = msgMessageID;

            return data;
        }

        public override void RestoreSaveData(object dataIn)
        {
            if (dataIn == null)
                return;

            SaveData_v1 data = (SaveData_v1)dataIn;
            foeSymbol = data.foeSymbol;
            lastSpawnTime = data.lastSpawnTime;
            spawnInterval = data.spawnInterval;
            spawnMaxTimes = data.spawnMaxTimes;
            spawnChance = data.spawnChance;
            spawnCounter = data.spawnCounter;
            isSendAction = data.isSendAction;
            msgMessageID = data.msgMessageID;

            // Set timer to current game time if not loaded from save
            if (lastSpawnTime == 0)
                lastSpawnTime = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToSeconds();
        }

        #endregion
    }
}