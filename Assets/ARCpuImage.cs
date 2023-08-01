using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Klak.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace BlocInBloc {
    /// <summary>
    /// This component tests getting the latest camera image
    /// and converting it to RGBA format. If successful,
    /// it displays the image on the screen as a RawImage
    /// and also displays information about the image.
    ///
    /// This is useful for computer vision applications where
    /// you need to access the raw pixels from camera image
    /// on the CPU.
    ///
    /// This is different from the ARCameraBackground component, which
    /// efficiently displays the camera image on the screen. If you
    /// just want to blit the camera texture to the screen, use
    /// the ARCameraBackground, or use Graphics.Blit to create
    /// a GPU-friendly RenderTexture.
    ///
    /// In this example, we get the camera image data on the CPU,
    /// convert it to an RGBA format, then display it on the screen
    /// as a RawImage texture to demonstrate it is working.
    /// This is done as an example; do not use this technique simply
    /// to render the camera image on screen.
    /// </summary>
    public class ARCpuImage : MonoBehaviour {
        [Tooltip ("The ARCameraManager which will produce frame events.")]
        public ARCameraManager cameraManager;
        public XRCpuImage.Transformation transformation = XRCpuImage.Transformation.None;
        public ImageSource imageSourcePrefab;
        public UnityEvent<Texture2D> onNewCameraData;
        public bool useImageSource = true;
        
        public bool useCustomResolution;
        [SerializeField] private Vector2Int _customResolution;
        
        private Texture2D m_cameraBarracudaTexture;
        private ImageSource _imageSource;
        private bool _isProcessing = false;
        private int _customHeight => _customResolution.y;
        private int _customWidth => _customResolution.x;

        private void OnEnable () {
            if (useImageSource) {
                _imageSource = GameObject.Instantiate<ImageSource> (imageSourcePrefab);
            }
            if (cameraManager == null) {
                cameraManager = Camera.main.gameObject.GetComponent<ARCameraManager> ();
            }
            if (cameraManager != null) { 
                cameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable () {
            if (cameraManager != null) {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        public void StartScan () {
            enabled = true;
        }
        
        public async Task StopScan () {
            enabled = false;
            await UniTask.WaitUntil (() => !_isProcessing);
        }

        public async Task<Texture2D> GetCameraImage () {
            if (useImageSource) {
                UpdateBarracudaTextureFromSourceImage ();
                return m_cameraBarracudaTexture;
            } else {
                return await TryAcquireLatestCpuImage (-1,-1, null, XRCpuImage.Transformation.MirrorX);
            }
        }

        public void SetCustomResolution (Vector2Int customSize) {
            _customResolution = customSize;
            imageSourcePrefab.SetOutputResolution (customSize);
        }
        
#if UNITY_EDITOR
        private async void FixedUpdate () {
            if (useImageSource) {
                if (_imageSource == null)  {
                    return;
                }
                UpdateBarracudaTextureFromSourceImage ();
                TrigerNewARCameraImageEvent ();
            }
        }
#endif

        private async Task UpdateBarracudaTextureFromSourceImage () {
            if (useCustomResolution) {
                if (_imageSource.OutputResolution.x != _customWidth || _imageSource.OutputResolution.y != _customHeight) {
                    Debug.LogError ("Change resolution of ImageSource prefab to match : Width:" + _customWidth + " Height: " + _customHeight);
                }
            }
            
            Texture sourceImageTexture = _imageSource.Texture;
            if (sourceImageTexture != null) {
                if (m_cameraBarracudaTexture == null) {
                    m_cameraBarracudaTexture = new Texture2D (sourceImageTexture.width, sourceImageTexture.height, TextureFormat.RGBA32, false);
                }
                Graphics.CopyTexture (sourceImageTexture, m_cameraBarracudaTexture);
            }
        }
        
        private async void OnCameraFrameReceived (ARCameraFrameEventArgs eventArgs) {
            if (_isProcessing || !enabled) {
                return;
            }
            _isProcessing = true;
            if (useImageSource) {
                await UpdateBarracudaTextureFromSourceImage ();
            } else {
                await UpdateCameraImageForBarracuda ();
            }
            TrigerNewARCameraImageEvent ();
            _isProcessing = false;
        }

        private async Task UpdateCameraImageForBarracuda () {
            int outputWidth = useCustomResolution ? _customWidth : -1;
            int outputHeight = useCustomResolution ? _customHeight : -1;
            m_cameraBarracudaTexture = await TryAcquireLatestCpuImage (outputWidth, outputHeight, m_cameraBarracudaTexture, transformation);
        }
        
        private async Task<Texture2D> TryAcquireLatestCpuImage (int outputWidth, int outputHeight, Texture2D targetTexture, XRCpuImage.Transformation imageTransformation = XRCpuImage.Transformation.MirrorY) {
            // Attempt to get the latest camera image. If this method succeeds,
            // it acquires a native resource that must be disposed (see below).
            if (!cameraManager.TryAcquireLatestCpuImage (out XRCpuImage image)) {
                return null;
            }

            // Display some information about the camera image
            /*string info = string.Format (
                "Image info:\n\twidth: {0}\n\theight: {1}\n\tplaneCount: {2}\n\ttimestamp: {3}\n\tformat: {4}",
                image.width, image.height, image.planeCount, image.timestamp, image.format);
            */

            // Once we have a valid XRCpuImage, we can access the individual image "planes"
            // (the separate channels in the image). XRCpuImage.GetPlane provides
            // low-overhead access to this data. This could then be passed to a
            // computer vision algorithm. Here, we will convert the camera image
            // to an RGBA texture and draw it on the screen.
            
            // Choose an RGBA format.
            // See XRCpuImage.FormatSupported for a complete list of supported formats.
            TextureFormat format = TextureFormat.RGBA32;

            if (outputWidth == -1) {
                outputWidth = image.width;
            }

            if (outputHeight == -1) {
                outputHeight = image.height;
            }

            // Convert the image to format
            // We can also get a sub rectangle, but we'll get the full image here.
            XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams (image, format, imageTransformation);
            conversionParams.outputDimensions = new Vector2Int (outputWidth, outputHeight);

            if (targetTexture == null || targetTexture.width != outputWidth || targetTexture.height != outputHeight) {
                if (targetTexture != null) {
                    Destroy (targetTexture);
                }
                targetTexture = new Texture2D (outputWidth, outputHeight, format, false);
            }

            try {
                XRCpuImage.AsyncConversion request = image.ConvertAsync (conversionParams);
                while (!request.status.IsDone ()) {
                    await UniTask.Yield();
                }
                if (request.status != XRCpuImage.AsyncConversionStatus.Ready) {
                    throw new InvalidOperationException($"Request failed with status {request.status}");
                }
                
                // Apply the updated texture data to our texture
                NativeArray<byte> rawTextureData = request.GetData <byte> ();
                targetTexture.LoadRawTextureData (rawTextureData);
                targetTexture.Apply ();
                
                rawTextureData.Dispose ();
                request.Dispose ();
            } finally {
                // We must dispose of the XRCpuImage after we're finished
                // with it to avoid leaking native resources.
                image.Dispose ();
            }

            return targetTexture;
        }
        
        private static void UpdateRawImage (RawImage rawImage, XRCpuImage cpuImage, XRCpuImage.Transformation transformation) {
            // Get the texture associated with the UI.RawImage that we wish to display on screen.
            Texture2D texture = rawImage.texture as Texture2D;

            // If the texture hasn't yet been created, or if its dimensions have changed, (re)create the texture.
            // Note: Although texture dimensions do not normally change frame-to-frame, they can change in response to
            //    a change in the camera resolution (for camera images) or changes to the quality of the human depth
            //    and human stencil buffers.
            if (texture == null || texture.width != cpuImage.width || texture.height != cpuImage.height) {
                texture = new Texture2D (cpuImage.width, cpuImage.height, cpuImage.format.AsTextureFormat (), false);
                rawImage.texture = texture;
            }

            // For display, we need to mirror about the vertical access.
            XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams (cpuImage, cpuImage.format.AsTextureFormat (), transformation);

            // Get the Texture2D's underlying pixel buffer.
            NativeArray<byte> rawTextureData = texture.GetRawTextureData<byte> ();

            // Make sure the destination buffer is large enough to hold the converted data (they should be the same size)
            Debug.Assert (rawTextureData.Length == cpuImage.GetConvertedDataSize (conversionParams.outputDimensions, conversionParams.outputFormat),
                "The Texture2D is not the same size as the converted data.");

            // Perform the conversion.
            cpuImage.Convert (conversionParams, rawTextureData);

            // "Apply" the new pixel data to the Texture2D.
            texture.Apply ();
        }
        
        private void TrigerNewARCameraImageEvent () {
            if (onNewCameraData != null) {
                onNewCameraData.Invoke (m_cameraBarracudaTexture);
            }
        }
    }
}