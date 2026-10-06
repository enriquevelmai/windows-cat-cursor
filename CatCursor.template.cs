using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Cat Cursor")]
[assembly: AssemblyProduct("Cat Cursor")]
[assembly: AssemblyDescription("Turn your Windows cursors into cats - colours, animation, and custom pictures.")]
[assembly: AssemblyCompany("Cat Cursor")]
[assembly: AssemblyCopyright("MIT Licensed")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

// Cat Cursor - themes the whole Windows cursor set as cats (seven coats, two
// pointer styles, animated busy/loading pointers) and turns any picture into a
// cursor. Self-contained: every cursor file is embedded as a compressed resource.
//
// Layout of this file:
//   Assets        embedded cursor pack
//   CurCodec      decodes .cur/.ani bytes into bitmaps for the in-app preview
//   Engine        registry + SystemParametersInfo work (apply / revert / size)
//   Theme, Ui     colours (light & dark) and drawing helpers
//   controls      Card, TilePicker, CursorGrid, Segmented, ModernButton, StatusLabel
//   MainForm      the window
//   CustomForm    "make a cursor from my picture" dialog
//   Program       entry point (also: --screenshot <png> renders the window to a file)

// ---------------------------------------------------------------------------
// Assets
// ---------------------------------------------------------------------------
static class Assets
{
    public static readonly string[] COLOURS = { "Orange", "Black", "Grey", "White", "Siamese", "Calico", "Tuxedo" };

    // registry role -> file inside each colour folder ("Arrow" is resolved per style)
    public static readonly string[][] ROLE_FILES = {
        new[]{"Arrow","cat_cursor.cur"}, new[]{"Hand","cat_paw.cur"},
        new[]{"IBeam","cat_text.cur"},   new[]{"Wait","cat_busy.ani"},
        new[]{"AppStarting","cat_working.ani"}, new[]{"Help","cat_help.cur"},
        new[]{"No","cat_no.cur"},        new[]{"SizeNS","cat_ns.cur"},
        new[]{"SizeWE","cat_we.cur"},    new[]{"SizeNWSE","cat_nwse.cur"},
        new[]{"SizeNESW","cat_nesw.cur"}, new[]{"SizeAll","cat_move.cur"},
        new[]{"Crosshair","cat_cross.cur"}, new[]{"NWPen","cat_pen.cur"},
        new[]{"UpArrow","cat_up.cur"}
    };

    // friendly names, in the same order as ROLE_FILES (used by the preview grid)
    public static readonly string[] ROLE_NAMES = {
        "Normal", "Link", "Text", "Busy", "Working", "Help", "Unavailable",
        "Resize N-S", "Resize W-E", "Resize NW-SE", "Resize NE-SW", "Move",
        "Precision", "Pen", "Up"
    };

    public static readonly string[] ALL_ROLES = {
        "Arrow","Hand","Help","AppStarting","Wait","IBeam","Crosshair","No",
        "SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","NWPen","UpArrow"
    };

    public static readonly Dictionary<string, byte[]> Pack = Load();

    static Dictionary<string, byte[]> Load()
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CatCursor.cursors.zip");
        if (s == null) return dict;
        using (s)
        using (ZipArchive za = new ZipArchive(s, ZipArchiveMode.Read))
            foreach (ZipArchiveEntry e in za.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                using (Stream es = e.Open())
                using (MemoryStream ms = new MemoryStream())
                { es.CopyTo(ms); dict[e.FullName.Replace('\\', '/')] = ms.ToArray(); }
            }
        return dict;
    }

    public static string FileFor(string role, bool arrowStyle)
    {
        if (role == "Arrow") return arrowStyle ? "cat_arrow.cur" : "cat_cursor.cur";
        foreach (string[] rf in ROLE_FILES) if (rf[0] == role) return rf[1];
        return null;
    }

    public static byte[] Get(string colour, string file)
    {
        byte[] b;
        return Pack.TryGetValue(colour + "/" + file, out b) ? b : null;
    }

    public static Image Face(string colour)
    {
        byte[] b = Get(colour, "preview.png");
        if (b == null) return null;
        using (MemoryStream ms = new MemoryStream(b))
        using (Image img = Image.FromStream(ms))
            return new Bitmap(img);
    }

    public static Icon AppIcon()
    {
        byte[] b;
        if (!Pack.TryGetValue("icon.ico", out b)) return null;
        using (MemoryStream ms = new MemoryStream(b))
            return (Icon)new Icon(ms).Clone();
    }
}

// ---------------------------------------------------------------------------
// CurCodec - turn .cur / .ani bytes into Bitmaps (for the live preview grid)
// ---------------------------------------------------------------------------
static class CurCodec
{
    public class Clip
    {
        public Bitmap[] Frames;
        public int FrameMs = 100;
        public Point Hotspot;
    }

    public static Clip Decode(byte[] data, int wantSize)
    {
        if (data == null || data.Length < 12) return null;
        if (data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F')
            return DecodeAni(data, wantSize);
        Point hs;
        Bitmap bmp = DecodeCur(data, 0, data.Length, wantSize, out hs);
        if (bmp == null) return null;
        return new Clip { Frames = new[] { bmp }, Hotspot = hs };
    }

    static Clip DecodeAni(byte[] d, int want)
    {
        var frames = new List<Bitmap>();
        int jiffies = 6;
        Point hs = Point.Empty;
        int pos = 12;
        while (pos + 8 <= d.Length)
        {
            string fourcc = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            int size = BitConverter.ToInt32(d, pos + 4);
            if (fourcc == "anih" && size >= 36) jiffies = BitConverter.ToInt32(d, pos + 8 + 28);
            else if (fourcc == "LIST" && System.Text.Encoding.ASCII.GetString(d, pos + 8, 4) == "fram")
            {
                int sub = pos + 12, end = pos + 8 + size;
                while (sub + 8 <= end)
                {
                    string sc = System.Text.Encoding.ASCII.GetString(d, sub, 4);
                    int ss = BitConverter.ToInt32(d, sub + 4);
                    if (sc == "icon")
                    {
                        Bitmap b = DecodeCur(d, sub + 8, ss, want, out hs);
                        if (b != null) frames.Add(b);
                    }
                    sub += 8 + ss + (ss & 1);
                }
            }
            pos += 8 + size + (size & 1);
        }
        if (frames.Count == 0) return null;
        return new Clip { Frames = frames.ToArray(), FrameMs = Math.Max(16, jiffies * 1000 / 60), Hotspot = hs };
    }

    static Bitmap DecodeCur(byte[] d, int off, int len, int want, out Point hotspot)
    {
        hotspot = Point.Empty;
        if (len < 6) return null;
        int count = BitConverter.ToUInt16(d, off + 4);
        int best = -1, bestDiff = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int e = off + 6 + i * 16;
            int w = d[e]; if (w == 0) w = 256;
            int diff = Math.Abs(w - want);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        if (best < 0) return null;
        int entry = off + 6 + best * 16;
        hotspot = new Point(BitConverter.ToUInt16(d, entry + 4), BitConverter.ToUInt16(d, entry + 6));
        int size = BitConverter.ToInt32(d, entry + 8);
        int imgOff = off + BitConverter.ToInt32(d, entry + 12);
        if (imgOff + 8 > d.Length) return null;
        if (d[imgOff] == 0x89 && d[imgOff + 1] == (byte)'P')
        {
            using (MemoryStream ms = new MemoryStream(d, imgOff, size))
                return new Bitmap(Image.FromStream(ms));
        }
        int w2 = BitConverter.ToInt32(d, imgOff + 4);
        int h2 = BitConverter.ToInt32(d, imgOff + 8) / 2;
        int bpp = BitConverter.ToUInt16(d, imgOff + 14);
        if (bpp != 32 || w2 <= 0 || h2 <= 0) return null;
        Bitmap bmp = new Bitmap(w2, h2, PixelFormat.Format32bppArgb);
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w2, h2), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        int src = imgOff + 40;
        for (int y = 0; y < h2; y++)
        {
            int srcRow = src + (h2 - 1 - y) * w2 * 4;
            Marshal.Copy(d, srcRow, bd.Scan0 + y * bd.Stride, w2 * 4);
        }
        bmp.UnlockBits(bd);
        return bmp;
    }
}

