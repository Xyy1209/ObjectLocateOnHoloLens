using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HoloToolkit.Unity.InputModule;
using System.Linq;
using UnityEngine.XR.WSA.WebCam;
using System;


public class FromPixelTo3D : MonoBehaviour, IInputClickHandler
{


    PhotoCapture photoCaptureObj = null;
    Texture2D targetTexture = null;
    Resolution cameraResolution;
    bool capturingPhoto = false;
    bool capturingSucceed=false;

    //将原始拍摄图片数据保存在此空字节列表中(待用)
    List<byte> imageBufferList;
    internal bool CaptureIsActive;

    Matrix4x4 cameraToWorldMatrix;
    Matrix4x4 projectionMatrix;
    Matrix4x4 worldToCameraMatrix;
    //使用图像中心像素点做测试
    public Vector2 pixelPosition;

    //List一定要记得new！否则会NullObjectReference！
    List<RayPoints> rays=new List<RayPoints>();

    Vector3 BoundingBoxPosition;




    // Use this for initialization
    void Start()
    {
        Debug.Log("WebCam Mode is: " + WebCam.Mode);


        cameraResolution = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).First();
        Debug.Log("The Resolution of Camera: " + cameraResolution);
        targetTexture = new Texture2D(cameraResolution.width, cameraResolution.height);

       

    }



    public void OnInputClicked(InputClickedEventData eventData)
    {
       
        //在整个场景中点击均有效
        //InputManager.Instance.AddGlobalListener(gameObject);

        PhotoCapture.CreateAsync(true, delegate (PhotoCapture captureObject)
        {
            photoCaptureObj = captureObject;

            CameraParameters cameraParameters = new CameraParameters();
            cameraParameters.hologramOpacity = 1.0f;
            cameraParameters.cameraResolutionWidth = cameraResolution.width;
            cameraParameters.cameraResolutionHeight = cameraResolution.height;
            cameraParameters.pixelFormat = CapturePixelFormat.BGRA32;
         
            photoCaptureObj.StartPhotoModeAsync(cameraParameters, delegate (PhotoCapture.PhotoCaptureResult result)
            {
                photoCaptureObj.TakePhotoAsync(OnCapturedPhotoToMemory);
            });
        });

        capturingPhoto = true;
        
        Debug.Log("Photo Capture CreateAsync Succeed!");
    }



    void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        if(result.success)
        {
            //imageBufferList待用
            imageBufferList = new List<byte>();
            photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBufferList);

            photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
            worldToCameraMatrix = cameraToWorldMatrix.inverse;
            photoCaptureFrame.TryGetProjectionMatrix(out projectionMatrix);

            Debug.LogFormat(@"The value of cameraToWorld Matrix: {0}{1}{2}{3} ", cameraToWorldMatrix.GetRow(0),cameraToWorldMatrix.GetRow(1),cameraToWorldMatrix.GetRow(2),cameraToWorldMatrix.GetRow(3));
           
            photoCaptureFrame.UploadImageDataToTexture(targetTexture);

            //创建相框，并赋予照片材质和Shader矩阵
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Renderer quadRenderer = quad.GetComponent<Renderer>() as Renderer;
            quadRenderer.material = new Material(Shader.Find("AR/HolographicImageBlend"));
            quadRenderer.sharedMaterial.SetTexture("_MainTex", targetTexture);
            quadRenderer.sharedMaterial.SetMatrix("_WorldToCameraMatrix", worldToCameraMatrix);
            quadRenderer.sharedMaterial.SetMatrix("_CameraProjectionMatrix", projectionMatrix);
            quadRenderer.sharedMaterial.SetFloat("_VignetteScale", 1.0f);


            //设置包含照片quad的位置和朝向。位置为拍摄时camera的位置，朝向用户
            //每个object由自己的局部坐标轴，确定一个物体的坐标轴朝向即可定向和定位该物体
            //lookRotation由坐标轴的朝向构建一个代表该朝向的四元数
            //GetColumn()方法中的参数是列号，从0开始

            //此段目的：将相框Quad放置在HoloLens上真实相机前边一点。
            //LookRotation代表一个特定的旋转四元数，即旋转方向
            Quaternion rotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));
            //cameraToWorldMatrix矩阵0，1，2列代表right/up/forward方向。最后一行和最后一列，除[3][3]值是1以外，其余均是0.
           
            // 将Quad放置在camera前一点。若要放在相机的位置，直接乘Vector3.zero
            Vector3 position = cameraToWorldMatrix.GetColumn(3) - cameraToWorldMatrix.GetColumn(2);

            quad.transform.parent = this.transform;
            quad.transform.position = position;
            quad.transform.rotation = rotation;
            Debug.Log("Quad's Position: " + quad.transform.position);

            capturingSucceed = true;
            Debug.Log("Capture Photo to Memory Succeed!");
        
        }

        photoCaptureObj.StopPhotoModeAsync(OnStoppedPhotoMode);

    }





    void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        photoCaptureObj.Dispose();
        photoCaptureObj = null;


        Debug.Log("Stopped Photo Mode Succeed!");
    }





    void Update()
    {
        //Debug.Log("The value of capturing succeed: " + capturingSucceed);
        if(capturingSucceed)
        {
            capturingSucceed = false;
            AddRay(pixelPosition, cameraResolution.width, cameraResolution.height, projectionMatrix, cameraToWorldMatrix);
            Debug.Log("DrawRay Succeed !");
        }
    }





    //在UnityEditor中显示射线，Hololens上待验证！
    public void AddRay(Vector2 pixelPosition, int ImageWidth, int ImageHeight, Matrix4x4 projectionMatrix, Matrix4x4 cameraToWorldMatrix)
    {
        //Image的xy两轴取值范围均为-1 to +1，需要将像素坐标转化为该范围内。
        Vector2 ImagePosZeroToOne = new Vector2(pixelPosition.x / ImageWidth, 1 - (pixelPosition.y / ImageHeight));
        Vector2 ImagePosProjected = ((ImagePosZeroToOne * 2.0f) - new Vector2(1.0f, 1.0f));
        Vector3 CameraSpacePos = UnProjectVector(projectionMatrix, new Vector3(ImagePosProjected.x, ImagePosProjected.y, 1));
        //worldSpaceRayPoint1就是cameraPosition，相当于Ray的起点，而worldSpaceRayPoint2相当于Ray的终点。
        Vector3 worldSpaceRayPoint1 = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
        Vector3 worldSpaceRayPoint2 = cameraToWorldMatrix.MultiplyPoint(CameraSpacePos);

        Debug.Log("The Value of RayPosition: " + worldSpaceRayPoint2);


        //增加Ray并画出。
        rays.Add(new RayPoints()
        {
            origin=worldSpaceRayPoint1,direction= worldSpaceRayPoint2 - worldSpaceRayPoint1
        });
        Debug.DrawRay(rays[0].origin, rays[0].direction, Color.red,100);



        //调用该方法，找到Ray与真实场景的碰撞点，即目标实物所在位置，并实例化标注框。
        PlaceBoundingBox(worldSpaceRayPoint1,worldSpaceRayPoint2);

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





    private void PlaceBoundingBox(Vector3 worldSpaceRayPoint1, Vector3 worldSpaceRayPoint2)
    {
        RaycastHit hitInfo;


        //注：在Unity中，LayerMask用到移位符号，1是开启，0是关闭。可以过滤射线碰撞事件，移位符号左侧为0的表示被射线忽略。
        //UserLayer31即为SpatialMapping层

        //与真实场景碰撞Failed!
        if (Physics.Raycast(worldSpaceRayPoint1, worldSpaceRayPoint2 - worldSpaceRayPoint1, out hitInfo, 5.0f, 1 << 31))
        {
            BoundingBoxPosition = hitInfo.point;

            //暂时先用一个Quad做测试
            GameObject BoundingBox;
            BoundingBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            BoundingBox.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
            BoundingBox.transform.position = BoundingBoxPosition;
            BoundingBox.GetComponent<Renderer>().material.color = Color.green;
            Debug.Log("---------------------------- The Position of Bounding Box: " + BoundingBox.transform.position);         
        }
        else
        {
            Debug.Log("Physics Raycast Failed! ");
        }
                 
    }



}



//为了运行ToVideoFrame代码，先将其注释掉
/*
public class RayPoints
{
    //可再加上DetectedObjectData这一项，包含检测到的物体信息
    public Vector3 origin;
    public Vector3 direction;
}
*/