using Sanctuary.SoeProtocol.Objects;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Services;

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

        if (inputBuffer.IsEmpty)
            return;

        // Pin references to elide all bounds checks and span allocations in the loop
        ref byte stateRef = ref MemoryMarshal.GetReference(keyState.MutableKeyState);
        ref byte inputRef = ref MemoryMarshal.GetReference(inputBuffer);
        ref byte outputRef = ref MemoryMarshal.GetReference(outputBuffer);

        // Keep indices in registers for the duration of the loop
        byte i = keyState.Index1;
        byte j = keyState.Index2;

        int length = inputBuffer.Length;

        for (int n = 0; n < length; n++)
        {
            // This is a simplified version of the below

            // keyState.Index1 = (byte)(keyState.Index1 + 1);
            // keyState.Index2 = (byte)(keyState.Index2 + myKeyState[keyState.Index1]);
            // myKeyState.Swap(keyState.Index1, keyState.Index2);
            // 
            // int xorIndex = (myKeyState[keyState.Index1] + myKeyState[keyState.Index2]) % Rc4KeyState.LENGTH;
            // outputBuffer[i] = (byte)(inputBuffer[i] ^ myKeyState[xorIndex]);

            i = (byte)(i + 1);
            byte si = Unsafe.Add(ref stateRef, i);
            j = (byte)(j + si);
            byte sj = Unsafe.Add(ref stateRef, j);

            Unsafe.Add(ref stateRef, i) = sj;
            Unsafe.Add(ref stateRef, j) = si;

            byte xorIndex = (byte)(si + sj);
            byte xorValue = Unsafe.Add(ref stateRef, xorIndex);

            Unsafe.Add(ref outputRef, n) = (byte)(Unsafe.Add(ref inputRef, n) ^ xorValue);
        }

        // Persist the final index state back to object
        keyState.Index1 = i;
        keyState.Index2 = j;
    }

    /// <summary>
    /// Advances the given key state.
    /// </summary>
    /// <param name="amount">The amount to advance the key state by.</param>
    /// <param name="keyState">The key state to advance.</param>
    public static void Advance(int amount, ref Rc4KeyState keyState)
    {
        if (amount <= 0)
            return;

        ref byte stateRef = ref MemoryMarshal.GetReference(keyState.MutableKeyState);
        byte i = keyState.Index1;
        byte j = keyState.Index2;

        for (int n = 0; n < amount; n++)
        {
            i = (byte)(i + 1);
            byte si = Unsafe.Add(ref stateRef, i);
            j = (byte)(j + si);
            byte sj = Unsafe.Add(ref stateRef, j);

            Unsafe.Add(ref stateRef, i) = sj;
            Unsafe.Add(ref stateRef, j) = si;
        }

        keyState.Index1 = i;
        keyState.Index2 = j;
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
                swapIndex2 = (byte)(swapIndex2 + keyState[i] + keyDataBuffer[swapIndex1]);
                keyState.Swap(i, swapIndex2);

                // Using a conditional is faster than the integer division of a modulo against the buffer length
                if (++swapIndex1 == keyDataBuffer.Length)
                    swapIndex1 = 0;
            }
        }
    }
}
