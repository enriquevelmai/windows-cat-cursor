using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// Cat Cursor - themes the whole Windows cursor set as cats (five colours, with
// animated busy/loading pointers) and turns any picture into a cursor.
// Self-contained: every cursor file is embedded as a compressed resource.
class CatCursorApp
{
    const string VERSION = "1.0.0";
    const string REPO_URL = "https://github.com/enriquevelmai/windows-cat-cursor";

    static readonly string[] COLOR_ORDER = { "Orange", "Black", "Grey", "White", "Siamese" };

    // registry role -> file name inside each colour folder (Wait/AppStarting are animated)
    static readonly string[][] ROLE_FILES = {
        new[]{"Arrow","cat_cursor.cur"}, new[]{"Hand","cat_paw.cur"},
        new[]{"Help","cat_help.cur"}, new[]{"IBeam","cat_text.cur"},
        new[]{"Crosshair","cat_cross.cur"}, new[]{"No","cat_no.cur"},
        new[]{"SizeNS","cat_ns.cur"}, new[]{"SizeWE","cat_we.cur"},
        new[]{"SizeNWSE","cat_nwse.cur"}, new[]{"SizeNESW","cat_nesw.cur"},
        new[]{"SizeAll","cat_move.cur"}, new[]{"NWPen","cat_pen.cur"},
        new[]{"UpArrow","cat_up.cur"},
        new[]{"Wait","cat_busy.ani"}, new[]{"AppStarting","cat_working.ani"}
    };

    static readonly string[] ALL_ROLES = {
        "Arrow","Hand","Help","AppStarting","Wait","IBeam","Crosshair","No",
        "SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","NWPen","UpArrow"
    };
    static readonly int[] SIZES = { 32, 48, 64, 96, 128 };

    // Embedded cursor pack: entryFullName ("Orange/cat_cursor.cur") -> bytes.
    static readonly Dictionary<string, byte[]> ASSETS = LoadAssets();
    static Dictionary<string, byte[]> LoadAssets()
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

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
    static void Refresh() { SystemParametersInfo(0x57, 0, IntPtr.Zero, 0x03); }

    const string CURSOR_KEY = @"Control Panel\Cursors";

    static string AssetDir(string sub)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatCursor", sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- themes -------------------------------------------------------------

