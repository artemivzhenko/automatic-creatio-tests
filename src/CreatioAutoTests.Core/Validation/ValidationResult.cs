using CreatioAutoTests.Configuration;

namespace CreatioAutoTests.Validation;

/// <summary>
/// An element reference resolved statically: which entry it is and, through the entry, which class
/// will realise it. The live instance is created later, once the page is open.
/// </summary>
public sealed record ResolvedTarget(string Code, CatalogEntry Entry)
{
    /// <summary>Configured element definition that will later be materialized on an open page.</summary>
    public ElementDefinition Definition => Entry.Definition;

    /// <summary>Concrete CLR element type chosen statically from the catalog entry.</summary>
    public Type ElementType => Entry.ElementType;

    /// <summary>Configured page against which this target was resolved.</summary>
    public string Page => Entry.Page;

    /// <summary>Formats the catalog entry for diagnostics.</summary>
    public override string ToString() => Entry.ToString();
}

/// <summary>
/// What a command does to the case's <c>$name</c> table, seen statically: it defines a name (an insert alias
/// or a remembered value), or deletes the record an alias names.
/// </summary>
/// <param name="Defines">The name the command sets, or null.</param>
/// <param name="Inserts">The name is the alias of a record this command inserts.</param>
/// <param name="Kept">The inserted record is marked <c>keep</c>: it outlives the case.</param>
/// <param name="Deletes">The alias whose record this command deletes, or null.</param>
public sealed record VariableEffect(string? Defines, bool Inserts = false, bool Kept = false, string? Deletes = null);

/// <summary>Outcome of validating one command.</summary>
public sealed class ValidationResult
{
    private static readonly IReadOnlyList<ResolvedTarget> NoTargets = Array.Empty<ResolvedTarget>();

    private ValidationResult(
        bool isValid,
        string? error,
        IReadOnlyList<ResolvedTarget> targets,
        string? nextPage,
        bool declaresUser,
        string? nextProcess = null)
    {
        IsValid = isValid;
        Error = error;
        Targets = targets;
        NextPage = nextPage;
        DeclaresUser = declaresUser;
        NextProcess = nextProcess;
    }

    /// <summary>
    /// Process the script has started after this command. Set by 'start' so that later
    /// 'check-result' lines are validated against that process's parameters.
    /// </summary>
    public string? NextProcess { get; }

    /// <summary>Creates a success that records the process later result checks read.</summary>
    public static ValidationResult OkWithProcess(string process) => new(true, null, NoTargets, null, false, process);

    /// <summary>Whether the command shape and all of its references passed static validation.</summary>
    public bool IsValid { get; }

    /// <summary>Author-facing validation explanation when <see cref="IsValid"/> is false.</summary>
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

    /// <summary>What the command does to the case's variables; null for most commands.</summary>
    public VariableEffect? Variable { get; private init; }

    /// <summary>The same success, also recording what the command does to the case's variables.</summary>
    public ValidationResult WithVariable(VariableEffect effect) =>
        new(IsValid, Error, Targets, NextPage, DeclaresUser, NextProcess) { Variable = effect };

    /// <summary>Creates a successful result for a targetless command.</summary>
    public static ValidationResult Ok() => new(true, null, NoTargets, null, false);

    /// <summary>Creates a successful result with the statically resolved command targets.</summary>
    public static ValidationResult Ok(params ResolvedTarget[] targets) =>
        new(true, null, targets, null, false);

    /// <summary>Creates a success that advances the virtual page used to resolve later lines.</summary>
    public static ValidationResult OkOnPage(string nextPage, params ResolvedTarget[] targets) =>
        new(true, null, targets, nextPage, false);

    /// <summary>Creates the successful user declaration that satisfies a script's first-line rule.</summary>
    public static ValidationResult OkWithUser() => new(true, null, NoTargets, null, true);

    /// <summary>Creates an invalid result with an author-facing explanation.</summary>
    public static ValidationResult Fail(string error) => new(false, error, NoTargets, null, false);
}
