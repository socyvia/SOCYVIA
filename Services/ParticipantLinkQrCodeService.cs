using System;
using QRCoder;

namespace SOCYVIA.Services;

/// <summary>Generates participant-link QR PNG data locally; URLs never leave SOCYVIA.</summary>
public static class ParticipantLinkQrCodeService
{
    public static byte[] CreatePng(string canonicalParticipantUrl, int pixelsPerModule = 12)
    {
        if (!Uri.TryCreate(canonicalParticipantUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A valid HTTPS participant URL is required.", nameof(canonicalParticipantUrl));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
