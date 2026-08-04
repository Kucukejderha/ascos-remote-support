using System.Security.Cryptography;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

namespace RemoteSupport.SessionAgent;

public sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(7, 27, 43);
    private static readonly Color Blue = Color.FromArgb(11, 102, 195);
    private static readonly Color Mint = Color.FromArgb(43, 210, 160);
    private readonly Uri _server;
    private readonly Label _status = new();
    private readonly Label _code = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _copy = new();
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private SignalingHostClient? _api;
    private HostSession? _session;
    private ECDsaCng? _identity;
    private bool _closing;

    public MainForm(string? serverAddress)
    {
        _server = new Uri(serverAddress ?? "https://45.87.173.201.nip.io");
        Text = "Rotaniz Remote Support — RotaLink";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 440);
        MinimumSize = new Size(520, 410);
        BackColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 10F);

        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Navy };
        header.Controls.Add(new Label { Text = "RotaLink", ForeColor = Color.White, Font = new Font("Segoe UI", 25F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        header.Controls.Add(new Label { Text = "Rotaniz Remote Support", ForeColor = Color.FromArgb(169, 189, 203), Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(31, 66) });
        Controls.Add(header);

        var card = new Panel { Location = new Point(28, 130), Size = new Size(504, 196), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(220, 230, 237), ButtonBorderStyle.Solid);
        card.Controls.Add(new Label { Text = "DESTEK KODUNUZ", ForeColor = Blue, Font = new Font("Segoe UI", 9F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 22) });
        _code.Text = "Hazırlanıyor…";
        _code.ForeColor = Navy;
        _code.Font = new Font("Segoe UI", 28F, FontStyle.Bold);
        _code.AutoSize = true;
        _code.Location = new Point(22, 48);
        card.Controls.Add(_code);
        _copy.Text = "Kodu kopyala";
        _copy.Location = new Point(350, 53);
        _copy.Size = new Size(126, 38);
        _copy.Enabled = false;
        _copy.Click += (_, _) => { if (_session != null) Clipboard.SetText(_session.Code); };
        card.Controls.Add(_copy);
        _status.Text = "Güvenli bağlantı hazırlanıyor…";
        _status.ForeColor = Color.FromArgb(99, 120, 138);
        _status.AutoEllipsis = true;
        _status.Location = new Point(24, 117);
        _status.Size = new Size(452, 24);
        card.Controls.Add(_status);
        card.Controls.Add(new Label { Text = "Ekran paylaşımı yalnızca siz başlattığınızda etkinleşir.", ForeColor = Color.FromArgb(70, 96, 117), AutoSize = true, Location = new Point(24, 151) });
        Controls.Add(card);

        ConfigureButton(_start, "Paylaşımı Başlat", Blue, Color.White, new Point(28, 350), new Size(180, 48));
        _start.Enabled = false;
        _start.Click += StartClicked;
        Controls.Add(_start);

        ConfigureButton(_stop, "Bağlantıyı Sonlandır", Color.White, Navy, new Point(218, 350), new Size(190, 48));
        _stop.FlatAppearance.BorderColor = Color.FromArgb(189, 204, 215);
        _stop.Enabled = false;
        _stop.Click += (_, _) => StopSession();
        Controls.Add(_stop);

        Controls.Add(new Label
        {
            Text = "v0.4.2",
            ForeColor = Color.FromArgb(99, 120, 138),
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Location = new Point(492, 414)
        });
        var diagnostics = new LinkLabel
        {
            Text = "Tanılama günlüğü",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Location = new Point(28, 414),
            LinkColor = Color.FromArgb(70, 96, 117)
        };
        diagnostics.Click += (_, _) => Process.Start("explorer.exe", "/select,\"" + AppDiagnostics.LogPath + "\"");
        Controls.Add(diagnostics);

        Shown += async (_, _) => await PrepareSessionAsync();
        FormClosing += (_, _) =>
        {
            _closing = true;
            StopSession();
        };
    }

    private static void ConfigureButton(Button button, string text, Color backColor, Color foreColor, Point location, Size size)
    {
        button.Text = text;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = backColor == Color.White ? 1 : 0;
        button.Location = location;
        button.Size = size;
    }

    private async Task PrepareSessionAsync()
    {
        try
        {
            _start.Enabled = false;
            _stop.Enabled = false;
            _copy.Enabled = false;
            _code.Text = "Hazırlanıyor…";
            SetStatus("Yeni destek kodu hazırlanıyor…", Color.FromArgb(99, 120, 138));

            _identity ??= new ECDsaCng(ECCurve.NamedCurves.nistP256);
            _api ??= new SignalingHostClient(_server, _identity);
            _session = await _api.CreateSessionAsync(Environment.MachineName, CancellationToken.None);
            if (_closing || IsDisposed) return;

            AppDiagnostics.Write("Support session prepared. SessionId=" + _session.SessionId);
            _code.Text = _session.Code;
            _copy.Enabled = true;
            _start.Enabled = true;
            SetStatus("Hazır — kodu iletin ve Paylaşımı Başlat'a tıklayın.", Blue);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write("Session preparation failed.", ex);
            SetStatus("Sunucuya bağlanılamadı: " + ex.Message, Color.Firebrick);
        }
    }

    private void StartClicked(object? sender, EventArgs e) => TryStartSession();

    private void TryStartSession()
    {
        if (_api == null || _session == null || _sessionTask != null) return;
        _sessionCancellation?.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        AppDiagnostics.Write("Local user started screen sharing from the main window.");
        _start.Enabled = false;
        _stop.Enabled = true;
        SetStatus("Bağlantı aktif", Mint);
        _sessionTask = RemoteSession.RunAsync(_api, _session, _sessionCancellation.Token);
        _ = ObserveSessionAsync(_sessionTask);
    }

    private async Task ObserveSessionAsync(Task task)
    {
        try { await task; SetStatus("Bağlantı sonlandırıldı.", Color.FromArgb(99, 120, 138)); }
        catch (OperationCanceledException) { SetStatus("Bağlantı sizin tarafınızdan sonlandırıldı.", Color.FromArgb(99, 120, 138)); }
        catch (Exception ex)
        {
            AppDiagnostics.Write("Remote session failed.", ex);
            SetStatus("Bağlantı kapandı: " + ex.Message, Color.Firebrick);
        }
        finally
        {
            _sessionTask = null;
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            _session = null;

            if (!_closing && !IsDisposed)
            {
                _stop.Enabled = false;
                _start.Enabled = false;
                _copy.Enabled = false;
                await PrepareSessionAsync();
            }
        }
    }

    private void StopSession()
    {
        var cancellation = _sessionCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested) return;

        _stop.Enabled = false;
        SetStatus("Bağlantı sonlandırılıyor…", Color.FromArgb(99, 120, 138));
        cancellation.Cancel();
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text, color))); return; }
        _status.Text = text;
        _status.ForeColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sessionCancellation?.Dispose();
            _api?.Dispose();
            _identity?.Dispose();
        }
        base.Dispose(disposing);
    }
}
