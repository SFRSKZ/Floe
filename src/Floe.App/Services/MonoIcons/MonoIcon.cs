using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
public static class MonoIcon {
 public static string Convert(string source,string output,string stem,bool silhouette) {
  Directory.CreateDirectory(Path.Combine(output,"PNG"));Directory.CreateDirectory(Path.Combine(output,"SVG"));Directory.CreateDirectory(Path.Combine(output,"ICO"));
  const int n=256; bool[,] mask=new bool[n,n];int[] hist=new int[256];byte[,] lum=new byte[n,n];bool[,] valid=new bool[n,n];bool[,] white=new bool[n,n];int count=0,whites=0;long sum=0;
  using(var original=Image.FromFile(source))using(var bmp=new Bitmap(n,n,PixelFormat.Format32bppArgb)) {
   using(var g=Graphics.FromImage(bmp)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;double scale=Math.Min(1.0*n/original.Width,1.0*n/original.Height);int w=(int)(original.Width*scale),h=(int)(original.Height*scale);g.DrawImage(original,(n-w)/2,(n-h)/2,w,h);}
   for(int y=0;y<n;y++)for(int x=0;x<n;x++){var c=bmp.GetPixel(x,y);if(c.A<128)continue;valid[x,y]=true;white[x,y]=c.R>210&&c.G>210&&c.B>210;if(white[x,y])whites++;int v=(int)(.299*c.R+.587*c.G+.114*c.B);lum[x,y]=(byte)v;hist[v]++;sum+=v;count++;}
  }
  if(count==0)throw new Exception("Source has no visible pixels");
  double best=-1;long sumB=0;int weightB=0,threshold=127,min=255,max=0;
  for(int i=0;i<256;i++){if(hist[i]>0){min=Math.Min(min,i);max=Math.Max(max,i);}weightB+=hist[i];sumB+=(long)i*hist[i];if(weightB==0||weightB==count)continue;double mB=(double)sumB/weightB,mF=(double)(sum-sumB)/(count-weightB);double variance=(double)weightB*(count-weightB)*(mB-mF)*(mB-mF);if(variance>best){best=variance;threshold=i;}}
  // Remove the dominant boundary colour, preserving solid silhouettes and negative space.
  int edgeDark=0,edgeLight=0,dark=0,light=0;
  for(int y=0;y<n;y++)for(int x=0;x<n;x++)if(valid[x,y]){bool d=lum[x,y]<=threshold;if(d)dark++;else light++;bool edge=x==0||y==0||x==n-1||y==n-1||!valid[Math.Max(0,x-1),y]||!valid[Math.Min(n-1,x+1),y]||!valid[x,Math.Max(0,y-1)]||!valid[x,Math.Min(n-1,y+1)];if(edge){if(d)edgeDark++;else edgeLight++;}}
  bool keepDark=edgeLight>edgeDark;
  int vx0=n,vy0=n,vx1=0,vy1=0;for(int y=0;y<n;y++)for(int x=0;x<n;x++)if(valid[x,y]){vx0=Math.Min(vx0,x);vx1=Math.Max(vx1,x);vy0=Math.Min(vy0,y);vy1=Math.Max(vy1,y);}
  bool transparentShape=count<(vx1-vx0+1)*(vy1-vy0+1)*.88;
  bool cutWhite=whites>count*.002&&whites<count*.65;
  int visible=0,x0=n,y0=n,x1=0,y1=0;
  for(int y=0;y<n;y++)for(int x=0;x<n;x++){bool on=valid[x,y]&&(silhouette||max-min<24||(transparentShape?(!cutWhite||!white[x,y]):((lum[x,y]<=threshold)==keepDark)));mask[x,y]=on;if(on){visible++;x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}}
  if(visible==0)throw new Exception("Conversion produced an empty icon");
  var path=new StringBuilder();for(int y=0;y<n;y++){int x=0;while(x<n){if(!mask[x,y]){x++;continue;}int start=x;while(x<n&&mask[x,y])x++;path.Append("M").Append(start).Append(' ').Append(y).Append('h').Append(x-start).Append("v1H").Append(start).Append('z');}}
  // Padding is part of the asset; no frame, shading or opacity effect.
  int box=Math.Max(x1-x0+1,y1-y0+1);double scaleOut=216.0/box;double tx=128-(x0+x1+1)*.5*scaleOut,ty=128-(y0+y1+1)*.5*scaleOut;
  var inv=System.Globalization.CultureInfo.InvariantCulture;
  string svg="<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\"><path fill=\"#111316\" transform=\"translate("+tx.ToString(inv)+" "+ty.ToString(inv)+") scale("+scaleOut.ToString(inv)+")\" d=\""+path+"\"/></svg>";
  File.WriteAllText(Path.Combine(output,"SVG",stem+".svg"),svg,Encoding.UTF8);
  using(var shape=new Bitmap(n,n,PixelFormat.Format32bppArgb))using(var final=new Bitmap(512,512,PixelFormat.Format32bppArgb)){
   for(int y=0;y<n;y++)for(int x=0;x<n;x++)if(mask[x,y])shape.SetPixel(x,y,Color.FromArgb(255,17,19,22));
   using(var g=Graphics.FromImage(final)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(shape,(float)(tx*2),(float)(ty*2),(float)(n*scaleOut*2),(float)(n*scaleOut*2));}
   // Only alpha is antialiased. RGB remains the exact dock ink at every visible pixel.
   for(int y=0;y<512;y++)for(int x=0;x<512;x++){int a=final.GetPixel(x,y).A;final.SetPixel(x,y,Color.FromArgb(a,17,19,22));}
   final.Save(Path.Combine(output,"PNG",stem+".png"),ImageFormat.Png);
   int[] sizes={16,24,32,48,64,128,256};byte[][] payload=new byte[sizes.Length][];
   for(int i=0;i<sizes.Length;i++)using(var small=new Bitmap(sizes[i],sizes[i])){using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(final,0,0,sizes[i],sizes[i]);}using(var ms=new MemoryStream()){small.Save(ms,ImageFormat.Png);payload[i]=ms.ToArray();}}
   using(var bw=new BinaryWriter(File.Create(Path.Combine(output,"ICO",stem+".ico")))){bw.Write((short)0);bw.Write((short)1);bw.Write((short)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){bw.Write((byte)(sizes[i]==256?0:sizes[i]));bw.Write((byte)(sizes[i]==256?0:sizes[i]));bw.Write((byte)0);bw.Write((byte)0);bw.Write((short)1);bw.Write((short)32);bw.Write(payload[i].Length);bw.Write(offset);offset+=payload[i].Length;}foreach(var p in payload)bw.Write(p);}
  }
  return silhouette?"silhouette":"adaptive-cutout";
 }
}
