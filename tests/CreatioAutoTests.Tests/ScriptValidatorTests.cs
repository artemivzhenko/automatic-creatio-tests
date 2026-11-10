using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public class ScriptValidatorTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        _catalog = CatalogBuilder.Default();
        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private ExecutionPlan Validate(params string[] lines)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string> { ["DefaultName"] = "Contoso" },
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate("case", "case.act", _parser.Parse(lines), context);
    }

    [Test]
    public void A_correct_script_produces_one_step_per_command()
    {
        var plan = Validate("use tester", "open TestPage", "set Name \"abc\"", "click SaveButton");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
        Assert.That(plan.Steps, Has.Count.EqualTo(4));
    }

    [Test]
    public void A_script_must_start_by_naming_its_user()
    {
        var plan = Validate("open TestPage");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("must start with 'use"));
    }

    [Test]
    public void An_element_cannot_be_addressed_before_a_page_is_open()
    {
        var plan = Validate("use tester", "set Name \"abc\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("no page is open"));
    }

    [Test]
    public void An_unknown_element_is_reported_with_the_codes_that_do_exist()
    {
        var plan = Validate("use tester", "open TestPage", "set Nmae \"abc\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].LineNumber, Is.EqualTo(3));
        Assert.That(plan.Issues[0].Message, Does.Contain("Nmae"));
        Assert.That(plan.Issues[0].Message, Does.Contain("Name"));
    }

    [Test]
    public void Every_problem_in_a_file_is_reported_in_one_pass()
    {
        var plan = Validate("use tester", "open TestPage", "set Nope \"a\"", "click AlsoNope");

        Assert.That(plan.Issues, Has.Count.EqualTo(2));
    }

    [Test]
    public void Clicking_something_that_is_not_clickable_is_caught_before_the_run()
    {
        var plan = Validate("use tester", "open TestPage", "click Name");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("IClickable"));
    }

    [Test]
    public void An_incompatible_copy_is_caught_before_the_run()
    {
        var plan = Validate("use tester", "open TestPage", "set Started Name");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("cannot be copied"));
    }

    [Test]
    public void An_ordering_operator_on_text_is_caught_before_the_run()
    {
        var plan = Validate("use tester", "open TestPage", "check-value Name gt \"5\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("does not apply"));
    }

    [Test]
    public void A_static_value_is_accepted_where_a_literal_is_expected()
    {
        var plan = Validate("use tester", "open TestPage", "set Name DefaultName");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void An_unknown_keyword_lists_the_commands_that_exist()
    {
        var plan = Validate("use tester", "opne TestPage");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("Unknown command"));
        Assert.That(plan.Issues[0].Message, Does.Contain("open"));
    }

    [Test]
    public void Tab_warnings_follow_tab_switches_and_do_not_block_validation()
    {
        var field = CatalogBuilder.Element("Name", "Text");
        field.Tab = "GeneralInfoTab";
        _catalog = CatalogBuilder.Build(field,
            CatalogBuilder.Element("GeneralInfoTab", "Tab", label: "General"),
            CatalogBuilder.Element("OtherTab", "Tab", label: "Other"));

        var plan = Validate("use tester", "open TestPage", "set Name \"one\"",
            "click GeneralInfoTab", "set Name \"two\"", "click OtherTab", "set Name \"three\"");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
        Assert.That(plan.Warnings.Select(w => w.LineNumber), Is.EqualTo(new[] { 3, 7 }));
        Assert.That(plan.Warnings[0].Message, Does.Contain("Name lies on tab GeneralInfoTab"));
    }

    [Test]
    public void Warnings_are_retained_even_when_another_line_is_invalid()
    {
        var field = CatalogBuilder.Element("Name", "Text");
        field.Tab = "GeneralInfoTab";
        _catalog = CatalogBuilder.Build(field, CatalogBuilder.Element("GeneralInfoTab", "Tab"));
        var plan = Validate("use tester", "open TestPage", "set Name \"one\"", "set Missing \"two\"");
        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Warnings, Has.Count.EqualTo(1));
        Assert.That(plan.Issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void Classic_tab_panel_caption_opens_the_corresponding_tab()
    {
        var field = CatalogBuilder.Element("Name", "Text");
        field.Tab = "GeneralInfoTab";
        _catalog = CatalogBuilder.BuildPages(new PageDefinition
        {
            Name = "TestPage", Url = "/0/Shell/#CardModuleV2/UsrPage", Type = "ClassicUI", Schema = "UsrPage",
            Elements = new List<ElementDefinition>
            {
                field, CatalogBuilder.Element("GeneralInfoTab", "Tab", label: "General"),
                CatalogBuilder.Element("OtherTab", "Tab", label: "Other"),
                CatalogBuilder.Element("Tabs", "TabPanel"),
            },
        });

        var plan = Validate("use tester", "open TestPage", "set Tabs \"General\"", "set Name \"one\"",
            "set Tabs \"Other\"", "set Name \"two\"", "open TestPage", "set Name \"three\"");
        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
        Assert.That(plan.Warnings.Select(w => w.LineNumber), Is.EqualTo(new[] { 6, 8 }));
    }
}
