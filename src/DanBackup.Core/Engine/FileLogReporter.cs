using System.Text;

namespace DanBackup.Core.Engine;

/// <summary>Grava o log em arquivo e repassa tudo para o reporter interno (a UI).</summary>
internal sealed class FileLogReporter : IOperationReporter, IDisposable
{
    private readonly IOperationReporter _inner;
    private readonly StreamWriter? _writer;
    private readonly Lock _lock = new();

    public FileLogReporter(string path, IOperationReporter inner)
    {
        _inner = inner;
        try
        {
            _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            inner.Log(LogLevel.Warning, $"Não foi possível criar o arquivo de log ({ex.Message}).");
        }
    }

    public void Log(LogLevel level, string message)
    {
        if (_writer is not null)
        {
            var prefix = level switch
            {
                LogLevel.Warning => "AVISO",
                LogLevel.Error => "ERRO ",
                _ => "INFO ",
            };
            lock (_lock) _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {prefix} {message}");
        }
        _inner.Log(level, message);
    }

    public void Status(string text) => _inner.Status(text);
    public void Progress(int done, int total) => _inner.Progress(done, total);

    public void Dispose()
    {
        lock (_lock) _writer?.Dispose();
    }
}
