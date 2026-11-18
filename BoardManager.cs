using Godot;
using System.Collections.Generic;

public partial class BoardManager : Node2D
{
    [Export] public float BoardSizePixels = 900f; // board width (ignored when FitToViewport is on)
    [Export] public Vector2 BoardTopLeft = Vector2.Zero;
    [Export] public PackedScene TokenScene;

    // Centers the board in the game window
    [Export] public bool CenterInViewport = true;

    // Grows the board to fill the screen width
    [Export] public bool FitToViewport = true;
    [Export] public float BoardMargin = 24f;

    // Cell height compared with cell width. 1.5 = path boxes are 1.5x taller.
    // Reduced automatically if the screen is not tall enough.
    [Export] public float CellAspect = 1.5f;

    // Height kept free for dice, labels and status text
    [Export] public float ReservedHeight = 760f;

    private float _cellW;
    private float _cellH;
    private float _cellMin;
    private Node2D _tokensNode;

    private readonly Color RedColor    = new Color(0.90f, 0.04f, 0.04f);
    private readonly Color GreenColor  = new Color(0.02f, 0.60f, 0.12f);
    private readonly Color YellowColor = new Color(0.96f, 0.82f, 0.00f);
    private readonly Color BlueColor   = new Color(0.00f, 0.65f, 0.95f);

    private const float GridLineWidth = 2f;

    public float CellWidth => _cellW;
    public float CellHeight => _cellH;
    public float BoardWidth => _cellW * 15f;
    public float BoardHeight => _cellH * 15f;

    public List<Vector2> MainPathPositions { get; private set; } = new();
    public Dictionary<string, List<Vector2>> HomePathPositions { get; private set; } = new();

    private readonly List<Token> _allTokens = new();
    public IReadOnlyList<Token> AllTokens => _allTokens;

    // 52-cell loop, clockwise, starting at Red's start cell
    private readonly Vector2I[] _mainLoopCoords =
    {
        new(1,6), new(2,6), new(3,6), new(4,6), new(5,6),
        new(6,5), new(6,4), new(6,3), new(6,2), new(6,1), new(6,0),
        new(7,0), new(8,0),
        new(8,1), new(8,2), new(8,3), new(8,4), new(8,5),
        new(9,6), new(10,6), new(11,6), new(12,6), new(13,6), new(14,6),
        new(14,7), new(14,8),
        new(13,8), new(12,8), new(11,8), new(10,8), new(9,8),
        new(8,9), new(8,10), new(8,11), new(8,12), new(8,13), new(8,14),
        new(7,14), new(6,14),
        new(6,13), new(6,12), new(6,11), new(6,10), new(6,9),
        new(5,8), new(4,8), new(3,8), new(2,8), new(1,8), new(0,8),
        new(0,7), new(0,6)
    };

    private readonly Dictionary<string, int> _startIndices = new()
    {
        { "Red", 0 },
        { "Green", 13 },
        { "Yellow", 26 },
        { "Blue", 39 }
    };

    private readonly Dictionary<string, Vector2I> _yardOrigins = new()
    {
        { "Red", new Vector2I(0, 0) },
        { "Green", new Vector2I(9, 0) },
        { "Yellow", new Vector2I(9, 9) },
        { "Blue", new Vector2I(0, 9) }
    };

    // Yard token slots in grid units from the yard's top-left corner
    private readonly Vector2[] _yardSlots =
    {
        new(2f, 2f), new(4f, 2f),
        new(2f, 4f), new(4f, 4f)
    };

    public override void _Ready()
    {
        Vector2 view = GetViewportRect().Size;

        // Fill the screen width
        if (FitToViewport)
            BoardSizePixels = Mathf.Max(300f, view.X - 2f * BoardMargin);

        _cellW = BoardSizePixels / 15f;

        // Make the cells taller (up to CellAspect) using the spare screen height
        float maxAspect = Mathf.Max(1f, (view.Y - ReservedHeight) / (15f * _cellW));
        _cellH = _cellW * Mathf.Clamp(CellAspect, 1f, maxAspect);
        _cellMin = Mathf.Min(_cellW, _cellH);

        if (CenterInViewport)
        {
            Vector2 boardSize = new Vector2(BoardWidth, BoardHeight);
            BoardTopLeft = ToLocal((view - boardSize) / 2f);
        }

        _tokensNode = GetNodeOrNull<Node2D>("../Tokens")
                      ?? GetNodeOrNull<Node2D>("Tokens");

        BuildBoardCoordinates();

        if (TokenScene != null)
        {
            foreach (var pair in _yardOrigins)
                SpawnTokensForColor(pair.Key, pair.Value);
        }

        QueueRedraw();
    }

