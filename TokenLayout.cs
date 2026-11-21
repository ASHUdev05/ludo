using Godot;
using System.Collections.Generic;
using System.Linq;

// Decides where every token rests: yard sockets, single cells, shared cells (stacks),
// and the pop-out fan used when a player has to choose among crowded tokens.
public class TokenLayout
{
    private readonly BoardManager _board;

    public TokenLayout(BoardManager board)
    {
        _board = board;
    }

    // Glides every token to its resting place. Tokens in "flying" move slower and draw on top
    // (used for captured tokens going home and for revoked moves). "skip" is left alone.
    public void Relayout(HashSet<Token> flying = null, Token skip = null, Token skip2 = null)
    {
        foreach (Token t in _board.AllTokens)
        {
            if (t == skip || t == skip2 || !t.IsInYard) continue;
            Place(t, t.YardPosition, 1f, flying);
        }

        foreach (var group in GroupByCell(skip, skip2).Values)
        {
            int n = group.Tokens.Count;

            for (int i = 0; i < n; i++)
                Place(group.Tokens[i], group.Pos + StackOffset(i, n, group.Cell), StackScale(n), flying);
        }
    }

    // When it's the player's choice and three or more tokens share a cell, the player's
    // movable tokens fan out at normal size so they are easy to see and tap.
    // They stay close to their own cell so they don't hide neighbouring cells.
    public void PopOut(ICollection<Token> movable)
    {
        foreach (var group in GroupByCell(null).Values)
        {
            if (group.Tokens.Count < 3) continue;

            List<Token> mine = group.Tokens.Where(t => movable.Contains(t)).ToList();
            if (mine.Count == 0) continue;

            // Tokens left behind re-spread in the small stack
            List<Token> rest = group.Tokens.Where(t => !mine.Contains(t)).ToList();
            for (int i = 0; i < rest.Count; i++)
                Place(rest[i], group.Pos + StackOffset(i, rest.Count, group.Cell), StackScale(rest.Count), null);

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

    // Groups tokens that stand on the same board cell (yard tokens are ignored)
    private Dictionary<Vector2I, (Vector2 Pos, Vector2 Cell, List<Token> Tokens)> GroupByCell(Token skip, Token skip2 = null)
    {
        var groups = new Dictionary<Vector2I, (Vector2 Pos, Vector2 Cell, List<Token> Tokens)>();

        foreach (Token t in _board.AllTokens)
        {
            if (t == skip || t == skip2 || t.IsInYard) continue;

            Vector2 basePos = _board.GetTokenLocalPosition(t.PlayerColor, t.StepsTaken);
            var key = new Vector2I(Mathf.RoundToInt(basePos.X), Mathf.RoundToInt(basePos.Y));

            if (!groups.TryGetValue(key, out var group))
            {
                group = (basePos, _board.GetCellSize(t.PlayerColor, t.StepsTaken), new List<Token>());
                groups[key] = group;
            }

            group.Tokens.Add(t);
        }

        return groups;
    }

    private static void Place(Token token, Vector2 position, float scale, HashSet<Token> flying)
    {
        bool fly = flying != null && flying.Contains(token);

        token.ZIndex = fly ? 5 : 0;
        token.GlideTo(position, scale, fly ? 0.4f : 0.16f);
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
