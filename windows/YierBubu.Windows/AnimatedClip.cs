using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace YierBubu.Windows;

/// <summary>A completely decoded, immutable animation. Durations are in seconds.</summary>
public sealed class AnimatedClip
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<BitmapSource> Frames { get; }
    public IReadOnlyList<double> FrameDurations { get; }
    public double Duration { get; }
    public System.Windows.Rect ContentBounds { get; }
    public int PixelWidth => Frames[0].PixelWidth;
    public int PixelHeight => Frames[0].PixelHeight;
    public long EstimatedBytes => (long)PixelWidth * PixelHeight * 4 * Frames.Count;
    private readonly double[] frameEnds;

    internal AnimatedClip(AnimationEntry entry, BitmapSource[] frames, double[] durations,System.Windows.Rect contentBounds)
    {
        Id = entry.Id; Name = entry.Name; Description = entry.Description;
        ContentBounds=contentBounds;
        Frames = new ReadOnlyCollection<BitmapSource>(frames);
        FrameDurations = new ReadOnlyCollection<double>(durations);
        frameEnds = new double[frames.Length];
        double end = 0;
        for (int i = 0; i < frames.Length; i++) { end += durations[i]; frameEnds[i] = end; }
        Duration = end;
    }

    public int FrameIndexAt(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0 || Frames.Count == 1) return 0;
        double time = elapsed % Duration;
        int low = 0, high = frameEnds.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (time < frameEnds[mid]) high = mid; else low = mid + 1;
        }
        return low;
    }
    public BitmapSource FrameAt(double elapsed) => Frames[FrameIndexAt(elapsed)];

    internal static AnimatedClip Decode(AnimationEntry entry, string path, long maximumBytes)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("动画文件过大，请选择小于 32 MB 的动画。");
        if (!string.IsNullOrWhiteSpace(entry.Sha256))
        {
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("动画文件校验失败，请恢复完整的原始动画文件。");
            stream.Position = 0;
        }
        if(string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))return DecodeAtlas(entry,stream,maximumBytes);
        using var gif = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        if (gif.RawFormat.Guid != ImageFormat.Gif.Guid) throw new InvalidDataException("动画格式不受支持，请使用 GIF。");
        int count = gif.GetFrameCount(FrameDimension.Time);
        if (count < 1 || count > 300 || gif.Width < 1 || gif.Height < 1 || gif.Width > 4096 || gif.Height > 4096)
            throw new InvalidDataException("动画尺寸或帧数超出支持范围。");

        // Preserve source detail up to 512 px; cap decoded memory rather than trusting
        // the compressed file size. This also bounds a six-animation preload.
        double scale = Math.Min(1, Math.Min(512.0 / gif.Width, 512.0 / gif.Height));
        scale = Math.Min(scale, Math.Sqrt(maximumBytes / ((double)count * gif.Width * gif.Height * 4)));
        int width = Math.Max(1, (int)Math.Floor(gif.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(gif.Height * scale));
        byte[]? delays = gif.PropertyIdList.Contains(0x5100) ? gif.GetPropertyItem(0x5100)?.Value : null;
        var frames = new BitmapSource[count];
        var durations = new double[count];
        int minX=width,minY=height,maxX=-1,maxY=-1;
        for (int index = 0; index < count; index++)
        {
            // GDI+ assembles each active GIF frame with its frame rectangle,
            // transparency and disposal metadata. Always select in source order.
            gif.SelectActiveFrame(FrameDimension.Time, index);
            using var canvas = new Bitmap(width, height, DrawingPixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(gif, new Rectangle(0, 0, width, height), 0, 0, gif.Width, gif.Height, GraphicsUnit.Pixel);
            }
            var data = canvas.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
            try
            {
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                for (int row = 0; row < height; row++)
                    Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * stride, stride);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[y*stride+x*4+3]>8)
                {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                frame.Freeze(); frames[index] = frame;
            }
            finally { canvas.UnlockBits(data); }
            uint hundredths = delays is not null && delays.Length >= (index + 1) * 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(delays.AsSpan(index * 4, 4)) : 0;
            // Keep authored timing, including short holds. Missing/zero delays use
            // the conventional 100 ms fallback; never manufacture interpolated frames.
            durations[index] = hundredths == 0 ? .1 : hundredths / 100.0;
        }
        var content=maxX<0?new System.Windows.Rect(0,0,width,height):new System.Windows.Rect(Math.Max(0,minX-1),Math.Max(0,minY-1),Math.Min(width,maxX+2)-Math.Max(0,minX-1),Math.Min(height,maxY+2)-Math.Max(0,minY-1));
        return new AnimatedClip(entry, frames, durations,content);
    }

    private static AnimatedClip DecodeAtlas(AnimationEntry entry,Stream stream,long maximumBytes)
    {
        int width=entry.FrameWidth,height=entry.FrameHeight,count=entry.FrameCount;
        if(width<=0||height<=0||count<2||count>300||entry.SpriteRow<0||(long)width*height*count*4>maximumBytes||
            entry.Delays==null||entry.Delays.Length!=count||entry.Delays.Any(d=>!double.IsFinite(d)||d<=0||d>10))
            throw new InvalidDataException("动画图集的帧尺寸或播放时长无效。");
        var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var atlas=decoder.Frames[0];
        if(atlas.PixelWidth>4096||atlas.PixelHeight>4096||(long)width*count>atlas.PixelWidth||((long)entry.SpriteRow+1)*height>atlas.PixelHeight)
            throw new InvalidDataException("动画图集的帧位置超出图片边界。");
        var source=new FormatConvertedBitmap(atlas,PixelFormats.Pbgra32,null,0);
        var frames=new BitmapSource[count];int stride=width*4,minX=width,minY=height,maxX=-1,maxY=-1;
        for(int index=0;index<count;index++)
        {
            byte[] pixels=new byte[stride*height];
            source.CopyPixels(new System.Windows.Int32Rect(index*width,entry.SpriteRow*height,width,height),pixels,stride,0);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[y*stride+x*4+3]>8)
            {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
            // Copy each frame so the small clip does not retain the entire atlas.
            var frame=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,stride);frame.Freeze();frames[index]=frame;
        }
        var content=maxX<0?new System.Windows.Rect(0,0,width,height):new System.Windows.Rect(Math.Max(0,minX-1),Math.Max(0,minY-1),Math.Min(width,maxX+2)-Math.Max(0,minX-1),Math.Min(height,maxY+2)-Math.Max(0,minY-1));
        return new AnimatedClip(entry,frames,entry.Delays.ToArray(),content);
    }
}
