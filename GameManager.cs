using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameManager : Node
{
    private enum State { Setup, WaitingForRoll, Rolling, WaitingForMove, Busy, GameOver }

    // Optional: assign in the inspector. If empty, it is found automatically.
    [Export] public BoardManager Board;

    [Export] public float DiceSize = 180f;
    [Export] public float DiceGap = 28f;

    // Capturing an opponent's token gives the capturer another turn
    [Export] public bool ExtraTurnOnCapture = true;

    // Bringing a token all the way home gives another turn
    [Export] public bool ExtraTurnOnHome = true;

    // Three sixes in a row: the third is void, the turn is lost, and the moves
    // made with the first two sixes are undone
    [Export] public bool ThreeSixesLoseTurn = true;

    // Chance of a 6 relative to each other face (1.0 = fair die, 2.0 = about 29% sixes)
    [Export] public float SixWeight = 2.0f;

    // Bot pacing (seconds)
    [Export] public float BotRollDelay = 0.45f;
    [Export] public float BotMoveDelay = 0.40f;

    // Time a token takes to hop one cell (shorter = snappier moves)
    [Export] public float HopDuration = 0.10f;

    private const int HomeSteps = 56; // 0..50 track, 51..55 lane, 56 = finished

    private GameConfig _config;
    private readonly List<string> _activeOrder = new();

    private readonly Dictionary<string, DiceButton> _dice = new();
    private readonly Dictionary<string, Label> _tags = new();
    private readonly Dictionary<string, Button> _autoButtons = new();
    private readonly HashSet<string> _auto = new(); // human seats currently played by the bot
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Token> _movable = new();

    // Players who have brought all four tokens home: color -> rank (1 = first)
    private readonly Dictionary<string, int> _ranks = new();

    // Token positions before the first six of the current streak (for revoking)
    private Dictionary<Token, int> _sixSnapshot;

    private int _currentIndex = 0;
    private int _diceValue = 0;
    private int _sixStreak = 0; // consecutive sixes rolled by the current player
    private State _state = State.Setup;

    private Label _statusLabel;
    private SoundManager _sound;

    // Sparkle assets are built once and reused (creating them per burst causes hitches)
    private GradientTexture2D _glowTexture;
    private Gradient _sparkFade;

    private string CurrentColor => _activeOrder[_currentIndex];

    private bool IsBot(string color) =>
        _config.Seats[color] == SeatType.Bot || _auto.Contains(color);

    public override void _Ready()
    {
        _rng.Randomize();

        _sound = new SoundManager();
        AddChild(_sound);

        // Wait one frame so BoardManager has finished spawning tokens
        CallDeferred(nameof(Init));
    }

    private void Init()
    {
        Board ??= FindBoard(GetTree().CurrentScene);

        if (Board == null)
        {
            GD.PushError("GameManager: BoardManager not found. Assign it in the inspector.");
            return;
        }

        var screen = new StartScreen { Config = GameConfig.Last?.Clone() ?? new GameConfig() };
        AddChild(screen);

        screen.Started += config =>
        {
            screen.QueueFree();
            StartGame(config);
        };
    }

    private static BoardManager FindBoard(Node node)
    {
        if (node == null) return null;
        if (node is BoardManager bm) return bm;

        foreach (Node child in node.GetChildren())
        {
            BoardManager found = FindBoard(child);
            if (found != null) return found;
        }
        return null;
    }

    // =====================================
    // GAME SETUP
    // =====================================

    private void StartGame(GameConfig config)
    {
        _config = config;
        GameConfig.Last = config.Clone();

        _activeOrder.Clear();
        _activeOrder.AddRange(config.ActiveColors);

        // Remove tokens (and grey out the yard) of seats that are not playing
        foreach (string color in GameConfig.Order)
            if (config.Seats[color] == SeatType.Off)
                Board.RemoveColor(color);

        BuildUi();

        foreach (Token token in Board.AllTokens)
            token.OnTokenClicked += OnTokenClicked;

        _ranks.Clear();
        _currentIndex = 0;
        StartTurn();
    }

    // =====================================
    // UI: dice, name tag and Auto button per player, status label
    // =====================================

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        Vector2 view = GetViewport().GetVisibleRect().Size;
        Vector2 boardTopLeft = Board.ToGlobal(Board.BoardTopLeft);
        float top = boardTopLeft.Y;
        float bottom = top + Board.BoardHeight;
        float midX = boardTopLeft.X + Board.BoardWidth / 2f;
        float midY = top + Board.BoardHeight / 2f;

        foreach (string color in _activeOrder)
        {
            Vector2 yardCenter = Board.ToGlobal(Board.GetYardCenter(color));
            bool onTop = yardCenter.Y < midY;
            bool onLeft = yardCenter.X < midX;

            var dice = new DiceButton
            {
                PlayerColor = color,
                Size = new Vector2(DiceSize, DiceSize),
                CustomMinimumSize = new Vector2(DiceSize, DiceSize)
            };
            layer.AddChild(dice);

            float diceTop = onTop ? top - DiceGap - DiceSize : bottom + DiceGap;
            dice.Position = new Vector2(yardCenter.X - DiceSize / 2f, diceTop);

            string c = color;
            dice.Clicked += () => OnDiceClicked(c);
            dice.RollFinished += () => OnRollFinished(c);
            _dice[color] = dice;

            // Name tag on the outer side of the dice
            var tag = new Label
            {
                Size = new Vector2(360f, 34f),
                Position = new Vector2(
                    yardCenter.X - 180f,
                    onTop ? diceTop - 38f : diceTop + DiceSize + 6f),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tag.AddThemeFontSizeOverride("font_size", 26);
            tag.AddThemeColorOverride("font_color", DiceButton.ColorFor(color));
            tag.AddThemeColorOverride("font_outline_color", Colors.Black);
            tag.AddThemeConstantOverride("outline_size", 6);
            layer.AddChild(tag);
            _tags[color] = tag;
            UpdateTag(color);

            // Auto button: lets a bot play this seat while the player is away
            if (_config.Seats[color] == SeatType.Human)
            {
                const float buttonW = 170f;
                const float buttonH = 70f;

                float bx = onLeft
                    ? dice.Position.X + DiceSize + 18f
                    : dice.Position.X - 18f - buttonW;

                var auto = new Button
                {
                    Text = "Auto: OFF",
                    ToggleMode = true,
                    FocusMode = Control.FocusModeEnum.None,
                    Size = new Vector2(buttonW, buttonH),
                    Position = new Vector2(bx, diceTop + (DiceSize - buttonH) / 2f)
                };
                auto.AddThemeFontSizeOverride("font_size", 26);
                auto.Toggled += on => SetAutoplay(c, on);
                layer.AddChild(auto);
                _autoButtons[color] = auto;
            }
        }

        _statusLabel = new Label
        {
            Position = new Vector2(0, Mathf.Max(10f, top - DiceGap - DiceSize - 160f)),
            Size = new Vector2(view.X, 70f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 36);
        layer.AddChild(_statusLabel);
    }

    private void UpdateTag(string color)
    {
        string who = _config.Seats[color] == SeatType.Bot ? "Bot"
                   : _auto.Contains(color) ? "Auto"
                   : "Player";

        string team = _config.IsTeams
            ? (color == "Red" || color == "Yellow" ? " - Team 1" : " - Team 2")
            : "";

        string rank = _ranks.TryGetValue(color, out int r) ? $" - {Ordinal(r)}" : "";

        _tags[color].Text = $"{color} ({who}){team}{rank}";
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color", DiceButton.ColorFor(CurrentColor));
    }

    // =====================================
    // AUTOPLAY (bot plays for a human seat until switched off)
    // =====================================

    private void SetAutoplay(string color, bool on)
    {
        if (on) _auto.Add(color); else _auto.Remove(color);

        if (_autoButtons.TryGetValue(color, out Button button))
            button.Text = on ? "Auto: ON" : "Auto: OFF";

        UpdateTag(color);

        if (_state == State.GameOver || color != CurrentColor) return;

        if (_state == State.WaitingForRoll)
        {
            _dice[color].CanRoll = !IsBot(color);

            if (on)
            {
                SetStatus($"{color} (auto) is thinking...");
                GetTree().CreateTimer(BotRollDelay).Timeout += () => BotRollTick(color);
            }
            else
            {
                SetStatus($"{color}'s turn - tap your dice");
            }
        }
        else if (_state == State.WaitingForMove && on)
        {
            // The bot takes over a choice the human was still making
            RelayoutTokens();
            BotChooseAndMove();
        }
    }

    // =====================================
    // TURN FLOW
    // =====================================

    private void StartTurn()
    {
        _state = State.WaitingForRoll;

        string color = CurrentColor;
        DiceButton dice = _dice[color];
        dice.IsTurn = true;

        if (IsBot(color))
        {
            dice.CanRoll = false; // humans can't roll for a bot
            SetStatus($"{color} (bot) is thinking...");
            GetTree().CreateTimer(BotRollDelay).Timeout += () => BotRollTick(color);
        }
        else
        {
            dice.CanRoll = true;
            SetStatus($"{color}'s turn - tap your dice");
        }
    }

    private void BotRollTick(string color)
    {
        if (_state != State.WaitingForRoll || color != CurrentColor) return;

        // Auto was switched off while waiting: hand the dice back to the player
        if (!IsBot(color))
        {
            _dice[color].CanRoll = true;
            SetStatus($"{color}'s turn - tap your dice");
            return;
        }

        OnDiceClicked(color);
    }

    private void EndTurn(bool extraTurn)
    {
        if (!extraTurn)
        {
            _dice[CurrentColor].IsTurn = false;
            _sixStreak = 0;
            _sixSnapshot = null;

            // Skip players who already finished. In team games they stay in,
            // because they move their partner's tokens.
            do
            {
                _currentIndex = (_currentIndex + 1) % _activeOrder.Count;
            }
            while (!_config.IsTeams && _ranks.ContainsKey(CurrentColor));
        }

        StartTurn();
    }

    // Called by a human tapping their dice, or by the bot timer
    private void OnDiceClicked(string color)
    {
        if (_state != State.WaitingForRoll || color != CurrentColor) return;

        _state = State.Rolling;
        _diceValue = RollDie();
        _dice[color].Roll(_diceValue);
        _sound.PlayDice();
        SetStatus($"{color} is rolling...");
    }

    // Faces 1-5 have weight 1; a 6 has weight SixWeight, so sixes come up more often
    private int RollDie()
    {
        float total = 5f + Mathf.Max(0f, SixWeight);
        float r = _rng.Randf() * total;

        return r >= 5f ? 6 : (int)r + 1;
    }

    // Called when the dice animation has settled on its value
    private void OnRollFinished(string color)
    {
        if (_state != State.Rolling || color != CurrentColor) return;

        // Track the streak. Remember the board before the first six so the whole
        // streak can be revoked if a third six follows.
        _sixStreak = _diceValue == 6 ? _sixStreak + 1 : 0;

        if (_sixStreak == 1)
            _sixSnapshot = Board.AllTokens.ToDictionary(t => t, t => t.StepsTaken);
        else if (_sixStreak == 0)
            _sixSnapshot = null; // streak broken: earlier moves are permanent

        if (ThreeSixesLoseTurn && _sixStreak >= 3)
        {
            _state = State.Busy;

            HashSet<Token> undone = RevokeSixMoves();
            SetStatus($"{CurrentColor} rolled three 6s - this streak's moves are undone!");

            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                foreach (Token t in undone) t.ZIndex = 0;
                EndTurn(false);
            };
            return;
        }

        _movable.Clear();
        _movable.AddRange(ControlledTokens().Where(CanMove));

        // Nothing can move: pass the turn
        if (_movable.Count == 0)
        {
            _state = State.Busy;
            SetStatus($"{CurrentColor} rolled {_diceValue} - no moves");
            GetTree().CreateTimer(0.7).Timeout += () => EndTurn(false);
            return;
        }

        // Every movable token is in the same spot, so the outcome is identical:
        // move one automatically.
        bool allEquivalent = _movable
            .Select(t => (t.PlayerColor, t.StepsTaken))
            .Distinct()
            .Count() == 1;

        if (allEquivalent)
        {
            Token pick = _movable[_rng.RandiRange(0, _movable.Count - 1)];

            _state = State.Busy;
            SetStatus($"{CurrentColor} rolled {_diceValue}");
            GetTree().CreateTimer(0.25).Timeout += () => MoveToken(pick);
            return;
        }

        // Bot chooses with a simple heuristic
        if (IsBot(CurrentColor))
        {
            SetStatus($"{CurrentColor} (bot) rolled {_diceValue}");
            BotChooseAndMove();
            return;
        }

        // Human chooses: highlight the movable tokens and pop crowded ones out
        _state = State.WaitingForMove;
        foreach (Token t in _movable) t.Highlighted = true;
        PopOutMovableStacks();
        SetStatus($"{CurrentColor} rolled {_diceValue} - pick a token");
    }

    private void BotChooseAndMove()
    {
        Token pick = ChooseBotMove();

        _state = State.Busy;
        foreach (Token t in _movable) t.Highlighted = false;
        pick.Highlighted = true;

        GetTree().CreateTimer(BotMoveDelay).Timeout += () => MoveToken(pick);
    }

    // Puts every token back where it was before this streak's sixes
    private HashSet<Token> RevokeSixMoves()
    {
        var changed = new HashSet<Token>();

        if (_sixSnapshot == null) return changed;

        foreach (var pair in _sixSnapshot)
        {
            if (!IsInstanceValid(pair.Key)) continue;
            if (pair.Key.StepsTaken == pair.Value) continue;

            pair.Key.StepsTaken = pair.Value;
            changed.Add(pair.Key);
        }

        _sixSnapshot = null;
        RelayoutTokens(changed); // they glide back to their old cells
        return changed;
    }

    // =====================================
    // RULES
    // =====================================

    // The current player's tokens. In team games, a player who has brought all
    // their own tokens home moves their partner's tokens instead.
    private IEnumerable<Token> ControlledTokens()
    {
        string me = CurrentColor;
        string partner = GameConfig.PartnerOf(me);

        bool controlsPartner = _config.IsTeams && AllFinished(me);

        return Board.AllTokens.Where(t =>
            t.PlayerColor == me || (controlsPartner && t.PlayerColor == partner));
    }

    private bool AllFinished(string color)
    {
        return Board.AllTokens.Where(t => t.PlayerColor == color).All(t => t.IsFinished);
    }

    private bool CanMove(Token t)
    {
        if (t.IsInYard) return _diceValue == 6;          // need a 6 to leave the yard
        return t.StepsTaken + _diceValue <= HomeSteps;   // can't overshoot home
    }

    private void OnTokenClicked(Token token)
    {
        if (_state != State.WaitingForMove) return;
        if (!_movable.Contains(token)) return;

        MoveToken(token);
    }

    private void MoveToken(Token token)
    {
        foreach (Token t in _movable) t.Highlighted = false;
        _state = State.Busy;

        // Cells to hop through
        var path = new List<Vector2>();
        int newSteps;

        if (token.IsInYard)
        {
            newSteps = 0;
            path.Add(Board.GetTokenLocalPosition(token.PlayerColor, 0));
        }
        else
        {
            newSteps = token.StepsTaken + _diceValue;
            for (int s = token.StepsTaken + 1; s <= newSteps; s++)
                path.Add(Board.GetTokenLocalPosition(token.PlayerColor, s));
        }

        token.StepsTaken = newSteps;

        // Moving token returns to full size and draws above the others
        token.ZIndex = 10;
        token.ScaleTo(1f, 0.15f);

        // Other tokens settle back into their normal layout (undoes any pop-out)
        RelayoutTokens(null, token);

        Tween tween = token.MoveAlongPath(path, HopDuration, _sound.PlayStep);
        tween.Finished += () => OnMoveFinished(token);
    }

    private void OnMoveFinished(Token token)
    {
        token.ZIndex = 0;

        // Landing on an unsafe cell sends every opposing token there back to its yard
        List<Token> captured = FindCaptures(token);
        foreach (Token victim in captured)
            victim.StepsTaken = -1;

        RelayoutTokens(new HashSet<Token>(captured));

        if (captured.Count > 0)
            _sound.PlayCapture();

        string victims = string.Join(", ", captured.Select(t => t.PlayerColor).Distinct());

        // Sparkles + sound when a token gets home; rank (and a bigger burst) when the whole house is done
        string tokenOwner = token.PlayerColor;
        bool reachedHome = token.IsFinished;
        bool houseDone = false;
        int houseRank = 0;

        if (reachedHome)
        {
            SpawnSparkles(token.GlobalPosition, 28);
            _sound.PlayStar();

            if (AllFinished(tokenOwner) && !_ranks.ContainsKey(tokenOwner))
            {
                houseDone = true;
                houseRank = AssignRank(tokenOwner);
            }
        }

        if (CheckGameOver()) return;

        // Extra turn for a 6, a capture, or bringing a token home
        bool extraTurn = _diceValue == 6
            || (captured.Count > 0 && ExtraTurnOnCapture)
            || (reachedHome && ExtraTurnOnHome);

        // A finished player in a free-for-all has nothing left to move
        if (houseDone && !_config.IsTeams) extraTurn = false;

        if (captured.Count > 0 || reachedHome)
        {
            // Pause briefly so the captures / sparkles can be seen
            _state = State.Busy;

            var parts = new List<string>();
            if (captured.Count > 0) parts.Add($"{CurrentColor} captured {victims}!");
            if (houseDone) parts.Add($"{tokenOwner} finished {Ordinal(houseRank)}!");
            else if (reachedHome) parts.Add($"{tokenOwner} got a token home!");
            SetStatus(string.Join("  ", parts));

            GetTree().CreateTimer(0.6).Timeout += () =>
            {
                foreach (Token victim in captured) victim.ZIndex = 0;
                EndTurn(extraTurn);
            };
            return;
        }

        EndTurn(extraTurn);
    }

    // A player wins when all their tokens are home; a team wins when both partners' are.
    private bool HasCurrentTeamWon()
    {
        return Board.AllTokens
            .Where(t => _config.SameTeam(CurrentColor, t.PlayerColor))
            .All(t => t.IsFinished);
    }

    private string WinTitle()
    {
        if (!_config.IsTeams) return $"{CurrentColor} wins!";

        var pair = new List<string> { CurrentColor, GameConfig.PartnerOf(CurrentColor) };
        pair.Sort((a, b) => Array.IndexOf(GameConfig.Order, a).CompareTo(Array.IndexOf(GameConfig.Order, b)));
        return $"{pair[0]} & {pair[1]} win!";
    }

    // =====================================
    // RANKS, GAME OVER, SPARKLES
    // =====================================

    private static string Ordinal(int n)
    {
        return n switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{n}th"
        };
    }

    // Called when a player's fourth token reaches home
    private int AssignRank(string color)
    {
        int rank = _ranks.Count + 1;
        _ranks[color] = rank;

        // Ranks are permanent, so an unfinished six-streak can no longer be revoked
        _sixSnapshot = null;

        // The house is replaced by a rank medal
        Board.SetRank(color, rank);
        SpawnSparkles(Board.ToGlobal(Board.GetYardCenter(color)), 70);

        UpdateTag(color);

        // Free-for-all: this player is out of the rotation
        if (!_config.IsTeams)
        {
            _dice[color].Visible = false;

            if (_autoButtons.TryGetValue(color, out Button button))
                button.Visible = false;
        }

        return rank;
    }

    // Free-for-all: ends when only one player is left. Teams: ends when a team is all home.
    private bool CheckGameOver()
    {
        string title;
        string colorName;

        if (_config.IsTeams)
        {
            if (!HasCurrentTeamWon()) return false;

            title = WinTitle();
            colorName = CurrentColor;
        }
        else
        {
            int unfinished = _activeOrder.Count - _ranks.Count;
            if (unfinished > 1) return false;

            // The last player left takes the last rank
            foreach (string c in _activeOrder)
                if (!_ranks.ContainsKey(c))
                    _ranks[c] = _ranks.Count + 1;

            string winner = _ranks.First(p => p.Value == 1).Key;
            title = $"{winner} wins!";
            colorName = winner;
        }

        _state = State.GameOver;
        foreach (DiceButton d in _dice.Values)
        {
            d.CanRoll = false;
            d.IsTurn = false;
        }
        foreach (Button b in _autoButtons.Values)
            b.Disabled = true;

        SetStatus(title);

        // Short pause so the final hop and sparkles are visible before the overlay
        GetTree().CreateTimer(0.6).Timeout += () => ShowWinScreen(title, colorName);
        return true;
    }

    // One-shot burst of glowing sparks at a screen position
    private void SpawnSparkles(Vector2 globalPosition, int amount)
    {
        // Built once, reused for every burst
        if (_glowTexture == null)
        {
            var glowGradient = new Gradient();
            glowGradient.SetColor(0, Colors.White);
            glowGradient.SetColor(1, new Color(1, 1, 1, 0));

            _glowTexture = new GradientTexture2D
            {
                Gradient = glowGradient,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 32,
                Height = 32
            };

            // Gold that fades out over the particle's life
            _sparkFade = new Gradient();
            _sparkFade.SetColor(0, new Color(1f, 0.95f, 0.6f, 1f));
            _sparkFade.SetColor(1, new Color(1f, 0.7f, 0.2f, 0f));
        }

        var sparkles = new CpuParticles2D
        {
            Emitting = false, // start after positioning
            Amount = amount,
            Lifetime = 1.0,
            OneShot = true,
            Explosiveness = 0.9f,
            Texture = _glowTexture,
            Direction = Vector2.Up,
            Spread = 180f,
            Gravity = Vector2.Zero,
            InitialVelocityMin = 120f,
            InitialVelocityMax = 320f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.6f,
            ColorRamp = _sparkFade,
            ZIndex = 50,
            Material = new CanvasItemMaterial
            {
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add
            }
        };

        Board.AddChild(sparkles);
        sparkles.GlobalPosition = globalPosition;
        sparkles.Emitting = true;

        GetTree().CreateTimer(2.0).Timeout += () =>
        {
            if (IsInstanceValid(sparkles)) sparkles.QueueFree();
        };
    }

    // =====================================
    // CAPTURES
    // =====================================

    private List<Token> FindCaptures(Token mover)
    {
        var result = new List<Token>();

        // Stars, start cells and home lanes are safe
        if (Board.IsSafe(mover.PlayerColor, mover.StepsTaken))
            return result;

        int cell = Board.GetPathIndex(mover.PlayerColor, mover.StepsTaken);

        foreach (Token other in Board.AllTokens)
        {
            // Never capture yourself or your teammates
            if (_config.SameTeam(mover.PlayerColor, other.PlayerColor)) continue;

            if (Board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                result.Add(other);
        }

        return result;
    }

    // =====================================
    // BOT AI
    // =====================================

    private Token ChooseBotMove()
    {
        Token best = _movable[0];
        float bestScore = float.MinValue;

        foreach (Token t in _movable)
        {
            // Small random term breaks ties so bots aren't perfectly predictable
            float score = ScoreMove(t) + _rng.Randf() * 3f;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private float ScoreMove(Token t)
    {
        string color = t.PlayerColor;
        int from = t.StepsTaken;
        int to = t.IsInYard ? 0 : from + _diceValue;

        float score = 0f;

        // Getting a token out of the yard is valuable; otherwise prefer progress
        if (t.IsInYard) score += 40f;
        else score += to * 0.5f;

        // Capturing: more valuable the further the victim had travelled
        if (!Board.IsSafe(color, to))
        {
            int cell = Board.GetPathIndex(color, to);

            foreach (Token other in Board.AllTokens)
            {
                if (_config.SameTeam(color, other.PlayerColor)) continue;

                if (Board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                    score += 100f + other.StepsTaken * 2f;
            }
        }

        // Getting home / into the home lane
        if (to == HomeSteps) score += 80f;
        else if (to > 50 && from <= 50) score += 50f;

        // Reaching a safe square
        if (to <= 50 && Board.IsSafe(color, to)) score += 25f;

        // Danger: run away from threats, avoid walking into them
        if (from >= 0 && IsThreatened(color, from)) score += 30f + from * 0.2f;
        if (IsThreatened(color, to)) score -= 35f;

        return score;
    }

    // True if an opposing token could land on this cell with a single roll (1-6)
    private bool IsThreatened(string color, int steps)
    {
        if (Board.IsSafe(color, steps)) return false;

        int loop = Board.MainPathPositions.Count;
        int cell = Board.GetPathIndex(color, steps);

        foreach (Token other in Board.AllTokens)
        {
            if (_config.SameTeam(color, other.PlayerColor)) continue;

            int otherCell = Board.GetPathIndex(other.PlayerColor, other.StepsTaken);
            if (otherCell < 0) continue;

            int distance = (cell - otherCell + loop) % loop;

            // It must also still be on the shared track when it gets there
            if (distance >= 1 && distance <= 6 && other.StepsTaken + distance <= 50)
                return true;
        }

        return false;
    }

    // =====================================
    // WIN SCREEN + RESTART
    // =====================================

    private void ShowWinScreen(string titleText, string colorName)
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);

        Vector2 view = GetViewport().GetVisibleRect().Size;

        // Dim background (also blocks clicks on the board behind it)
        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            Size = view,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        layer.AddChild(dim);
        dim.CreateTween().TweenProperty(dim, "color:a", 0.65f, 0.4f);

        SpawnConfetti(layer, view);

        // Centered panel: title, standings, button
        var panel = new VBoxContainer
        {
            Size = new Vector2(view.X, 700f),
            Position = new Vector2(0, view.Y / 2f - 350f),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        panel.AddThemeConstantOverride("separation", 40);
        panel.PivotOffset = panel.Size / 2f;
        panel.Scale = new Vector2(0.5f, 0.5f);
        panel.Modulate = new Color(1, 1, 1, 0);
        layer.AddChild(panel);

        var title = new Label
        {
            Text = titleText,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 96);
        title.AddThemeColorOverride("font_color", DiceButton.ColorFor(colorName));
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 16);
        panel.AddChild(title);

        // Final standings
        foreach (var entry in _ranks.OrderBy(p => p.Value))
        {
            var line = new Label
            {
                Text = $"{Ordinal(entry.Value)}   {entry.Key}",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            line.AddThemeFontSizeOverride("font_size", 52);
            line.AddThemeColorOverride("font_color", DiceButton.ColorFor(entry.Key));
            line.AddThemeColorOverride("font_outline_color", Colors.Black);
            line.AddThemeConstantOverride("outline_size", 10);
            panel.AddChild(line);
        }

        // Reloading the scene returns to the setup screen (last settings are remembered)
        var restart = new Button
        {
            Text = "New game",
            CustomMinimumSize = new Vector2(360, 100),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        restart.AddThemeFontSizeOverride("font_size", 44);
        restart.Pressed += () => GetTree().ReloadCurrentScene();
        panel.AddChild(restart);

        // Pop-in animation
        var pop = panel.CreateTween();
        pop.SetParallel(true);
        pop.TweenProperty(panel, "scale", Vector2.One, 0.5f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        pop.TweenProperty(panel, "modulate:a", 1f, 0.3f);
    }

    private void SpawnConfetti(CanvasLayer layer, Vector2 view)
    {
        Color[] palette =
        {
            DiceButton.ColorFor("Red"), DiceButton.ColorFor("Green"),
            DiceButton.ColorFor("Yellow"), DiceButton.ColorFor("Blue"),
            Colors.White
        };

        for (int i = 0; i < 90; i++)
        {
            var piece = new ColorRect
            {
                Color = palette[_rng.RandiRange(0, palette.Length - 1)],
                Size = new Vector2(_rng.RandfRange(10f, 18f), _rng.RandfRange(16f, 26f)),
                Position = new Vector2(_rng.RandfRange(0f, view.X), -40f),
                Rotation = _rng.RandfRange(0f, Mathf.Tau),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            layer.AddChild(piece);

            float delay = _rng.RandfRange(0f, 1.2f);
            float fall = _rng.RandfRange(2.5f, 4.5f);
            float drift = _rng.RandfRange(-120f, 120f);

            var tween = piece.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(piece, "position:y", view.Y + 60f, fall).SetDelay(delay);
            tween.TweenProperty(piece, "position:x", piece.Position.X + drift, fall).SetDelay(delay);
            tween.TweenProperty(piece, "rotation", piece.Rotation + _rng.RandfRange(-8f, 8f), fall).SetDelay(delay);
            tween.Chain().TweenCallback(Callable.From(piece.QueueFree));
        }
    }

    // =====================================
    // TOKEN LAYOUT: stacks, pop-out, smooth gliding
    // =====================================

    // Groups tokens that stand on the same board cell (yard tokens are ignored)
    private Dictionary<Vector2I, (Vector2 Pos, Vector2 Cell, List<Token> Tokens)> GroupByCell(Token skip)
    {
        var groups = new Dictionary<Vector2I, (Vector2 Pos, Vector2 Cell, List<Token> Tokens)>();

        foreach (Token t in Board.AllTokens)
        {
            if (t == skip || t.IsInYard) continue;

            Vector2 basePos = Board.GetTokenLocalPosition(t.PlayerColor, t.StepsTaken);
            var key = new Vector2I(Mathf.RoundToInt(basePos.X), Mathf.RoundToInt(basePos.Y));

            if (!groups.TryGetValue(key, out var group))
            {
                group = (basePos, Board.GetCellSize(t.PlayerColor, t.StepsTaken), new List<Token>());
                groups[key] = group;
            }

            group.Tokens.Add(t);
        }

        return groups;
    }

    // Glides every token to its resting place. Tokens in "flying" move slower and draw on top
    // (used for captured tokens going home and for revoked moves). "skip" is left alone.
    private void RelayoutTokens(HashSet<Token> flying = null, Token skip = null)
    {
        foreach (Token t in Board.AllTokens)
        {
            if (t == skip || !t.IsInYard) continue;
            PlaceToken(t, t.YardPosition, 1f, flying);
        }

        foreach (var group in GroupByCell(skip).Values)
        {
            int n = group.Tokens.Count;

            for (int i = 0; i < n; i++)
                PlaceToken(group.Tokens[i], group.Pos + StackOffset(i, n, group.Cell), StackScale(n), flying);
        }
    }

    private void PlaceToken(Token token, Vector2 position, float scale, HashSet<Token> flying)
    {
        bool fly = flying != null && flying.Contains(token);

        token.ZIndex = fly ? 5 : 0;
        token.GlideTo(position, scale, fly ? 0.4f : 0.16f);
    }

    // When it's the player's choice and three or more tokens share a cell, the player's
    // movable tokens fan out at normal size so they are easy to see and tap.
    // They stay close to their own cell so they don't hide neighbouring cells.
    private void PopOutMovableStacks()
    {
        foreach (var group in GroupByCell(null).Values)
        {
            if (group.Tokens.Count < 3) continue;

            List<Token> mine = group.Tokens.Where(t => _movable.Contains(t)).ToList();
            if (mine.Count == 0) continue;

            // Tokens left behind re-spread in the small stack
            List<Token> rest = group.Tokens.Where(t => !mine.Contains(t)).ToList();
            for (int i = 0; i < rest.Count; i++)
                PlaceToken(rest[i], group.Pos + StackOffset(i, rest.Count, group.Cell), StackScale(rest.Count), null);

            // Movable tokens: overlapping fan, normal size, nudged slightly up
            float pawnWidth = mine[0].Radius * 1.76f;
            float spacing = pawnWidth * 0.62f;
            float startX = -spacing * (mine.Count - 1) / 2f;

            for (int i = 0; i < mine.Count; i++)
            {
                Vector2 pos = group.Pos + new Vector2(startX + i * spacing, -group.Cell.Y * 0.18f);

                mine[i].ZIndex = 30 + i;
                mine[i].GlideTo(pos, 1f, 0.2f);
            }
        }
    }

    // Offsets for several tokens sharing a cell (scaled to that cell's width and height)
    private static Vector2 StackOffset(int index, int count, Vector2 cell)
    {
        if (count == 1) return Vector2.Zero;

        float f = count switch
        {
            2 => 0.25f,
            3 => 0.27f,
            4 => 0.30f,
            _ => 0.36f
        };

        float rx = cell.X * f;
        float ry = cell.Y * f * 0.9f;

        // Two tokens sit side by side; more form a ring starting at the top
        float start = count == 2 ? Mathf.Pi : -Mathf.Pi / 2f;
        float angle = start + Mathf.Tau * index / count;

        return new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
    }

    private static float StackScale(int count)
    {
        return count switch
        {
            1 => 1f,
            2 => 0.72f,
            3 => 0.62f,
            4 => 0.58f,
            _ => 0.46f
        };
    }
}