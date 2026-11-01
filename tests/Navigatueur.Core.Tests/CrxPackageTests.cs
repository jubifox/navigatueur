using System.Buffers.Binary;
using System.Security.Cryptography;
using Navigatueur.Core.Extensions;
using Xunit;

namespace Navigatueur.Core.Tests;

public class CrxPackageTests
{
    private static readonly byte[] Zip = "PK\u0003\u0004 a pretend zip archive"u8.ToArray();

    // ------------------------------------------------------- CRX3 builder

    private static void WriteVarint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(value | 0x80));
            value >>= 7;
        }

        output.Add((byte)value);
    }

    private static void WriteBytesField(List<byte> output, int number, byte[] value)
    {
        WriteVarint(output, ((ulong)number << 3) | 2);
        WriteVarint(output, (ulong)value.Length);
        output.AddRange(value);
    }

    private static byte[] SignedPayload(byte[] signedHeaderData, byte[] archive)
    {
        var data = new List<byte>("CRX3 SignedData\0"u8.ToArray());
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)signedHeaderData.Length);
        data.AddRange(length);
        data.AddRange(signedHeaderData);
        data.AddRange(archive);
        return data.ToArray();
    }

    /// <summary>A CRX3 claiming <paramref name="claimedIdKey"/>'s id, signed by <paramref name="signer"/>, wrapping <paramref name="archive"/>.</summary>
    private static byte[] BuildCrx(AsymmetricAlgorithm signer, byte[] claimedIdKey, byte[] archive, byte[]? signedArchive = null)
    {
        var crxId = SHA256.HashData(claimedIdKey)[..16];
        var signedHeader = new List<byte>();
        WriteBytesField(signedHeader, 1, crxId);
        var signedHeaderData = signedHeader.ToArray();

        var payload = SignedPayload(signedHeaderData, signedArchive ?? archive);
        var (publicKey, signature, field) = signer switch
        {
            RSA rsa => (rsa.ExportSubjectPublicKeyInfo(), rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), 2),
            ECDsa ecdsa => (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), 3),
            _ => throw new ArgumentException("unsupported key"),
        };

        var proof = new List<byte>();
        WriteBytesField(proof, 1, publicKey);
        WriteBytesField(proof, 2, signature);

        var header = new List<byte>();
        WriteBytesField(header, field, proof.ToArray());
        WriteBytesField(header, 10000, signedHeaderData);

        var crx = new List<byte>("Cr24"u8.ToArray());
        var numbers = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(numbers.AsSpan(0, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(numbers.AsSpan(4, 4), (uint)header.Count);
        crx.AddRange(numbers);
        crx.AddRange(header);
        crx.AddRange(archive);
        return crx.ToArray();
    }

    // -------------------------------------------------------------- tests

    [Fact]
    public void ExtensionId_IsTheChromeEncodingOfTheKeyHash()
    {
        using var rsa = RSA.Create(2048);
        var id = CrxPackage.ExtensionIdFromPublicKey(rsa.ExportSubjectPublicKeyInfo());

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.InRange(c, 'a', 'p'));
    }

    [Fact]
    public void RsaSignedPackage_FromTheExpectedDeveloper_IsAccepted()
    {
        using var developer = RSA.Create(2048);
        var key = developer.ExportSubjectPublicKeyInfo();
        var crx = BuildCrx(developer, key, Zip);

        var archive = CrxPackage.ExtractVerifiedArchive(crx, CrxPackage.ExtensionIdFromPublicKey(key));

        Assert.Equal(Zip, archive);
    }

    [Fact]
    public void EcdsaSignedPackage_IsAcceptedToo()
    {
        using var developer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = developer.ExportSubjectPublicKeyInfo();
        var crx = BuildCrx(developer, key, Zip);

        Assert.Equal(Zip, CrxPackage.ExtractVerifiedArchive(crx, CrxPackage.ExtensionIdFromPublicKey(key)));
    }

    [Fact]
    public void TamperedArchive_IsRejected()
    {
        using var developer = RSA.Create(2048);
        var key = developer.ExportSubjectPublicKeyInfo();
        var tampered = Zip.ToArray();
        tampered[^1] ^= 0xFF;
        var crx = BuildCrx(developer, key, tampered, signedArchive: Zip); // signature was made over the original

        Assert.Throws<InvalidDataException>(() => CrxPackage.ExtractVerifiedArchive(crx, CrxPackage.ExtensionIdFromPublicKey(key)));
    }

    [Fact]
    public void AttackerSigningWithTheirOwnKey_WhileClaimingTheId_IsRejected()
    {
        using var developer = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var genuineKey = developer.ExportSubjectPublicKeyInfo();
        var crx = BuildCrx(attacker, genuineKey, Zip); // claims the genuine id, but only the attacker's key signed it

        Assert.Throws<InvalidDataException>(() => CrxPackage.ExtractVerifiedArchive(crx, CrxPackage.ExtensionIdFromPublicKey(genuineKey)));
    }

    [Fact]
    public void GenuinePackageOfAnotherExtension_IsRejected()
    {
        using var other = RSA.Create(2048);
        using var expected = RSA.Create(2048);
        var crx = BuildCrx(other, other.ExportSubjectPublicKeyInfo(), Zip);

        Assert.Throws<InvalidDataException>(() =>
            CrxPackage.ExtractVerifiedArchive(crx, CrxPackage.ExtensionIdFromPublicKey(expected.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void ArchiveOf_SkipsACrx3Header()
    {
        using var developer = RSA.Create(2048);
        var crx = BuildCrx(developer, developer.ExportSubjectPublicKeyInfo(), Zip);

        Assert.Equal(Zip, CrxPackage.ArchiveOf(crx).ToArray());
    }

    [Fact]
    public void ArchiveOf_SkipsACrx2Header()
    {
        // magic, version 2, key length 3, signature length 2, key, signature, zip
        var crx = new byte[] { 0x43, 0x72, 0x32, 0x34, 2, 0, 0, 0, 3, 0, 0, 0, 2, 0, 0, 0, 1, 2, 3, 4, 5 }.Concat(Zip).ToArray();

        Assert.Equal(Zip, CrxPackage.ArchiveOf(crx).ToArray());
    }

    [Fact]
    public void ArchiveOf_LeavesAPlainZipAlone()
    {
        Assert.Equal(Zip, CrxPackage.ArchiveOf(Zip).ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 2, 0, 0, 0, 0xFF, 0, 0, 0, 0, 0, 0, 0 })] // CRX2 key longer than the file
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 3, 0, 0, 0, 0xFF, 0xFF, 0, 0 })]           // CRX3 header longer than the file
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 9, 0, 0, 0, 0, 0, 0, 0 })]                 // unknown version
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 2 })]                                      // truncated
    public void ArchiveOf_RejectsBrokenHeaders(byte[] crx)
    {
        Assert.Throws<InvalidDataException>(() => CrxPackage.ArchiveOf(crx));
    }

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04 })]                                 // a plain zip
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })] // "Cr24", CRX2
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 3, 0, 0, 0, 0xFF, 0xFF, 0, 0 })]     // header longer than the file
    [InlineData(new byte[] { 0x43, 0x72, 0x32, 0x34, 3, 0, 0, 0, 2, 0, 0, 0, 0x12, 0x7F })] // field claims 127 bytes
    public void MalformedPackages_AreRejected(byte[] crx)
    {
        Assert.Throws<InvalidDataException>(() => CrxPackage.ExtractVerifiedArchive(crx, new string('a', 32)));
    }
}
