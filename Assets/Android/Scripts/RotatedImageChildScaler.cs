using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(RectTransform))]
public class RotatedImageChildScaler : MonoBehaviour
{
    void Update()
    {
        switch (Mathf.RoundToInt(transform.rotation.eulerAngles.z))
        {
            case 180:
            case 0:
                ((RectTransform)transform).sizeDelta = new Vector2(((RectTransform)transform.parent).sizeDelta.x, ((RectTransform)transform.parent).sizeDelta.y);
                break;
            case 90:
            case -90:
            case 270:
                ((RectTransform)transform).sizeDelta = new Vector2(((RectTransform)transform.parent).sizeDelta.y, ((RectTransform)transform.parent).sizeDelta.x);
                break;
            //case 180:
            //    ((RectTransform)transform).sizeDelta = new Vector2(((RectTransform)transform.parent).sizeDelta.x, ((RectTransform)transform.parent).sizeDelta.y);
            //    break;
        }
    }
}
