﻿using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;

namespace Sanctuary.SoeProtocol.Tests.Util;

public class Crc32Tests
{
    private const uint Seed = 1858192374;

    private static readonly byte[] Data =
    {
        0x00, 0x0d, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x7f, 0xaa, 0xa6, 0xb8, 0x43, 0xf1, 0x37, 0xcd, 0x34, 0xfe, 0x67, 0x6c, 0x32, 0x04, 0xa2, 0xe7, 0x40, 0x30, 0x7f,
        0xb0, 0xc1, 0x47, 0x22, 0xba, 0xf9, 0x8f, 0xf1, 0x59, 0xd0, 0x9f, 0x99, 0xaf, 0x89, 0x64, 0xcb, 0xe9, 0x8b, 0x1f, 0x71, 0x09, 0xac, 0x03, 0xbf, 0xf1, 0xaf, 0xc7,
        0x7c, 0xb9, 0xbb, 0x25, 0xb6, 0x11, 0x8a, 0x42, 0x30, 0xc0, 0x52, 0x28, 0xbc, 0x65, 0x00, 0x3a, 0x9a, 0xf7, 0x3c, 0x72, 0x18, 0x0b, 0x4a, 0x39, 0x86, 0x7b, 0x28,
        0x5d, 0xc1, 0x56, 0xc2, 0x1d, 0xe4, 0xbe, 0xc7, 0x7a, 0xa9, 0x91, 0xa7, 0x7c, 0xa3, 0xde, 0x89, 0x73, 0x6f, 0x8b, 0x62, 0x52, 0xcf, 0xf6, 0x17, 0x14, 0xf8, 0x1d,
        0x47, 0x7a, 0x1e, 0xba, 0xf4, 0x93, 0x23, 0x67, 0x21, 0x3e, 0x58, 0xba, 0xd7, 0x18, 0x2c, 0x69, 0xd7, 0x2d, 0xef, 0x10, 0x7d, 0xa4, 0xd1, 0xc7, 0x35, 0xde, 0xc0,
        0x11, 0xa4, 0x7c, 0x30, 0x89, 0xd6, 0xe8, 0x7a, 0x25, 0xff, 0xc3, 0xa6, 0x5f, 0xf3, 0xdf, 0x0e, 0xe0, 0x6d, 0x40, 0xc2, 0x82, 0x64, 0x45, 0x19, 0x45, 0xb9, 0x4f,
        0x7c, 0x1b, 0xab, 0x69, 0x8a, 0x2e, 0x54, 0xe3, 0x1d, 0x43, 0x25, 0x24, 0x4f, 0xd3, 0x88, 0xa9, 0xa0, 0x6c, 0x3d, 0x2c, 0x2a, 0xb5, 0xad, 0xee, 0x4c, 0x1d, 0x01,
        0x95, 0x8b, 0x47, 0x20, 0x10, 0xd4, 0x57, 0x3e, 0x05, 0x14, 0xcf, 0x6d, 0x57, 0x6d, 0xf0, 0xab, 0xf9, 0x77, 0xfb, 0x6d, 0x4c, 0x58, 0xd9, 0x94, 0x38, 0x48, 0x61,
        0xdd, 0x63, 0xa9, 0x31, 0x82, 0xf3, 0xcc, 0xf1, 0x85, 0x59, 0x3a, 0x37, 0x8e, 0xc9, 0x8c, 0xac, 0x8d, 0xee, 0x46, 0x0a, 0x97, 0x2f, 0x84, 0x11, 0xc7, 0x38, 0x25,
        0x24, 0x28, 0x47, 0x9d, 0xe8, 0x4a, 0x3b, 0x1d, 0x6e, 0xe4, 0x23, 0x9e, 0xab, 0xe8, 0xe6, 0x6d, 0x06, 0xe9, 0xc1, 0xaf, 0xd8, 0x18, 0x5e, 0x0b, 0x8d, 0x42, 0x4b,
        0x01, 0xa6, 0x94, 0x8e, 0x65, 0xba, 0xe0, 0x40, 0x84, 0x6c, 0xa4, 0x6a, 0x26, 0xba, 0x5e, 0x8a, 0x21, 0xc8, 0xae, 0x8d, 0xc3, 0xde, 0x04, 0xc3, 0xc5, 0xd7, 0x8d,
        0xe8, 0x1d, 0xcb, 0xaa, 0x53, 0xbd, 0xb0, 0xe9, 0xc5, 0x86, 0xf0, 0x23, 0x73, 0x59, 0x0e, 0x04, 0x74, 0x09, 0xa9, 0x14, 0xd6, 0x3b, 0xc6, 0x73, 0x38, 0xb0, 0x21,
        0x61, 0x42, 0xd3, 0xb0, 0x6e, 0xa2, 0x28, 0xf7, 0x3f, 0x2f, 0xaa, 0x92, 0x92, 0x23, 0x2d, 0x81, 0xdd, 0x40, 0x48, 0x2d, 0x9f, 0xca, 0xc7, 0x04, 0x52, 0xd3, 0x82,
        0x47, 0xd1, 0xc2, 0xae, 0xb1, 0xc6, 0x4e, 0x01, 0x7e, 0x04, 0x6c, 0x9f, 0x17, 0x65, 0xd2, 0xd7, 0x3a, 0x66, 0x89, 0x9a, 0xb5, 0x7e, 0xbd, 0x95, 0x13, 0x1a, 0x88,
        0x63, 0x58, 0xed, 0x59, 0x99, 0x18, 0x82, 0xa9, 0xe7, 0x01, 0xf1, 0x53, 0x8b, 0x38, 0x45, 0x75, 0x59, 0xee, 0xaa, 0x2c, 0xd1, 0xda, 0xf1, 0x88, 0xbb, 0x12, 0x7b,
        0x5f, 0xa5, 0xf3, 0xb2, 0x94, 0xc1, 0x05, 0xd0, 0xf9, 0x25, 0x22, 0x3d, 0x08, 0x7f, 0xfd, 0x5a, 0x4e, 0xec, 0x3d, 0x38, 0xb4, 0x05, 0xc5, 0xc1, 0x5f, 0xcf, 0x67,
        0xba, 0x07, 0xa1, 0x14, 0x67, 0x7d, 0xd8, 0x0e, 0xcb, 0x5c, 0xe6, 0x80, 0xdb, 0x89, 0xb9, 0x04, 0xd3, 0x6a, 0x49, 0xbc, 0x70, 0xb3, 0xcb, 0xa8, 0xeb, 0x2d, 0x60,
        0x85, 0xbf, 0x38, 0xa7, 0xc1, 0xc2, 0xc3, 0xbd, 0x70, 0x41, 0xd5, 0xd7, 0x14, 0x85, 0x46, 0xd9, 0x46, 0x56, 0xc2, 0x79, 0x42, 0xe5, 0xf1, 0x22, 0x4b, 0x1d, 0xc6,
        0x11, 0xa9, 0xed, 0xda, 0x47, 0x42, 0x82, 0x47, 0xde, 0x9a, 0x1a, 0x95, 0xee, 0xf9, 0xc4, 0xee, 0x6e, 0xd2, 0x58, 0x82, 0xf1, 0xa6, 0x70, 0x17
    };

    [Fact]
    public void Hash_IsCorrect()
    {
        const ushort hash = 52395;

        Assert.Equal(hash, (ushort)Crc32.Hash(Data, Seed));
    }
}
