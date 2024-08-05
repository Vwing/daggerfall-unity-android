using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[ExecuteAlways]
public class SliderTextDriver : MonoBehaviour
{
    public Slider slider;
    public TMP_Text text;

    private void Update()
    {
        if(slider && text && text.text != slider.value.ToString()){ // only update if text has changed, since UI operations can be heavy.
            text.text = slider.value.ToString();
        }
    }
}
