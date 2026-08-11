using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

using Rbx2Source.Geometry;
using RobloxFiles;
using Rbx2Source.Web;

namespace Rbx2Source.Textures
{
    public class TextureCompositor
    {
        private readonly List<CompositData> layers = new List<CompositData>();
        private string context = "Humanoid Texture Map";
        private readonly AvatarType avatarType;
        private Rectangle canvas;
        private int composed;

        public Folder CharacterAssets;

        public TextureCompositor(AvatarType at, int width, int height)
        {
            avatarType = at;
            canvas = new Rectangle(0, 0, width, height);
        }

        public TextureCompositor(AvatarType at, Rectangle rect)
        {
            avatarType = at;
            canvas = rect;
        }

        public static Rectangle GetBoundingBox(params Point[] points)
        {
            int min_X = int.MaxValue,
                min_Y = int.MaxValue;

            int max_X = int.MinValue,
                max_Y = int.MinValue;

            foreach (Point point in points)
            {
                int point_X = point.X,
                    point_Y = point.Y;

                min_X = Math.Min(min_X, point_X);
                min_Y = Math.Min(min_Y, point_Y);

                max_X = Math.Max(max_X, point_X);
                max_Y = Math.Max(max_Y, point_Y);
            }

            int width  = max_X - min_X,
                height = max_Y - min_Y;

            return new Rectangle(min_X, min_Y, width, height);
        }

        public void AppendColor(string hexColor3, string guide, Rectangle guideSize, byte layer = 0)
        {
            var composit = new CompositData(DrawFlags.Guide | DrawFlags.Color);
            composit.SetGuide(guide, guideSize, avatarType);
            composit.SetDrawColor(hexColor3);
            composit.Layer = layer;

            layers.Add(composit);
        }

        public void AppendTexture(object img, string guide, Rectangle guideSize, byte layer = 0)
        {
            var composit = new CompositData(DrawFlags.Guide | DrawFlags.Texture);
            composit.SetGuide(guide, guideSize, avatarType);
            composit.Texture = img;
            composit.Layer = layer;

            layers.Add(composit);
        }

        public void AppendColor(string hexColor3, Rectangle rect, byte layer = 0)
        {
            var composit = new CompositData(DrawFlags.Rect | DrawFlags.Color);
            composit.SetDrawColor(hexColor3);
            composit.Layer = layer;
            composit.Rect = rect;

            layers.Add(composit);
        }

        public void AppendTexture(object img, Rectangle rect, byte layer = 0, RotateFlipType flipMode = RotateFlipType.RotateNoneFlipNone)
        {
            var composit = new CompositData(DrawFlags.Rect | DrawFlags.Texture)
            {
                FlipMode = flipMode,
                Texture = img,
                Layer = layer,
                Rect = rect
            };

            layers.Add(composit);
        }

        public void SetContext(string newContext)
        {
            context = newContext;
        }

        public Bitmap BakeTextureMap()
        {
            var bitmap = new Bitmap(canvas.Width, canvas.Height);
            layers.Sort();

            composed = 0;

            Rbx2Source.Print("Composing " + context + "...");
            Rbx2Source.IncrementStack();

            foreach (CompositData composit in layers)
            {
                var drawFlags = composit.DrawFlags;
                var canvas = composit.Rect;

                if (drawFlags.HasFlag(DrawFlags.Rect))
                {
                    using (var buffer = Graphics.FromImage(bitmap))
                    {
                        if (drawFlags.HasFlag(DrawFlags.Color))
                        {
                            composit.UseBrush(brush => buffer.FillRectangle(brush, canvas));
                        }
                        else if (drawFlags.HasFlag(DrawFlags.Texture))
                        {
                            Bitmap image = composit.GetTextureBitmap();

                            if (composit.FlipMode > 0)
                                image.RotateFlip(composit.FlipMode);

                            buffer.DrawImage(image, canvas);
                        }
                    }
                }
                else if (drawFlags.HasFlag(DrawFlags.Guide))
                {
                    Mesh guide = composit.Guide;

                    if (drawFlags.HasFlag(DrawFlags.Color))
                    {
                        using (var buffer = Graphics.FromImage(bitmap))
                        {
                            for (int face = 0; face < guide.Faces.Count; face++)
                            {
                                Vertex[] verts = composit.GetGuideVerts(face);
                                Point offset = canvas.Location;

                                Point[] poly = verts
                                    .Select(vert => vert.ToPoint(canvas, offset))
                                    .ToArray();

                                composit.UseBrush(brush => buffer.FillPolygon(brush, poly));
                            }
                        }
                    }
                    else if (drawFlags.HasFlag(DrawFlags.Texture))
                    {
                        BakeGuideTextures(bitmap, composit);
                    }
                }

                Rbx2Source.Print("{0}/{1} layers composed...", ++composed, layers.Count);

                if (layers.Count > 2)
                    Rbx2Source.SetDebugImage(bitmap);
            }

            Rbx2Source.Print("Done!");
            Rbx2Source.DecrementStack();

            return bitmap;
        }

