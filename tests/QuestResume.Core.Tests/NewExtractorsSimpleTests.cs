using System.Text;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

/// <summary>
/// Testes reais para os novos extratores cujo conteúdo de entrada pode ser construído
/// diretamente em texto/binário simples: .reg (PlainText), .dxf/.dwg, .fb2, .torrent, .lnk,
/// .psd, .chm/.djvu, .mobi, .apk, .pst.
/// </summary>
public class NewExtractorsSimpleTests
{
    private static string TempFile(string extension)
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");

    // ---------- .reg (item 1) ----------

    [Fact]
    public void PlainTextExtractor_SupportsReg()
    {
        var extractor = new PlainTextExtractor();
        Assert.Contains(".reg", extractor.SupportedExtensions);
    }

    [Fact]
    public async Task PlainTextExtractor_ReadsRegContent()
    {
        var path = TempFile(".reg");
        try
        {
            await File.WriteAllTextAsync(path,
                "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Software\\Teste]\r\n\"Chave\"=\"Valor\"");
            var doc = await new PlainTextExtractor().ExtractAsync(path);
            Assert.Contains("HKEY_CURRENT_USER", doc.Text);
            Assert.Contains("Valor", doc.Text);
        }
        finally { File.Delete(path); }
    }

    // ---------- .dxf / .dwg (item 5) ----------

    [Fact]
    public async Task DwgDxfExtractor_ExtractsTextEntitiesFromDxf()
    {
        var path = TempFile(".dxf");
        try
        {
            // Entidade TEXT com conteúdo no grupo de código 1.
            var dxf = "0\nSECTION\n2\nENTITIES\n0\nTEXT\n8\n0\n1\nOla Mundo CAD\n0\nMTEXT\n1\nSegundo texto\n0\nENDSEC\n0\nEOF\n";
            await File.WriteAllTextAsync(path, dxf);
            var doc = await new DwgDxfExtractor().ExtractAsync(path);
            Assert.Contains("Ola Mundo CAD", doc.Text);
            Assert.Contains("Segundo texto", doc.Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DwgDxfExtractor_DwgReportsLimitedSupportWarning()
    {
        var path = TempFile(".dwg");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("AC1027extra"));
            var doc = await new DwgDxfExtractor().ExtractAsync(path);
            Assert.True(doc.Metadata.ContainsKey("warning"));
            Assert.Contains("Suporte limitado", doc.Metadata["warning"]);
        }
        finally { File.Delete(path); }
    }

    // ---------- .fb2 (item 7) ----------

    [Fact]
    public async Task Fb2Extractor_ExtractsTitleAndBody()
    {
        var path = TempFile(".fb2");
        try
        {
            var fb2 = """
                <?xml version="1.0" encoding="UTF-8"?>
                <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
                  <description>
                    <title-info>
                      <book-title>O Livro de Teste</book-title>
                      <author><first-name>Ana</first-name><last-name>Silva</last-name></author>
                    </title-info>
                  </description>
                  <body>
                    <section>
                      <p>Primeiro paragrafo do corpo.</p>
                      <p>Segundo paragrafo.</p>
                    </section>
                  </body>
                </FictionBook>
                """;
            await File.WriteAllTextAsync(path, fb2);
            var doc = await new Fb2Extractor().ExtractAsync(path);
            Assert.Contains("O Livro de Teste", doc.Text);
            Assert.Contains("Primeiro paragrafo", doc.Text);
        }
        finally { File.Delete(path); }
    }

    // ---------- .torrent (item 3) ----------

    [Fact]
    public async Task TorrentExtractor_ParsesNameSizeAndTracker()
    {
        var path = TempFile(".torrent");
        try
        {
            // Bencode: d 8:announce 20:http://tracker.test/ 4:info d 6:length i12345e 4:name 9:meu_video e e
            var bencode = "d8:announce20:http://tracker.test/4:infod6:lengthi12345e4:name9:meu_videoee";
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes(bencode));
            var doc = await new TorrentExtractor().ExtractAsync(path);
            Assert.Contains("meu_video", doc.Text);
            Assert.Contains("12345", doc.Text);
            Assert.Contains("http://tracker.test/", doc.Text);
        }
        finally { File.Delete(path); }
    }

    // ---------- .lnk (item 2) ----------

    [Fact]
    public async Task LnkExtractor_ExtractsTargetPath()
    {
        var path = TempFile(".lnk");
        try
        {
            await File.WriteAllBytesAsync(path, BuildLnk(@"C:\Windows\notepad.exe"));
            var doc = await new LnkExtractor().ExtractAsync(path);
            Assert.Contains("notepad.exe", doc.Text);
            Assert.Contains("Atalho para:", doc.Text);
        }
        finally { File.Delete(path); }
    }

    private static byte[] BuildLnk(string target)
    {
        var targetBytes = Encoding.ASCII.GetBytes(target);
        var buffer = new List<byte>();

        // ShellLinkHeader: HeaderSize = 0x4C.
        buffer.AddRange(BitConverter.GetBytes(0x0000004Cu));
        // LinkCLSID.
        buffer.AddRange(new byte[]
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
        });
        // LinkFlags: HasLinkInfo (0x2) only.
        buffer.AddRange(BitConverter.GetBytes(0x00000002u));
        // Restante do header ate 76 bytes.
        while (buffer.Count < 76) buffer.Add(0);

        // LinkInfo (comeca em 76, sem IDList).
        const int headerSize = 28;
        var localBasePathOffset = headerSize;
        var linkInfoSize = headerSize + targetBytes.Length + 1;