// ---------------------------------------------------------------------------
// Engine - the part that actually changes Windows
// ---------------------------------------------------------------------------
static class Engine
{
    const string CURSOR_KEY = @"Control Panel\Cursors";
    const string SETTINGS_KEY = @"Software\CatCursor";
    static readonly int[] SIZES = { 32, 48, 64, 96, 128 };

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadCursorFromFile(string path);

    public static void Refresh() { SystemParametersInfo(0x57, 0, IntPtr.Zero, 0x03); }

    public static string Dir(string sub)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatCursor", sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- settings (what the user picked last time) ----
    public static bool ArrowStyle
    {
        get { return ReadSetting("PointerStyle", 0) == 1; }
        set { WriteSetting("PointerStyle", value ? 1 : 0); }
    }
    static int ReadSetting(string name, int def)
    {
        try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SETTINGS_KEY)) { object v = k == null ? null : k.GetValue(name); return v is int ? (int)v : def; } }
        catch { return def; }
    }
    static void WriteSetting(string name, int value)
    {
        try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SETTINGS_KEY)) k.SetValue(name, value, RegistryValueKind.DWord); } catch { }
    }

    // ---- themes ----
    public static void ApplyTheme(string colour, bool arrowStyle)
    {
        string dir = Dir(colour);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            foreach (string role in Assets.ALL_ROLES)
            {
                string file = Assets.FileFor(role, arrowStyle);
                byte[] data = Assets.Get(colour, file);
                if (data == null) continue;
                string path = Path.Combine(dir, file);
                File.WriteAllBytes(path, data);
                k.SetValue(role, path, RegistryValueKind.String);
            }
            k.SetValue("", "Cat Cursor (" + colour + ")", RegistryValueKind.String);
        }
        ArrowStyle = arrowStyle;
        Refresh();
    }

    public static void Revert()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            foreach (string r in Assets.ALL_ROLES) k.SetValue(r, "", RegistryValueKind.String);
            k.SetValue("", "Windows Default", RegistryValueKind.String);
        }
        Refresh();
    }

    // The colour of the currently-applied cat theme, or null.
    public static string CurrentColour()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY))
            {
                string v = k == null ? null : k.GetValue("") as string;
                if (v != null && v.StartsWith("Cat Cursor (") && v.EndsWith(")"))
                    return v.Substring(12, v.Length - 13);
                if (v == "Custom Cursor") return "custom";
            }
        }
        catch { }
        return null;
    }

    // ---- pointer size (same registry values Windows Settings writes) ----
    public static readonly int[] SIZE_PX = { 32, 48, 64, 96 };
    public static readonly string[] SIZE_NAMES = { "Normal", "Large", "Larger", "Huge" };

    public static int CurrentSizePx()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY))
            {
                object v = k == null ? null : k.GetValue("CursorBaseSize");
                if (v is int) return (int)v;
            }
        }
        catch { }
        return 32;
    }

    public static void SetSizePx(int px)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
            k.SetValue("CursorBaseSize", px, RegistryValueKind.DWord);
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility"))
                k.SetValue("CursorSize", 1 + (px - 32) / 16, RegistryValueKind.DWord);
        }
        catch { }
        Refresh();
    }

    // ---- custom picture -> cursor ----
    public static void ApplyCustom(string roleKey, Image img, double hxFrac, double hyFrac)
    {
        string dir = Dir("custom");
        byte[] cur = BuildCur(img, hxFrac, hyFrac);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            if (roleKey == "ALL")
            {
                string p = Path.Combine(dir, "custom_all.cur");
                File.WriteAllBytes(p, cur);
                foreach (string r in Assets.ALL_ROLES) k.SetValue(r, p, RegistryValueKind.String);
                k.SetValue("", "Custom Cursor", RegistryValueKind.String);
            }
            else
            {
                string p = Path.Combine(dir, "custom_" + roleKey + ".cur");
                File.WriteAllBytes(p, cur);
                k.SetValue(roleKey, p, RegistryValueKind.String);
            }
        }
        Refresh();
    }

    public static byte[] BuildCur(Image src, double hxFrac, double hyFrac)
    {
        List<byte[]> blobs = new List<byte[]>();
        List<int[]> dims = new List<int[]>();
        foreach (int s in SIZES)
        {
            using (Bitmap bmp = RenderFitted(src, s))
            {
                blobs.Add(Dib(bmp));
                dims.Add(new int[] { s, s, (int)Math.Round((s - 1) * hxFrac), (int)Math.Round((s - 1) * hyFrac) });
            }
        }
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write((short)0); bw.Write((short)2); bw.Write((short)blobs.Count);
            int offset = 6 + 16 * blobs.Count;
            for (int i = 0; i < blobs.Count; i++)
            {
                int[] dm = dims[i];
                bw.Write((byte)(dm[0] >= 256 ? 0 : dm[0]));
                bw.Write((byte)(dm[1] >= 256 ? 0 : dm[1]));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)dm[2]); bw.Write((short)dm[3]);
                bw.Write(blobs[i].Length);
                bw.Write(offset);
                offset += blobs[i].Length;
            }
            foreach (byte[] b in blobs) bw.Write(b);
            return ms.ToArray();
        }
    }

    // Scale the picture to fit an s x s square, centred, keeping transparency.
    public static Bitmap RenderFitted(Image src, int s)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            float scale = Math.Min((float)s / src.Width, (float)s / src.Height);
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));
            g.DrawImage(src, (s - w) / 2, (s - h) / 2, w, h);
        }
        return bmp;
    }

    static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        byte[] buf = new byte[stride * h];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(data);
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) ms.Write(buf, y * stride, w * 4);
            int rowBytes = ((w + 31) / 32) * 4;
            byte[] row = new byte[rowBytes];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(row, 0, rowBytes);
                for (int x = 0; x < w; x++)
                    if (buf[y * stride + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                ms.Write(row, 0, rowBytes);
            }
            return ms.ToArray();
        }
    }

    public static Image LoadImageUnlocked(string path)
    {
        return Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
    }

    // ---- real cursors for "hover to try it" ----
    static readonly Dictionary<string, Cursor> realCursors = new Dictionary<string, Cursor>(StringComparer.OrdinalIgnoreCase);

    public static Cursor RealCursor(string colour, string file)
    {
        string key = colour + "/" + file;
        Cursor c;
        if (realCursors.TryGetValue(key, out c)) return c;
        byte[] data = Assets.Get(colour, file);
        if (data == null) return null;
        string path = Path.Combine(Dir(Path.Combine("preview", colour)), file);
        if (!File.Exists(path) || new FileInfo(path).Length != data.Length) File.WriteAllBytes(path, data);
        c = RealCursorFromFile(path);
        realCursors[key] = c;
        return c;
    }

    public static Cursor RealCursorFromFile(string path)
    {
        IntPtr h = LoadCursorFromFile(path);
        return h == IntPtr.Zero ? null : new Cursor(h);
    }
}