        private void BakeGuideTextures(Bitmap bitmap, CompositData composit)
        {
            Bitmap texture = composit.GetTextureBitmap();

            BitmapData texData = texture.LockBits(
                new Rectangle(0, 0, texture.Width, texture.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            BitmapData drawData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);

            try
            {
                int texStride, drawStride;
                byte[] texBuffer = CopyLockedRegion(texData, out texStride);
                byte[] drawBuffer = CopyLockedRegion(drawData, out drawStride);

                int textureWidth = texture.Width,
                    textureHeight = texture.Height;

                Mesh guide = composit.Guide;
                Rectangle canvas = composit.Rect;
                Point offset = canvas.Location;

                for (int face = 0; face < guide.Faces.Count; face++)
                {
                    Vertex[] verts = composit.GetGuideVerts(face);

                    Point[] poly = verts
                        .Select(vert => vert.ToPoint(canvas, offset))
                        .ToArray();

                    Point[] uv = verts
                        .Select(vert => vert.ToUV(texture))
                        .ToArray();

                    Rectangle bbox = GetBoundingBox(poly);
                    bbox.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));

                    if (bbox.Width <= 0 || bbox.Height <= 0)
                        continue;

                    var sampler = new BarycentricPoint(poly);

                    for (int x = bbox.Left; x < bbox.Right; x++)
                    {
                        for (int y = bbox.Top; y < bbox.Bottom; y++)
                        {
                            Point uvPixel;
                            if (!sampler.TryMap(new Point(x, y), uv, out uvPixel))
                                continue;

                            int tx = uvPixel.X,
                                ty = uvPixel.Y;

                            if (tx < 0) tx = 0;
                            else if (tx >= textureWidth) tx = textureWidth - 1;

                            if (ty < 0) ty = 0;
                            else if (ty >= textureHeight) ty = textureHeight - 1;

                            int src = (ty * texStride) + (tx * 4);
                            int dst = (y * drawStride) + (x * 4);

                            int sa = texBuffer[src + 3];

                            if (sa == 0)
                                continue;

                            if (sa == 255)
                            {
                                drawBuffer[dst]     = texBuffer[src];
                                drawBuffer[dst + 1] = texBuffer[src + 1];
                                drawBuffer[dst + 2] = texBuffer[src + 2];
                                drawBuffer[dst + 3] = texBuffer[src + 3];
                                continue;
                            }

                            int sr = texBuffer[src + 2],
                                sg = texBuffer[src + 1],
                                sb = texBuffer[src];

                            int da = drawBuffer[dst + 3],
                                dr = drawBuffer[dst + 2],
                                dg = drawBuffer[dst + 1],
                                db = drawBuffer[dst];

                            int inv = 255 - sa;
                            int outA = sa + (da * inv) / 255;

                            if (outA <= 0)
                            {
                                drawBuffer[dst]     = 0;
                                drawBuffer[dst + 1] = 0;
                                drawBuffer[dst + 2] = 0;
                                drawBuffer[dst + 3] = 0;
                                continue;
                            }

                            drawBuffer[dst]     = (byte)(((sa * sb) + ((da * db * inv) / 255)) / outA);
                            drawBuffer[dst + 1] = (byte)(((sa * sg) + ((da * dg * inv) / 255)) / outA);
                            drawBuffer[dst + 2] = (byte)(((sa * sr) + ((da * dr * inv) / 255)) / outA);
                            drawBuffer[dst + 3] = (byte)outA;
                        }
                    }
                }

                WriteLockedRegion(drawBuffer, drawData);
            }
            finally
            {
                texture.UnlockBits(texData);
                bitmap.UnlockBits(drawData);
            }
        }

        private static byte[] CopyLockedRegion(BitmapData data, out int absStride)
        {
            int stride = data.Stride;
            absStride = Math.Abs(stride);

            byte[] buffer = new byte[absStride * data.Height];

            if (stride > 0)
            {
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            }
            else
            {
                for (int y = 0; y < data.Height; y++)
                    Marshal.Copy(data.Scan0 + (stride * y), buffer, y * absStride, absStride);
            }

            return buffer;
        }

        private static void WriteLockedRegion(byte[] buffer, BitmapData data)
        {
            int stride = data.Stride;

            if (stride > 0)
            {
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            else
            {
                int absStride = -stride;

                for (int y = 0; y < data.Height; y++)
                    Marshal.Copy(buffer, y * absStride, data.Scan0 + (stride * y), absStride);
            }
        }

        public static Bitmap CropBitmap(Bitmap src, Rectangle crop)
        {
            Bitmap target = new Bitmap(crop.Width, crop.Height);

            using (Graphics graphics = Graphics.FromImage(target))
                graphics.DrawImage(src, -crop.X, -crop.Y);

            return target;
        }

        public Bitmap BakeTextureMap(Rectangle crop)
        {
            Bitmap result;

            using (Bitmap src = BakeTextureMap())
                result = CropBitmap(src, crop);

            return result;
        }
    }
}
