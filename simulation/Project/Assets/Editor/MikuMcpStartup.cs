using System;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor;

// Project-local: does not change MCP transport preferences for other projects.
[InitializeOnLoad]
public static class MikuMcpStartup
{
    static MikuMcpStartup()
    {
        EditorApplication.delayCall += Start;
    }

    static void Start()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Start;
            return;
        }
        try
        {
            McpCiBoot.StartStdioForCi();
            Debug.Log("Miku lab: MCP stdio auto-start enabled for this editor session.");
        }
        catch (Exception e) { Debug.LogException(e); }
    }
}
