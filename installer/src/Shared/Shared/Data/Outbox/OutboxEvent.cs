using System.Text.Json.Serialization;

namespace Shared.Data.Outbox;

public enum OutboxStatus
{
    Pending = 0,
    Dispatched = 1,
    Failed = 2
}

public class OutboxEvent
{
    // 👇 1) Then identifiers / status / error
    [JsonPropertyOrder(-10)]
    public Guid Id { get; set; } = Guid.NewGuid();

    // 👇 2) Put the "human" field first
    [JsonPropertyOrder(-20)]
    public string StatusName => Status.ToString();

    [JsonPropertyOrder(-9)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    [JsonPropertyOrder(-8)]
    public int RetryCount { get; set; }

    [JsonPropertyOrder(-7)]
    public string? Error { get; set; }

    // 👇 3) Then meta and payload
    [JsonPropertyOrder(0)]
    public string ModuleName { get; set; } = default!;

    [JsonPropertyOrder(1)]
    public string EventType { get; set; } = default!; // Assembly-qualified type name

    [JsonPropertyOrder(2)]
    public string Payload { get; set; } = default!;   // JSON

    [JsonPropertyOrder(3)]
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;
}