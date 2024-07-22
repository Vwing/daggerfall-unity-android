
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DaggerfallWorkshop.Game
{
    public static class UnityUIUtils
    {
        // gotten from sildeflask in https://forum.unity.com/threads/how-to-get-a-rect-in-screen-space-from-a-recttransform.1490806/
        public static Rect GetScreenspaceRect(RectTransform rtf, Camera cam)
        {
            // Get the corners of the RectTransform in world space
            Vector3[] corners = new Vector3[4];
            rtf.GetWorldCorners(corners);

            // Convert world space to screen space in pixel values and round to integers
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = cam.WorldToScreenPoint(corners[i]);
                corners[i] = new Vector3(Mathf.RoundToInt(corners[i].x), Mathf.RoundToInt(corners[i].y), corners[i].z);
            }

            // Calculate the screen space rectangle
            float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float width = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x) - minX;
            float height = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y) - minY;

            // Display the screen space rectangle
            Rect screenRect = new Rect(minX, minY, width, height);

            return screenRect;
        }
        public static void MatchRectTFToScreenspaceRect(RectTransform rtf, Rect rect, Camera cam)
        {
            if (!rtf.parent || rtf.parent is not RectTransform)
                return;

            Vector2 localPoint, rectMin, rectMax;
            // Set the position of the RectTransform
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.position, cam, out localPoint))
            {
                rtf.anchoredPosition = localPoint;
            }
            // Set the sizeDelta of the RectTransform
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.min, cam, out rectMin)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.max, cam, out rectMax))
            {
                rtf.sizeDelta = rectMax - rectMin;
            }
        }
        public static bool Approximately(this Vector2 v1, Vector2 v2)
        {
            return Mathf.Approximately(v1.x, v2.x) && Mathf.Approximately(v1.y, v2.y);
        }
    }
}
