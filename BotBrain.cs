using Godot;
using System.Collections.Generic;

// Decides which token a bot moves. Each possible move gets a score; the best wins.
public class BotBrain
{
    private readonly BoardManager _board;
    private readonly GameConfig _config;
    private readonly RandomNumberGenerator _rng;

    public BotBrain(BoardManager board, GameConfig config, RandomNumberGenerator rng)
    {
        _board = board;
        _config = config;
        _rng = rng;
    }

    public Token ChooseMove(IReadOnlyList<Token> movable, int dice)
    {
        Token best = movable[0];
        float bestScore = float.MinValue;

        foreach (Token t in movable)
        {
            // Small random term breaks ties so bots aren't perfectly predictable
            float score = ScoreMove(t, dice) + _rng.Randf() * 3f;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private float ScoreMove(Token t, int dice)
    {
        string color = t.PlayerColor;
        int from = t.StepsTaken;
        int to = t.IsInYard ? 0 : from + dice;

        float score = 0f;

        // Getting a token out of the yard is valuable; otherwise prefer progress
        if (t.IsInYard) score += 40f;
        else score += to * 0.5f;

        // Capturing: more valuable the further the victim had travelled
        if (!_board.IsSafe(color, to))
        {
            int cell = _board.GetPathIndex(color, to);

            foreach (Token other in _board.AllTokens)
            {
                if (_config.SameTeam(color, other.PlayerColor)) continue;

                if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                    score += 100f + other.StepsTaken * 2f;
            }
        }

        // Getting home / into the home lane
        if (to == GameRules.HomeSteps) score += 80f;
        else if (to > 50 && from <= 50) score += 50f;

        // Reaching a safe square
        if (to <= 50 && _board.IsSafe(color, to)) score += 25f;

        // Danger: run away from threats, avoid walking into them
        if (from >= 0 && IsThreatened(color, from)) score += 30f + from * 0.2f;
        if (IsThreatened(color, to)) score -= 35f;

        return score;
    }

    // True if an opposing token could land on this cell with a single roll (1-6)
    private bool IsThreatened(string color, int steps)
    {
        if (_board.IsSafe(color, steps)) return false;

        int loop = _board.MainPathPositions.Count;
        int cell = _board.GetPathIndex(color, steps);

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(color, other.PlayerColor)) continue;

            int otherCell = _board.GetPathIndex(other.PlayerColor, other.StepsTaken);
            if (otherCell < 0) continue;

            int distance = (cell - otherCell + loop) % loop;

            // It must also still be on the shared track when it gets there
            if (distance >= 1 && distance <= 6 && other.StepsTaken + distance <= 50)
                return true;
        }

        return false;
    }
}
