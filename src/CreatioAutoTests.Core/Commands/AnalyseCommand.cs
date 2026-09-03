using CreatioAutoTests.Analysis;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Validation.Validators;
using Microsoft.Extensions.Logging;

namespace CreatioAutoTests.Commands;

/// <summary>
/// Describes a page and writes its configuration file.
///
///   analyse                                   -- the page 'open' left us on
///   analyse "/0/Shell/#Card/Contacts_FormPage/edit/{id}"
///   analyse "/0/Shell/#Card/Contacts_FormPage/edit/{id}" ContactForm
///
/// Note what this command is for: it produces the configuration a later test run validates against.
/// Elements it discovers cannot be used further down the same script, because a script is validated
/// in full before any of it runs. Analyse in one case, test in another.
/// </summary>
[Command("analyse", Validator = typeof(AnalyseValidator),
    Usage = "analyse [\"<page path>\"] [<pageName>]")]
public sealed class AnalyseCommand : ICommand
{
    private readonly PageAnalyzer _analyzer;
    private readonly PageConfigWriter _writer;
    private readonly ConfigurationPaths _paths;

    /// <summary>Initializes page analysis and configuration-writing services for the command.</summary>
    public AnalyseCommand(PageAnalyzer analyzer, PageConfigWriter writer, ConfigurationPaths paths)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>Analyzes the open or specified page and writes its elements into the page configuration directory.</summary>
    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var session = context.Session;
        var address = context.Command.ArgumentCount > 0 ? context.ResolveValue(0) : null;

        string pageName;
        string url;
        string? recordId;

        if (address is null)
        {
            // No address: describe whatever 'open' left on screen.
            var openPage = session.CurrentPage
                           ?? throw new CommandFailedException("No page is open to analyse.");

            session.Catalog.TryGetPage(openPage, out var definition);
            pageName = openPage;
            url = definition?.Url ?? string.Empty;
            recordId = null;
        }
        else
        {
            var path = new PagePath(address);
            pageName = context.Command.ArgumentCount > 1 ? context.ResolveValue(1) : path.DefaultPageName;
            url = path.Url;
            recordId = path.RecordId;

            await session.NavigateAsync(path.Raw, cancellationToken).ConfigureAwait(false);
        }

        var analysis = await _analyzer
            .AnalyzeAsync(session.RequirePage(), pageName, url, recordId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = _writer.Write(analysis, _paths.PagesDirectory, keepMissing: true);

        Report(context, analysis, result);
    }

    private static void Report(CommandContext context, PageAnalysis analysis, PageConfigWriteResult result)
    {
        var logger = context.Logger;

        logger.LogInformation("analyse {Page}: {Count} control(s) -> {File}",
            analysis.PageName, result.Written, result.FilePath);

        foreach (var group in analysis.ByType)
        {
            logger.LogInformation("  {Count,3}  {Type}", group.Count(), group.Key);
        }

        var pinned = result.Pinned.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var element in analysis.Uncertain.Where(e => !pinned.Contains(e.Code)))
        {
            logger.LogWarning("  {Code}: {Reason}", element.Code, element.Uncertainty);
        }

        foreach (var element in analysis.PresenceOnly.Where(e => !pinned.Contains(e.Code)))
        {
            logger.LogInformation("  {Code}: {Reason}", element.Code, element.Uncertainty);
        }

        if (result.Pinned.Count > 0)
        {
            logger.LogInformation("  {Count} pinned entry(ies) left untouched: {Codes}",
                result.Pinned.Count, string.Join(", ", result.Pinned.Select(e => e.Code)));
        }

        if (result.NoLongerOnPage.Count > 0)
        {
            logger.LogWarning("  {Count} configured entry(ies) are no longer on the page and were kept: {Codes}",
                result.NoLongerOnPage.Count,
                string.Join(", ", result.NoLongerOnPage.Select(e => e.Code)));
        }

        foreach (var unknown in analysis.UnknownControls)
        {
            logger.LogInformation("  no element type yet: {Tag} {Code}", unknown.HostTag, unknown.Code);
        }
    }
}
