#if UNITY_UWP && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Windows.AI.MachineLearning.Preview;
using Windows.Media;
using Windows.Storage;

//TinyYolo模型的输入、输出及预测

namespace TinyYOLO
{
    //sealed类为密封类，不可被其他类继承，防止方法被重写
    public sealed class TinyYOLOModelInput
    {
        public VideoFrame image { get; set; }
    }


    public sealed class TinyYOLOModelOutput
    {
        //IList只是一个接口，其中的对表的操作方法需要自己去实现，仅仅是作为集合数据的承载体；而List<>是泛型类别，已经实现了IList中定义的那些方法，即已实现对表的操作方法。
        public IList<float> grid { get; set; }
        public TinyYOLOModelOutput()
        {
            this.grid = new List<float>();
        }
    }


    public sealed class TinyYOLOModel
    {
        private LearningModelPreview learningModel;


        //注：CreateTinyYOLOModel是静态方法，类名调用
        //async和await 结合，才是真正的异步方法。返回一个Task,返回类型是TinyYOLOModel类型。
        public static async Task<TinyYOLOModel> CreateTinyYOLOModel(StorageFile file)
        {
            //using System;
            LearningModelPreview learningModel = await LearningModelPreview.LoadModelFromStorageFileAsync(file);
            TinyYOLOModel model = new TinyYOLOModel();
            model.learningModel = learningModel;
            return model;

        }


        public async Task<TinyYOLOModelOutput> EvaluateAsync(TinyYOLOModelInput input)
        {
            TinyYOLOModelOutput output = new TinyYOLOModelOutput();
            LearningModelBindingPreview binding = new LearningModelBindingPreview(learningModel);
            binding.Bind("image", input.image);
            binding.Bind("grid", output.grid);
            LearningModelEvaluationResultPreview evalResult = await learningModel.EvaluateAsync(binding, string.Empty);
            return output;

        }
    }

    }


#endif
