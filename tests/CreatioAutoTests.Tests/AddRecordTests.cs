using System.Text.Json;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Composite;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class RecordAssignmentTests
{
    [Test]
    public void A_list_is_read_in_the_order_it_was_written()
    {
        var parsed = RecordAssignments.TryParse(
            "Name=Jon;Full name=Jon Doe;Surname=Doe", out var assignments, out var error);

        Assert.That(parsed, Is.True, error);
        Assert.That(assignments.Select(a => a.Column), Is.EqualTo(new[] { "Name", "Full name", "Surname" }));
        Assert.That(assignments.Select(a => a.Value), Is.EqualTo(new[] { "Jon", "Jon Doe", "Doe" }));
    }

    /// <summary>Only the first '=' splits a pair, so a value is free to contain one.</summary>
    [Test]
    public void A_value_may_contain_the_value_separator()
    {
        RecordAssignments.TryParse("Formula=a=b", out var assignments, out _);

        Assert.That(assignments[0].Value, Is.EqualTo("a=b"));
    }

    [Test]
    public void Whitespace_around_a_name_or_value_is_dropped()
    {
        RecordAssignments.TryParse(" Name = Jon ; Surname = Doe ", out var assignments, out _);

        Assert.Multiple(() =>
        {
            Assert.That(assignments[0], Is.EqualTo(new RecordAssignment("Name", "Jon")));
            Assert.That(assignments[1], Is.EqualTo(new RecordAssignment("Surname", "Doe")));
        });
    }

    /// <summary>An empty value is a deliberate instruction to leave the column blank.</summary>
    [Test]
    public void A_column_may_be_assigned_an_empty_value()
    {
        var parsed = RecordAssignments.TryParse("Name=", out var assignments, out var error);

        Assert.That(parsed, Is.True, error);
        Assert.That(assignments[0].Value, Is.Empty);
    }

    [TestCase("", "no column assignments")]
    [TestCase("Name", "is not a 'Column=Value' pair")]
    [TestCase("=Jon", "names no column")]
    [TestCase("Name=Jon;Name=Bob", "assigned more than once")]
    public void A_malformed_list_says_what_is_wrong(string text, string expected)
    {
        var parsed = RecordAssignments.TryParse(text, out _, out var error);

        Assert.That(parsed, Is.False);
        Assert.That(error, Does.Contain(expected));
    }

    /// <summary>
    /// The tokenizer splits on whitespace, so a value containing a space arrives as several tokens.
    /// Rejoining them is what lets the list be written without quotes.
    /// </summary>
    [Test]
    public void Tokens_split_on_whitespace_are_rejoined_into_one_list()
    {
        var tokens = ScriptParser.Tokenize("add-record Grid set Name=Jon;Full name=Jon Doe").Skip(3);

        Assert.That(RecordAssignments.Rejoin(tokens), Is.EqualTo("Name=Jon;Full name=Jon Doe"));
    }

    [Test]
    public void A_quoted_list_is_read_the_same_way_as_an_unquoted_one()
    {
        var quoted = ScriptParser.Tokenize("add-record Grid set \"Name=Jon;Full name=Jon Doe\"").Skip(3);
        var plain = ScriptParser.Tokenize("add-record Grid set Name=Jon;Full name=Jon Doe").Skip(3);

        Assert.That(RecordAssignments.Rejoin(quoted), Is.EqualTo(RecordAssignments.Rejoin(plain)));
    }
}

[TestFixture]
public sealed class AddRecordRegistrationTests
{
    private readonly UiElementRegistry _elements = UiElementRegistry.Discover();
    private readonly CommandRegistry _commands = CommandRegistry.Discover();

    [Test]
    public void A_grid_can_take_a_record_and_a_field_cannot()
    {
        _commands.TryResolve("add-record", out var addRecord);

        Assert.Multiple(() =>
        {
            Assert.That(_elements.Resolve("DataGrid").Capabilities, Does.Contain(typeof(IRecordAddable)));
            Assert.That(addRecord.Accepts(typeof(DataGrid)), Is.True);
            Assert.That(addRecord.Accepts(typeof(Frontend.Fields.TextField)), Is.False);
        });
    }
}

[TestFixture]
public sealed class AddRecordValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        var contacts = CatalogBuilder.Element("ContactList", "DataGrid");
        contacts.Extra = Extra("{\"columns\":[\"Full name\",\"Job title\",\"Primary\"]}");

        _catalog = CatalogBuilder.Build(
            contacts,
            CatalogBuilder.Element("Undescribed", "DataGrid"),
            CatalogBuilder.Element("Name", "Text"));

        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private static Dictionary<string, JsonElement> Extra(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private ExecutionPlan Validate(string line)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string>(),
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate(
            "records",
            "records.act",
            _parser.Parse(new[] { "use tester", "open TestPage", line }),
            context);
    }

    [TestCase("add-record ContactList set Full name=Jon Doe")]
    [TestCase("add-record ContactList set Full name=Jon Doe;Job title=Developer")]
    [TestCase("add-record ContactList set \"Full name=Jon Doe;Primary=true\"")]
    [TestCase("add-record Undescribed set Anything=Goes")]
    public void Every_supported_form_validates(string line)
    {
        var plan = Validate(line);

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    /// <summary>
    /// A column the page file does not list is a typo the validation pass can catch, which is the
    /// whole point of describing the page before testing it.
    /// </summary>
    [Test]
    public void A_column_the_page_does_not_have_is_rejected_before_a_browser_opens()
    {
        var plan = Validate("add-record ContactList set Surname=Doe");

        Assert.That(plan.IsValid, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Issues[0].Message, Does.Contain("has no column 'Surname'"));
            Assert.That(plan.Issues[0].Message, Does.Contain("'Full name'"));
        });
    }

    [Test]
    public void An_element_that_takes_no_record_is_rejected()
    {
        var plan = Validate("add-record Name set Full name=Jon");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("cannot take a new record"));
    }

    [Test]
    public void The_set_keyword_is_required()
    {
        var plan = Validate("add-record ContactList with Full name=Jon");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("must be followed by 'set'"));
    }

    [Test]
    public void A_malformed_assignment_list_is_rejected()
    {
        var plan = Validate("add-record ContactList set FullName");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("is not a 'Column=Value' pair"));
    }

    [Test]
    public void The_values_are_required()
    {
        var plan = Validate("add-record ContactList set");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("'add-record' takes a code"));
    }
}
