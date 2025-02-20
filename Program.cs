using System.IO.Ports;
using System.Net;
using System.Runtime.InteropServices;

Console.WriteLine("Hello world!");


public class DC20Controller {
    public int LastResponse { get; private set; }
    public bool LastResponseCorrect {get; private set;}
    public enum BaudRate {
        _9600,
        _19200,
        _38400,
        _57600,
        _115200
    }

    SerialPort serial_port;

    public DC20Controller(string portName) {
        serial_port = new SerialPort(portName, baudRate: 9600, Parity.Even, dataBits: 8, StopBits.One);
    }
    ~DC20Controller() {
        serial_port.Close();
    }

    private void Write(params byte[] bytes) {
        serial_port.Write(bytes, 0, bytes.Length);
    }
    private void WriteAck(int acknowledgement = 0xD2) {
        serial_port.Write(new byte[1] {(byte)acknowledgement}, 0, 1);
    }
    private void Acknowledge(int expected = 0xD1) {
        LastResponse = serial_port.ReadByte();
        LastResponseCorrect = LastResponse == expected;
    }

    public bool Init(BaudRate newBaudRate) {
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

    private (bool, byte[]) ReadWithChecksum(int byte_count) {
        byte[] status = new byte[256];
        var bytes_read = serial_port.Read(status, 0, 256);
        var checksum = serial_port.ReadByte();
        var checksum_correct = status.Aggregate(0, (s, x) => s ^ x) == checksum;
        return (bytes_read == 256 && checksum_correct, status);
    }

    public bool Status() {
        Write( 0x7F, 00, 00, 00, 00, 00, 00, 0x1A );
        Acknowledge();
        var (correct, bytes) = ReadWithChecksum(256);
        var result = StatusData.From(bytes);
        WriteAck(0xD2);
        Acknowledge(0x00);

        return correct;
    }

    struct StatusData {
        byte Model;
        byte PicturesTaken;
        byte PicturesRemaining;
        byte Resolution;
        byte Battery;

        public static StatusData From(byte[] bytes) {
            StatusData s = new StatusData();
            s.Model = bytes[2];
            s.PicturesTaken = bytes[10];
            s.PicturesRemaining = bytes[12];
            s.Resolution = bytes[24];
            s.Battery = bytes[30];
            return s;
        }
    }
}