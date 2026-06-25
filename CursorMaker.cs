// Shared image -> Windows .cur converter (used by Set-CustomCursor.ps1).
// Builds a multi-resolution cursor from any image, preserving transparency.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class CursorMaker
{
    static readonly int[] SIZES = { 32, 48, 64, 96, 128 };

    public static void BuildCurFile(string imagePath, string outPath, double hxFrac, double hyFrac)
    {
        using (Image src = Image.FromStream(new MemoryStream(File.ReadAllBytes(imagePath))))
        {
            File.WriteAllBytes(outPath, BuildCur(src, hxFrac, hyFrac));
        }
    }

    public static byte[] BuildCur(Image src, double hxFrac, double hyFrac)
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
}
