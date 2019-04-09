using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity.InputModule;
using System.Linq;
using UnityEngine.XR.WSA.WebCam;
using System;
using TinyYOLO;
using UnityEngine.UI;


#if UNITY_UWP && !UNITY_EDITOR
using Windows.AI.MachineLearning.Preview;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage.Pickers;
using Windows.Storage;
#endif



public class ToVideoFrame : MonoBehaviour, IInputClickHandler
{

    public const int HoloCamWidth = 2048;
    public const int HoloCamHeight = 1052;

    public static ToVideoFrame Instance;

    int imageConvertCount = 0;

    PhotoCapture photoCaptureObj = null;
    Resolution cameraResolution;
    bool capturingPhoto=false;
    public bool evaluating = false;
    int objectsCount;

    Matrix4x4 cameraToWorldMatrix;
    Matrix4x4 projectionMatrix;
    Matrix4x4 worldToCameraMatrix;
    
    //使用图像中心像素点做测试(现由Box的中点)
    //public Vector2 pixelPosition;
    
    //ImageBufferList存储图像的字节列表
    List<byte> imageBufferList;
    byte[] imageBufferArray=null;

    //List一定要记得new！否则会NullObjectReference！
    List<RayPoints> rays = new List<RayPoints>();

    public GameObject labelTextHolder;
    Vector3 BoundingBoxPosition;
    bool drawingBoxSucceed = false;

    //此处boxes是指NonMaxSuppress之前的包围盒
    IList<YoloBoundingBox> boxes = new List<YoloBoundingBox>();
    YoloWinMLParser parser = new YoloWinMLParser();

    public Font labelFont;

#if UNITY_UWP && !UNITY_EDITOR
    public VideoFrame inputImage=null;
#endif


    void Awake()
    {
        //单例
        Instance = this;
        this.gameObject.AddComponent<SceneStartup>();
    }


    // Use this for initialization
    void Start()
    {
        UnityEngine.Debug.Log("WebCam Mode is: " + WebCam.Mode);
        cameraResolution = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).First();
        UnityEngine.Debug.Log("The Resolution of Camera: " + cameraResolution);     
    }




    public void OnInputClicked(InputClickedEventData eventData)
    {
        if(!capturingPhoto)
        {
            PhotoCapture.CreateAsync(false, delegate (PhotoCapture captureObject)
            {
                photoCaptureObj = captureObject;

                CameraParameters cameraParameters = new CameraParameters();
                cameraParameters.hologramOpacity = 0.0f;
                cameraParameters.cameraResolutionWidth = cameraResolution.width;
                cameraParameters.cameraResolutionHeight = cameraResolution.height;
                cameraParameters.pixelFormat = CapturePixelFormat.BGRA32;

               
                //开始拍照时,capturingPhoto为真，capturingSucceed为假
                photoCaptureObj.StartPhotoModeAsync(cameraParameters, delegate (PhotoCapture.PhotoCaptureResult result)
                {
                    photoCaptureObj.TakePhotoAsync(OnCapturedPhotoToMemoryAsync);
                    capturingPhoto = true;
                    evaluating = false;
                });
               
            });

            UnityEngine.Debug.Log("Photo Capture CreateAsync Succeed!");
        }
       
    }





     void OnCapturedPhotoToMemoryAsync(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        if(result.success)
        {
            //将photoCaptureFrame转为List<byte>,再转为byte[].
            imageBufferList = new List<byte>();
            photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBufferList);
            //将拍摄内容保存到imageBufferArray中
            imageBufferArray = imageBufferList.ToArray();

            photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
            worldToCameraMatrix = cameraToWorldMatrix.inverse;
            photoCaptureFrame.TryGetProjectionMatrix(out projectionMatrix);

            UnityEngine.Debug.LogFormat(@"The value of cameraToWorld Matrix: {0}{1}{2}{3} ", cameraToWorldMatrix.GetRow(0), cameraToWorldMatrix.GetRow(1), cameraToWorldMatrix.GetRow(2), cameraToWorldMatrix.GetRow(3));

            UnityEngine.Debug.Log("Captured Photo To Memory Succeed! ");

        }    

        photoCaptureObj.StopPhotoModeAsync(OnStoppedPhotoMode);
    }

   


    //在拍照结束时,capturingPhoto为假，capturingSucceed为真
    void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        photoCaptureObj.Dispose();
        photoCaptureObj = null;

        capturingPhoto = false;
        UnityEngine.Debug.Log("Stopped Photo Mode Succeed!");
    }



