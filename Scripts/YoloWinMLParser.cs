using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

namespace TinyYOLO
{
    //将416*416图片划分为13*13网格cell，每个网格大小为32*32。每个网格均产生20个类别预测结果和5个预测的BoundingBox，每个BoundingBox均有5个值（x,y,w,h,c）
    //输出Tensor为13*13*125

     class YoloWinMLParser : MonoBehaviour
    {
        
        public const int ROW_COUNT = 13;
        public const int COL_COUNT = 13;
        //每一个网格cell均有125（5*（20+5））个通道
        public const int CHANNEL_COUNT = 125;
        public const int BOXES_PER_CELL = 5;

        public const int BOX_INFO_FEATURE_COUNT = 5;
        public const int CLASS_COUNT = 20;
        public const int CELL_WIDTH = 32;
        public const int CELL_HEIGHT = 32;

        //channelStride指两个不同的相邻channel间的步幅,即从该channel到下一个channel间的步幅
        private int channelStride = ROW_COUNT * COL_COUNT;


        //通过K-Means得到的anchors,用来预测BoundingBox位置
        private float[] anchors = new float[] 
        {
            1.08F, 1.19F, 3.42F, 4.41F, 6.63F, 11.38F, 9.42F, 5.11F, 16.62F, 10.52F
        };


        private string[] labels = new string[]
        {
             "aeroplane", "bicycle", "bird", "boat", "bottle",
             "bus", "car", "cat", "chair", "cow",
             "diningtable", "dog", "horse", "motorbike", "person",
             "pottedplant", "sheep", "sofa", "train", "tvmonitor"
        };

        //函数中的形参threshold有默认值，表示如果在调用该函数时第二个参数未赋值，则使用默认值。
        public IList<YoloBoundingBox> ParseOutputs(float[] yoloModelOutputs,float threshold=.6f)
        {
            var boxes = new List<YoloBoundingBox>();
            var featuresPerBox = BOX_INFO_FEATURE_COUNT + CLASS_COUNT;
            var stride = featuresPerBox * BOXES_PER_CELL;

            //cy是从0~ROW_COUNT-1
            for (int cy = 0; cy < ROW_COUNT; cy++)
            {
                for (int cx = 0; cx < COL_COUNT; cx++)
                {
                    for (int b=0;b<BOXES_PER_CELL;b++)
                    {
                        //channel=b*featuresPerBox
                        var channel = b * (CLASS_COUNT + BOX_INFO_FEATURE_COUNT);

                        var tx = yoloModelOutputs[GetOffset(cx, cy, channel)];
                        var ty = yoloModelOutputs[GetOffset(cx, cy, channel + 1)];
                        var tw = yoloModelOutputs[GetOffset(cx, cy, channel+2)];
                        var th = yoloModelOutputs[GetOffset(cx, cy, channel + 3)];
                        var tc = yoloModelOutputs[GetOffset(cx, cy, channel + 4)];

                        //x,y是BoundingBox中心位置相当于当前cell的位置偏移量，width和height和图片大小有关。均被归一化到[0,1]
                        var x = ((float)cx + Sigmoid(tx)) * CELL_WIDTH;
                        var y = ((float)cy + Sigmoid(ty)) * CELL_HEIGHT;
                        var width = (float)Math.Exp(tw) * CELL_WIDTH * this.anchors[b * 2];
                        var height = (float)Math.Exp(th) * CELL_HEIGHT * this.anchors[b * 2 + 1];

                        //confidence计算方式为 P(Object)(0|1)*IOU ，包含两重信息：1.这个包围盒里有没有物体 2.这个包围盒预测的位置信息和Ground Truth相比有多准
                        //confidence也运行Sigmoid激活函数。
                        var confidence = Sigmoid(tc);

                        //若置信度小于阈值，则直接忽略
                        if (confidence < threshold)
                            continue;

                        var classes = new float[CLASS_COUNT];
                        var classOffset = channel + BOXES_PER_CELL;

                        for (int i = 0; i < CLASS_COUNT; i++)
                            classes[i] = yoloModelOutputs[GetOffset(cx, cy, i + classOffset)];


                        //=>表达式，相当于new了一个匿名类型键值对（Value,Index）。v代表概率值，ik表示类别索引
                        var result = Softmax(classes).Select((v, ik) => new { Value = v, Index = ik });

                        var topClass = result.OrderByDescending(r => r.Value).First().Index;
                        //topScore是由softMax后的数值*confidence得到,相当于这个BoundingBox中有物体，且类别和位置预测概率较大的才是TopScore。
                        var topScore = result.OrderByDescending(r => r.Value).First().Value * confidence;
                        //经softmax后的20个类别概率值和应为1
                        var testSum = result.Sum(r => r.Value);


                        if (topScore < threshold)
                            continue;


                        boxes.Add(new YoloBoundingBox
                        {
                            Label = labels[topClass],
                            X = x - width / 2,
                            Y = y - height / 2,
                            Width = width,
                            Height = height,
                            //YoLoBoundingBox中的Confidence是指总体的topScore
                            Confidence = topScore
                        });

                    }
                }
            }


            return boxes;

        }



