namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Enumerates the possible results of validating an SOE packet.
/// </summary>
public enum SoePacketValidationResult
{
    Valid = 0,
    TooShort = 1,
    CrcMismatch = 2,
    InvalidOpCode = 3
}
