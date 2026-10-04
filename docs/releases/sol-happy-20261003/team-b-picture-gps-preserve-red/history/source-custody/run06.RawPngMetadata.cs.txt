using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// Test-only independent persisted-byte oracle. TagLibSharp2.3 XmpTag typed GPS getters are unsupported.
// No owner exporter/model implementation is replaced. Coordinate equality uses exact decimal arithmetic.
internal sealed record RawPngMetadata(string[] Chunks, Dictionary<string, string> Gps, int XmpPackets)
{
    private const string ExifNamespace = "http://ns.adobe.com/exif/1.0/";
    internal static RawPngMetadata Read(string path) => Read(File.ReadAllBytes(path));
    internal static RawPngMetadata Read(byte[] bytes)
    {
        if (!bytes.AsSpan(0, Math.Min(bytes.Length, 8)).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }))
            throw new InvalidDataException("Invalid PNG signature.");
        var chunks = new List<string>(); var gps = new Dictionary<string, string>(StringComparer.Ordinal); var packets = 0;
        var position = 8; var ended = false;
        while (position < bytes.Length)
        {
            if (ended || bytes.Length - position < 12) throw new InvalidDataException("Invalid PNG chunk boundary.");
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4)));
            if (length > bytes.Length - position - 12) throw new InvalidDataException("Truncated PNG chunk.");
            var type = Encoding.ASCII.GetString(bytes, position + 4, 4); var payload = bytes.AsSpan(position + 8, length);
            var expected = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + length, 4));
            if (Crc(bytes.AsSpan(position + 4, length + 4)) != expected) throw new InvalidDataException("PNG CRC mismatch: " + type);
            chunks.Add(type);
            if (type == "iTXt")
            {
                var separator = payload.IndexOf((byte)0);
                if (separator < 0) throw new InvalidDataException("Invalid PNG iTXt keyword.");
                if (Encoding.ASCII.GetString(payload[..separator]) == "XML:com.adobe.xmp")
                {
                    var start = separator + 1;
                    if (payload.Length - start < 4 || payload[start] != 0 || payload[start + 1] != 0)
                        throw new InvalidDataException("Fixture oracle requires uncompressed XMP iTXt.");
                    start += 2;
                    for (var i = 0; i < 2; i++)
                    {
                        var end = payload[start..].IndexOf((byte)0);
                        if (end < 0) throw new InvalidDataException("Invalid PNG iTXt language fields.");
                        start += end + 1;
                    }
                    using var text = new StringReader(new UTF8Encoding(false, true).GetString(payload[start..]));
                    using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    var xml = XDocument.Load(reader); packets++;
                    foreach (var element in xml.Descendants())
                    {
                        foreach (var attribute in element.Attributes()) Add(attribute.Name, attribute.Value);
                        if (!element.HasElements) Add(element.Name, element.Value);
                    }
                }
            }
            if (type == "IEND") { if (length != 0) throw new InvalidDataException("Invalid PNG IEND."); ended = true; }
            position += length + 12;
        }
        if (!ended || chunks.FirstOrDefault() != "IHDR") throw new InvalidDataException("Incomplete PNG.");
        return new(chunks.ToArray(), gps, packets);
        void Add(XName name, string value)
        {
            if (name.NamespaceName != ExifNamespace || !name.LocalName.StartsWith("GPS", StringComparison.Ordinal)) return;
            if (!gps.TryAdd(name.LocalName, value)) throw new InvalidDataException("Duplicate raw GPS field: " + name.LocalName);
        }
    }
    internal (decimal Latitude, decimal Longitude, decimal Altitude) Coordinates()
    {
        var latitude = Coordinate(Gps["GPSLatitude"], "NS", 90); var longitude = Coordinate(Gps["GPSLongitude"], "EW", 180);
        var ratio = Gps["GPSAltitude"].Split('/');
        if (ratio.Length != 2) throw new InvalidDataException("Invalid raw GPS altitude rational.");
        var altitude = Number(ratio[0]) / Number(ratio[1]);
        var reference = Gps["GPSAltitudeRef"];
        if (reference is not "0" and not "1") throw new InvalidDataException("Invalid raw GPS altitude reference.");
        return (latitude, longitude, reference == "1" ? -altitude : altitude);
    }
    private static decimal Coordinate(string value, string directions, decimal maximum)
    {
        if (value.Length < 2 || !directions.Contains(value[^1])) throw new InvalidDataException("Invalid GPS direction.");
        var components = value[..^1].Split(',');
        if (components.Length != 2) throw new InvalidDataException("Fixture GPS requires degrees,decimal-minutes.");
        var degrees = Number(components[0]); var minutes = Number(components[1]);
        if (degrees < 0 || minutes < 0 || minutes >= 60) throw new InvalidDataException("Invalid raw GPS coordinate range.");
        var result = degrees + minutes / 60m;
        if (result > maximum) throw new InvalidDataException("Raw GPS coordinate exceeds range.");
        return value[^1] is 'S' or 'W' ? -result : result;
    }
    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }
}
