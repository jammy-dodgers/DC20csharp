using System.Data;
using System.IO.Ports;
using System.Text;



public class DC20
{
    public int LastResponse { get; private set; }
    public bool LastResponseCorrect { get; private set; }

    private bool hasReadStatus;
    private Status StatusInternal;

    private bool debug;
    public enum BaudRate
    {
        _9600,
        _19200,
        _38400,
        _57600,
        _115200
    }

    SerialPort serial_port;

    private void Debug(string text)
    {
        if (debug)
            Console.WriteLine($"LR:{LastResponse},LRC:{LastResponseCorrect},{text}");
    }

    public DC20(string portName, bool debug_mode = false)
    {
        serial_port = new SerialPort(portName, baudRate: 9600, Parity.Even, dataBits: 8, StopBits.One);
        debug = debug_mode;
        serial_port.Open();
        Debug($"Hello, serial! {serial_port.IsOpen}");
        hasReadStatus = false;
    }
    ~DC20()
    {
        serial_port.Close();
    }

    private void Write(params byte[] bytes)
    {
        serial_port.Write(bytes, 0, bytes.Length);
        Debug($"Wrote [{string.Join(',', bytes.Select(x => x.ToString("X2")))}]");
    }
    private void WriteAck(int acknowledgement = 0xD2)
    {
        serial_port.Write(new byte[1] { (byte)acknowledgement }, 0, 1);
        Debug($"Wrote ACK {acknowledgement:X2}");
    }
    private void ReadAck(int expected = 0xD1)
    {
        LastResponse = serial_port.ReadByte();
        LastResponseCorrect = LastResponse == expected;
        Debug($"Read ACK got {LastResponse:X2} == exp {expected:X2}: {LastResponseCorrect}");
    }

