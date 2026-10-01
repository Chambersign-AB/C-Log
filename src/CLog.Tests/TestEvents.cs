using CLog.Model;

namespace CLog.Tests;

/// <summary>Builders for the log events the tests work with.</summary>
public static class TestEvents
{
    /// <summary>A realistic stack trace: framework frames on top, application frames below.</summary>
    public const string StackTrace =
        """
        System.InvalidOperationException: The order could not be saved
           at System.Data.SqlClient.SqlCommand.ExecuteNonQuery()
           at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReader(RelationalCommandParameterObject parameterObject)
           at Contoso.Orders.OrderRepository.Save(Order order) in C:/src/contoso/OrderRepository.cs:line 84
           at Contoso.Orders.OrderService.PlaceOrder(OrderRequest request) in C:/src/contoso/OrderService.cs:line 31
        """;

    public const string TopApplicationFrame = "Contoso.Orders.OrderRepository.Save(Order order)";

    public static SeqEvent Error(
        string id = "event-1",
        string template = "Order {OrderId} could not be saved for {Email}",
        string? rendered = null,
        string? exception = StackTrace,
        DateTimeOffset? timestamp = null,
        IReadOnlyDictionary<string, string?>? properties = null) =>
        new()
        {
            Id = id,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero),
            Level = "Error",
            MessageTemplate = template,
            RenderedMessage = rendered ?? template,
            Exception = exception,
            Properties = properties ?? new Dictionary<string, string?>()
        };

    /// <summary>An event with no exception attached, only a message.</summary>
    public static SeqEvent MessageOnly(string rendered, string template = "", string id = "event-1") =>
        Error(id: id, template: template, rendered: rendered, exception: null);
}
