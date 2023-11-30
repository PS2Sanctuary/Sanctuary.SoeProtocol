using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class Rc4CipherTests
{
    [Fact]
    public void TestEncryption()
    {
        foreach (TestVector testVector in TestVector.Defaults)
        {
            Rc4KeyState state = GetRc4KeyState(testVector);
            byte[] plaintextBytes = Encoding.ASCII.GetBytes(testVector.PlainText);
            byte[] cipherBytes = new byte[plaintextBytes.Length];

            Rc4Cipher.Transform(plaintextBytes, cipherBytes, ref state);
            Assert.Equal(testVector.CipherText, cipherBytes);
        }
    }

    [Fact]
    public void TestRoundTrip()
    {
        foreach (TestVector testVector in TestVector.Defaults)
        {
            Rc4KeyState encryptState = GetRc4KeyState(testVector);
            Rc4KeyState decryptState = GetRc4KeyState(testVector);

            byte[] plaintextBytes = Encoding.ASCII.GetBytes(testVector.PlainText);
            byte[] encryptedBytes = new byte[plaintextBytes.Length];
            byte[] decryptedBytes = new byte[plaintextBytes.Length];

            Rc4Cipher.Transform(plaintextBytes, encryptedBytes, ref encryptState);
            Rc4Cipher.Transform(encryptedBytes, decryptedBytes, ref decryptState);

            Assert.Equal(plaintextBytes, decryptedBytes);
        }
    }

    /// <summary>
    /// Tests that the state is correctly advanced after
    /// a transform is completed.
    /// </summary>
    [Fact]
    public void TestExistingKeyState()
    {
        foreach (TestVector testVector in TestVector.Defaults)
        {
            int half = testVector.CipherText.Length / 2;
            byte[] decrypted = new byte[testVector.CipherText.Length];

            Rc4KeyState state = GetRc4KeyState(testVector);

            Rc4Cipher.Transform(testVector.CipherText.AsSpan(0, half), decrypted, ref state);
            Rc4Cipher.Transform(testVector.CipherText.AsSpan(half), decrypted.AsSpan(half), ref state);

            Assert.Equal(testVector.PlainText, Encoding.ASCII.GetString(decrypted));
        }
    }

    /// <summary>
    /// Tests that the <see cref="Rc4Cipher.Advance"/> function works correctly.
    /// </summary>
    [Fact]
    public void TestAdvance()
    {
        byte[] testValues1 = { 1, 2, 3 };
        byte[] testValues2 = { 1, 2, 3 };

        Rc4KeyState state1 = GetRc4KeyState(TestVector.Defaults[0]);
        Rc4KeyState state2 = GetRc4KeyState(TestVector.Defaults[0]);
        Rc4Cipher.Transform(testValues1, testValues1, ref state1);

        Rc4Cipher.Advance(2, ref state2);
        Rc4Cipher.Transform(testValues2.AsSpan(2), testValues2.AsSpan(2), ref state2);

        Assert.Equal(testValues1[2], testValues2[2]);
    }

    private static Rc4KeyState GetRc4KeyState(TestVector testVector)
        => new(Encoding.ASCII.GetBytes(testVector.Key));

    /// <summary>
    /// Initializes a new instance of the <see cref="TestVector"/> record.
    /// </summary>
    /// <param name="Key">The key used to encrypted the <paramref name="PlainText"/>.</param>
    /// <param name="PlainText">The plaintext.</param>
    /// <param name="CipherText">The correct cipher for the <paramref name="PlainText"/> when encrypted with the <paramref name="Key"/>.</param>
    public record TestVector(string Key, string PlainText, byte[] CipherText)
    {
        /// <summary>
        /// Gets an array of known correct test vectors from <see href="https://en.wikipedia.org/wiki/RC4#Test_vectors"/>.
        /// </summary>
        public static readonly IReadOnlyList<TestVector> Defaults = new List<TestVector>()
        {
            new
            (
                "Key",
                "Plaintext",
                new byte[] { 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 }
            ),
            new
            (
                "Wiki",
                "pedia",
                new byte[] { 0x10, 0x21, 0xBF, 0x04, 0x20 }
            ),
            new
            (
                "Secret",
                "Attack at dawn",
                new byte[] { 0x45, 0xA0, 0x1F, 0x64, 0x5F, 0xC3, 0x5B, 0x38, 0x35, 0x52, 0x54, 0x4B, 0x9B, 0xF5 }
            )
        };
    }
}
