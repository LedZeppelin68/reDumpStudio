using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Xml;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace reDumpStudio
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public struct name_md5
        {
            public string name;
            public string crc;
            public string md5;
            public string size;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(textBox1.Text == string.Empty)
            {
                MessageBox.Show("input working directory");
                return;
            }
            if (textBox2.Text == string.Empty)
            {
                MessageBox.Show("input output directory");
                return;
            }
            if (comboBox1.Text == string.Empty)
            {
                MessageBox.Show("choose dat");
                return;
            }
            if (comboBox2.Text == string.Empty)
            {
                MessageBox.Show("choose game");
                return;
            }

            XmlDocument dat = new XmlDocument();
            dat.Load(comboBox1.Text);

            string romname = comboBox2.Text;

            XmlNode game = dat.SelectSingleNode(string.Format("//game[@name=\"{0}\"]", romname));

            List<name_md5> name_md5_collection = new List<name_md5>();
            
            foreach(XmlNode rom in game.SelectNodes("rom"))
            {
                if(!rom.Attributes["name"].Value.Contains(".gdi"))
                {
                    name_md5_collection.Add(new name_md5 {
                        name = rom.Attributes["name"].Value,
                        crc = rom.Attributes["crc"].Value,
                        md5 = rom.Attributes["md5"].Value,
                        size = rom.Attributes["size"].Value
                    });
                }
            }

            List<string> files = new List<string>();

            foreach(string s in Directory.GetFiles(textBox1.Text))
            {
                if(Path.GetExtension(s) != ".gdi")
                {
                    files.Add(s);
                }
            }

            if (name_md5_collection.Count == files.Count)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    Roll(name_md5_collection[i], files[i]);
                }
            }
        }

        static UInt32 kCrcPoly = 0xEDB88320;
        //static int WINSIZE = 100;
        //static int TESTSIZE = 200;
        static uint CRC_INIT_VAL = 0xffffffff;

        private void Roll(name_md5 name_md5, string p)
        {
            string md5 = name_md5.md5;
            UInt32 crc = Convert.ToUInt32(name_md5.crc, 16);
            Int64 size = Convert.ToInt64(name_md5.size);
            string filename = name_md5.name;

            string samplepath = p;

            UInt32 start_value = 0;
            long roll_window_bytes = size;
            long add = ~start_value;
            add = Multiply(add, Xpow8N(roll_window_bytes));
            add ^= 0xffffffff;

            long mul = 0x0000000080000000 ^ Xpow8N(1);
            add = Multiply(add, mul);

            mul = XpowN(8 * roll_window_bytes + 0x00000020);

            UInt32[] out_ = new UInt32[256];

            for (uint ii = 0; ii < 256; ++ii)
            {
                out_[ii] = (uint)(MultiplyUnnormalized(ii, 8, mul) ^ add);
            }

            UInt32 i, j, r, crc2;

            UInt32[] CRCTab = new UInt32[256];
            UInt32[] FastCRCTab = new UInt32[256];
            UInt32[] RollingCRCTab = new UInt32[256];

            FastTableBuild(ref FastCRCTab, kCrcPoly);

            for (i = 0; i < 256; i++)
            {
                r = i;
                for (j = 0; j < 8; j++)
                {
                    r = (r >> 1) ^ (kCrcPoly & ~((r & 1) - 1));
                }
                CRCTab[i] = r;
            }


            FileStream samlpe_reader = new FileStream(samplepath, FileMode.Open);
            byte[] first16bytes = new byte[16];
            samlpe_reader.Read(first16bytes, 0, 16);
            samlpe_reader.Position = 0;

            //byte[] raw = File.ReadAllBytes(samplepath);

            bool data_track = checksync(ref first16bytes);

            //List<byte> total_buffer = new List<byte>();

            long size_of_total_buffer = 0;
            if(data_track)
            {
                size_of_total_buffer = samlpe_reader.Length + 6 * 2352 * 75;
            }
            else
            {
                size_of_total_buffer = samlpe_reader.Length + 4 * 2352 * 75;
            }

            byte[] total_buffer = new byte[size_of_total_buffer];

            MemoryStream ms_buffer = new MemoryStream(total_buffer);

            MemoryStream gap2sec = new MemoryStream(new byte[2352 * 150]);
            gap2sec.CopyTo(ms_buffer);
            gap2sec.Position = 0;

            if (data_track)
            {
                MemoryStream pregap = new MemoryStream(generatePregap(ref first16bytes));
                pregap.CopyTo(ms_buffer);
                pregap.Dispose();
            }

            samlpe_reader.CopyTo(ms_buffer);
            samlpe_reader.Dispose();

            gap2sec.CopyTo(ms_buffer);
            gap2sec.Dispose();

            crc2 = calcCRC(total_buffer, (int)roll_window_bytes, CRCTab, 0);
            for (i = 0; i < total_buffer.Length - roll_window_bytes; i++)
            {
                crc2 = (crc2 >> 8) ^ FastCRCTab[(byte)(crc2) ^ total_buffer[(int)(roll_window_bytes + i)]] ^ out_[total_buffer[(int)i]];

                if (crc2 == crc)
                {
                    using(MD5 hash = MD5.Create())
                    {
                        string q = BitConverter.ToString(hash.ComputeHash(total_buffer, (int)(i + 1), (int)roll_window_bytes)).Replace("-", "").ToLower();

                        if (q == md5)
                        {
                            Console.WriteLine(string.Format("{0}: offset {1}", crc2, i + 1));

                            FileStream writer = new FileStream(Path.Combine(textBox2.Text, filename), FileMode.Create);
                            writer.Write(total_buffer, (int)(i + 1), (int)roll_window_bytes);
                            writer.Close();
                            break;
                        }
                    }
                    

                }
            }
            MessageBox.Show("Complete");
        }

        private byte[] generatePregap(ref byte[] raw)
        {
            FillEDCECCLuts();
            byte[] msf = new byte[3];

            for (int i = 0; i < 3; i++)
            {
                msf[i] = raw[12 + i];
            }

            int minutes = msf_table.IndexOf(msf[0]);
            int seconds = msf_table.IndexOf(msf[1]);
            int frames = msf_table.IndexOf(msf[2]);

            int sector = minutes * 75 * 60 + seconds * 75 + frames;

            sector -= 150;
            
            List<byte> pregap = new List<byte>();

            for (int i = 0; i < 2 * 75; i++)
            {
                byte[] chunk = new byte[2352];

                for (int j = 0; j < 12; j++)
                {
                    chunk[j] = sync[j];
                }

                msf = GetMSF(sector);

                for (int j = 0; j < 3; j++)
                {
                    chunk[j + 12] = msf[j];
                }

                chunk[15] = 1;

                sector++;

                CalculateEDC(ref chunk, 0, 2064);
                CalculateECCP(ref chunk);
                CalculateECCQ(ref chunk);

                pregap.AddRange(chunk);

                
            }

            //File.WriteAllBytes(sector + ".bin", pregap.ToArray());

            return pregap.ToArray();
        }

        private void CalculateECCQ(ref byte[] chunk)
        {
            UInt32 major_count, minor_count, major_mult, minor_inc;
            major_count = 52;
            minor_count = 43;
            major_mult = 86;
            minor_inc = 88;

            var eccsize = major_count * minor_count;
            UInt32 major, minor;
            for (major = 0; major < major_count; major++)
            {
                var index = (major >> 1) * major_mult + (major & 1);
                byte ecc_a = 0;
                byte ecc_b = 0;
                for (minor = 0; minor < minor_count; minor++)
                {
                    byte temp = chunk[12 + index];
                    index += minor_inc;
                    if (index >= eccsize) index -= eccsize;
                    ecc_a ^= temp;
                    ecc_b ^= temp;
                    ecc_a = ecc_f_lut[ecc_a];
                }
                ecc_a = ecc_b_lut[ecc_f_lut[ecc_a] ^ ecc_b];
                chunk[2076 + 172 + major] = ecc_a;
                chunk[2076 + 172 + major + major_count] = (byte)(ecc_a ^ ecc_b);
            }
        }

        private void CalculateECCP(ref byte[] chunk)
        {
            UInt32 major_count, minor_count, major_mult, minor_inc;
            major_count = 86;
            minor_count = 24;
            major_mult = 2;
            minor_inc = 86;

            var eccsize = major_count * minor_count;
            UInt32 major, minor;
            for (major = 0; major < major_count; major++)
            {
                var index = (major >> 1) * major_mult + (major & 1);
                byte ecc_a = 0;
                byte ecc_b = 0;
                for (minor = 0; minor < minor_count; minor++)
                {
                    byte temp = chunk[12 + index];
                    index += minor_inc;
                    if (index >= eccsize) index -= eccsize;
                    ecc_a ^= temp;
                    ecc_b ^= temp;
                    ecc_a = ecc_f_lut[ecc_a];
                }
                ecc_a = ecc_b_lut[ecc_f_lut[ecc_a] ^ ecc_b];
                chunk[2076 + major] = ecc_a;
                chunk[2076 + major + major_count] = (byte)(ecc_a ^ ecc_b);
            }
        }

        internal static uint[] edc_lut = new uint[256];
        internal static byte[] ecc_f_lut = new byte[256];
        internal static byte[] ecc_b_lut = new byte[256];

        private void FillEDCECCLuts()
        {
            UInt32 k, l, m;

            for (k = 0; k < 256; k++)
            {
                l = (UInt32)((k << 1) ^ ((k & 0x80) != 0 ? 0x11d : 0));
                ecc_f_lut[k] = (byte)l;
                ecc_b_lut[k ^ l] = (byte)k;
                m = k;

                for (l = 0; l < 8; l++)
                {
                    m = (m >> 1) ^ ((m & 1) != 0 ? 0xd8018001 : 0);
                }
                edc_lut[k] = m;
            }
        }

        private void CalculateEDC(ref byte[] buffer, int offset, int count)
        {
            UInt32 edc = 0;
            //int count = 0;
            var i = 0;
            //int offset = 0;

            while (i != count)
            {
                edc = (UInt32)((edc >> 8) ^ edc_lut[(edc ^ (buffer[offset + i++])) & 0xff]);
            }
            byte[] edc_ = BitConverter.GetBytes(edc);

            for (i = 0; i < 4; i++)
            {
                buffer[2064 + i] = edc_[i];
            }
        }

        private byte[] GetMSF(int sector_number)
        {
            byte[] msf = new byte[3];
            int minutes = sector_number / 4500;
            int seconds = sector_number % 4500 / 75;
            int frames = sector_number % 75;

            msf[0] = msf_table[minutes];
            msf[1] = msf_table[seconds];
            msf[2] = msf_table[frames];

            return msf;
        }

        static List<byte> msf_table = new List<byte> {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99,
            0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9,
            0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9,
            0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9,
            0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9
        };


        private bool checksync(ref byte[] raw)
        {
            for (int i = 0; i < 12; i++)
            {
                if (raw[i] != sync[i]) return false;
            }
            return true;
        }

        static byte[] sync = { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };

        static int degree_ = 0x20;
        private static long MultiplyUnnormalized(uint unnorm, int degree, long m)
        {
            uint v = unnorm;
            long result = 0;
            while (degree > 0x20)
            {
                degree -= 0x20;
                long value = v & (one_ | (one_ - 1));
                result ^= Multiply(value, Multiply(m, XpowN(degree)));
                v >>= degree_;
            }
            result ^= Multiply(v << (degree_ - degree), m);
            return result;
        }

        static long one_ = 0x0000000080000000;

        private static long Xpow8N(long n)
        {
            return XpowN(n << 3);
        }

        private static long XpowN(long n)
        {
            long one = one_;
            long result = one;

            for (int i = 0; n != 0; ++i, n >>= 1)
            {
                if ((n & 1) == 1)
                {
                    result = Multiply(result, x_pow_2n_[i]);
                }
            }

            return result;
        }

        static long[] x_pow_2n_ = {
            0x0000000040000000,0x0000000020000000,0x0000000008000000,0x0000000000800000,
            0x0000000000008000,0x00000000edb88320,0x00000000b1e6b092,0x00000000a06a2517,
            0x00000000ed627dae,0x0000000088d14467,0x00000000d7bbfe6a,0x00000000ec447f11,
            0x000000008e7ea170,0x000000006427800e,0x000000004d47bae0,0x0000000009fe548f,
            0x0000000083852d0f,0x0000000030362f1a,0x000000007b5a9cc3,0x0000000031fec169,
            0x000000009fec022a,0x000000006c8dedc4,0x0000000015d6874d,0x000000005fde7a4e,
            0x00000000bad90e37,0x000000002e4e5eef,0x000000004eaba214,0x00000000a8a472c0,
            0x00000000429a969e,0x00000000148d302a,0x00000000c40ba6d0,0x00000000c4e22c3c,
            0x0000000040000000,0x0000000020000000,0x0000000008000000,0x0000000000800000,
            0x0000000000008000,0x00000000edb88320,0x00000000b1e6b092,0x00000000a06a2517,
            0x00000000ed627dae,0x0000000088d14467,0x00000000d7bbfe6a,0x00000000ec447f11,
            0x000000008e7ea170,0x000000006427800e,0x000000004d47bae0,0x0000000009fe548f,
            0x0000000083852d0f,0x0000000030362f1a,0x000000007b5a9cc3,0x0000000031fec169,
            0x000000009fec022a,0x000000006c8dedc4,0x0000000015d6874d,0x000000005fde7a4e,
            0x00000000bad90e37,0x000000002e4e5eef,0x000000004eaba214,0x00000000a8a472c0,
            0x00000000429a969e,0x00000000148d302a,0x00000000c40ba6d0,0x00000000c4e22c3c
        };

        private static long Multiply(long aa, long bb)
        {
            long a = aa;
            long b = bb;
            if ((a ^ (a - 1)) < (b ^ (b - 1)))
            {
                long temp = a;
                a = b;
                b = temp;
            }

            if (a == 0)
            {
                return a;
            }

            long product = 0;
            long one = one_;
            for (; a != 0; a <<= 1)
            {
                if ((a & one) != 0)
                {
                    product ^= b;
                    a ^= one;
                }
                b = (b >> 1) ^ normalize_[(byte)(b & 1)];
            }

            return product;
        }

        static long[] normalize_ = { 0x0000000000000000, 0x00000000edb88320 };

        private static uint calcCRC(byte[] buffer, int len, uint[] CRCTable, int offset)
        {
            UInt32 crc = init_CRC();
            for (int i = offset; i < len + offset; i++)
            {
                crc = update_CRC(crc, CRCTable, buffer[i]);
            }
            return finish_CRC(crc);
        }

        private static uint finish_CRC(uint crc)
        {
            return ((crc) ^ CRC_INIT_VAL);
        }

        private static uint update_CRC(uint crc, uint[] CRCTable, uint c)
        {
            return (CRCTable[((crc) ^ (c)) & 0xFF] ^ ((crc) >> 8));
        }

        private static uint init_CRC()
        {
            return CRC_INIT_VAL;
        }

        private static void FastTableBuild(ref uint[] CRCTable, uint seed)
        {
            UInt32 i, j, r;

            CRCTable[0] = 0;
            CRCTable[128] = r = seed;
            for (i = 64; i != 0; i /= 2)
            {
                CRCTable[i] = r = (r >> 1) ^ (kCrcPoly & ~((r & 1) - 1));
            }

            for (i = 2; i < 256; i *= 2)
            {
                for (j = 1; j < i; j++)
                {
                    CRCTable[i + j] = CRCTable[i] ^ CRCTable[j];
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            textBox1.Text = folderBrowserDialog1.SelectedPath;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            textBox2.Text = folderBrowserDialog1.SelectedPath;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboBox2.Items.Clear();
            XmlDocument dat = new XmlDocument();
            dat.Load(comboBox1.Text);

            //string romname = "KISS Psycho Circus - The Nightmare Child (Europe)";

            XmlNodeList game = dat.DocumentElement.SelectNodes("game");

            SortedSet<string> games = new SortedSet<string>();

            foreach(XmlNode node in game)
            {
                games.Add(node.Attributes["name"].Value);
            }

            comboBox2.Items.AddRange(games.ToArray<string>());
        }

        private void comboBox1_MouseClick(object sender, MouseEventArgs e)
        {
            comboBox1.Items.Clear();
            string[] dats = Directory.GetFiles("dat");


            comboBox1.Items.AddRange(dats);
        }
    }
}
