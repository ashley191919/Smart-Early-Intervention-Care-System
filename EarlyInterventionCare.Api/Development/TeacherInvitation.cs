using QRCoder;

namespace EarlyInterventionCare.Api.Development;

public static class TeacherInvitation
{
    // The credential is a fragment: it is never sent in an HTTP request URL.
    public static string LocalOrigin(HttpRequest request)
    {
        if (!Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var origin)
            || !origin.IsLoopback || origin.Scheme is not ("http" or "https"))
            throw new WorkspaceOperationException("LOCAL_INVITATION_ONLY", "目前邀請僅供本機測試，請使用 localhost 或 127.0.0.1 網址。", 400);
        return origin.GetLeftPart(UriPartial.Authority);
    }

    public static (string Url, string QrImage) Generate(string origin, string code)
    {
        var url = origin + "/?role=teacher#code=" + Uri.EscapeDataString(code);
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var renderer = new PngByteQRCode(data);
        return (url, "data:image/png;base64," + Convert.ToBase64String(renderer.GetGraphic(6)));
    }
}
