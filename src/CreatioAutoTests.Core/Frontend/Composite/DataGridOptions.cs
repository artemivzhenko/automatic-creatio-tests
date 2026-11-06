namespace CreatioAutoTests.Frontend.Composite;

/// <summary>
/// Extra members a data grid declares in its page file.
/// </summary>
public sealed class DataGridOptions
{
    /// <summary>
    /// Column captions expected in the grid, in left-to-right order.
    ///
    /// Filled in by <c>analyse</c> and asserted by <c>check X columns</c>. Only the columns a person
    /// sees are listed: the row-controls column and the trailing spacer Creatio adds to every grid
    /// are layout, not data, and naming them in a page file would be noise no test could act on.
    /// </summary>
    public string[] Columns { get; set; } = Array.Empty<string>();
}
