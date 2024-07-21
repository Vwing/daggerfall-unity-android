using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace DaggerfallWorkshop.Game
{
    public static class DFULayerMasks
    {
        public static readonly string[] CorporealIgnoredLayers = new string[] { "NoclipLayer", "Automap", "UI", "Ignore Raycast", "SpellMissiles" };
        public static readonly string[] CorporealWithPlayerExcludedIgnoredLayers = new string[] { "NoclipLayer", "Automap", "UI", "Ignore Raycast", "SpellMissiles", "Player"};
        public static LayerMask CorporealMask
        {
            get { return ~LayerMask.GetMask(CorporealIgnoredLayers); }
        }

        public static LayerMask CorporealMaskWithPlayerExcluded
        {
            get { return ~LayerMask.GetMask(CorporealWithPlayerExcludedIgnoredLayers); }
        }
    }

}
