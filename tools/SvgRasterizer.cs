using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Xml;
using Media = System.Windows.Media;

internal static class SvgRasterizer
{
    // Use the framework geometry parser at build time. The delivered app needs no SVG package or WPF dependency.
    internal static void Export(string source,string destination,int size)
    {
        var document=new XmlDocument {XmlResolver=null};
        using (var reader=XmlReader.Create(source,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))
            document.Load(reader);
        using (var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var graphics=Graphics.FromImage(bitmap))
        using (var pen=new Pen(Color.Black,2) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            graphics.Clear(Color.Transparent); graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size/24f,size/24f);
            foreach (XmlNode node in document.DocumentElement.ChildNodes)
            {
                var element=node as XmlElement; if (element==null) continue;
                if (element.LocalName=="circle")
                {
                    float radius=Number(element,"r");
                    graphics.DrawEllipse(pen,Number(element,"cx")-radius,Number(element,"cy")-radius,radius*2,radius*2);
                }
                else if (element.LocalName=="rect")
                {
                    float x=Number(element,"x"),y=Number(element,"y"),w=Number(element,"width"),h=Number(element,"height"),r=Number(element,"rx");
                    if (r==0) graphics.DrawRectangle(pen,x,y,w,h);
                    else using (var path=PSConverter.Theme.Rounded(new RectangleF(x,y,w,h),r)) graphics.DrawPath(pen,path);
                }
                else if (element.LocalName=="path")
                {
                    var geometry=Media.Geometry.Parse(element.GetAttribute("d")).GetFlattenedPathGeometry(0.01,Media.ToleranceType.Absolute);
                    using (var path=new GraphicsPath())
                    {
                        foreach (Media.PathFigure figure in geometry.Figures)
                        {
                            path.StartFigure(); var previous=Point(figure.StartPoint);
                            foreach (Media.PathSegment segment in figure.Segments)
                            {
                                var line=segment as Media.LineSegment;
                                var polyline=segment as Media.PolyLineSegment;
                                if (line!=null) { var end=Point(line.Point); path.AddLine(previous,end); previous=end; }
                                else if (polyline!=null) foreach (var point in polyline.Points)
                                { var end=Point(point); path.AddLine(previous,end); previous=end; }
                                else throw new InvalidDataException("The flattened SVG contains an unsupported segment.");
                            }
                            if (figure.IsClosed) path.CloseFigure();
                        }
                        graphics.DrawPath(pen,path);
                    }
                }
                else throw new InvalidDataException("Unsupported SVG element: "+element.LocalName);
            }
            bitmap.Save(destination,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    private static PointF Point(System.Windows.Point point) {return new PointF((float)point.X,(float)point.Y);}
    private static float Number(XmlElement element,string name)
    {string text=element.GetAttribute(name);return text.Length==0?0:float.Parse(text,CultureInfo.InvariantCulture);}
}
