namespace CreatioAutoTests.Frontend;

/// <summary>
/// Capability interfaces. A command declares the capability it needs instead of listing concrete
/// element classes, so a new element type becomes usable by every matching command for free.
/// </summary>

/// <summary>The element can report whether it is present in the DOM.</summary>
public interface IExistable : IUiElement
{
    /// <summary>Checks presence for normal commands, using the configured element timeout.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for the element with an explicit budget.
    ///
    /// Absence is the expected answer for a negative assertion and for a diagnostic sweep, and the
    /// full element timeout is the worst possible price to pay for it: a page whose controls are
    /// spread over tabs would otherwise cost 15 s for every control that is not on the open tab.
    /// </summary>
    Task<bool> ExistsAsync(int timeoutMs, CancellationToken cancellationToken = default);
}

/// <summary>
/// The element can wait for itself to be shown or not shown. A business rule toggles a control, and in
/// Freedom UI a hidden control is absent from the DOM, so the answer is awaited, never sampled.
/// Used by <c>check X is [not] shown</c>.
/// </summary>
public interface IVisibilityAware : IUiElement
{
    /// <summary>Waits up to <paramref name="timeoutMs"/> for the shown state to equal <paramref name="expected"/>; false on timeout.</summary>
    /// <param name="expected">True to wait for the element to be present and laid out; false for absent or not displayed.</param>
    /// <param name="timeoutMs">Wait budget in milliseconds.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task<bool> IsShownAsync(bool expected, int timeoutMs, CancellationToken cancellationToken = default);
}

/// <summary>The element renders a caption that can be compared with the configured label.</summary>
public interface ILabeled : IUiElement
{
    /// <summary>Reads the caption that <c>check X exists</c> compares with configuration.</summary>
    Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element can be clicked: buttons, tabs.</summary>
public interface IClickable : IUiElement
{
    /// <summary>Performs the interaction required by the <c>click</c> command.</summary>
    Task ClickAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element holds a value that can be emptied.</summary>
public interface IClearable : IUiElement
{
    /// <summary>Removes the held value for the <c>clear</c> command.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element exposes required / read-only state that the DSL can assert.</summary>
public interface IStateAware : IUiElement
{
    /// <summary>Reports the required state used by <c>check X is [not] required</c>.</summary>
    Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the read-only state used by <c>check X is [not] readonly</c>.</summary>
    Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The element holds a value that the DSL reads and writes as text.
/// This is the only contract 'set', 'check-value' and 'clear' need, which is what keeps
/// type switches out of the command layer.
/// </summary>
public interface IValueElement : IUiElement
{
    /// <summary>Reads a value in the text form shared by <c>set</c> and <c>check-value</c>.</summary>
    Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a text value without exposing the concrete field implementation.</summary>
    Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default);
}

/// <summary>Strongly typed access on top of <see cref="IValueElement"/> for callers that want it.</summary>
public interface ITypedValue<T> : IValueElement
{
    /// <summary>Reads the field through the concrete value type when a caller needs it.</summary>
    Task<T?> GetValueAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a concrete value while retaining the string-based command contract.</summary>
    Task SetValueAsync(T value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Value kind used by <see cref="Validation.ValueCompatibilityPolicy"/> to decide, statically,
/// whether 'set target source' is a legal copy.
/// </summary>
public enum ValueKind
{
    /// <summary>An element with no transferable value, such as a button or tab.</summary>
    None = 0,
    /// <summary>Free-form textual content.</summary>
    Text = 1,
    /// <summary>An invariant numeric value.</summary>
    Number = 2,
    /// <summary>A <c>true</c> or <c>false</c> checkbox value.</summary>
    Boolean = 3,
    /// <summary>An ISO 8601 date, time, or date-time value.</summary>
    DateTime = 4,
    /// <summary>A displayed reference to another Creatio record.</summary>
    Lookup = 5,
    /// <summary>The selected stage of a progress bar.</summary>
    Stage = 6,
    /// <summary>The open tab of a toggle tab group.</summary>
    Tab = 7
}

/// <summary>The element renders a link to another record and can follow it. Used by 'goto'.</summary>
public interface INavigableToRecord : IUiElement
{
    /// <summary>Follows the linked record for the <c>goto</c> command.</summary>
    Task NavigateToRecordAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The element can report whether it accepts inline editing.
///
/// Distinct from <see cref="IStateAware"/>: read-only is a property of a field's value, while
/// editability is a property of a grid as a whole, configured independently of whether the grid sits
/// inside an expansion panel. Used by <c>check X is [not] editable</c>.
/// </summary>
public interface IEditableAware : IUiElement
{
    /// <summary>Reads whether inline editing is switched on for this element.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<bool> IsEditableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The element renders named columns whose captions and order a test can assert.
///
/// The expectation lives in configuration rather than in the script, the same way a caption does:
/// a column list is a property of the page, and repeating it in every case that touches the grid
/// would make a renamed column a change in many files instead of one.
/// Used by <c>check X columns</c>.
/// </summary>
public interface IColumned : IUiElement
{
    /// <summary>Column captions declared in configuration, in the expected left-to-right order.</summary>
    IReadOnlyList<string> ExpectedColumns { get; }

    /// <summary>Reads the column captions the element currently renders, in display order.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<string>> GetColumnsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The element lists records as rows with named columns and can find one by its cell values.
/// Used by <c>check-row</c>, <c>select-row</c> and <c>delete-row</c>: a row is named by
/// <c>Column=Value;Column=Value</c> on the rendered captions, never by position.
/// </summary>
public interface IRowed : IUiElement
{
    /// <summary>Whether a rendered row has every named cell equal to the given text.</summary>
    Task<bool> HasRowAsync(IReadOnlyList<Execution.RecordAssignment> cells, CancellationToken cancellationToken = default);

    /// <summary>Clicks the matching row, making it the selected one.</summary>
    Task SelectRowAsync(IReadOnlyList<Execution.RecordAssignment> cells, CancellationToken cancellationToken = default);

    /// <summary>Deletes the matching row through the element's own delete action, confirming when asked.</summary>
    Task DeleteRowAsync(IReadOnlyList<Execution.RecordAssignment> cells, CancellationToken cancellationToken = default);
}

/// <summary>
/// The element can gain a new record from inside the page, without navigating away.
///
/// A Creatio grid that allows inline editing adds a row in place and takes its values cell by cell,
/// which is what <c>add-record</c> drives. Whether the row then reaches the database follows the
/// page's own save, exactly as it does for a value written with <c>set</c>.
/// </summary>
public interface IRecordAddable : IUiElement
{
    /// <summary>Adds one record and fills the named columns.</summary>
    /// <param name="assignments">Columns to fill, in the order they should be entered.</param>
    /// <param name="cancellationToken">Cancels the addition.</param>
    Task AddRecordAsync(
        IReadOnlyList<Execution.RecordAssignment> assignments,
        CancellationToken cancellationToken = default);
}
