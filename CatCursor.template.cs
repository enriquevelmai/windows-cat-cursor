using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// Cat Cursor - themes the whole Windows cursor set as cats, in several colours
// (with animated busy/loading pointers), and lets you build your OWN cursors
// from any picture. Self-contained: no install ever needed.
// All cursor files are embedded as a compressed (zip) resource and read at run time.
class CatCursorApp
{
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

    // Embedded cursor pack: entryFullName ("Orange/cat_cursor.cur") -> bytes.
    static readonly Dictionary<string, byte[]> ASSETS = LoadAssets();
    static Dictionary<string, byte[]> LoadAssets()
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CatCursor.cursors.zip"))
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

    const string PREVIEW_B64 = "__PREVIEW_BASE64__";

    static readonly string[] ALL_ROLES = {
        "Arrow","Hand","Help","AppStarting","Wait","IBeam","Crosshair","No",
        "SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","NWPen","UpArrow"
    };
    static readonly int[] SIZES = { 32, 48, 64, 96, 128 };

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
    static void Refresh() { SystemParametersInfo(0x57, 0, IntPtr.Zero, 0x03); }

    static string AssetDir(string sub)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatCursor", sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- built-in themes ----------------------------------------------------

    static void ApplyTheme(string colour)
    {
        string dir = AssetDir(colour);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
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
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
        {
            foreach (string r in ALL_ROLES) k.SetValue(r, "", RegistryValueKind.String);
            k.SetValue("", "Windows Default", RegistryValueKind.String);
        }
        Refresh();
    }

    // ---- custom picture -> cursor -------------------------------------------

    static void ApplyCustom(string roleKey, Image img, double hxFrac, double hyFrac)
    {
        string dir = AssetDir("custom");
        byte[] cur = BuildCur(img, hxFrac, hyFrac);
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
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

    // ---- UI -----------------------------------------------------------------

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Image[] picked = new Image[1];

        Form f = new Form();
        f.Text = "Cat Cursor";
        f.ClientSize = new Size(364, 566);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = Color.FromArgb(250, 244, 236);

        Label title = new Label();
        title.Text = "Cat Cursor";
        title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        title.AutoSize = true;
        title.Location = new Point(18, 12);
        f.Controls.Add(title);

        PictureBox logo = new PictureBox();
        logo.Size = new Size(48, 48);
        logo.Location = new Point(300, 8);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        try { logo.Image = Image.FromStream(new MemoryStream(Convert.FromBase64String(PREVIEW_B64))); }
        catch { }
        f.Controls.Add(logo);

        Label lblColor = new Label();
        lblColor.Text = "Cat colour:";
        lblColor.Font = new Font("Segoe UI", 10F);
        lblColor.AutoSize = true;
        lblColor.Location = new Point(18, 56);
        f.Controls.Add(lblColor);

        ComboBox cmbColor = new ComboBox();
        cmbColor.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbColor.Font = new Font("Segoe UI", 10F);
        cmbColor.Size = new Size(170, 26);
        cmbColor.Location = new Point(96, 52);
        cmbColor.Items.AddRange(COLOR_ORDER);
        cmbColor.SelectedIndex = 0;
        f.Controls.Add(cmbColor);

        Button apply = new Button();
        apply.Text = "Turn my cursors into cats";
        apply.Size = new Size(326, 42);
        apply.Location = new Point(18, 86);
        apply.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        apply.BackColor = Color.FromArgb(255, 165, 60);
        apply.ForeColor = Color.White;
        apply.FlatStyle = FlatStyle.Flat;
        apply.FlatAppearance.BorderSize = 0;
        f.Controls.Add(apply);

        Button revert = new Button();
        revert.Text = "Restore normal cursors";
        revert.Size = new Size(326, 34);
        revert.Location = new Point(18, 132);
        revert.Font = new Font("Segoe UI", 10F);
        revert.FlatStyle = FlatStyle.Flat;
        f.Controls.Add(revert);

        GroupBox grp = new GroupBox();
        grp.Text = "Make your own cursor from a picture";
        grp.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        grp.Location = new Point(18, 176);
        grp.Size = new Size(326, 352);
        f.Controls.Add(grp);

        Label hint = new Label();
        hint.Text = "Tip: PNG with a transparent background looks best.";
        hint.Font = new Font("Segoe UI", 8.5F);
        hint.ForeColor = Color.FromArgb(120, 105, 90);
        hint.AutoSize = true;
        hint.Location = new Point(12, 22);
        grp.Controls.Add(hint);

        Button choose = new Button();
        choose.Text = "Choose picture...";
        choose.Size = new Size(160, 34);
        choose.Location = new Point(12, 46);
        choose.Font = new Font("Segoe UI", 9.5F);
        grp.Controls.Add(choose);

        PictureBox prev = new PictureBox();
        prev.Size = new Size(118, 118);
        prev.Location = new Point(192, 42);
        prev.SizeMode = PictureBoxSizeMode.Zoom;
        prev.BorderStyle = BorderStyle.FixedSingle;
        prev.BackColor = Color.White;
        grp.Controls.Add(prev);

        Label lblRole = new Label();
        lblRole.Text = "Replace which pointer?";
        lblRole.Font = new Font("Segoe UI", 9F);
        lblRole.AutoSize = true;
        lblRole.Location = new Point(12, 170);
        grp.Controls.Add(lblRole);

        ComboBox cmbRole = new ComboBox();
        cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRole.Size = new Size(302, 24);
        cmbRole.Location = new Point(12, 190);
        cmbRole.Font = new Font("Segoe UI", 9.5F);
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
        lblHot.Location = new Point(12, 222);
        grp.Controls.Add(lblHot);

        ComboBox cmbHot = new ComboBox();
        cmbHot.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbHot.Size = new Size(302, 24);
        cmbHot.Location = new Point(12, 242);
        cmbHot.Font = new Font("Segoe UI", 9.5F);
        string[] hotNames = { "Top-left (like a normal arrow)", "Top-center", "Center" };
        double[][] hotFrac = { new double[] { 0, 0 }, new double[] { 0.5, 0 }, new double[] { 0.5, 0.5 } };
        cmbHot.Items.AddRange(hotNames);
        cmbHot.SelectedIndex = 0;
        grp.Controls.Add(cmbHot);

        Button use = new Button();
        use.Text = "Use this picture as my cursor";
        use.Size = new Size(302, 40);
        use.Location = new Point(12, 282);
        use.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        use.BackColor = Color.FromArgb(120, 180, 90);
        use.ForeColor = Color.White;
        use.FlatStyle = FlatStyle.Flat;
        use.FlatAppearance.BorderSize = 0;
        grp.Controls.Add(use);

        Label status = new Label();
        status.Text = "Pick a colour and click the orange button.";
        status.AutoSize = false;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Size = new Size(344, 26);
        status.Location = new Point(10, 534);
        status.ForeColor = Color.FromArgb(90, 80, 70);
        f.Controls.Add(status);

        apply.Click += delegate
        {
            try
            {
                string c = cmbColor.SelectedItem == null ? COLOR_ORDER[0] : cmbColor.SelectedItem.ToString();
                ApplyTheme(c);
                status.Text = "Done! " + c + " cats applied. \U0001F431";
            }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        };
        revert.Click += delegate
        {
            try { Revert(); status.Text = "Default cursors restored."; }
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

        Application.Run(f);
    }
}
