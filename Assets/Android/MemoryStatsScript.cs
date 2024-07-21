using System.Text;
using Unity.Profiling;
using UnityEngine;

public class MemoryStatsScript : MonoBehaviour
{
    string statsText;
    ProfilerRecorder totalReservedMemoryRecorder;
    ProfilerRecorder gcReservedMemoryRecorder;
    ProfilerRecorder systemUsedMemoryRecorder;
    ProfilerRecorder gfxReservedMemoryRecorder;
    ProfilerRecorder audioReservedMemoryRecorder;
    ProfilerRecorder videoReservedMemoryRecorder;
    ProfilerRecorder profilerReservedMemoryRecorder;

    GUIStyle guiStyle = new GUIStyle();
    GUIContent boxContent = new GUIContent();

    void Awake()
    {
        guiStyle.fontSize = 14; // Adjust font size as needed
        guiStyle.normal.textColor = Color.white;
        guiStyle.padding = new RectOffset(10, 10, 10, 10);
        //guiStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.5f)); // Semi-transparent black
        boxContent.image = (Texture)MakeTex(2, 2, new Color(0f, 0f, 0f, 0.7f));
    }
    void OnEnable()
    {
        totalReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory");
        gcReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
        systemUsedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        gfxReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Reserved Memory");
        audioReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Audio Reserved Memory");
        videoReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Video Reserved Memory");
        profilerReservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Profiler Reserved Memory");
    }

    void OnDisable()
    {
        totalReservedMemoryRecorder.Dispose();
        gcReservedMemoryRecorder.Dispose();
        systemUsedMemoryRecorder.Dispose();
        gfxReservedMemoryRecorder.Dispose();
        audioReservedMemoryRecorder.Dispose();
        videoReservedMemoryRecorder.Dispose();
        profilerReservedMemoryRecorder.Dispose();
    }

    void Update()
    {
        var sb = new StringBuilder(500);
        if (totalReservedMemoryRecorder.Valid)
            sb.AppendLine($"Total Reserved Memory: {totalReservedMemoryRecorder.LastValue/1000000} mb");
        if (gcReservedMemoryRecorder.Valid)
            sb.AppendLine($"GC Reserved Memory: {gcReservedMemoryRecorder.LastValue / 1000000} mb");
        if (systemUsedMemoryRecorder.Valid)
            sb.AppendLine($"System Used Memory: {systemUsedMemoryRecorder.LastValue / 1000000} mb");
        if (gfxReservedMemoryRecorder.Valid)
            sb.AppendLine($"Graphics Reserved Memory: {gfxReservedMemoryRecorder.LastValue / 1000000} mb");
        if (audioReservedMemoryRecorder.Valid)
            sb.AppendLine($"Audio Reserved Memory: {audioReservedMemoryRecorder.LastValue / 1000000} mb");
        if (videoReservedMemoryRecorder.Valid)
            sb.AppendLine($"Video Reserved Memory: {videoReservedMemoryRecorder.LastValue / 1000000} mb");
        if (profilerReservedMemoryRecorder.Valid)
            sb.AppendLine($"Profiler Reserved Memory: {profilerReservedMemoryRecorder.LastValue / 1000000} mb");
        statsText = sb.ToString();
    }
    int optimalFontSize = -1;
    void OnGUI()
    {
        GUI.depth = -1;
        float width = Screen.currentResolution.width / 4;
        float height = width * 0.5f;
        Rect boxRect = new Rect(Screen.currentResolution.width / 2 - width / 2, 30, width, height);
        Rect textRect = new Rect(Screen.currentResolution.width / 2 - width / 2, 30, width, height);
        GUI.Box(boxRect, boxContent); // Adjust size as needed
        if(optimalFontSize < 0)
            optimalFontSize = CalculateOptimalFontSize(statsText, textRect, guiStyle, boxContent);
        guiStyle.fontSize = optimalFontSize;
        GUI.Label(textRect, statsText, guiStyle);
    }
    // Utility function to create a texture
    Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i)
        {
            pix[i] = col;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }
    int CalculateOptimalFontSize(string text, Rect rect, GUIStyle style, GUIContent content)
    {
        int minFontSize = 2;
        int maxFontSize = 200;
        int optimalFontSize = maxFontSize;

        for (int i = minFontSize; i <= maxFontSize; i++)
        {
            style.fontSize = i;
            if (style.CalcHeight(content, rect.width) > rect.height/6)
            {
                optimalFontSize = i;
                Debug.Log("Optimal font size: " + optimalFontSize);
                break;
            }
        }

        return optimalFontSize;
    }
}