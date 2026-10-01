using CLog.Knowledge;

namespace CLog.Tests;

public class KnownErrorsTests
{
    private const string TwoEntries =
        """
        # Known errors

        Introductory text for people, not an entry.

        ## Deadlock when saving an order
        **Symptom** System.InvalidOperationException at Contoso.Orders.OrderRepository.Save.
        **Cause** The sync job and the order API take the same tables in opposite order.
        **Solution** Restart the sync job and note the time. Fixed in #412.

        ## Payment provider timeout
        Retry after a minute; the provider recovers on its own.
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("# Known errors\n\nNothing has been understood yet.")]
    [InlineData("## Template — copy this for a new entry\n**Symptom**\nWhat shows up.\n**Solution**\nWhat to do.")]
    [InlineData("<!--\n## Deadlock when saving an order\n**Solution** Restart the sync job.\n-->")]
    public void A_file_without_real_entries_is_an_empty_knowledge_base(string? markdown)
    {
        Assert.Empty(KnownErrors.Parse(markdown));
    }

    [Fact]
    public void Each_heading_becomes_an_entry()
    {
        var entries = KnownErrors.Parse(TwoEntries);

        Assert.Equal(
            new[] { "Deadlock when saving an order", "Payment provider timeout" },
            entries.Select(e => e.Title));
        Assert.DoesNotContain("Introductory text", entries[0].Text);
        Assert.DoesNotContain("Payment provider", entries[0].Text);
    }

    [Fact]
    public void The_solution_is_what_follows_the_solution_marker()
    {
        var entry = KnownErrors.Parse(TwoEntries)[0];

        Assert.Equal("Restart the sync job and note the time. Fixed in #412.", entry.Solution);
    }

    [Fact]
    public void An_entry_without_sections_offers_its_whole_body_as_the_solution()
    {
        var entry = KnownErrors.Parse(TwoEntries)[1];

        Assert.Equal("Retry after a minute; the provider recovers on its own.", entry.Solution);
    }

    [Fact]
    public void Windows_line_endings_do_not_leak_into_titles_or_solutions()
    {
        var entry = Assert.Single(KnownErrors.Parse("## Deadlock\r\n**Solution**\r\nRestart the sync job.\r\n"));

        Assert.Equal("Deadlock", entry.Title);
        Assert.Equal("Restart the sync job.", entry.Solution);
    }
}
