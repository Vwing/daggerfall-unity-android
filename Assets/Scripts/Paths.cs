// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: marcoscampi
// Contributors: Vincent Wing (vwing@uci.edu)
// 
// Notes:
//

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using ICSharpCode.SharpZipLib.Zip;
using DaggerfallWorkshop.Game;
namespace DaggerfallWorkshop
{
    public sealed class Paths
    {
        public static Paths Instance => lazy.Value;
        public static string StreamingAssetsPath => lazy.Value.streamingAssetsPath;
        public static string DataPath => lazy.Value.dataPath;
        public static string PersistentDataPath => lazy.Value.persistentDataPath;
        public static string StoragePath => lazy.Value.storagePath;
        public static string LayoutsPath => TouchscreenLayoutsManager.LayoutsPath;

        private string streamingAssetsPath;
        private string dataPath;
        private string persistentDataPath;
        private string storagePath;

        private static readonly Lazy<Paths> lazy = new Lazy<Paths>(() => new Paths());

        private Paths()
        {
            Console.WriteLine(Application.systemLanguage);
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    this.initAndroid();
                    break;
                default:
                    this.init();
                    break;
            }
        }
        private void init()
        {
            storagePath = "/";
            streamingAssetsPath = Application.streamingAssetsPath;
            dataPath = Application.dataPath;
            persistentDataPath = Application.persistentDataPath;

        }
        /**
        * Application.dataPath refers to where the APK is located
        * Application.streamingAssets refers to assets inside Application.dataPath
        * Application.persistentDataPath refers to some private storage, so must be overriden
        */
        private void initAndroid()
        {
            var apkPath = Application.dataPath;
            
            storagePath = Application.persistentDataPath;
            dataPath = Path.Combine(Application.persistentDataPath, "DaggerfallUnity");
            persistentDataPath = dataPath;
            streamingAssetsPath = Path.Combine(dataPath, "assets");

            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
                // Extract apk
                {
                    FastZip fastZip = new FastZip();
                    fastZip.ExtractZip(apkPath, dataPath, @".*;-^(?!assets);-^\/assets\/bin");
                }
            }
        }
    }
}