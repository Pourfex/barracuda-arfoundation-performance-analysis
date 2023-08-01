using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Rendering;

namespace Setec {
    public class BarracudaSample : MonoBehaviour {
        public NNModel onnxModel;

        // Used to compute input tensor image
        private ComputeBuffer _networkInputBuffer;
        private Model _model;
        private IWorker _worker;
        // Input image channel defined by neural network model.
        private int _onnxInputChannel;
        // Input image width defined by neural network model.
        private int _onnxInputWidth;
        // Input image height defined by neural network model.
        private int _onnxInputHeight;

        private Texture2D _onnxOutput;

        // Called one time when scene start
        private void Awake () {
            // Prepare neural network model.
            _model = ModelLoader.Load (onnxModel);
            _worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, _model);

            _onnxInputHeight = _model.inputs[0].shape[5];
            _onnxInputWidth = _model.inputs[0].shape[6];
            _onnxInputChannel = _model.inputs[0].shape[7];

            _networkInputBuffer = new ComputeBuffer (_onnxInputHeight * _onnxInputWidth * _onnxInputChannel, sizeof (float));

            _onnxOutput = new Texture2D (_onnxInputWidth, _onnxInputHeight, TextureFormat.RGBA32, false);
        }

        // Called when GameObject is disable (exit scene or exit play mode in editor)
        private void OnDisable () {
            _worker.Dispose ();
            _networkInputBuffer.Dispose ();
        }
        
        public Vector2Int GetInputImageSizeONNX () {
            Model model = ModelLoader.Load (onnxModel);
            return new Vector2Int (model.inputs[0].shape[6], model.inputs[0].shape[5]);
        }

        public async Task<BarracudaOutputData> ProcessImage (Texture image) {
            if (image.width != _onnxInputWidth) {
                Debug.LogError ($"Image input width doesn't match with ONNX input, Image width :{image.width}, ONNX Input width: {_onnxInputWidth}");
            }
            
            if (image.height != _onnxInputHeight) {
                Debug.LogError ($"Image input width doesn't match with ONNX input, Image width :{image.height}, ONNX Input width: {_onnxInputHeight}");
            }
            
            AsyncGPUReadbackRequest? result = null;
            AsyncGPUReadback.Request (image, 0, (AsyncGPUReadbackRequest asyncAction) => result = asyncAction);
            await UniTask.WaitUntil (() => result != null);

            Color32[] colors = result.Value.GetData<Color32> ().ToArray ();

            List<float> tmpDatas = new List<float> ();

            foreach (Color32 c in colors) {
                tmpDatas.Add (c.r / 255f);
                tmpDatas.Add (c.g / 255f);
                tmpDatas.Add (c.b / 255f);
            }

            float[] datas = tmpDatas.ToArray ();

            // Execute barracuda with datas
            Tensor inputTensor = new Tensor (1, _onnxInputHeight, _onnxInputWidth, _onnxInputChannel, datas);
            try {
                await _worker.StartManualSchedule (inputTensor).WithCancellation (this.GetCancellationTokenOnDestroy ());
            } catch (OperationCanceledException ex) {
                return default;
            } finally {
                inputTensor.Dispose ();
            }

            // Get output tensor
            Tensor outputTensor = _worker.PeekOutput ();
            // Get int array result from output tensor
            int[] results = outputTensor.AsInts ();
            outputTensor.Dispose ();
            
            return new BarracudaOutputData () {
                results = results,
                colorResults = null,
                outputTexture = null
            };
        }
    }
}