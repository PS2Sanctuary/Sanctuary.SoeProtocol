namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains statistical values related to data output.
/// </summary>
public class DataOutputStats
{
    /// <summary>
    /// The total number of reliable data packets sent, including re-sent packets.
    /// </summary>
    public int TotalSentReliablePackets { get; set; }

    /// <summary>
    /// The total number of reliable data packets that were re-sent.
    /// </summary>
    public int TotalResentReliablePackets { get; set; }

    /// <summary>
    /// The total number of received acknowledgement packets (incl. ack all).
    /// </summary>
    public int IncomingAcknowledgeCount { get; set; }

    /// <summary>
    /// The total number of reliable data packets that were acknowledged (i.e. including packets ack'ed by an ack-all).
    /// </summary>
    public int ActualAcknowledgeCount { get; set; }
}
