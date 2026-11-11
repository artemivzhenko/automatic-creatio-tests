using CreatioAutoTests.Frontend;

namespace CreatioAutoTests.Session;

/// <summary>
/// Tells whether the browser stands on the page a case claims. 'on &lt;page&gt;' rebinds without navigating,
/// so a wrong claim used to surface lines later as an element that was "not found"; now the claim itself
/// fails, naming what it looked for.
/// </summary>
internal static class PageProbe
{
    /// <summary>How many elements are tried, and named in the failure.</summary>
    public const int Sample = 5;

    /// <summary>Per-element budget of one round: short, so every sampled element is tried before the budget ends.</summary>
    private const int RoundMs = 250;

    /// <summary>
    /// Polls the elements until one is present or <paramref name="budgetMs"/> is spent. True when nothing
    /// can be probed (a page file without elements cannot be contradicted).
    /// </summary>
    public static async Task<bool> AnyPresentAsync(IReadOnlyList<IExistable> elements, int budgetMs, CancellationToken cancellationToken)
    {
        if (elements.Count == 0)
        {
            return true;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(budgetMs);
        do
        {
            foreach (var element in elements)
            {
                if (await element.ExistsAsync(RoundMs, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }
}
