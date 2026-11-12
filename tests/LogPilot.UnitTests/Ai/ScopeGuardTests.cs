using AwesomeAssertions;
using LogPilot.AI.Prompts;
using LogPilot.AI.Security;
using static LogPilot.AI.Security.ScopeGuard;

namespace LogPilot.UnitTests.Ai;

/// Kapsam koruyucusu: alakasız ve argo soruları reddeder, ama GERÇEK bir log sorusunu asla reddetmez.
public class ScopeGuardTests
{
    public static TheoryData<string> PlaybookExamples()
    {
        var data = new TheoryData<string>();
        foreach (var e in InvestigationPlaybooks.All.SelectMany(p => p.Examples)) data.Add(e);
        return data;
    }

    [Theory]
    [MemberData(nameof(PlaybookExamples))]
    public void Every_playbook_example_passes_the_guard(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.InScope);

    [Theory]
    [InlineData("BOB sık sık hata alıyor, neden?")]                  // "sık" → "sik": argo sanılmamalı
    [InlineData("Müşteriden şikayet geldi, ELIF giriş yapamıyor")]   // "şikayet" → "sikayet"
    [InlineData("permission denied error for BOB")]                  // "mission" konu dışı kalıbı değil
    [InlineData("Kasko teklifi neden gelmiyor?")]                     // sigorta kelimesi + sorun fiili
    [InlineData("Which errors are related to correlation ID COR-20261108-ABC123?")]
    [InlineData("What errors occurred in the last 15 minutes?")]
    [InlineData("Son 1 saatte SMS'ler ulaştı mı?")]
    public void Real_log_questions_are_never_refused(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.InScope);

    [Theory]
    [InlineData("Can you help me?")]
    [InlineData("Merhaba")]
    [InlineData("Hello")]
    public void Greetings_and_vague_requests_are_left_to_the_model(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.Unclear);

    [Theory]
    [InlineData("Kasko nedir?")]
    [InlineData("Trafik sigortası ne kadar tutar?")]
    [InlineData("Hangi kaskoyu tavsiye edersin?")]
    [InlineData("En ucuz trafik sigortası hangisi?")]
    [InlineData("Şirketiniz hakkında bilgi verir misin?")]
    [InlineData("Şirketin CEO'su kim?")]
    [InlineData("Maaşlar ne zaman yatıyor?")]
    [InlineData("Bugün hava nasıl?")]
    [InlineData("Bana bir fıkra anlat")]
    [InlineData("Galatasaray maç sonucu ne oldu?")]
    [InlineData("What's the capital of France?")]
    [InlineData("Write code for a todo app")]
    public void Off_topic_questions_are_refused(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.OffTopic);

    [Theory]
    [InlineData("amk sistem yine çöktü")]              // alan sinyali olsa bile argo reddedilir
    [InlineData("s1kt1r git")]                         // leetspeak
    [InlineData("salaaak bot")]                        // harf tekrarı
    [InlineData("aptal mısın sen")]
    [InlineData("this is fucking broken, why can't BOB log in")]
    public void Abusive_messages_are_refused(string question)
        => Evaluate(question).Verdict.Should().Be(Verdict.Abusive);

    [Fact]
    public void The_refusal_speaks_the_language_of_the_question()
    {
        RefusalMessage(Verdict.OffTopic, "Kasko nedir?").Should().StartWith("Üzgünüm, bu konuda size yardımcı olamıyorum");
        RefusalMessage(Verdict.OffTopic, "What is the weather?").Should().StartWith("Sorry, I can't help with that");
        RefusalMessage(Verdict.Abusive, "salak bot").Should().Contain("yardımcı olamıyorum");
    }
}
