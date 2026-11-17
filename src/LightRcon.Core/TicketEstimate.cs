namespace LightRcon.Core;

public static class TicketEstimate
{
    // Mode defaults are a preview, not map-specific authoritative server values.
    public static int? DefaultBase(string mode) => mode switch
    {
        "ConquestLarge0" => 800,
        "ConquestSmall0" => 400,
        "RushLarge0" => 100,
        "TeamDeathMatch0" or "TeamDeathMatch1" => 100,
        "Domination0" => 300,
        "SquadDeathMatch0" or "SquadDeathMatch1" => 50,
        _ => null
    };
    public static decimal Calculate(decimal percent, decimal baseline) => checked(percent / 100m * baseline);
}
