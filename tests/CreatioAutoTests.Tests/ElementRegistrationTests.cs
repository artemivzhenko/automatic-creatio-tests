using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Fields;
using CreatioAutoTests.Frontend.Registry;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ElementRegistrationTests
{
    private readonly UiElementRegistry _registry = UiElementRegistry.Discover();

    [TestCase("Phone", typeof(PhoneField), ValueKind.Text)]
    [TestCase("Link", typeof(LinkField), ValueKind.Text)]
    [TestCase("Integer", typeof(IntegerField), ValueKind.Number)]
    [TestCase("Decimal", typeof(DecimalField), ValueKind.Number)]
    [TestCase("Date", typeof(DateField), ValueKind.DateTime)]
    [TestCase("Time", typeof(TimeField), ValueKind.DateTime)]
    [TestCase("Boolean", typeof(BooleanField), ValueKind.Boolean)]
    public void Remaining_field_types_are_discovered_with_their_value_kinds(
        string typeName,
        Type implementationType,
        ValueKind valueKind)
    {
        var registration = _registry.Resolve(typeName);

        Assert.That(registration.ImplementationType, Is.EqualTo(implementationType));
        Assert.That(registration.ValueKind, Is.EqualTo(valueKind));
    }

    [Test]
    public void The_production_element_configuration_builds_a_catalog()
    {
        var loader = new ConfigurationLoader(FindRepositoryRoot());
        var pages = loader.LoadPages("Configuration/pages");

        var catalog = ElementCatalog.Build(pages, _registry);

        Assert.That(catalog.Pages, Is.Not.Empty);
        Assert.That(catalog.Entries, Is.Not.Empty);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CreatioAutoTests.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test directory.");
    }
}