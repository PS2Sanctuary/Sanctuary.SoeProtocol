using Sanctuary.SoeProtocol.Objects;
using System;
using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Provides a means for transforming data using the RC4 algorithm.
/// </summary>
[SkipLocalsInit]
public static class Rc4Cipher
{
    /// <summary>
    /// Transforms a buffer using an existing key state.
    /// </summary>
    /// <param name="inputBuffer">The buffer to encrypt.</param>
    /// <param name="outputBuffer">The output buffer. Must be at least as long as the <paramref name="inputBuffer"/>.</param>
    /// <param name="keyState">The key state to use.</param>
    public static void Transform(ReadOnlySpan<byte> inputBuffer, Span<byte> outputBuffer, ref Rc4KeyState keyState)
    {
        if (outputBuffer.Length < inputBuffer.Length)
            throw new ArgumentException("The output buffer must be at least as long as the input buffer.", nameof(outputBuffer));

        Span<byte> myKeyState = keyState.MutableKeyState;

        unchecked
        {
            for (int i = 0; i < inputBuffer.Length; i++)
            {
                IncrementKeyState(ref keyState);

                int xorIndex = (myKeyState[keyState.Index1] + myKeyState[keyState.Index2]) % Rc4KeyState.LENGTH;
                outputBuffer[i] = (byte)(inputBuffer[i] ^ myKeyState[xorIndex]);
            }
        }
    }

    /// <summary>
    /// Advances the given key state.
    /// </summary>
    /// <param name="amount">The amount to advance the key state by.</param>
    /// <param name="keyState">The key state to advance.</param>
    public static void Advance(int amount, ref Rc4KeyState keyState)
    {
        for (int i = 0; i < amount; i++)
            IncrementKeyState(ref keyState);
    }

    /// <summary>
    /// Schedules the given key into the given state buffer.
    /// </summary>
    /// <param name="keyDataBuffer">A buffer containing the key data.</param>
    /// <param name="keyState">A buffer to place the scheduled key data into.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if the length of the <paramref name="keyDataBuffer"/> is
    /// less than one, or greater than <see cref="Rc4KeyState.LENGTH"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if the <paramref name="keyState"/> buffer is shorter than <see cref="Rc4KeyState.LENGTH"/>.
    /// </exception>
    public static void ScheduleKey(ReadOnlySpan<byte> keyDataBuffer, Span<byte> keyState)
    {
        if (keyDataBuffer.Length is < 1 or > Rc4KeyState.LENGTH)
        {
            throw new ArgumentOutOfRangeException
            (
                nameof(keyDataBuffer),
                keyDataBuffer.Length,
                $"Key length must be greater than zero and less than {Rc4KeyState.LENGTH}."
            );
        }

        if (keyState.Length < Rc4KeyState.LENGTH)
            throw new ArgumentException($"The key state buffer must be at least {Rc4KeyState.LENGTH} bytes long", nameof(keyState));

        for (int i = 0; i < Rc4KeyState.LENGTH; i++)
            keyState[i] = (byte)i;

        unchecked
        {
            byte swapIndex1 = 0;
            byte swapIndex2 = 0;

            for (int i = 0; i < Rc4KeyState.LENGTH; i++)
            {
                swapIndex2 = (byte)((swapIndex2 + keyState[i] + keyDataBuffer[swapIndex1]) % Rc4KeyState.LENGTH);
                keyState.Swap(i, swapIndex2);

                swapIndex1 = (byte)((swapIndex1 + 1) % keyDataBuffer.Length);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementKeyState(ref Rc4KeyState keyState)
    {
        Span<byte> myKeyState = keyState.MutableKeyState;

        unchecked
        {
            keyState.Index1 = (keyState.Index1 + 1) % Rc4KeyState.LENGTH;
            keyState.Index2 = (keyState.Index2 + myKeyState[keyState.Index1]) % Rc4KeyState.LENGTH;
            myKeyState.Swap(keyState.Index1, keyState.Index2);
        }
    }
}