#if UNITY_UWP && !UNITY_EDITOR 
    //若成功拍摄了照片，则要将其转化为模型的输入类型
     void FrameConvert2VideoFrame()
    {

        //Bgra8和Bgra32像素格式应该都是各channel占8Bits，一样的，待验证。
        //将HoloLens拍摄获得的PhotoCaptureFrame=》字节数组=》IBuffer=》SoftwareBitmap=》VideoFrame，作为模型的输入。
        //待验证经类型转化保存而得的VideoFrame是否正确
        IBuffer imageIBuffer = imageBufferArray.AsBuffer();
        SoftwareBitmap softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, cameraResolution.width, cameraResolution.height, BitmapAlphaMode.Premultiplied);
        softwareBitmap.CopyFromBuffer(imageIBuffer);
        //TinyYoloModel的输入是inputImage
        inputImage = VideoFrame.CreateWithSoftwareBitmap(softwareBitmap);

        //生成模型输入后，imageBufferArray要重新置空，以进行下次拍摄成功判断
        imageBufferArray = null;
        imageConvertCount++;
        UnityEngine.Debug.Log("PhotoCaptureFrame Convert to VideoFrame Succeed! " + imageConvertCount);

        //获取都输入图片后开始预测
        EvaluateVideoFrame(inputImage);
}
#endif






#if UNITY_UWP && !UNITY_EDITOR
    private async void EvaluateVideoFrame(VideoFrame inputImage)
    {
        evaluating = true;

        UnityEngine.Debug.Log("Start Evaluate VideoFrame! ");
        SceneStartup.Instance.DisplayText("EvaluateVideoFrame Starting...");
        this.gameObject.GetComponentInChildren<TextMesh>().color = Color.red;
        //binding实现Dictionary接口
        var binding = new LearningModelBindingPreview(SceneStartup.Instance.model as LearningModelPreview);

        //RS4的WinML需要预先分配空间给输出,输出总大小共211125。相当于先占了空间而已
        var outputArray = new List<float>();
        outputArray.AddRange(new float[21125]);

        binding.Bind(SceneStartup.Instance.inputImageDescription.Name, inputImage);
        binding.Bind(SceneStartup.Instance.outputTensorDescription.Name, outputArray);

        //绑定好输入图片后也要置空
        inputImage = null;

        //模型评估
        //LearningModelEvaluationResultPreview下的Outputs变量包含模型的输出特征和probabilities
        LearningModelEvaluationResultPreview results = await SceneStartup.Instance.model.EvaluateAsync(binding, "TinyYOLO");
        List<float> resultProbabilities = results.Outputs[SceneStartup.Instance.outputTensorDescription.Name] as List<float>;

        //调用YoloWinMLParser的ParseOutputs方法，得到x,y,w,h,c和rect
        this.boxes = this.parser.ParseOutputs(resultProbabilities.ToArray(), .6F);

        SceneStartup.Instance.DisplayText("Model Evaluation Completed! ");
        Debug.Log("Evaluate Video Frame Succeed! ");

        if (this.boxes.Count > 0)
        {
            FinalizeBoundingBox();
        }
        else
        {
            evaluating = false;
            SceneStartup.Instance.DisplayText("Boxes' Count is 0. Nothing Detected! ");
            Debug.Log("Boxes' Count is Zero. ");
        }

        /*
                catch (Exception ex)
                {
                    Debug.LogFormat($"Exception Occured! Error {0}", ex.Message);
                }
        */

    }