    private (bool, byte[]) ReadWithChecksum(int byte_count)
    {
        byte[] status = new byte[byte_count];

        // var bytes_read = serial_port.Read(status, 0, byte_count); 
        // Not working. Why? Work out later.

        var bytes_read = 0;
        for (int i = 0; i < byte_count; i++)
        {
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

    public bool Init(BaudRate newBaudRate = BaudRate._9600)
    {
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

    public Status? GetStatus()
    {
        Write(0x7F, 00, 00, 00, 00, 00, 00, 0x1A);
        ReadAck();
        var (correct, bytes) = ReadWithChecksum(256);
        Debug($"[{string.Join(", ", bytes.Select((x, i) => $"{i + 1}:{x:X2}"))}]");
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
    public Thumbnail GetThumbnail(byte index)
    {
        Write(0x56, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
        ReadAck();
        byte[] thumbnail = new byte[5120];
        for (int i = 0; i < 5; i++)
        {
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

    public bool ChangeResolution(Resolution desired)
    {
        switch (desired)
        {
            case Resolution.High:
                Write(0x71, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1A);
                return true;
            case Resolution.Low:
                Write(0x71, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x1A);
                return true;
        }
        return false;
    }

    public byte[] GetRawCCD(byte index)
    {
        if (!hasReadStatus)
        {
            var status = GetStatus();
            //todo: sanity check index vs picture count
        }
        Write(0x51, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
        ReadAck();

        var byteCount = StatusInternal.Resolution == Resolution.High ? 122 : 61;
        byte[] raw_data = new byte[1024 * byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            var (correct, bytes) = ReadWithChecksum(1024);
            WriteAck(0xD2);
            bytes.CopyTo(raw_data, i * 1024);
        }
        return raw_data;
    }

    // /// <summary>
    // /// Starts from 1.
    // /// </summary>
    // /// <param name="index">Starts from 1</param>
    // /// <returns></returns>
    // public Image GetImage(byte index)
    // {
    //     Debug($"Attempt to read image {index}");
    //     var img = new Image();
    //     if (!hasReadStatus)
    //     {
    //         GetStatus();
    //         //todo: sanity check index vs picture count
    //     }
    //     Write(0x51, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
    //     ReadAck();

    //     // the camera sends 122 data blocks (if in high-res mode) or 61 data blocks (if in low-res mode)
    //     var byteCount = StatusInternal.Resolution == Resolution.High ? 122 : 61;
    //     byte[] raw_data = new byte[1024 * byteCount];
    //     for (int i = 0; i < byteCount; i++)
    //     {
    //         var (correct, bytes) = ReadWithChecksum(1024);
    //         WriteAck(0xD2);
    //         bytes.CopyTo(raw_data, i * 1024);
    //     }
    //     ReadAck(0x00);
    //     var rowWidth = StatusInternal.Resolution == Resolution.High ? 512 : 256;
    //     var columnHeight = 243;
    //     var header = new byte[rowWidth]; //For some reason the header goes into a row
    //     Array.Copy(raw_data, 0, header, 0, rowWidth);
    //     img.RawHeader = header;
    //     img.Resolution = StatusInternal.Resolution;
    //     img.RawData = Helper.New2D<byte>(columnHeight + 1, rowWidth);
    //     img.RawPixelData = Helper.New2D<byte>(columnHeight, rowWidth);
    //     Array.Copy(header, 0, img.RawData[0], 0, rowWidth);
    //     for (int i = 0; i < columnHeight; i++)
    //     {
    //         Array.Copy(raw_data, rowWidth * (i + 1), img.RawData[i + 1], 0, rowWidth);
    //         Array.Copy(raw_data, rowWidth * (i + 1), img.RawPixelData[i], 0, rowWidth);
    //     }
    //     img.MyHeader = Image.Header.From(header);
    //     return img;
    // }


    // public struct Image {
    //     /// <summary>
    //     /// hires margins
    //     /// LM: 1, RM: 10, TM: 0, BM: 1
    //     /// lowres margins
    //     /// LM: 1, RM: 5, TM: 0, BM: 1
    //     /// first row is header
    //     /// </summary>
    //     public byte[][] RawData;
    //     public byte[][] RawPixelData;

    //     /// <summary>
    //     /// Wrong place for this i know
    //     /// </summary>
    //     public Resolution Resolution;
    //     private Resolution res;
    //     public byte[] RawHeader;
    //     public Header MyHeader;

    //     /// <summary>
    //     /// Currently assumes hi-res
    //     /// </summary>
    //     /// <returns></returns>
    //     public byte[] ToGreyscale() {
    //         var greyscale_output = new byte[256*243];
    //         for (int row = 0; row < 243; row++) {
    //             for (int col = 0; col < 512; col += 2) {
    //                 greyscale_output[256 * row + (col / 2)] = (byte)((RawPixelData[row][col] + RawPixelData[row][col+1])/2);
    //             }
    //         }
    //         return greyscale_output;
    //     }

    //     public byte[] ToRawData() {
    //         return RawData.SelectMany(x => x).ToArray();
    //     }
    //     public static Image FromRawData(byte[] data) {
    //         var img = new Image();
    //         img.MyHeader = Header.From(data);
    //         var rowWidth = img.MyHeader.Resolution == Resolution.High ? 512 : 256;
    //         img.RawData = data.Chunk(rowWidth).ToArray();
    //         img.RawHeader = img.RawData[0];
    //         img.RawPixelData = img.RawData.Skip(1).ToArray();
    //         return img;
    //     }

    //     public struct Header {
    //         public CameraModel Model;
    //         public UInt16 PictureNumber;
    //         public Resolution Resolution;
    //         public UInt32 FileSize;

    //         public sbyte EXP1;
    //         public sbyte EXP2;
    //         public sbyte EXP3;
    //         public sbyte EXP4;

    //         public static Header From(byte[] bytes) {
    //             var header = new Header();
    //             header.Model = (CameraModel)bytes[1];
    //             header.PictureNumber = BitConverter.ToUInt16(bytes, 2);
    //             header.Resolution = (Resolution)bytes[4];
    //             header.FileSize = BitConverter.ToUInt32(bytes, 6);

    //             header.EXP1 = unchecked((sbyte)bytes[16]);
    //             header.EXP2 = unchecked((sbyte)bytes[17]);
    //             header.EXP3 = unchecked((sbyte)bytes[34]);
    //             header.EXP4 = unchecked((sbyte)bytes[35]);
    //             return header;
    //         }
    //     }
    // } 

    public struct Thumbnail
    {
        public const int Width = 80;
        public const int Height = 60;
        public byte[] GreyscaleData;
    }

    public enum CameraModel : byte
    {
        DC20 = 0x20,
        DC25 = 0x25
    }
    public enum Resolution : byte
    {
        High = 0x00,
        Low = 0x01
    }
    /// <summary>
    /// It's as vague as this in the spec '1 if battery down, else 0'
    /// </summary>
    public enum Battery : byte
    {
        Down = 0x01,
        Other = 0x00
    }

    public struct Status
    {
        public CameraModel Model;
        public byte PicturesTaken;
        public byte PicturesRemaining;
        public Resolution Resolution;
        public Battery Battery;

        public static Status From(byte[] bytes)
        {
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

static class DC20Util
{
    public static string ThumbnailToPGM(DC20.Thumbnail thumbnail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("P2");
        sb.AppendLine($"{80} {60}");
        sb.AppendLine("255");
        int i = 0;
        for (int x = 0; x < DC20.Thumbnail.Width; x++)
        {
            for (int y = 0; y < DC20.Thumbnail.Height; y++)
            {
                sb.Append($"{thumbnail.GreyscaleData[i]} ");
                i++;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

internal static class Helper
{
    internal static T[][] New2D<T>(int a, int b)
    {
        var output = new T[a][];
        for (int i = 0; i < a; i++)
        {
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
    internal static T[][] Deepcopy<T>(this T[][] thing)
    {
        var thingDimSize = thing.Length;
        var output = new T[thingDimSize][];
        for (int i = 0; i < thingDimSize; i++)
        {
            var len = thing[i].Length;
            output[i] = new T[len];
            Array.Copy(thing[i], output[i], len);
        }
        return output;

    }
}