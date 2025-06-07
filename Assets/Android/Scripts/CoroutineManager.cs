using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace DaggerfallWorkshop.Game
{
    public class CoroutineManager : MonoBehaviour
    {
        public static CoroutineManager Instance { get; private set; }
        private void Awake()
        {
            if (Instance){
                Destroy(gameObject);
                return;
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(this);
            }
        }
        public void DoOnDelay(Action action, float delay)
        {
            Debug.Log("CoroutineManager: Starting DoOnDelay with delay " + delay);
            StartCoroutine(DoOnDelayCoroutine(action, delay));
        }

        public IEnumerator DoOnDelayCoroutine(Action action, float delay)
        {
            Debug.Log("CoroutineManager: Coroutine started, waiting...");
            yield return new WaitForSecondsRealtime(delay);
            Debug.Log("CoroutineManager: Delay finished, invoking action");
            action?.Invoke();
        }
    }
}