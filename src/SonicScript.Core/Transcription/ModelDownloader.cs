namespace SonicScript.Core.Transcription;

public sealed class ModelDownloader(HttpClient http, string modelsDirectory)
{
    public string PathFor(WhisperModelInfo model) => Path.Combine(modelsDirectory, model.FileName);

    public bool IsInstalled(WhisperModelInfo model)
    {
        var p = PathFor(model);
        return File.Exists(p) && new FileInfo(p).Length > model.ApproxBytes / 2;
    }

    /// <summary>Lädt nach "*.part" und benennt erst nach Prüfung der GGML-Magic um. progress: 0..1</summary>
    public async Task<string> DownloadAsync(WhisperModelInfo model, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(modelsDirectory);
        var target = PathFor(model);
        var part = target + ".part";

        using (var resp = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? model.ApproxBytes;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                progress?.Report(Math.Min(1, (double)done / total));
            }
        }

        var head = new byte[4];
        await using (var fs = File.OpenRead(part))
            _ = await fs.ReadAsync(head, ct).ConfigureAwait(false);
        if (!WhisperModelCatalog.HasGgmlMagic(head))
        {
            File.Delete(part);
            throw new InvalidDataException("not_a_ggml_model");
        }
        File.Move(part, target, overwrite: true);
        return target;
    }
}
