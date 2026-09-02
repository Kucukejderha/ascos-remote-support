using System.Security.Cryptography;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using RemoteSupport.Protocol;

namespace RemoteSupport.SessionAgent;

public sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(7, 27, 43);
    private static readonly Color Blue = Color.FromArgb(11, 102, 195);
    private readonly Uri _server;
    private readonly Label _status = new();
    private readonly Label _code = new();
    private readonly Button _copy = new();
    private readonly Button _newCode = new();
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private SignalingHostClient? _api;
    private HostSession? _session;
    private DeviceIdentity? _identity;
    private readonly DeviceIdentityStore _identityStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RotaLink", "identity.json"));
    private bool _closing;

    public MainForm(string? serverAddress, bool elevated, bool privilegedInputReady)
    {
        _server = new Uri(serverAddress ?? "https://ascos.rotaniz.com");
        Text = "Rotaniz Remote Support — RotaLink";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 380);
        MinimumSize = new Size(520, 380);
        BackColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 10F);

        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Navy };
        header.Controls.Add(new Label { Text = "RotaLink", ForeColor = Color.White, Font = new Font("Segoe UI", 25F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 18) });
        header.Controls.Add(new Label
        {
            Text = privilegedInputReady
                ? "Rotaniz Remote Support • SYSTEM kontrol motoru hazır"
                : elevated
                    ? "Rotaniz Remote Support • Kontrol motoru başlatılamadı"
                    : "Rotaniz Remote Support • Sınırlı kullanıcı oturumu",
            ForeColor = privilegedInputReady ? Color.FromArgb(92, 214, 164) : Color.FromArgb(244, 179, 80),
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Location = new Point(31, 66)
        });
        Controls.Add(header);

        // The layout is deliberately static: the title bar stays native (the
        // main window buttons are only the operating system's), and the card
        // below is a simple two-row flow that never overflows.
        var card = new Panel { Location = new Point(28, 130), Size = new Size(504, 196), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(220, 230, 237), ButtonBorderStyle.Solid);
        card.Controls.Add(new Label { Text = "DESTEK KODUNUZ", ForeColor = Blue, Font = new Font("Segoe UI", 9F, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) });
        _code.Text = "Hazırlanıyor…";
        _code.ForeColor = Navy;
        _code.Font = new Font("Segoe UI", 28F, FontStyle.Bold);
        _code.AutoSize = true;
        _code.AutoEllipsis = true;
        _code.MaximumSize = new Size(456, 60);
        _code.Location = new Point(22, 44);
        card.Controls.Add(_code);
        _copy.Text = "Kodu kopyala";
        _copy.Location = new Point(24, 104);
        _copy.Size = new Size(200, 38);
        _copy.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _copy.Enabled = false;
        _copy.Click += (_, _) => { if (_session != null) Clipboard.SetText(_session.Code); };
        card.Controls.Add(_copy);
        _newCode.Text = "Yeni kod";
        _newCode.Location = new Point(240, 104);
        _newCode.Size = new Size(200, 38);
        _newCode.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _newCode.Enabled = false;
        _newCode.Click += async (_, _) => await RequestNewCodeAsync();
        card.Controls.Add(_newCode);
        _status.Text = "Güvenli bağlantı hazırlanıyor…";
        _status.ForeColor = Color.FromArgb(99, 120, 138);
        _status.AutoEllipsis = true;
        _status.Location = new Point(24, 156);
        _status.Size = new Size(456, 24);
        card.Controls.Add(_status);
        Controls.Add(card);

        var informationalVersion = typeof(MainForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(MainForm).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = BackColor,
            Padding = new Padding(28, 4, 28, 4),
            ColumnCount = 3,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var diagnostics = new LinkLabel
        {
            Text = "Tanılama günlüğü (tümü)",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkColor = Color.FromArgb(70, 96, 117)
        };
        diagnostics.Click += (_, _) =>
        {
            try
            {
                var bundlePath = AppDiagnostics.CreateSupportBundle();
                Process.Start("explorer.exe", "/select,\"" + bundlePath + "\"");
            }
            catch (Exception exception)
            {
                AppDiagnostics.Write("Combined diagnostics could not be created.", exception);
                MessageBox.Show(this, "Tanılama dosyası oluşturulamadı: " + exception.Message,
                    "RotaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        footer.Controls.Add(diagnostics, 0, 0);
        var sourceAndLicense = new LinkLabel
        {
            Text = "Kaynak kod ve lisans",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = Color.FromArgb(70, 96, 117)
        };
        sourceAndLicense.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://github.com/Kucukejderha/ascos-rotalink")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                AppDiagnostics.Write("Source and license page could not be opened.", exception);
                MessageBox.Show(this, "Kaynak kod sayfası açılamadı: " + exception.Message,
                    "RotaLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        footer.Controls.Add(sourceAndLicense, 1, 0);
        footer.Controls.Add(new Label
        {
            Text = "v" + informationalVersion,
            ForeColor = Color.FromArgb(99, 120, 138),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        }, 2, 0);
        Controls.Add(footer);

        Shown += async (_, _) => await PrepareSessionAsync();
        FormClosing += (_, _) =>
        {
            _closing = true;
            StopSession();
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0112) // WM_SYSCOMMAND
        {
            var command = m.WParam.ToInt64() & 0xFFF0;
            if (command is 0xF020 or 0xF030 or 0xF120 or 0xF060) // SC_MINIMIZE/MAXIMIZE/RESTORE/CLOSE
            {
                AppDiagnostics.Write("WM_SYSCOMMAND enter: command=0x" + command.ToString("X") +
                    ", WindowState=" + WindowState + ", Thread=" + Environment.CurrentManagedThreadId + ".");
                base.WndProc(ref m);
                AppDiagnostics.Write("WM_SYSCOMMAND exit: command=0x" + command.ToString("X") +
                    ", WindowState=" + WindowState + ".");
                return;
            }
        }
        base.WndProc(ref m);
    }

    private async Task PrepareSessionAsync()
    {
        try
        {
            _copy.Enabled = false;
            _newCode.Enabled = false;
            _code.Text = "Hazırlanıyor…";
            SetStatus("Güncelleme denetleniyor…", Color.FromArgb(99, 120, 138));

            if (await SelfUpdate.TryUpdateAsync(CancellationToken.None))
            {
                SetStatus("Yeni sürüm yüklendi — yeniden başlatılıyor…", Blue);
                Application.Exit();
                return;
            }

            SetStatus("Yeni destek kodu hazırlanıyor…", Color.FromArgb(99, 120, 138));

            _identity ??= await _identityStore.LoadOrCreateAsync(CancellationToken.None);
            _api ??= new SignalingHostClient(_server, _identity.SigningKey);
            _session = await _api.CreateSessionAsync(Environment.MachineName, CancellationToken.None);
            if (_closing || IsDisposed) return;

            AppDiagnostics.Write("Support session prepared. SessionId=" + _session.SessionId);
            _code.Text = _session.Code;
            _copy.Enabled = true;
            SetStatus("Bağlantı aktif — destek kodunu iletin.", Blue);
            StartSession();
            _newCode.Enabled = true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write("Session preparation failed.", ex);
            _session = null;
            _code.Text = "Kullanılamıyor";
            _newCode.Enabled = !_closing && !IsDisposed;
            SetStatus("Sunucuya bağlanılamadı: " + ex.Message, Color.Firebrick);
        }
    }

    private async Task RequestNewCodeAsync()
    {
        if (_closing || IsDisposed || !_newCode.Enabled) return;

        _newCode.Enabled = false;
        _copy.Enabled = false;
        _code.Text = "Hazırlanıyor…";

        if (_sessionTask is not null)
        {
            AppDiagnostics.Write("New support code requested; current session is being revoked.");
            StopSession();
            SetStatus("Eski oturum kapatılıyor — yeni kod hazırlanacak…", Color.FromArgb(99, 120, 138));
            return;
        }

        await PrepareSessionAsync();
    }

    private void StartSession()
    {
        if (_api == null || _session == null || _sessionTask != null) return;
        _sessionCancellation?.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        AppDiagnostics.Write("Automatic screen sharing started.");
        // The whole session chain (WebSocket receive, input dispatch, capture)
        // runs on a threadpool thread so the UI message loop is never blocked:
        // the helper's routed WM_SYSCOMMAND commands must be processed by this
        // window immediately.
        var api = _api;
        var session = _session;
        var cancellation = _sessionCancellation.Token;
        _sessionTask = Task.Run(() => RemoteSession.RunAsync(api, session, cancellation));
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
                _copy.Enabled = false;
                await PrepareSessionAsync();
            }
        }
    }

    private void StopSession()
    {
        var cancellation = _sessionCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested) return;

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
