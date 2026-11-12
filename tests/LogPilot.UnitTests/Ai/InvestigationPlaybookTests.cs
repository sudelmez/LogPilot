using AwesomeAssertions;
using LogPilot.AI.Prompts;
using LogPilot.AI.Security;
using LogPilot.AI.Tools;
using LogPilot.Application.Ai;
using Moq;

namespace LogPilot.UnitTests.Ai;

/// "Hangi soruda nereye bakılır" kayıt defterinin tutarlılığı: her playbook kendi örneklerini tanır,
/// andığı araç ve tablolar gerçekten vardır, prompt hepsini içerir.
public class InvestigationPlaybookTests
{
    private static readonly ToolRegistry Registry = new(new Mock<ILogAnalysisTools>().Object, new PromptInjectionGuard());

    public static TheoryData<string, string> Examples()
    {
        var data = new TheoryData<string, string>();
        foreach (var p in InvestigationPlaybooks.All)
        foreach (var example in p.Examples) data.Add(p.Id, example);
        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_example_question_is_routed_to_its_own_playbook(string playbookId, string question)
        => InvestigationPlaybooks.Classify(question)!.Id.Should().Be(playbookId, $"\"{question}\" is an example of {playbookId}");

    [Fact]
    public void Playbooks_only_mention_tools_and_collections_that_exist()
    {
        var tools = Registry.Definitions.Select(d => d.Name).ToHashSet();
        var sources = InvestigationPlaybooks.Sources.Select(s => s.Name).ToHashSet();

        foreach (var p in InvestigationPlaybooks.All)
        {
            p.Tools.Should().OnlyContain(t => tools.Contains(t), $"{p.Id} names an unknown tool");
            p.Path.Should().OnlyContain(s => sources.Contains(s), $"{p.Id} names an unknown collection");
        }
        InvestigationPlaybooks.Sources.SelectMany(s => s.Tools).Should().OnlyContain(t => tools.Contains(t));
        InvestigationPlaybooks.Links.Should().OnlyContain(l => sources.Contains(l.From) && sources.Contains(l.To));
    }

    [Fact]
    public void Every_tool_belongs_to_at_least_one_playbook_or_source()
    {
        var mentioned = InvestigationPlaybooks.All.SelectMany(p => p.Tools).Concat(InvestigationPlaybooks.Sources.SelectMany(s => s.Tools)).ToHashSet();
        var orphans = Registry.Definitions.Select(d => d.Name)
            .Where(n => !mentioned.Contains(n) && n is not ("GetIncidentDetails" or "SearchKnowledgeBase"));
        orphans.Should().BeEmpty("the model must be told when to use each tool");
    }

    [Fact]
    public void The_system_prompt_contains_the_whole_catalog()
    {
        var prompt = LogPilot.AI.Prompts.SystemPrompts.Chat(new ToolContext(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow), 8);

        foreach (var s in InvestigationPlaybooks.Sources) prompt.Should().Contain($"`{s.Collection}`");
        foreach (var p in InvestigationPlaybooks.All) prompt.Should().Contain(p.Id + " " + p.Title);
        prompt.Should().Contain("quote_logs → users via identityNo → phone");
    }

    [Theory]
    [InlineData("Why can't BOB log in?", "P1")]
    [InlineData("TKL-20261112-48213 teklifinin SMS'i gitti mi?", "P6")]   // teklif no + SMS ⇒ SMS zinciri kazanır
    [InlineData("TKL-20261112-48213 neden düştü? teklif", "P3")]
    [InlineData("KARL acente teklifleri", "P5")]
    [InlineData("BOB", "P8")]
    [InlineData("Hello", null)]
    public void Ambiguous_questions_follow_the_priority_rules(string question, string? expected)
        => InvestigationPlaybooks.Classify(question)?.Id.Should().Be(expected);
}
