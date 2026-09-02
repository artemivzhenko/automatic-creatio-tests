namespace CreatioAutoTests.Frontend;

/// <summary>
/// Capability interfaces. A command declares the capability it needs instead of listing concrete
/// element classes, so a new element type becomes usable by every matching command for free.
/// </summary>

/// <summary>The element can report whether it is present in the DOM.</summary>
public interface IExistable : IUiElement
{
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

/// <summary>The element renders a caption that can be compared with the configured label.</summary>
public interface ILabeled : IUiElement
{
    Task<string?> GetActualLabelAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element can be clicked: buttons, tabs.</summary>
public interface IClickable : IUiElement
{
    Task ClickAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element holds a value that can be emptied.</summary>
public interface IClearable : IUiElement
{
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>The element exposes required / read-only state that the DSL can assert.</summary>
public interface IStateAware : IUiElement
{
    Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default);

    Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The element holds a value that the DSL reads and writes as text.
/// This is the only contract 'set', 'check-value' and 'clear' need, which is what keeps
/// type switches out of the command layer.
/// </summary>
public interface IValueElement : IUiElement
{
    Task<string?> GetValueAsStringAsync(CancellationToken cancellationToken = default);

    Task SetValueFromStringAsync(string value, CancellationToken cancellationToken = default);
}

/// <summary>Strongly typed access on top of <see cref="IValueElement"/> for callers that want it.</summary>
public interface ITypedValue<T> : IValueElement
{
    Task<T?> GetValueAsync(CancellationToken cancellationToken = default);

    Task SetValueAsync(T value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Value kind used by <see cref="Validation.ValueCompatibilityPolicy"/> to decide, statically,
/// whether 'set target source' is a legal copy.
/// </summary>
public enum ValueKind
{
    None = 0,
    Text = 1,
    Number = 2,
    Boolean = 3,
    DateTime = 4,
    Lookup = 5,
    Stage = 6
}

/// <summary>The element renders a link to another record and can follow it. Used by 'goto'.</summary>
public interface INavigableToRecord : IUiElement
{
    Task NavigateToRecordAsync(CancellationToken cancellationToken = default);
}
