// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Pango (petchema@concept-micro.com)
// Contributors:    
// 
// Notes:
//

using DaggerfallWorkshop.Game;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace DaggerfallWorkshop
{
    public static class DaggerfallGC
    {
        // Min time between two unused assets collections
        // (Unless unused assets collection was 'forced')
        private const float uuaThrottleDelay = 300f;

        private static float uuaTimer = Time.realtimeSinceStartup;
        private static AudioMixerGroup _defaultAudioMixerGroup;

        private static bool isUnloadingAssets;

        public static void ThrottledUnloadUnusedAssets(bool forceUnload = false, bool pruneCache = true, float overridenPruneCacheThreshold = 0)
        {
            Debug.Log("ThrottleUnloadUnusedAssets");
            if (pruneCache)
                DaggerfallUnity.Instance.PruneCache(overridenPruneCacheThreshold);
            if (forceUnload || Time.realtimeSinceStartup >= uuaTimer)
                ForcedUnloadUnusedAssets();
        }

        private static void ForcedUnloadUnusedAssets()
        {
            if(!isUnloadingAssets)
                CoroutineManager.Instance.StartCoroutine(ForcedUnloadUnusedAssets_Coroutine());
        }

        /// <summary>
        /// Unloads unused assets from Resources
        /// </summary>
        /// <remarks>
        /// The game's sound is muted during this unload, so that the 'repeated sfx bug' doesn't occur
        /// </remarks>
        private static IEnumerator ForcedUnloadUnusedAssets_Coroutine()
        {
            isUnloadingAssets = true;
            uuaTimer = Time.realtimeSinceStartup + uuaThrottleDelay;
            if (!_defaultAudioMixerGroup)
                _defaultAudioMixerGroup = Resources.Load<AudioMixer>("MainMixer").FindMatchingGroups("Master")[0];
            _defaultAudioMixerGroup.audioMixer.SetFloat("volume", -80);
            Debug.Log("ThrottleUnloadUnusedAssets: muted volume");
            for(int i =0; i < 6; ++i)
                yield return new WaitForEndOfFrame();
            Debug.Log("ThrottleUnloadUnusedAssets: UnloadUnusedAssets");
            yield return Resources.UnloadUnusedAssets();
            for (int i = 0; i < 10; ++i)
                yield return new WaitForEndOfFrame();
            _defaultAudioMixerGroup.audioMixer.SetFloat("volume", 0);
            Debug.Log("ThrottleUnloadUnusedAssets: unmuted volume");
            isUnloadingAssets = false;
        }
    }
}
