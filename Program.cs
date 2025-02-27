using System.Data;
using System.IO.Ports;
using System.Text;

Console.WriteLine("Hello world!");
var dc20 = new DC20("COM6");
dc20.Init(DC20.BaudRate._115200);
var status = dc20.GetStatus();
if (status != null) {
    Console.WriteLine(status.Value.ToString());
    for (int i = 0; i < status.Value.PicturesTaken; i++) {
        var thumb = dc20.GetThumbnail((byte)(i + 1));
        File.WriteAllText($"./thumb_{i+1}.pgm", DC20Util.ThumbnailToPGM(thumb));
        var img_n = dc20.GetImage((byte)(i+1));
        File.WriteAllText($"./img_data_grey_{i+1}.pgm", DC20Util.ImageToPGM(img_n));
        File.WriteAllText($"./img_data_colour_{i+1}.ppm", DC20Util.ImageToPPM(img_n));
    }
}

public class DC20 { 
    public int LastResponse { get; private set; }
    public bool LastResponseCorrect {get; private set;}

    private bool hasReadStatus;
    private Status StatusInternal;

    private bool debug;
    public enum BaudRate {
        _9600,
        _19200,
        _38400,
        _57600,
        _115200
    }

    SerialPort serial_port;

    private void Debug(string text) {
        Console.WriteLine($"LR:{LastResponse},LRC:{LastResponseCorrect},{text}");
    }

    public DC20(string portName, bool debug_mode = false) {
        serial_port = new SerialPort(portName, baudRate: 9600, Parity.Even, dataBits: 8, StopBits.One);
        debug = debug_mode;
        serial_port.Open();
        Debug($"Hello, serial! {serial_port.IsOpen}");
        hasReadStatus = false;
    }
    ~DC20() {
        serial_port.Close();
    }

    private void Write(params byte[] bytes) {
        serial_port.Write(bytes, 0, bytes.Length);
        Debug($"Wrote [{string.Join(',', bytes.Select(x => x.ToString("X2")))}]");
    }
    private void WriteAck(int acknowledgement = 0xD2) {
        serial_port.Write(new byte[1] {(byte)acknowledgement}, 0, 1);
        Debug($"Wrote ACK {acknowledgement:X2}");
    }
    private void ReadAck(int expected = 0xD1) {
        LastResponse = serial_port.ReadByte();
        LastResponseCorrect = LastResponse == expected;
        Debug($"Read ACK got {LastResponse:X2} == exp {expected:X2}: {LastResponseCorrect}");
    }

    private (bool, byte[]) ReadWithChecksum(int byte_count) {
        byte[] status = new byte[byte_count];
        
        // var bytes_read = serial_port.Read(status, 0, byte_count); 
        // Not working. Why? Work out later.

        var bytes_read = 0;
        for (int i = 0; i < byte_count; i++) {
            var read = serial_port.ReadByte();
            if (read == -1)
                break;
            status[i] = (byte)read;
            bytes_read++;
        }

        var checksum = serial_port.ReadByte();
        var checksum_correct = status.Aggregate(0, (s, x) => s ^ x) == checksum;
        Debug($"Attempt read {byte_count} bytes, got {bytes_read}, checksum {checksum_correct}");
        return (bytes_read == byte_count && checksum_correct, status);
    }

    public bool Init(BaudRate newBaudRate = BaudRate._9600) {
        (byte BaudA, byte BaudB) = newBaudRate switch
        {
            BaudRate._9600 => ((byte)0x96, (byte)0x00),
            BaudRate._19200 => ((byte)0x19, (byte)0x20),
            BaudRate._38400 => ((byte)0x38, (byte)0x40),
            BaudRate._57600 => ((byte)0x57, (byte)0x60),
            BaudRate._115200 => ((byte)0x11, (byte)0x52),
            _ => throw new NotImplementedException(),
        };
        Write(0x41, 0x00,
            BaudA, BaudB,
            0x00, 0x00,
            0x00, 0x1A);
        ReadAck();
        Debug("Connected");
        serial_port.BaudRate = newBaudRate switch
        {
            BaudRate._9600 => 9600,
            BaudRate._19200 => 19200,
            BaudRate._38400 => 38400,
            BaudRate._57600 => 57600,
            BaudRate._115200 => 115200,
            _ => throw new NotImplementedException(),
        };
        return LastResponseCorrect;
    }

    public Status? GetStatus() {
        Write( 0x7F, 00, 00, 00, 00, 00, 00, 0x1A );
        ReadAck();
        var (correct, bytes) = ReadWithChecksum(256);
        Debug($"[{string.Join(", ", bytes.Select((x, i) => $"{i+1}:{x:X2}"))}]");
        var result = Status.From(bytes);
        StatusInternal = result;
        hasReadStatus = correct;
        WriteAck(0xD2);
        ReadAck(0x00);

        return correct ? result : null;
    }

