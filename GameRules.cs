using System;
using System.Collections.Generic;
using System.Linq;

// The rules of Ludo, kept separate from UI and flow so they are easy to read and test.
public class GameRules
{
    // 0..50 track, 51..55 home lane, 56 = finished
    public const int HomeSteps = 56;

    private readonly BoardManager _board;
    private readonly GameConfig _config;

    public GameRules(BoardManager board, GameConfig config)
    {
        _board = board;
        _config = config;
    }

    public bool CanMove(Token t, int dice)
    {
        if (t.IsInYard) return dice == 6;               // need a 6 to leave the yard
        return t.StepsTaken + dice <= HomeSteps;        // can't overshoot home
    }

    // The tokens a player may move. In team games, a player who has brought all
    // their own tokens home moves their partner's tokens instead.
    public IEnumerable<Token> ControlledTokens(string color)
    {
        string partner = GameConfig.PartnerOf(color);
        bool controlsPartner = _config.IsTeams && AllFinished(color);

        return _board.AllTokens.Where(t =>
            t.PlayerColor == color || (controlsPartner && t.PlayerColor == partner));
    }

    public bool AllFinished(string color)
    {
        return _board.AllTokens.Where(t => t.PlayerColor == color).All(t => t.IsFinished);
    }

    // Opposing tokens sent home by this token's landing. Stars, start cells and
    // home lanes are safe; teammates and yourself are never captured.
    public List<Token> FindCaptures(Token mover)
    {
        var result = new List<Token>();

        if (_board.IsSafe(mover.PlayerColor, mover.StepsTaken))
            return result;

        int cell = _board.GetPathIndex(mover.PlayerColor, mover.StepsTaken);

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(mover.PlayerColor, other.PlayerColor)) continue;

            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                result.Add(other);
        }

        return result;
    }

    // A player wins when all their tokens are home; a team wins when both partners' are.
    public bool HasWon(string color)
    {
        return _board.AllTokens
            .Where(t => _config.SameTeam(color, t.PlayerColor))
            .All(t => t.IsFinished);
    }

    public string WinTitle(string color)
    {
        if (!_config.IsTeams) return $"{color} wins!";

        var pair = new List<string> { color, GameConfig.PartnerOf(color) };
        pair.Sort((a, b) => Array.IndexOf(GameConfig.Order, a).CompareTo(Array.IndexOf(GameConfig.Order, b)));
        return $"{pair[0]} & {pair[1]} win!";
    }

    public static string Ordinal(int n)
    {
        return n switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{n}th"
        };
    }
}
