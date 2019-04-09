using UnityEngine;


namespace TinyYOLO
{

    //原YoloBoundingBox中的RectangleF（构建矩形框）无法在Unity中使用，更改为Unity中的Rect
    public class YoloBoundingBox
    {
        //均为YoloBoundingBox中的成员变量
        public string Label { get; set; }

        //此处的X,Y是左上角坐标,为方便下一步构建Rectangle。YOLO输出的x,y其实是BoundingBox中心点坐标
        public float X { get; set; }
        public float Y { get; set; }

        public float Height { get; set; }
        public float Width { get; set; }
        //是总体的置信度
        public float Confidence { get; set; }


        public Rect rect
        {
            get { return new Rect(X, Y, Width, Height); }          
        }


    }

}