// LCG1 golden container reader/writer (format: Tests/LocalChart/tools/golden_io.py).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SushiChain.LocalChart.Tests
{
    internal sealed class GoldenArray
    {
        public string Name = "";
        public string Dtype = "";
        public int[] Shape = new int[0];
        public byte[] Data = new byte[0];

        public float[] F4() { Expect("f4"); var r = new float[Data.Length / 4]; Buffer.BlockCopy(Data, 0, r, 0, Data.Length); return r; }
        public double[] F8() { Expect("f8"); var r = new double[Data.Length / 8]; Buffer.BlockCopy(Data, 0, r, 0, Data.Length); return r; }
        public long[] I8() { Expect("i8"); var r = new long[Data.Length / 8]; Buffer.BlockCopy(Data, 0, r, 0, Data.Length); return r; }
        public int[] I4() { Expect("i4"); var r = new int[Data.Length / 4]; Buffer.BlockCopy(Data, 0, r, 0, Data.Length); return r; }
        public int[] I1() { Expect("i1"); var r = new int[Data.Length]; for (int i = 0; i < r.Length; i++) r[i] = (sbyte)Data[i]; return r; }

        public int[] Ints()
        {
            switch (Dtype)
            {
                case "i8": { var a = I8(); var r = new int[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = checked((int)a[i]); return r; }
                case "i4": return I4();
                case "i1": return I1();
                default: throw new InvalidDataException($"{Name}: not an int array ({Dtype})");
            }
        }

        private void Expect(string d) { if (Dtype != d) throw new InvalidDataException($"{Name}: dtype {Dtype}, expected {d}"); }
    }

    internal sealed class GoldenFile
    {
        public readonly Dictionary<string, GoldenArray> Arrays = new Dictionary<string, GoldenArray>();
        public JsonElement Meta;

        public bool Has(string name) => Arrays.ContainsKey(name);
        public GoldenArray this[string name] => Arrays.TryGetValue(name, out var a) ? a : throw new KeyNotFoundException(name);

        public static GoldenFile Read(string path)
        {
            byte[] raw;
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                raw = ms.ToArray();
            }
            if (Encoding.ASCII.GetString(raw, 0, 4) != "LCG1") throw new InvalidDataException(path + ": not LCG1");
            int hlen = BitConverter.ToInt32(raw, 4);
            var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(raw, 8, hlen));
            var g = new GoldenFile { Meta = doc.RootElement.GetProperty("meta").Clone() };
            int baseOff = 8 + hlen;
            foreach (var e in doc.RootElement.GetProperty("arrays").EnumerateArray())
            {
                var a = new GoldenArray
                {
                    Name = e.GetProperty("name").GetString() ?? "",
                    Dtype = e.GetProperty("dtype").GetString() ?? "",
                };
                var shape = new List<int>();
                foreach (var s in e.GetProperty("shape").EnumerateArray()) shape.Add(s.GetInt32());
                a.Shape = shape.ToArray();
                int off = e.GetProperty("offset").GetInt32(), nb = e.GetProperty("nbytes").GetInt32();
                a.Data = new byte[nb];
                Buffer.BlockCopy(raw, baseOff + off, a.Data, 0, nb);
                g.Arrays[a.Name] = a;
            }
            return g;
        }

        public static void Write(string path, IList<(string name, string dtype, int[] shape, byte[] data)> arrays, string metaJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"meta\":").Append(metaJson).Append(",\"arrays\":[");
            long off = 0;
            for (int i = 0; i < arrays.Count; i++)
            {
                var a = arrays[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(a.name).Append("\",\"dtype\":\"").Append(a.dtype).Append("\",\"shape\":[")
                  .Append(string.Join(",", a.shape)).Append("],\"offset\":").Append(off).Append(",\"nbytes\":").Append(a.data.Length).Append('}');
                off += a.data.Length;
            }
            sb.Append("]}");
            var header = Encoding.UTF8.GetBytes(sb.ToString());
            using var fs = File.Create(path);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            gz.Write(Encoding.ASCII.GetBytes("LCG1"), 0, 4);
            gz.Write(BitConverter.GetBytes(header.Length), 0, 4);
            gz.Write(header, 0, header.Length);
            foreach (var a in arrays) gz.Write(a.data, 0, a.data.Length);
        }

        public static byte[] Bytes(float[] a) { var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); return b; }
        public static byte[] Bytes(double[] a) { var b = new byte[a.Length * 8]; Buffer.BlockCopy(a, 0, b, 0, b.Length); return b; }
        public static byte[] BytesI8(int[] a) { var l = new long[a.Length]; for (int i = 0; i < a.Length; i++) l[i] = a[i]; var b = new byte[l.Length * 8]; Buffer.BlockCopy(l, 0, b, 0, b.Length); return b; }
    }
}
