// EXP12 - the ICO container, written and read as bytes.
//
// An .ico is an ICONDIR, an ICONDIRENTRY per image, then the images themselves. Windows wants the
// small frames as a DIB (a BITMAPINFOHEADER, 32-bit BGRA bottom-up, then an AND mask - which 32-bit
// icons keep, zeroed, for the benefit of anything older) and accepts a whole PNG for the large ones.
// This class does exactly that and nothing else, so a frame is a function of its pixels and a file
// is a function of its frames: a regeneration is byte-identical, which is what makes a committed
// asset reviewable.
//
// It is also a reader, so the tests can parse back what was written - the app never reads an .ico
// this way; it hands the bytes to System.Drawing.
// C# 5 only.

using System;
using System.Collections.Generic;
using System.IO;

namespace Monitorn.App
{
    /// <summary>One image in the file. <see cref="Data"/> is a PNG or a DIB, per <see cref="Png"/>.</summary>
    internal sealed class IcoFrame
    {
        public int Width;
        public int Height;
        public bool Png;
        public byte[] Data;

        public static IcoFrame FromPng(int size, byte[] png)
        {
            return new IcoFrame { Width = size, Height = size, Png = true, Data = png };
        }

        /// <summary>
        /// A DIB frame from straight (non-premultiplied) BGRA pixels, top-down, size*size*4 bytes.
        /// </summary>
        public static IcoFrame FromDib(int size, byte[] bgra)
        {
            return new IcoFrame { Width = size, Height = size, Png = false, Data = DibBytes(size, bgra) };
        }

        /// <summary>The BITMAPINFOHEADER + XOR bitmap + AND mask a 32-bit icon image is made of.</summary>
        internal static byte[] DibBytes(int size, byte[] bgra)
        {
            if (bgra == null) throw new ArgumentNullException("bgra");
            if (bgra.Length != size * size * 4)
                throw new ArgumentException("expected " + (size * size * 4) + " bytes, got " + bgra.Length);

            int maskStride = ((size + 31) / 32) * 4;
            int maskBytes = maskStride * size;
            int imageBytes = size * size * 4;
            var bytes = new byte[40 + imageBytes + maskBytes];
            var stream = new MemoryStream(bytes);
            var writer = new BinaryWriter(stream);

            writer.Write(40);                       // biSize
            writer.Write(size);                     // biWidth
            writer.Write(size * 2);                 // biHeight: XOR and AND stacked
            writer.Write((short)1);                 // biPlanes
            writer.Write((short)32);                // biBitCount
            writer.Write(0);                        // biCompression: BI_RGB
            writer.Write(imageBytes + maskBytes);   // biSizeImage
            writer.Write(0);                        // biXPelsPerMeter
            writer.Write(0);                        // biYPelsPerMeter
            writer.Write(0);                        // biClrUsed
            writer.Write(0);                        // biClrImportant

            // The XOR bitmap is bottom-up. The AND mask follows it, also bottom-up, and stays zero:
            // the alpha channel is what does the work, and the mask is only there for old readers.
            for (int y = size - 1; y >= 0; y--) writer.Write(bgra, y * size * 4, size * 4);
            writer.Write(new byte[maskBytes]);

            writer.Flush();
            return bytes;
        }
    }

    internal static class IcoFile
    {
        private const int DirectorySize = 6;
        private const int EntrySize = 16;

        /// <summary>The whole file: directory, entries, images in the order given.</summary>
        public static byte[] Write(IList<IcoFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException("frames");
            if (frames.Count == 0) throw new ArgumentException("no frames");
            if (frames.Count > 255) throw new ArgumentException("more than 255 frames");

            int offset = DirectorySize + frames.Count * EntrySize;
            var bytes = new byte[offset];
            var head = new BinaryWriter(new MemoryStream(bytes));
            head.Write((short)0);                   // reserved
            head.Write((short)1);                   // type: icon
            head.Write((short)frames.Count);

            var images = new List<byte[]>(frames.Count);
            foreach (IcoFrame frame in frames)
            {
                if (frame.Width <= 0 || frame.Height <= 0 || frame.Width != frame.Height)
                    throw new ArgumentException("frames must be square and positive");
                if (frame.Width > 256) throw new ArgumentException("a frame is at most 256 px");
                if (frame.Data == null || frame.Data.Length == 0)
                    throw new ArgumentException("a frame without data");

                head.Write((byte)(frame.Width == 256 ? 0 : frame.Width)); // 0 means 256
                head.Write((byte)(frame.Height == 256 ? 0 : frame.Height));
                head.Write((byte)0);                // colours in the palette
                head.Write((byte)0);                // reserved
                head.Write((short)1);               // planes
                head.Write((short)32);              // bits per pixel
                head.Write(frame.Data.Length);
                head.Write(offset);

                offset += frame.Data.Length;
                images.Add(frame.Data);
            }

            var file = new MemoryStream();
            file.Write(bytes, 0, bytes.Length);
            foreach (byte[] image in images) file.Write(image, 0, image.Length);
            return file.ToArray();
        }

        /// <summary>Parses a file back into its frames. Throws on anything malformed.</summary>
        public static IList<IcoFrame> Read(byte[] file)
        {
            if (file == null) throw new ArgumentNullException("file");
            if (file.Length < DirectorySize) throw new FormatException("shorter than a directory");

            var stream = new MemoryStream(file);
            var reader = new BinaryReader(stream);
            if (reader.ReadInt16() != 0) throw new FormatException("not an icon (reserved)");
            if (reader.ReadInt16() != 1) throw new FormatException("not an icon (type)");
            int count = reader.ReadInt16();
            if (count <= 0) throw new FormatException("no frames");

            var sizes = new int[count];
            var offsets = new int[count];
            var lengths = new int[count];
            for (int i = 0; i < count; i++)
            {
                int width = reader.ReadByte();
                reader.ReadByte();                  // height, the same as width here
                reader.ReadByte();                  // palette
                reader.ReadByte();                  // reserved
                reader.ReadInt16();                 // planes
                reader.ReadInt16();                 // bits per pixel
                lengths[i] = reader.ReadInt32();
                offsets[i] = reader.ReadInt32();
                sizes[i] = width == 0 ? 256 : width;
            }

            var frames = new List<IcoFrame>(count);
            for (int i = 0; i < count; i++)
            {
                if (offsets[i] < 0 || lengths[i] < 0 || offsets[i] + lengths[i] > file.Length)
                    throw new FormatException("frame " + i + " runs off the end");
                var data = new byte[lengths[i]];
                Array.Copy(file, offsets[i], data, 0, lengths[i]);
                frames.Add(new IcoFrame
                {
                    Width = sizes[i],
                    Height = sizes[i],
                    // A PNG frame starts with its signature; a DIB starts with its header size.
                    Png = data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E
                        && data[3] == 0x47,
                    Data = data
                });
            }
            return frames;
        }
    }
}
