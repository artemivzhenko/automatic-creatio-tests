using CreatioAutoTests.Execution;

namespace CreatioAutoTests.Tests;

[TestFixture]
public class ScriptParserTests
{
    private readonly ScriptParser _parser = new();

    [Test]
    public void Blank_lines_and_comments_are_skipped_but_line_numbers_are_kept()
    {
        var commands = _parser.Parse(new[]
        {
            "# a comment",
            string.Empty,
            "use tester",
            "   ",
            "open TestPage"
        });

        Assert.That(commands.Select(c => c.LineNumber), Is.EqualTo(new[] { 3, 5 }));
        Assert.That(commands[0].Keyword, Is.EqualTo("use"));
        Assert.That(commands[1].Keyword, Is.EqualTo("open"));
    }

    [Test]
    public void A_quoted_argument_keeps_its_spaces_and_is_marked_as_quoted()
    {
        var command = _parser.Parse(new[] { "set Name \"Sample Name 123\"" }).Single();

        Assert.That(command.ArgumentCount, Is.EqualTo(2));
        Assert.That(command.ValueAt(1), Is.EqualTo("Sample Name 123"));
        Assert.That(command.Arguments[1].IsQuoted, Is.True);
    }

    [Test]
    public void An_unquoted_argument_is_not_marked_as_quoted()
    {
        var command = _parser.Parse(new[] { "set Notes Name" }).Single();

        Assert.That(command.Arguments[1].IsQuoted, Is.False);
        Assert.That(command.ValueAt(1), Is.EqualTo("Name"));
    }

    [Test]
    public void An_empty_quoted_argument_survives_tokenizing()
    {
        var command = _parser.Parse(new[] { "check-value Account eq \"\"" }).Single();

        Assert.That(command.ArgumentCount, Is.EqualTo(3));
        Assert.That(command.ValueAt(2), Is.EqualTo(string.Empty));
        Assert.That(command.Arguments[2].IsQuoted, Is.True);
    }
}
