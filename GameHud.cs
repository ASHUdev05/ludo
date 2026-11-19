using Godot;
using System;
using System.Collections.Generic;

// All on-screen controls during a game: one dice + name tag + Auto button per player,
// the status line and the Pause button. It only reports events; GameManager decides what happens.
public partial class GameHud : CanvasLayer
{
    public event Action<string> DiceClicked;
    public event Action<string> RollFinished;
    public event Action<string, bool> AutoToggled;
    public event Action PausePressed;

    private readonly Dictionary<string, DiceButton> _dice = new();
    private readonly Dictionary<string, Label> _tags = new();
    private readonly Dictionary<string, Button> _autoButtons = new();
    private Label _status;

    public DiceButton GetDice(string color) => _dice[color];
    public IEnumerable<DiceButton> AllDice => _dice.Values;

    // Call after adding the HUD to the tree
    public void Build(BoardManager board, GameConfig config, IReadOnlyList<string> activeColors,
                      float diceSize, float diceGap)
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        Vector2 boardTopLeft = board.ToGlobal(board.BoardTopLeft);
        float top = boardTopLeft.Y;
        float bottom = top + board.BoardHeight;
        float midX = boardTopLeft.X + board.BoardWidth / 2f;
        float midY = top + board.BoardHeight / 2f;

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
                HorizontalAlignment = HorizontalAlignment.Center
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

        // Status line above the top dice
        _status = new Label
        {
            Position = new Vector2(0, Mathf.Max(10f, top - diceGap - diceSize - 160f)),
            Size = new Vector2(view.X, 70f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _status.AddThemeFontSizeOverride("font_size", 36);
        AddChild(_status);

        // Pause button, top right
        var pause = new Button
        {
            Text = "Pause",
            FocusMode = Control.FocusModeEnum.None,
            Size = new Vector2(160f, 76f),
            Position = new Vector2(view.X - 160f - 24f, 24f)
        };
        pause.AddThemeFontSizeOverride("font_size", 32);
        pause.Pressed += () => PausePressed?.Invoke();
        AddChild(pause);
    }

    public void SetStatus(string text, string colorName)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", DiceButton.ColorFor(colorName));
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
