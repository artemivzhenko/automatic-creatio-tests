using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;
using Microsoft.Playwright;

namespace CreatioAutoTests.Execution;

/// <summary>
/// The seam between commands and everything stateful: the logged in user, the open page and the
/// factory that turns a resolved target into a live element. Commands never touch Playwright
/// directly except through the page this exposes.
/// </summary>
public interface IExecutionSession
{
    /// <summary>Runtime timeouts and failure-continuation settings that commands honor.</summary>
    RunSettings Settings { get; }

    /// <summary>Current static page and element catalog from which commands materialize targets.</summary>
    ElementCatalog Catalog { get; }

    /// <summary>Named scalar values available for command argument expansion.</summary>
    IReadOnlyDictionary<string, string> StaticValues { get; }

    /// <summary>Configured business-object workflows used by the <c>create</c> command.</summary>
    IReadOnlyDictionary<string, ObjectDefinition> Objects { get; }

    /// <summary>User selected by the last 'use', or null before it runs.</summary>
    string? CurrentUser { get; }

    /// <summary>Page opened by the last 'open' or 'create', or null before that.</summary>
    string? CurrentPage { get; }

    /// <summary>Switches the active user. Sessions are cached, so repeat calls are cheap.</summary>
    Task UseUserAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a configured page, optionally an existing record, and waits for Creatio to settle.
    /// </summary>
    Task OpenPageAsync(string pageName, string? recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebinds the current browser page to another configured page without navigating. Used after
    /// Creatio itself navigates, for example when the New button opens a form.
    /// </summary>
    Task AdoptPageAsync(string pageName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Navigates to a raw path under the base URL, without it having to be a configured page.
    /// Used by 'analyse', which exists precisely to produce that configuration.
    /// </summary>
    Task NavigateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The open Playwright page, or an error explaining that nothing is open yet.</summary>
    IPage RequirePage();

    /// <summary>Creates the live element for a target the validator resolved earlier.</summary>
    IUiElement Materialize(ResolvedTarget target);

    /// <summary>Processes a script may start, keyed by schema name.</summary>
    IReadOnlyDictionary<string, ProcessDefinition> Processes { get; }

    /// <summary>What the last 'start' produced, or null before one ran.</summary>
    Session.ProcessRunResult? LastProcess { get; }

    /// <summary>
    /// Starts a configured process over the process engine service, asking for every output
    /// parameter back, and remembers the outcome for 'check-result'. Needs no page.
    /// </summary>
    Task<Session.ProcessRunResult> StartProcessAsync(
        string process,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);

    /// <summary>Whether at least one record of the object matches the OData filter.</summary>
    Task<bool> RecordExistsAsync(string entity, string filter, CancellationToken cancellationToken = default);
}
