using Setec;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlocInBloc
{
    public class InspectionSimulator : MonoBehaviour
    {
        public static int processingTimeInMillisecons = 0;

        public Camera camera;
        public ARCpuImage arCpuImage;
        public BarracudaSample barracudaSamplePrefab;
        public RawImage inputImage;
        public RawImage outputImageProcessed;

        private bool _isProcessingARCameraImage;
        private bool _isProcessingBCF;
        private BarracudaSample _barracudaSample;
        private Texture _outputProcessImage;

        private void Awake () {
            Vector2Int inputImageSizeOnnx = barracudaSamplePrefab.GetInputImageSizeONNX ();
            arCpuImage.SetCustomResolution (inputImageSizeOnnx);
            arCpuImage.useCustomResolution = true;
        }

        /// <summary>
        /// Called one time when scene start by Unity
        /// </summary>
        private void Start()
        {
            _barracudaSample = GameObject.Instantiate<BarracudaSample>(barracudaSamplePrefab);
        }

        /// <summary>
        /// Called each time Camera image is available by ARCpuImage in the scene
        /// </summary>
        public async void ProcessARCameraImage(Texture2D inputCameraImage)
        {
            // Don't call too many process image
            if (_isProcessingARCameraImage)
            {
                return;
            }

            inputImage.texture = inputCameraImage;
            _isProcessingARCameraImage = true;

            System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
            stopWatch.Start();

            BarracudaOutputData output = await _barracudaSample.ProcessImage(inputCameraImage);
            _outputProcessImage = output.outputTexture;

            stopWatch.Stop();
            processingTimeInMillisecons = Convert.ToInt32(stopWatch.ElapsedMilliseconds);

            if (outputImageProcessed.texture == null)
            {
                outputImageProcessed.texture = _outputProcessImage;
            }
            _isProcessingARCameraImage = false;
        }
    }
}