    static void ApplyTheme(string colour)
    {
        string dir = AssetDir(colour);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            foreach (string[] rf in ROLE_FILES)
            {
                byte[] data;
                if (!ASSETS.TryGetValue(colour + "/" + rf[1], out data)) continue;
                string path = Path.Combine(dir, rf[1]);
                File.WriteAllBytes(path, data);
                k.SetValue(rf[0], path, RegistryValueKind.String);
            }
            k.SetValue("", "Cat Cursor (" + colour + ")", RegistryValueKind.String);
        }
        Refresh();
    }

    static void Revert()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            foreach (string r in ALL_ROLES) k.SetValue(r, "", RegistryValueKind.String);
            k.SetValue("", "Windows Default", RegistryValueKind.String);
        }
        Refresh();
    }

    // The colour of the currently-applied cat theme, or null.
    static string CurrentColour()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY))
            {
                string v = k == null ? null : k.GetValue("") as string;
                if (v != null && v.StartsWith("Cat Cursor (") && v.EndsWith(")"))
                    return v.Substring(12, v.Length - 13);
            }
        }
        catch { }
        return null;
    }

    // ---- custom picture -> cursor -------------------------------------------

    static void ApplyCustom(string roleKey, Image img, double hxFrac, double hyFrac)
    {
        string dir = AssetDir("custom");
        byte[] cur = BuildCur(img, hxFrac, hyFrac);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CURSOR_KEY, true))
        {
            if (roleKey == "ALL")
            {
                string p = Path.Combine(dir, "custom_all.cur");
                File.WriteAllBytes(p, cur);
                foreach (string r in ALL_ROLES) k.SetValue(r, p, RegistryValueKind.String);
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

    static byte[] BuildCur(Image src, double hxFrac, double hyFrac)
    {
        List<byte[]> blobs = new List<byte[]>();
        List<int[]> dims = new List<int[]>();
        foreach (int s in SIZES)
        {
            using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
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
                blobs.Add(Dib(bmp));
                dims.Add(new int[] { s, s, (int)Math.Round(s * hxFrac), (int)Math.Round(s * hyFrac) });
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

    static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
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

    static Image LoadImageUnlocked(string path)
    {
        return Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
    }

    static Image ColorPreview(string colour)
    {
        byte[] b;
        if (!ASSETS.TryGetValue(colour + "/preview.png", out b)) return null;
        using (MemoryStream ms = new MemoryStream(b))
        using (Image img = Image.FromStream(ms))
            return new Bitmap(img);
    }

    static Icon AppIcon()
    {
        byte[] b;
        if (!ASSETS.TryGetValue("icon.ico", out b)) return null;
        using (MemoryStream ms = new MemoryStream(b))
            return (Icon)new Icon(ms).Clone();
    }

    // ---- UI helpers ---------------------------------------------------------

    static Button FlatButton(string text, Rectangle r, Color back, Color fore, float size, bool bold)
    {
        Button b = new Button();
        b.Text = text;
        b.Bounds = r;
        b.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        b.BackColor = back;
        b.ForeColor = fore;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(220, 210, 198);
        b.FlatAppearance.BorderSize = back == Color.White ? 1 : 0;
        b.Cursor = Cursors.Hand;
        return b;
    }

    // ---- UI -----------------------------------------------------------------

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Image[] picked = new Image[1];
        Color cream = Color.FromArgb(250, 244, 236);
        Color ink = Color.FromArgb(70, 55, 40);
        Color orange = Color.FromArgb(255, 160, 55);

        // pre-render small swatches for the colour dropdown
        Dictionary<string, Image> swatches = new Dictionary<string, Image>();
        foreach (string c in COLOR_ORDER)
        {
            Image p = ColorPreview(c);
            if (p != null) { swatches[c] = new Bitmap(p, 16, 16); p.Dispose(); }
        }

        Form f = new Form();
        f.Text = "Cat Cursor";
        f.ClientSize = new Size(380, 600);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = cream;
        f.Font = new Font("Segoe UI", 9F);
        f.AutoScaleMode = AutoScaleMode.Font;
        Icon ic = AppIcon();
        if (ic != null) f.Icon = ic;

        // ---- header ----
        Panel header = new Panel();
        header.Bounds = new Rectangle(0, 0, 380, 62);
        header.BackColor = orange;
        f.Controls.Add(header);

        PictureBox hIcon = new PictureBox();
        hIcon.Bounds = new Rectangle(16, 9, 44, 44);
        hIcon.SizeMode = PictureBoxSizeMode.Zoom;
        header.Controls.Add(hIcon);

        Label hTitle = new Label();
        hTitle.Text = "Cat Cursor";
        hTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        hTitle.ForeColor = Color.White;
        hTitle.AutoSize = true;
        hTitle.Location = new Point(68, 8);
        header.Controls.Add(hTitle);

        Label hSub = new Label();
        hSub.Text = "Windows cursor themer";
        hSub.Font = new Font("Segoe UI", 8.5F);
        hSub.ForeColor = Color.FromArgb(255, 240, 225);
        hSub.AutoSize = true;
        hSub.Location = new Point(70, 36);
        header.Controls.Add(hSub);

        // ---- theme picker ----
        Label lblColor = new Label();
        lblColor.Text = "Cat colour";
        lblColor.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        lblColor.ForeColor = ink;
        lblColor.AutoSize = true;
        lblColor.Location = new Point(18, 78);
        f.Controls.Add(lblColor);

        ComboBox cmbColor = new ComboBox();
        cmbColor.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbColor.DrawMode = DrawMode.OwnerDrawFixed;
        cmbColor.ItemHeight = 22;
        cmbColor.Bounds = new Rectangle(100, 74, 262, 26);
        cmbColor.Items.AddRange(COLOR_ORDER);
        cmbColor.DrawItem += delegate (object s, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                string name = cmbColor.Items[e.Index].ToString();
                Image sw;
                if (swatches.TryGetValue(name, out sw))
                    e.Graphics.DrawImage(sw, e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
                using (SolidBrush br = new SolidBrush(e.ForeColor))
                    e.Graphics.DrawString(name, cmbColor.Font, br, e.Bounds.Left + 26, e.Bounds.Top + 3);
            }
            e.DrawFocusRectangle();
        };
        f.Controls.Add(cmbColor);

        Button apply = FlatButton("Turn my cursors into cats", new Rectangle(18, 108, 344, 44), orange, Color.White, 11.5F, true);
        f.Controls.Add(apply);

        Button revert = FlatButton("Restore normal cursors", new Rectangle(18, 158, 344, 32), Color.White, ink, 10F, false);
        f.Controls.Add(revert);

        Label lblCurrent = new Label();
        lblCurrent.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
        lblCurrent.ForeColor = Color.FromArgb(130, 115, 100);
        lblCurrent.AutoSize = true;
        lblCurrent.Location = new Point(20, 196);
        f.Controls.Add(lblCurrent);

        // ---- custom picture ----
        GroupBox grp = new GroupBox();
        grp.Text = "Make your own cursor from a picture";
        grp.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        grp.ForeColor = ink;
        grp.Bounds = new Rectangle(18, 218, 344, 318);
        f.Controls.Add(grp);

        Label hint = new Label();
        hint.Text = "Tip: a PNG with a transparent background looks best.";
        hint.Font = new Font("Segoe UI", 8.5F);
        hint.ForeColor = Color.FromArgb(130, 115, 100);
        hint.AutoSize = true;
        hint.Location = new Point(12, 22);
        grp.Controls.Add(hint);

        Button choose = FlatButton("Choose picture...", new Rectangle(12, 46, 160, 32), Color.White, ink, 9.5F, false);
        grp.Controls.Add(choose);

        PictureBox prev = new PictureBox();
        prev.Bounds = new Rectangle(208, 40, 120, 120);
        prev.SizeMode = PictureBoxSizeMode.Zoom;
        prev.BorderStyle = BorderStyle.FixedSingle;
        prev.BackColor = Color.White;
        grp.Controls.Add(prev);

        Label lblRole = new Label();
        lblRole.Text = "Replace which pointer?";
        lblRole.Font = new Font("Segoe UI", 9F);
        lblRole.AutoSize = true;
        lblRole.Location = new Point(12, 168);
        grp.Controls.Add(lblRole);

        ComboBox cmbRole = new ComboBox();
        cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRole.Bounds = new Rectangle(12, 188, 320, 24);
        string[] roleNames = { "Normal pointer", "Link / hover", "Text cursor",
                               "Busy / loading", "Help", "Move",
                               "Unavailable", "Every pointer (all of them)" };
        string[] roleKeys = { "Arrow", "Hand", "IBeam", "Wait", "Help", "SizeAll", "No", "ALL" };
        cmbRole.Items.AddRange(roleNames);
        cmbRole.SelectedIndex = 0;
        grp.Controls.Add(cmbRole);

        Label lblHot = new Label();
        lblHot.Text = "Click point (the exact pixel that clicks):";
        lblHot.Font = new Font("Segoe UI", 9F);
        lblHot.AutoSize = true;
        lblHot.Location = new Point(12, 220);
        grp.Controls.Add(lblHot);

        ComboBox cmbHot = new ComboBox();
        cmbHot.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbHot.Bounds = new Rectangle(12, 240, 320, 24);
        string[] hotNames = { "Top-left (like a normal arrow)", "Top-center", "Center" };
        double[][] hotFrac = { new double[] { 0, 0 }, new double[] { 0.5, 0 }, new double[] { 0.5, 0.5 } };
        cmbHot.Items.AddRange(hotNames);
        cmbHot.SelectedIndex = 0;
        grp.Controls.Add(cmbHot);

        Button use = FlatButton("Use this picture as my cursor", new Rectangle(12, 274, 320, 38), Color.FromArgb(120, 180, 90), Color.White, 10.5F, true);
        grp.Controls.Add(use);

        // ---- footer ----
        Label status = new Label();
        status.AutoSize = false;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Bounds = new Rectangle(12, 544, 356, 20);
        status.ForeColor = ink;
        f.Controls.Add(status);

        LinkLabel link = new LinkLabel();
        link.Text = "View on GitHub";
        link.Font = new Font("Segoe UI", 8.5F);
        link.LinkColor = Color.FromArgb(190, 120, 40);
        link.AutoSize = true;
        link.Location = new Point(18, 572);
        link.LinkClicked += delegate { try { Process.Start(REPO_URL); } catch { } };
        f.Controls.Add(link);

        Label lblVer = new Label();
        lblVer.Text = "v" + VERSION;
        lblVer.Font = new Font("Segoe UI", 8.5F);
        lblVer.ForeColor = Color.FromArgb(150, 135, 120);
        lblVer.AutoSize = false;
        lblVer.TextAlign = ContentAlignment.MiddleRight;
        lblVer.Bounds = new Rectangle(262, 570, 100, 18);
        f.Controls.Add(lblVer);

        // ---- behaviour ----
        cmbColor.SelectedIndexChanged += delegate
        {
            Image old = hIcon.Image;
            hIcon.Image = ColorPreview(cmbColor.SelectedItem.ToString());
            if (old != null) old.Dispose();
        };

        apply.Click += delegate
        {
            try
            {
                string c = cmbColor.SelectedItem.ToString();
                ApplyTheme(c);
                lblCurrent.Text = "Currently applied: " + c + " cats";
                status.Text = "Done! " + c + " cats applied. \U0001F431";
            }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        };
        revert.Click += delegate
        {
            try
            {
                Revert();
                lblCurrent.Text = "No cat theme applied.";
                status.Text = "Default Windows cursors restored.";
            }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        };
        choose.Click += delegate
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose a picture";
                ofd.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Image img = LoadImageUnlocked(ofd.FileName);
                        if (picked[0] != null) picked[0].Dispose();
                        picked[0] = img;
                        prev.Image = img;
                        status.Text = "Picture loaded. Pick a pointer, then \"Use this picture\".";
                    }
                    catch (Exception ex) { status.Text = "Couldn't load image: " + ex.Message; }
                }
            }
        };
        use.Click += delegate
        {
            if (picked[0] == null) { status.Text = "Choose a picture first."; return; }
            try
            {
                double[] hf = hotFrac[cmbHot.SelectedIndex];
                ApplyCustom(roleKeys[cmbRole.SelectedIndex], picked[0], hf[0], hf[1]);
                status.Text = "Applied your picture to: " + roleNames[cmbRole.SelectedIndex] + ".";
            }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        };

        // initial state: reflect whatever theme is currently applied
        string current = CurrentColour();
        int idx = current == null ? 0 : Array.IndexOf(COLOR_ORDER, current);
        cmbColor.SelectedIndex = idx < 0 ? 0 : idx;
        if (current != null)
        {
            lblCurrent.Text = "Currently applied: " + current + " cats";
            status.Text = "A cat theme is active. Pick a colour or restore defaults.";
        }
        else
        {
            lblCurrent.Text = "No cat theme applied yet.";
            status.Text = "Pick a colour and click the orange button.";
        }

        if (ASSETS.Count == 0)
            status.Text = "Error: cursor pack missing from this build.";

        Application.Run(f);
    }
}
