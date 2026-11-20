using Godot;
using System;
using System.Collections.Generic;

// On-screen controls during a game: one dice + name tag + Auto button per player and the
// Pause button. There is no status text outside the board; short messages appear as a
// toast over the middle of the board. It only reports events; GameManager decides what happens.
public partial class GameHud : CanvasLayer
{
    public event Action<string> DiceClicked;
    public event Action<string> RollFinished;
    public event Action<string, bool> AutoToggled;
    public event Action PausePressed;

    // Set before Build(). The small name labels next to each dice.
    public bool ShowTags = true;

    private readonly Dictionary<string, DiceButton> _dice = new();
    private readonly Dictionary<string, Label> _tags = new();
    private readonly Dictionary<string, Button> _autoButtons = new();

    private Vector2 _boardCenter;
    private Vector2 _boardSize;

    public DiceButton GetDice(string color) => _dice[color];
    public IEnumerable<DiceButton> AllDice => _dice.Values;

    // Call after adding the HUD to the tree
    public void Build(BoardManager board, GameConfig config, IReadOnlyList<string> activeColors,
                      float diceSize, float diceGap)
    {
        Vector2 boardTopLeft = board.ToGlobal(board.BoardTopLeft);
        float top = boardTopLeft.Y;
        float bottom = top + board.BoardHeight;
        float midX = boardTopLeft.X + board.BoardWidth / 2f;
        float midY = top + board.BoardHeight / 2f;

        _boardSize = new Vector2(board.BoardWidth, board.BoardHeight);
        _boardCenter = new Vector2(midX, midY);

        foreach (string color in activeColors)
        {
            Vector2 yardCenter = board.ToGlobal(board.GetYardCenter(color));
            bool onTop = yardCenter.Y < midY;
            bool onLeft = yardCenter.X < midX;

            var dice = new DiceButton
            {
                PlayerColor = color,
                Size = new Vector2(diceSize, diceSize),
                CustomMinimumSize = new Vector2(diceSize, diceSize)
            };
            AddChild(dice);

            float diceTop = onTop ? top - diceGap - diceSize : bottom + diceGap;
            dice.Position = new Vector2(yardCenter.X - diceSize / 2f, diceTop);

            string c = color;
            dice.Clicked += () => DiceClicked?.Invoke(c);
            dice.RollFinished += () => RollFinished?.Invoke(c);
            _dice[color] = dice;

            // Name tag on the outer side of the dice
            var tag = new Label
            {
                Size = new Vector2(360f, 34f),
                Position = new Vector2(
                    yardCenter.X - 180f,
                    onTop ? diceTop - 38f : diceTop + diceSize + 6f),
                HorizontalAlignment = HorizontalAlignment.Center,
                Visible = ShowTags
            };
            tag.AddThemeFontSizeOverride("font_size", 26);
            tag.AddThemeColorOverride("font_color", DiceButton.ColorFor(color));
            tag.AddThemeColorOverride("font_outline_color", Colors.Black);
            tag.AddThemeConstantOverride("outline_size", 6);
            AddChild(tag);
            _tags[color] = tag;

            // Auto button: lets a bot play this seat while the player is away
            if (config.Seats[color] == SeatType.Human)
            {
                const float buttonW = 170f;
                const float buttonH = 70f;

                float bx = onLeft
                    ? dice.Position.X + diceSize + 18f
                    : dice.Position.X - 18f - buttonW;

                var auto = new Button
                {
                    Text = "Auto: OFF",
                    ToggleMode = true,
                    FocusMode = Control.FocusModeEnum.None,
                    Size = new Vector2(buttonW, buttonH),
                    Position = new Vector2(bx, diceTop + (diceSize - buttonH) / 2f)
                };
                auto.AddThemeFontSizeOverride("font_size", 26);
                auto.Toggled += on => AutoToggled?.Invoke(c, on);
                AddChild(auto);
                _autoButtons[color] = auto;
            }
        }

        // Pause button, pinned to the top-right corner of the screen
        var pause = new Button
        {
            Text = "Pause",
            FocusMode = Control.FocusModeEnum.None
        };
        pause.AddThemeFontSizeOverride("font_size", 32);
        pause.Pressed += () => PausePressed?.Invoke();
        AddChild(pause);

        pause.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        pause.OffsetLeft = -184f;
        pause.OffsetRight = -24f;
        pause.OffsetTop = 24f;
        pause.OffsetBottom = 100f;
    }

    // Short message over the middle of the board that fades out by itself
    public void Toast(string text, string colorName)
    {
        var label = new Label
        {
            Text = text,
            Size = new Vector2(_boardSize.X, 110f),
            Position = _boardCenter - new Vector2(_boardSize.X / 2f, 55f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", 56);
        label.AddThemeColorOverride("font_color", DiceButton.ColorFor(colorName));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 14);
        AddChild(label);

        var tween = label.CreateTween();
        tween.TweenProperty(label, "modulate:a", 0f, 0.3f).SetDelay(0.8f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    public void SetTag(string color, string text)
    {
        _tags[color].Text = text;
    }

    public void SetAutoText(string color, bool on)
    {
        if (_autoButtons.TryGetValue(color, out Button button))
            button.Text = on ? "Auto: ON" : "Auto: OFF";
    }

    // A player who has finished leaves the rotation (their dice and Auto button disappear)
    public void HidePlayer(string color)
    {
        _dice[color].Visible = false;

        if (_autoButtons.TryGetValue(color, out Button button))
            button.Visible = false;
    }

    public void DisableAll()
    {
        foreach (DiceButton d in _dice.Values)
        {
            d.CanRoll = false;
            d.IsTurn = false;
        }

        foreach (Button b in _autoButtons.Values)
            b.Disabled = true;
    }
}