#endif


    private void FinalizeBoundingBox()
    {
        //filteredBox过滤掉overlapping和confidence较低的box
        var filteredBoxes = this.parser.NonMaxSuppress(this.boxes, 5, 0);
        
        if(filteredBoxes.Count>0)
        {
            foreach (YoloBoundingBox b in filteredBoxes)
                Debug.Log("The Object is " + b.Label + ". The Confidence is " + b.Confidence + ".");

            objectsCount = filteredBoxes.Count;
            UnityEngine.Debug.LogFormat("Actually There are {0} objects in the scene.", this.boxes.Count);

            foreach (YoloBoundingBox box in filteredBoxes)
            {
                //BoundingBox中心点坐标
                var centerX = (uint)Math.Max(box.X + (box.Width / 2), 0);
                var centerY = (uint)Math.Max(box.Y + (box.Height / 2), 0);
                var w = (uint)Math.Min(HoloCamWidth - centerX, box.Width);
                var h = (uint)Math.Min(HoloCamHeight - centerY, box.Height);

                Debug.Log("Center X: " + centerX + ", Center Y: " + centerY + ", Width: " + w + ", Height: " + h);
                var fixedCenterX = HoloCamWidth * centerX / 416;
                var fixedCenterY = HoloCamHeight * centerY / 416;
                //fixedWidth和fixedHeight这两个数值并不是以m为单位的，而是像素值。若要构建出完整的BoundingBox还要进一步操作
                var fixedWidth = HoloCamWidth * w / 416;
                var fixedHeight = HoloCamHeight * h / 416;

                Vector2 pixelPosition = new Vector2(fixedCenterX, fixedCenterY);
                ToVideoFrame.Instance.AddRay(box, pixelPosition, fixedWidth, fixedHeight, ToVideoFrame.Instance.cameraResolution.width, ToVideoFrame.Instance.cameraResolution.height, ToVideoFrame.Instance.projectionMatrix, ToVideoFrame.Instance.cameraToWorldMatrix);
            }
        }

        UnityEngine.Debug.Log("Finalize Bounding Box Finished! ");
        SceneStartup.Instance.DisplayText("There are " + objectsCount + " Objects in The Scene. ");
        evaluating = false;
    }








    //在UnityEditor中显示射线，Hololens上验证成功
    public void AddRay(YoloBoundingBox box,Vector2 pixelPosition, uint boxWidth, uint boxHeight, int ImageWidth, int ImageHeight, Matrix4x4 projectionMatrix, Matrix4x4 cameraToWorldMatrix)
    {
        //Image的xy两轴取值范围均为-1 to +1，需要将像素坐标转化为该范围内。
        Vector2 ImagePosZeroToOne = new Vector2(pixelPosition.x / ImageWidth, 1 - (pixelPosition.y / ImageHeight));
        Vector2 ImagePosProjected = ((ImagePosZeroToOne * 2.0f) - new Vector2(1.0f, 1.0f));
        Vector3 CameraSpacePos = UnProjectVector(projectionMatrix, new Vector3(ImagePosProjected.x, ImagePosProjected.y, 1));
        //worldSpaceRayPoint1就是cameraPosition，相当于Ray的起点，而worldSpaceRayPoint2相当于Ray的终点。
        Vector3 worldSpaceRayPoint1 = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
        Vector3 worldSpaceRayPoint2 = cameraToWorldMatrix.MultiplyPoint(CameraSpacePos);

        UnityEngine.Debug.Log("The Value of RayPosition: " + worldSpaceRayPoint2);

        //增加Ray并画出。
        rays.Add(new RayPoints()
        {
            origin = worldSpaceRayPoint1,
            direction = worldSpaceRayPoint2 - worldSpaceRayPoint1
        });

        UnityEngine.Debug.DrawRay(rays[0].origin, rays[0].direction, Color.red, 100);
        UnityEngine.Debug.Log("DrawRay Succeed !");

        //调用该方法，找到Ray与真实场景的碰撞点，即目标实物所在位置，并实例化标注框。
        PlaceBoundingBox(box,worldSpaceRayPoint1, worldSpaceRayPoint2, boxWidth, boxHeight);
    }





    public Vector3 UnProjectVector(Matrix4x4 projectionMatrix, Vector3 to)
    {
        Vector3 from = new Vector3(0, 0, 0);
        var axsX = projectionMatrix.GetColumn(0);
        var axsY = projectionMatrix.GetColumn(1);
        var axsZ = projectionMatrix.GetColumn(2);
        from.z = to.z / axsZ.z;
        from.y = (to.y - (from.z * axsY.z)) / axsY.y;
        from.x = (to.x - (from.z * axsX.z)) / axsX.x;
        return from;
    }





    private void PlaceBoundingBox(YoloBoundingBox box,Vector3 worldSpaceRayPoint1, Vector3 worldSpaceRayPoint2, uint boxWidth, uint boxHeight)
    {
        RaycastHit hitInfo;

        UnityEngine.Debug.Log("Place Bounding Box Method Start.");

        //注：在Unity中，LayerMask用到移位符号，1是开启，0是关闭。可以过滤射线碰撞事件，移位符号左侧为0的表示被射线忽略。
        //UserLayer31即为SpatialMapping层

        try
        {
            //检测与真实场景的碰撞
            if (Physics.Raycast(worldSpaceRayPoint1, worldSpaceRayPoint2 - worldSpaceRayPoint1, out hitInfo, 8.0f, 1 << 31))
            {
                UnityEngine.Debug.Log("Physics Raycast Succeed! ");

                BoundingBoxPosition = hitInfo.point;

                GameObject label = Instantiate<GameObject>(labelTextHolder) as GameObject;
                label.transform.position = BoundingBoxPosition;
                label.transform.rotation = Camera.main.transform.rotation;
                TextMesh labelText =label.GetComponent<TextMesh>();
                labelText.text = box.Label+" Confidence: "+box.Confidence.ToString("0.##");
                labelText.font = labelFont;
                labelText.color = Color.green;
                labelText.anchor = TextAnchor.MiddleCenter;
              
                UnityEngine.Debug.Log("The Position of Label Text is : " + label.transform.position + ". The Position of BOunding Box is :" + BoundingBoxPosition);
            }
            else
            {
                UnityEngine.Debug.Log("Physics Raycast Failed! ");
                objectsCount--;
            }

        }
        catch(Exception e)
        {
            Debug.LogFormat("Exception occour in the placing bounding box method : {0}",e.Message);
        }
      
        this.gameObject.GetComponentInChildren<TextMesh>().color = Color.green;
    }




    void Update()
    {
        if (imageBufferArray != null)
        {
#if UNITY_UWP && !UNITY_EDITOR
            FrameConvert2VideoFrame();
#elif UNITY_EDITOR
            Debug.Log("Does Not Work in Player !");
#endif
        }

    }



}



//一个Script中含有两个并列的类，而不是子类，则该类可被其他Script直接用该类类名调用
public class RayPoints
{
    //可再加上DetectedObjectData这一项，包含检测到的物体信息
    public Vector3 origin;
    public Vector3 direction;
}





