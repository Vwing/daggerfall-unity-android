using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop.Game
{
    public class CoroutineManager : MonoBehaviour
    {
        public static CoroutineManager Instance { get; private set; }
        private void Awake()
        {
            if (Instance)
                Destroy(gameObject);
            else
            {
                Instance = this;
                DontDestroyOnLoad(this);
            }
        }
        private void OnDisable()
        {
            Debug.Log("CoroutineManager was disabled!");
        }
    }

}