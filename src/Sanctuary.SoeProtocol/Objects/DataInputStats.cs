namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains statistic values related to data input.
/// </summary>
public class DataInputStats
{
    /// <summary>
    /// The total number of reliable data packets received, including duplicates.
    /// </summary>
    public int TotalReceived { get; set; }

    /// <summary>
    /// The number of duplicate reliable data packets received.
    /// </summary>
    public int DuplicateCount { get; set; }

    /// <summary>
    /// The number of reliable data packets that were received out of order.
    /// </summary>
    public int OutOfOrderCount { get; set; }

    /// <summary>
    /// The total number of bytes received.
    /// </summary>
    /// <remarks>
    /// This value only includes the raw data count (i.e not multi-data indicators, encryption padding, etc).
    /// </remarks>
    public long TotalReceivedBytes { get; set; }

    /// <summary>
    /// The number of reliable data packets that were acknowledged.
    /// </summary>
    public int AcknowledgeCount { get; set; }
}
