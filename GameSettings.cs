using Godot;

// Player settings, saved to user://settings.cfg and applied to the engine.
public static class GameSettings
{
    private const string FilePath = "user://settings.cfg";

    public static bool SoundOn = true;
    public static float Volume = 1.0f;   // 0..1
    public static bool Vibration = true;
    public static int SpeedIndex = 0;    // 0 = normal, 1 = fast, 2 = faster

    public static readonly string[] SpeedNames = { "Normal", "Fast", "Faster" };
    private static readonly float[] Speeds = { 1.0f, 1.4f, 1.8f };

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok) return;

        SoundOn = cfg.GetValue("audio", "sound_on", true).AsBool();
        Volume = Mathf.Clamp(cfg.GetValue("audio", "volume", 1.0).AsSingle(), 0f, 1f);
        Vibration = cfg.GetValue("game", "vibration", true).AsBool();
        SpeedIndex = Mathf.Clamp(cfg.GetValue("game", "speed", 0).AsInt32(), 0, Speeds.Length - 1);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "sound_on", SoundOn);
        cfg.SetValue("audio", "volume", Volume);
        cfg.SetValue("game", "vibration", Vibration);
        cfg.SetValue("game", "speed", SpeedIndex);
        cfg.Save(FilePath);
    }

    // Pushes the settings into the engine. Game speed scales animations and timers.
    public static void Apply()
    {
        AudioServer.SetBusMute(0, !SoundOn);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(Volume, 0.0001f)));
        Engine.TimeScale = Speeds[Mathf.Clamp(SpeedIndex, 0, Speeds.Length - 1)];
    }

    // Short buzz on phones (needs the VIBRATE permission in the Android export preset)
    public static void Vibrate(int milliseconds = 40)
    {
        if (Vibration)
            Input.VibrateHandheld(milliseconds);
    }
}
