#pragma warning disable CS8602, CS8604
using System.Runtime.InteropServices;
namespace Floe.Core.Interop;
public static class ScreenCapture
{
 private const uint SrcCopy=0x00CC0020; private const int ColorOnColor=3;
 [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public int Size,Width,Height; public short Planes,BitCount; public int Compression,SizeImage,XPelsPerMeter,YPelsPerMeter,ClrUsed,ClrImportant; }
 [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h); [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc); [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc); [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc); [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfoHeader h,uint u,out IntPtr bits,IntPtr s,uint o); [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr o); [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o); [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d,int x,int y,int w,int h,IntPtr s,int sx,int sy,uint r); [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr d,int x,int y,int w,int h,IntPtr s,int sx,int sy,int sw,int sh,uint r); [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr d,int m); [DllImport("gdi32.dll")] static extern bool GdiFlush();
 public sealed class Session:IDisposable { IntPtr mem,dib,bits,old,screen; byte[]? pixels,previous; int width,height; bool disposed;
  public byte[]? Capture(int x,int y,int w,int h,out int stride,out bool changed)
   => CaptureScaled(x,y,w,h,w,h,out stride,out changed);

  // Retain the DIB, the screen DC and comparison buffers between grabs, including scaled grabs.
  public byte[]? CaptureScaled(int x,int y,int sourceWidth,int sourceHeight,int w,int h,out int stride,out bool changed)
  {
   stride=w>0?checked(w*4):0; changed=false;
   ObjectDisposedException.ThrowIf(disposed,this);
   if(sourceWidth<=0||sourceHeight<=0||w<=0||h<=0)return null;
   Ensure(w,h);
   bool copied;
   if(w==sourceWidth&&h==sourceHeight)
    copied=BitBlt(mem,0,0,w,h,screen,x,y,SrcCopy);
   else
   {
    // The result is blurred right after, so COLORONCOLOR's faster box downsample is
    // indistinguishable from HALFTONE's error diffusion here.
    SetStretchBltMode(mem,ColorOnColor);
    copied=StretchBlt(mem,0,0,w,h,screen,x,y,sourceWidth,sourceHeight,SrcCopy);
   }
   if(!copied){ReleaseScreen();throw new InvalidOperationException("Screen copy failed");}
   if(!GdiFlush())throw new InvalidOperationException("GdiFlush failed");
   Marshal.Copy(bits,pixels!,0,pixels.Length);
   // GDI's unused alpha byte is undefined; make it opaque and compare in the same pass,
   // ignoring alpha so an undefined byte cannot masquerade as a changed frame.
   var current=pixels.AsSpan(); var prior=previous.AsSpan(); var differs=false;
   for(int i=0;i<current.Length;i+=4){current[i+3]=255;differs|=current[i]!=prior[i]||current[i+1]!=prior[i+1]||current[i+2]!=prior[i+2];}
   changed=differs;
   if(changed)current.CopyTo(prior);
   return pixels;
  }
  void Ensure(int w,int h){EnsureScreen();if(mem!=IntPtr.Zero&&w==width&&h==height)return;ReleaseSurface();mem=CreateCompatibleDC(screen);if(mem==IntPtr.Zero)throw new InvalidOperationException("CreateCompatibleDC failed");var bh=new BitmapInfoHeader{Size=Marshal.SizeOf<BitmapInfoHeader>(),Width=w,Height=-h,Planes=1,BitCount=32};dib=CreateDIBSection(screen,ref bh,0,out bits,IntPtr.Zero,0);if(dib==IntPtr.Zero||bits==IntPtr.Zero)throw new InvalidOperationException("CreateDIBSection failed");old=SelectObject(mem,dib);if(old==IntPtr.Zero)throw new InvalidOperationException("SelectObject failed");width=w;height=h;pixels=new byte[checked(w*h*4)];previous=new byte[pixels.Length];}
  // A screen DC is valid until the session closes, so hold one instead of asking per frame.
  void EnsureScreen(){if(screen!=IntPtr.Zero)return;screen=GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)throw new InvalidOperationException("GetDC failed");}
  void ReleaseScreen(){if(screen!=IntPtr.Zero){ReleaseDC(IntPtr.Zero,screen);screen=IntPtr.Zero;}}
  void ReleaseSurface(){if(mem!=IntPtr.Zero&&old!=IntPtr.Zero)SelectObject(mem,old);if(dib!=IntPtr.Zero)DeleteObject(dib);if(mem!=IntPtr.Zero)DeleteDC(mem);mem=dib=bits=old=IntPtr.Zero;pixels=previous=null;width=height=0;}
  void Release(){ReleaseSurface();ReleaseScreen();}
  public void Dispose(){if(!disposed){Release();disposed=true;GC.SuppressFinalize(this);}}
 }
 public static byte[]? Capture(int x,int y,int w,int h,out int stride){stride=w>0?checked(w*4):0;if(w<=0||h<=0)return null;using var s=new Session();return s.Capture(x,y,w,h,out stride,out _)?.ToArray();}
 public static byte[]? CaptureScaled(int x,int y,int sw,int sh,int w,int h,out int stride)
 {
  using var session=new Session();
  return session.CaptureScaled(x,y,sw,sh,w,h,out stride,out _);
 }
}
