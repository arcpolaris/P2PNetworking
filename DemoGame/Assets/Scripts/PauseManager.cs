using DemoGame.Util;
using IngameDebugConsole;
using UnityEngine;
using UnityEngine.Events;

public class PauseManager : Singleton<PauseManager>
{
    private bool _paused = false;
    public bool IsPaused
    {
        get => _paused;
        set {
			if (_paused == value) return;

            _paused = value;
            if (IsPaused)
            {
                OnPause.Invoke();
            }
            else OnResume.Invoke();

            OnPauseChanged.Invoke(value);
        }
    }

    [field: SerializeField] public UnityEvent OnPause { get; private set; } = new();
	[field: SerializeField] public UnityEvent OnResume { get; private set; } = new();
	[field: SerializeField] public UnityEvent<bool> OnPauseChanged { get; private set; } = new();

	[ConsoleMethod("unpause", "Resume the game")]
	public static void UnPause() => Instance.IsPaused = false;

	protected override void OnInitialize()
	{
		OnPause.AddListener(DebugLogManager.Instance.ShowLogWindow);
		OnResume.AddListener(DebugLogManager.Instance.HideLogWindow);
		OnPauseChanged.AddListener(static paused => FixCursor = !paused);

		DebugLogManager.Instance.OnLogWindowHidden = static () => Instance.IsPaused = false;
	}

	private static bool FixCursor
	{
		set
		{
			Cursor.visible = !value;
			Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
		}
	}

	private void Start()
	{
		IsPaused = true;
	}

	public void Update()
	{
		if (Input.GetButtonDown("Cancel"))
		{
			IsPaused = true;
		}
	}

	protected override bool DontDestoryOnLoad => false;
}
