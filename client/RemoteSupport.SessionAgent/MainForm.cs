using System.Net.WebSockets;
using System.Security.Cryptography;

namespace RemoteSupport.SessionAgent;

public sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(7, 27, 43);
    private static readonly Color Blue = Color.FromArgb(11, 102, 195);
    private static readonly Color Cyan = Color.FromArgb(32, 189, 214);
    private static readonly Color Mint = Color.FromArgb(43, 210, 160);
    private readonly Uri _server;
    private readonly Label _status = new();
    private readonly Label _code = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _copy = new();
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;

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
        _copy.Click += (_, _) => { if (_code.Text.Length > 0) Clipboard.SetText(_code.Text); };
        card.Controls.Add(_copy);
        _status.Text = "Güvenli bağlantı hazırlanıyor…";
        _status.ForeColor = Color.FromArgb(99, 120, 138);
        _status.AutoSize = true;
        _status.Location = new Point(24, 117);
        card.Controls.Add(_status);
        card.Controls.Add(new Label { Text = "Ekran paylaşımı yalnızca siz başlattığınızda etkinleşir.", ForeColor = Color.FromArgb(70, 96, 117), AutoSize = true, Location = new Point(24, 151) });
        Controls.Add(card);

        _start.Text = "Paylaşımı Başlat";
        _start.BackColor = Blue;
        _start.ForeColor = Color.White;
        _start.FlatStyle = FlatStyle.Flat;
        _start.FlatAppearance.BorderSize = 0;
        _start.Enabled = false;
        _start.Size = new Size(180, 48);
        _start.Location = new Point(28, 350);
        _start.Click += StartClicked;
        Controls.Add(_start);

        _stop.Text = "Bağlantıyı Sonlandır";
        _stop.BackColor = Color.White;
        _stop.ForeColor = Navy;
        _stop.FlatStyle = FlatStyle.Flat;
        _stop.FlatAppearance.BorderColor = Color.FromArgb(189, 204, 215);
        _stop.Enabled = false;
        _stop.Size = new Size(190, 48);
        _stop.Location = new Point(218, 350);
        _stop.Click += (_, _) => StopSession();
        Controls.Add(_stop);

        Shown += async (_, _) => await PrepareSessionAsync();
        FormClosing += (_, _) => StopSession();
    }

    private SignalingHostClient? _api;
    private HostSession? _session;
    private ECDsa? _identity;

    private async Task PrepareSessionAsync()
    {
        try
        {
            _identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _api = new SignalingHostClient(_server, _identity);
            _session = await _api.CreateSessionAsync(Environment.MachineName, CancellationToken.None);
            _code.Text = _session.Code;
            _copy.Enabled = true;
            _start.Enabled = true;
            SetStatus("Hazır — kodu destek personeline iletin.", Blue);
        }
        catch (Exception ex)
        {
            SetStatus("Sunucuya bağlanılamadı: " + ex.Message, Color.Firebrick);
        }
    }

    private void StartClicked(object? sender, EventArgs e)
    {
        if (_api is null || _session is null || _sessionTask is not null) return;
        var approved = MessageBox.Show(this,
            "Ekranınızın paylaşılmasına ve uzaktan fare/klavye kontrolüne izin veriyor musunuz?",
            "RotaLink — Kullanıcı Onayı", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        if (!approved) return;
        _sessionCancellation = new CancellationTokenSource();
        _start.Enabled = false;
        _stop.Enabled = true;
        SetStatus("Bağlantı aktif", Mint);
        _sessionTask = RunSessionAsync(_api, _session, _sessionCancellation.Token);
        _ = ObserveSessionAsync(_sessionTask);
    }

    private async Task ObserveSessionAsync(Task task)
    {
        try { await task; SetStatus("Bağlantı sonlandırıldı.", Color.FromArgb(99, 120, 138)); }
        catch (OperationCanceledException) { SetStatus("Bağlantı sizin tarafınızdan sonlandırıldı.", Color.FromArgb(99, 120, 138)); }
        catch (Exception ex) { SetStatus("Bağlantı kapandı: " + ex.Message, Color.Firebrick); }
        finally { _sessionTask = null; _stop.Enabled = false; }
    }

    private void StopSession() => _sessionCancellation?.Cancel();

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, color)); return; }
        _status.Text = text;
        _status.ForeColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _sessionCancellation?.Dispose(); _api?.Dispose(); _identity?.Dispose(); }
        base.Dispose(disposing);
    }

    private static async Task RunSessionAsync(SignalingHostClient api, HostSession session, CancellationToken cancellationToken)
    {
        var consent = new ConsentStateMachine(TimeProvider.System);
        var sessionId = Guid.ParseExact(session.SessionId, "N");
        consent.Request(sessionId, TimeSpan.FromHours(8));
        consent.Decide(sessionId, approved: true);
        using var socket = await api.ConnectHostSocketAsync(session, cancellationToken);
        using var capture = new GdiScreenCapture(960, 540);
        var input = new WindowsInputDispatcher(consent, sessionId);
        var captureTask = CaptureLoopAsync(socket, capture, cancellationToken);
        var inputTask = ReceiveInputLoopAsync(socket, input, cancellationToken);
        try { await Task.WhenAny(captureTask, inputTask); }
        finally
        {
            consent.Stop(sessionId);
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "local user stopped", CancellationToken.None);
        }
    }

    private static async Task CaptureLoopAsync(ClientWebSocket socket, GdiScreenCapture capture, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var encoder = new ScreenFrameEncoder();
        var nextKeyFrame = Environment.TickCount64;
        while (await timer.WaitForNextTickAsync(token) && socket.State == WebSocketState.Open)
        {
            var frame = capture.Capture();
            var now = Environment.TickCount64;
            var forceKeyFrame = now >= nextKeyFrame;
            var packet = encoder.Encode(frame, forceKeyFrame);
            if (packet is null) continue;
            if (forceKeyFrame) nextKeyFrame = now + 2_000;
            await socket.SendAsync(packet, WebSocketMessageType.Binary, true, token);
        }
    }

    private static async Task ReceiveInputLoopAsync(ClientWebSocket socket, WindowsInputDispatcher input, CancellationToken token)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
                input.TryDispatch(buffer.AsSpan(0, result.Count));
        }
    }
}
