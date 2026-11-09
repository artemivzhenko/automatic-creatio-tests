using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class RowCommandValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
        _parser = new ScriptParser();

        var products = CatalogBuilder.Element("Products", "Detail", "OrderClassic", "Products");
        products.Extra = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["columns"] = System.Text.Json.JsonDocument.Parse("[\"Product\",\"Quantity\"]").RootElement,
        };

        _catalog = CatalogBuilder.BuildPages(
            new PageDefinition
            {
                Name = "OrderClassic", Url = "/0/Shell/#CardModuleV2/UsrOrder1Page", Type = "ClassicUI", Schema = "UsrOrder1Page",
                Elements = new List<ElementDefinition>
                {
                    products,
                    CatalogBuilder.Element("UsrName", "Text", "OrderClassic"),
                    CatalogBuilder.Element("AddProduct", "Button", "OrderClassic"),
                },
            },
            new PageDefinition
            {
                Name = "ProductClassic", Url = "/0/Shell/#CardModuleV2/UsrProductPage", Type = "ClassicUI", Schema = "UsrProductPage",
                Elements = new List<ElementDefinition> { CatalogBuilder.Element("UsrProductName", "Text", "ProductClassic") },
            });
    }

    private ExecutionPlan Validate(params string[] lines)
    {
        var context = new ValidationContext(_catalog, new Dictionary<string, string>(), new[] { "tester" }, new Dictionary<string, ObjectDefinition>());
        return _validator.Validate("rows", "rows.act", _parser.Parse(lines), context);
    }

    [Test]
    public void Row_commands_validate_against_a_detail_with_columns()
    {
        var plan = Validate(
            "use tester",
            "open OrderClassic for 1",
            "check-row Products Product=Pump;Quantity=2 exists",
            "check-row Products Product=Pump not exists",
            "select-row Products Product=Pump",
            "delete-row Products Product=Pump",
            "add-record Products set Product=Pump;Quantity=2");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void A_column_the_detail_does_not_declare_is_rejected_before_a_browser_starts()
    {
        var plan = Validate("use tester", "open OrderClassic for 1", "check-row Products Price=10 exists");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("'Price'"));
        Assert.That(plan.Issues[0].Message, Does.Contain("'Product'"));
    }

    [Test]
    public void Row_commands_refuse_an_element_without_rows()
    {
        var plan = Validate("use tester", "open OrderClassic for 1", "check-row UsrName Product=Pump exists");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("has no rows"));
    }

    [Test]
    public void Check_row_must_end_in_an_expectation()
    {
        var plan = Validate("use tester", "open OrderClassic for 1", "check-row Products Product=Pump Quantity=2");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("'exists' or 'not exists'"));
    }

    /// <summary>'on' moves the virtual page, so the codes of the page Creatio navigated to resolve on the lines after it.</summary>
    [Test]
    public void On_rebinds_later_lines_to_the_named_page()
    {
        var plan = Validate(
            "use tester",
            "open OrderClassic for 1",
            "click AddProduct",
            "on ProductClassic",
            "set UsrProductName \"Pump\"");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void On_rejects_an_unknown_page()
    {
        var plan = Validate("use tester", "open OrderClassic for 1", "on Nowhere");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("Nowhere"));
    }

    [Test]
    public void The_Classic_detail_offers_rows_and_in_place_records()
    {
        var registration = UiElementRegistry.Discover().Resolve(UiGeneration.ClassicUI, "Detail");

        Assert.That(registration.Capabilities, Does.Contain(typeof(IRowed)));
        Assert.That(registration.Capabilities, Does.Contain(typeof(IRecordAddable)));
    }
}
