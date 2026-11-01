using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Navigatueur.Core.Extensions;

/// <summary>
/// Reads and verifies a CRX3 package — the signed format the Chrome Web Store
/// distributes extensions in — and returns the zip inside.
///
/// Layout: "Cr24", version (3), header length, a protobuf CrxFileHeader, then
/// the zip. The header carries one or more signature proofs (public key +
/// signature) and the signed header data, which names the extension's id.
/// Each signature covers "CRX3 SignedData\0", the signed header data's length
/// and bytes, then the whole zip. An extension's id is derived from its
/// developer's public key (first 16 bytes of its SHA-256, as letters a–p), so
/// a valid signature by the key that maps to the expected id proves the zip
/// is genuinely that extension's, untampered — whoever served the file.
/// </summary>
public static class CrxPackage
{
    private const int MaxHeaderSize = 1 << 20;
    private static readonly byte[] Magic = "Cr24"u8.ToArray();
    private static readonly byte[] SignaturePrefix = "CRX3 SignedData\0"u8.ToArray();

    /// <summary>
    /// The zip bytes of a package: a plain zip as is, or what follows a CRX2/CRX3
    /// header. Checks nothing — only for packages already pinned by checksum.
    /// </summary>
    public static ReadOnlyMemory<byte> ArchiveOf(byte[] package)
    {
        if (package.Length < 4 || !package.AsSpan(0, 4).SequenceEqual(Magic))
        {
            return package;
        }

        if (package.Length < 12)
        {
            throw new InvalidDataException("en-tête CRX de taille invalide");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(4, 4));
        long headerEnd = version switch
        {
            // CRX2: magic, version, key length, signature length, key, signature.
            2 when package.Length >= 16 => 16L + BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(8, 4)) + BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(12, 4)),
            3 => 12L + BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(8, 4)),
            _ => throw new InvalidDataException($"format CRX{version} non pris en charge"),
        };

        return headerEnd <= package.Length
            ? new ReadOnlyMemory<byte>(package, (int)headerEnd, package.Length - (int)headerEnd)
            : throw new InvalidDataException("en-tête CRX de taille invalide");
    }

    /// <summary>The Chrome extension id a developer key (DER SubjectPublicKeyInfo) maps to.</summary>
    public static string ExtensionIdFromPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(subjectPublicKeyInfo, hash);
        return IdFromBytes(hash[..16]);
    }

    /// <summary>
    /// The zip inside <paramref name="crx"/>, once it's proven to be
    /// <paramref name="expectedExtensionId"/>'s and signed by that extension's
    /// own key. Throws <see cref="InvalidDataException"/> otherwise.
    /// </summary>
    public static byte[] ExtractVerifiedArchive(byte[] crx, string expectedExtensionId)
    {
        if (crx.Length < 12 || !crx.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("ce n'est pas un paquet d'extension (CRX)");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(crx.AsSpan(4, 4));
        if (version != 3)
        {
            throw new InvalidDataException($"format CRX{version} non pris en charge (CRX3 attendu)");
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(crx.AsSpan(8, 4));
        if (headerSize > MaxHeaderSize || 12L + headerSize > crx.Length)
        {
            throw new InvalidDataException("en-tête CRX de taille invalide");
        }

        var header = new ReadOnlyMemory<byte>(crx, 12, (int)headerSize);
        var archive = new ReadOnlyMemory<byte>(crx, 12 + (int)headerSize, crx.Length - 12 - (int)headerSize);

        var proofs = new List<(byte[] PublicKey, byte[] Signature, bool IsRsa)>();
        byte[]? signedHeaderData = null;
        foreach (var field in Protobuf.Read(header))
        {
            if (field.Number is 2 or 3 && field.IsLengthDelimited)
            {
                // AsymmetricKeyProof: 1 = public key, 2 = signature (2 = RSA proofs, 3 = ECDSA proofs).
                byte[]? publicKey = null;
                byte[]? signature = null;
                foreach (var inner in Protobuf.Read(field.Bytes))
                {
                    if (inner.Number == 1 && inner.IsLengthDelimited)
                    {
                        publicKey = inner.Bytes.ToArray();
                    }
                    else if (inner.Number == 2 && inner.IsLengthDelimited)
                    {
                        signature = inner.Bytes.ToArray();
                    }
                }

                if (publicKey is not null && signature is not null)
                {
                    proofs.Add((publicKey, signature, field.Number == 2));
                }
            }
            else if (field.Number == 10000 && field.IsLengthDelimited)
            {
                signedHeaderData = field.Bytes.ToArray();
            }
        }

        if (signedHeaderData is null)
        {
            throw new InvalidDataException("paquet CRX sans données signées");
        }

        byte[]? crxId = null;
        foreach (var field in Protobuf.Read(signedHeaderData))
        {
            if (field.Number == 1 && field.IsLengthDelimited)
            {
                crxId = field.Bytes.ToArray();
            }
        }

        if (crxId is not { Length: 16 } || IdFromBytes(crxId) != expectedExtensionId)
        {
            throw new InvalidDataException("ce paquet est celui d'une autre extension");
        }

        var signedData = new byte[SignaturePrefix.Length + 4 + signedHeaderData.Length + archive.Length];
        SignaturePrefix.CopyTo(signedData, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(signedData.AsSpan(SignaturePrefix.Length, 4), (uint)signedHeaderData.Length);
        signedHeaderData.CopyTo(signedData, SignaturePrefix.Length + 4);
        archive.Span.CopyTo(signedData.AsSpan(SignaturePrefix.Length + 4 + signedHeaderData.Length));

        foreach (var proof in proofs)
        {
            if (ExtensionIdFromPublicKey(proof.PublicKey) == expectedExtensionId && Verify(proof.PublicKey, proof.Signature, proof.IsRsa, signedData))
            {
                return archive.ToArray();
            }
        }

        throw new InvalidDataException("le paquet n'est pas signé par la clé de l'extension");
    }

    private static bool Verify(byte[] publicKey, byte[] signature, bool isRsa, byte[] signedData)
    {
        try
        {
            if (isRsa)
            {
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string IdFromBytes(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[2 * i] = (char)('a' + (bytes[i] >> 4));
            chars[(2 * i) + 1] = (char)('a' + (bytes[i] & 0x0F));
        }

        return new string(chars);
    }

    /// <summary>Just enough protobuf wire-format reading for the CRX header.</summary>
    private static class Protobuf
    {
        public readonly record struct Field(int Number, int WireType, ReadOnlyMemory<byte> Bytes)
        {
            public bool IsLengthDelimited => WireType == 2;
        }

        public static List<Field> Read(ReadOnlyMemory<byte> data)
        {
            var fields = new List<Field>();
            var span = data.Span;
            var position = 0;
            while (position < span.Length)
            {
                var tag = ReadVarint(span, ref position);
                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);
                switch (wireType)
                {
                    case 0:
                        ReadVarint(span, ref position);
                        fields.Add(new Field(number, wireType, default));
                        break;
                    case 1:
                        position = Advance(span, position, 8);
                        break;
                    case 2:
                        var length = ReadVarint(span, ref position);
                        if (length > (ulong)(span.Length - position))
                        {
                            throw new InvalidDataException("en-tête CRX mal formé");
                        }

                        fields.Add(new Field(number, wireType, data.Slice(position, (int)length)));
                        position += (int)length;
                        break;
                    case 5:
                        position = Advance(span, position, 4);
                        break;
                    default:
                        throw new InvalidDataException("en-tête CRX mal formé");
                }
            }

            return fields;
        }

        private static int Advance(ReadOnlySpan<byte> span, int position, int count) =>
            position + count <= span.Length ? position + count : throw new InvalidDataException("en-tête CRX mal formé");

        private static ulong ReadVarint(ReadOnlySpan<byte> span, ref int position)
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (position >= span.Length)
                {
                    break;
                }

                var b = span[position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("en-tête CRX mal formé");
        }
    }
}