// ---------------------------------------------------------------------------
// Theme + drawing helpers
// ---------------------------------------------------------------------------
static class Theme
{
    public static bool Dark;
    public static Color Bg, Card, CardBorder, Text, SubText, Accent, AccentHover, AccentPressed, OnAccent,
                        Secondary, SecondaryHover, SecondaryBorder, Hover, Selected, Success, Danger, Track;

    public static void Init() { Init(IsDarkMode()); }

    public static void Init(bool dark)
    {
        Dark = dark;
        if (Dark)
        {
            Bg = Color.FromArgb(30, 30, 35); Card = Color.FromArgb(42, 42, 49); CardBorder = Color.FromArgb(60, 60, 70);
            Text = Color.FromArgb(240, 238, 234); SubText = Color.FromArgb(165, 160, 154);
            Accent = Color.FromArgb(255, 150, 58); AccentHover = Color.FromArgb(255, 165, 88); AccentPressed = Color.FromArgb(232, 132, 44);
            OnAccent = Color.FromArgb(40, 26, 12);
            Secondary = Color.FromArgb(54, 54, 63); SecondaryHover = Color.FromArgb(66, 66, 76); SecondaryBorder = Color.FromArgb(82, 82, 94);
            Hover = Color.FromArgb(56, 54, 58); Selected = Color.FromArgb(70, 54, 40);
            Success = Color.FromArgb(92, 200, 130); Danger = Color.FromArgb(240, 100, 100); Track = Color.FromArgb(54, 54, 63);
        }
        else
        {
            Bg = Color.FromArgb(247, 243, 237); Card = Color.White; CardBorder = Color.FromArgb(232, 225, 214);
            Text = Color.FromArgb(44, 34, 28); SubText = Color.FromArgb(128, 114, 102);
            Accent = Color.FromArgb(246, 138, 44); AccentHover = Color.FromArgb(255, 152, 66); AccentPressed = Color.FromArgb(226, 120, 32);
            OnAccent = Color.White;
            Secondary = Color.White; SecondaryHover = Color.FromArgb(252, 247, 240); SecondaryBorder = Color.FromArgb(216, 206, 194);
            Hover = Color.FromArgb(252, 246, 238); Selected = Color.FromArgb(255, 241, 226);
            Success = Color.FromArgb(44, 150, 88); Danger = Color.FromArgb(206, 56, 56); Track = Color.FromArgb(242, 236, 228);
        }
    }

    static bool IsDarkMode()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Dark title bar on Windows 10 1809+ / 11 (silently ignored elsewhere).
    public static void ApplyTitleBar(Form f)
    {
        try
        {
            int dark = Dark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref dark, 4) != 0)
                DwmSetWindowAttribute(f.Handle, 19, ref dark, 4);
        }
        catch { }
    }
}

static class Ui
{
    public static Font F(float size, bool bold)
    {
        return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Round(r, radius))
        {
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
            if (border.HasValue) using (Pen pen = new Pen(border.Value, 1f)) g.DrawPath(pen, p);
        }
    }

    public static void Prep(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, ContentAlignment align)
    {
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (s.Contains("\n") ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
        switch (align)
        {
            case ContentAlignment.MiddleLeft: flags |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
            case ContentAlignment.MiddleCenter: flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter; break;
            case ContentAlignment.MiddleRight: flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter; break;
            case ContentAlignment.TopLeft: flags |= TextFormatFlags.Left | TextFormatFlags.Top; break;
            default: flags |= TextFormatFlags.Left | TextFormatFlags.Top; break;
        }
        TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags);
    }

    public static float Scale(Control c)
    {
        using (Graphics g = c.CreateGraphics()) return g.DpiX / 96f;
    }

    public static void DrawFocus(Graphics g, RectangleF r, float radius)
    {
        using (GraphicsPath p = Round(r, radius))
        using (Pen pen = new Pen(Theme.Accent, 2f))
            g.DrawPath(pen, p);
    }
}

// Buffered base for all custom controls.
class Canvas : Control
{
    public Canvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        TabStop = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected bool ShowFocus { get { return Focused && ShowFocusCues; } }
}

// ---------------------------------------------------------------------------
// Card - rounded surface with a title and an optional step number
// ---------------------------------------------------------------------------
class Card : Panel
{
    public string Title = "", Step = "", Hint = "";
    public const int TitleHeight = 40;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(Theme.Bg);
        float s = Ui.Scale(this);
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Ui.FillRound(g, r, 12 * s, Theme.Card, Theme.CardBorder);
        float x = 16 * s;
        if (Step.Length > 0)
        {
            RectangleF badge = new RectangleF(x, 11 * s, 20 * s, 20 * s);
            Ui.FillRound(g, badge, 10 * s, Theme.Accent, null);
            using (Font f = Ui.F(8.5f, true)) Ui.Text(g, Step, f, Theme.OnAccent, badge, ContentAlignment.MiddleCenter);
            x += 28 * s;
        }
        using (Font f = Ui.F(10.5f, true))
            Ui.Text(g, Title, f, Theme.Text, new RectangleF(x, 8 * s, Width - x - 16 * s, 26 * s), ContentAlignment.MiddleLeft);
        if (Hint.Length > 0)
            using (Font f = Ui.F(8.5f, false))
                Ui.Text(g, Hint, f, Theme.SubText, new RectangleF(x, 8 * s, Width - x - 16 * s, 26 * s), ContentAlignment.MiddleRight);
    }
}

// ---------------------------------------------------------------------------
// TilePicker - a row (or grid) of picture tiles, one selected
// ---------------------------------------------------------------------------
class TilePicker : Canvas
{
    public class Tile { public string Name; public string Key; public Image Image; }
    public readonly List<Tile> Tiles = new List<Tile>();
    public int Columns = 7;
    public float ImageFrac = 0.62f;          // image size relative to the tile height
    public bool ShowNames = true;
    int selected = 0, hover = -1;
    public event EventHandler SelectionChanged;

