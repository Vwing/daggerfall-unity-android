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

using System;
using UnityEngine;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Entity;

namespace DaggerfallWorkshop.Game.Utility
{
    /// <summary>
    /// Spawn one or more enemies near player.
    /// Will attempt to start placing spawns after game objects are set and spawn count greater than 0.
    /// This is a generic spawn helper not tied to any specific system.
    /// NOTES:
    ///  * Spawns foes immediately. Be careful spawning multiple foes as they are likely to become stuck on each other.
    ///  * The spawner will self-destroy once all foes spawned. Do not attach to anything you want to remain in scene.
    ///  * There is a prefab carrying this component in Prefabs/Scene for easy spawner setups.
    ///  * Will attempt to find best parent at time if none specified (e.g. dungeon, interior).
    ///  * Might need to reduce MinDistance if expecting to spawn in tigt confines like small interiors.
    /// </summary>
    public class FoeSpawner : MonoBehaviour
    {
        // Set these values at create time to setup a foe spawn automatically on start
        public MobileTypes FoeType = MobileTypes.None;
        public int SpawnCount = 0;
        public float MinDistance = 4f;
        public float MaxDistance = 20f;
        public Transform Parent = null;
        public bool LineOfSightCheck = true;
        public bool AlliedToPlayer = false;

        public MobileTypes lastFoeType = MobileTypes.None;
        GameObject[] pendingFoeGameObjects;
        int pendingFoesSpawned = 0;
        bool spawnInProgress = false;

        void Awake()
        {
            // Register as Foe Spawner object
            ActiveGameObjectDatabase.RegisterFoeSpawner(gameObject);
        }

        void Update()
        {
            // Create new foe list when changed in editor
            if (FoeType != MobileTypes.None && FoeType != lastFoeType && SpawnCount > 0)
            {
                DestroyOldFoeGameObjects(pendingFoeGameObjects);
                SetFoeGameObjects(GameObjectHelper.CreateFoeGameObjects(Vector3.zero, FoeType, SpawnCount, alliedToPlayer: AlliedToPlayer));
                lastFoeType = FoeType;
            }

            // Do nothing if no spawns or we are done spawning
            if (pendingFoeGameObjects == null || !spawnInProgress)
                return;

            // Clear pending foes if all have been spawned
            if (spawnInProgress && pendingFoesSpawned >= pendingFoeGameObjects.Length)
            {
                spawnInProgress = false;
                Destroy(gameObject);
                return;
            }

            // Try placing foes near player
            PlaceFoeFreely(pendingFoeGameObjects, MinDistance, MaxDistance);
            if (spawnInProgress)
                GameManager.Instance.RaiseOnEncounterEvent();
        }

        #region Public Methods

        /// <summary>
        /// Assign an array of pending foe GameObjects to spawn.
        /// The spawner will then try to place these foes around player until none remain.
        /// Use GameObjectHelper.CreateFoeGameObjects() static method to create foe GameObjects first.
        /// </summary>
        public void SetFoeGameObjects(GameObject[] gameObjects, Transform parent = null)
        {
            // Do nothing if array not valid
            if (gameObjects == null || gameObjects.Length == 0)
            {
                spawnInProgress = false;
                return;
            }

            // Store array and start spawning
            pendingFoeGameObjects = gameObjects;
            pendingFoesSpawned = 0;
            spawnInProgress = true;
            
            // Set parent if specified
            if (parent)
            {
                Parent = parent;
                for (int i = 0; i < pendingFoeGameObjects.Length; i++)
                {
                    pendingFoeGameObjects[i].transform.parent = parent;
                }
            }
        }

        #endregion

        #region Private Methods

        // Uses raycasts to find next spawn position just outside of player's field of view
        bool PlaceFoeFreely(GameObject[] gameObjects, float minDistance = 5f, float maxDistance = 20f, bool placeEnemyBehindPlayer = true, int spawnTries = 15)
        {
            const float separationDistance = 1.25f;
            const float maxFloorDistance = 4f;
            const float overlapSphereRadius = 0.65f;

            // override min/max distance in small interior spaces.
            var dfInterior = FindObjectOfType<DaggerfallInterior>();
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
            var enemyCC = gameObjects[pendingFoesSpawned].GetComponent<CharacterController>();
            var playerCC = GameManager.Instance.PlayerController;
            // Set parent if none specified already
            if (!gameObjects[pendingFoesSpawned].transform.parent)
                gameObjects[pendingFoesSpawned].transform.parent = GameObjectHelper.GetBestParent();
            
            for(int i =0; i < spawnTries; ++i){
                float fov = GameManager.Instance.MainCamera.fieldOfView;
                float randomAngle;
                if(!LineOfSightCheck || i > spawnTries/3) // give up on line of sight checks after we've tried a few times
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

                Debug.Log($"FoeSpawner: Found an enemy spawn point in {i} tries");
                // This looks like a good spawn position
                pendingFoeGameObjects[pendingFoesSpawned].transform.position = spawnPos;
                FinalizeFoe(pendingFoeGameObjects[pendingFoesSpawned]);
                gameObjects[pendingFoesSpawned].transform.LookAt(GameManager.Instance.PlayerObject.transform.position);

                // Increment count
                pendingFoesSpawned++;
                return true;
            }

            // Couldn't find a spawn point
            Debug.Log($"FoeSPawner: Couldn't find a valid enemy spawn point after {spawnTries} tries.");
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

        // Destroy other foes if replaced during spawn
        void DestroyOldFoeGameObjects(GameObject[] gameObjects)
        {
            if (gameObjects == null || gameObjects.Length == 0)
                return;

            foreach(GameObject go in gameObjects)
            {
                Destroy(go);
            }
        }

        #endregion
    }
}