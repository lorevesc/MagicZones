// Drag reproduction harness: opens its own test window and drives real mouse input (SendInput)
// against it while MagicZones is running, then checks the window can still be grabbed.
// Build+run: tools\run-harness.ps1   (moves the mouse for ~15 s; only ever clicks its own window)
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class DragHarness
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public int type; public MOUSEINPUT mi; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);

    const uint MOVE = 0x0001, LDOWN = 0x0002, LUP = 0x0004, ABS = 0x8000, VDESK = 0x4000;

    static Form form;
    static int failures;

    [STAThread]
    static int Main()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        form = new Form
        {
            Text = "MZ Harness - non toccare il mouse",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(700, 400, 900, 500),
            BackColor = Color.FromArgb(40, 44, 52),
        };
        form.Shown += (s, e) => new Thread(Run) { IsBackground = true }.Start();
        Application.Run(form);
        return failures;
    }

    static void Run()
    {
        GetCursorPos(out var original);
        try
        {
            Thread.Sleep(600);
            Scenario("normale: trascina giu', fermo, rilascia", () => SetBounds(700, 400), 60, 700);
            Scenario("in cima: trascina giu' poco, fermo, rilascia", () => SetBounds(700, 4), 30, 700);
            Scenario("click senza muovere", () => SetBounds(700, 400), 0, 700);
            Scenario("massimizzata: trascina giu', fermo, rilascia", () => UI(() => form.WindowState = FormWindowState.Maximized), 80, 700);
            PopupScenarios();
            Console.WriteLine(failures == 0 ? "HARNESS OK" : "HARNESS: " + failures + " FALLITI");
        }
        catch (Exception e)
        {
            Console.WriteLine("ABORT: " + e.Message);
            failures++;
        }
        finally
        {
            MoveTo(original.X, original.Y);
            UI(() => form.Close());
        }
    }

    static void Scenario(string name, Action setup, int dragDown, int holdMs)
    {
        UI(() => { form.WindowState = FormWindowState.Normal; });
        UI(setup);
        UI(() => { form.Activate(); SetForegroundWindow(form.Handle); });
        Thread.Sleep(400);

        // 1) The gesture under test.
        var r0 = Visible();
        var grab = new Point(r0.X + 300, r0.Y + 14);
        Press(grab);
        for (int i = 1; i <= 10 && dragDown > 0; i++) { MoveTo(grab.X, grab.Y + dragDown * i / 10); Thread.Sleep(20); }
        Thread.Sleep(holdMs);
        MouseUp();
        Thread.Sleep(700);
        var r1 = Visible();

        // 2) Can we still grab it? Drag 150 px to the right.
        var g2 = new Point(r1.X + 300, r1.Y + 14);
        Press(g2);
        for (int i = 1; i <= 10; i++) { MoveTo(g2.X + 15 * i, g2.Y); Thread.Sleep(20); }
        Thread.Sleep(150);
        MouseUp();
        Thread.Sleep(700);
        var r2 = Visible();

        bool ok = Math.Abs(r2.X - r1.X - 150) <= 12;
        if (!ok) failures++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}\n     prima={r0} dopo gesto={r1} dopo ripresa={r2}");
    }

    // ---- Popup scenarios ------------------------------------------------------------------------
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    /// <summary>The MagicZones popup: its only visible window smaller than a monitor.</summary>
    static Rectangle FindPopup()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("MagicZones");
        if (procs.Length == 0) return Rectangle.Empty;
        uint pid = (uint)procs[0].Id;
        Rectangle found = Rectangle.Empty;
        EnumWindows((h, l) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out RECT r) && r.R - r.L < 1000 && r.B - r.T < 700 && r.R - r.L > 100)
                found = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Starts a drag on the title bar and waits for the popup; returns its rect.</summary>
    static Rectangle BeginDragWithPopup(Point grab)
    {
        Press(grab);
        for (int i = 1; i <= 5; i++) { MoveTo(grab.X, grab.Y + 4 * i); Thread.Sleep(25); }
        Thread.Sleep(250);
        var p = FindPopup();
        if (p.IsEmpty) throw new InvalidOperationException("il popup non e' comparso");
        return p;
    }

    static void Glide(Point from, Point to, int steps = 12)
    {
        for (int i = 1; i <= steps; i++) { MoveTo(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps); Thread.Sleep(18); }
    }

    static bool RegrabWorks(string name)
    {
        var r1 = Visible();
        var g = new Point(r1.X + Math.Min(300, r1.Width / 2), r1.Y + 14);
        Press(g);
        Glide(g, new Point(g.X - 150, g.Y), 10);
        Thread.Sleep(150);
        MouseUp();
        Thread.Sleep(900);
        var r2 = Visible();
        bool ok = Math.Abs(r2.X - (r1.X - 150)) <= 12 || Math.Abs(r2.X - r1.X) > 100 && Math.Abs(r2.Y - r1.Y) <= 12;
        if (!ok) failures++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}\n     prima della ripresa={r1} dopo={r2}");
        return ok;
    }

    static void PopupScenarios()
    {
        // Window low enough that the popup opens above it.
        UI(() => { form.WindowState = FormWindowState.Normal; SetBounds(700, 450); form.Activate(); });
        Thread.Sleep(400);
        var r0 = Visible();
        var grab = new Point(r0.X + 300, r0.Y + 14);

        // E) Hover a tile, change your mind, release outside the popup.
        var pop = BeginDragWithPopup(grab);
        var tile = new Point(pop.X + 409, pop.Y + 129);   // main monitor, zone 3 (see Popup.Layout math)
        var here = new Point(grab.X, grab.Y + 20);
        Glide(here, tile);
        Thread.Sleep(300);
        Glide(tile, new Point(grab.X, grab.Y + 60));
        Thread.Sleep(300);
        MouseUp();
        Thread.Sleep(500);
        var rE = Visible();
        bool notLaunched = rE.Width == r0.Width && Math.Abs(rE.X - r0.X) < 60;
        if (!notLaunched) failures++;
        Console.WriteLine($"{(notLaunched ? "ok  " : "FAIL")} popup: passo su una zona ed esco -> nessun lancio\n     {rE}");
        RegrabWorks("popup: dopo aver cambiato idea la finestra si riprende");

        // F) Launch to a zone and grab it again while it's still flying.
        UI(() => { SetBounds(700, 450); form.Activate(); });
        Thread.Sleep(400);
        r0 = Visible();
        grab = new Point(r0.X + 300, r0.Y + 14);
        pop = BeginDragWithPopup(grab);
        tile = new Point(pop.X + 409, pop.Y + 129);
        Glide(new Point(grab.X, grab.Y + 20), tile);
        Thread.Sleep(200);
        MouseUp();
        Thread.Sleep(900);
        var rF = Visible();
        bool launched = rF.X > 1600 && rF.Height > 1000;
        if (!launched) failures++;
        Console.WriteLine($"{(launched ? "ok  " : "FAIL")} popup: rilascio sulla zona 3 -> lanciata\n     {rF}");

        // G) Snapped window: drag out and release without choosing -> old size back, still grabbable.
        var g = new Point(rF.X + 300, rF.Y + 14);
        BeginDragWithPopup(g);
        Glide(new Point(g.X, g.Y + 20), new Point(g.X - 400, g.Y + 300));
        Thread.Sleep(300);
        MouseUp();
        Thread.Sleep(600);
        var rG = Visible();
        bool restored = Math.Abs(rG.Width - r0.Width) <= 4 && Math.Abs(rG.Height - r0.Height) <= 4;
        if (!restored) failures++;
        Console.WriteLine($"{(restored ? "ok  " : "FAIL")} popup: tolta dalla zona -> torna alla dimensione originale\n     {rG}");
        RegrabWorks("popup: dopo il ripristino la finestra si riprende");

        // H) Launch again and grab it right after landing, while MagicZones is still "settling" it.
        UI(() => { SetBounds(700, 450); form.Activate(); });
        Thread.Sleep(400);
        r0 = Visible();
        grab = new Point(r0.X + 300, r0.Y + 14);
        pop = BeginDragWithPopup(grab);
        tile = new Point(pop.X + 409, pop.Y + 129);
        Glide(new Point(grab.X, grab.Y + 20), tile);
        Thread.Sleep(200);
        MouseUp();
        Thread.Sleep(380);
        RegrabWorks("popup: ripresa subito dopo l'atterraggio (niente tira e molla)");
    }

    static void SetBounds(int x, int y) => form.Bounds = new Rectangle(x, y, 900, 500);

    static void UI(Action a) => form.Invoke(a);

    static Rectangle Visible()
    {
        Rectangle r = Rectangle.Empty;
        UI(() =>
        {
            DwmGetWindowAttribute(form.Handle, 9, out RECT rc, 16);
            r = Rectangle.FromLTRB(rc.L, rc.T, rc.R, rc.B);
        });
        return r;
    }

    static void Press(Point p)
    {
        MoveTo(p.X, p.Y);
        Thread.Sleep(60);
        var h = GetAncestor(WindowFromPoint(new POINT { X = p.X, Y = p.Y }), 2);
        IntPtr mine = IntPtr.Zero;
        UI(() => mine = form.Handle);
        if (h != mine) throw new InvalidOperationException($"sotto il cursore {p} non c'e' la finestra di test (hwnd {h}), interrompo");
        Send(0, 0, LDOWN);
        Thread.Sleep(60);
    }

    static void MouseUp() => Send(0, 0, LUP);

    static void MoveTo(int x, int y)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        Send((int)((x - vx) * 65535L / (vw - 1)), (int)((y - vy) * 65535L / (vh - 1)), MOVE | ABS | VDESK);
    }

    static void Send(int dx, int dy, uint flags)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