    public int SelectedIndex
    {
        get { return selected; }
        set
        {
            int v = Math.Max(0, Math.Min(Tiles.Count - 1, value));
            if (v == selected) return;
            selected = v; Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }
    }
    public Tile Selected { get { return Tiles.Count == 0 ? null : Tiles[selected]; } }

    RectangleF CellRect(int i)
    {
        int rows = (Tiles.Count + Columns - 1) / Columns;
        float gap = 6 * Ui.Scale(this);
        float cw = (Width - gap * (Columns - 1)) / Columns;
        float ch = (Height - gap * (rows - 1)) / rows;
        int r = i / Columns, c = i % Columns;
        return new RectangleF(c * (cw + gap), r * (ch + gap), cw, ch);
    }

    int HitTest(Point p)
    {
        for (int i = 0; i < Tiles.Count; i++) if (CellRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int h = HitTest(e.Location);
        if (h >= 0) SelectedIndex = h;
    }
    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
        else if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
        else if (e.KeyCode == Keys.Up) SelectedIndex = selected - Columns;
        else if (e.KeyCode == Keys.Down) SelectedIndex = selected + Columns;
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(BackColor);
        float s = Ui.Scale(this);
        using (Font f = Ui.F(8.5f, false))
        using (Font fb = Ui.F(8.5f, true))
            for (int i = 0; i < Tiles.Count; i++)
            {
                RectangleF r = CellRect(i);
                bool sel = i == selected, hov = i == hover;
                if (sel) Ui.FillRound(g, r, 10 * s, Theme.Selected, Theme.Accent);
                else if (hov) Ui.FillRound(g, r, 10 * s, Theme.Hover, Theme.CardBorder);
                if (sel && ShowFocus) Ui.DrawFocus(g, RectangleF.Inflate(r, -1, -1), 9 * s);
                float nameH = ShowNames ? 18 * s : 0;
                float img = Math.Min(r.Width - 10 * s, (r.Height - nameH) * (ShowNames ? 0.78f : 0.70f));
                Tile t = Tiles[i];
                if (t.Image != null)
                    g.DrawImage(t.Image, r.X + (r.Width - img) / 2, r.Y + (r.Height - nameH - img) / 2, img, img);
                if (ShowNames)
                    Ui.Text(g, t.Name, sel ? fb : f, sel ? Theme.Text : Theme.SubText,
                            new RectangleF(r.X, r.Bottom - nameH - 4 * s, r.Width, nameH), ContentAlignment.MiddleCenter);
            }
    }
}

// ---------------------------------------------------------------------------
// CursorGrid - every pointer of the selected coat, animated, hover to try it
// ---------------------------------------------------------------------------
class CursorGrid : Canvas
{
    class Cell { public string Name; public string File; public CurCodec.Clip Clip; public int Frame; public int Elapsed; }
    readonly List<Cell> cells = new List<Cell>();
    readonly Timer timer = new Timer();
    string colour; bool arrow;
    int hover = -1;
    public int Columns = 5;

    public CursorGrid()
    {
        timer.Interval = 33;
        timer.Tick += delegate
        {
            bool any = false;
            foreach (Cell c in cells)
            {
                if (c.Clip == null || c.Clip.Frames.Length < 2) continue;
                c.Elapsed += timer.Interval;
                if (c.Elapsed >= c.Clip.FrameMs)
                {
                    c.Elapsed = 0; c.Frame = (c.Frame + 1) % c.Clip.Frames.Length; any = true;
                }
            }
            if (any) Invalidate();
        };
    }

    public void Load(string colour, bool arrowStyle)
    {
        this.colour = colour; this.arrow = arrowStyle;
        foreach (Cell c in cells) if (c.Clip != null) foreach (Bitmap b in c.Clip.Frames) b.Dispose();
        cells.Clear();
        for (int i = 0; i < Assets.ROLE_FILES.Length; i++)
        {
            string role = Assets.ROLE_FILES[i][0];
            string file = Assets.FileFor(role, arrowStyle);
            Cell c = new Cell { Name = Assets.ROLE_NAMES[i], File = file };
            c.Clip = CurCodec.Decode(Assets.Get(colour, file), 48);
            cells.Add(c);
        }
        timer.Enabled = true;
        Invalidate();
    }

    RectangleF CellRect(int i)
    {
        int rows = (cells.Count + Columns - 1) / Columns;
        float gap = 4 * Ui.Scale(this);
        float cw = (Width - gap * (Columns - 1)) / Columns;
        float ch = (Height - gap * (rows - 1)) / rows;
        return new RectangleF((i % Columns) * (cw + gap), (i / Columns) * (ch + gap), cw, ch);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < cells.Count; i++) if (CellRect(i).Contains(e.Location)) { h = i; break; }
        if (h == hover) return;
        hover = h;
        Cursor real = h >= 0 ? Engine.RealCursor(colour, cells[h].File) : null;
        Cursor = real ?? Cursors.Default;
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Cursor = Cursors.Default; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(BackColor);
        float s = Ui.Scale(this);
        using (Font f = Ui.F(8f, false))
        using (Font fb = Ui.F(8f, true))
            for (int i = 0; i < cells.Count; i++)
            {
                RectangleF r = CellRect(i);
                Cell c = cells[i];
                bool hov = i == hover;
                if (hov) Ui.FillRound(g, r, 8 * s, Theme.Hover, Theme.CardBorder);
                float nameH = 16 * s;
                float img = Math.Min(40 * s, r.Height - nameH - 8 * s);
                if (c.Clip != null)
                {
                    Bitmap b = c.Clip.Frames[c.Frame % c.Clip.Frames.Length];
                    g.DrawImage(b, r.X + (r.Width - img) / 2, r.Y + 5 * s, img, img);
                }
                Ui.Text(g, c.Name, hov ? fb : f, hov ? Theme.Text : Theme.SubText,
                        new RectangleF(r.X, r.Bottom - nameH - 3 * s, r.Width, nameH), ContentAlignment.MiddleCenter);
            }
    }
}

// ---------------------------------------------------------------------------
// Segmented - a pill with N options, one selected
// ---------------------------------------------------------------------------
class Segmented : Canvas
{
    public string[] Items = new string[0];
    int selected = 0, hover = -1;
    public event EventHandler SelectionChanged;

    public int SelectedIndex
    {
        get { return selected; }
        set
        {
            int v = Math.Max(0, Math.Min(Items.Length - 1, value));
            if (v == selected) return;
            selected = v; Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }
    }

    RectangleF SegRect(int i)
    {
        float pad = 3 * Ui.Scale(this);
        float w = (Width - pad * 2) / Items.Length;
        return new RectangleF(pad + i * w, pad, w, Height - pad * 2);
    }
    int HitTest(Point p) { for (int i = 0; i < Items.Length; i++) if (SegRect(i).Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); int h = HitTest(e.Location); if (h >= 0) SelectedIndex = h; }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { SelectedIndex = selected - 1; e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { SelectedIndex = selected + 1; e.Handled = true; }
        else base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(BackColor);
        float s = Ui.Scale(this);
        RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Ui.FillRound(g, outer, Height / 2f, Theme.Track, Theme.CardBorder);
        if (ShowFocus) Ui.DrawFocus(g, outer, Height / 2f);
        using (Font f = Ui.F(9f, false))
        using (Font fb = Ui.F(9f, true))
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF r = SegRect(i);
                bool sel = i == selected;
                if (sel) Ui.FillRound(g, r, r.Height / 2f, Theme.Card, Theme.SecondaryBorder);
                else if (i == hover) Ui.FillRound(g, r, r.Height / 2f, Theme.Hover, null);
                Ui.Text(g, Items[i], sel ? fb : f, sel ? Theme.Text : Theme.SubText, r, ContentAlignment.MiddleCenter);
            }
    }
}

