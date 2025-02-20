using System.IO.Ports;
using System.Net;

Console.WriteLine("Hello world!");


public class DC20Controller {
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
    public ~DC20Controller() {
        serial_port.Close();
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
        var init_string = new byte[]{
            0x41, 0x00,
            BaudA, BaudB,
            0x00, 0x00,
            0x00, 0x1A
        };
        serial_port.Write(init_string, 0, init_string.Length);
        var response = serial_port.ReadByte() == 0xD1;
        serial_port.BaudRate = newBaudRate switch
        {
            BaudRate._9600 => 9600,
            BaudRate._19200 => 19200,
            BaudRate._38400 => 38400,
            BaudRate._57600 => 57600,
            BaudRate._115200 => 115200,
            _ => throw new NotImplementedException(),
        };
        return response;
    }
}