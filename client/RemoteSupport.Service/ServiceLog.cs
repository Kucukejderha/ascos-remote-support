namespace RemoteSupport.Service;

internal sealed class ServiceLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public ServiceLog()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RotaLink", "Logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "Service.log");
    }

    public void Write(string message)
    {
        lock (_gate)
            File.AppendAllText(_path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
    }
}