// ---------------------------------------------------------------------------
// ModernButton - rounded, with primary / secondary / link looks
// ---------------------------------------------------------------------------
class ModernButton : Canvas
{
    public enum Kind { Primary, Secondary, Link }
    public Kind Style = Kind.Secondary;
    public float FontSize = 10f;
    bool hover, pressed;

    public ModernButton() { Cursor = Cursors.Hand; }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        else base.OnKeyDown(e);
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(BackColor);
        float s = Ui.Scale(this);
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Color fill, text; Color? border = null;
        if (Style == Kind.Primary)
        {
            fill = !Enabled ? Color.FromArgb(120, Theme.Accent) : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
            text = Theme.OnAccent;
        }
        else if (Style == Kind.Secondary)
        {
            fill = pressed ? Theme.Hover : hover ? Theme.SecondaryHover : Theme.Secondary;
            text = Enabled ? Theme.Text : Theme.SubText;
            border = Theme.SecondaryBorder;
        }
        else
        {
            fill = hover ? Theme.Hover : BackColor;
            text = hover ? Theme.Text : Theme.Accent;
        }
        if (Style != Kind.Link || hover) Ui.FillRound(g, r, 9 * s, fill, border);
        if (ShowFocus) Ui.DrawFocus(g, RectangleF.Inflate(r, -1, -1), 8 * s);
        using (Font f = Ui.F(FontSize, Style == Kind.Primary))
            Ui.Text(g, Text, f, text, r, ContentAlignment.MiddleCenter);
    }
}

// ---------------------------------------------------------------------------
// StatusLabel - a coloured dot and one line of feedback
// ---------------------------------------------------------------------------
class StatusLabel : Canvas
{
    public enum Level { Info, Success, Error }
    Level level = Level.Info;
    public StatusLabel() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }

    public void Show(string text, Level lvl) { Text = text; level = lvl; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(BackColor);
        float s = Ui.Scale(this);
        Color dot = level == Level.Success ? Theme.Success : level == Level.Error ? Theme.Danger : Theme.SubText;
        float d = 8 * s;
        using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, 2 * s, (Height - d) / 2, d, d);
        using (Font f = Ui.F(9f, false))
            Ui.Text(g, Text, f, level == Level.Error ? Theme.Danger : Theme.Text,
                    new RectangleF(16 * s, 0, Width - 16 * s, Height), ContentAlignment.MiddleLeft);
    }
}

// ---------------------------------------------------------------------------
// MainForm
// ---------------------------------------------------------------------------
class MainForm : Form
{
    public const string VERSION = "2.0.0";
    public const string REPO_URL = "https://github.com/enriquevelmai/windows-cat-cursor";

    readonly TilePicker coats = new TilePicker();
    readonly CursorGrid grid = new CursorGrid();
    readonly Segmented style = new Segmented();
    readonly Segmented size = new Segmented();
    readonly ModernButton apply = new ModernButton();
    readonly ModernButton restore = new ModernButton();
    readonly ModernButton custom = new ModernButton();
    readonly StatusLabel status = new StatusLabel();
    readonly PictureBox logo = new PictureBox();
    readonly Label title = new Label(), subtitle = new Label(), pill = new Label(), ver = new Label();
    readonly LinkLabel link = new LinkLabel();

