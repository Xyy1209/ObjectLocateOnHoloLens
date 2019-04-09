using System.Collections.Generic;
using UnityEngine;
using TinyYOLO;
using HoloToolkit.Unity.InputModule;
using System;
using System.Linq;


#if UNITY_UWP && !UNITY_EDITOR
using Windows.Media;
using Windows.Storage;
using Windows.AI.MachineLearning.Preview;
#endif


public class SceneStartup : MonoBehaviour
{
    public static SceneStartup Instance;


    public GameObject Instruction;
    public TextMesh InstructionalText;
    string textToDisplay;
    bool textToDisplayChanged;
   
    //ToVideoFrame toVideoFrame = new ToVideoFrame();
    bool shouldUseGpu = false;

    //声明TinyYOLOModel的实例：tinyYoloModel
#if UNITY_UWP && !UNITY_EDITOR
    TinyYOLOModel tinyYoloModel;

    public  LearningModelPreview model = null;
    public  ImageVariableDescriptorPreview inputImageDescription;
    public  TensorVariableDescriptorPreview outputTensorDescription;
#endif


    private void Awake()
    {
        Instance = this;
        this.gameObject.AddComponent<YoloWinMLParser>();

        Instruction = GameObject.FindGameObjectWithTag("InstrcutionObject");
        InstructionalText = Instruction.GetComponent<TextMesh>();
    }


    // Use this for initializationenviro
    void Start ()
    {
        Instruction.transform.position = GazeManager.Instance.HitPosition;
        Instruction.transform.rotation = Camera.main.transform.rotation;
        InstructionalText = Instruction.GetComponent<TextMesh>();
        DisplayText("Loading Tiny YOLO Model. Please Walk Around to Scan the Environment...");

#if UNITY_UWP && !UNITY_EDITOR
        InitializeModelAsync();
#else
        Debug.Log("Does not work in player!");
#endif

    }




    public void DisplayText(string text)
    {
        textToDisplay = text;
        textToDisplayChanged = true;
    }





#if UNITY_UWP && !UNITY_EDITOR
    public async void InitializeModelAsync()
    {
        //若模型已加载，则可略过此步
        if (this.model != null)
            return;

        //ms-appx是指当前正运行应用的程序包。应该是Assets/StreamingAssets下的文件部署至Hololens后会在Data文件夹中
        StorageFile tinyYoloModelFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Data/StreamingAssets/TinyYOLO.onnx"));
        this.model = await LearningModelPreview.LoadModelFromStorageFileAsync(tinyYoloModelFile);
        //评估后回收内存
        this.model.InferencingOptions.ReclaimMemoryAfterEvaluation = true;
        this.model.InferencingOptions.PreferredDeviceKind =
            shouldUseGpu ? LearningModelDeviceKindPreview.LearningDeviceGpu : LearningModelDeviceKindPreview.LearningDeviceCpu;
            

        //模型的输入描述是Image，输出是Tensor
        var inputFeatures = this.model.Description.InputFeatures.ToArray();
        var outputFeatures = this.model.Description.OutputFeatures.ToArray();

        this.inputImageDescription = inputFeatures.FirstOrDefault(feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Image) as ImageVariableDescriptorPreview;
        this.outputTensorDescription = outputFeatures.FirstOrDefault(feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Tensor) as TensorVariableDescriptorPreview;
        
        if(this.model!=null)
        {
            DisplayText("Tiny YOLO Model Loaded. Please Walk Around to Scan the Environment... ");
            Debug.Log("Initialize Model Async Succeed! ");
        }
       
    }
#endif





    // Update is called once per frame
    void Update ()
    {
        if (textToDisplayChanged)
        {
            Instruction.transform.position = GazeManager.Instance.HitPosition+new Vector3(0.05f,0.05f,0.0f);
            Instruction.transform.rotation = Camera.main.transform.rotation;
            InstructionalText.text = textToDisplay;
            textToDisplayChanged = false;
        }	
	}




}
