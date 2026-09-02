using CreatioAutoTests.Configuration;

namespace CreatioAutoTests.Validation;

/// <summary>
/// An element reference resolved statically: which entry it is and, through the entry, which class
/// will realise it. The live instance is created later, once the page is open.
/// </summary>
public sealed record ResolvedTarget(string Code, CatalogEntry Entry)
{
    public ElementDefinition Definition => Entry.Definition;

    public Type ElementType => Entry.ElementType;

    public string Page => Entry.Page;

    public override string ToString() => Entry.ToString();
}

/// <summary>Outcome of validating one command.</summary>
public sealed class ValidationResult
{
    private static readonly IReadOnlyList<ResolvedTarget> NoTargets = Array.Empty<ResolvedTarget>();

    private ValidationResult(
        bool isValid,
        string? error,
        IReadOnlyList<ResolvedTarget> targets,
        string? nextPage,
        bool declaresUser)
    {
        IsValid = isValid;
        Error = error;
        Targets = targets;
        NextPage = nextPage;
        DeclaresUser = declaresUser;
    }

    public bool IsValid { get; }

    public string? Error { get; }

    /// <summary>Elements this command will act on, in the order the command expects them.</summary>
    public IReadOnlyList<ResolvedTarget> Targets { get; }

    /// <summary>
    /// Page the script is on after this command. Set by 'open' and 'create' so that later commands
    /// resolve their element codes against the right page while still being validated statically.
    /// </summary>
    public string? NextPage { get; }

    /// <summary>True for 'use', which is what satisfies the "a script starts with use" rule.</summary>
    public bool DeclaresUser { get; }

    public static ValidationResult Ok() => new(true, null, NoTargets, null, false);

    public static ValidationResult Ok(params ResolvedTarget[] targets) =>
        new(true, null, targets, null, false);

    public static ValidationResult OkOnPage(string nextPage, params ResolvedTarget[] targets) =>
        new(true, null, targets, nextPage, false);

    public static ValidationResult OkWithUser() => new(true, null, NoTargets, null, true);

    public static ValidationResult Fail(string error) => new(false, error, NoTargets, null, false);
}
