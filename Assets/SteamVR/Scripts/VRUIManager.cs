using DaggerfallWorkshop.Game;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/**
 * This component disables the default UI and replaces it with VR logic
 **/
public class VRUIManager : MonoBehaviour
{
	void Start ()
    {

        /*GameObject quad = GameObject.FindGameObjectWithTag("UI");
        RawImage rawImage = DaggerfallUI.Instance.NonDiegeticUIOutput.GetComponent<RawImage>();
        if (quad && rawImage)
        {
            Debug.Log("Found UI Quad, Mesh Renderer, and raw image!");
            quad.GetComponent<RawImage>().texture = rawImage.texture;
        }
        else
        {
            Debug.Log("Didn't find UI Quad, Mesh Renderer, or raw image.");
        }
        */
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}
