namespace HostAgent.Runtime.ServiceRuntime;

public static class IdempotencyKeyBuilder
{
    public const int MaxLength = 256;

    /// <summary>
    /// Format: {serviceKey}:{operation}:{instanceId}:{rawKey}
    /// </summary>
    public static string ForInstanceOperation(
        string serviceKey,
        string operation,
        Guid instanceId,
        string rawKey)
    {
        serviceKey = NormalizeToken(serviceKey, nameof(serviceKey));
        operation = NormalizeToken(operation, nameof(operation));

        if (instanceId == Guid.Empty) throw new ArgumentException("instanceId required", nameof(instanceId));

        rawKey = NormalizeRawKey(rawKey);

        var key = $"{serviceKey}:{operation}:{instanceId}:{rawKey}";
        EnsureMaxLength(key);

        return key;
    }

    /// <summary>
    /// Format: {serviceKey}:{operation}:stack:{stackId}:{rawKey}
    /// </summary>
    public static string ForStackOperation(
        string serviceKey,
        string operation,
        Guid stackId,
        string rawKey)
    {
        serviceKey = NormalizeToken(serviceKey, nameof(serviceKey));
        operation = NormalizeToken(operation, nameof(operation));

        if (stackId == Guid.Empty)
            throw new ArgumentException("stackId required", nameof(stackId));

        rawKey = NormalizeRawKey(rawKey);

        var key = $"{serviceKey}:{operation}:stack:{stackId}:{rawKey}";
        EnsureMaxLength(key);

        return key;
    }

    /// <summary>
    /// Convenience: if the caller already provides a fully-formed idempotency key, validate it.
    /// (We don't attempt to parse/normalize segments because callers may depend on their own shape.)
    /// </summary>
    public static string NormalizeKey(string key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) throw new ArgumentException("key required", nameof(key));
        EnsureMaxLength(key);
        return key;
    }

    // Optional: strongly suggest these to callers to avoid drift
    public static class Operations
    {
        public const string Provision = "provision";
        public const string Restart = "restart";
        public const string Stop = "stop";
        public const string Delete = "delete";
        public const string Upgrade = "upgrade";
    }

    private static string NormalizeToken(string value, string paramName)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) throw new ArgumentException($"{paramName} required", paramName);
        return value;
    }

    private static string NormalizeRawKey(string rawKey)
    {
        rawKey = (rawKey ?? "").Trim();
        if (rawKey.Length == 0) throw new ArgumentException("rawKey required", nameof(rawKey));

        // Prevent accidental segment injection
        rawKey = rawKey.Replace(":", "_");

        return rawKey;
    }

    private static void EnsureMaxLength(string key)
    {
        if (key.Length > MaxLength)
            throw new ArgumentException($"Idempotency key must be <= {MaxLength} chars (was {key.Length}).", nameof(key));
    }
}
