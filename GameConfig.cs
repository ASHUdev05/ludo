using System.Collections.Generic;
using System.Linq;

public enum SeatType { Off, Human, Bot }

// How many players and whether they play in teams
public enum GameMode { TwoPlayers, ThreePlayers, FourPlayers, Teams }

// Which rules are used
public enum RuleSet
{
    Classic,     // standard Ludo
    ThreeSixes,  // three 6s in a row void the turn and undo that streak's moves
    Deathmatch,  // every token starts out on the start square
    Supermatch   // all tokens out + speed multipliers
}

// Plain data describing how a game is set up. Not a Godot node.
public class GameConfig
{
    // Clockwise around the board
    public static readonly string[] Order = { "Red", "Green", "Yellow", "Blue" };

    // Remembered between games (survives scene reloads)
    public static GameConfig Last;

    // Set before reloading the scene to start a rematch without showing the setup screen
    public static bool SkipSetup;

    public GameMode Mode = GameMode.FourPlayers;
    public RuleSet Rules = RuleSet.Classic;

    public Dictionary<string, SeatType> Seats = new()
    {
        { "Red", SeatType.Human },
        { "Green", SeatType.Human },
        { "Yellow", SeatType.Human },
        { "Blue", SeatType.Human }
    };

    public bool IsTeams => Mode == GameMode.Teams;

    // Deathmatch and Supermatch start with every token already on the board
    public bool AllTokensOut => Rules == RuleSet.Deathmatch || Rules == RuleSet.Supermatch;

    public int RequiredPlayers => Mode switch
    {
        GameMode.TwoPlayers => 2,
        GameMode.ThreePlayers => 3,
        _ => 4
    };

    public IEnumerable<string> ActiveColors =>
        Order.Where(c => Seats[c] != SeatType.Off);

    // Teams are the opposite seats: Red + Yellow vs Green + Blue
    public static string PartnerOf(string color) => color switch
    {
        "Red" => "Yellow",
        "Yellow" => "Red",
        "Green" => "Blue",
        _ => "Green"
    };

    public bool SameTeam(string a, string b)
    {
        return a == b || (IsTeams && PartnerOf(a) == b);
    }

    public GameConfig Clone()
    {
        var copy = new GameConfig { Mode = Mode, Rules = Rules };
        foreach (var pair in Seats)
            copy.Seats[pair.Key] = pair.Value;
        return copy;
    }
}
