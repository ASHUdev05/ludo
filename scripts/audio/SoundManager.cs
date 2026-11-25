using Godot;
using System.Collections.Generic;

// Plays the game's sound effects. Created by GameManager, no scene setup needed.
// Missing files are skipped quietly, so the game still runs without audio.
public partial class SoundManager : Node
{
    private const string Folder = "res://audio/";

    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _pool = new();

    // Per-effect volume trim in dB (the files were recorded at different levels)
    private readonly Dictionary<string, float> _volumes = new()
    {
        { "dice.mp3", -4f },
        { "tik.wav", -4f },
        { "capture.mp3", -3f },
        { "star.mp3", 6f }
    };

    public override void _Ready()
    {
        // Several players so overlapping sounds (fast tiks) don't cut each other off
        for (int i = 0; i < 8; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _pool.Add(player);
        }

        foreach (string file in _volumes.Keys)
        {
            string path = Folder + file;

            if (ResourceLoader.Exists(path))
                _streams[file] = GD.Load<AudioStream>(path);
            else
                GD.PushWarning($"SoundManager: missing {path}");
        }
    }

    public void PlayDice()    => Play("dice.mp3");
    public void PlayStep()    => Play("tik.wav", pitchVariation: 0.06f);
    public void PlayCapture() => Play("capture.mp3");
    public void PlayStar()    => Play("star.mp3");

    private void Play(string file, float pitchVariation = 0f)
    {
        if (!_streams.TryGetValue(file, out AudioStream stream)) return;

        // Reuse a free player, or steal the first one if all are busy
        AudioStreamPlayer player = _pool[0];
        foreach (AudioStreamPlayer p in _pool)
        {
            if (!p.Playing)
            {
                player = p;
                break;
            }
        }

        player.Stream = stream;
        player.VolumeDb = _volumes[file];
        player.PitchScale = 1f + (GD.Randf() * 2f - 1f) * pitchVariation;
        player.Play();
    }
}
