using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine("Hello world!");
var dc20 = new DC20("COM6");
dc20.Init(DC20.BaudRate._115200);
var status = dc20.Status();
if (status != null) {
    Console.WriteLine(status.Value.ToString());
    for (int i = 0; i < status.Value.PicturesTaken; i++) {
        var thumb = dc20.GetThumbnail((byte)(i + 1));
        File.WriteAllText($"./thumb_{i+1}.pgm", DC20Util.ThumbnailToPGM(thumb));
    }
}

public class DC20 {
    public int LastResponse { get; private set; }
    public bool LastResponseCorrect {get; private set;}

    private StatusData statusDataInternal;

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
    private void Acknowledge(int expected = 0xD1) {
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
        Acknowledge();
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

    public Thumbnail GetThumbnail(byte index) {
        Write(0x56, 0x00, 0x00, (byte)index, 0x00, 0x00, 0x00, 0x1A);
        Acknowledge();
        byte[] thumbnail = new byte[5120];
        for (int i = 0; i < 5; i++) {
            var (correct, bytes) = ReadWithChecksum(1024);
            WriteAck(0xD2);
            bytes.CopyTo(thumbnail, i * 1024);
        }
        Acknowledge(0x00);
        Thumbnail thm = new Thumbnail();
        thm.GrayscaleData = new byte[4800];
        Array.Copy(thumbnail, thm.GrayscaleData, 4800);
        return thm;
    }

    public StatusData? Status() {
        Write( 0x7F, 00, 00, 00, 00, 00, 00, 0x1A );
        Acknowledge();
        var (correct, bytes) = ReadWithChecksum(256);
        Debug($"[{string.Join(", ", bytes.Select((x, i) => $"{i+1}:{x:X2}"))}]");
        var result = StatusData.From(bytes);
        statusDataInternal = result;
        WriteAck(0xD2);
        Acknowledge(0x00);

        return correct ? result : null;
    }

    public struct Thumbnail {
        public const int Width = 80;
        public const int Height = 60;
        public byte[] GrayscaleData;
    }

    public struct StatusData {
        public byte Model;
        public byte PicturesTaken;
        public byte PicturesRemaining;
        public byte Resolution;
        public byte Battery;

        public static StatusData From(byte[] bytes) {
            StatusData s = new StatusData();
            s.Model = bytes[1];
            s.PicturesTaken = bytes[9];
            s.PicturesRemaining = bytes[11];
            s.Resolution = bytes[23];
            s.Battery = bytes[29];
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
                sb.Append($"{thumbnail.GrayscaleData[i]} ");
                i++;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}