    // =====================================
    // PUBLIC API FOR GAME LOGIC
    // =====================================

    // Where a token with the given step count sits (local to the Tokens node).
    // steps: 0..50 = main track, 51..56 = home lane.
    public Vector2 GetTokenLocalPosition(string color, int steps)
    {
        Vector2 boardPos;

        if (steps <= 50)
        {
            int index = (_startIndices[color] + steps) % MainPathPositions.Count;
            boardPos = MainPathPositions[index];
        }
        else
        {
            boardPos = HomePathPositions[color][steps - 51];
        }

        return _tokensNode.ToLocal(ToGlobal(boardPos));
    }

    // Center of a player's yard (local to this node; use ToGlobal for screen coords)
    public Vector2 GetYardCenter(string color)
    {
        Vector2I origin = _yardOrigins[color];
        return GridPoint(origin.X + 3f, origin.Y + 3f);
    }

    // Finished players: color -> rank. Their yard is drawn as a rank medal.
    private readonly Dictionary<string, int> _ranks = new();

    public void SetRank(string color, int rank)
    {
        _ranks[color] = rank;
        QueueRedraw();
    }

    // Colors that are not playing this game (their yard sockets are drawn grey)
    private readonly HashSet<string> _inactiveColors = new();

    // Removes a seat from the game: frees its tokens and greys out its yard
    public void RemoveColor(string color)
    {
        _inactiveColors.Add(color);

        for (int i = _allTokens.Count - 1; i >= 0; i--)
        {
            if (_allTokens[i].PlayerColor != color) continue;

            _allTokens[i].QueueFree();
            _allTokens.RemoveAt(i);
        }

        QueueRedraw();
    }

    // Cells where a token can never be captured: the 4 start cells and the
    // 4 star cells (indices into the 52-cell loop)
    private readonly HashSet<int> _safePathIndices = new() { 0, 13, 26, 39, 8, 21, 34, 47 };

    // Index (0..51) of the shared loop cell a token stands on, or -1 if it is
    // in the yard or already inside its own home lane
    public int GetPathIndex(string color, int steps)
    {
        if (steps < 0 || steps > 50) return -1;
        return (_startIndices[color] + steps) % MainPathPositions.Count;
    }

    // True if a token with this step count cannot be captured
    public bool IsSafe(string color, int steps)
    {
        int index = GetPathIndex(color, steps);
        return index < 0 || _safePathIndices.Contains(index);
    }

    // =====================================
    // GRID COORDINATES
    // =====================================

    public Vector2 GridToScreen(int col, int row)
    {
        return BoardTopLeft + new Vector2(
            (col + 0.5f) * _cellW,
            (row + 0.5f) * _cellH
        );
    }

    private Vector2 GridPoint(float x, float y)
    {
        return BoardTopLeft + new Vector2(x * _cellW, y * _cellH);
    }

    private Rect2 CellRect(int col, int row)
    {
        return new Rect2(
            BoardTopLeft + new Vector2(col * _cellW, row * _cellH),
            new Vector2(_cellW, _cellH)
        );
    }

    private void BuildBoardCoordinates()
    {
        MainPathPositions.Clear();
        HomePathPositions.Clear();

        foreach (Vector2I coord in _mainLoopCoords)
            MainPathPositions.Add(GridToScreen(coord.X, coord.Y));

        HomePathPositions["Red"] = new()
        {
            GridToScreen(1,7), GridToScreen(2,7), GridToScreen(3,7),
            GridToScreen(4,7), GridToScreen(5,7), GridToScreen(6,7)
        };

        HomePathPositions["Green"] = new()
        {
            GridToScreen(7,1), GridToScreen(7,2), GridToScreen(7,3),
            GridToScreen(7,4), GridToScreen(7,5), GridToScreen(7,6)
        };

        HomePathPositions["Yellow"] = new()
        {
            GridToScreen(13,7), GridToScreen(12,7), GridToScreen(11,7),
            GridToScreen(10,7), GridToScreen(9,7),  GridToScreen(8,7)
        };

        HomePathPositions["Blue"] = new()
        {
            GridToScreen(7,13), GridToScreen(7,12), GridToScreen(7,11),
            GridToScreen(7,10), GridToScreen(7,9),  GridToScreen(7,8)
        };
    }

    // =====================================
    // SPAWN TOKENS (no game rules here)
    // =====================================

