using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

namespace Eresoth.EditorTools
{
    /// <summary>一键生成演示场景：菜单 工具 → 生成RTS演示场景。</summary>
    public static class RTSSceneSetup
    {
        [MenuItem("工具/生成RTS演示场景")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GAME");
            go.AddComponent<Game>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            Selection.activeGameObject = go;
            EditorUtility.DisplayDialog("完成", "场景已生成：Assets/Scenes/Main.unity\n直接点击 Play 开始游戏！", "好");
        }
    }
}
