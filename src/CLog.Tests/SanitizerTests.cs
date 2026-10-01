using CLog.Triage;

namespace CLog.Tests;

public class SanitizerTests
{
    [Theory]
    [InlineData("900101-1234")]          // 10 digits, hyphen
    [InlineData("9001011234")]           // 10 digits, no separator
    [InlineData("19900101-1234")]        // 12 digits, hyphen
    [InlineData("199001011234")]         // 12 digits, no separator
    [InlineData("400101+1234")]          // the plus form used past the age of 100
    public void Personal_identity_numbers_are_removed(string personalId)
    {
        var scrubbed = Sanitizer.Scrub($"Lookup failed for {personalId} in the register");

        Assert.DoesNotContain(personalId, scrubbed);
        Assert.Contains(Sanitizer.PersonalIdPlaceholder, scrubbed);
    }

    [Theory]
    [InlineData("anna.svensson@example.com")]
    [InlineData("bo+test@sub.example.co.uk")]
    [InlineData("NAMN@EXEMPEL.SE")]
    public void Email_addresses_are_removed(string email)
    {
        var scrubbed = Sanitizer.Scrub($"Could not deliver to {email} today");

        Assert.DoesNotContain(email, scrubbed);
        Assert.Contains(Sanitizer.EmailPlaceholder, scrubbed);
    }

    [Theory]
    [InlineData("""{"firstName": "Anna", "orderId": 7}""", "Anna")]
    [InlineData("""{"lastName": "Svensson"}""", "Svensson")]
    [InlineData("""{"name": "Anna Svensson"}""", "Anna Svensson")]
    [InlineData("""{"FirstName":"Anna"}""", "Anna")]
    public void Name_fields_in_json_are_redacted(string json, string name)
    {
        var scrubbed = Sanitizer.Scrub(json);

        Assert.DoesNotContain(name, scrubbed);
        Assert.Contains(Sanitizer.NamePlaceholder, scrubbed);
    }

    [Theory]
    [InlineData("firstName=Anna", "Anna")]
    [InlineData("lastName: Svensson", "Svensson")]
    [InlineData("name=Anna Svensson, orderId=7", "Anna")]
    public void Name_fields_in_key_value_text_are_redacted(string text, string name)
    {
        var scrubbed = Sanitizer.Scrub(text);

        Assert.DoesNotContain(name, scrubbed);
        Assert.Contains(Sanitizer.NamePlaceholder, scrubbed);
    }

    [Fact]
    public void Every_kind_of_personal_data_is_removed_from_one_message()
    {
        const string message =
            """
            Order failed: {"firstName": "Anna", "lastName": "Svensson", "email": "anna@example.com",
            "personalId": "900101-1234", "backup": "19801231-5678", "orderId": 4711}
            """;

        var scrubbed = Sanitizer.Scrub(message);

        Assert.DoesNotContain("Anna", scrubbed);
        Assert.DoesNotContain("Svensson", scrubbed);
        Assert.DoesNotContain("anna@example.com", scrubbed);
        Assert.DoesNotContain("900101-1234", scrubbed);
        Assert.DoesNotContain("19801231-5678", scrubbed);

        // What is not personal data survives, or the log stops being useful.
        Assert.Contains("4711", scrubbed);
        Assert.Contains("Order failed", scrubbed);
    }

    [Theory]
    [InlineData("orderId 4711")]
    [InlineData("processed 12 of 30 rows")]
    [InlineData("port 5341")]
    public void Ordinary_numbers_are_left_alone(string text)
    {
        Assert.Equal(text, Sanitizer.Scrub(text));
    }

    [Fact]
    public void A_longer_digit_run_is_not_mistaken_for_a_personal_id()
    {
        // 14 digits is not a personal identity number, and must not be chopped into one.
        const string text = "transaction 12345678901234 failed";

        Assert.Equal(text, Sanitizer.Scrub(text));
    }

    [Fact]
    public void Scrubbing_an_event_covers_message_exception_and_properties()
    {
        var logEvent = TestEvents.Error(
            template: "Order failed for {Email}",
            rendered: "Order failed for anna@example.com",
            exception: "System.Exception: lookup of 900101-1234 failed",
            properties: new Dictionary<string, string?>
            {
                ["firstName"] = "Anna",
                ["lastName"] = "Svensson",
                ["name"] = "Anna Svensson",
                ["Email"] = "anna@example.com",
                ["OrderId"] = "4711"
            });

        var scrubbed = Sanitizer.ScrubEvent(logEvent);

        Assert.DoesNotContain("anna@example.com", scrubbed.RenderedMessage);
        Assert.DoesNotContain("900101-1234", scrubbed.Exception);
        Assert.Equal(Sanitizer.NamePlaceholder, scrubbed.Properties["firstName"]);
        Assert.Equal(Sanitizer.NamePlaceholder, scrubbed.Properties["lastName"]);
        Assert.Equal(Sanitizer.NamePlaceholder, scrubbed.Properties["name"]);
        Assert.Equal(Sanitizer.EmailPlaceholder, scrubbed.Properties["Email"]);
        Assert.Equal("4711", scrubbed.Properties["OrderId"]);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("Name")]
    [InlineData("firstName")]
    [InlineData("first_name")]
    [InlineData("LASTNAME")]
    [InlineData("last_name")]
    public void Name_property_keys_are_recognised_whatever_their_spelling(string key)
    {
        Assert.True(Sanitizer.IsNameField(key));
    }

    [Theory]
    [InlineData("orderId")]
    [InlineData("hostname")]
    [InlineData("filename")]
    public void Other_property_keys_are_not_treated_as_names(string key)
    {
        Assert.False(Sanitizer.IsNameField(key));
    }

    [Fact]
    public void Null_and_empty_input_are_handled()
    {
        Assert.Equal("", Sanitizer.Scrub(null));
        Assert.Equal("", Sanitizer.Scrub(""));
    }
}