    public MainForm()
    {
        Text = "Cat Cursor";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(548, 708);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Ui.F(9f, false);
        Icon ic = Assets.AppIcon();
        if (ic != null) Icon = ic;

        const int M = 16, W = 548 - 2 * M;   // margin, content width

        // ---- header ----
        logo.Bounds = new Rectangle(M, 14, 44, 44);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.BackColor = Theme.Bg;
        Controls.Add(logo);

        title.Text = "Cat Cursor";
        title.Font = Ui.F(16f, true);
        title.ForeColor = Theme.Text;
        title.AutoSize = true;
        title.Location = new Point(M + 54, 10);
        Controls.Add(title);

        subtitle.Text = "Make every Windows pointer a cat.";
        subtitle.Font = Ui.F(9f, false);
        subtitle.ForeColor = Theme.SubText;
        subtitle.AutoSize = true;
        subtitle.Location = new Point(M + 56, 40);
        Controls.Add(subtitle);

        pill.Font = Ui.F(8.5f, true);
        pill.AutoSize = false;
        pill.TextAlign = ContentAlignment.MiddleCenter;
        pill.Bounds = new Rectangle(548 - M - 150, 24, 150, 24);
        pill.Paint += PaintPill;
        Controls.Add(pill);

        // ---- 1. coat ----
        Card c1 = new Card { Title = "Choose your cat", Step = "1", Bounds = new Rectangle(M, 72, W, 132) };
        Controls.Add(c1);
        coats.Bounds = new Rectangle(12, Card.TitleHeight, W - 24, 132 - Card.TitleHeight - 10);
        coats.BackColor = Theme.Card;
        coats.Columns = Assets.COLOURS.Length;
        foreach (string col in Assets.COLOURS)
            coats.Tiles.Add(new TilePicker.Tile { Name = col, Key = col, Image = Assets.Face(col) });
        c1.Controls.Add(coats);

        // ---- 2. preview ----
        Card c2 = new Card { Title = "Every pointer, live", Step = "2", Hint = "Hover a pointer to try it", Bounds = new Rectangle(M, 212, W, 262) };
        Controls.Add(c2);
        grid.Bounds = new Rectangle(12, Card.TitleHeight, W - 24, 262 - Card.TitleHeight - 10);
        grid.BackColor = Theme.Card;
        c2.Controls.Add(grid);

        // ---- 3. options ----
        Card c3 = new Card { Title = "Pointer style & size", Step = "3", Bounds = new Rectangle(M, 482, W, 92) };
        Controls.Add(c3);
        Label l1 = Lbl("Normal pointer", 16, Card.TitleHeight + 2, Theme.Card);
        c3.Controls.Add(l1);
        style.Items = new[] { "Cat face", "Arrow + cat" };
        style.Bounds = new Rectangle(16, Card.TitleHeight + 20, 200, 30);
        style.BackColor = Theme.Card;
        c3.Controls.Add(style);
        Label l2 = Lbl("Size", 236, Card.TitleHeight + 2, Theme.Card);
        c3.Controls.Add(l2);
        size.Items = Engine.SIZE_NAMES;
        size.Bounds = new Rectangle(236, Card.TitleHeight + 20, W - 236 - 16, 30);
        size.BackColor = Theme.Card;
        c3.Controls.Add(size);

        // ---- actions ----
        apply.Style = ModernButton.Kind.Primary; apply.FontSize = 11f;
        apply.Bounds = new Rectangle(M, 588, 322, 46);
        apply.BackColor = Theme.Bg;
        Controls.Add(apply);

        restore.Style = ModernButton.Kind.Secondary; restore.Text = "Restore Windows cursors";
        restore.Bounds = new Rectangle(M + 332, 588, W - 332, 46);
        restore.BackColor = Theme.Bg;
        Controls.Add(restore);

        custom.Style = ModernButton.Kind.Link; custom.Text = "Make a cursor from my own picture…";
        custom.FontSize = 9.5f;
        custom.Bounds = new Rectangle(M - 6, 642, 300, 30);
        custom.BackColor = Theme.Bg;
        Controls.Add(custom);

        link.Text = "GitHub";
        link.Font = Ui.F(9f, false);
        link.LinkColor = Theme.SubText; link.ActiveLinkColor = Theme.Accent; link.VisitedLinkColor = Theme.SubText;
        link.LinkBehavior = LinkBehavior.HoverUnderline;
        link.AutoSize = true;
        link.BackColor = Theme.Bg;
        link.Location = new Point(548 - M - 100, 648);
        link.LinkClicked += delegate { try { Process.Start(REPO_URL); } catch { } };
        Controls.Add(link);

        ver.Text = "v" + VERSION;
        ver.Font = Ui.F(9f, false);
        ver.ForeColor = Theme.SubText;
        ver.AutoSize = false; ver.TextAlign = ContentAlignment.MiddleRight;
        ver.Bounds = new Rectangle(548 - M - 50, 646, 50, 22);
        Controls.Add(ver);

        status.Bounds = new Rectangle(M, 678, W, 22);
        status.BackColor = Theme.Bg;
        Controls.Add(status);

        // ---- behaviour ----
        coats.SelectionChanged += delegate { RefreshPreview(); };
        style.SelectionChanged += delegate { RefreshPreview(); };
        apply.Click += delegate { DoApply(); };
        restore.Click += delegate { DoRestore(); };
        size.SelectionChanged += delegate { DoSize(); };
        custom.Click += delegate
        {
            using (CustomForm cf = new CustomForm(coats.Selected.Key, style.SelectedIndex == 1))
            {
                if (cf.ShowDialog(this) == DialogResult.OK)
                {
                    status.Show(cf.ResultMessage, StatusLabel.Level.Success);
                    RefreshPill();
                }
            }
        };

        // ---- initial state ----
        string current = Engine.CurrentColour();
        int idx = current == null ? 0 : Array.IndexOf(Assets.COLOURS, current);
        coats.SelectedIndex = idx < 0 ? 0 : idx;
        style.SelectedIndex = Engine.ArrowStyle ? 1 : 0;
        size.SelectedIndex = Math.Max(0, Array.IndexOf(Engine.SIZE_PX, Engine.CurrentSizePx()));
        sizeReady = true;
        RefreshPreview();
        RefreshPill();
        if (Assets.Pack.Count == 0) status.Show("The cursor pack is missing from this build.", StatusLabel.Level.Error);
        else if (current != null && current != "custom") status.Show(current + " cats are active. Pick another coat or restore the defaults.", StatusLabel.Level.Info);
        else if (current == "custom") status.Show("A custom picture cursor is active.", StatusLabel.Level.Info);
        else status.Show("Pick a coat, then click the orange button.", StatusLabel.Level.Info);

        Load += delegate { Theme.ApplyTitleBar(this); };
    }

    bool sizeReady;

    static Label Lbl(string text, int x, int y, Color back)
    {
        Label l = new Label { Text = text, AutoSize = true, Location = new Point(x, y), BackColor = back };
        l.Font = Ui.F(8.5f, false);
        l.ForeColor = Theme.SubText;
        return l;
    }

    void RefreshPreview()
    {
        string colour = coats.Selected.Key;
        Image old = logo.Image;
        logo.Image = Assets.Face(colour);
        if (old != null) old.Dispose();
        grid.Load(colour, style.SelectedIndex == 1);
        apply.Text = "Apply " + colour + " cats";
    }

    void RefreshPill() { pill.Invalidate(); }

    void PaintPill(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Prep(g);
        g.Clear(Theme.Bg);
        string current = Engine.CurrentColour();
        bool on = current != null;
        string txt = current == null ? "Windows default" : current == "custom" ? "Custom picture" : current + " active";
        Color fill = on ? Theme.Selected : Theme.Track;
        Color dot = on ? Theme.Success : Theme.SubText;
        float s = Ui.Scale(pill);
        using (Font f = Ui.F(8.5f, true))
        {
            int tw = TextRenderer.MeasureText(txt, f).Width;
            float w = tw + 30 * s, h = pill.Height - 2;
            RectangleF r = new RectangleF(pill.Width - w - 1, 1, w, h);
            Ui.FillRound(g, r, h / 2, fill, on ? Theme.Accent : Theme.CardBorder);
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, r.X + 10 * s, r.Y + (h - 7 * s) / 2, 7 * s, 7 * s);
            Ui.Text(g, txt, f, Theme.Text, new RectangleF(r.X + 22 * s, r.Y, r.Width - 22 * s, h), ContentAlignment.MiddleLeft);
        }
    }

    void DoApply()
    {
        try
        {
            string colour = coats.Selected.Key;
            Engine.ApplyTheme(colour, style.SelectedIndex == 1);
            status.Show(colour + " cats applied. Enjoy! \U0001F43E", StatusLabel.Level.Success);
        }
        catch (Exception ex) { status.Show("Couldn't apply: " + ex.Message, StatusLabel.Level.Error); }
        RefreshPill();
    }

    void DoRestore()
    {
        try
        {
            Engine.Revert();
            status.Show("Default Windows cursors restored.", StatusLabel.Level.Success);
        }
        catch (Exception ex) { status.Show("Couldn't restore: " + ex.Message, StatusLabel.Level.Error); }
        RefreshPill();
    }

    void DoSize()
    {
        if (!sizeReady) return;
        try
        {
            int px = Engine.SIZE_PX[size.SelectedIndex];
            Engine.SetSizePx(px);
            status.Show("Pointer size: " + Engine.SIZE_NAMES[size.SelectedIndex].ToLower() + " (" + px + " px). Applies to any cursor set.", StatusLabel.Level.Success);
        }
        catch (Exception ex) { status.Show("Couldn't change size: " + ex.Message, StatusLabel.Level.Error); }
    }
}

// ---------------------------------------------------------------------------
// CustomForm - turn a picture into a cursor
// ---------------------------------------------------------------------------
class CustomForm : Form
{
    public string ResultMessage = "";
    readonly string colour; readonly bool arrowStyle;
    Image picked;
    double hx = 0, hy = 0;                       // hotspot as a fraction of the image
    readonly DropZone zone = new DropZone();
    readonly TilePicker roles = new TilePicker();
    readonly Segmented hot = new Segmented();
    readonly ResultPreview result = new ResultPreview();
    readonly ModernButton browse = new ModernButton(), use = new ModernButton(), cancel = new ModernButton();
    readonly StatusLabel status = new StatusLabel();

