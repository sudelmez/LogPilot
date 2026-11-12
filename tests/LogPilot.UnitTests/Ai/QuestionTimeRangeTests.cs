using AwesomeAssertions;
using LogPilot.Application.Ai;

namespace LogPilot.UnitTests.Ai;

public class QuestionTimeRangeTests
{
    // 2026-11-09 20:00 UTC = 23:00 İstanbul (UTC+3)
    private static readonly DateTime Now = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");

    private static QuestionRange? Parse(string q) => QuestionTimeRange.TryParse(q, Now, Istanbul);

    [Theory]
    [InlineData("What errors occurred in the last 15 minutes?", 15)]
    [InlineData("Which errors occurred in the past 30 mins?", 30)]
    [InlineData("Son 15 dakikada hangi hatalar oluştu?", 15)]
    [InlineData("son 10 dk içindeki hatalar", 10)]
    [InlineData("son on dakikadaki hatalar", 10)]
    [InlineData("Son yarım saatte ne oldu?", 30)]
    [InlineData("What happened in the last half hour?", 30)]
    [InlineData("What failed in the last hour?", 60)]
    [InlineData("son bir saatte TCLI neden giriş yapamadı", 60)]
    [InlineData("Son 2 saatteki MongoDB hataları", 120)]
    [InlineData("errors within the last 1.5 hours", 90)]
    [InlineData("son 3 gün", 3 * 24 * 60)]
    [InlineData("geçen hafta neler oldu", 7 * 24 * 60)]
    public void Relative_ranges_end_now(string question, int minutes)
    {
        var range = Parse(question);

        range.Should().NotBeNull();
        range!.To.Should().Be(Now);
        range.From.Should().Be(Now.AddMinutes(-minutes));
    }

    [Fact]
    public void Ranges_are_capped_at_seven_days()
    {
        Parse("last 30 days")!.From.Should().Be(Now.AddDays(-7));
    }

    [Fact]
    public void Today_starts_at_local_midnight()
    {
        var range = Parse("Which service has the highest error rate today?");

        range!.From.Should().Be(new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc));   // 9 Kasım 00:00 İstanbul
        range.To.Should().Be(Now);
        Parse("Bugün kritik incident var mı?")!.From.Should().Be(range.From);
    }

    [Fact]
    public void Yesterday_is_the_whole_previous_local_day()
    {
        var range = Parse("Dün hangi hatalar oldu?");

        range!.From.Should().Be(new DateTime(2026, 11, 7, 21, 0, 0, DateTimeKind.Utc));
        range.To.Should().Be(new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void The_matched_phrase_is_reported()
    {
        Parse("Son 15 dakikada hangi hatalar oluştu?")!.Label.Should().Be("Son 15 dakikada");
    }

    [Theory]
    [InlineData("Why can't TCLI log in to the mobile application?")]
    [InlineData("What was the last error for user BOB?")]
    [InlineData("Son hata neydi?")]
    [InlineData("Which errors are related to correlation ID COR-20261108-001ABC?")]
    [InlineData("Dünya genelinde etkilenen kullanıcı var mı?")]
    [InlineData("Is there an active MongoDB-related incident?")]
    [InlineData("")]
    public void Questions_without_an_explicit_time_keep_the_selected_range(string question)
    {
        Parse(question).Should().BeNull();
    }
}
