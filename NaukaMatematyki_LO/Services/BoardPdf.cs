using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace NaukaMatematyki_LO;

internal static class BoardPdf
{
    public static void Write(string path, int pixelWidth, int pixelHeight, Action<Graphics, int, int> paintSlice)
    {
        const double pageWidth = 595;
        const double pageHeight = 842;
        const double margin = 36;
        var contentWidth = pageWidth - margin * 2;
        var contentHeight = pageHeight - margin * 2;
        var scale = contentWidth / Math.Max(1, pixelWidth);
        var slicePixels = Math.Max(1, (int)Math.Floor(contentHeight / scale));
        var pages = new List<byte[]>();
        var heights = new List<int>();

        for (var top = 0; top < pixelHeight; top += slicePixels)
        {
            var height = Math.Min(slicePixels, pixelHeight - top);
            using var bitmap = new Bitmap(pixelWidth, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                paintSlice(graphics, top, height);
            }
            pages.Add(EncodeJpeg(bitmap));
            heights.Add(height);
        }

        if (pages.Count == 0)
        {
            using var bitmap = new Bitmap(Math.Max(1, pixelWidth), 1, PixelFormat.Format24bppRgb);
            pages.Add(EncodeJpeg(bitmap));
            heights.Add(1);
        }

        WriteDocument(path, pixelWidth, pages, heights, pageWidth, pageHeight, margin, scale, contentHeight);
    }

    private static byte[] EncodeJpeg(Bitmap bitmap)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        using var stream = new MemoryStream();
        bitmap.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    private static void WriteDocument(
        string path,
        int pixelWidth,
        IReadOnlyList<byte[]> pages,
        IReadOnlyList<int> heights,
        double pageWidth,
        double pageHeight,
        double margin,
        double scale,
        double contentHeight)
    {
        var count = 2 + pages.Count * 3;
        var positions = new long[count + 1];
        using var output = File.Create(path);
        WriteAscii(output, "%PDF-1.4\n");

        Start(output, positions, 1);
        WriteAscii(output, "<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(index => $"{PageId(index)} 0 R"));
        Start(output, positions, 2);
        WriteAscii(output, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = PageId(index);
            var contentId = pageId + 1;
            var imageId = pageId + 2;
            var drawWidth = pixelWidth * scale;
            var drawHeight = Math.Min(contentHeight, heights[index] * scale);
            var imageY = pageHeight - margin - drawHeight;
            var content = string.Create(CultureInfo.InvariantCulture, $"q\n{drawWidth:0.###} 0 0 {drawHeight:0.###} {margin:0.###} {imageY:0.###} cm\n/Im0 Do\nQ\n");
            var contentBytes = Encoding.ASCII.GetBytes(content);

            Start(output, positions, pageId);
            WriteAscii(output, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth.ToString(CultureInfo.InvariantCulture)} {pageHeight.ToString(CultureInfo.InvariantCulture)}] /Contents {contentId} 0 R /Resources << /XObject << /Im0 {imageId} 0 R >> >> >>\nendobj\n");

            Start(output, positions, contentId);
            WriteAscii(output, $"<< /Length {contentBytes.Length} >>\nstream\n");
            output.Write(contentBytes);
            WriteAscii(output, "endstream\nendobj\n");

            Start(output, positions, imageId);
            WriteAscii(output, $"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {heights[index]} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {pages[index].Length} >>\nstream\n");
            output.Write(pages[index]);
            WriteAscii(output, "\nendstream\nendobj\n");
        }

        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {count + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= count; id++)
            WriteAscii(output, $"{positions[id]:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static int PageId(int index) => 3 + index * 3;

    private static void Start(Stream output, long[] positions, int id)
    {
        positions[id] = output.Position;
        WriteAscii(output, $"{id} 0 obj\n");
    }

    private static void WriteAscii(Stream output, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes);
    }
}
