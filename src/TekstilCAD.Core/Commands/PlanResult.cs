namespace TekstilCAD.Core.Commands;

/// <summary>What <see cref="CommandPlanner"/> answers: a <see cref="Commands.Plan"/> or a <see cref="Commands.Refusal"/>. Exactly one is set.</summary>
public sealed class PlanResult
{
    private PlanResult(Plan? plan, Refusal? refusal)
    {
        Plan = plan;
        Refusal = refusal;
    }

    /// <summary>The plan; null when the command was refused.</summary>
    public Plan? Plan { get; }

    /// <summary>The refusal, question or choice (R15b lists the matching pieces as buttons); null when there is a plan.</summary>
    public Refusal? Refusal { get; }

    public bool IsPlan => Plan is not null;

    /// <summary>The refusal code ("R03"), or null when there is a plan.</summary>
    public string? Code => Refusal?.Code;

    internal static PlanResult Of(Plan plan) => new(plan, null);

    internal static PlanResult Refused(Refusal refusal) => new(null, refusal);

    public override string ToString() => IsPlan ? Plan!.Preview : $"{Refusal!.Code}: {Refusal.Message}";
}
