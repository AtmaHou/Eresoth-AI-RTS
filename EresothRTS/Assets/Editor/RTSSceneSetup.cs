using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using System.IO;
using System.Linq;

namespace Eresoth.EditorTools
{
    /// <summary>一键生成演示场景：菜单 工具 → 生成RTS演示场景。</summary>
    public static class RTSSceneSetup
    {
        const string IntroScenePath = "Assets/Scenes/Intro.unity";
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string IntroVideoPath = "Assets/Videos/暗黑中古魔幻RTS游戏开场CG.mp4";

        [MenuItem("工具/生成RTS演示场景")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GAME");
            go.AddComponent<Game>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            SetBuildScenes();
            Selection.activeGameObject = go;
            EditorUtility.DisplayDialog("完成", "场景已生成：Assets/Scenes/Main.unity\n直接点击 Play 开始游戏！", "好");
        }

        /// <summary>一键生成开场动画场景：菜单 工具 → 生成开场动画场景。</summary>
        [MenuItem("工具/生成开场动画场景")]
        public static void CreateIntro()
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(IntroVideoPath);
            if (clip == null)
            {
                Debug.LogError("[RTSSceneSetup] 找不到开场视频: " + IntroVideoPath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("IntroCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();

            var audio = camGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;

            var vp = camGo.AddComponent<VideoPlayer>();
            vp.renderMode = VideoRenderMode.CameraFarPlane;
            vp.targetCamera = cam;
            vp.clip = clip;
            vp.playOnAwake = true;
            vp.isLooping = false;
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.SetTargetAudioSource(0, audio);

            var intro = camGo.AddComponent<IntroPlayer>();
            intro.nextScene = "Main";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, IntroScenePath);
            SetBuildScenes();
            Selection.activeGameObject = camGo;
            Debug.Log("[RTSSceneSetup] 开场动画场景已生成: " + IntroScenePath);
        }

        /// <summary>构建设置中注册场景：Intro 在 index 0，Main 在其后（已存在的 Main 场景 guid 保持不变）。</summary>
        static void SetBuildScenes()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(IntroScenePath, true)
            };
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path != IntroScenePath) scenes.Add(s);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
