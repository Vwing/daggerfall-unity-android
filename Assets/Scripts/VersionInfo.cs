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

public static class VersionInfo
{
    public const string DaggerfallUnityProductName = "Daggerfall Unity";
    public const string DaggerfallUnityStatus = "Release";

    // Last updated versions 8-May-2024
    public const string DaggerfallUnityVersion = "1.1.1";
    public const string DaggerfallToolsForUnityVersion = "1.9.2";

    public const string DaggerfallUnityForAndroidVersion = "1.1.1.6";

    public static string DaggerfallUnityVersionPlatformIndependent => Application.isMobilePlatform ? DaggerfallUnityForAndroidVersion : DaggerfallUnityVersion;

    public const string BaselineUnityVersion = "2022.3.30f1";
}