    private void SpawnTokensForColor(string color, Vector2I yardOrigin)
    {
        if (_tokensNode == null)
        {
            GD.PushError("Cannot find Tokens node!");
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            Token token = TokenScene.Instantiate<Token>();

            token.PlayerColor = color;
            token.TokenId = i;
            token.Radius = _cellW * 0.40f; // pawn is about 70% of a cell wide

            Vector2 boardPosition = GridPoint(
                yardOrigin.X + _yardSlots[i].X,
                yardOrigin.Y + _yardSlots[i].Y
            );

            token.Position = _tokensNode.ToLocal(ToGlobal(boardPosition));
            token.YardPosition = token.Position;

            _tokensNode.AddChild(token);
            _allTokens.Add(token);
        }
    }

    // =====================================
    // MAIN BOARD DRAWING
    // =====================================

    public override void _Draw()
    {
        DrawHomeAreas();
        DrawTrackCells();
        DrawHomeLanes();
        DrawStartCells();
        DrawYards();
        DrawCenter();
        DrawSymbols();
        DrawGridLines();

        DrawRect(
            new Rect2(BoardTopLeft, new Vector2(BoardWidth, BoardHeight)),
            Colors.Black, false, 3f
        );
    }

    private void DrawHomeAreas()
    {
        Vector2 size = new Vector2(_cellW * 6f, _cellH * 6f);

        DrawRect(new Rect2(GridPoint(0, 0), size), RedColor);
        DrawRect(new Rect2(GridPoint(9, 0), size), GreenColor);
        DrawRect(new Rect2(GridPoint(9, 9), size), YellowColor);
        DrawRect(new Rect2(GridPoint(0, 9), size), BlueColor);
    }

    private bool IsTrackCell(int col, int row)
    {
        bool corner =
            (col < 6 && row < 6) || (col > 8 && row < 6) ||
            (col < 6 && row > 8) || (col > 8 && row > 8);

        bool center = col >= 6 && col <= 8 && row >= 6 && row <= 8;

        return !corner && !center;
    }

    private void DrawTrackCells()
    {
        for (int row = 0; row < 15; row++)
            for (int col = 0; col < 15; col++)
                if (IsTrackCell(col, row))
                    DrawRect(CellRect(col, row), Colors.White);
    }

    private void DrawHomeLanes()
    {
        for (int c = 1; c <= 5; c++)  DrawRect(CellRect(c, 7), RedColor);
        for (int r = 1; r <= 5; r++)  DrawRect(CellRect(7, r), GreenColor);
        for (int c = 9; c <= 13; c++) DrawRect(CellRect(c, 7), YellowColor);
        for (int r = 9; r <= 13; r++) DrawRect(CellRect(7, r), BlueColor);
    }

    private void DrawStartCells()
    {
        DrawRect(CellRect(1, 6),  RedColor);
        DrawRect(CellRect(8, 1),  GreenColor);
        DrawRect(CellRect(13, 8), YellowColor);
        DrawRect(CellRect(6, 13), BlueColor);
    }

    private void DrawYards()
    {
        DrawYard("Red",    new Vector2I(0, 0), RedColor);
        DrawYard("Green",  new Vector2I(9, 0), GreenColor);
        DrawYard("Yellow", new Vector2I(9, 9), YellowColor);
        DrawYard("Blue",   new Vector2I(0, 9), BlueColor);
    }

    private void DrawYard(string name, Vector2I origin, Color color)
    {
        // Empty seats get grey sockets
        if (_inactiveColors.Contains(name))
            color = new Color(0.70f, 0.70f, 0.70f);

        Rect2 rect = new Rect2(
            GridPoint(origin.X + 1, origin.Y + 1),
            new Vector2(_cellW * 4, _cellH * 4)
        );

        DrawRect(rect, Colors.White);
        DrawRect(rect, Colors.Black, false, GridLineWidth);

        // Finished player: show their rank instead of the sockets
        if (_ranks.TryGetValue(name, out int rank))
        {
            DrawRankMedal(rect, rank, color);
            return;
        }

        // Sockets: pale, hollow-looking rings, clearly different from the solid pawns
        foreach (Vector2 slot in _yardSlots)
        {
            Vector2 center = GridPoint(origin.X + slot.X, origin.Y + slot.Y);
            float radius = _cellW * 0.52f;

            DrawCircle(center, radius, color.Lerp(Colors.White, 0.78f));
            DrawArc(center, radius, 0f, Mathf.Tau, 48, color, 4f, true);
            DrawArc(center, radius * 0.5f, 0f, Mathf.Tau, 32, color.Lerp(Colors.White, 0.45f), 3f, true);
        }
    }