    /// <summary>
    /// Starts from 1
    /// </summary>
    /// <param name="index">Starts from 1</param>
    /// <returns></returns>
    public Thumbnail GetThumbnail(byte index) {
        Write(0x56, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
        ReadAck();
        byte[] thumbnail = new byte[5120];
        for (int i = 0; i < 5; i++) {
            var (correct, bytes) = ReadWithChecksum(1024);
            WriteAck(0xD2);
            bytes.CopyTo(thumbnail, i * 1024);
        }
        ReadAck(0x00);
        Thumbnail thm = new Thumbnail();
        thm.GreyscaleData = new byte[4800];
        Array.Copy(thumbnail, thm.GreyscaleData, 4800);
        return thm;
    }

    public bool ChangeResolution(Resolution desired) {
        switch (desired) {
            case Resolution.High:
            Write(0x71, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1A);
            return true;
            case Resolution.Low:
            Write(0x71, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x1A);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Starts from 1.
    /// </summary>
    /// <param name="index">Starts from 1</param>
    /// <returns></returns>
    public Image GetImage(byte index) {
        Debug($"Attempt to read image {index}");
        var img = new Image();
        if (!hasReadStatus) {
            GetStatus();
        }
        Write(0x51, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
        ReadAck();

        // the camera sends 122 data blocks (if in high-res mode) or 61 data blocks (if in low-res mode)
        var byteCount = StatusInternal.Resolution == Resolution.High ? 122 : 61;
        byte[] raw_data = new byte[1024 * byteCount];
        for (int i = 0; i < byteCount; i++) {
            var (correct, bytes) = ReadWithChecksum(1024);
            WriteAck(0xD2);
            bytes.CopyTo(raw_data, i * 1024);
        }
        ReadAck(0x00);
        var rowWidth = StatusInternal.Resolution == Resolution.High ? 512 : 256;
        var columnHeight = 243;
        var header = new byte[rowWidth]; //For some reason the header goes into a row
        Array.Copy(raw_data, 0, header, 512, 0);
        img.RawHeader = header;
        img.Resolution = StatusInternal.Resolution;
        img.RawPixelData = Helper.New2D<byte>(columnHeight, rowWidth);
        for (int i = 0; i < columnHeight; i++) {
            Array.Copy(raw_data, rowWidth * (i+1), img.RawPixelData[i], 0, rowWidth);
        }
        img.MyHeader = Image.Header.From(header);
        return img;
    }


    public struct Image {
        /// <summary>
        /// hires margins
        /// LM: 1, RM: 10, TM: 0, BM: 1
        /// lowres margins
        /// LM: 1, RM: 5, TM: 0, BM: 1
        /// </summary>
        public byte[][] RawPixelData;

        /// <summary>
        /// Wrong place for this i know
        /// </summary>
        public Resolution Resolution {get => res; internal set {
            res = value;
            if (res == Resolution.Low) {
                cols = 256;
                r_margin = 5;
            } else {
                cols = 512;
                r_margin = 10;
            }
            rows = 243;
            l_margin = 1;
            t_margin = 0;
            b_margin = 1;
        }}
        private Resolution res;
        public byte[] RawHeader;
        public Header MyHeader;

        private short[][] initInterp;
        private short[][] hInterp;
        private short[][] vInterp;

        private (byte,byte,byte)[][] RGB;

        private int rows;
        private int cols;
        private int l_margin;
        private int r_margin;
        private int t_margin;
        private int b_margin;
        private int net_cols {get => cols - l_margin - r_margin;}
        private int net_rows {get => rows - t_margin - b_margin;}
        private int net_pixels {get => net_rows * net_cols;}

        /// <summary>
        /// Currently assumes hi-res
        /// </summary>
        /// <returns></returns>
        public byte[] ToGreyscale() {
            var greyscale_output = new byte[256*243];
            for (int row = 0; row < 243; row++) {
                for (int col = 0; col < 512; col += 2) {
                    greyscale_output[256 * row + (col / 2)] = (byte)((RawPixelData[row][col] + RawPixelData[row][col+1])/2);
                }
            }
            return greyscale_output;
        }

        private const short SCALE = 64;
        private const short SMAX = (256 * SCALE - 1);
        private const int H_INTERPOLATIONS = 3;
        /// <summary>
        /// Adaptation of the 'cmttoppm.c' file from YOSHIDA Hideki
        /// Original comment:
        ///  *
        ///  * Converts CMT file of Chinon ES-1000 or IMG file of LXDC to PPM file.
        ///  *
        ///  *	written by YOSHIDA Hideki <hideki@yk.rim.or.jp>
        ///  *	In public domain; you can do whatever you want to this program
        ///  *	as long as you admit that the original code is written by me.
        ///  *
        /// I must send a thank you email sometime; this code has useful to many.
        /// </summary>
        /// <returns>An image.</returns>
        public (byte,byte,byte)[][] ToRGBRaw() {
            if (initInterp == null) {
                InitInterp();
            }
            if (hInterp == null) {
                HInterp();
            }
            if (vInterp == null) {
                VInterp();
            }
            return RGB;
        }
        private void InitInterp() {
            initInterp = Helper.New2D<short>(rows, cols);
            for (int line = 0; line < rows; line++) {
                initInterp[line][l_margin] = (short)(RawPixelData[line][l_margin + 1] * SCALE);
                initInterp[line][cols - r_margin - 1] = (short)(RawPixelData[line][r_margin - 2] * SCALE);
                for (var column = l_margin + 1; column < cols - r_margin - 1; column++) {
                    initInterp[line][column] = (short)((RawPixelData[line][column - 1] + RawPixelData[line][column + 1]) * (SCALE/2));
                }
            }
        }
        private void HInterp() {
            hInterp = initInterp.Deepcopy();
            for (var line = t_margin; line < rows - b_margin + 1; line++) {
                for (var i = 0; i < H_INTERPOLATIONS; i++) {
                    for (var initial_column = l_margin + 1; initial_column <= l_margin + 2; initial_column++) {
                        for (var column = initial_column; column < cols - r_margin - 1; column += 2) {
                            hInterp[line][column] =
                                (short)(((float)RawPixelData[line][column - 1] /
                                    hInterp[line][column - 1] +
                                    (float)RawPixelData[line][column + 1] /
                                    hInterp[line][column + 1]) *
                                    RawPixelData[line][column] * (SCALE * SCALE / 2) + 0.5);
                        }
                    }
                }
            }
        }
        private void VInterp() {
            vInterp = Helper.New2D<short>(rows, cols);
            RGB = Helper.New2D<(byte,byte,byte)>(rows, cols);
            for (var line = t_margin; line < rows - b_margin; line++) {
                for (var column = l_margin; column < cols - r_margin; column++) {
                    int thisCCD = RawPixelData[line][column] * SCALE;
                    int upCCD = RawPixelData[line - 1][column] * SCALE;
                    int downCCD = RawPixelData[line + 1][column] * SCALE;
                    int thisHInterp = hInterp[line][column];
                    int thisIntensity = thisCCD + thisHInterp;
                    int upIntensity = upCCD + hInterp[line - 1][column];
                    int downIntensity = upCCD + hInterp[line + 1][column];

                    int thisVInterp;
                    if (line == t_margin) {
                        thisVInterp = (int)((float)downCCD / downIntensity * thisIntensity + 0.5f);
                    } else if (line == rows - b_margin - 1) {
                        thisVInterp = (int)((float)upCCD / upIntensity * thisIntensity + 0.5f);
                    } else {
                        thisVInterp = (int)(((float)upCCD / upIntensity + (float)downCCD / downIntensity) * thisIntensity / 2.0f + 0.5f);
                    }

                    int r2gb = 0, g2b = 0, rg2 = 0, rgb2 = 0, r = 0, g = 0, b = 0;

                    if (line % 2 == 1) {
                        if (column % 2 == 1) {
                            r2gb = thisCCD;
                            g2b = thisHInterp;
                            rg2 = thisVInterp;
                            r = (2 * (r2gb - g2b) + rg2) / 5;
                            g = (rg2 - r) / 2;
                            b = g2b - 2 * g;
                        } else {
                            g2b = thisCCD;
                            r2gb = thisHInterp;
                            rgb2 = thisVInterp;
                            r = (3 * r2gb - g2b - rgb2) / 5;
                            g = 2 * r - r2gb + g2b;
                            b = g2b - 2 * g;
                        }
                    } else {
                        if (column % 2 == 1) {
                            rg2 = thisCCD;
                            rgb2 = thisHInterp;
                            r2gb = thisVInterp;
                            b = (3 * rgb2 - r2gb - rg2) / 5;
                            g = (rgb2 - r2gb + rg2 - b) / 2;
                            r = rg2 - 2 * g;
                        } else {
                            rgb2 = thisCCD;
                            rg2 = thisHInterp;
                            g2b = thisVInterp;
                            b = (g2b - 2 * (rg2 - rgb2)) / 5;
                            g = (g2b - b) / 2;
                            r = rg2 - 2 * g;
                        }
                    }
                    if (r < 0) r = 0;
                    if (g < 0) g = 0;
                    if (b < 0) b = 0;
                    // Should probably keep these in a bigger data type while working on them...
                    RGB[line][column] = ((byte)r,(byte)g,(byte)b);
                }
            }
        }

        public struct Header {
            CameraModel Model;
            UInt16 PictureNumber;
            Resolution Resolution;
            UInt32 FileSize;

            sbyte EXP1;
            sbyte EXP2;
            sbyte EXP3;
            sbyte EXP4;

            public static Header From(byte[] bytes) {
                var header = new Header();
                header.Model = (CameraModel)bytes[1];
                header.PictureNumber = BitConverter.ToUInt16(bytes, 2);
                header.Resolution = (Resolution)bytes[4];
                header.FileSize = BitConverter.ToUInt32(bytes, 6);
                
                header.EXP1 = unchecked((sbyte)bytes[16]);
                header.EXP2 = unchecked((sbyte)bytes[17]);
                header.EXP3 = unchecked((sbyte)bytes[34]);
                header.EXP4 = unchecked((sbyte)bytes[35]);
                return header;
            }
        }
    } 
    
    public struct Thumbnail {
        public const int Width = 80;
        public const int Height = 60;
        public byte[] GreyscaleData;
    }

    public enum CameraModel : byte {
        DC20 = 0x20,
        DC25 = 0x25
    }
    public enum Resolution : byte {
        High = 0x00,
        Low = 0x01
    }
    /// <summary>
    /// It's as vague as this in the spec '1 if battery down, else 0'
    /// </summary>
    public enum Battery : byte {
        Down = 0x01,
        Other = 0x00
    }

    public struct Status {
        public CameraModel Model;
        public byte PicturesTaken;
        public byte PicturesRemaining;
        public Resolution Resolution;
        public Battery Battery;

        public static Status From(byte[] bytes) {
            Status s = new Status();
            s.Model = (CameraModel)bytes[1];
            s.PicturesTaken = bytes[9];
            s.PicturesRemaining = bytes[11];
            s.Resolution = (Resolution)bytes[23];
            s.Battery = (Battery)bytes[29];
            return s;
        }

        public override string ToString()
        {
            return $"{{Model: {Model}, PicturesTaken: {PicturesTaken}, PicturesRemaining: {PicturesRemaining}, Resolution: {Resolution}, Battery: {Battery} }}";
        }
    }
}

static class DC20Util {
    public static string ThumbnailToPGM(DC20.Thumbnail thumbnail) {
        var sb = new StringBuilder();
        sb.AppendLine("P2");
        sb.AppendLine($"{80} {60}");
        sb.AppendLine("255");
        int i = 0;
        for (int x = 0; x < DC20.Thumbnail.Width; x++) {
            for (int y = 0; y < DC20.Thumbnail.Height; y++) {
                sb.Append($"{thumbnail.GreyscaleData[i]} ");
                i++;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
    public static string ImageToPGM(DC20.Image img) {
        var sb = new StringBuilder();
        sb.AppendLine("P2");
        sb.AppendLine($"{256} {243}");
        sb.AppendLine("255");
        int i = 0;
        var greyscale = img.ToGreyscale();
        for (int x = 0; x < 256; x++) {
            for (int y = 0; y < 243; y++) {
                sb.Append($"{greyscale[i]} ");
                i++;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
    public static string ImageToPPM(DC20.Image img) {
        var rgb = img.ToRGBRaw();
        var sb = new StringBuilder();
        sb.AppendLine("P3");
        sb.AppendLine($"{rgb[0].Length} {rgb.Length}");
        sb.AppendLine("255");
        int i = 0;
        for (int x = 0; x < 256; x++) {
            for (int y = 0; y < 243; y++) {
                sb.AppendLine($"{rgb[0]} {rgb[1]} {rgb[2]}");
                i++;
            }
        }
        return sb.ToString();
    }
}

internal static class Helper {
    internal static T[][] New2D<T>(int a, int b) {
        var output = new T[a][];
        for (int i = 0; i < a; i++) {
            output[i] = new T[b];
        }
        return output;
    }
    /// <summary>
    /// A seal gets fed cement every time I have to rewrite a new Deepcopy function
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="thing"></param>
    /// <returns></returns>
    internal static T[][] Deepcopy<T>(this T[][] thing) {
        var thingDimSize = thing.Length;
        var output = new T[thingDimSize][];
        for (int i = 0; i < thingDimSize; i++) {
            var len = thing[i].Length;
            output[i] = new T[len];
            Array.Copy(thing[i], output[i], len);
        }
        return output;

    }
}