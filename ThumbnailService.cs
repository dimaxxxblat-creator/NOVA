using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Nova.App.Services;
/// <summary>Calls Windows Shell on a background STA thread; never launches a media player or modifies the source.</summary>
public sealed class ThumbnailService {
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width,Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr handle);
    int busy;
    public static bool IsLocalVideo(string path) {
        try {
            if(!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !File.Exists(path))return false;
            if(!new[]{".mp4",".webm",".gif"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))return false;
            string? root=Path.GetPathRoot(path); if(string.IsNullOrEmpty(root))return false;
            return new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        } catch { return false; }
    }
    public Task<byte[]> CreateAsync(string path) {
        if(!IsLocalVideo(path))throw new InvalidOperationException("Доступен только существующий локальный файл MP4, WebM или GIF.");
        if(Interlocked.CompareExchange(ref busy,1,0)!=0)throw new InvalidOperationException("Windows ещё создаёт предыдущую миниатюру. Попробуйте позже.");
        var completion=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=> {
            IShellItemImageFactory? factory=null; IntPtr h=IntPtr.Zero; byte[]? result=null; Exception? error=null;
            try {
                var id=typeof(IShellItemImageFactory).GUID;
                int hr=SHCreateItemFromParsingName(path,IntPtr.Zero,ref id,out factory); Marshal.ThrowExceptionForHR(hr);
                hr=factory.GetImage(new NativeSize { Width=640,Height=360 },0x08,out h); Marshal.ThrowExceptionForHR(hr);
                if(h==IntPtr.Zero)throw new InvalidOperationException("Windows не вернула миниатюру.");
                var image=Imaging.CreateBitmapSourceFromHBitmap(h,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream=new MemoryStream(); encoder.Save(stream); result=stream.ToArray();
            } catch(Exception ex) { error=ex; }
            finally { try { if(h!=IntPtr.Zero)DeleteObject(h); if(factory!=null)Marshal.ReleaseComObject(factory); } catch { } finally { Interlocked.Exchange(ref busy,0); } }
            if(error!=null)completion.TrySetException(error); else if(result!=null)completion.TrySetResult(result); else completion.TrySetException(new InvalidOperationException("Не удалось получить миниатюру."));
        }) { IsBackground=true, Name="NOVA thumbnail" };
        try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); } catch { Interlocked.Exchange(ref busy,0); throw; }
        return completion.Task;
    }
}