    private void DrawRankMedal(Rect2 box, int rank, Color playerColor)
    {
        Vector2 center = box.GetCenter();
        float radius = _cellW * 1.7f;

        Color medal = rank switch
        {
            1 => new Color(1.00f, 0.84f, 0.10f), // gold
            2 => new Color(0.80f, 0.80f, 0.85f), // silver
            3 => new Color(0.80f, 0.50f, 0.20f), // bronze
            _ => new Color(0.55f, 0.58f, 0.65f)
        };

        DrawCircle(center, radius, medal);
        DrawArc(center, radius, 0f, Mathf.Tau, 64, playerColor, 10f, true);
        DrawArc(center, radius + 6f, 0f, Mathf.Tau, 64, Colors.Black, 2f, true);

        string text = rank switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{rank}th"
        };

        Font font = ThemeDB.FallbackFont;
        int fontSize = (int)(_cellW * 1.5f);
        Vector2 pos = new Vector2(box.Position.X, center.Y + fontSize * 0.33f);

        DrawStringOutline(font, pos, text, HorizontalAlignment.Center, box.Size.X, fontSize, 12, Colors.Black);
        DrawString(font, pos, text, HorizontalAlignment.Center, box.Size.X, fontSize, Colors.White);
    }

    private void DrawCenter()
    {
        Vector2 topLeft     = GridPoint(6, 6);
        Vector2 topRight    = GridPoint(9, 6);
        Vector2 bottomLeft  = GridPoint(6, 9);
        Vector2 bottomRight = GridPoint(9, 9);
        Vector2 center      = GridPoint(7.5f, 7.5f);

        DrawColoredPolygon(new Vector2[] { topLeft, bottomLeft, center },     RedColor);
        DrawColoredPolygon(new Vector2[] { topLeft, topRight, center },       GreenColor);
        DrawColoredPolygon(new Vector2[] { topRight, bottomRight, center },   YellowColor);
        DrawColoredPolygon(new Vector2[] { bottomLeft, bottomRight, center }, BlueColor);

        DrawRect(
            new Rect2(topLeft, new Vector2(_cellW * 3, _cellH * 3)),
            Colors.Black, false, GridLineWidth
        );
    }

    private void DrawSymbols()
    {
        DrawStar(GridToScreen(6, 2),  RedColor);
        DrawStar(GridToScreen(12, 6), GreenColor);
        DrawStar(GridToScreen(8, 12), YellowColor);
        DrawStar(GridToScreen(2, 8),  BlueColor);

        DrawArrow(GridToScreen(0, 7),  Vector2.Right, RedColor);
        DrawArrow(GridToScreen(7, 0),  Vector2.Down,  GreenColor);
        DrawArrow(GridToScreen(14, 7), Vector2.Left,  YellowColor);
        DrawArrow(GridToScreen(7, 14), Vector2.Up,    BlueColor);
    }

    private void DrawStar(Vector2 center, Color color)
    {
        Vector2[] points = new Vector2[10];

        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.Pi * i / 5f - Mathf.Pi / 2f;
            float radius = i % 2 == 0 ? _cellMin * 0.38f : _cellMin * 0.17f;

            points[i] = center + new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius
            );
        }

        DrawColoredPolygon(points, color);

        Vector2[] outline = new Vector2[11];
        for (int i = 0; i < 10; i++) outline[i] = points[i];
        outline[10] = points[0];

        DrawPolyline(outline, Colors.Black, 1.5f, true);
    }

    private void DrawArrow(Vector2 center, Vector2 direction, Color color)
    {
        Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

        float length = _cellMin * 0.42f;
        float width  = _cellMin * 0.22f;

        Vector2[] points =
        {
            center + direction * length,
            center + direction * (length * 0.15f) + perpendicular * width,
            center + direction * (length * 0.15f) + perpendicular * (width * 0.45f),
            center - direction * (length * 0.75f) + perpendicular * (width * 0.45f),
            center - direction * (length * 0.75f) - perpendicular * (width * 0.45f),
            center + direction * (length * 0.15f) - perpendicular * (width * 0.45f),
            center + direction * (length * 0.15f) - perpendicular * width
        };

        DrawColoredPolygon(points, color);

        Vector2[] outline =
        {
            points[0], points[1], points[2], points[3],
            points[4], points[5], points[6], points[0]
        };

        DrawPolyline(outline, Colors.Black, 1.5f, true);
    }

    private void DrawGridLines()
    {
        for (int row = 0; row < 15; row++)
            for (int col = 0; col < 15; col++)
                if (IsTrackCell(col, row))
                    DrawRect(CellRect(col, row), Colors.Black, false, GridLineWidth);
    }
}