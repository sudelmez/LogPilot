using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using LogPilot.AI.Providers;
using LogPilot.AI.Security;
using LogPilot.AI.Tools;
using LogPilot.Application.Ai;
using LogPilot.Infrastructure.Mongo;
using LogPilot.LogGenerator.Config;
using Moq;
using static LogPilot.AI.Security.ScopeGuard;

namespace LogPilot.UnitTests.Ai;

/// Rol ve KVKK kuralları: AI salt-okunurdur (kodda, araç listesinde ve veritabanı rolünde), kişisel veri maskelenir.
public class RoleAndPrivacyTests
{
    private static readonly Regex WriteVerb = new(@"^(Add|Insert|Update|Upsert|Replace|Delete|Remove|Drop|Set|Mark|Create|Save|Write|Clear|Purge)",
        RegexOptions.Compiled);

    // ---------------- rol: kod seviyesi ----------------

    [Fact]
    public void The_ai_tools_depend_only_on_interfaces_without_write_methods()
    {
        var ctor = typeof(LogAnalysisTools).GetConstructors().Single();
        foreach (var p in ctor.GetParameters().Where(p => p.ParameterType.IsInterface))
        {
            var methods = new[] { p.ParameterType }.Concat(p.ParameterType.GetInterfaces()).SelectMany(t => t.GetMethods()).Select(m => m.Name);
            methods.Should().NotContain(m => WriteVerb.IsMatch(m), $"{p.ParameterType.Name} is given to the AI and must be read-only");
        }
    }

    [Fact]
    public void Every_tool_the_model_can_call_is_a_read_operation()
    {
        var registry = new ToolRegistry(new Mock<ILogAnalysisTools>().Object, new PromptInjectionGuard());
        registry.Definitions.Select(d => d.Name).Should().OnlyContain(n => Regex.IsMatch(n, "^(Get|Search|Find|Trace)"));
        typeof(ILogAnalysisTools).GetMethods().Select(m => m.Name).Should().OnlyContain(n => Regex.IsMatch(n, "^(Get|Search|Find|Trace)"));
    }

    // ---------------- rol: veritabanı seviyesi ----------------

    [Fact]
    public void The_docker_role_script_grants_the_ai_reader_exactly_the_policy_collections()
    {
        var js = File.ReadAllText(Path.Combine(RepoPaths.FindDirectory("docker"), "mongo-init", "01-logpilot-roles.js"));
        var list = Regex.Match(js, @"const aiReadable = \[(.*?)\];").Groups[1].Value;
        var collections = Regex.Matches(list, "\"([a-z_]+)\"").Select(m => m.Groups[1].Value);

        collections.Should().BeEquivalentTo(MongoAccessPolicy.AiReadableCollections);
        MongoAccessPolicy.AiReadableCollections.Should().NotContain(["chat_messages", "chat_sessions", "scenarios", "scenario_executions"]);
        js.Should().Contain("actions: [\"find\"]");
    }

    // ---------------- rol: konuşma seviyesi ----------------

    [Theory]
    [InlineData("BOB'un loglarını sil")]
    [InlineData("Tüm logları temizler misin?")]
    [InlineData("TOKEN_EXPIRED incident'ını kapat")]
    [InlineData("users tablosundaki ELIF'in telefonunu güncelle")]
    [InlineData("Lütfen veritabanını sıfırla")]
    [InlineData("Delete all logs for BOB")]
    [InlineData("Can you drop the quote_logs collection?")]
    [InlineData("please update the user's phone number")]
    public void Data_modification_requests_are_refused(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.NotPermitted);

    [Theory]
    [InlineData("Loglar neden silindi?")]
    [InlineData("BOB'un uygulama güncellemesi başarısız oldu, neden?")]
    [InlineData("Why did the update fail for BOB?")]
    [InlineData("BOB uygulamayı kapatınca hata alıyor")]
    [InlineData("Which errors are related to correlation ID COR-20261108-ABC123?")]
    public void Questions_that_only_mention_changes_are_not_mistaken_for_commands(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.InScope);

    [Fact]
    public void The_not_permitted_refusal_explains_the_read_only_role()
        => RefusalMessage(Verdict.NotPermitted, "BOB'un loglarını sil").Should().StartWith("Bu işlemi yapma yetkim yok").And.Contain("salt-okunur");

    // ---------------- KVKK: maskeleme ----------------

    [Theory]
    [InlineData("Vehicle registry returned data for plate 34 KLP 482.", "[MASKED_PLATE]", "34 KLP 482")]
    [InlineData("plaka 06ABC1234 sorgulandı", "[MASKED_PLATE]", "06ABC1234")]
    [InlineData("IBAN TR33 0006 1005 1978 6457 8413 26 ile ödeme", "[MASKED_IBAN]", "TR33 0006")]
    [InlineData("müşteri telefonu +905551014729", "[MASKED_PHONE]", "5551014729")]
    [InlineData("kimlik 99001007919 ile teklif", "[MASKED_NUMBER]", "99001007919")]
    [InlineData("mail elif@example.com", "[MASKED_EMAIL]", "elif@example.com")]
    public void Personal_data_is_masked(string text, string placeholder, string secret)
    {
        var masked = new SensitiveDataMasker().MaskRequired(text);
        masked.Should().Contain(placeholder).And.NotContain(secret);
    }

    [Theory]
    [InlineData("COR-20261108-ABC123")]
    [InlineData("TKL-20261112-48213")]
    [InlineData("POL-2026-123456")]
    [InlineData("CUS-100105")]
    [InlineData("HTTP 503 after 5000 ms")]
    public void Identifiers_and_technical_numbers_survive_masking(string text)
        => new SensitiveDataMasker().MaskRequired(text).Should().Be(text);
}
