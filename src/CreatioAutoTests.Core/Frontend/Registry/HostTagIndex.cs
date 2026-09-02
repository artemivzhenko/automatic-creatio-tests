namespace CreatioAutoTests.Frontend.Registry;

/// <summary>
/// Maps a Freedom UI host tag back to the element types registered for it.
///
/// This is what makes page analysis extend itself: a new element class declares its tag on its
/// [UiElement] attribute and becomes recognisable on a page with no change here.
/// </summary>
public sealed class HostTagIndex
{
    private readonly Dictionary<string, List<UiElementRegistration>> _byTag;

    public HostTagIndex(UiElementRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _byTag = new Dictionary<string, List<UiElementRegistration>>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registry.Registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.HostTag))
            {
                continue;
            }

            if (!_byTag.TryGetValue(registration.HostTag, out var list))
            {
                list = new List<UiElementRegistration>();
                _byTag[registration.HostTag] = list;
            }

            list.Add(registration);
        }
    }

    /// <summary>Every tag worth querying the DOM for.</summary>
    public IReadOnlyCollection<string> Tags =>
        _byTag.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Types registered for a tag. More than one means the tag alone is ambiguous.</summary>
    public IReadOnlyList<UiElementRegistration> For(string hostTag) =>
        _byTag.TryGetValue(hostTag, out var list) ? list : Array.Empty<UiElementRegistration>();

    /// <summary>True when a tag maps to several types, so the value has to break the tie.</summary>
    public bool IsAmbiguous(string hostTag) => For(hostTag).Count > 1;
}