    static readonly string[] ROLE_KEYS = { "Arrow", "Hand", "IBeam", "Wait", "AppStarting", "Help", "No", "SizeAll", "ALL" };
    static readonly string[] ROLE_LABELS = { "Normal", "Link", "Text", "Busy", "Working", "Help", "Unavailable", "Move", "All pointers" };

    public CustomForm(string colour, bool arrowStyle)
    {
        this.colour = colour; this.arrowStyle = arrowStyle;
        Text = "Make a cursor from a picture";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(548, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        Font = Ui.F(9f, false);
        Icon ic = Assets.AppIcon();
        if (ic != null) Icon = ic;
        AllowDrop = true;

        const int M = 16, W = 548 - 2 * M;

        // ---- picture ----
        Card c1 = new Card { Title = "Your picture", Step = "1", Hint = "PNG with transparency looks best", Bounds = new Rectangle(M, M, W, 232) };
        Controls.Add(c1);
        zone.Bounds = new Rectangle(16, Card.TitleHeight, 170, 150);
        zone.BackColor = Theme.Card;
        zone.Click += delegate { Browse(); };
        zone.HotspotPicked += delegate (PointF p) { hx = p.X; hy = p.Y; hot.SelectedIndex = 3; UpdateResult(); };
        c1.Controls.Add(zone);

        Label l = new Label { Text = "Click point", AutoSize = true, Location = new Point(204, Card.TitleHeight + 2), BackColor = Theme.Card, ForeColor = Theme.SubText, Font = Ui.F(8.5f, false) };
        c1.Controls.Add(l);
        hot.Items = new[] { "Top-left", "Top", "Centre", "Custom" };
        hot.Bounds = new Rectangle(204, Card.TitleHeight + 20, W - 204 - 16, 30);
        hot.BackColor = Theme.Card;
        hot.SelectionChanged += delegate
        {
            switch (hot.SelectedIndex)
            {
                case 0: hx = 0; hy = 0; break;
                case 1: hx = 0.5; hy = 0; break;
                case 2: hx = 0.5; hy = 0.5; break;
            }
            UpdateResult();
        };
        c1.Controls.Add(hot);
        Label l3 = new Label { Text = "The exact pixel that clicks. Choose Custom, then click the spot on your picture.", AutoSize = false, Bounds = new Rectangle(204, Card.TitleHeight + 56, W - 204 - 16, 34), BackColor = Theme.Card, ForeColor = Theme.SubText, Font = Ui.F(8.5f, false) };
        c1.Controls.Add(l3);

        Label l4 = new Label { Text = "Result at real size", AutoSize = true, Location = new Point(204, Card.TitleHeight + 94), BackColor = Theme.Card, ForeColor = Theme.SubText, Font = Ui.F(8.5f, false) };
        c1.Controls.Add(l4);
        result.Bounds = new Rectangle(204, Card.TitleHeight + 112, W - 204 - 16, 60);
        result.BackColor = Theme.Card;
        c1.Controls.Add(result);

        browse.Style = ModernButton.Kind.Secondary; browse.Text = "Choose picture…"; browse.FontSize = 9f;
        browse.Bounds = new Rectangle(16, Card.TitleHeight + 156, 170, 28);
        browse.BackColor = Theme.Card;
        browse.Click += delegate { Browse(); };
        c1.Controls.Add(browse);

        // ---- role ----
        Card c2 = new Card { Title = "Which pointer does it replace?", Step = "2", Bounds = new Rectangle(M, 264, W, 200) };
        Controls.Add(c2);
        roles.Bounds = new Rectangle(12, Card.TitleHeight, W - 24, 200 - Card.TitleHeight - 10);
        roles.BackColor = Theme.Card;
        roles.Columns = 5;
        for (int i = 0; i < ROLE_KEYS.Length; i++)
        {
            Image img = null;
            if (ROLE_KEYS[i] != "ALL")
            {
                CurCodec.Clip clip = CurCodec.Decode(Assets.Get(colour, Assets.FileFor(ROLE_KEYS[i], arrowStyle)), 48);
                if (clip != null) img = clip.Frames[0];
            }
            else img = Assets.Face(colour);
            roles.Tiles.Add(new TilePicker.Tile { Name = ROLE_LABELS[i], Key = ROLE_KEYS[i], Image = img });
        }
        c2.Controls.Add(roles);

        // ---- actions ----
        use.Style = ModernButton.Kind.Primary; use.Text = "Use this picture as my cursor"; use.FontSize = 10.5f;
        use.Bounds = new Rectangle(M, 480, 340, 42);
        use.BackColor = Theme.Bg;
        use.Enabled = false;
        use.Click += delegate { Apply(); };
        Controls.Add(use);

        cancel.Style = ModernButton.Kind.Secondary; cancel.Text = "Cancel";
        cancel.Bounds = new Rectangle(M + 350, 480, W - 350, 42);
        cancel.BackColor = Theme.Bg;
        cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);
        CancelButton = null;

        status.Bounds = new Rectangle(M, 530, W, 22);
        status.BackColor = Theme.Bg;
        status.Show("Drop a picture on the square, or click it to browse.", StatusLabel.Level.Info);
        Controls.Add(status);

        DragEnter += delegate (object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += delegate (object s, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) LoadPicture(files[0]);
        };
        KeyPreview = true;
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        Load += delegate { Theme.ApplyTitleBar(this); };
    }

    void Browse()
    {
        using (OpenFileDialog ofd = new OpenFileDialog())
        {
            ofd.Title = "Choose a picture";
            ofd.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*";
            if (ofd.ShowDialog(this) == DialogResult.OK) LoadPicture(ofd.FileName);
        }
    }

    void LoadPicture(string path)
    {
        try
        {
            Image img = Engine.LoadImageUnlocked(path);
            if (picked != null) picked.Dispose();
            picked = img;
            zone.Picture = img;
            use.Enabled = true;
            status.Show(Path.GetFileName(path) + " loaded (" + img.Width + "×" + img.Height + "). Pick a pointer, then apply.", StatusLabel.Level.Info);
            UpdateResult();
        }
        catch (Exception ex) { status.Show("Couldn't load that picture: " + ex.Message, StatusLabel.Level.Error); }
    }

    void UpdateResult()
    {
        zone.Hotspot = new PointF((float)hx, (float)hy);
        result.Update(picked, hx, hy);
    }