        buffer.AddRange(BitConverter.GetBytes((uint)linkInfoSize));       // 0: LinkInfoSize
        buffer.AddRange(BitConverter.GetBytes((uint)headerSize));         // 4: LinkInfoHeaderSize
        buffer.AddRange(BitConverter.GetBytes(0x00000001u));             // 8: LinkInfoFlags (VolumeIDAndLocalBasePath)
        buffer.AddRange(BitConverter.GetBytes(0x00000000u));             // 12: VolumeIDOffset
        buffer.AddRange(BitConverter.GetBytes((uint)localBasePathOffset)); // 16: LocalBasePathOffset
        buffer.AddRange(BitConverter.GetBytes(0x00000000u));             // 20: CommonNetworkRelativeLinkOffset
        buffer.AddRange(BitConverter.GetBytes(0x00000000u));             // 24: CommonPathSuffixOffset
        buffer.AddRange(targetBytes);                                     // 28: LocalBasePath
        buffer.Add(0);                                                    // terminador nulo

        return buffer.ToArray();
    }

    // ---------- .psd (item 4) ----------

    [Fact]
    public async Task PsdExtractor_ExtractsDimensionsAndColorMode()
    {
        var path = TempFile(".psd");
        try
        {
            await File.WriteAllBytesAsync(path, BuildPsdHeader(width: 800, height: 600, channels: 3, depth: 8, colorMode: 3));
            var doc = await new PsdExtractor().ExtractAsync(path);
            Assert.Contains("800x600", doc.Text);
            Assert.Contains("RGB", doc.Text);
            Assert.Equal("800", doc.Metadata["width"]);
            Assert.Equal("600", doc.Metadata["height"]);
        }
        finally { File.Delete(path); }
    }

    private static byte[] BuildPsdHeader(uint width, uint height, ushort channels, ushort depth, ushort colorMode)
    {
        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("8BPS"));      // signature
        buffer.AddRange(Be16(1));                              // version
        buffer.AddRange(new byte[6]);                          // reserved
        buffer.AddRange(Be16(channels));
        buffer.AddRange(Be32(height));
        buffer.AddRange(Be32(width));
        buffer.AddRange(Be16(depth));
        buffer.AddRange(Be16(colorMode));
        // Color Mode Data (0), Image Resources (0), Layer/Mask (0) length sections.
        buffer.AddRange(Be32(0));
        buffer.AddRange(Be32(0));
        buffer.AddRange(Be32(0));
        return buffer.ToArray();
    }

    private static byte[] Be16(ushort v) => new[] { (byte)(v >> 8), (byte)(v & 0xFF) };
    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)(v & 0xFF) };

    // ---------- .chm / .djvu (item 14) ----------

    [Fact]
    public async Task ChmDjvuExtractor_ReportsLimitedSupport()
    {
        var path = TempFile(".chm");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("ITSF----conteudo"));
            var doc = await new ChmDjvuMetadataExtractor().ExtractAsync(path);
            Assert.Equal(string.Empty, doc.Text);
            Assert.True(doc.Metadata.ContainsKey("warning"));
            Assert.Contains("Suporte limitado", doc.Metadata["warning"]);
            Assert.True(doc.Metadata.ContainsKey("sizeBytes"));
        }
        finally { File.Delete(path); }
    }

    // ---------- .mobi (item 12) ----------

    [Fact]
    public async Task MobiExtractor_ExtractsUncompressedText()
    {
        var path = TempFile(".mobi");
        try
        {
            await File.WriteAllBytesAsync(path, BuildUncompressedMobi("Hello Mobi World"));
            var doc = await new MobiExtractor().ExtractAsync(path);
            Assert.Contains("Hello Mobi World", doc.Text);
        }
        finally { File.Delete(path); }
    }

    private static byte[] BuildUncompressedMobi(string text)
    {
        var textBytes = Encoding.ASCII.GetBytes(text);
        const int record0Offset = 94;   // 78 header + 16 record-info table (2 entries)
        const int record1Offset = 110;  // 94 + 16-byte palmdoc header

        var buffer = new byte[record1Offset + textBytes.Length];

        // recordCount = 2 em BE no offset 76.
        buffer[76] = 0x00;
        buffer[77] = 0x02;

        // record info table (offset 78): entry0, entry1 (cada 8 bytes: 4 offset + 4 attrib).
        WriteBe32(buffer, 78, record0Offset);
        WriteBe32(buffer, 86, record1Offset);

        // record0 = cabecalho PalmDOC (16 bytes). compression=1 em [0..1], textRecordCount=1 em [8..9].
        buffer[record0Offset + 0] = 0x00;
        buffer[record0Offset + 1] = 0x01;
        buffer[record0Offset + 8] = 0x00;
        buffer[record0Offset + 9] = 0x01;

        // record1 = texto.
        Array.Copy(textBytes, 0, buffer, record1Offset, textBytes.Length);

        return buffer;
    }

    private static void WriteBe32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    // ---------- .pst (item 13) - caminho de degradacao graciosa ----------

    [Fact]
    public async Task PstOstExtractor_InvalidFile_ReturnsWarningWithoutThrowing()
    {
        var path = TempFile(".pst");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("nao e um arquivo pst valido"));
            var doc = await new PstOstExtractor().ExtractAsync(path);
            Assert.True(doc.Metadata.ContainsKey("warning"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PstOstExtractor_SupportsPstAndOst()
    {
        var extractor = new PstOstExtractor();
        Assert.Contains(".pst", extractor.SupportedExtensions);
        Assert.Contains(".ost", extractor.SupportedExtensions);
    }
}