        //按照confidence降序排序，去掉和之前的BoundingBox重叠面积较大的Box。最终的BoundingBox存储至results中
        public IList<YoloBoundingBox> NonMaxSuppress(IList<YoloBoundingBox> boxes,int limit,float threshold)
        {
            var activeCount = boxes.Count;
            var isActiveBox = new bool[boxes.Count];

            for (int i = 0; i < isActiveBox.Length; i++)
                isActiveBox[i] = true;

            //Select => new 相当于创建了一个匿名类型(sortedBoxes)，这个类中有两个变量（变量成员1是Box，是YoloBoundingBox类型；变量成员2是Index,是int）
            //第二行的b相当于new了一个此匿名类型的实例，它含成员变量Box
            var sortedBoxes = boxes.Select((b, i) => new { Box = b, Index = i })
                            .OrderByDescending(b => b.Box.Confidence).ToList();

            var results = new List<YoloBoundingBox>();


            for(int i=0 ; i<boxes.Count ; i++)
            {
                if(isActiveBox[i])
                {
                    var boxA = sortedBoxes[i].Box;
                    results.Add(boxA);

                    if (results.Count >= limit)
                        break;

                    for (int j=i+1;j<boxes.Count;j++)
                    {
                        if(isActiveBox[j])
                        {
                            var boxB = sortedBoxes[j].Box;

                            if(IntersectionOverUnion(boxA.rect,boxB.rect)>threshold)
                            {
                                isActiveBox[j] = false;
                                activeCount--;

                                if (activeCount <= 0)
                                    break;
                            }
                        }
                    }

                    if (activeCount <= 0)
                        break;
                }

            }

            return results;
        }





        private int GetOffset(int x, int y, int channel)
        {
            //YOLO输出Tensor为13*13*125，WinML将它转化为一维数组float[]了，故要转化一下，求偏移值
            return (channel * this.channelStride) + (y * COL_COUNT) + x;
        }




        //激活函数，增加非线性元素，使其可逼近任意函数
        //将变量映射到（0,1）之间。 Sigmoid公式: 1 / (1+exp(-z))
        private float Sigmoid(float value)
        {
            var k = (float)Math.Exp(value);

            return k / (1.0f + k);
        }




        //soft max公式：Exp(Vi-maxVi) / ( Sum(Exp(Vi) ) 。V中的每个元素减去最大值，求指数，得到一组值例如 V2[]。再求这组值的和Sum(V2)作为分母。最后用V2[]中的每一项除以分母，得到概率大小的相对值。
        //soft max返回浮点数数组，柔性最大值函数，并非绝对比较数值大小。数值大的有大几率取到，数值小的那一项也有小概率会被取到
        private float[] Softmax(float[] values)
        {
            var maxVal = values.Max();
            //返回一个double数组
            var exp = values.Select(v => Math.Exp(v - maxVal));
            var sumExp = exp.Sum();

            return exp.Select(v => (float)(v / sumExp)).ToArray();
        }



        //计算IOU
        private float IntersectionOverUnion(Rect a, Rect b)
        {
            var areaA = a.width * a.height;

            if (areaA <= 0)
                return 0;

            var areaB = b.width * b.height;

            if (areaB <= 0)
                return 0;

            var minX = Math.Max(a.xMin, b.xMin);
            var minY = Math.Max(a.yMin, b.yMin);
            var maxX = Math.Min(a.xMax, b.xMax);
            var maxY = Math.Min(a.yMax, b.yMax);

            var intersectionArea = Math.Max(maxY - minY, 0) * Math.Max(maxX - minX, 0);

            return intersectionArea / (areaA + areaB - intersectionArea);
        }




    }

}