    void Apply()
    {
        if (picked == null) { status.Show("Choose a picture first.", StatusLabel.Level.Error); return; }
        try
        {
            string key = roles.Selected.Key;
            Engine.ApplyCustom(key, picked, hx, hy);
            ResultMessage = "Your picture is now the " + roles.Selected.Name.ToLower() + " pointer.";
            if (key == "ALL") ResultMessage = "Your picture is now every pointer.";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { status.Show("Couldn't apply: " + ex.Message, StatusLabel.Level.Error); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (picked != null) picked.Dispose();
    }

    // Drop target + hotspot picker: shows the picture on a checkerboard.
    class DropZone : Canvas
    {
        public Image Picture;
        public PointF Hotspot;
        public delegate void PointHandler(PointF p);
        public event PointHandler HotspotPicked;
        bool hover;
        public DropZone() { Cursor = Cursors.Hand; TabStop = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Picture == null || e.Button != MouseButtons.Left) return;
            RectangleF r = ImageRect();
            if (!r.Contains(e.Location)) return;
            float fx = Math.Max(0, Math.Min(1, (e.X - r.X) / r.Width));
            float fy = Math.Max(0, Math.Min(1, (e.Y - r.Y) / r.Height));
            if (HotspotPicked != null) HotspotPicked(new PointF(fx, fy));
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            // only browse when there's no picture yet (clicks otherwise set the hotspot)
            if (Picture == null) base.OnMouseClick(e);
        }
        protected override void OnClick(EventArgs e) { if (Picture == null) base.OnClick(e); }

        RectangleF ImageRect()
        {
            float s = Ui.Scale(this);
            float pad = 12 * s, box = Math.Min(Width, Height) - pad * 2;
            if (Picture == null) return new RectangleF(pad, pad, box, box);
            float scale = Math.Min(box / Picture.Width, box / Picture.Height);
            float w = Picture.Width * scale, h = Picture.Height * scale;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Prep(g);
            g.Clear(BackColor);
            float s = Ui.Scale(this);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.Round(r, 12 * s))
            {
                g.SetClip(p);
                // checkerboard
                int cell = (int)(10 * s);
                Color a = Theme.Dark ? Color.FromArgb(58, 58, 66) : Color.FromArgb(240, 236, 230);
                Color b = Theme.Dark ? Color.FromArgb(48, 48, 55) : Color.White;
                using (SolidBrush ba = new SolidBrush(a)) using (SolidBrush bb = new SolidBrush(b))
                    for (int y = 0; y < Height; y += cell)
                        for (int x = 0; x < Width; x += cell)
                            g.FillRectangle(((x / cell + y / cell) % 2 == 0) ? ba : bb, x, y, cell, cell);
                g.ResetClip();
                if (Picture != null)
                {
                    RectangleF ir = ImageRect();
                    g.DrawImage(Picture, ir);
                    // hotspot marker
                    float px = ir.X + Hotspot.X * ir.Width, py = ir.Y + Hotspot.Y * ir.Height;
                    using (Pen w = new Pen(Color.White, 3f)) using (Pen k = new Pen(Theme.Accent, 1.5f))
                    {
                        g.DrawLine(w, px - 9 * s, py, px + 9 * s, py); g.DrawLine(w, px, py - 9 * s, px, py + 9 * s);
                        g.DrawLine(k, px - 9 * s, py, px + 9 * s, py); g.DrawLine(k, px, py - 9 * s, px, py + 9 * s);
                        g.DrawEllipse(w, px - 5 * s, py - 5 * s, 10 * s, 10 * s);
                        g.DrawEllipse(k, px - 5 * s, py - 5 * s, 10 * s, 10 * s);
                    }
                }
                else
                {
                    using (Font f = Ui.F(9.5f, true)) using (Font f2 = Ui.F(8.5f, false))
                    {
                        Ui.Text(g, "Drop a picture here", f, Theme.Text, new RectangleF(0, Height / 2f - 22 * s, Width, 20 * s), ContentAlignment.MiddleCenter);
                        Ui.Text(g, "or click to browse", f2, Theme.SubText, new RectangleF(0, Height / 2f, Width, 18 * s), ContentAlignment.MiddleCenter);
                    }
                }
                using (Pen pen = new Pen(hover ? Theme.Accent : Theme.SecondaryBorder, hover ? 2f : 1f))
                {
                    pen.DashStyle = Picture == null ? DashStyle.Dash : DashStyle.Solid;
                    g.DrawPath(pen, p);
                }
            }
        }
    }

    // The picture rendered the way Windows will show it at 32 and 48 px.
    class ResultPreview : Canvas
    {
        Bitmap b32, b48; Point h32, h48;
        public ResultPreview() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }
        public void Update(Image src, double hx, double hy)
        {
            if (b32 != null) b32.Dispose(); if (b48 != null) b48.Dispose();
            b32 = b48 = null;
            if (src != null)
            {
                b32 = Engine.RenderFitted(src, 32); b48 = Engine.RenderFitted(src, 48);
                h32 = new Point((int)Math.Round(31 * hx), (int)Math.Round(31 * hy));
                h48 = new Point((int)Math.Round(47 * hx), (int)Math.Round(47 * hy));
            }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float s = Ui.Scale(this);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Ui.FillRound(g, r, 8 * s, Theme.Track, Theme.CardBorder);
            if (b32 == null)
            {
                using (Font f = Ui.F(8.5f, false)) Ui.Text(g, "No picture yet", f, Theme.SubText, r, ContentAlignment.MiddleCenter);
                return;
            }
            float x = 14 * s, y32 = (Height - 32 * s) / 2, y48 = (Height - 48 * s) / 2;
            g.DrawImage(b32, x, y32, 32 * s, 32 * s);
            Mark(g, x + h32.X * s, y32 + h32.Y * s, s);
            x += 32 * s + 18 * s;
            g.DrawImage(b48, x, y48, 48 * s, 48 * s);
            Mark(g, x + h48.X * s, y48 + h48.Y * s, s);
            x += 48 * s + 14 * s;
            using (Font f = Ui.F(8.5f, false))
                Ui.Text(g, "32 px and 48 px\nred dot = click point", f, Theme.SubText, new RectangleF(x, 0, Width - x, Height), ContentAlignment.MiddleLeft);
        }
        static void Mark(Graphics g, float x, float y, float s)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 40, 40))) g.FillEllipse(b, x - 1.5f * s, y - 1.5f * s, 3 * s, 3 * s);
        }
    }
}

// ---------------------------------------------------------------------------
// Program
// ---------------------------------------------------------------------------
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Init();
        if (Array.IndexOf(args, "--dark") >= 0) Theme.Init(true);     // force a mode (screenshots / testing)
        if (Array.IndexOf(args, "--light") >= 0) Theme.Init(false);

        // --custom opens the picture dialog on its own (for screenshots)
        Form f = Array.IndexOf(args, "--custom") >= 0 ? (Form)new CustomForm("Orange", false) : new MainForm();

        // --screenshot <file.png> : render the window's client area to a PNG and quit (used for the README)
        int si = Array.IndexOf(args, "--screenshot");
        if (si >= 0 && si + 1 < args.Length)
        {
            string outPath = args[si + 1];
            f.Shown += delegate
            {
                Application.DoEvents();
                Point origin = f.PointToScreen(Point.Empty);
                int ox = origin.X - f.Left, oy = origin.Y - f.Top;
                using (Bitmap full = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(full, new Rectangle(0, 0, f.Width, f.Height));
                    using (Bitmap client = full.Clone(new Rectangle(ox, oy, f.ClientSize.Width, f.ClientSize.Height), full.PixelFormat))
                        client.Save(outPath, ImageFormat.Png);
                }
                f.Close();
            };
        }
        Application.Run(f);
    }
}
