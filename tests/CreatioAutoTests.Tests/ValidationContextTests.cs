using CreatioAutoTests.Configuration;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ValidationContextTests
{
    [Test]
    public void Resolution_requires_an_open_page_and_exact_element_code()
    {
        var context = CreateContext();
        Assert.That(context.TryResolve("Name", out _, out var unopened), Is.False);
        Assert.That(unopened, Does.Contain("no page is open"));

        SetCurrentPage(context, "testpage");
        Assert.That(context.TryResolve("Name", out _, out _), Is.True);
        Assert.That(context.TryResolve("name", out _, out var unknown), Is.False);
        Assert.That(unknown, Does.Contain("Name"));
    }

    [Test]
    public void Unknown_codes_list_the_codes_available_on_the_page()
    {
        var context = CreateContext();
        SetCurrentPage(context, "TestPage");

        Assert.That(context.TryResolve("Missing", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("Missing"));
        Assert.That(error, Does.Contain("Name"));
    }

    private static ValidationContext CreateContext() => new(
        CatalogBuilder.Default(), new Dictionary<string, string>(), Array.Empty<string>(),
        new Dictionary<string, ObjectDefinition>());

    private static void SetCurrentPage(ValidationContext context, string page)
    {
        typeof(ValidationContext)
            .GetProperty(nameof(ValidationContext.CurrentPage))!
            .SetValue(context, page);
    }
}