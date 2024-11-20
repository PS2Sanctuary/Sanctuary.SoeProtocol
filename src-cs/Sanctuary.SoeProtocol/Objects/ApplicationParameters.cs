using System;
using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used by an application to control the underlying SOE session.
/// </summary>
[SkipLocalsInit]
public class ApplicationParameters
{
    private bool _isEncryptionEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether encryption is enabled
    /// for the session.
    /// </summary>
    public bool IsEncryptionEnabled
    {
        get => _isEncryptionEnabled;
        set
        {
            if (value && EncryptionKeyState is null)
                throw new InvalidOperationException("Cannot enable encryption when the key state is null");

            _isEncryptionEnabled = value;
        }
    }

    /// <summary>
    /// Gets the encryption key state to use with this session.
    /// </summary>
    public Rc4KeyState? EncryptionKeyState { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationParameters"/> class.
    /// </summary>
    /// <param name="encryptionKeyState">The initial encryption key state to use.</param>
    public ApplicationParameters(Rc4KeyState? encryptionKeyState)
    {
        EncryptionKeyState = encryptionKeyState;
        IsEncryptionEnabled = false;
    }
}
