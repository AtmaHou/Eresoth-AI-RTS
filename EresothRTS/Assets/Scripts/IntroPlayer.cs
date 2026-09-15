using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>开场动画：全屏播放开场CG，按回车或点击跳过，播完自动进入 Main 场景。</summary>
[RequireComponent(typeof(VideoPlayer))]
public class IntroPlayer : MonoBehaviour
{
    public string nextScene = "Main";

    VideoPlayer player;
    bool skipping;

    void Awake()
    {
        player = GetComponent<VideoPlayer>();
        player.loopPointReached += OnVideoFinished;
        player.errorReceived += OnVideoError;
    }

    void Update()
    {
        if (skipping) return;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0))
            Skip();
    }

    void OnVideoFinished(VideoPlayer vp) => Skip();

    void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogWarning("[IntroPlayer] 视频播放出错，直接进入游戏: " + message);
        Skip();
    }

    void Skip()
    {
        if (skipping) return;
        skipping = true;
        player.loopPointReached -= OnVideoFinished;
        player.errorReceived -= OnVideoError;
        player.Stop();
        SceneManager.LoadScene(nextScene);
    }

    void OnGUI()
    {
        if (skipping) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleRight
        };
        style.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(Screen.width - 280, Screen.height - 50, 260, 30), "按 回车 / 点击 跳过", style);
    }
}
