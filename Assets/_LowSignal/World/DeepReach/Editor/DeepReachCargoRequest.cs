using System;
using System.IO;
using UnityEditor;

// Allows the open editor to process an explicit authoring request without a second instance.
[InitializeOnLoad]
public static class DeepReachCargoRequest
{
    const string Request="Library/DeepReachCargo.request";
    static DeepReachCargoRequest(){EditorApplication.update+=Tick;}
    static void Tick(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        File.Delete(Request);
        try {DeepReachCaveDarkness.Apply();File.WriteAllText("Library/DeepReachCargo.result","CAVE_DARKNESS_PASS");}
        catch(Exception e){UnityEngine.Debug.LogException(e);File.WriteAllText("Library/DeepReachCargo.result",e.ToString());}
    }
}
